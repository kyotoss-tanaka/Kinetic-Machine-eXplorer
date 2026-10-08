using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;

public class MPX_R3 : MPX_RX
{
    #region 変数
    protected GameObject arm1;
    protected GameObject arm2_1;
    protected GameObject arm2_2;
    protected GameObject arm3;
    protected GameObject arm4;
    protected GameObject arm5;
    protected GameObject fin;
    protected GameObject plate;

    private Vector3 ang1;
    private Vector3 ang2_1;
    private Vector3 ang2_2;
    private Vector3 ang3;
    private Vector3 ang4;
    private Vector3 ang5;
    private Vector3 finP;
    private Vector3 angP;

    /// <summary>
    /// プレートが逆
    /// </summary>
    private bool isPlateRvs = false;

    /// <summary>
    /// 自己保持用フィン
    /// </summary>
    private bool isFin = false;

    /// <summary>
    /// 地面設置
    /// </summary>
    private bool isGround = false;

    /// <summary>
    /// アームが揃って組み立てられたか（揃わない時は動かさない）
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
        // SetTarget の角度の当て方を逆にたどる（arm1=±a1、arm2_1=±(a0-180)、plate=±(-a2) または ±(90-a2)）
        raw = Vector3.zero;
        if ((arm1 == null) || (arm2_1 == null))
        {
            return false;
        }
        var g = isGround ? -1f : 1f;
        var a1 = g * Mathf.DeltaAngle(0f, ang1.z);
        var a0 = Mathf.DeltaAngle(0f, g * Mathf.DeltaAngle(0f, ang2_1.z) + 180f);
        var p = isPlateRvs ? -1f : 1f;
        var a2 = -a1;   // プレートが無い時は回転0
        if (plate != null)
        {
            a2 = isFin ? -p * Mathf.DeltaAngle(0f, angP.z) : 90f - p * Mathf.DeltaAngle(0f, angP.z);
        }
        raw = ForwardMPX(a0, a1, a2);
        return true;
    }

    public override void SetTarget(float x, float y, float z)
    {
        base.SetTarget(x, y, z);
        if (!isAssembled)
        {
            return;
        }
        arm1.transform.localEulerAngles = new Vector3(ang1.x, ang1.y, isGround ? -angle[1] : angle[1]);
        arm2_1.transform.localEulerAngles = new Vector3(ang2_1.x, ang2_1.y, isGround ? -(angle[0] - 180) : (angle[0] - 180));
        arm2_2.transform.localEulerAngles = new Vector3(ang2_2.x, ang2_2.y, isGround ? (angle[1] + angle[0]) : -(angle[1] + angle[0]));
        arm3.transform.localEulerAngles = new Vector3(ang3.x, ang3.y, isGround ? (angle[0] + angle[1] - 180) : -(angle[0] + angle[1] - 180));
        if (isFin)
        {
            plate.transform.localEulerAngles = new Vector3(angP.x, angP.y, (isPlateRvs ? -1 : 1) * (-angle[2]));
            fin.transform.localEulerAngles = new Vector3(finP.x, finP.y, 180 - angle[0]);
            arm4.transform.localEulerAngles = new Vector3(ang4.x, ang4.y, angle[0]);
            arm5.transform.localEulerAngles = new Vector3(ang5.x, ang5.y, -angle[1]);
        }
        else
        {
            if (plate != null)
            {
                plate.transform.localEulerAngles = new Vector3(angP.x, angP.y, (isPlateRvs ? -1 : 1) * (90 - angle[2]));
            }
        }
    }

    /// <summary>
    /// モデル再構築
    /// </summary>
    /// <param name="instance"></param>
    protected override void ModelRestructProcess()
    {
        var baseObj = base.ModelRestructProcess("MPX-R3");

        r1 = 480;
        r2 = 480;

        var children = unitSetting.moveObject.GetComponentsInChildren<Transform>().ToList();

        // 各アームはロボットの定義（Datas/Robots/RobotModels.json）の名前で探す。定義に無いアームはここに書いた既定の名前で探す。
        // フィン付き（θ固定）は、アーム3・プレートの図番がフィン無しと違う（どちらか一方しかモデルに無い）
        var finder = RobotDefinitions.Finder(unitSetting, Parameters.RobotType.MPX_R3, children, HeadObject);
        arm1 = finder.Find("アーム1", "parent:W0250623-");
        arm2_1 = finder.Find("アーム2-1", "parent:W0250562-");
        arm2_2 = finder.Find("アーム2-2", "parent:W0250599-");
        fin = finder.Find("フィン", "parent:W0459419-");   // 自己保持用フィン
        isFin = fin != null;
        if (isFin)
        {
            // θ固定
            arm4 = finder.Find("アーム4", "parent:W0263919-");
            arm5 = finder.Find("アーム5", "parent:W0263937-");
        }
        arm3 = finder.Find("アーム3", "parent:W0262345-", "parent:W0250614-");
        var plateTmp = finder.Find("プレート", "parent:W0370723-", "parent:W0250632-");
        finder.Log();
        if (arm1 != null)
        {
            axisType = 3;
        }
        baseObj.name += isFin ? "D" : "T";

        // 見つからないアームがあれば組み立てない（途中で null に触って例外になるため）
        if ((arm1 == null) || (arm2_1 == null) || (arm2_2 == null) || (arm3 == null) || (isFin && ((arm4 == null) || (arm5 == null))))
        {
            Debug.LogWarning($"[Robot] {unitSetting.name}: MPX-R3 のアームが揃わないため、組み立てずに動かしません");
            return;
        }
        isAssembled = true;

        // プレート W0250632- W0370723-
        if (plateTmp == null)
        {
            if (HeadObject != null)
            {
                plate = HeadObject;
                plate.transform.parent = arm3.transform;
                angP = plate.transform.localEulerAngles;
            }
            else
            {
                // プレートの部品が無いモデルは、ロボット設定でヘッドを指定するとヘッドをプレートとして回す
                Debug.LogWarning($"[Robot] {unitSetting.name}: プレートが無く、ヘッドも指定されていないため、Z（ヘッドの回転）は動きません");
            }
        }
        else
        {
            plate = plateTmp;
            plate.transform.parent = arm3.transform;
            angP = plate.transform.localEulerAngles;
            isPlateRvs = Mathf.Abs(plate.transform.localEulerAngles.y) > 90;
            // ヘッドセット
            if (HeadObject != null)
            {
                HeadObject.transform.parent = plate.transform;
            }
        }
        // 親子関係構築
        arm1.transform.parent = mpx.transform;
        arm2_1.transform.parent = mpx.transform;
        arm2_2.transform.parent = arm1.transform;
        arm3.transform.parent = arm2_1.transform;

        // 初期角度セット
        ang1 = arm1.transform.localEulerAngles;
        ang2_1 = arm2_1.transform.localEulerAngles;
        ang2_2 = arm2_2.transform.localEulerAngles;
        ang3 = arm3.transform.localEulerAngles;

        if (isFin)
        {
            fin.transform.parent = arm2_1.transform;
            arm5.transform.parent = fin.transform;
            ang4 = arm4.transform.localEulerAngles;
            ang5 = arm5.transform.localEulerAngles;
            finP = fin.transform.localEulerAngles;
        }
    }
}
