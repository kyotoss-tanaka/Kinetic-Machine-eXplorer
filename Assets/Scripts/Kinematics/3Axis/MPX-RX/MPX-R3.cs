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

        if (children.Find(d => d.name.Contains("W0250623-")) != null)
        {
            axisType = 3;
        }
        isFin = children.Find(d => d.name.Contains("W0459419-") || d.name.Contains("W0282640-")) != null;

        baseObj.name += isFin ? "D" : "T";
        var arm1Name = "W0250623-";
        var arm2_1Name = "W0250562-";
        var arm2_2Name = "W0250599-";
        var finName = "W0459419-";
        var arm3Name = isFin ? "W0262345-" : "W0250614-";
        var arm4Name = "W0263919-";
        var arm5Name = "W0263937-";
        var plateName = isFin ? "W0370723-" : "W0250632-";

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
        var armFinTmp = children.Find(d => d.name.Contains(finName));
        if (armFinTmp != null)
        {
            fin = armFinTmp.parent.gameObject;
        }
        if (isFin)
        {
            // θ固定
            // アーム4 W0263919-
            var arm4Tmp = children.Find(d => d.name.Contains(arm4Name));
            if (arm4Tmp != null)
            {
                arm4 = arm4Tmp.parent.gameObject;
            }

            // アーム5 W0263937-
            var arm5Tmp = children.Find(d => d.name.Contains(arm5Name));
            if (arm5Tmp != null)
            {
                arm5 = arm5Tmp.parent.gameObject;
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
            plate = plateTmp.parent.gameObject;
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
