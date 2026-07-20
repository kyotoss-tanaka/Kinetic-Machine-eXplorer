using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ロボット周辺のオブジェクトを収集し、障害物として ROS2(MoveIt planning scene) へ送る。
///
/// 方針（ユーザー選択）：
///  - 形状 = 既存 Collider を primitive 化（Box→BOX / Sphere→SPHERE / Capsule→CYLINDER / それ以外→AABB box）。
///  - 対象 = ロボット基部から半径 radius 内の Collider を自動収集（layerMask で絞り込み可）。
///  - 更新 = 静的（トリガで1回送信）。ContextMenu「Send Obstacles」または SendObstacles() を呼ぶ。
///
/// 姿勢は「ロボット基部(base_link)相対」で送る（世界原点ズレ回避）。Unity→ROS(FLU)・メートル変換は
/// トランスポート側(ROSGeometry)で行う。ROS2 側ノードが受信して CollisionObject 化し planning scene へ。
///
/// ⚠ キャリブレーション: Unity 基部の向き/スケールと MoveIt base_link が食い違う場合、
///    unitScale・基部Transform・（必要なら）box dimensions/pose の対応を調整すること。まず1個で位置確認を推奨。
///
/// プラットフォーム：Standalone のみ。
/// </summary>
[DisallowMultipleComponent]
public class ComRos2Obstacles : MonoBehaviour
{
    [SerializeField] private string topic = "/kmx/obstacles";
    [SerializeField] private string frameId = "base_link";
    [Tooltip("未割当なら実行時に robotBaseNameContains で自動探索")]
    [SerializeField] private Transform robotBase;
    [SerializeField] private string robotBaseNameContains = "CRX-30IA";
    [Tooltip("ロボット基部からの収集半径（Unity単位）")]
    [SerializeField] private float radius = 3.0f;
    [Tooltip("収集対象レイヤー。既定は全レイヤー(~0)だが、床/機械フレームを拾うと巨大障害物になるため実運用ではレイヤーで絞ること")]
    [SerializeField] private LayerMask layerMask = ~0;
    [Tooltip("Unity単位→メートル。KMXのスケールに合わせる")]
    [SerializeField] private float unitScale = 1.0f;
    /// <summary>Unity単位→メートル係数（先端加速度のG換算などに共用）。</summary>
    public float UnitScale => unitScale;
    [Tooltip("このサイズ(Unity単位)を超えるAABBは床/機械フレームとみなし障害物にしない（基部包含→START_STATE_IN_COLLISION 回避）。0以下で無効")]
    [SerializeField] private float maxObstacleSize = 2.0f;
    [Tooltip("明示的に障害物として送るオブジェクト名（半径外/巨大サイズの除外を無視）。"
        + "基部を内包するものは計画不能回避のため安全に除外。完全一致優先→部分一致")]
    [SerializeField] private string[] extraObstacleNames = new string[0];

    [Header("地面(ground plane) — 床の高さに可動範囲サイズの薄板を1枚")]
    [Tooltip("基部の真下・床の高さに、可動範囲サイズの薄い板を地面として送る（巨大な実床は送らない）")]
    [SerializeField] private bool sendGroundPlane = true;
    [Tooltip("床の高さ取得用オブジェクト名（この Collider 上面を地面高さにする）。見つからなければ基部直下")]
    [SerializeField] private string groundNameContains = "Floor";
    [Tooltip("地面板の一辺の大きさ(Unity単位)。ロボット可動範囲を覆う程度で十分")]
    [SerializeField] private float groundPlaneSize = 4.0f;
    [Tooltip("地面板の厚み(Unity単位)")]
    [SerializeField] private float groundPlaneThickness = 0.1f;
    [Tooltip("MeshCollider等は AABB box にして送る")]
    [SerializeField] private bool includeNonPrimitiveAsBox = true;
    [Tooltip("ロード完了後に1回だけ自動送信する")]
    [SerializeField] private bool autoSendOnLoad = false;
    [Tooltip("座標キャリブレーション用。基部/各障害物の座標をログ出力する（既定OFF。ズレ調査時にON）")]
    [SerializeField] private bool debugPose = false;
    [Tooltip("基部フレーム→URDF base_link の補正回転(度・基部ローカル)。CRX-30iA の Unity 基部は世界軸(Y-up)なので、"
        + "水平面をヨー-90°して base_link(X=前,Y=左,Z=上)へ合わせる。向きが違う構成では調整")]
    [SerializeField] private Vector3 baseCalibrationEuler = new Vector3(0f, -90f, 0f);

    [Tooltip("DCS keep-out を障害物として送る際の安全マージン(m・各面)。計画経路が DCS 境界をかすめて実機DCSが作動するのを"
        + "防ぐため、keep-out箱をこの分だけ各方向に広げて送る（プランナが余裕を持って回避）。0=余裕なし(境界ギリギリ)。")]
    [SerializeField] private float dcsObstacleMargin = 0.02f;

