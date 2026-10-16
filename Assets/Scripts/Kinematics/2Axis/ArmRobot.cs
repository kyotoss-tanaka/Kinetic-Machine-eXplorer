using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ArmRobot : UseHeadBase3DScript
{
    /// <summary>
    /// ２軸アーム用オブジェクト
    /// </summary>
    protected GameObject arm;

    /// <summary>
    /// 角度
    /// </summary>
    [SerializeField]
    protected List<float> angle;

    /// <summary>
    /// アーム長1
    /// </summary>
    protected float L1;

    /// <summary>
    /// アーム長2
    /// </summary>
    protected float L2;

    protected GameObject arm1_1;
    protected GameObject arm1_2;
    protected GameObject arm1_3;
    protected GameObject arm1Lever;
    protected GameObject armTri;
    protected GameObject arm2_1;
    protected GameObject arm2_2;
    protected GameObject plate;

    protected Vector3 ang1_1;
    protected Vector3 ang1_2;
    protected Vector3 ang1_3;
    protected Vector3 ang1Lever;
    protected Vector3 angTri;
    protected Vector3 ang2_1;
    protected Vector3 ang2_2;
    protected Vector3 angP;

    /// <summary>
    /// 組み立てできたか（部品が揃い、アーム長が0でない）
    /// </summary>
    protected bool isAssembled;

    /// <summary>
    /// 組み立て失敗の警告を出したか（毎フレーム出さないため）
    /// </summary>
    private bool warnedNotAssembled;

    // ---- 確認用：リンクの先がつながっているか（モデルの初期姿勢でつながっている点を、両側の部品の座標で覚える） ----
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

    /// <summary>
    /// 開始処理
    /// </summary>
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
    public override void SetTarget(float x, float y, float z)
    {
        // 組み立てに失敗している（部品が見つからない・アーム長が0）時は動かさない。
        // アーム長0のまま逆解を解くと 0÷0 で NaN になり、回転に NaN を入れて毎フレームエラーになっていた
        if (!isAssembled)
        {
            if (!warnedNotAssembled)
            {
                warnedNotAssembled = true;
                Debug.LogWarning($"[ArmRobot] {name}: アームの組み立てに失敗しているため動かしません（ModelRestruct のログを確認してください）");
            }
            return;
        }
        angle = kinematics_R(y, x);
        ApplyAngles(angle[0], angle[1], partSigns);
        LogLinkGaps(new Vector3(x, y, z));
    }

    // ---- 部品の回し方 ----
    // 各部品は「モデルの初期の姿勢からの変化分」だけ、全部品共通の1本の軸（第一アームの回転軸）のまわりに回す。
    // 以前は部品ごとに自分の Z 軸で localEulerAngles.z を決め打ちしていたため、CAD で Z 軸が裏返しの部品
    // （R9240 C1/C5 の二軸リンク・姿勢保持リンクは Y=180°）だけ逆に回り、つながる点が外れていた。
    // 向き(partSigns)は組み立て時に仮想で動かして、つながる点が離れない組み合わせを選ぶ（DecidePartSigns）。
    private static readonly string[] PartNames = { "第一アーム", "二軸リンク", "第一姿勢保持リンク", "二軸レバー", "フィン", "第二アーム", "第二姿勢保持リンク", "ヘッド" };
    private const int ArmPartCount = 8;
    private Transform[] parts;
    private readonly Quaternion[] partInitRot = new Quaternion[ArmPartCount];
    private readonly Vector3[] partAxis = new Vector3[ArmPartCount];   // 共通の軸を各部品の親の座標で表したもの
    private readonly float[] partSigns = { 1, 1, 1, 1, 1, 1, 1, 1 };
    private readonly float[] partDeltas = new float[ArmPartCount];
    private float angle0Init;
    private float angle1Init;

    /// <summary>
    /// 逆解の角度の変化分 → 各部品の回転の変化分（従来の式から変化分だけを取り出したもの。並びは PartNames）
    /// </summary>
    private static void PartDeltas(float d0, float d1, float[] o)
    {
        o[0] = d0;              // 第一アーム
        o[1] = d1;              // 二軸リンク（従来: angle1 - 180）
        o[2] = d0;              // 第一姿勢保持リンク
        o[3] = d0 - d1;         // 二軸レバー（従来: 180 + angle0 - angle1）
        o[4] = -d0;             // フィン：初期の向きを保つ
        o[5] = -d1;             // 第二アーム
        o[6] = d0 - d1;         // 第二姿勢保持リンク：第二アームと平行を保つ
        o[7] = -(d0 - d1);      // ヘッド：平行リンクで初期の向きを保つ（ヘッドを回す軸は無い）
    }

    /// <summary>
    /// 逆解の角度(angle0, angle1)の姿勢にする。signs は部品ごとの回す向き
    /// </summary>
    private void ApplyAngles(float a0, float a1, float[] signs)
    {
        PartDeltas(Mathf.DeltaAngle(angle0Init, a0), Mathf.DeltaAngle(angle1Init, a1), partDeltas);
        for (int i = 0; i < ArmPartCount; i++)
        {
            parts[i].localRotation = Quaternion.AngleAxis(signs[i] * partDeltas[i], partAxis[i]) * partInitRot[i];
        }
    }

    /// <summary>
    /// 組み立て時に、試しの姿勢へ仮想で動かして、つながる点（linkChecks）が離れない向きの組み合わせを選ぶ。
    /// 第一アームと第二アーム（逆解の角度そのもの）は +1 に固定し、残り6部品の ±1（64通り）を試す。
    /// 同点なら逆にする部品が少ない方。最後に初期の姿勢へ戻す
    /// </summary>
    private void DecidePartSigns()
    {
        var poses = new[]
        {
            new Vector2(angle0Init + 10f, angle1Init),
            new Vector2(angle0Init, angle1Init + 10f),
            new Vector2(angle0Init - 10f, angle1Init - 10f),
        };
        var trial = new float[ArmPartCount];
        float Score(float[] s, out float maxGap)
        {
            float sum = 0f;
            maxGap = 0f;
            foreach (var p in poses)
            {
                ApplyAngles(p.x, p.y, s);
                foreach (var c in linkChecks)
                {
                    var gap = Vector3.Distance(c.a.TransformPoint(c.aLocal), c.b.TransformPoint(c.bLocal)) * 1000f;
                    sum += gap;
                    maxGap = Mathf.Max(maxGap, gap);
                }
            }
            return sum;
        }
        int[] free = { 1, 2, 3, 4, 6, 7 };
        Score(partSigns, out var defaultMax);
        float best = float.MaxValue, bestMax = 0f;
        int bestFlips = int.MaxValue;
        var bestSigns = (float[])partSigns.Clone();
        for (int mask = 0; mask < (1 << free.Length); mask++)
        {
            int flips = 0;
            for (int i = 0; i < ArmPartCount; i++) { trial[i] = 1f; }
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
        ApplyAngles(angle0Init, angle1Init, partSigns);   // 初期の姿勢に戻す

        var reversed = Enumerable.Range(0, ArmPartCount).Where(i => partSigns[i] < 0).Select(i => PartNames[i]).ToList();
        var text = $"[ArmRobot] {unitSetting.name}: 回す向きを仮想で確認：逆向き={(reversed.Count == 0 ? "なし" : string.Join("・", reversed))} " +
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

    /// <summary>
    /// 逆解を解く
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    /// <returns></returns>
    protected virtual List<float> kinematics_R(float x, float y)
    {
        var ret = new List<float>();

        float dist2 = x * x + y * y;
        float dist = Mathf.Sqrt(dist2);
        float theta1, theta2;

        // 到達不可能
        if (dist > L1 + L2 || dist < Mathf.Abs(L1 - L2))
        {
            ret.Add(0);
            ret.Add(0);
            return ret;
        }

        // --- θ2 ---
        float cos2 = (dist2 - L1 * L1 - L2 * L2) / (2f * L1 * L2);
        cos2 = Mathf.Clamp(cos2, -1f, 1f);

        float sin2 = Mathf.Sqrt(1f - cos2 * cos2);
//        if (!elbowUp) sin2 = -sin2; // 肘下げ解

        theta2 = Mathf.Atan2(sin2, cos2);

        // --- θ1 ---
        float k1 = L1 + L2 * cos2;
        float k2 = L2 * sin2;

        theta1 = Mathf.Atan2(y, x) - Mathf.Atan2(k2, k1);

        ret.Add(90f - (theta1 * Mathf.Rad2Deg));
        ret.Add(theta2 * Mathf.Rad2Deg);
        return ret;
    }

    /// <summary>
    /// モデル再構築
    /// </summary>
    /// <param name="instance"></param>
    protected override void ModelRestructProcess()
    {
        isAssembled = false;
        arm = new GameObject("ARM");
        arm.transform.parent = unitSetting.moveObject.transform;

        var children = unitSetting.moveObject.GetComponentsInChildren<Transform>().ToList();

        // 各アームはロボットの定義（Datas/Robots/RobotModels.json）の名前で探す。定義に無いアームはここに書いた既定の名前で探す。
        // 既定は「役割の名前の入れ物（アセンブリ）」→「図番の部品の親」の順。
        // 例：R8790 は「…-第二姿勢保持リンク-1」の中が第一姿勢保持リンクと同じ図番(W0334703)で、図番では区別できない。
        //     天吊りのフィンは W0883388、ヘッドのプレートは「…-ヘッド-2」の入れ物（W0334721 は無い）
        var finder = RobotDefinitions.Finder(unitSetting, DefinitionType, children, HeadObject);
        arm1_1 = finder.Find("第一アーム", "第一アーム", "parent:W0334776-");
        arm1_2 = finder.Find("二軸リンク", "二軸リンク", "parent:W0334688-");
        arm1_3 = finder.Find("第一姿勢保持リンク", "第一姿勢保持リンク", "parent:W0334703-");
        arm1Lever = finder.Find("二軸レバー", "二軸レバー", "parent:W0334679-");
        armTri = finder.Find("フィン", "フィン", "parent:W0334712-", "parent:W0652636-", "parent:W0693785-", "parent:W0883388-");   // 三角プレート
        arm2_1 = finder.Find("第二アーム", "第二アーム", "parent:W0334864-");
        arm2_2 = finder.Find("第二姿勢保持リンク", "第二姿勢保持リンク", "parent:W0656252-", "parent:W0693776-");

        // プレート（ヘッド）：図番 W0334721 の部品 → 「ヘッド」の名前の物 → 登録したヘッドユニット の順
        var plateTmp = finder.Find("ヘッド", "W0334721-", "ヘッド");
        finder.Log();
        if (plateTmp != null)
        {
            plate = plateTmp;
            if ((HeadObject != null) && (HeadObject != plate))
            {
                // プレートがヘッドの中にある時は、先にプレートをヘッドから出す（ヘッドを自分の子の下に付けられないため）。
                // ヘッドはプレートに付けて、プレートと一緒に動かす
                if (plate.transform.IsChildOf(HeadObject.transform))
                {
                    plate.transform.SetParent(HeadObject.transform.parent, true);
                }
                HeadObject.transform.parent = plate.transform;
                head_offset = HeadObject.transform.localEulerAngles.z;
            }
        }
        else if (HeadObject != null)
        {
            plate = HeadObject;
        }

        // 見つからない部品があれば組み立てない（途中で null に触って例外になり、アーム長0のまま動かしていた）
        var missing = new List<string>();
        if (arm1_1 == null) { missing.Add("第一アーム"); }
        if (arm1_2 == null) { missing.Add("二軸リンク"); }
        if (arm1_3 == null) { missing.Add("第一姿勢保持リンク"); }
        if (arm1Lever == null) { missing.Add("二軸レバー"); }
        if (armTri == null) { missing.Add("フィン(三角プレート)"); }
        if (arm2_1 == null) { missing.Add("第二アーム"); }
        if (arm2_2 == null) { missing.Add("第二姿勢保持リンク"); }
        if (plate == null) { missing.Add("ヘッド(プレート)"); }
        if (missing.Count > 0)
        {
            Debug.LogWarning($"[ArmRobot] {unitSetting.name}: アームの部品が見つかりません：{string.Join("、", missing)}");
            return;
        }
        plate.transform.parent = arm2_1.transform;
        angP = plate.transform.localEulerAngles;

        // 親子関係セット
        arm.transform.position = arm1_1.transform.position;
        arm.transform.localEulerAngles = Vector3.zero;
        arm.transform.localScale = Vector3.one;
        arm1_1.transform.parent = arm.transform;
        arm1_3.transform.parent = arm.transform;
        arm1_2.transform.parent = arm1Lever.transform;
        arm1Lever.transform.parent = arm.transform;
        armTri.transform.parent = arm1_1.transform;
        arm2_1.transform.parent = arm1_1.transform;
        arm2_2.transform.parent = armTri.transform;

        // 初期角度セット
        ang1_1 = arm1_1.transform.localEulerAngles;
        ang1_2 = arm1_2.transform.localEulerAngles;
        ang1_3 = arm1_3.transform.localEulerAngles;
        ang1Lever = arm1Lever.transform.localEulerAngles;
        angTri = armTri.transform.localEulerAngles;
        ang2_1 = arm2_1.transform.localEulerAngles;
        ang2_2 = arm2_2.transform.localEulerAngles;

        // アーム長セット
        L1 = Vector3.Distance(Vector3.zero, Vector3.Scale(arm2_1.transform.localPosition, new Vector3(1, 1, 0))) * 1000f;
        L2 = Vector3.Distance(Vector3.zero, Vector3.Scale(plate.transform.localPosition, new Vector3(1, 1, 0))) * 1000f;
        isAssembled = (L1 > 0.001f) && (L2 > 0.001f);

        Debug.Log($"[ArmRobot] {unitSetting.name}: 第一アーム={arm1_1.name} / 第二アーム={arm2_1.name} / 二軸リンク={arm1_2.name} / 二軸レバー={arm1Lever.name} / " +
            $"第一姿勢保持={arm1_3.name} / 第二姿勢保持={arm2_2.name} / フィン={armTri.name} / ヘッド={plate.name} / L1={L1:0.0}mm L2={L2:0.0}mm" +
            (isAssembled ? "" : " ※アーム長が0のため動かしません"));

        // 確認用：初期の角度（SetTarget はこの x,y を保ち z を決め打ちで入れる）
        Debug.Log($"[ArmRobot] {unitSetting.name}: 初期の localEulerAngles 第一アーム={ang1_1} 二軸リンク={ang1_2} 第一姿勢保持={ang1_3} 二軸レバー={ang1Lever} " +
            $"フィン={angTri} 第二アーム={ang2_1} 第二姿勢保持={ang2_2} ヘッド={angP}");
        // 確認用：平行リンクの先の点（相手側の部品の上の点）。平行四辺形なので、リンクの原点＋（相手のアームの長さのベクトル）
        linkChecks.Clear();
        void AddCheck(string checkName, Transform a, Transform b, Vector3 world)
        {
            linkChecks.Add(new LinkCheck { name = checkName, a = a, aLocal = a.InverseTransformPoint(world), b = b, bLocal = b.InverseTransformPoint(world) });
        }
        var elbowVec = arm2_1.transform.position - arm1_1.transform.position;
        AddCheck("第一姿勢保持リンク→フィン", arm1_3.transform, armTri.transform, arm1_3.transform.position + elbowVec);
        AddCheck("二軸リンク→第二アーム", arm1_2.transform, arm2_1.transform, arm1_2.transform.position + elbowVec);
        AddCheck("第二姿勢保持リンク→ヘッド", arm2_2.transform, plate.transform, arm2_2.transform.position + (plate.transform.position - arm2_1.transform.position));

        if (isAssembled)
        {
            // 回し方の準備：共通の軸＝第一アームの回転軸（第一アーム自身の Z 軸は回しても変わらない）
            parts = new[] { arm1_1.transform, arm1_2.transform, arm1_3.transform, arm1Lever.transform, armTri.transform, arm2_1.transform, arm2_2.transform, plate.transform };
            var axisWorld = arm1_1.transform.TransformDirection(Vector3.forward);
            for (int i = 0; i < ArmPartCount; i++)
            {
                partInitRot[i] = parts[i].localRotation;
                partAxis[i] = Quaternion.Inverse(parts[i].parent.rotation) * axisWorld;
                partSigns[i] = 1f;
            }
            // 初期の姿勢の逆解の角度（InitialPoseTarget と同じ読み方）
            angle0Init = Mathf.DeltaAngle(0f, ang1_1.z);
            angle1Init = Mathf.DeltaAngle(0f, -ang2_1.z);
            DecidePartSigns();
        }
    }

    /// <summary>
    /// 確認用：リンクの先が相手の部品からどれだけずれたか（mm）をログに出す。目標が変わった時だけ、1秒に1回まで
    /// </summary>
    private void LogLinkGaps(Vector3 target)
    {
        if ((linkChecks.Count == 0) || (target == lastCheckTarget) || (Time.unscaledTime < nextCheckTime))
        {
            return;
        }
        lastCheckTarget = target;
        nextCheckTime = Time.unscaledTime + 1f;
        var text = new List<string>();
        foreach (var c in linkChecks)
        {
            var gap = Vector3.Distance(c.a.TransformPoint(c.aLocal), c.b.TransformPoint(c.bLocal)) * 1000f;
            text.Add($"{c.name}={gap:0.0}mm");
        }
        Debug.Log($"[ArmRobot] {unitSetting.name}: 目標({target.x:0.0},{target.y:0.0},{target.z:0.0}) 角度 angle0={angle[0]:0.0} angle1={angle[1]:0.0} " +
            $"localZ 第一アーム={arm1_1.transform.localEulerAngles.z:0.0} 第二アーム={arm2_1.transform.localEulerAngles.z:0.0} フィン={armTri.transform.localEulerAngles.z:0.0} " +
            $"二軸レバー={arm1Lever.transform.localEulerAngles.z:0.0}  ずれ：{string.Join(" / ", text)}");
    }

    /// <summary>
    /// モデルの初期姿勢の先端位置（SetTarget に渡す x,y,z の形）。
    /// 逆解は angle[0]=90-θ1、angle[1]=θ2、SetTarget は kinematics_R(y, x) なので、その逆をたどる
    /// </summary>
    private Vector3 InitialPoseTarget()
    {
        var angle0 = Mathf.DeltaAngle(0f, ang1_1.z);
        var angle1 = Mathf.DeltaAngle(0f, -ang2_1.z);
        var t1 = (90f - angle0) * Mathf.Deg2Rad;
        var t12 = t1 + angle1 * Mathf.Deg2Rad;
        var kx = L1 * Mathf.Cos(t1) + L2 * Mathf.Cos(t12);
        var ky = L1 * Mathf.Sin(t1) + L2 * Mathf.Sin(t12);
        // プレートの角度 = angle1 - angle0 + z
        var z = Mathf.DeltaAngle(0f, angP.z - angle1 + angle0);
        return new Vector3(ky, kx, z);
    }

    /// <summary>
    /// モデルの初期姿勢の目標（逆解 kinematics_R の逆算）
    /// </summary>
    protected override bool TryModelPoseTarget(out Vector3 raw)
    {
        raw = Vector3.zero;
        if (!isAssembled)
        {
            return false;
        }
        raw = ToInputTarget(InitialPoseTarget());
        return true;
    }

    /// <summary>
    /// SetTarget に渡す形から、目標（Inspector の target）の形へ（天吊りは x,y の符号が逆）
    /// </summary>
    protected virtual Vector3 ToInputTarget(Vector3 value)
    {
        return value;
    }

    /// <summary>
    /// ヘッドを回す軸は無い（ヘッドの向きは平行リンクで決まる）ので、マニュアルは X・Y だけ
    /// </summary>
    protected override bool HasManualZ
    {
        get { return false; }
    }

    /// <summary>
    /// ロボットの定義で使う型（アームの名前をどの型の定義から読むか）
    /// </summary>
    protected virtual Parameters.RobotType DefinitionType
    {
        get { return Parameters.RobotType.ARM; }
    }

}
