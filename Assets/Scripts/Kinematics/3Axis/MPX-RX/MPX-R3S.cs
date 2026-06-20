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
    #endregion 変数

    /// <summary>
    /// 目標位置セット
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    public override void SetTarget(float x, float y, float z)
    {
        base.SetTarget(x, y, z);
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
        isFin = children.Find(d => d.name.Contains("W0282640-")) != null;
        isGround = Mathf.Abs(unitSetting.unitObject.transform.localEulerAngles.z) > 90;

        r1 = 260;
        r2 = 260;

        baseObj.name += isFin ? "D" : "T";
        var arm1Name = "W0282303-";
        var arm2_1Name = "W0143305-";
        var arm2_2Name = "W0282552-";
        var finName1 = "W0282640-";
        var finName2 = "W0282589-";
        var arm3Name = isFin ? "W0282428-" : "";
        var arm45Name = "W0282604-";
        var plateName = isFin ? "W0282631-" : "";

        // アーム1 W0250623-
        var arm1Tmp = children.Find(d => d.name.Contains(arm1Name));
        if (arm1Tmp != null)
        {
            arm1 = arm1Tmp.parent.gameObject;
        }

        // アーム2-1 W0250562-
        var arm2_1Tmp = children.Find(d => d.name.Contains(arm2_1Name));
        if (arm2_1Tmp != null)
        {
            arm2_1 = arm2_1Tmp.parent.gameObject;
        }

        // アーム2-2 W0250599-
        var arm2_2Tmp = children.Find(d => d.name.Contains(arm2_2Name));
        if (arm2_2Tmp != null)
        {
            arm2_2 = arm2_2Tmp.parent.gameObject;
        }

        // 自己保持用フィン W0459419-, W0282640-
        var armFinTmp1 = children.Find(d => d.name.Contains(finName1));
        if (armFinTmp1 != null)
        {
            fin_1 = armFinTmp1.parent.gameObject;
        }
        var armFinTmp2 = children.Find(d => d.name.Contains(finName2));
        if (armFinTmp2 != null)
        {
            fin_2 = armFinTmp2.parent.gameObject;
        }
        if (isFin)
        {
            // θ固定
            // アーム4 W0263919-
            var arm45Tmp = children.FindAll(d => d.name.Contains(arm45Name));
            if (arm45Tmp.Count == 4)
            {
                arm4_1 = arm45Tmp[0].parent.gameObject;
                arm4_2 = arm45Tmp[1].parent.gameObject;
                arm5_1 = arm45Tmp[2].parent.gameObject;
                arm5_2 = arm45Tmp[3].parent.gameObject;
            }
        }
        // アーム3 W0250614-
        var arm3Tmp = children.Find(d => d.name.Contains(arm3Name));
        if (arm3Tmp != null)
        {
            arm3 = arm3Tmp.parent.gameObject;
        }

        // プレート W0250632- W0668220- W0655776-
        var plateTmp = children.Find(d => d.name.Contains(plateName));
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
            plate = plateTmp.parent.parent.gameObject;
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
