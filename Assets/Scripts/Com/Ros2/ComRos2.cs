using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// ROS2 連携（タグ ⇔ ROS2 トピック、双方向）。
///
/// 設計方針：
///  - KMX は全てタグ駆動（GlobalScript.tagDatas[database][mechId][tag] = TagInfo）。
///    本クラスは「設定で指定したタグ」を ROS2 へ publish し、ROS2 から来た値をタグへ書き戻すだけ。
///    ユニット/ロボット本体は無改造で連携できる。
///  - 通信手段は IRos2Transport に抽象化（ROS-TCP-Connector / rosbridge / native DDS を差替え可能）。
///    パッケージ未導入時は NullRos2Transport（no-op）になり、プロジェクトは常にコンパイルできる。
///    実通信を有効化するには Scripting Define に "KMX_ROS2" を追加し、ROS-TCP-Connector を導入する。
///  - 受信は別スレッドの可能性があるため一旦キューに積み、メインスレッド（Update）でタグへ反映する。
///
/// プラットフォーム：Standalone(Windows/Linux) 前提。WebGL/Android/iPhone では無効化。
/// </summary>
[DisallowMultipleComponent]
public class ComRos2 : MonoBehaviour, ITagCom
{
    #region 設定
    public enum Ros2Dir { Publish, Subscribe, Both }

    /// <summary>1タグの連携定義</summary>
    [Serializable]
    public class Ros2TagMap
    {
        /// <summary>
        /// GlobalScript.tagDatas のキー。ユニットが読む先に合わせる。
        /// 空の場合は起動時に unit 名（または tagDatas 走査）から自動解決する（可搬）。解決できなければ ComRos2 の Name。
        /// </summary>
        public string database;
        /// <summary>機番。空なら unit 名または tagDatas 走査から自動解決。</summary>
        public string mechId;
        /// <summary>
        /// ユニット名（例 "FANUC"）。database/mechId が空のとき、このユニットの実 DB/機番を実行時に解決する。
        /// ※内部の完全一致にのみ使用し ROS メッセージには載せないため、日本語名でも安全。
        /// </summary>
        public string unit;
        /// <summary>タグ名</summary>
        public string tag;
        /// <summary>ROS2 メッセージ内での名前（省略時は tag を使用）</summary>
        public string name;
        /// <summary>"Publish" / "Subscribe" / "Both"</summary>
        public string dir = "Both";
        /// <summary>浮動小数点として扱うか（true=fValue、false=Value(int)）</summary>
        public bool isFloat;

        public Ros2Dir Direction => Enum.TryParse(dir, true, out Ros2Dir d) ? d : Ros2Dir.Both;
        public string Key => string.IsNullOrEmpty(name) ? tag : name;
    }

    /// <summary>ComRos2 全体の設定（Ros2Info.json に対応）</summary>
    [Serializable]
    public class Ros2Setting
    {
        /// <summary>
        /// ROS2連携を有効にするか（既定 false）。ビルド(Standalone)では ParameterLoader がこの値で
        /// アタッチ可否を最終判定する（顧客/デモ用ビルドで誤って ROS 接続しないよう既定OFF）。
        /// Editor では Kyotoss メニューのトグル(EditorPrefs)が優先。
        /// </summary>
        public bool enabled = false;
        public string ip = "127.0.0.1";
        public int port = 10000;                 // ros_tcp_endpoint の待受ポート
        public string publishTopic = "/kmx/state";
        public string subscribeTopic = "/kmx/command";
        public int cycleMs = 50;                 // publish 周期
        public List<Ros2TagMap> tags = new();

        // ── Unity からの ROS2 起動制御（LAUNCH_CONTROL_UNITY_SPEC.md）。ComRos2Launcher が参照。──
        public string wslUser = "kyotoss";       // WSL ユーザー（制御スクリプトは /home/<user>/ros2_ws/）
        public string wslDistro = "";            // 空=既定ディストロ。指定時は wsl -d <distro>
        public bool launchUseMoveit = true;      // 起動時に MoveIt 込み(true)/補間のみ(false)

