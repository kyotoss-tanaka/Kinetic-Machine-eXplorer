using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;

public class MPX_R6 : MPX_RX
{
    #region 変数
    protected GameObject arm1_1;
    protected GameObject arm1_2;
    protected GameObject arm2_1;
    protected GameObject arm2_2;
    protected GameObject arm3;
    protected GameObject arm4;
    protected GameObject plate;

    private Vector3 ang1_1;
    private Vector3 ang1_2;
    private Vector3 ang2_1;
    private Vector3 ang2_2;
    private Vector3 ang3;
    private Vector3 ang4;
    private Vector3 angP;

    private float offset = 45;

    /// <summary>
    /// アームの部品が揃って組み立てられたか（揃わない時は動かさない）
    /// </summary>
    private bool isAssembled = false;
    #endregion 変数

    /// <summary>
    /// 目標位置セット
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    protected override bool TryModelPoseTarget(out Vector3 raw)
    {
        // SetTarget の角度の当て方を逆にたどる（arm1_1=180+a0、arm2_1=-a1、plate=(±90)-head_offset-a2）
        raw = Vector3.zero;
        if ((arm1_1 == null) || (arm1_2 == null) || (arm2_1 == null) || (arm2_2 == null) || (arm3 == null) || (arm4 == null) || (plate == null))
        {
            return false;
        }
        var a0 = Mathf.DeltaAngle(0f, ang1_1.z - 180f);
        var a1 = -Mathf.DeltaAngle(0f, ang2_1.z);
        var a2 = Mathf.DeltaAngle(0f, (isRvs ? -90f : 90f) - head_offset - angP.z);
        raw = ForwardMPX(a0, a1, a2);
        raw.x *= xSign;
        raw.z = FromIkZ(raw.z);
        return true;
    }

    public override void SetTarget(float x, float y, float z)
    {
        base.SetTarget(xSign * x, y, ToIkZ(z));
        if (!isAssembled)
        {
            return;
        }
        ApplyAngles(angle[0], angle[1], angle[2], partSigns);
        LogLinkGaps(new Vector3(x, y, z));
    }

    // ---- 部品の回し方（ArmRobot と同じ方式）----
    // 各部品は「モデルの初期の姿勢からの変化分」だけ、全部品共通の1本の軸（アーム1-1 の回転軸）のまわりに回す。
    // 以前は部品ごとに localEulerAngles.z を決め打ちの式（180・±90・offset 45）で入れていたため、
    // モデルの初期の角度が式と合わないと外れた（R9240 F1：アーム4 の式は -90° だがモデルは 180° で、二軸リンク1 がフィンから外れた）。
    // 向き(partSigns)は組み立て時に仮想で動かして、つながる点が離れない組み合わせを選ぶ（DecidePartSigns）。
    private static readonly string[] PartNames = { "アーム1-1", "アーム1-2", "アーム2-1", "アーム2-2", "アーム3", "アーム4", "プレート" };
    private const int R6PartCount = 7;
    private Transform[] parts;
    private readonly Quaternion[] partInitRot = new Quaternion[R6PartCount];
    private readonly Vector3[] partAxis = new Vector3[R6PartCount];
    private readonly float[] partSigns = { 1, 1, 1, 1, 1, 1, 1 };
    private readonly float[] partDeltas = new float[R6PartCount];
    private float a0Init, a1Init, a2Init;

    /// <summary>逆解の角度の変化分 → 各部品の回転の変化分（従来の式から変化分だけを取り出したもの。並びは PartNames）</summary>
    private static void PartDeltas(float d0, float d1, float d2, float[] o)
    {
        o[0] = d0;              // アーム1-1（従来: 180 + a0）
        o[1] = d0 + d1;         // アーム1-2（従来: a0 + a1 - 180）
        o[2] = -d1;             // アーム2-1 フィン（従来: -a1）
        o[3] = -(d0 + d1);      // アーム2-2（従来: 180 - (a0 + a1)）
        o[4] = -d1;             // アーム3 二軸レバー：フィンと一緒に回る（従来: offset - a1）
        o[5] = d1;              // アーム4 二軸リンク1：向きを保つ＝アーム3 の回転を打ち消す（従来: ±90 - アーム3）
        o[6] = -d2;             // プレート（従来: ±90 - head_offset - a2）
    }

