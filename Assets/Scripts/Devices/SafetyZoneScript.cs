using System.Collections.Generic;
using Parameters;
using UnityEngine;

/// <summary>
/// DCS(Dual Check Safety)安全ゾーンの可視化。SafetyZoneInfo.json のゾーン（直交箱・mm・ロボット World/base フレーム）を
/// ロボット基準（IRos2PlanTarget.GetBaseTransform）下に **半透明ボックス＋ワイヤフレーム** で表示する。
///
/// - **読むだけ**（DCS 設定は書き換えない）。DCS＝固定の検証済み安全エンベロープ。
/// - insideAllowed で色分け（true=内側が安全域＝緑 / false=内側が進入禁止＝赤）。
/// - 座標合わせは setting.calibrationEuler / calibrationOffset で実測調整（kmx_ros2/DCS_ZONE_IMPORT_SPEC.md §4.4）。
///   既定は障害物送信と同じ基準補正(0,-90,0)＋ROS→Unity 逆軸写像。まず1ゾーンで DCS 表示と目視突合してから調整。
/// - ROS2 非依存（KMX_ROS2 gate の外）。基準 Transform が未確定な間は再試行し、確定後に1回だけ生成。
/// </summary>
[DisallowMultipleComponent]
public sealed class SafetyZoneScript : MonoBehaviour
{
    private SafetyZoneSetting setting;
    private IRos2PlanTarget target;
    private GameObject container;
    private bool built;
    private int tries;

    /// <summary>ゾーン設定を渡す（ParameterLoader から）。基準確定後に Update が1回だけ描画する。</summary>
    public void SetParameter(SafetyZoneSetting s)
    {
        setting = s;
        built = false;
        tries = 0;
        if (s == null) { Cleanup(); }   // 設定が無くなったら即座に既存ゾーンを消す（再読込で0件等）
    }

    /// <summary>単位（"mm" 既定）。ROS base フレーム。障害物送信の mm→m 用。</summary>
    public string ZoneUnit => (setting != null && !string.IsNullOrEmpty(setting.unit)) ? setting.unit : "mm";

    /// <summary>
    /// keep-out（進入禁止＝insideAllowed=false）かつ有効なゾーン。障害物として MoveIt planning_scene へ送る用
    /// （ComRos2Obstacles が /kmx/obstacles 経由で送信→プランナ回避＋RViz表示）。設定無しなら空。
    /// min/max は DCS の ROS base フレーム値（unit 準拠）。
    /// </summary>
    public IReadOnlyList<SafetyZone> KeepOutZones
    {
        get
        {
            var list = new List<SafetyZone>();
            if (setting != null && setting.zones != null)
            {
                foreach (var z in setting.zones)
                {
                    if (z != null && z.enabled && !z.insideAllowed
                        && z.min != null && z.max != null && z.min.Count >= 3 && z.max.Count >= 3)
                    {
                        list.Add(z);
                    }
                }
            }
            return list;
        }
    }

    private void Update()
    {
        if (built || setting == null)
        {
            return;
        }
        if (tries++ > 600)   // ~10s(60fps) 基準が来なければ諦める
        {
            setting = null;
            return;
        }
        if (target == null)
        {
            target = GetComponent<IRos2PlanTarget>();
        }
        var baseT = target != null ? target.GetBaseTransform() : null;
        if (baseT == null)
        {
            return;   // 基準未確定 → 次フレーム再試行
        }
        Build(baseT);
        built = true;
    }

    private void OnDestroy()
    {
        Cleanup();
    }

    private void Cleanup()
    {
        if (container != null)
        {
            Destroy(container);
            container = null;
        }
    }

    private void Build(Transform baseT)
    {
        Cleanup();
        container = new GameObject("SafetyZones");
        container.transform.SetParent(baseT, false);
        // 座標合わせ。障害物送信の順写像は ROS = FLU( cal · Rbaseᵀ · (world - base) )（cal=Euler(calibrationEuler) 既定(0,-90,0)）。
        // その逆で world = base + Rbase · calⁱⁿᵛ · P（P はUnity軸の較正済base座標）。よってコンテナは calⁱⁿᵛ 回転で base 直下に置き、
        // 各ゾーンはその中で localPosition=P（=ROSの逆軸写像）を取れば正しい位置に来る。offset は P フレームでの微調整。
        Vector3 calEuler = ToVec(setting.calibrationEuler, new Vector3(0f, -90f, 0f));
        Quaternion calInv = Quaternion.Inverse(Quaternion.Euler(calEuler));
        Vector3 offset = ToVec(setting.calibrationOffset, Vector3.zero);
        // 原点(0,0,0)は DCS/ROBOGUIDE の World 原点＝ロボット実ベース位置に合わせる。
        // 姿勢は base(crx)の固定姿勢、位置は GetRobotOriginWorldPosition()（CRX は arm1=J1軸位置）。
        // crx(=moveObject原点)は実ベースと高さがずれるため、ここで実ベース位置へ寄せる。
        Vector3 originPos = target != null ? target.GetRobotOriginWorldPosition() : baseT.position;
        container.transform.localRotation = calInv;
        container.transform.localPosition = baseT.InverseTransformPoint(originPos) + calInv * offset;
        // 座標合わせ診断。base/origin の位置・姿勢と各ゾーンの world 中心を出す。
        Debug.Log($"[SafetyZone] base '{baseT.name}' basePos={baseT.position.ToString("F3")} " +
                  $"originPos={originPos.ToString("F3")} euler={baseT.eulerAngles.ToString("F1")} calEuler={calEuler.ToString("F1")}");

        // 単位: "mm"(既定) は ×0.001 で m へ。
        float sc = (string.IsNullOrEmpty(setting.unit) || setting.unit.ToLowerInvariant() == "mm") ? 0.001f : 1f;

        if (setting.zones == null)
        {
            return;
        }
        foreach (var z in setting.zones)
        {
            if (z == null || !z.enabled) { continue; }
            if (z.min == null || z.max == null || z.min.Count < 3 || z.max.Count < 3) { continue; }

            // ROS/base フレーム(単位補正後) の中心・寸法。
            Vector3 cR = new Vector3(z.min[0] + z.max[0], z.min[1] + z.max[1], z.min[2] + z.max[2]) * (0.5f * sc);
            Vector3 sR = new Vector3(Mathf.Abs(z.max[0] - z.min[0]), Mathf.Abs(z.max[1] - z.min[1]), Mathf.Abs(z.max[2] - z.min[2])) * sc;
            // ROS(base) → Unity 逆軸写像: 障害物送信 Unity(x,y,z)→ROS(z,-x,y) の逆 = Unity(-ry, rz, rx)。
            Vector3 cU = new Vector3(-cR.y, cR.z, cR.x);
            Vector3 sU = new Vector3(sR.y, sR.z, sR.x);

            bool danger = !z.insideAllowed;                          // 内側禁止=赤 / 内側安全=緑
            Color fill = danger ? new Color(1f, 0.15f, 0.1f, 0.20f) : new Color(0.2f, 1f, 0.35f, 0.15f);
            Color line = danger ? new Color(1f, 0.3f, 0.2f, 0.9f) : new Color(0.3f, 1f, 0.5f, 0.9f);
            BuildZoneBox(string.IsNullOrEmpty(z.id) ? "zone" : z.id, cU, sU, fill, line);
        }
    }

