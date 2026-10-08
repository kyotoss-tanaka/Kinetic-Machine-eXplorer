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
        arm1_1.transform.localEulerAngles = new Vector3(ang1_1.x, ang1_1.y, angle[0]);
        arm1_2.transform.localEulerAngles = new Vector3(ang1_2.x, ang1_2.y, angle[0] - 180 - (angle[0] - angle[1]));
        arm1_3.transform.localEulerAngles = new Vector3(ang1_3.x, ang1_3.y, angle[0]);
        arm1Lever.transform.localEulerAngles = new Vector3(ang1Lever.x, ang1Lever.y, 180 + (angle[0] - angle[1]));
        // フィン（三角プレート）と第二姿勢保持リンクは、モデルの初期の姿勢からの変化分で回す。
        // フィンは初期の向きを保ち、第二姿勢保持リンクは第二アームと平行を保つ。
        // （以前は「フィンの向き＝0°」と決め打ちしていたため、フィンの基準が -90° の天吊り(R8790 N1)で
        //   フィンが90°ずれ、つながるアームが外れていた。フィンが0°のモデルでは以前と同じ値になる）
        var arm1Delta = angle[0] - ang1_1.z;
        armTri.transform.localEulerAngles = new Vector3(angTri.x, angTri.y, angTri.z - arm1Delta);
        arm2_1.transform.localEulerAngles = new Vector3(ang2_1.x, ang2_1.y, -angle[1]);
        arm2_2.transform.localEulerAngles = new Vector3(ang2_2.x, ang2_2.y, ang2_2.z + arm1Delta + (-angle[1] - ang2_1.z));
        // ヘッド（プレート）の姿勢は、第二アームと第二姿勢保持リンクの平行リンクで保たれる（ヘッドを回す軸は無い）。
        // そのため、フィンと同じくモデルの初期の向きから、第二アームが回った分だけ打ち消して向きを保つ。
        // （以前は Z の値で角度を決めていたため、Z がモデルの姿勢と合わないと（D1 は Z=-90 の時だけ合う）
        //   ヘッドが回り、上下に並ぶはずの接合部が水平に並んでいた）
        var arm2Delta = arm1Delta + (-angle[1] - ang2_1.z);
        plate.transform.localEulerAngles = new Vector3(angP.x, angP.y, angP.z - arm2Delta);
        LogLinkGaps(new Vector3(x, y, z));
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
