using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;

public class MPX_R1 : MPX_RX
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
    private float head_offset = 0;
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
        arm1_1.transform.localEulerAngles = new Vector3(ang1_1.x, ang1_1.y, 180 + angle[0]);
        arm1_2.transform.localEulerAngles = new Vector3(ang1_2.x, ang1_2.y, (angle[0] + angle[1]) - 180);
        arm2_1.transform.localEulerAngles = new Vector3(ang2_1.x, ang2_1.y, -angle[1]);
        arm2_2.transform.localEulerAngles = new Vector3(ang2_2.x, ang2_2.y, 180 - (angle[0] + angle[1]));
        arm3.transform.localEulerAngles = new Vector3(ang3.x, ang3.y, offset - angle[1]);
        arm4.transform.localEulerAngles = new Vector3(ang4.x, ang4.y, -90 - arm3.transform.localEulerAngles.z);
        plate.transform.localEulerAngles = new Vector3(angP.x, angP.y, 90 - angle[2] - head_offset);
    }

    /// <summary>
    /// モデル再構築
    /// </summary>
    /// <param name="instance"></param>
    protected override void ModelRestructProcess()
    {
        base.ModelRestructProcess();

        r1 = 200;
        r2 = 200;

        var children = unitSetting.moveObject.GetComponentsInChildren<Transform>().ToList();

        if (children.Find(d => d.name.Contains("W0578936-")) != null)
        {
            axisType = 3;
        }

        // アーム1-1 W0578802-
        var arm1_1Tmp = children.Find(d => d.name.Contains("W0578802-"));
        if (arm1_1Tmp != null)
        {
            arm1_1 = arm1_1Tmp.parent.gameObject;
        }

        // アーム1-2 W0578972-
        var arm1_2Tmp = children.Find(d => d.name.Contains("W0578972-"));
        if (arm1_2Tmp != null)
        {
            arm1_2 = arm1_2Tmp.parent.gameObject;
        }

        // アーム2-1 W0579111-(三角プレート)
        var arm2_1Tmp = children.Find(d => d.name.Contains("W0579111-"));
        if (arm2_1Tmp != null)
        {
            arm2_1 = arm2_1Tmp.parent.gameObject;
        }

        // アーム2-2 W0578963-
        var arm2_2Tmp = children.Find(d => d.name.Contains("W0578963-"));
        if (arm2_2Tmp != null)
        {
            arm2_2 = arm2_2Tmp.parent.gameObject;
        }


        // アーム3 W0578936-
        var arm3Tmp = children.Find(d => d.name.Contains("W0578936-"));
        if (arm3Tmp != null)
        {
            arm3 = arm3Tmp.parent.gameObject;
        }

        // アーム4 W0578981-
        var arm4Tmp = children.Find(d => d.name.Contains("W0578981-"));
        if (arm4Tmp != null)
        {
            arm4 = arm4Tmp.parent.gameObject;
        }

        // プレート 減速機の回転部
        var plateTmp = children.Find(d => d.name.Contains("VRGF-45B60P-8AG8_2^88P3_VRGF-45B60P-8AG8"));
        if (plateTmp != null)
        {
            plate = plateTmp.gameObject;
            angP = plate.transform.localEulerAngles;
        }

        // 親子関係構築
        arm1_1.transform.parent = mpx.transform;
        arm2_1.transform.parent = mpx.transform;
        arm3.transform.parent = mpx.transform;
        arm1_2.transform.parent = arm2_1.transform;
        arm2_2.transform.parent = arm1_1.transform;
        arm4.transform.parent = arm3.transform;
        plate.transform.parent = arm2_2.transform;

        // 初期角度セット
        ang1_1 = arm1_1.transform.localEulerAngles;
        ang1_2 = arm1_2.transform.localEulerAngles;
        ang2_1 = arm2_1.transform.localEulerAngles;
        ang2_2 = arm2_2.transform.localEulerAngles;
        ang3 = arm3.transform.localEulerAngles;
        ang4 = arm4.transform.localEulerAngles;
        angP = plate.transform.localEulerAngles;

        // ヘッドセット
        if (HeadObject != null)
        {
            HeadObject.transform.parent = plate.transform;
            head_offset = HeadObject.transform.localEulerAngles.z;
        }
    }
}
