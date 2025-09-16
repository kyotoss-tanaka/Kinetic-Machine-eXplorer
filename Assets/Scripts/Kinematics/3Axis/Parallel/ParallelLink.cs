using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;

public class ParallelLink : UseHeadBase3DScript
{
    #region 列挙型
    [Serializable]
    protected enum ParallelType
    {
        /// <summary>
        /// タイプ無し
        /// </summary>
        None,
        /// <summary>
        /// 村田パラレル(3軸)
        /// </summary>
        MPS2_3AS,
        /// <summary>
        /// 村田パラレル(4軸)
        /// </summary>
        MPS2_4AS,
        /// <summary>
        /// 変則パラレル
        /// </summary>
        MPX_PI,
        /// <summary>
        /// 川重パラレル
        /// </summary>
        YF03N4,
    }

    #endregion 列挙型

    #region 変数
    [SerializeField]
    protected List<List<float>> angle;

    [SerializeField]
    protected ParallelType parallelType; 

    protected static float DEGREES = 180 / Mathf.PI;
    protected static float RADIANS = Mathf.PI / 180;

    protected static int AXIS_MAX = 3;
    protected float[] ARM_OFFSET = { 0, 120, 240};
    protected float[] ARM_RAD_OFFSET = new float[AXIS_MAX];

    protected float ARM1_OFFSET = -5.12f;
    protected float ARM1_RAD_OFFSET;

    protected float SPRING1_OFFSET_X = 0.035f;
    protected float SPRING2_OFFSET_X = 0.765f;
    protected float SPRING_OFFSET_Y = 0.05f;

    protected float[] fL = { 350, 350, 350 };
    protected float[] fM = { 800, 800, 800 };
    protected float[] fH = { 225, 225, 225 };
    protected float[] fSH = { 70, 70, 70 };

    protected float[] fSH_H = new float[AXIS_MAX];
    protected float[] fL2 = new float[AXIS_MAX];
    protected float[] fM2 = new float[AXIS_MAX];
    protected float[] fH2 = new float[AXIS_MAX];
    protected float[] fSH2 = new float[AXIS_MAX];

    protected List<Transform> arm1 = new List<Transform>();
    protected List<Transform> arm2 = new List<Transform>();
    protected List<Transform> armSpring = new List<Transform>();
    protected Transform plate;
    bool isChgPrm = true;
    #endregion 変数

    public ParallelLink()
    {
        tzMin = 600;
        tzMax = 1000;
    }

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
            for (var i = 0; i < AXIS_MAX; i++)
            {
                ARM_RAD_OFFSET[i] = ARM_OFFSET[i] * RADIANS;
                fSH_H[i] = fSH[i] - fH[i];
                fL2[i] = fL[i] * fL[i];
                fM2[i] = fM[i] * fM[i];
                fH2[i] = fH[i] * fH[i];
                fSH2[i] = fSH[i] * fSH[i];
            }
            ARM1_RAD_OFFSET = ARM1_OFFSET * RADIANS;

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
        /*
        var angle = kinematics_R(x, y, z);
        for (var i = 0; i < AXIS_MAX; i++)
        {
            // アーム1の位置
            arm1[i].transform.localEulerAngles = new Vector3(arm1[i].transform.localEulerAngles.x, arm1[i].transform.localEulerAngles.y, angle[i][0]);
            // アーム2の位置
            arm2_1[i].transform.localEulerAngles = new Vector3(0, -angle[i][1], angle[i][2]);
            arm2_2[i].transform.localEulerAngles = new Vector3(0, angle[i][1], -angle[i][2]);
        }
        plate.transform.localPosition = new Vector3(y / 1000, -z / 1000, x / 1000);
        */
    }

    /// <summary>
    /// 逆解を解く
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    /// <returns></returns>
    protected virtual List<List<float>> kinematics_R(float x, float y, float z)
    {
        var ret = new List<List<float>>();
        var x2 = x * x;
        var y2 = y * y;
        var z2 = z * z;
        for (var i = 0; i < AXIS_MAX; i++)
        {
            var sinA = Mathf.Sin(-ARM_RAD_OFFSET[i]);
            var cosA = Mathf.Cos(-ARM_RAD_OFFSET[i]);
            var sinAm = Mathf.Sin(ARM_RAD_OFFSET[i]);
            var cosAm = Mathf.Cos(ARM_RAD_OFFSET[i]);
            // Θ1計算
            var wk = x * cosA + y * sinA;
            var v = wk + fSH_H[i];
            var w = (x2 + y2 + z2 + fSH2[i] + fH2[i] + fL2[i] - fM2[i] + 2 * wk * fSH_H[i] - 2 * fSH[i] * fH[i]) / (2 * fL[i]);
            var wk2 = 2 * Mathf.Atan2(z - Mathf.Sqrt(z2 - w * w + v * v), v + w);
            var th1 = wk2;
            if (th1 <= -Mathf.PI)
            {
                th1 = th1 + Mathf.PI * 2;
            }
            else if (th1 >= Mathf.PI)
            {
                th1 = th1 - Mathf.PI * 2;
            }
            // 点Aの座標
            var Ax = fH[i] * cosA;
            var Ay = fH[i] * sinA;
            // 点Bの座標
            var Bx = Ax + fL[i] * Mathf.Cos(wk2) * cosA;
            var By = Ay + fL[i] * Mathf.Cos(wk2) * sinA;
            var Bz = fL[i] * Mathf.Sin(wk2);
            // 点Cの座標
            var Cx = x + fSH[i] * cosA;
            var Cy = y + fSH[i] * sinA;
            var Cz = z;
            // ねじれ計算
            var Bx2 = Bx * cosAm - By * sinAm;
            var Cx2 = Cx * cosAm - Cy * sinAm;
            var M2 = Mathf.Sqrt((Bx2 - Cx2) * (Bx2 - Cx2) + (Bz - Cz) * (Bz - Cz));
            var th3 = Mathf.Acos(M2 / fM[i]);
            if (double.IsNaN(th3))
            {
                th3 = 0;
            }
            else
            if (((i == 0) && (Cy < 0)) || ((i == 1) && (Cy > Cx * sinA / cosA)) || ((i == 2) && (Cy > Cx * sinA / cosA)))
            {
                th3 = -th3;
            }
            var BC = (Cz - Bz);
            M2 = fM[i] * Mathf.Cos(th3);
            var th2 = 0.0f;
            if (Bx2 > Cx2)
            {
                th2 = (Mathf.PI / 2 - th1) + Mathf.Acos(BC / M2);
            }
            else
            {
                th2 = (Mathf.PI / 2 - th1) - Mathf.Acos(BC / M2);
            }
            var tmp = new List<float>();
            tmp.AddRange(new float[] { th1 * DEGREES, th2 * DEGREES, th3 * DEGREES });
            ret.Add(tmp);
        }
        return ret;
    }
}
