using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;

public class MPX_R3S : MPX_RX
{
    #region 変数
    protected GameObject arm1;
    protected GameObject arm2_1;
    protected GameObject arm2_2;
    protected GameObject arm3;
    protected GameObject arm4_1;
    protected GameObject arm4_2;
    protected GameObject arm5_1;
    protected GameObject arm5_2;
    protected GameObject fin_1;
    protected GameObject fin_2;
    protected GameObject plate;

    private Vector3 ang1;
    private Vector3 ang2_1;
    private Vector3 ang2_2;
    private Vector3 ang3;
    private Vector3 ang4_1;
    private Vector3 ang4_2;
    private Vector3 ang5_1;
    private Vector3 ang5_2;
    private Vector3 fin1P;
    private Vector3 fin2P;
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
        // SetTarget の角度の当て方を逆にたどる（arm1=a1、arm2_1=a0-180、plate=±(-a2) または ±(90-a2)）
        raw = Vector3.zero;
        if ((arm1 == null) || (arm2_1 == null))
        {
            return false;
        }
        var a1 = Mathf.DeltaAngle(0f, ang1.z);
        var a0 = Mathf.DeltaAngle(0f, ang2_1.z + 180f);
        var p = isPlateRvs ? -1f : 1f;
        var a2 = -a1;
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
        if (isGround)
        {
            for (var i = 0; i < angle.Count; i++)
            {
//                angle[i] += 180;
            }
        }
        arm1.transform.localEulerAngles = new Vector3(ang1.x, ang1.y, angle[1]);
        arm2_1.transform.localEulerAngles = new Vector3(ang2_1.x, ang2_1.y, angle[0] - 180);
        arm2_2.transform.localEulerAngles = new Vector3(ang2_2.x, ang2_2.y,  -(angle[1] + angle[0]));
        arm3.transform.localEulerAngles = new Vector3(ang3.x, ang3.y, -(angle[0] + angle[1] - 180));
        if (isFin)
        {
            plate.transform.localEulerAngles = new Vector3(angP.x, angP.y, (isPlateRvs ? -1 : 1) * (-angle[2]));
            fin_1.transform.localEulerAngles = new Vector3(fin1P.x, fin1P.y, -(180 - angle[0]));
            fin_2.transform.localEulerAngles = new Vector3(fin2P.x, fin2P.y, -(180 - angle[0]));
            arm4_1.transform.localEulerAngles = new Vector3(ang4_1.x, ang4_1.y, angle[0]);
            arm4_2.transform.localEulerAngles = new Vector3(ang4_2.x, ang4_2.y, 180 - angle[0]);
            arm5_1.transform.localEulerAngles = new Vector3(ang5_1.x, ang5_1.y, angle[1] + 180);
            arm5_2.transform.localEulerAngles = new Vector3(ang5_2.x, ang5_2.y, -angle[1]);
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
        var baseObj = base.ModelRestructProcess("MPX-R3S");

        var children = unitSetting.moveObject.GetComponentsInChildren<Transform>().ToList();

        if (children.Find(d => d.name.Contains("W0250623-")) != null)
        {
            axisType = 3;
        }
        isGround = Mathf.Abs(unitSetting.unitObject.transform.localEulerAngles.z) > 90;

        r1 = 260;
        r2 = 260;

        // 各アームはロボットの定義（Datas/Robots/RobotModels.json）の名前で探す。定義に無いアームはここに書いた既定の名前で探す
        var finder = RobotDefinitions.Finder(unitSetting, Parameters.RobotType.MPX_R3S, children, HeadObject);
        arm1 = finder.Find("アーム1", "parent:W0282303-");
        arm2_1 = finder.Find("アーム2-1", "parent:W0143305-");
        arm2_2 = finder.Find("アーム2-2", "parent:W0282552-");
        fin_1 = finder.Find("フィン1", "parent:W0282640-");   // 自己保持用フィン
        fin_2 = finder.Find("フィン2", "parent:W0282589-");
        isFin = fin_1 != null;
        if (isFin)
        {
            // θ固定（同じ部品が4つ。先頭から アーム4-1・アーム4-2・アーム5-1・アーム5-2）
            var arm45 = finder.FindAll("アーム4・5", "parent:W0282604-");
            if (arm45.Count == 4)
            {
                arm4_1 = arm45[0];
                arm4_2 = arm45[1];
                arm5_1 = arm45[2];
                arm5_2 = arm45[3];
            }
        }
        arm3 = finder.Find("アーム3", "parent:W0282428-");
        var plateTmp = finder.Find("プレート", "parent:parent:W0282631-");
        finder.Log();
        baseObj.name += isFin ? "D" : "T";

        // 見つからないアームがあれば組み立てない（途中で null に触って例外になるため。フィン無しは以前から組み立てられなかった）
        if ((arm1 == null) || (arm2_1 == null) || (arm2_2 == null) || (arm3 == null) ||
            (isFin && ((fin_2 == null) || (arm4_1 == null) || (arm4_2 == null) || (arm5_1 == null) || (arm5_2 == null))))
        {
            Debug.LogWarning($"[Robot] {unitSetting.name}: MPX-R3S のアームが揃わないため、組み立てずに動かしません");
            return;
        }
        isAssembled = true;

        // プレート W0282631- の親の親
        if (plateTmp == null)
        {
            if (HeadObject != null)
            {
                plate = HeadObject;
                plate.transform.parent = arm3.transform;
                angP = plate.transform.localEulerAngles;
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
            fin_1.transform.parent = arm2_1.transform;
            fin_2.transform.parent = arm2_1.transform;
            arm5_1.transform.parent = fin_1.transform;
            arm5_2.transform.parent = fin_2.transform;

            ang4_1 = arm4_1.transform.localEulerAngles;
            ang4_2 = arm4_2.transform.localEulerAngles;
            ang5_1 = arm5_1.transform.localEulerAngles;
            ang5_2 = arm5_2.transform.localEulerAngles;
            fin1P = fin_1.transform.localEulerAngles;
            fin2P = fin_2.transform.localEulerAngles;
        }
    }
}