    [Header("ヘッド(ツール) → MoveIt へ attach（方式B）")]
    [Tooltip("ヘッド(ツール)を AttachedCollisionObject としてフランジに attach 送信する")]
    [SerializeField] private bool sendHead = false;
    [Tooltip("attach 用トピック（障害物と同じ ObstaclesMsg を流用。frame_id=attachLinkName）")]
    [SerializeField] private string attachedTopic = "/kmx/attached";
    [Tooltip("attach 先の URDF リンク名（例 flange / tool0 / link_6）。SRDF で確認")]
    [SerializeField] private string attachLinkName = "flange";
    // ★ヘッドの座標補正は ROS2 側 `head_calibration_rpy`(ros2 param・ライブ調整可) に一本化。
    //   Unity は「生(raw)のフランジ相対」で送る（二重補正防止）。認識合わせ: HANDOFF.md §4.1。
    [Tooltip("6軸目フランジのオブジェクト名（部分/完全一致・大小無視）。未指定なら HeadObject の親を使用")]
    [SerializeField] private string flangeNameContains = "J6FLANGE";
    [Tooltip("ヘッド(ツール)ルート。未割当なら Kinematics6D.HeadObject を使用")]
    [SerializeField] private Transform headRoot;
    // 計画対象ロボが SetTarget で確定したか。確定後は ResolveHead で他機体のヘッドを拾わない（ヘッド無し機体の誤爆防止）。
    private bool targetResolved;
    [Tooltip("ヘッドを全Collider合成の1個のAABBで送る（true=1箱・開口なし）。false=下のグリッドで数箱に間引く")]
    [SerializeField] private bool headAsSingleBox = false;
    /// <summary>ヘッドを1箱で送るか（true=1箱/false=グリッド間引き）。UIから切替可。</summary>
    public bool HeadAsSingleBox { get => headAsSingleBox; set => headAsSingleBox = value; }
    [Tooltip("ヘッド間引きのグリッド分割数(フランジ相対 X,Y,Z)。各非空セルを1箱に統合。積が ROS2 の統合閾値(12)以下推奨。"
        + "開口(コライダーの無いセル)は空のまま残る＝把持開口を保てる")]
    [SerializeField] private Vector3Int headGrid = new Vector3Int(2, 2, 3);

    private IRos2Transport transport;
    private bool started;
    private bool destroyed;
    private bool sentOnce;
    private float sinceLastAutoTry;   // autoSend: 基部未解決時の再試行スロットル用
    private int autoTries;            // autoSend: 試行回数（上限で打ち切り、全シーン走査の無限化を防ぐ）
    private const int AutoTryMax = 20;
    private Ros2PlanTargetRegistry registry;   // 他ロボを障害物として送る＆選択ロボ解決に使う

    private void Start()
    {
#if (UNITY_WEBGL || UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        enabled = false;
        return;
#else
        if (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer)
        {
            enabled = false;
            return;
        }
        transport = Ros2TransportFactory.Create();
        // publisher 事前登録（初回送信で "Not registered" レース回避）。
        // ヘッド(方式B)も同じ ObstaclesMsg を別トピックに流すので、TestPlan 等から SendHead を
        // 呼べるよう sendHead に関わらず両トピックを登録しておく（登録はトピック単位で冪等）。
        transport.RegisterObstaclesPublisher(topic);
        transport.RegisterObstaclesPublisher(attachedTopic);
        registry = GetComponent<Ros2PlanTargetRegistry>();   // 他ロボ障害物/選択ロボ解決（無くても既定動作）
        started = true;
        Debug.Log($"[ComRos2Obstacles] start topic='{topic}' frame='{frameId}' radius={radius} transport={transport.GetType().Name}");
#endif
    }

    private void OnDestroy()
    {
        destroyed = true;
    }

    private void Update()
    {
        if (!started || destroyed)
        {
            return;
        }
        if (autoSendOnLoad && !sentOnce && GlobalScript.isLoaded)
        {
            // 送信成功したときだけ確定する（基部未解決なら次回リトライ）。ただし全シーン走査を
            // 毎フレーム回さないよう間隔を空け、上限回数で打ち切る（見つからない設定でのspam防止）。
            sinceLastAutoTry += Time.deltaTime;
            if (sinceLastAutoTry >= 0.5f)
            {
                sinceLastAutoTry = 0f;
                autoTries++;
                if (SendObstacles())
                {
                    // ヘッドが縮退(pose 未確定)で送れないうちは確定しない＝次回リトライ（transform 確定を待つ）。
                    // 上限回数に達したら諦めて確定（送れた範囲で継続）。
                    bool headOk = !sendHead || SendHead();
                    if (headOk || autoTries >= AutoTryMax)
                    {
                        sentOnce = true;
                    }
                }
                else if (autoTries >= AutoTryMax)
                {
                    sentOnce = true;   // 打ち切り
                    Debug.LogWarning($"[ComRos2Obstacles] autoSend 打ち切り（基部未解決 {autoTries}回）。"
                        + "robotBase を Inspector で割当てるか robotBaseNameContains を確認してください。");
                }
            }
        }
    }

    /// <summary>計画対象ロボットに合わせて基準/ヘッド/補正を切替える（パネルの選択から呼ぶ）。</summary>
    public void SetTarget(Ros2PlanTargetRegistry.RegisteredRobot r)
    {
        if (r == null || r.Target == null)
        {
            return;
        }
        var b = r.Target.GetBaseTransform();
        if (b != null)
        {
            robotBase = b;   // ResolveBase はこれを優先して返す
        }
        var cfg = r.Config;
        if (cfg != null)
        {
            if (!string.IsNullOrEmpty(cfg.baseNameContains)) { robotBaseNameContains = cfg.baseNameContains; }
            if (!string.IsNullOrEmpty(cfg.flangeNameContains)) { flangeNameContains = cfg.flangeNameContains; }
            if (!string.IsNullOrEmpty(cfg.attachLinkName)) { attachLinkName = cfg.attachLinkName; }
            baseCalibrationEuler = cfg.baseCalibrationEuler;   // 機種別の base 補正
        }
        var head = r.Target.GetHeadObject();
        headRoot = head != null ? head.transform : null;
        targetResolved = true;   // 対象確定＝ヘッド有無もこの機体で確定（ResolveHead のフォールバック禁止）
        Debug.Log($"[ComRos2Obstacles] target='{r.DisplayName}' base='{(robotBase != null ? robotBase.name : "?")}'"
            + $" flange~'{flangeNameContains}' attach='{attachLinkName}' calib={baseCalibrationEuler} head='{(head != null ? head.name : "なし")}'");
    }

    /// <summary>右クリックメニュー用ラッパー（ContextMenu は void 前提のため分離）。</summary>
    [ContextMenu("Send Obstacles")]
    private void SendObstaclesMenu() => SendObstacles();

