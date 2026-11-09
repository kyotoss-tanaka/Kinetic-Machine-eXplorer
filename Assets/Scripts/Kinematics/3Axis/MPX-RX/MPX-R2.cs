using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// MPX-R2（2軸）。MPX-R3 と違いヘッドを回す3軸目が無く、自己保持の三角プレート（フィン）と平行リンクでヘッドの向きを保つ。
/// 部品と動き（S0730 S1_完成品移載 のモデルで確認。回転軸はすべて同じ向き）：
///   アーム1   … 肩を中心に回る第一アーム（1軸目）。肘までの長さ r1
///   アーム3   … 肩を中心に回る二軸レバー（2軸目）。アーム2 と常に平行
///   アーム4   … アーム3 の先とアーム2 の後ろをつなぐリンク。アーム1 と常に平行（平行四辺形）
///   アーム2   … 肘を中心に回る第二アーム。ヘッドまでの長さ r2。向きはアーム3 と同じ
///   フィン    … 肘に付く三角プレート。向きを変えずに肘と一緒に動く
///   リンク1   … 原動部とフィンをつなぐ（アーム1 と平行）。フィンの向きを保つ
///   リンク2   … フィンとヘッドをつなぐ（アーム2 と平行）。ヘッドの向きを保つ
///   プレート  … アーム2 の先（ヘッド）。向きを変えずに動く。見つからなければ登録したヘッドのユニットをプレート兼ヘッドとして使う
/// 角度の決め打ちの式ではなく、モデルの初期の姿勢から、各部品を「どの点を中心に・どれだけ回して・どれだけ動かすか」で置く。
/// （CAD で Z 軸が裏返しの部品（リンク1-2 等）があっても同じに動く）
/// 目標：X＝前後（肩からの水平の距離。肘のある側が +）、Y＝上下（肩からの下向きの距離。下が +）、mm。ヘッドの回転（Z）は無い
/// </summary>
public class MPX_R2 : MPX_RX
{
    #region 変数
    private Transform arm1;
    private Transform arm2;
    private Transform arm3;
    private Transform arm4;
    private Transform fin;
    private Transform plate;
    private readonly List<Transform> link1 = new();
    private readonly List<Transform> link2 = new();

    /// <summary>部品の初期の姿勢（mpx の座標）</summary>
    private readonly Dictionary<Transform, (Vector3 pos, Quaternion rot)> init = new();

    /// <summary>肩・肘・ヘッド（プレートの原点）・アーム4 の原点の初期の位置（mpx の座標）</summary>
    private Vector3 S, E0, H0, A40;
    /// <summary>アーム4 の原点がアーム3 の側（true）か、アーム2 の側（false）か</summary>
    private bool arm4OnLever = true;

    /// <summary>回転軸・目標の X の + 方向・Y の + 方向（下）（mpx の座標）</summary>
    private Vector3 axis, xDir, downDir;
    /// <summary>平面の角度（xDir から downDir へ回る向きが +）を、回転軸まわりの回転に直す時の符号</summary>
    private float rotSign = 1f;
    /// <summary>mpx の座標 1 の長さが何 mm か</summary>
    private float mmPerUnit = 1000f;

    /// <summary>初期の アーム1・アーム2 の向き（平面の角度・度）と、逆解の肘の側（+1/-1）</summary>
    private float alpha0, beta0, elbow = 1f;

    /// <summary>アームが揃って組み立てられたか（揃わない時は動かさない）</summary>
    private bool isAssembled = false;

    // つながる点（モデルの初期姿勢でつながっている点を、両側の部品の座標で覚える）
    private struct LinkCheck
    {
        public string name;
        public Transform a;
        public Vector3 aLocal;
        public Transform b;
        public Vector3 bLocal;
    }
    private readonly List<LinkCheck> linkChecks = new();
    private Vector3 lastCheckTarget = new Vector3(float.NaN, 0, 0);
    private float nextCheckTime;
    #endregion 変数