        // ── 複数ロボット対応（MULTI_ROBOT_ROS2_SPEC.md）。空なら従来どおり単一ロボットで動作。──
        public List<Ros2RobotConfig> robots = new();
    }

    /// <summary>ロボットごとの計画設定（Ros2Info.json の robots 配列の各要素）。</summary>
    [Serializable]
    public class Ros2RobotConfig
    {
        public string robotId;               // ROS2 robot_map のキーと一致させる（例 "crx30ia_1"）
        public string unit;                  // UnitSetting.name（レジストリ突合キー・内部用）
        public string mechId;                // 任意。unit 名重複時の識別
        public string[] jointNames;          // 順序付き。長さ=jointCount（未指定なら機種既定）
        public int jointCount;               // 6 以上（jointNames.Length と一致すべき）
        public string baseNameContains;      // 基準 Transform 名（GetBaseTransform が null の時のフォールバック）
        public string flangeNameContains;    // 6軸フランジ名（ヘッド attach）
        public string attachLinkName;        // URDF attach リンク名（例 "flange" / "tool0"）
        public Vector3 baseCalibrationEuler; // 機種別の Unity基準→URDF base_link 補正
    }
    #endregion 設定

    [SerializeField] private Ros2Setting setting = new();
    [SerializeField] private string configFileName = "Ros2Info.json";
    [SerializeField] private bool loadConfigFromFile = true;

    /// <summary>通信手段（差替え可能）</summary>
    private IRos2Transport transport;

    /// <summary>受信キュー（別スレッド → メインスレッドで適用）</summary>
    private readonly ConcurrentQueue<KeyValuePair<string, double>> inbox = new();

    /// <summary>受信名 → 書込先マップ（Subscribe/Both）</summary>
    private readonly Dictionary<string, Ros2TagMap> subByName = new();
    /// <summary>発行対象（Publish/Both）</summary>
    private readonly List<Ros2TagMap> pubList = new();

    private float pubTimer;
    private float sinceStart;   // 起動からの経過（発行トピック登録がendpointに伝わるまでの猶予に使用）
    private bool started;
    private bool destroyed;         // 破棄後に購読コールバックが残留しても処理しないためのガード
    private bool targetsResolved;   // database/mechId をユニット名等から解決済みか（ロード完了後に1回）
    private ComRos2Launcher launcher;   // ROS2 起動監視。running_full 到達で接続を張り直す。

    /// <summary>ITagCom：接続先名（tagDatas のキーにも使用可）</summary>
    public string Name => "ROS2:" + setting.ip + ":" + setting.port;

    /// <summary>Ros2Info.json の robots 設定（複数ロボット・レジストリ/計画が参照）。空なら単一ロボット。</summary>
    public IReadOnlyList<Ros2RobotConfig> RobotConfigs => setting.robots;

    #region ライフサイクル
    private void Start()
    {
#if (UNITY_WEBGL || UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        // WebGL は TCP/DDS 不可（将来 rosbridge トランスポートで対応）。Android/iPhone も実通信対象外。
        enabled = false;
        return;
#else
        if (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer)
        {
            enabled = false;
            return;
        }

        if (loadConfigFromFile)
        {
            LoadConfig();
        }
        BuildMaps();

        transport = Ros2TransportFactory.Create();
        transport.Subscribe(setting.subscribeTopic, OnRosMessage);
        transport.Connect(setting.ip, setting.port);
        if (pubList.Count > 0)
        {
            transport.RegisterPublisher(setting.publishTopic);  // 発行前に登録（初回publishのレース回避）
        }
        started = true;
        // ROS2 を後から起動した場合（Unityロード時は endpoint 不在で接続失敗）、running_full 到達を
        // 検知して接続を張り直す。ランチャは同一 GameObject（ParameterLoader が先に付与済み）。
        launcher = GetComponent<ComRos2Launcher>();
        if (launcher != null)
        {
            launcher.StateChanged += OnLaunchStateChanged;
        }
        Debug.Log($"[ComRos2] start ({Name}) pub='{setting.publishTopic}' sub='{setting.subscribeTopic}' tags={setting.tags.Count} transport={transport.GetType().Name}");
#endif
    }