    private void ApplyAngles(float a0, float a1, float a2, float[] signs)
    {
        PartDeltas(Mathf.DeltaAngle(a0Init, a0), Mathf.DeltaAngle(a1Init, a1), Mathf.DeltaAngle(a2Init, a2), partDeltas);
        for (int i = 0; i < R6PartCount; i++)
        {
            if (parts[i] != null)
            {
                parts[i].localRotation = Quaternion.AngleAxis(signs[i] * partDeltas[i], partAxis[i]) * partInitRot[i];
            }
        }
    }

    // ---- つながる点（モデルの初期姿勢でつながっている点を、両側の部品の座標で覚える）----
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

    private void AddCheck(string checkName, Transform a, Transform b, Vector3 world)
    {
        linkChecks.Add(new LinkCheck { name = checkName, a = a, aLocal = a.InverseTransformPoint(world), b = b, bLocal = b.InverseTransformPoint(world) });
    }

    private float Gap(LinkCheck c)
    {
        return Vector3.Distance(c.a.TransformPoint(c.aLocal), c.b.TransformPoint(c.bLocal)) * 1000f;
    }

    /// <summary>
    /// 組み立て時に試しの姿勢へ仮想で動かし、つながる点が離れない向きを選ぶ。
    /// アーム1-1・アーム2-1（逆解の角度そのもの）とプレート（つながる点に関係しない）は +1 に固定し、
    /// 残り4部品の ±1（16通り）を試す。同点なら逆にする部品が少ない方。最後に初期の姿勢へ戻す
    /// </summary>
    private void DecidePartSigns()
    {
        var poses = new[] { new Vector2(a0Init + 10f, a1Init), new Vector2(a0Init, a1Init + 10f), new Vector2(a0Init - 10f, a1Init - 10f) };
        float Score(float[] s, out float maxGap)
        {
            float sum = 0f;
            maxGap = 0f;
            foreach (var p in poses)
            {
                ApplyAngles(p.x, p.y, a2Init, s);
                foreach (var c in linkChecks)
                {
                    var g = Gap(c);
                    sum += g;
                    maxGap = Mathf.Max(maxGap, g);
                }
            }
            return sum;
        }
        int[] free = { 1, 3, 4, 5 };
        var trial = new float[R6PartCount];
        Score(partSigns, out var defaultMax);
        float best = float.MaxValue, bestMax = 0f;
        int bestFlips = int.MaxValue;
        var bestSigns = (float[])partSigns.Clone();
        for (int mask = 0; mask < (1 << free.Length); mask++)
        {
            int flips = 0;
            for (int i = 0; i < R6PartCount; i++) { trial[i] = 1f; }
            for (int k = 0; k < free.Length; k++)
            {
                if ((mask & (1 << k)) != 0) { trial[free[k]] = -1f; flips++; }
            }
            var score = Score(trial, out var max);
            if ((score < best - 0.01f) || ((Mathf.Abs(score - best) <= 0.01f) && (flips < bestFlips)))
            {
                best = score;
                bestMax = max;
                bestFlips = flips;
                trial.CopyTo(bestSigns, 0);
            }
        }
        bestSigns.CopyTo(partSigns, 0);
        ApplyAngles(a0Init, a1Init, a2Init, partSigns);   // 初期の姿勢に戻す

        var reversed = Enumerable.Range(0, R6PartCount).Where(i => partSigns[i] < 0).Select(i => PartNames[i]).ToList();
        var text = $"[Robot] {unitSetting.name}: MPX-R6 の回す向きを仮想で確認：逆向き={(reversed.Count == 0 ? "なし" : string.Join("・", reversed))} " +
            $"つながる点のずれ 最大 {bestMax:0.0}mm（すべて正向きなら最大 {defaultMax:0.0}mm）";
        if (bestMax > 2f)
        {
            Debug.LogWarning(text + " ※どの向きでもずれが残ります（部品の割り当てか回転の中心を確認してください）");
        }
        else
        {
            Debug.Log(text);
        }
    }