    #region 平面の計算
    /// <summary>平面の角度（度）の向き（mpx の座標）</summary>
    private Vector3 Dir(float deg)
    {
        var r = deg * Mathf.Deg2Rad;
        return Mathf.Cos(r) * xDir + Mathf.Sin(r) * downDir;
    }

    /// <summary>ベクトルの平面の角度（度）</summary>
    private float Angle(Vector3 v)
    {
        return Mathf.Atan2(Vector3.Dot(v, downDir), Vector3.Dot(v, xDir)) * Mathf.Rad2Deg;
    }

    /// <summary>平面の角度で delta 度回す回転（mpx の座標）</summary>
    private Quaternion Rot(float delta)
    {
        return Quaternion.AngleAxis(rotSign * delta, axis);
    }

    /// <summary>
    /// 逆解：ヘッドの位置（肩からの X・Y mm）→ アーム1・アーム2 の平面の角度。届かない時は false
    /// </summary>
    private bool Solve(float x, float y, float side, out float alpha, out float beta)
    {
        alpha = beta = 0f;
        var d = Mathf.Sqrt(x * x + y * y);
        if (d < 1f)
        {
            return false;
        }
        // 届く範囲に収める（伸び切り・畳み切りの少し手前）
        d = Mathf.Clamp(d, Mathf.Abs(r1 - r2) + 0.01f, r1 + r2 - 0.01f);
        var g = Mathf.Atan2(y, x);
        var c = Mathf.Clamp((r1 * r1 + d * d - r2 * r2) / (2f * r1 * d), -1f, 1f);
        var a = g + side * Mathf.Acos(c);
        var ex = r1 * Mathf.Cos(a);
        var ey = r1 * Mathf.Sin(a);
        alpha = a * Mathf.Rad2Deg;
        beta = Mathf.Atan2(y - ey, x - ex) * Mathf.Rad2Deg;
        return !(float.IsNaN(alpha) || float.IsNaN(beta));
    }
    #endregion

    /// <summary>
    /// モデルの初期姿勢の目標（ヘッドの位置）
    /// </summary>
    protected override bool TryModelPoseTarget(out Vector3 raw)
    {
        raw = Vector3.zero;
        if (!isAssembled)
        {
            return false;
        }
        var h = (H0 - S) * mmPerUnit;
        raw = new Vector3(Vector3.Dot(h, xDir), Vector3.Dot(h, downDir), 0f);
        return true;
    }

    /// <summary>
    /// 目標位置セット（X＝前後・Y＝上下 mm。Z は無い）
    /// </summary>
    public override void SetTarget(float x, float y, float z)
    {
        if (!isAssembled)
        {
            return;
        }
        if (!Solve(x, y, elbow, out var alpha, out var beta))
        {
            // 肩の位置（0,0）等は解けないので、今の姿勢のまま
            return;
        }
        angle = new List<float> { alpha, beta, 0f };
        Apply(alpha, beta);
        LogLinkGaps(new Vector3(x, y, z));
    }

    /// <summary>
    /// アーム1・アーム2 の平面の角度で各部品を置く
    /// </summary>
    private void Apply(float alpha, float beta)
    {
        var ra = Rot(Mathf.DeltaAngle(alpha0, alpha));
        var rb = Rot(Mathf.DeltaAngle(beta0, beta));
        var e = S + ra * (E0 - S);
        var h = e + rb * (H0 - E0);
        var finMove = e - E0;

        // 肩を中心に回る
        Place(arm1, S, S, ra);
        Place(arm3, S, S, rb);
        // 肘を中心に回る・肘と一緒に動く
        Place(arm2, E0, e, rb);
        Place(fin, E0, e, Quaternion.identity);
        // アーム4（アーム1 と平行）：アーム3 の側の端はアーム3 と、アーム2 の側の端はアーム2 と一緒に動く
        var a4 = arm4OnLever ? S + rb * (A40 - S) : e + rb * (A40 - E0);
        Place(arm4, A40, a4, ra);
        // リンク1（アーム1 と平行）：フィンの側の端はフィンと一緒に、原動部の側の端は動かない
        foreach (var l in link1)
        {
            var o = init[l].pos;
            Place(l, o, IsNearer(o, E0, S) ? o + finMove : o, ra);
        }
        // リンク2（アーム2 と平行）：フィンの側の端はフィンと、ヘッドの側の端はヘッドと一緒に動く
        foreach (var l in link2)
        {
            var o = init[l].pos;
            Place(l, o, IsNearer(o, E0, H0) ? o + finMove : o + (h - H0), rb);
        }
        // ヘッド（プレート）：向きを変えずに動く
        Place(plate, H0, h, Quaternion.identity);
    }

