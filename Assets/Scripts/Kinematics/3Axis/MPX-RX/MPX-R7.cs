using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;

public class MPX_R7 : MPX_RX
{
    #region 変数
    protected GameObject arm1;
    protected GameObject arm2_1;
    protected GameObject arm2_2;
    protected GameObject arm3;
    protected GameObject plate;

    private Vector3 ang1;
    private Vector3 ang2_1;
    private Vector3 ang2_2;
    private Vector3 ang3;
    private Vector3 angP;
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
        arm1.transform.localEulerAngles = new Vector3(ang1.x, ang1.y, angle[1]);
        arm2_1.transform.localEulerAngles = new Vector3(ang2_1.x, ang2_1.y, angle[0] - 180);
        arm2_2.transform.localEulerAngles = new Vector3(ang2_2.x, ang2_2.y, -angle[1] - angle[0]);
        arm3.transform.localEulerAngles = new Vector3(ang3.x, ang3.y, -angle[0] - angle[1] + 180);
        plate.transform.localEulerAngles = new Vector3(angP.x, angP.y, 90 - angle[2]);
    }

    /// <summary>
    /// モデル再構築
    /// </summary>
    /// <param name="instance"></param>
    protected override void ModelRestructProcess()
    {
        base.ModelRestructProcess();

        r1 = 480;
        r2 = 480;

        var children = unitSetting.moveObject.GetComponentsInChildren<Transform>().ToList();

        if (children.Find(d => d.name.Contains("W0250623-")) != null)
        {
            axisType = 3;
        }

        // アーム1 W0250623-
        var arm1Tmp = children.Find(d => d.name.Contains("W0250623-"));
        if (arm1Tmp != null)
        {
            arm1 = arm1Tmp.parent.gameObject;
        }

        // アーム2-1 W0250562-
        var arm2_1Tmp = children.Find(d => d.name.Contains("W0250562-"));
        if (arm2_1Tmp != null)
        {
            arm2_1 = arm2_1Tmp.parent.gameObject;
        }

        // アーム2-2 W0250599-
        var arm2_2Tmp = children.Find(d => d.name.Contains("W0250599-"));
        if (arm2_2Tmp != null)
        {
            arm2_2 = arm2_2Tmp.parent.gameObject;
        }

        // アーム3 W0250614-
        var arm3Tmp = children.Find(d => d.name.Contains("W0250614-"));
        if (arm3Tmp != null)
        {
            arm3 = arm3Tmp.parent.gameObject;
        }

        // プレート W0250632-
        var plateTmp = children.Find(d => d.name.Contains("W0250632-"));
        if (plateTmp != null)
        {
            plate = plateTmp.parent.gameObject;
            angP = plate.transform.localEulerAngles;
        }

        // 親子関係構築
        arm1.transform.parent = mpx.transform;
        arm2_1.transform.parent = mpx.transform;
        arm2_2.transform.parent = arm1.transform;
        arm3.transform.parent = arm2_1.transform;
        plate.transform.parent = arm3Tmp.transform;

        // 初期角度セット
        ang1 = arm1.transform.localEulerAngles;
        ang2_1 = arm2_1.transform.localEulerAngles;
        ang2_2 = arm2_2.transform.localEulerAngles;
        ang3 = arm3.transform.localEulerAngles;

        // ヘッドセット
        if (HeadObject != null)
        {
            HeadObject.transform.parent = plate.transform;
        }
    }
}
