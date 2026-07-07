using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// KAWASAKI RS007L（6軸）。CRX-30iA と同じ Kinematics6D 方式で実装し IRos2PlanTarget（経路計画対象）になる。
/// 関節規約（従来 Robo6Axis と同じ）：J1=ベース直下の Y 軸、J2..J6 = 各 _Jn の X 軸（CRX のような J3 連成は無し）。
/// arm 階層名：ベース / _J1 .. _J6。
///
/// ※ Phase2 時点ではこのクラスは compile 検証のみ（RS007L の Kawasaki モデル/ユニット設定/シーン配置が
///   本リポに無いため実機未検証）。GetRobotType の RS007L 検出・ParameterLoader 分岐・Ros2Info の
///   robots/タグ整備が揃ったら実機検証する（MULTI_ROBOT_ROS2_SPEC.md）。
/// </summary>
public class RS007L : Kinematics6D
{
    #region 変数
    protected Transform j1;
    protected Transform j2;
    protected Transform j3;
    protected Transform j4;
    protected Transform j5;
    protected Transform j6;
    #endregion 変数

    /// <summary>
    /// モデル再構築：ベース/_J1.._J6 を取得し親子チェーンを組む。ヘッドは J6 直下へ。
    /// </summary>
    protected override void ModelRestructProcess()
    {
        base.ModelRestructProcess();

        var children = unitSetting.moveObject.GetComponentsInChildren<Transform>().ToList();
        baseObject = children.FirstOrDefault(d => d.name.Contains("ベース"))?.gameObject;
        j1 = children.FirstOrDefault(d => d.name.Contains("_J1"));
        j2 = children.FirstOrDefault(d => d.name.Contains("_J2"));
        j3 = children.FirstOrDefault(d => d.name.Contains("_J3"));
        j4 = children.FirstOrDefault(d => d.name.Contains("_J4"));
        j5 = children.FirstOrDefault(d => d.name.Contains("_J5"));
        j6 = children.FirstOrDefault(d => d.name.Contains("_J6"));

        // 親子関係（先端→根の順に親をたどれるよう）。
        if (j6 != null) { j6.parent = j5; }
        if (j5 != null) { j5.parent = j4; }
        if (j4 != null) { j4.parent = j3; }
        if (j3 != null) { j3.parent = j2; }
        if (j2 != null) { j2.parent = j1; }
        if (j1 != null && baseObject != null) { j1.parent = baseObject.transform; }

        if (HeadObject != null && j6 != null)
        {
            HeadObject.transform.parent = j6;
        }
    }

    /// <summary>
    /// 目標姿勢セット。引数は J1..J6(度)。J1=Y軸、J2..J6=X軸（Robo6Axis と同じ規約）。
    /// </summary>
    public override void SetTarget(float x, float y, float z, float rx, float ry, float rz)
    {
        if (j1) { j1.localEulerAngles = new Vector3(0f, x, 0f); }    // J1
        if (j2) { j2.localEulerAngles = new Vector3(y, 0f, 0f); }    // J2
        if (j3) { j3.localEulerAngles = new Vector3(z, 0f, 0f); }    // J3
        if (j4) { j4.localEulerAngles = new Vector3(rx, 0f, 0f); }   // J4
        if (j5) { j5.localEulerAngles = new Vector3(ry, 0f, 0f); }   // J5
        if (j6) { j6.localEulerAngles = new Vector3(rz, 0f, 0f); }   // J6
    }

    /// <summary>
    /// 駆動：手動(ROS2ゴール/ゴースト)は setTarget、通常は robo.tags があれば基底の駆動を使う。
    /// robo 未設定（RS007L のユニット設定未整備）でも NRE しないようガード。
    /// </summary>
    protected override void MyFixedUpdate()
    {
        if (isManual)
        {
            setTarget(target, rotate);
            return;
        }
        if (robo != null && robo.tags != null && robo.tags.Count >= 6)
        {
            base.MyFixedUpdate();   // Kinematics6D の robo.tags 駆動を流用
        }
        // robo 未設定時は何もしない（実タグ駆動はデータ整備後）。
    }

    // --- IRos2PlanTarget（機種固有の実装） ---
    /// <summary>機種キー（robot_id=rs007l_N の生成・機種別既定の索引）。</summary>
    public override string ModelKey => "rs007l";

    /// <summary>与えた arm 群を J1..J6(度) 姿勢にする（SetTarget と同じ式）。ゴースト共用。</summary>
    private static void ApplyArmPose(Transform a1, Transform a2, Transform a3, Transform a4, Transform a5, Transform a6, double[] j)
    {
        if (j == null || j.Length < 6)
        {
            return;
        }
        if (a1) { a1.localEulerAngles = new Vector3(0f, (float)j[0], 0f); }
        if (a2) { a2.localEulerAngles = new Vector3((float)j[1], 0f, 0f); }
        if (a3) { a3.localEulerAngles = new Vector3((float)j[2], 0f, 0f); }
        if (a4) { a4.localEulerAngles = new Vector3((float)j[3], 0f, 0f); }
        if (a5) { a5.localEulerAngles = new Vector3((float)j[4], 0f, 0f); }
        if (a6) { a6.localEulerAngles = new Vector3((float)j[5], 0f, 0f); }
    }