    /// <summary>
    /// 部品を「初期の pivot0 の点が pivot に来て、rot だけ回った」姿勢に置く（mpx の座標）
    /// </summary>
    private void Place(Transform t, Vector3 pivot0, Vector3 pivot, Quaternion rot)
    {
        if ((t == null) || !init.TryGetValue(t, out var p))
        {
            return;
        }
        t.localPosition = pivot + rot * (p.pos - pivot0);
        t.localRotation = rot * p.rot;
    }

    private static bool IsNearer(Vector3 p, Vector3 a, Vector3 b)
    {
        return (p - a).sqrMagnitude < (p - b).sqrMagnitude;
    }

    /// <summary>
    /// モデル再構築
    /// </summary>
    protected override void ModelRestructProcess()
    {
        var baseObj = base.ModelRestructProcess("MPX-R2");
        axisType = 2;   // ヘッドの回転は無い

        var children = unitSetting.moveObject.GetComponentsInChildren<Transform>().ToList();

        // 各アームはロボットの定義（Datas/Robots/RobotModels.json）の名前で探す。定義に無いアームはここに書いた既定の名前で探す
        var finder = RobotDefinitions.Finder(unitSetting, Parameters.RobotType.MPX_R2, children, HeadObject);
        arm1 = finder.Find("アーム1", "parent:W0883546-")?.transform;
        arm2 = finder.Find("アーム2", "parent:W0250492-")?.transform;
        arm3 = finder.Find("アーム3", "parent:W0883643-")?.transform;
        arm4 = finder.Find("アーム4", "parent:W0250438-")?.transform;
        fin = finder.Find("フィン", "parent:W0250483-")?.transform;
        link1.Clear();
        link1.AddRange(finder.FindAll("リンク1", "parent:W0250456-").Select(d => d.transform));
        link2.Clear();
        link2.AddRange(finder.FindAll("リンク2", "parent:W0250447-").Select(d => d.transform));
        var plateTmp = finder.FindPlate("プレート", HeadObject, "parent:W0250429-");
        finder.Log();

        var missing = new List<string>();
        if (arm1 == null) { missing.Add("アーム1"); }
        if (arm2 == null) { missing.Add("アーム2"); }
        if (arm3 == null) { missing.Add("アーム3"); }
        if (arm4 == null) { missing.Add("アーム4"); }
        if (fin == null) { missing.Add("フィン"); }
        if (link1.Count == 0) { missing.Add("リンク1"); }
        if (link2.Count == 0) { missing.Add("リンク2"); }
        if (missing.Count > 0)
        {
            Debug.LogWarning($"[Robot] {unitSetting.name}: MPX-R2 の部品 {string.Join("・", missing)} が見つからないため、組み立てずに動かしません（Datas/Robots/RobotModels.json で指定できます）");
            return;
        }

        // プレート（アーム2 の先）：ヘッドの中にある時はヘッドごと動かす。ヘッドの外にある時はヘッドをプレートに付ける
        if ((plateTmp != null) && (HeadObject != null) && plateTmp.transform.IsChildOf(HeadObject.transform))
        {
            plate = HeadObject.transform;
        }
        else if (plateTmp != null)
        {
            plate = plateTmp.transform;
            if ((HeadObject != null) && !plate.IsChildOf(HeadObject.transform))
            {
                HeadObject.transform.SetParent(plate, true);
            }
        }
        else if (HeadObject != null)
        {
            plate = HeadObject.transform;
        }
        else
        {
            Debug.LogWarning($"[Robot] {unitSetting.name}: プレートが無く、ヘッドも指定されていないため、ヘッドは動きません（アームは動きます）");
        }

        // 全部品を mpx の下に移し、初期の姿勢を覚える（以降は mpx の座標で置く）
        var root = mpx.transform;
        var parts = new List<Transform> { arm1, arm2, arm3, arm4, fin };
        parts.AddRange(link1);
        parts.AddRange(link2);
        if (plate != null)
        {
            parts.Add(plate);
        }
        init.Clear();
        foreach (var t in parts)
        {
            t.SetParent(root, true);
            init[t] = (t.localPosition, t.localRotation);
        }

        // 肩・肘・ヘッド
        S = init[arm1].pos;
        E0 = init[arm2].pos;
        H0 = plate != null ? init[plate].pos : E0 + (E0 - S);
        A40 = init[arm4].pos;
        mmPerUnit = root.lossyScale.x * 1000f;
        r1 = Vector3.Distance(S, E0) * mmPerUnit;
        r2 = Vector3.Distance(E0, H0) * mmPerUnit;

        // 回転軸：アーム1 の Z 軸（アームの面に垂直でなければ、肩・肘・ヘッドの面の法線）
        axis = root.InverseTransformDirection(arm1.TransformDirection(Vector3.forward)).normalized;
        if (Mathf.Abs(Vector3.Dot(axis, (E0 - S).normalized)) > 0.1f)
        {
            axis = Vector3.Cross(E0 - S, H0 - E0).normalized;
        }
        // 下：ワールドの下をアームの面に写した向き。X：下と回転軸に垂直で、肘のある側が +
        downDir = Vector3.ProjectOnPlane(root.InverseTransformDirection(Vector3.down), axis).normalized;
        if (downDir.sqrMagnitude < 0.5f)
        {
            Debug.LogWarning($"[Robot] {unitSetting.name}: MPX-R2 の回転軸が上下を向いているため、上下の向きを決められません");
            return;
        }
        xDir = Vector3.Cross(axis, downDir).normalized;
        if (Vector3.Dot(E0 - S, xDir) < 0f)
        {
            xDir = -xDir;
        }
        rotSign = Vector3.Dot(Quaternion.AngleAxis(10f, axis) * xDir, downDir) > 0f ? 1f : -1f;

        // アーム4 の原点がどちらの端か：アーム2 の後ろ（肘とヘッドを結ぶ線の上）ならアーム2 の側
        var onArm2Line = Vector3.Cross((A40 - E0).normalized, (H0 - E0).normalized).magnitude < 0.05f;
        arm4OnLever = !onArm2Line;

        // 初期の角度と、逆解の肘の側（初期の姿勢に戻る方）
        alpha0 = Angle(E0 - S);
        beta0 = Angle(H0 - E0);
        var hx = Vector3.Dot(H0 - S, xDir) * mmPerUnit;
        var hy = Vector3.Dot(H0 - S, downDir) * mmPerUnit;
        float Err(float side)
        {
            return Solve(hx, hy, side, out var a, out _) ? Mathf.Abs(Mathf.DeltaAngle(a, alpha0)) : float.MaxValue;
        }
        elbow = Err(1f) <= Err(-1f) ? 1f : -1f;
        isAssembled = true;

        // つながる点（平行四辺形の対になる点）
        linkChecks.Clear();
        void AddCheck(string checkName, Transform a, Transform b, Vector3 local)
        {
            var world = root.TransformPoint(local);
            linkChecks.Add(new LinkCheck { name = checkName, a = a, aLocal = a.InverseTransformPoint(world), b = b, bLocal = b.InverseTransformPoint(world) });
        }
        var a4Far = arm4OnLever ? A40 + (E0 - S) : A40 - (E0 - S);
        AddCheck("アーム4→アーム2", arm4, arm2, arm4OnLever ? a4Far : A40);
        AddCheck("アーム4→アーム3", arm4, arm3, arm4OnLever ? A40 : a4Far);
        foreach (var l in link1)
        {
            var o = init[l].pos;
            var onFin = IsNearer(o, E0, S);
            AddCheck($"{l.name}→フィン", l, fin, onFin ? o : o + (E0 - S));
            AddCheck($"{l.name}→原動部", l, root, onFin ? o - (E0 - S) : o);
        }
        if (plate != null)
        {
            foreach (var l in link2)
            {
                var o = init[l].pos;
                var onFin = IsNearer(o, E0, H0);
                AddCheck($"{l.name}→フィン", l, fin, onFin ? o : o - (H0 - E0));
                AddCheck($"{l.name}→ヘッド", l, plate, onFin ? o + (H0 - E0) : o);
            }
        }
        CheckByTrial(hx, hy);

        Debug.Log($"[Robot] {unitSetting.name}: MPX-R2 r1={r1:0.0}mm r2={r2:0.0}mm 肘の側={(elbow > 0 ? "+" : "-")} " +
            $"アーム4の原点={(arm4OnLever ? "アーム3側" : "アーム2側")} リンク1={link1.Count}本 リンク2={link2.Count}本 プレート={(plate != null ? plate.name : "なし")}");
        Debug.Log($"[Robot] {unitSetting.name}: モデルの初期姿勢の目標 = ({hx:0.0}, {hy:0.0})（X＝前後：肘のある側が +、Y＝上下：下が +。タイムチャートの値と比べて確認）");
    }