    /// <summary>ロボット基部周辺の Collider を収集して障害物として送信する。成功で true。</summary>
    public bool SendObstacles()
    {
        if (!started || transport == null)
        {
            return false;
        }
        var baseT = ResolveBase();
        if (baseT == null)
        {
            Debug.LogWarning($"[ComRos2Obstacles] ロボット基部が見つかりません（name contains '{robotBaseNameContains}'）。robotBase を Inspector で割当ててください。");
            return false;
        }

        if (debugPose)
        {
            Debug.Log($"[ComRos2Obstacles] base='{baseT.name}' worldPos={baseT.position.ToString("F3")} "
                + $"euler={baseT.rotation.eulerAngles.ToString("F1")} lossyScale={baseT.lossyScale.ToString("F3")} unitScale={unitScale}");
        }

        var list = new List<Ros2Obstacle>();
        var seenIds = new HashSet<string>();
        int skipped = 0;
        var hits = Physics.OverlapSphere(baseT.position, radius, layerMask, QueryTriggerInteraction.Ignore);
        foreach (var col in hits)
        {
            // ロボット自身（基部配下）のコライダーは除外
            if (col.transform == baseT || col.transform.IsChildOf(baseT))
            {
                continue;
            }
            // #12: 床/機械フレーム等の巨大コライダーは planning scene で基部を包含し、
            //      START_STATE_IN_COLLISION（計画不能）を招く。AABB が閾値超え、または基部を
            //      内包するものは障害物にしない。layerMask で絞れない環境向けの安全弁。
            var b = col.bounds;
            float maxSize = Mathf.Max(b.size.x, b.size.y, b.size.z);
            if ((maxObstacleSize > 0f && maxSize > maxObstacleSize) || b.Contains(baseT.position))
            {
                skipped++;
                continue;
            }
            var ob = ToObstacle(col, baseT, baseCalibrationEuler);
            if (ob != null && seenIds.Add(ob.id))
            {
                list.Add(ob);
                if (debugPose)
                {
                    // ob.position は Unity 基部相対(メートル)。ROS へは To<FLU>() で (x=z, y=-x, z=y) 変換される。
                    // ここで同じ式を再現して「ROS が実際に受け取る x,y,z」も出す（キャリブレーション用）。
                    Vector3 p = ob.position;
                    Vector3 rosPos = new Vector3(p.z, -p.x, p.y);
                    Debug.Log($"[ComRos2Obstacles]   obs '{col.name}' type={ob.type} "
                        + $"base相対Unity(m)={p.ToString("F3")} → ROS(x,y,z)={rosPos.ToString("F3")} "
                        + $"dims(ROS順xyz)=[{string.Join(",", System.Array.ConvertAll(ob.dimensions, x => x.ToString("F3")))}]");
                }
            }
        }

        // 明示指定オブジェクト（床など）を、半径/巨大サイズの除外を無視して追加する。
        // ただし基部を内包するものは START_STATE_IN_COLLISION を招くため安全のため除外する。
        int extra = 0;
        foreach (var name in extraObstacleNames)
        {
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }
            var t = FindTransformByName(name);
            if (t == null)
            {
                Debug.LogWarning($"[ComRos2Obstacles] extraObstacle '{name}' が見つかりません。");
                continue;
            }
            foreach (var col in t.GetComponentsInChildren<Collider>())
            {
                if (col.transform == baseT || col.transform.IsChildOf(baseT))
                {
                    continue;
                }
                if (col.bounds.Contains(baseT.position))
                {
                    Debug.LogWarning($"[ComRos2Obstacles] extraObstacle '{col.name}' は基部を内包するため除外（計画不能回避）。");
                    continue;
                }
                var ob = ToObstacle(col, baseT, baseCalibrationEuler);
                if (ob != null && seenIds.Add(ob.id))
                {
                    list.Add(ob);
                    extra++;
                    if (debugPose)
                    {
                        Vector3 p = ob.position;
                        Vector3 rosPos = new Vector3(p.z, -p.x, p.y);
                        Debug.Log($"[ComRos2Obstacles]   extra '{col.name}' "
                            + $"base相対Unity(m)={p.ToString("F3")} → ROS(x,y,z)={rosPos.ToString("F3")} "
                            + $"dims(ROS順xyz)=[{string.Join(",", System.Array.ConvertAll(ob.dimensions, x => x.ToString("F3")))}]");
                    }
                }
            }
        }

        // 他のロボット（選択外）を「現在姿勢の障害物」として送る（1台ずつ計画＝他ロボは動かない障害物）。
        // 各ロボの現在姿勢コライダーを選択ロボ base 相対の AABB 化して合成（トリガの有無を問わず含める）。
        int others = 0;
        if (registry != null)
        {
            var sel = registry.Selected;
            foreach (var reg in registry.Robots)
            {
                if (reg == null || reg.Target == null || reg == sel)
                {
                    continue;   // 選択ロボ自身は障害物にしない
                }
                // 他ロボのヘッド(ツール)は Collider が多い(CAD 150+)ので、そのまま送ると重い＆過剰。
                // 選択ロボ自身のヘッドと同様に「簡略化」し、ヘッド配下は個別送信せず world AABB を 1 箱にまとめる
                // （避けたいだけなので把持開口は不要＝1箱が安全・軽量）。
                var otherHead = reg.Target.GetHeadObject();
                Transform otherHeadTf = otherHead != null ? otherHead.transform : null;
                bool haveHeadAabb = false;
                Vector3 hMin = Vector3.zero, hMax = Vector3.zero;
                foreach (var col in reg.Target.GetBodyColliders())
                {
                    if (col == null || col.transform == baseT || col.transform.IsChildOf(baseT))
                    {
                        continue;
                    }
                    if (otherHeadTf != null && (col.transform == otherHeadTf || col.transform.IsChildOf(otherHeadTf)))
                    {
                        // ヘッド配下 → world AABB に集約（後で 1 箱化）。
                        if (!haveHeadAabb) { hMin = col.bounds.min; hMax = col.bounds.max; haveHeadAabb = true; }
                        else { hMin = Vector3.Min(hMin, col.bounds.min); hMax = Vector3.Max(hMax, col.bounds.max); }
                        continue;
                    }
                    if (col.bounds.Contains(baseT.position))
                    {
                        continue;   // 基部内包は START_STATE_IN_COLLISION を招くため除外
                    }
                    var ob = ToObstacle(col, baseT, baseCalibrationEuler);
                    if (ob != null && seenIds.Add(ob.id))
                    {
                        list.Add(ob);
                        others++;
                    }
                }
                // 集約したヘッドを 1 箱(world AABB)で追加（簡略化ヘッド）。
                if (haveHeadAabb)
                {
                    var hb = new Bounds();
                    hb.SetMinMax(hMin, hMax);
                    if (!hb.Contains(baseT.position))
                    {
                        string hid = (otherHead != null ? otherHead.name : reg.DisplayName) + "#otherhead";
                        var hob = BoxFromWorldAabb(hid, hb.center, hb.size, baseT, baseCalibrationEuler);
                        if (hob != null && seenIds.Add(hob.id))
                        {
                            list.Add(hob);
                            others++;
                        }
                    }
                }
            }
        }
        if (others > 0)
        {
            Debug.Log($"[ComRos2Obstacles] 他ロボットを障害物として {others} 箱追加（選択外の機体）。");
        }

