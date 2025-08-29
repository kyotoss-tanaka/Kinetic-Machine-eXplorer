using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;

public class MPX_R3 : UseHeadBase3DScript
{
    #region 変数
    [SerializeField]
    List<float> angle;

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

    private float r1;
    private float r2;
    private float tx = 0;
    private float tz = 0;
    bool isChgPrm = true;
    #endregion 変数

    protected override void Start()
    {
        base.Start();
        tyMin = r1 / 2;
        tyMax = r1 * 2;
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
        angle = kinematics_R(x, y, z);
        arm1.transform.localEulerAngles = new Vector3(ang1.x, ang1.y, angle[1]);
        arm2_1.transform.localEulerAngles = new Vector3(ang2_1.x, ang2_1.y, angle[0] - 180);
        arm2_2.transform.localEulerAngles = new Vector3(ang2_2.x, ang2_2.y, -angle[1] - angle[0]);
        arm3.transform.localEulerAngles = new Vector3(ang3.x, ang3.y, -angle[0] - angle[1] + 180);
        plate.transform.localEulerAngles = new Vector3(angP.x, angP.y, 90 - angle[2]);
    }

    /// <summary>
    /// 逆解を解く
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    /// <returns></returns>
    protected virtual List<float> kinematics_R(float x, float y, float z)
    {
        var ret = new List<float>();
        var ddWkX = x;
        var ddWkY = y;
        var ddWkZ = z;
        ddWkZ = Mathf.Deg2Rad * ddWkZ;
        var ddPx = ddWkX - tz * Mathf.Cos(ddWkZ) - tx * Mathf.Sin(ddWkZ);
        var ddPz = ddWkY - tz * Mathf.Sin(ddWkZ) + tx * Mathf.Cos(ddWkZ);
        var ddRa = Mathf.Sqrt(ddPx * ddPx + ddPz * ddPz);
        var ddTa = Mathf.Atan2(-ddPz, -ddPx);
        var ddu = (r2 * r2 - ddRa * ddRa - r1 * r1) / (2 * ddRa * r1);
        var ddv = -Mathf.Sqrt(1 - ddu * ddu);
        ddWkX = -(Mathf.Atan2(ddv, ddu) + ddTa) - Mathf.PI;
        ddWkY = Mathf.PI + Mathf.Atan2(ddRa * Mathf.Sin(ddTa) + r1 * Mathf.Sin(Mathf.PI - ddWkX), ddRa * Mathf.Cos(ddTa) +r2 * Mathf.Cos(Mathf.PI - ddWkX));
        if (ddWkY > Mathf.PI)
        {
            ddWkY -= 2 * Mathf.PI;
        }
        else if (ddWkY < -Mathf.PI)
        {
            ddWkY += 2 * Mathf.PI;
        }
        ddWkZ = ddWkZ - ddWkY;
        ddWkX = Mathf.Rad2Deg * ddWkX;
        ddWkY = Mathf.Rad2Deg * ddWkY;
        ddWkZ = Mathf.Rad2Deg * ddWkZ;
        ret.Add(float.IsNaN(ddWkX) ? 0 : ddWkX);
        ret.Add(float.IsNaN(ddWkY) ? 0 : ddWkY);
        ret.Add(float.IsNaN(ddWkZ) ? 0 : ddWkZ);
        return ret;
    }

    /// <summary>
    /// モデル再構築
    /// </summary>
    /// <param name="instance"></param>
    protected override void ModelRestructProcess()
    {
        var mpx = new GameObject("MPX-R3");
        mpx.transform.parent = unitSetting.moveObject.transform;
        var children = unitSetting.moveObject.GetComponentsInChildren<Transform>().ToList();
        
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
            r1 = 480;
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
            r2 = 480;
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