    private void OnDestroy()
    {
        destroyed = true;
        if (launcher != null)
        {
            launcher.StateChanged -= OnLaunchStateChanged;
        }
        // Disconnect で購読解除（ROSConnection は常駐シングルトンのため、解除しないと
        // 破棄済みインスタンスのコールバックが残留し inbox に溜まり続ける＝リロード毎にリーク）。
        try { transport?.Disconnect(); } catch { /* ignore */ }
    }

    /// <summary>ランチャの状態変化。running_full（endpoint 起動済）を検知したら接続を張り直す。</summary>
    private void OnLaunchStateChanged(ComRos2Launcher.LaunchState s)
    {
        if (s == ComRos2Launcher.LaunchState.RunningFull && !transport.IsLinkUp)
        {
            Debug.Log("[ComRos2] running_full 検知 → ROS-TCP 再接続");
            Reconnect();
        }
    }

    /// <summary>ROS-TCP 接続を張り直す（ROS2 を後から起動/再起動した後に呼ぶ）。購読は常駐 ROSConnection が保持。</summary>
    public void Reconnect()
    {
        if (transport == null)
        {
            return;
        }
        try
        {
            transport.Connect(setting.ip, setting.port);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ComRos2] reconnect failed: {e.Message}");
        }
    }

    private void Update()
    {
        if (!started)
        {
            return;
        }

        // ロード完了まで待つ（unitSetting.Database の充填が SetDatabaseSetting より後段のため）。
        // 完了後に database/mechId をユニット名（または tagDatas 走査）から1回だけ解決する。
        if (!targetsResolved)
        {
            if (!GlobalScript.isLoaded)
            {
                return;
            }
            if (setting.tags.Count == 0)
            {
                AutoBuildTagsFromRobots();   // Ros2Info に tags 未記載なら robotSetting から自動生成
                BuildMaps();                 // 生成分を subByName/pubList へ反映
            }
            ResolveTargets();
            targetsResolved = true;
        }

        // 1) 受信適用（メインスレッド）
        while (inbox.TryDequeue(out var kv))
        {
            if (subByName.TryGetValue(kv.Key, out var map))
            {
                WriteTag(map, kv.Value);
            }
        }

        // 2) 発行（周期）。登録がendpointに伝わるまで少し待ってから発行開始（"Not registered" 回避）
        sinceStart += Time.deltaTime;
        pubTimer += Time.deltaTime;
        if (sinceStart >= 0.5f && pubTimer * 1000f >= setting.cycleMs)
        {
            pubTimer = 0f;
            PublishTags();
        }
    }
    #endregion ライフサイクル

    #region 発行 / 受信
    /// <summary>タグ → ROS2</summary>
    private void PublishTags()
    {
        if (transport == null || !transport.IsConnected || pubList.Count == 0)
        {
            return;
        }
        if (!GlobalScript.isLoaded || GlobalScript.isSystemRecorder)
        {
            return;
        }

        var names = new string[pubList.Count];
        var values = new double[pubList.Count];
        for (int i = 0; i < pubList.Count; i++)
        {
            names[i] = pubList[i].Key;
            values[i] = ReadTag(pubList[i]);
        }
        transport.Publish(setting.publishTopic, names, values);
    }

    /// <summary>受信キューの上限。ロード完了前は Update がドレインしないため、無制限蓄積を防ぐ。</summary>
    private const int InboxMax = 4096;

    /// <summary>ROS2 → キュー（別スレッドの可能性があるためここではキューイングのみ）</summary>
    private void OnRosMessage(string[] names, double[] values)
    {
        if (destroyed || names == null || values == null)
        {
            return;   // 破棄済み（購読解除前にコールバックが来ても死んだ inbox に溜めない）
        }
        // ロード完了（targetsResolved）まで Update はドレインしない。その間に受信が続くと
        // inbox が無制限に膨らむため、上限超過ぶんは古いものから捨てる（最新のコマンドを優先）。
        while (inbox.Count >= InboxMax)
        {
            inbox.TryDequeue(out _);
        }
        int n = Math.Min(names.Length, values.Length);
        for (int i = 0; i < n; i++)
        {
            inbox.Enqueue(new KeyValuePair<string, double>(names[i], values[i]));
        }
    }
    #endregion 発行 / 受信

    #region タグ読み書き
    private double ReadTag(Ros2TagMap m)
    {
        var db = string.IsNullOrEmpty(m.database) ? Name : m.database;
        if (GlobalScript.tagDatas.TryGetValue(db, out var mechs)
            && mechs.TryGetValue(m.mechId, out var tags)
            && tags.TryGetValue(m.tag, out var info) && info != null)
        {
            info.wasRead = true;
            // タグ自身の isFloat を正とする（GetTagValueF と同じ判定）。
            // WriteTag がマップ宣言型を info.isFloat に反映するので読み書きが一貫する。
            return info.isFloat ? info.fValue : info.Value;
        }
        return 0d;
    }

    /// <summary>タグへ書き込む（メインスレッドから呼ぶこと。TagInfo は ScriptableObject のため）。</summary>
    private void WriteTag(Ros2TagMap m, double value)
    {
        var db = string.IsNullOrEmpty(m.database) ? Name : m.database;
        var root = GlobalScript.tagDatas;
        if (!root.ContainsKey(db))
        {
            root[db] = new Dictionary<string, Dictionary<string, TagInfo>>();
        }
        if (!root[db].ContainsKey(m.mechId))
        {
            root[db][m.mechId] = new Dictionary<string, TagInfo>();
        }
        var tags = root[db][m.mechId];
        if (!tags.ContainsKey(m.tag) || tags[m.tag] == null)
        {
            var t = ScriptableObject.CreateInstance<TagInfo>();
            t.name = m.tag;
            t.Database = db;
            t.MechId = m.mechId;
            t.Tag = m.tag;
            tags[m.tag] = t;
        }
        var info = tags[m.tag];
        // ROS マップが宣言した型(isFloat)をタグに一貫適用する。
        // 旧実装は m.isFloat=true のときだけ info.isFloat=true にする「一方向」だったため、
        //   ・float 宣言タグ → 既存 int タグの解釈を反転（他サブシステムが誤読）
        //   ・int 宣言タグ  → float だった info.isFloat が残り、書いた Value を読まず古い fValue を読む
        // という非対称バグがあった。両方向で isFloat を確定し、対応するフィールドへ書く。
        // 例: d_robo_a はローダーが型未設定(isFloat=false 既定)だが、Ros2Info.json で isFloat=true と
        //     宣言することで float として扱われ、度の小数が保持される（GetTagValueF は info.isFloat で読む）。
        info.isFloat = m.isFloat;
        if (m.isFloat)
        {
            info.fValue = (float)value;
        }
        else
        {
            info.Value = (int)Math.Round(value);
        }
    }

    /// <summary>解決とマッピングが完了して書き込み可能か。</summary>
    public bool IsReady => started && targetsResolved;

    /// <summary>
    /// 経路再生用の書き込み口。ROS 名（例 "J1"）を、解決済みの database/mechId/tag へ書く。
    /// ComRos2 の Subscribe/Both マッピング（subByName）と WriteTag をそのまま再利用するので、
    /// 単位・可搬性（unit名解決）・タグ生成の扱いが通常受信と完全に一致する。ComRos2PathPlanner から使用。
    /// </summary>
    public bool ApplyValue(string rosName, double value)
    {
        if (!IsReady || string.IsNullOrEmpty(rosName))
        {
            return false;
        }
        if (subByName.TryGetValue(rosName, out var m))
        {
            WriteTag(m, value);
            return true;
        }
        return false;
    }

    /// <summary>経路生成の始点取得用: ROS 名（例 "J1"）の現在値を解決済みマッピングで読む。</summary>
    public bool TryReadValue(string rosName, out double value)
    {
        value = 0d;
        if (!IsReady || string.IsNullOrEmpty(rosName))
        {
            return false;
        }
        if (subByName.TryGetValue(rosName, out var m))
        {
            value = ReadTag(m);
            return true;
        }
        return false;
    }
    #endregion タグ読み書き

    #region 準備
    /// <summary>
    /// Ros2Info.json に tags を書いていない場合、シーンの 6軸ロボ(IRos2PlanTarget)の
    /// robotSetting.tags から /kmx state・command 用のタグ対応を自動生成する
    /// （例 FANUC: d_robo_a1..a6 → J1..J6）。手書きの tags があれば優先（本メソッドは呼ばれない）。
    /// </summary>
    private void AutoBuildTagsFromRobots()
    {
        var units = GlobalScript.unitSettings;
        if (units == null)
        {
            return;
        }
        int added = 0, robotsHit = 0;
        foreach (var u in units)
        {
            if (u == null || u.robotSetting == null || u.robotSetting.tags == null || u.moveObject == null)
            {
                continue;
            }
            var target = u.moveObject.GetComponent<IRos2PlanTarget>();
            if (target == null || target.JointCount < 6)
            {
                continue;   // 6 軸以上の計画対象ロボのみ
            }
            var jtags = u.robotSetting.tags;
            var names = target.JointNames;
            int n = target.JointCount;
            if (jtags.Count < n)
            {
                continue;   // 関節数ぶんのタグが無い（例: 3 軸位置ロボは対象外）
            }
            // 先頭 n 個が全て非空でなければスキップ（部分的タグは誤対応の元）。
            bool allSet = true;
            for (int i = 0; i < n; i++)
            {
                if (string.IsNullOrEmpty(jtags[i]))
                {
                    allSet = false;
                    break;
                }
            }
            if (!allSet)
            {
                continue;
            }
            for (int i = 0; i < n; i++)
            {
                setting.tags.Add(new Ros2TagMap
                {
                    unit = u.name,
                    tag = jtags[i],
                    name = (names != null && i < names.Length) ? names[i] : $"J{i + 1}",
                    dir = "Both",
                    isFloat = true,
                });
            }
            added += n;
            robotsHit++;
        }
        if (added > 0)
        {
            Debug.Log($"[ComRos2] Ros2Info に tags 未記載 → robotSetting から自動生成: {robotsHit}台 / {added}タグ");
            if (robotsHit > 1)
            {
                Debug.LogWarning("[ComRos2] 6軸ロボが複数あります。/kmx/state の関節名(J1..Jn)が衝突するため、"
                    + "複数ロボの直接駆動には per-robot トピック/接頭辞が必要です（経路計画は robot_id で分離）。");
            }
        }
    }

    private void BuildMaps()
    {
        subByName.Clear();
        pubList.Clear();
        foreach (var m in setting.tags)
        {
            if (m == null || string.IsNullOrEmpty(m.tag))
            {
                continue;
            }
            if (m.Direction != Ros2Dir.Publish)
            {
                subByName[m.Key] = m;   // Subscribe or Both
            }
            if (m.Direction != Ros2Dir.Subscribe)
            {
                pubList.Add(m);         // Publish or Both
            }
        }
    }

    /// <summary>
    /// 各タグの database/mechId を確定する（ロード完了後に1回だけ呼ぶ）。
    /// 優先順位：①明示指定 → ②unit 名から解決 → ③tagDatas 走査（tag名・任意でmechId一致）。
    /// いずれも決まらなければ空のまま（ReadTag/WriteTag が ComRos2 の Name サブツリーを使う＝ユニットには反映されない）。
    /// </summary>
    private void ResolveTargets()
    {
        foreach (var m in setting.tags)
        {
            if (m == null || string.IsNullOrEmpty(m.tag))
            {
                continue;
            }
            if (!string.IsNullOrEmpty(m.database) && !string.IsNullOrEmpty(m.mechId))
            {
                continue;   // 明示指定済み
            }

            // ② unit 名から解決（日本語名OK＝内部の完全一致のみ）
            if (!string.IsNullOrEmpty(m.unit)
                && GlobalScript.TryResolveUnitDb(m.unit, out var udb, out var umech))
            {
                if (string.IsNullOrEmpty(m.database))
                {
                    m.database = udb;
                }
                if (string.IsNullOrEmpty(m.mechId))
                {
                    m.mechId = umech;
                }
            }

            // ③ まだ database が決まらなければ tagDatas を走査（mechId 指定があれば一致優先）。
            //    Dictionary の列挙順は不定なので、同名タグが複数 DB/機番に在っても結果が決定的に
            //    なるよう、キーを序数ソートしてから走査する（非決定的な解決先を避ける）。
            if (string.IsNullOrEmpty(m.database))
            {
                foreach (var dbKey in GlobalScript.tagDatas.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    var mechs = GlobalScript.tagDatas[dbKey];
                    foreach (var mechKey in mechs.Keys.OrderBy(k => k, StringComparer.Ordinal))
                    {
                        if (!string.IsNullOrEmpty(m.mechId) && mechKey != m.mechId)
                        {
                            continue;
                        }
                        if (mechs[mechKey].ContainsKey(m.tag))
                        {
                            m.database = dbKey;
                            if (string.IsNullOrEmpty(m.mechId))
                            {
                                m.mechId = mechKey;
                            }
                            break;
                        }
                    }
                    if (!string.IsNullOrEmpty(m.database))
                    {
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(m.database))
            {
                Debug.LogWarning($"[ComRos2] タグ '{m.tag}' の database を解決できません（unit='{m.unit}' mechId='{m.mechId}'）。ユニットには反映されません。");
            }
            else
            {
                Debug.Log($"[ComRos2] resolve tag='{m.tag}' → database='{m.database}' mechId='{m.mechId}' (unit='{m.unit}')");
            }
        }
    }

    private void LoadConfig()
    {
        try
        {
            var path = Path.Combine(Application.streamingAssetsPath, "Datas", configFileName);
            if (File.Exists(path))
            {
                // 日本語ユニット名対策で UTF-8 明示読み込み。
                var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
                var loaded = JsonUtility.FromJson<Ros2Setting>(json);
                if (loaded != null)
                {
                    setting = loaded;
                }
            }
            else
            {
                Debug.LogWarning($"[ComRos2] 設定ファイルが見つかりません: {path}（既定値を使用）");
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[ComRos2] 設定読込失敗: " + e);
        }
    }
    #endregion 準備

    #region ITagCom
    // 現状は poll 型（Update で tagDatas を直接読み書き）なので、push 経路は未使用。
    // 将来 GlobalScript の SetDatas ディスパッチに載せたい場合はここで送信タグを受ける。
    public void SetDatas(List<TagInfo> tags) { }
    public void RenewData() { }
    #endregion ITagCom
}

/// <summary>
/// 経路生成の結果（関節トラジェクトリ）。ROS 型に依存しないプレーンな受け渡し用。
/// jointNames[j] の角度時系列を positions[point][j]（度）、各点の時刻を timesSec[point]（秒）で保持。
/// </summary>
public class Ros2Trajectory
{
    public string[] jointNames;   // 例 ["J1".."J6"]
    public double[] timesSec;     // 長さ = 点数
    public double[][] positions;  // [点数][関節数]（度）
}

/// <summary>
/// 障害物プリミティブ1個（ROS 型に依存しないプレーンな受け渡し用）。姿勢は Unity 座標・ロボット基部相対。
/// トランスポート実装側で ROS 系(FLU)・メートルへ変換して kmx_msgs/Obstacles にする。
/// </summary>
public class Ros2Obstacle
{
    public string id;
    public int type;              // 1=BOX, 2=SPHERE, 3=CYLINDER（shape_msgs/SolidPrimitive 準拠）
    public float[] dimensions;    // メートル。BOX:[x,y,z](ROS軸順) / SPHERE:[r] / CYLINDER:[h,r]
    public Vector3 position;      // Unity・ロボット基部相対
    public Quaternion rotation;   // Unity・ロボット基部相対
}

/// <summary>
/// ROS2 トランスポート抽象。名前配列＋値配列の「数値バス」を publish/subscribe する。
/// 実装を差し替えることで ROS-TCP-Connector / rosbridge(WebSocket) / native DDS を選択できる。
/// </summary>
public interface IRos2Transport
{
    bool IsConnected { get; }
    /// <summary>実際に ROS(endpoint) と接続が確立できているか（通信状態表示用）。</summary>
    bool IsLinkUp { get; }
    void Connect(string ip, int port);
    void Disconnect();
    void RegisterPublisher(string topic);
    void Publish(string topic, string[] names, double[] values);
    void Subscribe(string topic, Action<string[], double[]> onMessage);

    // --- 経路生成（Unity→ROS2 でプラン要求、ROS2→Unity で軌道受信）---
    /// <summary>plan要求 publisher を事前登録する（初回publishの "Not registered" レース回避）。</summary>
    void RegisterPlanRequestPublisher(string topic);
    /// <summary>
    /// 始点/終点(度)を kmx_msgs/PlanRequest で発行する。
    /// timeBudget(秒)/goodRatio は任意で計画の粘り具合を要求ごとに指定（0以下=ROS2ノード既定）。
    /// optimize=true で登録軌道の多目的最適化を要求（targetTimeSec=目標所要秒・0=成り行き、payload=段階2トルク用）。
    /// </summary>
    void PublishPlanRequest(string topic, string[] names, double[] startDeg, double[] goalDeg,
                            double timeBudget = 0.0, double goodRatio = 0.0, string robotId = "",
                            bool optimize = false, double targetTimeSec = 0.0,
                            double payloadMass = 0.0, double[] payloadCom = null);
    /// <summary>trajectory_msgs/JointTrajectory を購読し Ros2Trajectory(度) に変換して渡す。</summary>
    void SubscribeTrajectory(string topic, Action<Ros2Trajectory> onTrajectory);
    /// <summary>計画ステータス(std_msgs/String, 例 "planning"/"succeeded:.."/"failed:..")を購読する。</summary>
    void SubscribePlanStatus(string topic, Action<string> onStatus);
    /// <summary>探索中断を通知する（std_msgs/String）。登録の長時間探索を止めて現在の最良で確定させる。</summary>
    void PublishPlanCancel(string topic);

    // --- 障害物（Unity→ROS2 で planning scene へ）---
    /// <summary>障害物 publisher を事前登録する（初回publishの "Not registered" レース回避）。</summary>
    void RegisterObstaclesPublisher(string topic);
    /// <summary>障害物群を kmx_msgs/Obstacles で発行する（姿勢は ROS系・メートルへ変換して送る）。</summary>
    void PublishObstacles(string topic, string frameId, List<Ros2Obstacle> obstacles);
}

/// <summary>
/// 既定（パッケージ未導入時）のトランスポート。何もしない。
/// これにより KMX_ROS2 未定義でもプロジェクトは常にコンパイル・実行できる。
/// </summary>
public sealed class NullRos2Transport : IRos2Transport
{
    public bool IsConnected => false;
    public bool IsLinkUp => false;
    public void Connect(string ip, int port)
        => Debug.LogWarning("[ComRos2] ROS2 トランスポート未有効。Scripting Define に 'KMX_ROS2' を追加し ROS-TCP-Connector を導入すると有効化されます。");
    public void Disconnect() { }
    public void RegisterPublisher(string topic) { }
    public void Publish(string topic, string[] names, double[] values) { }
    public void Subscribe(string topic, Action<string[], double[]> onMessage) { }
    public void RegisterPlanRequestPublisher(string topic) { }
    public void PublishPlanRequest(string topic, string[] names, double[] startDeg, double[] goalDeg,
                                   double timeBudget = 0.0, double goodRatio = 0.0, string robotId = "",
                                   bool optimize = false, double targetTimeSec = 0.0,
                                   double payloadMass = 0.0, double[] payloadCom = null) { }
    public void SubscribeTrajectory(string topic, Action<Ros2Trajectory> onTrajectory) { }
    public void SubscribePlanStatus(string topic, Action<string> onStatus) { }
    public void PublishPlanCancel(string topic) { }
    public void RegisterObstaclesPublisher(string topic) { }
    public void PublishObstacles(string topic, string frameId, List<Ros2Obstacle> obstacles) { }
}

/// <summary>使用するトランスポートを選ぶファクトリ。差替えはここ一箇所。</summary>
public static class Ros2TransportFactory
{
    public static IRos2Transport Create()
    {
#if KMX_ROS2
        return new RosTcpConnectorTransport();
#else
        return new NullRos2Transport();
#endif
    }
}