    /// <summary>確認用：つながる点が相手の部品からどれだけずれたか（mm）。目標が変わった時だけ、1秒に1回まで</summary>
    private void LogLinkGaps(Vector3 target)
    {
        if ((linkChecks.Count == 0) || (target == lastCheckTarget) || (Time.unscaledTime < nextCheckTime))
        {
            return;
        }
        lastCheckTarget = target;
        nextCheckTime = Time.unscaledTime + 1f;
        var text = linkChecks.Select(c => $"{c.name}={Gap(c):0.0}mm");
        Debug.Log($"[Robot] {unitSetting.name}: 目標({target.x:0.0},{target.y:0.0},{target.z:0.0}) 角度 a0={angle[0]:0.0} a1={angle[1]:0.0} a2={angle[2]:0.0}  ずれ：{string.Join(" / ", text)}");
    }



    /// <summary>
    /// モデル再構築
    /// </summary>
    /// <param name="instance"></param>
    protected override void ModelRestructProcess()
    {
        var baseObj = base.ModelRestructProcess("MPX-R6");

        r1 = 200;
        r2 = 200;

        var children = unitSetting.moveObject.GetComponentsInChildren<Transform>().ToList();

        if (children.Find(d => d.name.Contains("W0578936-")) != null)
        {
            axisType = 3;
            isRvs = false;
        }
        else if (children.Find(d => d.name.Contains("W0652706-")) != null)
        {
            axisType = 3;
            offset = -offset; 
            isRvs = true;
        }

        // 各アームはロボットの定義（Datas/Robots/RobotModels.json）の名前で探す。定義に無いアームはここに書いた既定の名前で探す
        var finder = RobotDefinitions.Finder(unitSetting, Parameters.RobotType.MPX_R6, children, HeadObject);
        arm1_1 = finder.Find("アーム1-1", "parent:W0578802-", "parent:W0652681-");
        arm1_2 = finder.Find("アーム1-2", "parent:W0578972-");
        arm2_1 = finder.Find("アーム2-1", "parent:W0579111-", "parent:W0652733-");   // 三角プレート
        arm2_2 = finder.Find("アーム2-2", "parent:W0578963-", "parent:W0652724-", "parent:W0971610-");   // W0971610 は R8790（減速機・モーターと同じ入れ物）
        arm3 = finder.Find("アーム3", "parent:W0578936-", "parent:W0652706-");
        arm4 = finder.Find("アーム4", "parent:W0578981-");
        // プレート：減速機の回転部
        var plateTmp = finder.Find("プレート", "VRGF-45B60P-8AG8_2^88P3_VRGF-45B60P-8AG8");
        finder.Log();

        // 見つからない部品があれば組み立てない（途中で null に触って例外になり、毎フレーム例外を出していた）
        var missing = new List<string>();
        if (arm1_1 == null) missing.Add("アーム1-1");
        if (arm1_2 == null) missing.Add("アーム1-2");
        if (arm2_1 == null) missing.Add("アーム2-1");
        if (arm2_2 == null) missing.Add("アーム2-2");
        if (arm3 == null) missing.Add("アーム3");
        if (arm4 == null) missing.Add("アーム4");
        if (missing.Count > 0)
        {
            Debug.LogWarning($"[Robot] {unitSetting.name}: MPX-R6 の部品 {string.Join("・", missing)} が見つからないため、組み立てずに動かしません（Datas/Robots/RobotModels.json の parts で指定できます）");
            return;
        }

        // プレート 減速機の回転部
        if (plateTmp == null)
        {
            if (HeadObject != null)
            {
                // プレートの部品が無い時はヘッドをプレートとして回す。
                // ヘッドの Z 軸が回転軸と同じ向きとは限らない（R8790 の ROBO3 は直交している）ので、
                // ヘッドの位置にアーム2-2 と同じ向き（Z＝回転軸）の入れ物を作り、そちらを回す
                var pivot = new GameObject("PlatePivot");
                pivot.transform.SetParent(arm2_2.transform, false);
                pivot.transform.position = HeadObject.transform.position;
                pivot.transform.localRotation = Quaternion.identity;
                HeadObject.transform.parent = pivot.transform;
                plate = pivot;
                angP = plate.transform.localEulerAngles;
                // ヘッドの取付角（回転軸まわりの角度。プレートの部品がある時の head_offset と同じ役割で、ヘッドの向き＝90°−Z になる）。
                // ヘッドの Z 軸が回転軸と違う向きでも求まるよう、回転軸まわりのねじれ成分で求める（Z 軸が揃っていれば Euler の z と同じ）
                var q = HeadObject.transform.localRotation;
                head_offset = Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(q.z, q.w) * Mathf.Rad2Deg);
                Debug.Log($"[Robot] {unitSetting.name}: プレートの部品が無いため、ヘッド {HeadObject.name} をアーム2-2 の回転軸の向きで回します（取付角 {head_offset:0.00}°）");
            }
            else
            {
                Debug.LogWarning($"[Robot] {unitSetting.name}: プレート（減速機の回転部）が無く、ヘッドも指定されていないため、Z（ヘッドの回転）は動きません");
            }
        }
        else
        {
            plate = plateTmp;
            plate.transform.parent = arm2_2.transform;
            angP = plate.transform.localEulerAngles;

            // ヘッドセット
            if (HeadObject != null)
            {
                HeadObject.transform.parent = plate.transform;
                head_offset = HeadObject.transform.localEulerAngles.z;
            }
        }