        // DCS keep-out を障害物として追加（planning_scene→プランナが事前回避＋RViz表示）。ROS側は無変更。
        // DCS値は DCS World フレーム(=J1回転中心=J2BASE/arm1 基準, mm)。障害物は base(crx) 基準で送るため、
        //   arm1 の base相対位置(originOffset)を足して arm1 基準へ補正（Unity表示と同じ基準に揃える）。
        int dcsCount = 0;
        var szScript = baseT.GetComponentInParent<SafetyZoneScript>();
        if (szScript != null)
        {
            // DCS原点(J2BASE/arm1)の、障害物フレーム(base=crx 基準)での位置。既存障害物と同じ cal*invRef 変換。
            var dcsTarget = baseT.GetComponentInParent<IRos2PlanTarget>();
            Vector3 originWorld = dcsTarget != null ? dcsTarget.GetRobotOriginWorldPosition() : baseT.position;
            Quaternion invRefDcs = Quaternion.Inverse(baseT.rotation);
            Quaternion calDcs = Quaternion.Euler(baseCalibrationEuler);
            Vector3 originOffset = (calDcs * (invRefDcs * (originWorld - baseT.position))) * unitScale;
            float scDcs = (szScript.ZoneUnit.ToLowerInvariant() == "mm") ? 0.001f : 1f;
            float dcsMargin2 = Mathf.Max(0f, dcsObstacleMargin) * 2f;   // 各面マージン→寸法は両面ぶん加算
            foreach (var z in szScript.KeepOutZones)
            {
                // DCS原点(arm1)を内包する keep-out は START_STATE_IN_COLLISION を招くのでスキップ（DCS座標の符号で判定）。
                if (z.min[0] <= 0f && 0f <= z.max[0] && z.min[1] <= 0f && 0f <= z.max[1] && z.min[2] <= 0f && 0f <= z.max[2])
                {
                    Debug.LogWarning($"[ComRos2Obstacles] DCS 'dcs_{z.id}' は原点を内包→障害物送信スキップ");
                    continue;
                }
                Vector3 cR = new Vector3(z.min[0] + z.max[0], z.min[1] + z.max[1], z.min[2] + z.max[2]) * (0.5f * scDcs);
                Vector3 sR = new Vector3(Mathf.Abs(z.max[0] - z.min[0]), Mathf.Abs(z.max[1] - z.min[1]), Mathf.Abs(z.max[2] - z.min[2])) * scDcs;
                var ob = new Ros2Obstacle
                {
                    id = "dcs_" + (string.IsNullOrEmpty(z.id) ? "zone" : z.id),
                    type = 1,                                                      // BOX
                    dimensions = new float[] { sR.x + dcsMargin2, sR.y + dcsMargin2, sR.z + dcsMargin2 },   // ROS [x,y,z]＋安全マージン
                    position = new Vector3(-cR.y, cR.z, cR.x) + originOffset,      // FLU⁻¹(cR) ＋ arm1基準補正
                    rotation = Quaternion.identity,
                };
                if (seenIds.Add(ob.id)) { list.Add(ob); dcsCount++; }
            }
        }
        if (dcsCount > 0)
        {
            Debug.Log($"[ComRos2Obstacles] DCS keep-out を障害物として {dcsCount} 箱追加（planning_scene→計画回避＋RViz表示）。");
        }

        // 地面(ground plane): 基部の真下・床高さに、可動範囲サイズの薄い板を1枚張る。
        // 実床(1000m級)は送らず、これで「床下へ計画しない」を軽く担保する。
        if (sendGroundPlane)
        {
            float topY;
            var floorT = FindTransformByName(groundNameContains);
            var floorCol = floorT != null ? floorT.GetComponentInChildren<Collider>() : null;
            if (floorCol != null)
            {
                topY = floorCol.bounds.max.y;   // 床の上面（world）
            }
            else
            {
                topY = baseT.position.y - 0.001f;   // 床が無ければ基部直下を地面とする
                Debug.LogWarning($"[ComRos2Obstacles] 地面高さ用 '{groundNameContains}' が見つからず。基部直下を地面とします。");
            }
            // 上面を床高さに合わせた薄板を、基部の真下（水平中心）に配置。
            Vector3 gpCenter = new Vector3(baseT.position.x, topY - groundPlaneThickness * 0.5f, baseT.position.z);
            Vector3 gpSize = new Vector3(groundPlaneSize, groundPlaneThickness, groundPlaneSize);
            var gp = BoxFromWorldAabb("kmx_ground_plane", gpCenter, gpSize, baseT, baseCalibrationEuler);
            if (seenIds.Add(gp.id))
            {
                list.Add(gp);
                if (debugPose)
                {
                    Vector3 p = gp.position;
                    Vector3 rosPos = new Vector3(p.z, -p.x, p.y);
                    Debug.Log($"[ComRos2Obstacles]   ground '{gp.id}' 床上面Y={topY:F3} "
                        + $"base相対Unity(m)={p.ToString("F3")} → ROS(x,y,z)={rosPos.ToString("F3")} "
                        + $"dims(ROS順xyz)=[{string.Join(",", System.Array.ConvertAll(gp.dimensions, x => x.ToString("F3")))}]");
                }
            }
        }

