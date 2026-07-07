using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;

public class CRX_30iA: Kinematics6D
{
    #region 変数
    [SerializeField]
    protected List<float> angle;

    protected GameObject crx;

    protected Transform arm1;
    protected Transform arm2;
    protected Transform arm3;
    protected Transform arm4;
    protected Transform arm5;
    protected Transform arm6;

    private Vector3 ang1;
    /*
    private Vector3 ang2;
    private Vector3 ang3;
    private Vector3 ang4;
    private Vector3 ang5;
    private Vector3 ang6;
    */

    protected int axisType = 0;

    #endregion 変数

    protected override void Start()
    {
        base.Start();
    }

    /// <summary>
    /// パラメータ更新
    /// </summary>
    protected override void RenewParameter()
    {
        if (isChgPrm)
        {
            isChgPrm = false;
        }
    }

    /// <summary>
    /// 目標位置セット
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    public override void SetTarget(float x, float y, float z, float rx, float ry, float rz)
    {
        arm1.localEulerAngles = new Vector3(ang1.x, 0, x);
        arm2.localEulerAngles = new Vector3(0, -y, 0);
        // 3軸目はデータソースで規約が異なる:
        //  ・実機(OPC UA/Postgres): 3軸目の値は J2 と連成した値のため arm3 は y + z が必要。
        //  ・ROS(/kmx): 純粋な関節角(J3)を送るため arm3 は z のみが正しい。
        arm3.localEulerAngles = new Vector3(0, GlobalScript.useRos2 ? z : (y + z), 0);
        arm4.localEulerAngles = new Vector3(rx, 0, 0);
        arm5.localEulerAngles = new Vector3(0, ry, 0);
        arm6.localEulerAngles = new Vector3(rz, 0, 0);
    }

    /// <summary>
    /// 経路プレビュー用: 関節角セット列(度・J1..J6)で先端(ツール/フランジ)の世界位置列を返す。
    /// 現在の腕姿勢を保存→各点で SetTarget→先端 world 位置を記録→復元（1コール完結でロボは動いて見えない）。
    /// </summary>
    public override void SampleTipWorld(IReadOnlyList<double[]> jointsDeg, List<Vector3> outWorld)
    {
        outWorld.Clear();
        if (jointsDeg == null || arm1 == null || arm2 == null || arm3 == null
            || arm4 == null || arm5 == null || arm6 == null)
        {
            return;
        }
        var tip = HeadObject != null ? HeadObject.transform : arm6;
        // 現在姿勢を保存
        Vector3 s1 = arm1.localEulerAngles, s2 = arm2.localEulerAngles, s3 = arm3.localEulerAngles;
        Vector3 s4 = arm4.localEulerAngles, s5 = arm5.localEulerAngles, s6 = arm6.localEulerAngles;
        foreach (var j in jointsDeg)
        {
            if (j == null || j.Length < 6)
            {
                continue;
            }
            // SetTarget は useRos2 のとき arm3=z（純粋関節角）＝ROS軌道の規約と一致。
            SetTarget((float)j[0], (float)j[1], (float)j[2], (float)j[3], (float)j[4], (float)j[5]);
            outWorld.Add(tip.position);   // 子の .position 取得で world 行列は即再計算される
        }
        // 復元
        arm1.localEulerAngles = s1; arm2.localEulerAngles = s2; arm3.localEulerAngles = s3;
        arm4.localEulerAngles = s4; arm5.localEulerAngles = s5; arm6.localEulerAngles = s6;
    }

    // --- 経路プレビュー用ゴースト（半透明複製） ---
    private GameObject ghost;
    private Transform g1, g2, g3, g4, g5, g6;
    private float ghostAng1x;

    /// <summary>与えた腕Transform群を J1..J6(度) の姿勢にする（SetTarget と同じ式）。ゴースト共用。</summary>
    public static void ApplyArmPose(Transform a1, Transform a2, Transform a3, Transform a4, Transform a5, Transform a6,
                                    float ang1x, double[] j, bool ros2)
    {
        if (j == null || j.Length < 6)
        {
            return;
        }
        if (a1) { a1.localEulerAngles = new Vector3(ang1x, 0f, (float)j[0]); }
        if (a2) { a2.localEulerAngles = new Vector3(0f, -(float)j[1], 0f); }
        // 3軸目は arm3=z(ROS純粋角) / y+z(実機連成) を SetTarget と同じく切替。
        if (a3) { a3.localEulerAngles = new Vector3(0f, ros2 ? (float)j[2] : (float)(j[1] + j[2]), 0f); }
        if (a4) { a4.localEulerAngles = new Vector3((float)j[3], 0f, 0f); }
        if (a5) { a5.localEulerAngles = new Vector3(0f, (float)j[4], 0f); }
        if (a6) { a6.localEulerAngles = new Vector3((float)j[5], 0f, 0f); }
    }