    /// <summary>現在の arm 姿勢から J1..J6(度)を逆算（SetTarget の逆写像）。</summary>
    public override double[] GetCurrentJointsDeg()
    {
        var v = new double[6];
        if (j1 == null || j2 == null || j3 == null || j4 == null || j5 == null || j6 == null)
        {
            return v;
        }
        v[0] = Mathf.DeltaAngle(0f, j1.localEulerAngles.y);
        v[1] = Mathf.DeltaAngle(0f, j2.localEulerAngles.x);
        v[2] = Mathf.DeltaAngle(0f, j3.localEulerAngles.x);
        v[3] = Mathf.DeltaAngle(0f, j4.localEulerAngles.x);
        v[4] = Mathf.DeltaAngle(0f, j5.localEulerAngles.x);
        v[5] = Mathf.DeltaAngle(0f, j6.localEulerAngles.x);
        return v;
    }

    /// <summary>障害物 base フレーム＝ベース。</summary>
    public override Transform GetBaseTransform()
    {
        return baseObject != null ? baseObject.transform : null;
    }

    /// <summary>経路プレビュー用：関節角セット列で先端(ヘッド/ J6)の世界位置列を返す。現在姿勢は保存→復元。</summary>
    public override void SampleTipWorld(IReadOnlyList<double[]> jointsDeg, List<Vector3> outWorld)
    {
        outWorld.Clear();
        if (jointsDeg == null || j1 == null || j2 == null || j3 == null || j4 == null || j5 == null || j6 == null)
        {
            return;
        }
        var tip = HeadObject != null ? HeadObject.transform : j6;
        Vector3 s1 = j1.localEulerAngles, s2 = j2.localEulerAngles, s3 = j3.localEulerAngles;
        Vector3 s4 = j4.localEulerAngles, s5 = j5.localEulerAngles, s6 = j6.localEulerAngles;
        foreach (var j in jointsDeg)
        {
            if (j == null || j.Length < 6)
            {
                continue;
            }
            ApplyArmPose(j1, j2, j3, j4, j5, j6, j);
            outWorld.Add(tip.position);
        }
        j1.localEulerAngles = s1; j2.localEulerAngles = s2; j3.localEulerAngles = s3;
        j4.localEulerAngles = s4; j5.localEulerAngles = s5; j6.localEulerAngles = s6;
    }

    // --- 経路プレビュー用ゴースト（半透明複製） ---
    private GameObject ghost;
    private Transform g1, g2, g3, g4, g5, g6;

    public override GameObject CreateGhost()
    {
        DestroyGhost();
        if (baseObject == null)
        {
            return null;
        }
        ghost = Instantiate(baseObject, baseObject.transform.parent);
        ghost.name = "RS007L_Ghost";
        ghost.SetActive(false);
        foreach (var col in ghost.GetComponentsInChildren<Collider>(true)) { Destroy(col); }
        foreach (var mb in ghost.GetComponentsInChildren<MonoBehaviour>(true)) { Destroy(mb); }
        var mat = MakeGhostMaterial();
        if (mat != null)
        {
            foreach (var r in ghost.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) { mats[i] = mat; }
                r.sharedMaterials = mats;
            }
        }
        var ch = ghost.GetComponentsInChildren<Transform>(true).ToList();
        g1 = ch.Find(d => d.name.Contains("_J1"));
        g2 = ch.Find(d => d.name.Contains("_J2"));
        g3 = ch.Find(d => d.name.Contains("_J3"));
        g4 = ch.Find(d => d.name.Contains("_J4"));
        g5 = ch.Find(d => d.name.Contains("_J5"));
        g6 = ch.Find(d => d.name.Contains("_J6"));
        ghost.SetActive(true);
        return ghost;
    }

    public override void PoseGhostDeg(double[] j16)
    {
        if (ghost == null)
        {
            return;
        }
        ApplyArmPose(g1, g2, g3, g4, g5, g6, j16);
    }

    public override void DestroyGhost()
    {
        if (ghost != null)
        {
            Destroy(ghost);
            ghost = null;
        }
        g1 = g2 = g3 = g4 = g5 = g6 = null;
    }

    /// <summary>ゴースト用の半透明マテリアル（URP。取れなければ null）。CRX-30iA と同じ設定。</summary>
    private static Material MakeGhostMaterial()
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) { sh = Shader.Find("Universal Render Pipeline/Unlit"); }
        if (sh == null) { sh = Shader.Find("Sprites/Default"); }
        if (sh == null) { return null; }
        var m = new Material(sh);
        var col = new Color(0.15f, 0.8f, 1f, 0.35f);
        if (m.HasProperty("_BaseColor")) { m.SetColor("_BaseColor", col); }
        if (m.HasProperty("_Color")) { m.SetColor("_Color", col); }
        if (m.HasProperty("_Surface")) { m.SetFloat("_Surface", 1f); }
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_SURFACE_TYPE_OPAQUE");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return m;
    }
}