        transport.PublishObstacles(topic, frameId, list);
        Debug.Log($"[ComRos2Obstacles] {list.Count} obstacles 送信 (frame='{frameId}', radius={radius}, 除外={skipped} 巨大/基部包含, 明示追加={extra}, 地面={(sendGroundPlane ? 1 : 0)})");
        return true;
    }

    /// <summary>ヘッド全箱の中心がこの距離(m)以内でフランジ原点に潰れていたら「pose未確定＝縮退」とみなす。</summary>
    private const float HeadDegenerateEps = 1e-4f;

    /// <summary>
    /// ヘッド(ツール)を AttachedCollisionObject 用に送る（方式B）。
    /// 障害物と同じ ObstaclesMsg を別トピック attachedTopic に流し、frame_id に attach 先リンク名を入れる。
    /// ROS2 側は frame_id を attach 先として AttachedCollisionObject 化する（HEAD_TOOL_ROS2_SPEC.md 方式B）。
    /// ヘッド配下の Collider は isTrigger の有無を問わず全て対象にする。成功で true。
    /// </summary>
    [ContextMenu("Send Head (ツールを attach 送信)")]
    public bool SendHead()
    {
        if (!started || transport == null)
        {
            return false;
        }
        var head = ResolveHead();
        if (head == null)
        {
            // 対象確定済みでヘッド無し（例 ユニット3）＝正常。前に付いていたヘッドを消すため空の attached を送る
            //   （ROS側は受信リストで attach を置換＝空で detach。同一modelで再起動が無い切替時の残留を防ぐ）。
            if (targetResolved)
            {
                transport.PublishObstacles(attachedTopic, attachLinkName, new List<Ros2Obstacle>());
                Debug.Log("[ComRos2Obstacles] この機体はヘッド無し → attached を空送信でクリア");
                return true;
            }
            Debug.LogWarning("[ComRos2Obstacles] ヘッド(HeadObject)が見つかりません。headRoot を割当てるか Kinematics6D.HeadObject を確認してください。");
            return false;
        }
        // 参照フレーム＝フランジ。複数ロボットでは名前検索(FindTransformByName)がシーン全体を走査し、
        // 別ロボの同名フランジ(例 J6FLANGE)を先に掴んでヘッドが別ロボ位置に付く不具合になる。
        // → 選択ロボの「ヘッドの親(=そのロボの arm6/フランジに parent 済み)」を最優先。無い時だけ名前検索へ。
        var flange = head.parent;
        if (flange == null)
        {
            flange = FindTransformByName(flangeNameContains);
        }
        if (flange == null)
        {
            Debug.LogWarning($"[ComRos2Obstacles] フランジが見つかりません（flangeNameContains='{flangeNameContains}'）。");
            return false;
        }

        if (debugPose)
        {
            Debug.Log($"[ComRos2Obstacles] head flange='{flange.name}' worldPos={flange.position.ToString("F3")} "
                + $"euler={flange.rotation.eulerAngles.ToString("F1")} （補正は ROS2 head_calibration_rpy）");
        }

        // ヘッド配下の Collider を isTrigger の有無を問わず全て収集（ツール形状として送る）。
        // 補正は掛けず「生(raw)のフランジ相対」で送る（ROS2 側 head_calibration_rpy で補正）。
        // ★姿勢不変化: col.bounds(world AABB) はフランジが回ると膨らみ・姿勢で変動するので使わず、
        //   コライダーの「ローカル bounds」からフランジ相対 AABB を作る（剛体ツールなので姿勢に依らず一定）。
        var cols = head.GetComponentsInChildren<Collider>();
        var list = new List<Ros2Obstacle>();

        // 全 Collider を「フランジ相対の AABB(center,size)」に変換して集める。
        var boxMin = new List<Vector3>();
        var boxMax = new List<Vector3>();
        int nearZero = 0;   // 中心がフランジ原点付近の箱数（縮退検知用）
        foreach (var col in cols)
        {
            if (!TryHeadLocalAabb(col, flange, out var c, out var s))
            {
                continue;
            }
            if (c.magnitude < HeadDegenerateEps)
            {
                nearZero++;
            }
            boxMin.Add(c - s * 0.5f);
            boxMax.Add(c + s * 0.5f);
        }

        // ★縮退ガード：全箱の中心がフランジ原点付近に潰れている＝transform 未確定フレームで pose を
        //   読んだ疑い（HEAD_POSE_ZERO_UNITY_SPEC.md）。pose=0 で送ると ROS2 でヘッドが原点に潰れるため、
        //   今回は送信せず前回の正常な attach を維持し、呼び元（オート送信）のリトライに委ねる。
        if (boxMin.Count > 0 && nearZero >= boxMin.Count)
        {
            Debug.LogWarning($"[ComRos2Obstacles] ヘッド pose 縮退を検知（全{boxMin.Count}箱の中心がフランジ原点付近）。"
                + $"flange='{flange.name}' head='{head.name}' cols={cols.Length}。transform 未確定の疑い→送信スキップ（前回維持）。");
            return false;
        }

        if (boxMin.Count == 0)
        {
            // 何も無ければ空送信（全消し）。
        }
        else if (headAsSingleBox)
        {
            // 全体を1箱に統合。
            Vector3 mn = boxMin[0], mx = boxMax[0];
            for (int i = 1; i < boxMin.Count; i++) { mn = Vector3.Min(mn, boxMin[i]); mx = Vector3.Max(mx, boxMax[i]); }
            list.Add(BoxFromFlangeLocal(head.name + "#headbox", (mn + mx) * 0.5f, mx - mn));
        }
        else
        {
            // ★間引き: フランジ相対グリッドの各非空セルを1箱に統合（開口セルは空のまま残る）。
            Vector3 allMin = boxMin[0], allMax = boxMax[0];
            for (int i = 1; i < boxMin.Count; i++) { allMin = Vector3.Min(allMin, boxMin[i]); allMax = Vector3.Max(allMax, boxMax[i]); }
            int gx = Mathf.Max(1, headGrid.x), gy = Mathf.Max(1, headGrid.y), gz = Mathf.Max(1, headGrid.z);
            Vector3 span = allMax - allMin;
            Vector3 cell = new Vector3(span.x / gx, span.y / gy, span.z / gz);
            var cellMin = new Dictionary<int, Vector3>();
            var cellMax = new Dictionary<int, Vector3>();
            for (int i = 0; i < boxMin.Count; i++)
            {
                Vector3 ctr = (boxMin[i] + boxMax[i]) * 0.5f;
                int ix = cell.x > 1e-9f ? Mathf.Clamp((int)((ctr.x - allMin.x) / cell.x), 0, gx - 1) : 0;
                int iy = cell.y > 1e-9f ? Mathf.Clamp((int)((ctr.y - allMin.y) / cell.y), 0, gy - 1) : 0;
                int iz = cell.z > 1e-9f ? Mathf.Clamp((int)((ctr.z - allMin.z) / cell.z), 0, gz - 1) : 0;
                int key = (ix * gy + iy) * gz + iz;
                if (!cellMin.ContainsKey(key)) { cellMin[key] = boxMin[i]; cellMax[key] = boxMax[i]; }
                else { cellMin[key] = Vector3.Min(cellMin[key], boxMin[i]); cellMax[key] = Vector3.Max(cellMax[key], boxMax[i]); }
            }
            foreach (var kv in cellMin)
            {
                Vector3 mn = kv.Value, mx = cellMax[kv.Key];
                list.Add(BoxFromFlangeLocal($"{head.name}#hc{kv.Key}", (mn + mx) * 0.5f, mx - mn));
            }
        }
        if (debugPose)
        {
            foreach (var ob in list)
            {
                Vector3 p = ob.position;
                Vector3 rosPos = new Vector3(p.z, -p.x, p.y);   // To<FLU> 相当
                Debug.Log($"[ComRos2Obstacles]   head '{ob.id}' "
                    + $"flange相対Unity(m)={p.ToString("F3")} → ROS(x,y,z)={rosPos.ToString("F3")} "
                    + $"dims(ROS順xyz)=[{string.Join(",", System.Array.ConvertAll(ob.dimensions, x => x.ToString("F3")))}]");
            }
        }
        // frame_id に attach 先リンク名を載せる（ROS2側が AttachedCollisionObject の link に使う）。
        transport.PublishObstacles(attachedTopic, attachLinkName, list);
        Debug.Log($"[ComRos2Obstacles] head {list.Count}/{cols.Length} 個を attach 送信 "
            + $"(topic='{attachedTopic}' link='{attachLinkName}' flange='{flange.name}' head='{head.name}' nearZero={nearZero})");
        return true;
    }

    /// <summary>
    /// 障害物・ヘッドを全消しする（両トピックに空配列を送る）。ROS2側の全置換で古い分が消える。
    /// テストで planning scene をリセットしたい時に使う。
    /// </summary>
    [ContextMenu("Clear Scene (障害物/ヘッドを全消し)")]
    public void ClearScene()
    {
        if (!started || transport == null)
        {
            return;
        }
        var empty = new List<Ros2Obstacle>();
        transport.PublishObstacles(topic, frameId, empty);
        transport.PublishObstacles(attachedTopic, attachLinkName, empty);
        Debug.Log("[ComRos2Obstacles] Clear Scene 送信（障害物/ヘッド 空）");
    }

    /// <summary>ヘッド(ツール)ルートを解決。headRoot 明示 &gt; Kinematics6D.HeadObject。</summary>
    private Transform ResolveHead()
    {
        if (headRoot != null)
        {
            return headRoot;
        }
        // ★対象ロボが確定済み(SetTarget 済)なら、その機体にヘッドが無い＝ヘッド無しが正。
        //   ここで他機体のヘッドを拾うと、ヘッド未装着の機体(例 ユニット3)に別機体のヘッドが attach される誤爆になる。
        if (targetResolved)
        {
            return null;
        }
        // 単機/対象未確定時のみ：シーンの唯一のロボのヘッドを使う（後方互換）。
        foreach (var k in FindObjectsByType<Kinematics6D>(FindObjectsSortMode.None))
        {
            var h = k.GetHeadObject();
            if (h != null)
            {
                headRoot = h.transform;   // キャッシュ
                return headRoot;
            }
        }
        return null;
    }

    private Transform ResolveBase()
    {
        if (robotBase != null)
        {
            return robotBase;
        }
        if (string.IsNullOrEmpty(robotBaseNameContains))
        {
            return null;
        }
        // ⚠ 部分一致だとメッシュ名 "J1BASE^…_CRX-30IA_FANUC-1"（＝J1で回る arm1）に先にヒットし、
        //   関節で回る誤フレームを基部にしてしまう。コード生成の固定ルート（CRX-30iA.cs の
        //   new GameObject("CRX-30iA")）は名前が robotBaseNameContains と「完全一致」するので、
        //   完全一致（大小無視）を最優先で探す。無ければ従来どおり部分一致にフォールバック。
        //   別ロボ構成では robotBaseNameContains を変えるか robotBase を明示割当てすること。
        Transform containsMatch = null;
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (string.Equals(t.name, robotBaseNameContains, StringComparison.OrdinalIgnoreCase))
            {
                robotBase = t;   // 完全一致＝固定ルート（最優先・キャッシュ）
                return t;
            }
            if (containsMatch == null
                && t.name.IndexOf(robotBaseNameContains, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                containsMatch = t;
            }
        }
        robotBase = containsMatch;   // フォールバック（見つからなければ null）
        return containsMatch;
    }

    /// <summary>
    /// 世代をまたいでも安定な障害物ID（階層パス＋兄弟index）。
    /// GetInstanceID はセッション毎に変わり、move_group を起動したまま Play し直すと
    /// planning scene に古いIDが残留して累積する（＝重なる）。パスなら同じオブジェクトは同じID。
    /// </summary>
    private static string StableId(Collider col)
    {
        var t = col.transform;
        string id = t.name + "#" + t.GetSiblingIndex();
        var p = t.parent;
        int guard = 0;
        while (p != null && guard++ < 6)
        {
            id = p.name + "/" + id;
            p = p.parent;
        }
        return id;
    }

    /// <summary>名前で Transform を探す（完全一致=大小無視 を優先、無ければ部分一致）。
    /// プレビュー用ゴースト複製("_Ghost"配下)は実機でないので除外する（同名 J6FLANGE 等の誤取得防止）。</summary>
    private static Transform FindTransformByName(string nameKey)
    {
        if (string.IsNullOrEmpty(nameKey))
        {
            return null;
        }
        Transform contains = null;
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            bool exact = string.Equals(t.name, nameKey, StringComparison.OrdinalIgnoreCase);
            bool partial = !exact && (contains == null)
                && (t.name.IndexOf(nameKey, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!exact && !partial)
            {
                continue;   // 名前不一致は即スキップ（ゴースト判定コストを一致時だけに限定）
            }
            // ★ゴースト複製は実機ではない。再生中に同名フランジ等を誤って拾うとヘッド姿勢が崩れるため除外。
            if (IsUnderGhost(t))
            {
                continue;
            }
            if (exact)
            {
                return t;
            }
            contains = t;
        }
        return contains;
    }

    /// <summary>プレビュー用ゴースト複製（名前に "_Ghost" を含む）配下か。名前検索の対象外にする。</summary>
    private static bool IsUnderGhost(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
        {
            if (p.name.IndexOf("_Ghost", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// ヘッド(ツール)の寸法と、6軸フランジからの取付オフセットをログ出力する。
    /// URDF にツールを固定リンクとして足すときの box size / origin xyz の目安に使う（静的方式）。
    /// ※ フランジのローカル軸(Unity)と URDF リンク軸のずれ分は、base_link と同様に別途微調整が要る場合あり。
    /// </summary>
    [ContextMenu("Measure Head (ツール寸法をログ)")]
    private void MeasureHead()
    {
        var flange = FindTransformByName(flangeNameContains);
        if (flange == null)
        {
            Debug.LogWarning($"[ComRos2Obstacles] フランジ '{flangeNameContains}' が見つかりません。flangeNameContains を確認してください。");
            return;
        }
        var head = headRoot != null ? headRoot : flange;
        var cols = head.GetComponentsInChildren<Collider>();
        if (cols.Length == 0)
        {
            Debug.LogWarning($"[ComRos2Obstacles] '{head.name}' 配下に Collider がありません（ヘッドにコライダーを付けるか headRoot を割当ててください）。");
            return;
        }
        Bounds wb = cols[0].bounds;
        foreach (var c in cols)
        {
            wb.Encapsulate(c.bounds);   // 世界AABB を統合
        }
        Vector3 sizeM = wb.size * unitScale;                                   // box size(m)
        Vector3 localCenter = (Quaternion.Inverse(flange.rotation) * (wb.center - flange.position)) * unitScale; // Unityローカル(m)
        Vector3 rosCenter = new Vector3(localCenter.z, -localCenter.x, localCenter.y);   // ROS(FLU) 目安
        Debug.Log($"[ComRos2Obstacles] Head計測: flange='{flange.name}' head='{head.name}' colliders={cols.Length}\n"
            + $"  ★URDF box size(m) 目安 = {sizeM.ToString("F3")}\n"
            + $"  ★URDF origin xyz(m) 目安 = flange相対 Unity {localCenter.ToString("F3")} → ROS(FLU) {rosCenter.ToString("F3")}\n"
            + $"  （rpy は 0 起点で、実機と合わなければ base_link と同様に微調整。詳細は kmx_ros2/HEAD_TOOL_ROS2_SPEC.md）");
    }

    /// <summary>
    /// Collider → Ros2Obstacle。球以外は「基部フレームに軸整列した世界AABB(BOX)」で送る。
    ///
    /// なぜ AABB（向きを持たせない）か：対象が CAD 由来の B-rep コライダーだと、コライダーのローカル軸が
    /// 素直でなく（見た目は平置きでも frame は傾いている）、姿勢(To&lt;FLU&gt;)＋寸法並べ替えで送ると
    /// 箱が倒れる／隣接コライダーが分離する／上下が反転する。障害物回避の用途では軸整列AABBで十分・安全
    /// （やや大きめになるだけ）。姿勢は基部相対・メートル。
    /// </summary>
    private Ros2Obstacle ToObstacle(Collider col, Transform refT, Vector3 calEuler)
    {
        var ob = new Ros2Obstacle { id = StableId(col) };
        Quaternion invRef = Quaternion.Inverse(refT.rotation);
        // 参照フレーム(基部 or フランジ)→ ROS リンク の補正回転。位置・寸法に一貫適用。
        Quaternion cal = Quaternion.Euler(calEuler);

        // 球は向きに依らないので中心＋半径のまま。
        // ⚠ InverseTransformPoint は refT の lossyScale で割ってしまい寸法(world サイズ)と単位が食い違う
        //   ので使わない。回転のみで参照フレームへ入れ、world オフセットのまま unitScale でメートル化する。
        if (col is SphereCollider sc)
        {
            var ls = col.transform.lossyScale;
            float m = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
            ob.type = 2; // SPHERE
            ob.dimensions = new float[] { sc.radius * m * unitScale };
            Vector3 wc = col.transform.TransformPoint(sc.center);
            ob.position = (cal * (invRef * (wc - refT.position))) * unitScale;
            ob.rotation = Quaternion.identity;
            return ob;
        }

        // Box / Capsule / Mesh → 世界AABB。MeshCollider 等は includeNonPrimitiveAsBox で可否。
        if (!(col is BoxCollider) && !(col is CapsuleCollider) && !includeNonPrimitiveAsBox)
        {
            return null;
        }
        var b = col.bounds;   // world 軸AABB
        return BoxFromWorldAabb(StableId(col), b.center, b.size, refT, calEuler);
    }

    /// <summary>
    /// 世界AABB(中心・寸法) → 参照フレーム軸整列の BOX Ros2Obstacle。
    /// 位置は回転のみ（lossyScale で割らない）、寸法は任意回転で正しい AABB 式＋FLU 並べ替え。
    /// </summary>
    private Ros2Obstacle BoxFromWorldAabb(string id, Vector3 worldCenter, Vector3 worldSize, Transform refT, Vector3 calEuler)
    {
        var ob = new Ros2Obstacle { id = id, type = 1, rotation = Quaternion.identity };
        Quaternion invRef = Quaternion.Inverse(refT.rotation);
        Quaternion cal = Quaternion.Euler(calEuler);
        ob.position = (cal * (invRef * (worldCenter - refT.position))) * unitScale;
        // 寸法: newSize_i = Σ_j |R_ij| * size_j（R=cal*invRef）→ FLU 並べ替え [z,x,y]。
        Quaternion rot = cal * invRef;
        Vector3 rx = rot * Vector3.right;
        Vector3 ry = rot * Vector3.up;
        Vector3 rz = rot * Vector3.forward;
        Vector3 aabb = new Vector3(
            Mathf.Abs(rx.x) * worldSize.x + Mathf.Abs(ry.x) * worldSize.y + Mathf.Abs(rz.x) * worldSize.z,
            Mathf.Abs(rx.y) * worldSize.x + Mathf.Abs(ry.y) * worldSize.y + Mathf.Abs(rz.y) * worldSize.z,
            Mathf.Abs(rx.z) * worldSize.x + Mathf.Abs(ry.z) * worldSize.y + Mathf.Abs(rz.z) * worldSize.z)
            * unitScale;
        ob.dimensions = new float[] { aabb.z, aabb.x, aabb.y };
        return ob;
    }

    /// <summary>
    /// コライダーの「姿勢不変なフランジ相対 AABB」を返す（Unity flange-local の center/size, unitScale前）。
    /// col.bounds(world AABB) と違い、ローカル bounds をフランジ相対の回転で AABB 化するので、
    /// ロボット姿勢に依らず一定（剛体ツール向け）。かつ world→再AABB の二重膨張が無く小さい。
    /// </summary>
    private static bool TryHeadLocalAabb(Collider col, Transform refT, out Vector3 center, out Vector3 size)
    {
        center = Vector3.zero;
        size = Vector3.zero;
        if (!TryLocalBounds(col, out var lb))
        {
            return false;
        }
        var lossy = col.transform.lossyScale;
        Vector3 half = new Vector3(
            Mathf.Abs(lb.extents.x * lossy.x),
            Mathf.Abs(lb.extents.y * lossy.y),
            Mathf.Abs(lb.extents.z * lossy.z));
        Quaternion invRef = Quaternion.Inverse(refT.rotation);
        Vector3 worldCenter = col.transform.TransformPoint(lb.center);
        center = invRef * (worldCenter - refT.position);           // 姿勢不変のフランジ相対中心
        Quaternion R = invRef * col.transform.rotation;           // フランジ相対のコライダー回転（姿勢不変）
        Vector3 rx = R * Vector3.right, ry = R * Vector3.up, rz = R * Vector3.forward;
        size = new Vector3(
            (Mathf.Abs(rx.x) * half.x + Mathf.Abs(ry.x) * half.y + Mathf.Abs(rz.x) * half.z) * 2f,
            (Mathf.Abs(rx.y) * half.x + Mathf.Abs(ry.y) * half.y + Mathf.Abs(rz.y) * half.z) * 2f,
            (Mathf.Abs(rx.z) * half.x + Mathf.Abs(ry.z) * half.y + Mathf.Abs(rz.z) * half.z) * 2f);
        return true;
    }

    /// <summary>コライダーのローカル bounds（center/size, スケール前）。対応外は false。</summary>
    private static bool TryLocalBounds(Collider col, out Bounds lb)
    {
        if (col is BoxCollider bc) { lb = new Bounds(bc.center, bc.size); return true; }
        if (col is SphereCollider sc) { lb = new Bounds(sc.center, Vector3.one * (sc.radius * 2f)); return true; }
        if (col is CapsuleCollider cc)
        {
            float d = cc.radius * 2f;
            Vector3 sz = new Vector3(d, d, d);
            if (cc.direction == 0) { sz.x = Mathf.Max(cc.height, d); }
            else if (cc.direction == 1) { sz.y = Mathf.Max(cc.height, d); }
            else { sz.z = Mathf.Max(cc.height, d); }
            lb = new Bounds(cc.center, sz);
            return true;
        }
        if (col is MeshCollider mc && mc.sharedMesh != null) { lb = mc.sharedMesh.bounds; return true; }
        lb = default;
        return false;
    }

    /// <summary>Unity flange-local の center/size(スケール前) → Ros2Obstacle（FLU並べ替え・unitScale適用）。</summary>
    private Ros2Obstacle BoxFromFlangeLocal(string id, Vector3 center, Vector3 size)
    {
        return new Ros2Obstacle
        {
            id = id,
            type = 1,
            rotation = Quaternion.identity,
            position = center * unitScale,
            dimensions = new float[] { size.z * unitScale, size.x * unitScale, size.y * unitScale },
        };
    }
}
