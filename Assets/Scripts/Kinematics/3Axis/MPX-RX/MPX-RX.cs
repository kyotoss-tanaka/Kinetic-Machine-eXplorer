using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;

/// <summary>
/// MPX-R35-3   現行R2
/// MPX-R7-3    現行R3
/// MPX-R1-3    現行R6
/// 命名ルール：MPX-Ra-b
/// MPX: 機構名(パラレルならMPS？)
/// Ra: モータ容量(35:3.5KW 7:750W 1: 100W もう一桁増やしてもいいかも？)
/// b: タイプ(2: 2軸タイプ3: 3軸タイプ)
/// </summary>
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

    protected int axisType = 0;

    /// <summary>
    /// 逆勝手
    /// </summary>
    protected bool isRvs = false;

    #endregion 変数
    /// <summary>
    /// 開始処理
    /// </summary>
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
            // 回転あり
            if (robo.isTm)
            {
                z /= 1000f;
            }
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
        ret.Add(float.IsNaN(ddWkX) ? 0 : isRvs ? -ddWkX : ddWkX);
        ret.Add(float.IsNaN(ddWkY) ? 0 : isRvs ? -ddWkY : ddWkY);
        ret.Add(float.IsNaN(ddWkZ) ? 0 : isRvs ? -ddWkZ : ddWkZ);
        return ret;
    }

    /// <summary>
    /// 順運動学（kinematics_R の逆）。a0～a2 は kinematics_R の戻り値（逆勝手の符号込み）。
    /// 戻り値は SetTarget に渡す形。r1 = r2 の型で逆解と一致することを数値で確かめてある
    /// </summary>
    protected Vector3 ForwardMPX(float a0, float a1, float a2)
    {
        var s = isRvs ? -1f : 1f;
        var j1 = s * a0 * Mathf.Deg2Rad;
        var j2 = s * a1 * Mathf.Deg2Rad;
        var zDeg = s * a2 + s * a1;   // 逆解は j3 = z - j2
        var px = r1 * Mathf.Cos(Mathf.PI - j1) + r2 * Mathf.Cos(j2);
        var pz = r1 * Mathf.Sin(Mathf.PI - j1) + r2 * Mathf.Sin(j2);
        var zr = zDeg * Mathf.Deg2Rad;
        var x = px + tz * Mathf.Cos(zr) + tx * Mathf.Sin(zr);
        var y = pz + tz * Mathf.Sin(zr) - tx * Mathf.Cos(zr);
        if (axisType == 2)
        {
            zDeg = 0f;
        }
        else if ((axisType == 3) && (robo != null) && robo.isTm)
        {
            zDeg *= 1000f;   // SetTarget で 1/1000 にされるため
        }
        return new Vector3(x, y, Mathf.DeltaAngle(0f, zDeg));
    }

    /// <summary>
    /// モデル再構築
    /// </summary>
    /// <param name="instance"></param>
    protected virtual GameObject ModelRestructProcess(string name)
    {
        mpx = new GameObject(name);
        mpx.transform.parent = unitSetting.moveObject.transform;
        mpx.transform.localPosition = Vector3.zero;
        mpx.transform.localEulerAngles = Vector3.zero;
        return mpx;
    }
}