        // 親子関係構築
        arm1_1.transform.parent = mpx.transform;
        arm2_1.transform.parent = mpx.transform;
        arm3.transform.parent = mpx.transform;
        arm1_2.transform.parent = arm2_1.transform;
        arm2_2.transform.parent = arm1_1.transform;
        arm4.transform.parent = arm3.transform;

        // 初期角度セット
        ang1_1 = arm1_1.transform.localEulerAngles;
        ang1_2 = arm1_2.transform.localEulerAngles;
        ang2_1 = arm2_1.transform.localEulerAngles;
        ang2_2 = arm2_2.transform.localEulerAngles;
        ang3 = arm3.transform.localEulerAngles;
        ang4 = arm4.transform.localEulerAngles;
        isAssembled = true;
        LogPartGeometry();

        // つながる点（平行リンクの先）。平行四辺形なので、リンクの原点＋（対になる辺のベクトル）
        //  ・アーム1-2（二軸リンク2）の先 → アーム2-2（第二アーム）：アーム1-2 の原点 ＋（肘 − 肩）
        //  ・アーム4（二軸リンク1）の先 → アーム2-1（フィン）：アーム4 の原点 ＋（肩 − 二軸レバーの軸）
        linkChecks.Clear();
        AddCheck("二軸リンク2→第二アーム", arm1_2.transform, arm2_2.transform, arm1_2.transform.position + (arm2_2.transform.position - arm1_1.transform.position));
        AddCheck("二軸リンク1→フィン", arm4.transform, arm2_1.transform, arm4.transform.position + (arm2_1.transform.position - arm3.transform.position));