    /// <summary>1ゾーン＝半透明の塗り Cube ＋ 枠(LineRenderer)。</summary>
    private void BuildZoneBox(string id, Vector3 centerLocal, Vector3 sizeLocal, Color fill, Color line)
    {
        var root = new GameObject("Zone_" + id);
        root.transform.SetParent(container.transform, false);
        root.transform.localPosition = centerLocal;
        root.transform.localRotation = Quaternion.identity;
        Debug.Log($"[SafetyZone] {id} localCenter(P)={centerLocal.ToString("F3")} size={sizeLocal.ToString("F3")} " +
                  $"-> worldCenter={root.transform.position.ToString("F3")}");

        // 塗り（半透明 Cube）。DCSは「実際に入ってはいけない領域」なので Collider を残し、
        // isTrigger=false（＝物理的な当たり体積。すり抜け検知ではなく実体の no-go ゾーン）にする。
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Fill";
        var col = cube.GetComponent<Collider>();
        if (col != null) { col.isTrigger = false; }
        cube.transform.SetParent(root.transform, false);
        cube.transform.localScale = sizeLocal;
        var rend = cube.GetComponent<Renderer>();
        if (rend != null) { rend.sharedMaterial = MakeZoneMaterial(fill); }

        // 枠（ワイヤフレーム）: 箱の12辺を1本のLineRendererで（重複3辺含む16点）。
        var lrGo = new GameObject("Wire");
        lrGo.transform.SetParent(root.transform, false);
        var lr = lrGo.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = false;
        lr.widthMultiplier = 0.004f;
        lr.numCornerVertices = 0;
        lr.numCapVertices = 0;
        var lineMat = MakeLineMaterial(line);
        if (lineMat != null) { lr.sharedMaterial = lineMat; lr.startColor = line; lr.endColor = line; }
        Vector3 h = sizeLocal * 0.5f;
        // 8隅（±h）。0-3=下面(y-)、4-7=上面(y+)。
        Vector3[] c =
        {
            new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, -h.y, h.z), new Vector3(-h.x, -h.y, h.z),
            new Vector3(-h.x,  h.y, -h.z), new Vector3(h.x,  h.y, -h.z), new Vector3(h.x,  h.y, h.z), new Vector3(-h.x,  h.y, h.z),
        };
        int[] path = { 0, 1, 2, 3, 0, 4, 5, 1, 5, 6, 2, 6, 7, 3, 7, 4 };   // 全12辺を網羅（3辺重複）
        lr.positionCount = path.Length;
        for (int i = 0; i < path.Length; i++) { lr.SetPosition(i, c[path[i]]); }
    }

    /// <summary>指定した List(度/mでも同様) を Vector3 に。null/短ければ既定。</summary>
    private static Vector3 ToVec(List<float> v, Vector3 fallback)
    {
        if (v == null || v.Count < 3) { return fallback; }
        return new Vector3(v[0], v[1], v[2]);
    }

    /// <summary>半透明の塗り用 URP マテリアル（CRX ゴーストと同方式）。</summary>
    private static Material MakeZoneMaterial(Color col)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) { sh = Shader.Find("Universal Render Pipeline/Unlit"); }
        if (sh == null) { sh = Shader.Find("Sprites/Default"); }
        if (sh == null) { return null; }
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) { m.SetColor("_BaseColor", col); }
        if (m.HasProperty("_Color")) { m.SetColor("_Color", col); }
        if (m.HasProperty("_Surface")) { m.SetFloat("_Surface", 1f); }   // Transparent
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_SURFACE_TYPE_OPAQUE");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return m;
    }

    /// <summary>線用の単色 URP マテリアル。</summary>
    private static Material MakeLineMaterial(Color col)
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) { sh = Shader.Find("Sprites/Default"); }
        if (sh == null) { return null; }
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) { m.SetColor("_BaseColor", col); }
        if (m.HasProperty("_Color")) { m.SetColor("_Color", col); }
        return m;
    }
}