    /// <summary>
    /// 組み立て時に試しの姿勢へ動かして、つながる点が離れないか確かめる（最後に初期の姿勢へ戻す）
    /// </summary>
    private void CheckByTrial(float hx, float hy)
    {
        var maxGap = 0f;
        var worst = "";
        foreach (var d in new[] { new Vector2(100f, 0f), new Vector2(-100f, 50f), new Vector2(50f, 150f) })
        {
            if (!Solve(hx + d.x, hy + d.y, elbow, out var a, out var b))
            {
                continue;
            }
            Apply(a, b);
            foreach (var c in linkChecks)
            {
                var g = Gap(c);
                if (g > maxGap)
                {
                    maxGap = g;
                    worst = c.name;
                }
            }
        }
        Apply(alpha0, beta0);
        var text = $"[Robot] {unitSetting.name}: MPX-R2 を仮想で動かして確認：つながる点のずれ 最大 {maxGap:0.0}mm{(worst != "" ? $"（{worst}）" : "")}";
        if (maxGap > 2f)
        {
            Debug.LogWarning(text + " ※部品の割り当てか回転の中心を確認してください");
        }
        else
        {
            Debug.Log(text);
        }
    }

    private float Gap(LinkCheck c)
    {
        return Vector3.Distance(c.a.TransformPoint(c.aLocal), c.b.TransformPoint(c.bLocal)) * 1000f;
    }

    /// <summary>確認用：つながる点のずれ（mm）。目標が変わった時だけ、1秒に1回まで</summary>
    private void LogLinkGaps(Vector3 target)
    {
        if ((linkChecks.Count == 0) || (target == lastCheckTarget) || (Time.unscaledTime < nextCheckTime))
        {
            return;
        }
        lastCheckTarget = target;
        nextCheckTime = Time.unscaledTime + 1f;
        var max = linkChecks.Max(Gap);
        Debug.Log($"[Robot] {unitSetting.name}: 目標({target.x:0.0},{target.y:0.0}) 角度 アーム1={angle[0]:0.0} アーム2={angle[1]:0.0}  つながる点のずれ 最大 {max:0.0}mm");
    }
}