        // 回し方の準備：共通の軸＝アーム1-1 の回転軸
        parts = new[] { arm1_1.transform, arm1_2.transform, arm2_1.transform, arm2_2.transform, arm3.transform, arm4.transform, plate != null ? plate.transform : null };
        var axisWorld = arm1_1.transform.TransformDirection(Vector3.forward);
        for (int i = 0; i < R6PartCount; i++)
        {
            if (parts[i] == null)
            {
                continue;
            }
            partInitRot[i] = parts[i].localRotation;
            partAxis[i] = Quaternion.Inverse(parts[i].parent.rotation) * axisWorld;
            partSigns[i] = 1f;
        }
        // 初期の姿勢の逆解の角度（TryModelPoseTarget と同じ読み方）
        a0Init = Mathf.DeltaAngle(0f, ang1_1.z - 180f);
        a1Init = -Mathf.DeltaAngle(0f, ang2_1.z);
        a2Init = Mathf.DeltaAngle(0f, (isRvs ? -90f : 90f) - head_offset - angP.z);
        DecidePartSigns();
        DecideElbow();
        DecideReachSide();
    }

    /// <summary>
    /// 目標の X の向き。+1＝逆解の X のまま / -1＝反転（DecideReachSide で決める）
    /// </summary>
    private float xSign = 1f;

    /// <summary>
    /// θ の回る向き。+1＝逆解の角度が増えると θ も増える / -1＝逆（DecideReachSide で、θ を増やすと X の + 方向から下へ回る向きに決める）
    /// </summary>
    private float thetaSign = -1f;

    /// <summary>
    /// 目標の X を反転する時の θ の変換（タイムチャートの θ → 逆解の角度）。
    /// θ は R6 の座標で X の + 方向を向く時が 0°、- 方向が 180°、下を向く時が 90°。
    /// 逆解の角度は 0° で逆解の X の + 方向（＝反転後の X の - 方向）を向くので、0° の位置が 180° ずれる。
    /// 反転しない時はそのまま。z は SetTarget に渡す形（isTm の 3軸は 1/1000 度）
    /// </summary>
    private float ToIkZ(float z)
    {
        if (xSign > 0f)
        {
            return z;
        }
        var scale = ZScale;
        return Mathf.DeltaAngle(0f, 180f + thetaSign * z / scale) * scale;
    }

    /// <summary>ToIkZ の逆（逆解の角度 → タイムチャートの θ）</summary>
    private float FromIkZ(float z)
    {
        if (xSign > 0f)
        {
            return z;
        }
        var scale = ZScale;
        return Mathf.DeltaAngle(0f, thetaSign * (z / scale - 180f)) * scale;
    }

    private float ZScale
    {
        get { return (axisType == 3) && (robo != null) && robo.isTm ? 1000f : 1f; }
    }

    /// <summary>
    /// アームが伸びる向き（目標の X を増やした時にヘッドが動く向き）を、2軸目（アーム3 二軸レバーの軸）から
    /// 1軸目（アーム1-1 の軸）へ向かう向きに合わせる。逆なら目標の X を反転する(xSign)。
    /// 逆勝手(isRvs)と肘の向きを両方変えると鏡写しが打ち消し合って動きが変わらないため、目標の X の向きで合わせる。
    /// （R9240 F1：モデルの初期姿勢の目標が X=-231.5 になり、タイムチャートの X=135～323 と符号だけ逆だった）
    /// 確認用に、モデルの初期姿勢から求めた目標（タイムチャートの値と比べる）をログに出す
    /// </summary>
    private void DecideReachSide()
    {
        var head = plate != null ? plate.transform : arm2_2.transform;
        var reachDir = arm1_1.transform.position - arm3.transform.position;
        var t0 = ForwardMPX(a0Init, a1Init, a2Init);
        var zDeg = (axisType == 3) && (robo != null) && robo.isTm ? t0.z / 1000f : t0.z;
        if (reachDir.sqrMagnitude > 1e-8f)
        {
            var ik0 = kinematics_R(t0.x, t0.y, zDeg);
            var ik1 = kinematics_R(t0.x + 10f, t0.y, zDeg);
            ApplyAngles(ik0[0], ik0[1], ik0[2], partSigns);
            var p0 = head.position;
            ApplyAngles(ik1[0], ik1[1], ik1[2], partSigns);
            var p1 = head.position;
            ApplyAngles(a0Init, a1Init, a2Init, partSigns);   // 初期の姿勢に戻す
            var moved = Vector3.Dot(p1 - p0, reachDir.normalized) * 1000f;
            xSign = moved < 0f ? -1f : 1f;
            if (xSign < 0f)
            {
                Debug.Log($"[Robot] {unitSetting.name}: アームの伸びる向きを 2軸目→1軸目 に合わせるため、目標の X を反転しました（X を +10mm した時のヘッドの動き {moved:0.0}mm）");
                DecideThetaSign(xSign * (p1 - p0).normalized);
            }
        }
        Debug.Log($"[Robot] {unitSetting.name}: モデルの初期姿勢の目標 = ({xSign * t0.x:0.0}, {t0.y:0.0}, {FromIkZ(t0.z):0.0})（タイムチャートの値と比べて確認）");
    }

    /// <summary>
    /// θ の回る向きを仮想で確かめる。ヘッドの角度を少し回して、X の + 方向に向いた向きが下（重力の向き）へ回るなら
    /// θ が増える向き。xDir は目標の X の + 方向（ワールド）
    /// </summary>
    private void DecideThetaSign(Vector3 xDir)
    {
        if (plate == null)
        {
            return;
        }
        var axisWorld = arm1_1.transform.TransformDirection(Vector3.forward);
        var down = Vector3.ProjectOnPlane(Vector3.down, axisWorld);
        if (down.sqrMagnitude < 0.01f)
        {
            Debug.LogWarning($"[Robot] {unitSetting.name}: 回転軸が上下を向いているため θ の回る向き（下＝90°）を決められません。θ は 180°-θ で変換します");
            return;
        }
        var plateT = plate.transform;
        ApplyAngles(a0Init, a1Init, a2Init, partSigns);
        var local = plateT.InverseTransformDirection(xDir);
        ApplyAngles(a0Init, a1Init, a2Init + 10f, partSigns);
        var turnedDir = plateT.TransformDirection(local);
        ApplyAngles(a0Init, a1Init, a2Init, partSigns);   // 初期の姿勢に戻す
        // X の + 方向から下へ回る向きを + とした回転角（逆解の角度 a2 を +10° した時）
        var n = Vector3.Cross(xDir, down.normalized);
        var turned = Vector3.SignedAngle(xDir, turnedDir, n);
        var s = isRvs ? -1f : 1f;   // ForwardMPX：逆解の角度 z = s*a2 + s*a1
        thetaSign = (turned * s > 0f) ? 1f : -1f;
        Debug.Log($"[Robot] {unitSetting.name}: θ の回る向きを仮想で確認（逆解の角度 +10° で下へ {turned:0.0}°）→ θ = {(thetaSign > 0 ? "逆解の角度 - 180°" : "180° - 逆解の角度")}（X の + 方向＝0°・下＝90°）");
    }

    /// <summary>
    /// 逆解の肘の向きを、モデルの初期姿勢に合わせる。初期の角度から先端の位置を求め（ForwardMPX）、
    /// それを逆解に通して初期の角度に戻る方の解を使う（R9240 F1 は従来と反対側の解で、肘が逆に折れていた）
    /// </summary>
    private void DecideElbow()
    {
        var fk = ForwardMPX(a0Init, a1Init, a2Init);
        var zDeg = (axisType == 3) && (robo != null) && robo.isTm ? fk.z / 1000f : fk.z;
        float Err()
        {
            var ik = kinematics_R(fk.x, fk.y, zDeg);
            return Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(ik[0], a0Init)), Mathf.Abs(Mathf.DeltaAngle(ik[1], a1Init)));
        }
        var errDefault = Err();
        if (errDefault > 1f)
        {
            elbowSign = -elbowSign;
            var errFlip = Err();
            if (errFlip < errDefault)
            {
                Debug.Log($"[Robot] {unitSetting.name}: 逆解の肘の向きを反対にしました（モデルの初期姿勢に合わせた。初期の角度とのずれ {errDefault:0.0}° → {errFlip:0.0}°）");
                return;
            }
            elbowSign = -elbowSign;   // 反対でも合わなければ従来のまま
            Debug.LogWarning($"[Robot] {unitSetting.name}: 逆解がモデルの初期姿勢と合いません（ずれ {errDefault:0.0}°）。アームの長さ r1/r2 か部品の割り当てを確認してください");
        }
    }

    /// <summary>
    /// 【調査用】各部品の原点（回転の中心）の位置・初期の角度・Z 軸の向きを、ロボットの基準(mpx)の座標で出す。
    /// 部品の付け方（Z 軸が裏返し等）とアームの長さ（r1/r2 は 200mm 固定）がモデルと合うかを見るため
    /// </summary>
    private void LogPartGeometry()
    {
        var items = new List<(string name, Transform t)>
        {
            ("アーム1-1", arm1_1.transform), ("アーム1-2", arm1_2.transform), ("アーム2-1", arm2_1.transform),
            ("アーム2-2", arm2_2.transform), ("アーム3", arm3.transform), ("アーム4", arm4.transform),
        };
        if (plate != null) { items.Add(("プレート", plate.transform)); }
        if (HeadObject != null) { items.Add(("ヘッド", HeadObject.transform)); }
        var root = mpx.transform;
        var sb = new System.Text.StringBuilder();
        sb.Append($"[Robot] {unitSetting.name}: MPX-R6 の部品の配置（mpx 基準・mm）r1={r1} r2={r2} offset={offset} isRvs={isRvs} head_offset={head_offset:0.00}");
        foreach (var (n, t) in items)
        {
            var p = root.InverseTransformPoint(t.position) * 1000f;
            var zAxis = root.InverseTransformDirection(t.TransformDirection(Vector3.forward));
            sb.Append($"\n  {n}={t.name} 位置=({p.x:0.0},{p.y:0.0},{p.z:0.0}) 親={(t.parent != null ? t.parent.name : "-")} 角度={t.localEulerAngles.ToString("F2")} Z軸=({zAxis.x:0.00},{zAxis.y:0.00},{zAxis.z:0.00})");
        }
        Debug.Log(sb.ToString());
    }
}
