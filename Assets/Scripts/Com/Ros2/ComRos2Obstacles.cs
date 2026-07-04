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
    [SerializeField] private LayerMask layerMask = ~0;
    [Tooltip("Unity単位→メートル。KMXのスケールに合わせる")]
    [SerializeField] private float unitScale = 1.0f;
    [Tooltip("MeshCollider等は AABB box にして送る")]
    [SerializeField] private bool includeNonPrimitiveAsBox = true;
    [Tooltip("ロード完了後に1回だけ自動送信する")]
    [SerializeField] private bool autoSendOnLoad = false;

    private IRos2Transport transport;
    private bool started;
    private bool destroyed;
    private bool sentOnce;

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
            SendObstacles();
            sentOnce = true;
        }
    }

    /// <summary>ロボット基部周辺の Collider を収集して障害物として送信する。</summary>
    [ContextMenu("Send Obstacles")]
    public void SendObstacles()
    {
        if (!started || transport == null)
        {
            return;
        }
        var baseT = ResolveBase();
        if (baseT == null)
        {
            Debug.LogWarning($"[ComRos2Obstacles] ロボット基部が見つかりません（name contains '{robotBaseNameContains}'）。robotBase を Inspector で割当ててください。");
            return;
        }

        var list = new List<Ros2Obstacle>();
        var hits = Physics.OverlapSphere(baseT.position, radius, layerMask, QueryTriggerInteraction.Ignore);
        foreach (var col in hits)
        {
            // ロボット自身（基部配下）のコライダーは除外
            if (col.transform == baseT || col.transform.IsChildOf(baseT))
            {
                continue;
            }
            var ob = ToObstacle(col, baseT);
            if (ob != null)
            {
                list.Add(ob);
            }
        }
        transport.PublishObstacles(topic, frameId, list);
        Debug.Log($"[ComRos2Obstacles] {list.Count} obstacles 送信 (frame='{frameId}', radius={radius})");
    }

    private Transform ResolveBase()
    {
        if (robotBase != null)
        {
            return robotBase;
        }
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t.name.Contains(robotBaseNameContains))
            {
                robotBase = t;
                return t;
            }
        }
        return null;
    }

    /// <summary>Collider → Ros2Obstacle（基部相対・寸法はメートル・BOXはROS軸順[x,y,z]=Unity[z,x,y]）。</summary>
    private Ros2Obstacle ToObstacle(Collider col, Transform baseT)
    {
        var ob = new Ros2Obstacle { id = col.gameObject.name + "_" + col.GetInstanceID() };
        Vector3 worldCenter;
        Quaternion worldRot;

        if (col is BoxCollider bc)
        {
            var ls = col.transform.lossyScale;
            var size = new Vector3(Mathf.Abs(bc.size.x * ls.x), Mathf.Abs(bc.size.y * ls.y), Mathf.Abs(bc.size.z * ls.z)) * unitScale;
            ob.type = 1; // BOX
            ob.dimensions = new float[] { size.z, size.x, size.y };   // Unity(x,y,z)→ROS(FLU)軸順
            worldCenter = col.transform.TransformPoint(bc.center);
            worldRot = col.transform.rotation;
        }
        else if (col is SphereCollider sc)
        {
            var ls = col.transform.lossyScale;
            float m = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
            ob.type = 2; // SPHERE
            ob.dimensions = new float[] { sc.radius * m * unitScale };
            worldCenter = col.transform.TransformPoint(sc.center);
            worldRot = col.transform.rotation;
        }
        else if (col is CapsuleCollider cc)
        {
            var ls = col.transform.lossyScale;
            float mr = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.z));
            ob.type = 3; // CYLINDER（近似：高さ=Y方向想定）
            ob.dimensions = new float[] { cc.height * Mathf.Abs(ls.y) * unitScale, cc.radius * mr * unitScale };
            worldCenter = col.transform.TransformPoint(cc.center);
            worldRot = col.transform.rotation;
        }
        else
        {
            if (!includeNonPrimitiveAsBox)
            {
                return null;
            }
            // MeshCollider 等 → 世界AABB box
            var b = col.bounds;
            var size = b.size * unitScale;
            ob.type = 1; // BOX
            ob.dimensions = new float[] { size.z, size.x, size.y };
            worldCenter = b.center;
            worldRot = Quaternion.identity;   // AABB は世界軸整列
        }

        // 基部相対へ（トランスポートで ROS系へ軸変換）。位置も unitScale でメートル化（寸法と同じ扱い）。
        ob.position = baseT.InverseTransformPoint(worldCenter) * unitScale;
        ob.rotation = Quaternion.Inverse(baseT.rotation) * worldRot;
        return ob;
    }
}
