using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;

public class MPX_RX : UseHeadBase3DScript
{
    #region 変数
    [SerializeField]
    protected List<float> angle;

    protected GameObject mpx;

    protected float r1;
    protected float r2;
    protected float tx = 0;
    protected float tz = 0;

    protected bool isChgPrm = true;

    protected int axisType = 0;

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
        if (axisType == 2)
        {
            // 回転無効化
            z = 0;
        }
        else if (axisType == 3)
        {
        }
        else
        {
            // 設定異常
        }
        angle = kinematics_R(x, y, z);
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
        ddWkY = Mathf.PI + Mathf.Atan2(ddRa * Mathf.Sin(ddTa) + r1 * Mathf.Sin(Mathf.PI - ddWkX), ddRa * Mathf.Cos(ddTa) + r2 * Mathf.Cos(Mathf.PI - ddWkX));
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
        mpx = new GameObject("MPX-RX");
        mpx.transform.parent = unitSetting.moveObject.transform;
        mpx.transform.localPosition = Vector3.zero;
        mpx.transform.localEulerAngles = Vector3.zero;
    }
}
