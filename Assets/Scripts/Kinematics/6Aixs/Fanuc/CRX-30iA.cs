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

    protected Transform org;
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
            outWorld.Add(TcpWorldPos(tip));   // JOGゴールと同じ吸盤点基準（offset=0なら従来一致）。.position取得で world 行列は即再計算
        }
        // 復元
        arm1.localEulerAngles = s1; arm2.localEulerAngles = s2; arm3.localEulerAngles = s3;
        arm4.localEulerAngles = s4; arm5.localEulerAngles = s5; arm6.localEulerAngles = s6;
    }

    // ================= 数値IK(DLS): TCP world 姿勢 → 関節角(度) =================
    // Cartesian JOG 用。SetTarget ベースの非破壊FK＋数値ヤコビアン＋DLS で、CRX の混在軸を自動で扱う。

    private Transform TipTf => HeadObject != null ? HeadObject.transform : arm6;

    /// <summary>現在の TCP(先端) world 姿勢。JOG開始時の基準に使う。</summary>
    public override bool GetTcpPoseWorld(out Vector3 pos, out Quaternion rot)
    {
        var tip = TipTf;
        if (tip == null) { pos = Vector3.zero; rot = Quaternion.identity; return false; }
        pos = TcpWorldPos(tip); rot = tip.rotation; return true;   // ← ヘッドオフセット点(吸盤等)基準
    }

    /// <summary>非破壊FK: q(度・J1..J6) の TCP world 姿勢。呼び出し側で arm euler を save/restore する前提。</summary>
    private void FkPoseNoSave(double[] q, out Vector3 pos, out Quaternion rot)
    {
        SetTarget((float)q[0], (float)q[1], (float)q[2], (float)q[3], (float)q[4], (float)q[5]);
        var tip = TipTf;
        pos = TcpWorldPos(tip); rot = tip.rotation;               // ← ヘッドオフセット点基準
    }

    /// <summary>数値IK(DLS): TCP を world 姿勢 targetPos/targetRot に合わせる関節角を seed から反復で求める。収束すれば true。</summary>
    public override bool TrySolveIkWorld(Vector3 targetPos, Quaternion targetRot, double[] seedDeg, out double[] result)
    {
        result = (seedDeg != null && seedDeg.Length >= 6) ? (double[])seedDeg.Clone() : new double[6];
        if (arm1 == null || arm2 == null || arm3 == null || arm4 == null || arm5 == null || arm6 == null || TipTf == null)
        {
            return false;
        }
        Vector3 s1 = arm1.localEulerAngles, s2 = arm2.localEulerAngles, s3 = arm3.localEulerAngles;
        Vector3 s4 = arm4.localEulerAngles, s5 = arm5.localEulerAngles, s6 = arm6.localEulerAngles;
        double[] q = (double[])result.Clone();
        bool ok = false;
        try
        {
            const int maxIters = 40;
            const float lambda = 0.06f;   // DLS 減衰（特異点近傍で安定）
            const float dJ = 0.5f;        // ヤコビアン数値微分幅(度)
            for (int iter = 0; iter < maxIters; iter++)
            {
                FkPoseNoSave(q, out Vector3 p, out Quaternion r);
                Vector3 pe = targetPos - p;                 // 位置誤差(m)
                Vector3 re = OrientErr(r, targetRot);       // 姿勢誤差(rad・軸×角)
                if (pe.magnitude < 0.0008f && re.magnitude < 0.005f) { ok = true; break; }
                double[] eVec = { pe.x, pe.y, pe.z, re.x, re.y, re.z };
                double[,] J = new double[6, 6];             // 6x6 数値ヤコビアン(単位/radian)
                float dJr = dJ * Mathf.Deg2Rad;             // ★per-radian で計算（DLSの単位整合。度のままだと減衰過大で収束しない）
                for (int c = 0; c < 6; c++)
                {
                    double old = q[c]; q[c] = old + dJ;
                    FkPoseNoSave(q, out Vector3 p2, out Quaternion r2);
                    q[c] = old;
                    Vector3 dp = (p2 - p) / dJr;
                    Vector3 dr = OrientErr(r, r2) / dJr;
                    J[0, c] = dp.x; J[1, c] = dp.y; J[2, c] = dp.z;
                    J[3, c] = dr.x; J[4, c] = dr.y; J[5, c] = dr.z;
                }
                double[] dq = SolveDls6(J, eVec, lambda);    // Δq(rad)
                for (int c = 0; c < 6; c++) { q[c] = ClampJoint(c, q[c] + dq[c] * Mathf.Rad2Deg); }
            }
            FkPoseNoSave(q, out Vector3 pf, out Quaternion rf);
            if (!ok) { ok = (targetPos - pf).magnitude < 0.004f && OrientErr(rf, targetRot).magnitude < 0.02f; }
            result = q;
        }
        finally
        {
            arm1.localEulerAngles = s1; arm2.localEulerAngles = s2; arm3.localEulerAngles = s3;
            arm4.localEulerAngles = s4; arm5.localEulerAngles = s5; arm6.localEulerAngles = s6;
        }
        return ok;
    }

    /// <summary>回転誤差 from→to を「回転軸×角(rad)」の3ベクトルで返す。</summary>
    private static Vector3 OrientErr(Quaternion from, Quaternion to)
    {
        Quaternion d = to * Quaternion.Inverse(from);
        d.ToAngleAxis(out float ang, out Vector3 axis);
        if (float.IsNaN(axis.x) || float.IsInfinity(axis.x)) { return Vector3.zero; }
        if (ang > 180f) { ang -= 360f; }
        if (Mathf.Abs(ang) < 1e-4f) { return Vector3.zero; }
        return axis.normalized * (ang * Mathf.Deg2Rad);
    }

    /// <summary>J1..J6 可動域クランプ（暫定・±値。★実機仕様に合わせて調整）。</summary>
    private static double ClampJoint(int i, double deg)
    {
        double[] lim = { 180, 180, 180, 190, 180, 190 };   // CRX-30iA 概略(暫定)
        double L = lim[Mathf.Clamp(i, 0, 5)];
        return deg > L ? L : (deg < -L ? -L : deg);
    }

    /// <summary>DLS: Δq = Jᵀ (JJᵀ + λ²I)⁻¹ e （6x6）。</summary>
    private static double[] SolveDls6(double[,] J, double[] e, float lambda)
    {
        double[,] A = new double[6, 6];
        for (int i = 0; i < 6; i++)
        {
            for (int k = 0; k < 6; k++)
            {
                double s = 0;
                for (int m = 0; m < 6; m++) { s += J[i, m] * J[k, m]; }
                A[i, k] = s + (i == k ? (double)lambda * lambda : 0);
            }
        }
        double[] y = SolveLinear6(A, e);
        double[] dq = new double[6];
        for (int c = 0; c < 6; c++)
        {
            double s = 0;
            for (int i = 0; i < 6; i++) { s += J[i, c] * y[i]; }
            dq[c] = s;
        }
        return dq;
    }

    /// <summary>6x6 線形方程式 A x = b をガウス消去(部分ピボット)で解く。</summary>
    private static double[] SolveLinear6(double[,] A, double[] b)
    {
        const int n = 6;
        double[,] M = new double[n, n + 1];
        for (int i = 0; i < n; i++) { for (int k = 0; k < n; k++) { M[i, k] = A[i, k]; } M[i, n] = b[i]; }
        for (int col = 0; col < n; col++)
        {
            int piv = col; double best = System.Math.Abs(M[col, col]);
            for (int r = col + 1; r < n; r++) { double v = System.Math.Abs(M[r, col]); if (v > best) { best = v; piv = r; } }
            if (best < 1e-12) { continue; }
            if (piv != col) { for (int k = 0; k <= n; k++) { (M[col, k], M[piv, k]) = (M[piv, k], M[col, k]); } }
            double d = M[col, col];
            for (int k = col; k <= n; k++) { M[col, k] /= d; }
            for (int r = 0; r < n; r++)
            {
                if (r == col) { continue; }
                double f = M[r, col];
                for (int k = col; k <= n; k++) { M[r, k] -= f * M[col, k]; }
            }
        }
        double[] x = new double[n];
        for (int i = 0; i < n; i++) { x[i] = M[i, n]; }
        return x;
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

    /// <summary>DCS/ROBOGUIDE の World 原点(0,0,0)＝**J1回転中心＝J2BASE(arm1) 位置**。J1回転でピボットは不動。</summary>
    public override Vector3 GetRobotOriginWorldPosition()
    {
        if (arm1 != null) { return arm1.position; }        // J2BASE = J1回転中心（DCS World原点）
        return crx != null ? crx.transform.position : Vector3.zero;
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

        // 原点
        org = children.Find(d => d.name.Contains("J1BASE"));
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
        org.parent = crx.transform;
        arm1.parent = org;
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
