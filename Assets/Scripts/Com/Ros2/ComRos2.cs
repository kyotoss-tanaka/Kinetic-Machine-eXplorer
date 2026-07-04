using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
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
        public string ip = "127.0.0.1";
        public int port = 10000;                 // ros_tcp_endpoint の待受ポート
        public string publishTopic = "/kmx/state";
        public string subscribeTopic = "/kmx/command";
        public int cycleMs = 50;                 // publish 周期
        public List<Ros2TagMap> tags = new();
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
    private bool targetsResolved;   // database/mechId をユニット名等から解決済みか（ロード完了後に1回）

    /// <summary>ITagCom：接続先名（tagDatas のキーにも使用可）</summary>
    public string Name => "ROS2:" + setting.ip + ":" + setting.port;

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
        Debug.Log($"[ComRos2] start ({Name}) pub='{setting.publishTopic}' sub='{setting.subscribeTopic}' tags={setting.tags.Count} transport={transport.GetType().Name}");
#endif
    }

    private void OnDestroy()
    {
        try { transport?.Disconnect(); } catch { /* ignore */ }
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

    /// <summary>ROS2 → キュー（別スレッドの可能性があるためここではキューイングのみ）</summary>
    private void OnRosMessage(string[] names, double[] values)
    {
        if (names == null || values == null)
        {
            return;
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
            return m.isFloat ? info.fValue : info.Value;
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
        if (m.isFloat)
        {
            info.fValue = (float)value;
            info.isFloat = true;
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

            // ③ まだ database が決まらなければ tagDatas を走査（mechId 指定があれば一致優先）
            if (string.IsNullOrEmpty(m.database))
            {
                foreach (var kvDb in GlobalScript.tagDatas)
                {
                    foreach (var kvMech in kvDb.Value)
                    {
                        if (!string.IsNullOrEmpty(m.mechId) && kvMech.Key != m.mechId)
                        {
                            continue;
                        }
                        if (kvMech.Value.ContainsKey(m.tag))
                        {
                            m.database = kvDb.Key;
                            if (string.IsNullOrEmpty(m.mechId))
                            {
                                m.mechId = kvMech.Key;
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
    void Connect(string ip, int port);
    void Disconnect();
    void RegisterPublisher(string topic);
    void Publish(string topic, string[] names, double[] values);
    void Subscribe(string topic, Action<string[], double[]> onMessage);

    // --- 経路生成（Unity→ROS2 でプラン要求、ROS2→Unity で軌道受信）---
    /// <summary>plan要求 publisher を事前登録する（初回publishの "Not registered" レース回避）。</summary>
    void RegisterPlanRequestPublisher(string topic);
    /// <summary>始点/終点(度)を kmx_msgs/PlanRequest で発行する。</summary>
    void PublishPlanRequest(string topic, string[] names, double[] startDeg, double[] goalDeg);
    /// <summary>trajectory_msgs/JointTrajectory を購読し Ros2Trajectory(度) に変換して渡す。</summary>
    void SubscribeTrajectory(string topic, Action<Ros2Trajectory> onTrajectory);

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
    public void Connect(string ip, int port)
        => Debug.LogWarning("[ComRos2] ROS2 トランスポート未有効。Scripting Define に 'KMX_ROS2' を追加し ROS-TCP-Connector を導入すると有効化されます。");
    public void Disconnect() { }
    public void RegisterPublisher(string topic) { }
    public void Publish(string topic, string[] names, double[] values) { }
    public void Subscribe(string topic, Action<string[], double[]> onMessage) { }
    public void RegisterPlanRequestPublisher(string topic) { }
    public void PublishPlanRequest(string topic, string[] names, double[] startDeg, double[] goalDeg) { }
    public void SubscribeTrajectory(string topic, Action<Ros2Trajectory> onTrajectory) { }
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
