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
    [Tooltip("このサイズ(Unity単位)を超えるAABBは床/機械フレームとみなし障害物にしない（基部包含→START_STATE_IN_COLLISION 回避）。0以下で無効")]
    [SerializeField] private float maxObstacleSize = 2.0f;
    [Tooltip("MeshCollider等は AABB box にして送る")]
    [SerializeField] private bool includeNonPrimitiveAsBox = true;
    [Tooltip("ロード完了後に1回だけ自動送信する")]
    [SerializeField] private bool autoSendOnLoad = false;
    [Tooltip("座標キャリブレーション用。基部/各障害物の座標をログ出力する（既定OFF。ズレ調査時にON）")]
    [SerializeField] private bool debugPose = false;
    [Tooltip("基部フレーム→URDF base_link の補正回転(度・基部ローカル)。CRX-30iA の Unity 基部は世界軸(Y-up)なので、"
        + "水平面をヨー-90°して base_link(X=前,Y=左,Z=上)へ合わせる。向きが違う構成では調整")]
    [SerializeField] private Vector3 baseCalibrationEuler = new Vector3(0f, -90f, 0f);

    private IRos2Transport transport;
    private bool started;
    private bool destroyed;
    private bool sentOnce;
    private float sinceLastAutoTry;   // autoSend: 基部未解決時の再試行スロットル用
    private int autoTries;            // autoSend: 試行回数（上限で打ち切り、全シーン走査の無限化を防ぐ）
    private const int AutoTryMax = 20;

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
        // publisher 事前登録（初回送信で "Not registered" レース回避）
        transport.RegisterObstaclesPublisher(topic);
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
                    sentOnce = true;
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
            var ob = ToObstacle(col, baseT);
            if (ob != null)
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
        transport.PublishObstacles(topic, frameId, list);
        Debug.Log($"[ComRos2Obstacles] {list.Count} obstacles 送信 (frame='{frameId}', radius={radius}, 除外={skipped} 巨大/基部包含)");
        return true;
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
    /// Collider → Ros2Obstacle。球以外は「基部フレームに軸整列した世界AABB(BOX)」で送る。
    ///
    /// なぜ AABB（向きを持たせない）か：対象が CAD 由来の B-rep コライダーだと、コライダーのローカル軸が
    /// 素直でなく（見た目は平置きでも frame は傾いている）、姿勢(To&lt;FLU&gt;)＋寸法並べ替えで送ると
    /// 箱が倒れる／隣接コライダーが分離する／上下が反転する。障害物回避の用途では軸整列AABBで十分・安全
    /// （やや大きめになるだけ）。姿勢は基部相対・メートル。
    /// </summary>
    private Ros2Obstacle ToObstacle(Collider col, Transform baseT)
    {
        var ob = new Ros2Obstacle { id = col.gameObject.name + "_" + col.GetInstanceID() };
        Quaternion invBase = Quaternion.Inverse(baseT.rotation);
        // 基部フレーム→base_link の補正回転（既定 (0,-90,0)）。位置・寸法に一貫適用。
        Quaternion cal = Quaternion.Euler(baseCalibrationEuler);

        // 球は向きに依らないので中心＋半径のまま。
        // ⚠ InverseTransformPoint は baseT の lossyScale で割ってしまい寸法(world サイズ)と単位が食い違う
        //   ので使わない。回転のみで基部フレームへ入れ、world オフセットのまま unitScale でメートル化する。
        if (col is SphereCollider sc)
        {
            var ls = col.transform.lossyScale;
            float m = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
            ob.type = 2; // SPHERE
            ob.dimensions = new float[] { sc.radius * m * unitScale };
            Vector3 wc = col.transform.TransformPoint(sc.center);
            ob.position = (cal * (invBase * (wc - baseT.position))) * unitScale;
            ob.rotation = Quaternion.identity;
            return ob;
        }

        // Box / Capsule / Mesh → 世界AABB。MeshCollider 等は includeNonPrimitiveAsBox で可否。
        if (!(col is BoxCollider) && !(col is CapsuleCollider) && !includeNonPrimitiveAsBox)
        {
            return null;
        }
        var b = col.bounds;                     // world 軸AABB（中心・寸法とも world 軸整列）
        Vector3 worldCenter = b.center;
        Vector3 worldSize = b.size;

        ob.type = 1; // BOX
        ob.position = (cal * (invBase * (worldCenter - baseT.position))) * unitScale;
        // 向きは持たせない＝base_link 軸整列（To<FLU>(identity)=identity）。倒れ/相対上下反転を防ぐ。
        ob.rotation = Quaternion.identity;

        // 寸法: world AABB を (cal*invBase) で基部フレームへ回した後の AABB 寸法。
        // 任意回転で正しい式: newSize_i = Σ_j |R_ij| * size_j。最後に FLU 並べ替え [z,x,y]。
        Quaternion rot = cal * invBase;
        Vector3 rx = rot * Vector3.right;       // R 列0
        Vector3 ry = rot * Vector3.up;          // R 列1
        Vector3 rz = rot * Vector3.forward;     // R 列2
        Vector3 aabb = new Vector3(
            Mathf.Abs(rx.x) * worldSize.x + Mathf.Abs(ry.x) * worldSize.y + Mathf.Abs(rz.x) * worldSize.z,
            Mathf.Abs(rx.y) * worldSize.x + Mathf.Abs(ry.y) * worldSize.y + Mathf.Abs(rz.y) * worldSize.z,
            Mathf.Abs(rx.z) * worldSize.x + Mathf.Abs(ry.z) * worldSize.y + Mathf.Abs(rz.z) * worldSize.z)
            * unitScale;
        ob.dimensions = new float[] { aabb.z, aabb.x, aabb.y };   // Unity(x,y,z)→ROS(FLU)軸順
        return ob;
    }
}