    public override GameObject CreateGhost()
    {
        DestroyGhost();
        if (crx == null)
        {
            return null;
        }
        ghost = Instantiate(crx, crx.transform.parent);
        ghost.name = "CRX-30iA_Ghost";
        ghost.SetActive(false);   // 複製の Start を走らせない（Kinematics 等が動かないよう）
        // コライダー/スクリプトを除去（複製は表示専用）。
        foreach (var col in ghost.GetComponentsInChildren<Collider>(true))
        {
            Destroy(col);
        }
        foreach (var mb in ghost.GetComponentsInChildren<MonoBehaviour>(true))
        {
            Destroy(mb);
        }
        // 半透明マテリアルに差し替え。
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
        // 腕参照（複製内を名前で探索・本体と同じ規約）。
        var ch = ghost.GetComponentsInChildren<Transform>(true).ToList();
        g1 = ch.Find(d => d.name.Contains("J2BASE"));
        g2 = ch.Find(d => d.name.Contains("J2ARM"));
        g3 = ch.Find(d => d.name.Contains("J3CASING"));
        g4 = ch.Find(d => d.name.Contains("J3ARM"));
        g5 = ch.Find(d => d.name.Contains("J6CASING"));
        g6 = ch.Find(d => d.name.Contains("J6FLANGE"));
        ghostAng1x = g1 != null ? g1.localEulerAngles.x : 0f;
        ghost.SetActive(true);
        return ghost;
    }

    public override void PoseGhostDeg(double[] j16)
    {
        if (ghost == null)
        {
            return;
        }
        ApplyArmPose(g1, g2, g3, g4, g5, g6, ghostAng1x, j16, GlobalScript.useRos2);
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

    // --- IRos2PlanTarget（機種固有の実装） ---
    /// <summary>機種キー（robot_id=crx30ia_N の生成・機種別既定の索引）。</summary>
    public override string ModelKey => "crx30ia";

    /// <summary>現在の腕姿勢(localEulerAngles)から J1..J6(度)を逆算（SetTarget/ApplyArmPose の逆写像）。</summary>
    public override double[] GetCurrentJointsDeg()
    {
        var j = new double[6];
        if (arm1 == null || arm2 == null || arm3 == null || arm4 == null || arm5 == null || arm6 == null)
        {
            return j;   // 未構築時はゼロ
        }
        float j1 = Mathf.DeltaAngle(0f, arm1.localEulerAngles.z);   // arm1.z = J1
        float j2 = -Mathf.DeltaAngle(0f, arm2.localEulerAngles.y);  // arm2.y = -J2
        float a3y = Mathf.DeltaAngle(0f, arm3.localEulerAngles.y);  // arm3.y = ros2? J3 : (J2+J3)
        float j3 = GlobalScript.useRos2 ? a3y : (a3y - j2);
        float j4 = Mathf.DeltaAngle(0f, arm4.localEulerAngles.x);   // arm4.x = J4
        float j5 = Mathf.DeltaAngle(0f, arm5.localEulerAngles.y);   // arm5.y = J5
        float j6 = Mathf.DeltaAngle(0f, arm6.localEulerAngles.x);   // arm6.x = J6
        j[0] = j1; j[1] = j2; j[2] = j3; j[3] = j4; j[4] = j5; j[5] = j6;
        return j;
    }

    /// <summary>障害物 base フレーム＝ModelRestructProcess で作る crx ルート。</summary>
    public override Transform GetBaseTransform()
    {
        return crx != null ? crx.transform : null;
    }

    /// <summary>ゴースト用の半透明マテリアル（URP。取れなければ null）。</summary>
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
        // URP transparent 設定（Surface=Transparent / alpha blend / ZWrite off）。
        if (m.HasProperty("_Surface")) { m.SetFloat("_Surface", 1f); }
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_SURFACE_TYPE_OPAQUE");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return m;
    }

    /// <summary>
    /// モデル再構築
    /// </summary>
    /// <param name="instance"></param>
    protected override void ModelRestructProcess()
    {
        base.ModelRestructProcess();

        crx = new GameObject("CRX-30iA");
        crx.transform.parent = unitSetting.moveObject.transform;
        crx.transform.localPosition = Vector3.zero;
        crx.transform.localEulerAngles = Vector3.zero;

        var children = unitSetting.moveObject.GetComponentsInChildren<Transform>().ToList();

        // アーム1 Y軸
        arm1 = children.Find(d => d.name.Contains("J2BASE"));
        // アーム2 Y軸
        arm2 = children.Find(d => d.name.Contains("J2ARM"));
        // アーム3 Y軸
        arm3 = children.Find(d => d.name.Contains("J3CASING"));
        // アーム4 X軸
        arm4 = children.Find(d => d.name.Contains("J3ARM"));
        // アーム5 Y軸
        arm5 = children.Find(d => d.name.Contains("J6CASING"));
        // アーム6 X軸
        arm6 = children.Find(d => d.name.Contains("J6FLANGE"));

        // 親子関係セット
        arm1.parent = crx.transform;
        arm2.parent = arm1;
        arm3.parent = arm2;
        arm4.parent = arm3;
        arm5.parent = arm4;
        arm6.parent = arm5;

        // 初期角度セット
        ang1 = arm1.localEulerAngles;
        /*
        ang2 = arm2.localEulerAngles;
        ang3 = arm3.localEulerAngles;
        ang4 = arm4.localEulerAngles;
        ang5 = arm5.localEulerAngles;
        ang6 = arm6.localEulerAngles;
        */

        // ヘッドセット
        if (HeadObject != null)
        {
            HeadObject.transform.parent = arm6.transform;
        }
    }
}
