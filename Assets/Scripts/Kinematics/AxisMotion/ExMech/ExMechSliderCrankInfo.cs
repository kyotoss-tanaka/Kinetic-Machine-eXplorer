using Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// スライダクランク機構
/// </summary>
public class SliderCrankInfo : ExMechInfo
{
    public bool modeB = false;
    public float armM;
    private Vector3 rodDir;
    public GameObject pntAObject;
    public GameObject pntBObject;
    public GameObject pntFarObject;
    public Vector3 pntBOffset;
    private Quaternion rotation = new();
    private float yOffset;
    private Vector2 pntBGuidePos;
    private Vector2 pntBGuideOffset;
    Quaternion initRotRotation;
    float initMainAngleOffset;
    float initSliderOffset;
    float initSliderZeroX;

    public override Vector3 movePos
    {
        get
        {
            return Quaternion.Inverse(rotation) * (new Vector3((pntBGuidePos - pntBGuideOffset).x, 0, 0));
        }
    }
    private Vector2 pntAGuidePos
    {
        get
        {
            var pos = rotation * guideSpace.transform.InverseTransformPoint(pntAObject.transform.position);
            return new Vector2(pos.x, pos.y);
        }
    }
    /// <summary>
    /// 初期化処理
    /// </summary>
    public override void Initialize()
    {
        base.Initialize();

        if (exModeChange)
        {
            initExPos = sliderAxis.model.transform.localPosition;
        }

        // 一旦コンロッドの一番遠いオブジェクト取得
        var farPnt = GetModelFarPoint(pntAAxis.model, ref pntFarObject, new Vector3(1, 1, 0));
        var rodMax = Mathf.Max(Mathf.Abs(farPnt.x), Mathf.Abs(farPnt.y));
        rodDir = new Vector3
        {
            x = rodMax == Mathf.Abs(farPnt.x) ? 1 : 0,
            y = rodMax == Mathf.Abs(farPnt.y) ? 1 : 0,
            z = rodMax == Mathf.Abs(farPnt.z) ? 1 : 0
        };
        // 伸びてる方向がわかったので再度取得
        farPnt = GetModelFarPoint(pntAAxis.model, ref pntFarObject, rodDir);
        var pos = pntAAxis.model.transform.TransformPoint(farPnt);
        pntBOffset = guideSpace.transform.InverseTransformPoint(pos);

        // コンロッドの主軸側判定
        var tmpA = Vector3.Scale(mainAxis.model.transform.InverseTransformPoint(pntAAxis.root.position), mainMask);
        var tmpB = Vector3.Scale(mainAxis.model.transform.InverseTransformPoint(pntFarObject.transform.position), mainMask);

        // 制御対象オブジェクトを作成
        pntAObject = new GameObject("PointA");
        pntBObject = new GameObject("PointB");

        // コンロッドの根元がどちら側かチェック(主軸の軸上にいるか)
        if (CheckRod(tmpA, tmpB))
        {
            // AとB入れ替え
            armL = Vector3.Distance(Vector3.zero, tmpB);
            modeB = true;
            pntBOffset = pntAOffset;
            pntAOffset = guideSpace.transform.InverseTransformPoint(pos);
            pntAObject.transform.position = pntFarObject.transform.position;
            pntAObject.transform.eulerAngles = pntFarObject.transform.eulerAngles;
            pntBObject.transform.position = pntAAxis.root.position;
        }
        else
        {
            pntAObject.transform.position = pntAAxis.root.position;
            pntAObject.transform.eulerAngles = pntAAxis.model.transform.eulerAngles;
            pntBObject.transform.position = pntFarObject.transform.position;
        }
        pntAObject.transform.parent = mainAxis.model.transform;
        pntBObject.transform.parent = sliderAxis.model.transform;

        // コンロッドの親設定
        pntAAxis.SetParent(guideSpace);

        // コンロッドの方向取得（回転を適用するroot基準の座標系で取る）
        var conA = pntAAxis.root.InverseTransformPoint(guideSpace.transform.TransformPoint(Vector3.Scale(pntAOffset, moveMask)));
        var conB = pntAAxis.root.InverseTransformPoint(guideSpace.transform.TransformPoint(Vector3.Scale(pntBOffset, moveMask)));
        var conAB = conB - conA;
        armM = Mathf.Max(Mathf.Abs(conAB.x), Mathf.Max(Mathf.Abs(conAB.y), Mathf.Abs(conAB.z)));
        if (armM == conAB.x)
        {
            rodDir = conAB.x < 0 ? Vector3.left : Vector3.right;
        }
        else if (armM == conAB.y)
        {
            rodDir = conAB.y < 0 ? Vector3.up : Vector3.down;
        }
        else
        {
            rodDir = conAB.z < 0 ? Vector3.forward : Vector3.back;
        }
        // 初期姿勢（回転を適用するroot基準）
        initRotRotation = Quaternion.Euler(Vector3.Scale(pntAAxis.root.localEulerAngles, rodDir));

        // ガイド基準に変更
        var pntA = Vector3.Scale(pntAOffset, moveMask);
        var pntB = Vector3.Scale(pntBOffset, moveMask);

        // ガイドの方向別処理
        if ((guideDir == Vector3.right) || (guideDir == Vector3.left))
        {
            // ガイドがX方向
            // マイナス方向判定
            var xminus = (pntB - pntA).x < 0 ? 1 : 0;
            if ((moveDir == Vector3.forward) || (moveDir == Vector3.back))
            {
                // 回転軸がZ
                var yminus = pntA.y < 0 ? 1 : 0;
                rotation = Quaternion.Euler(xminus != yminus ? 180 : 0, 0, xminus * 180);
            }
            else if ((moveDir == Vector3.up) || (moveDir == Vector3.down))
            {
                // 回転軸がY
                var yminus = pntA.z < 0 ? 1 : 0;
                rotation = Quaternion.Euler((xminus != yminus ? 180 : 0) + 90, xminus * 180, 0);
            }
        }
        else if ((guideDir == Vector3.up) || (guideDir == Vector3.down))
        {
            // ガイドがY方向
            var xminus = (pntB - pntA).y < 0 ? 1 : 0;
            if ((moveDir == Vector3.forward) || (moveDir == Vector3.back))
            {
                // 回転軸がZ
                var yminus = pntA.x < 0 ? 1 : 0;

            }
            else if ((moveDir == Vector3.right) || (moveDir == Vector3.left))
            {
                // 回転軸がX
                var yminus = pntA.z < 0 ? 1 : 0;
                rotation = Quaternion.Euler(xminus * 180 + 90, xminus != yminus ? 180 : 0, -90);
            }
        }
        else
        {
            // ガイドがZ方向
        }
        var tmp = rotation * pntB;
        yOffset = tmp.y;
        pntBGuideOffset = new Vector2(tmp.x, tmp.y);
        // 計算空間（原点=主軸の回転中心）
        calcSpace = new GameObject("CalcSpace");
        calcSpace.transform.parent = workSpace.transform.parent;
        calcSpace.transform.position = mainAxis.root.position;
        calcSpace.transform.localRotation = guideSpace.transform.localRotation * rotation;
        calcSpace.transform.localScale = new(1, 1, 1);
        if (exModeChange)
        {
            // 計算空間へ移動
            mainAxis.root.parent = calcSpace.transform;
            sliderOffset = calcSpace.transform.InverseTransformPoint(sliderAxis.root.position);
        }
        // スライダ初期位置
        initSliderZeroX = Mathf.Sqrt(armM * armM - (armL - yOffset) * (armL - yOffset));
        initSliderOffset = initSliderZeroX - pntBGuideOffset.x;
        // 主軸の初期角度（回転を適用するroot基準）
        var initMainAngle = (Quaternion.Inverse(calcSpace.transform.rotation) * mainAxis.root.rotation).eulerAngles.z;
        // 初回の逆解
        var result = InverseKinematics(new Vector3(pntBGuideOffset.x, yOffset, 0));
        var th = result.theta[0] > result.theta[1] ? result.theta[0] : result.theta[1];
        initMainAngleOffset = th - initMainAngle;
        // 初期値
        nowPos.y = yOffset;
        nowPos.z = 0;
        nowAngle.x = 0;
        nowAngle.y = 0;
    }

    /// <summary>
    /// スライダー位置セット
    /// </summary>
    public override void RenewPos()
    {
        if (exModeChange)
        {
            // スライダ位置計算
            var m = moveExPos.x + moveExPos.y + moveExPos.z;
            var move = new Vector3
            {
                x = m + initSliderOffset,
                y = 0,
                z = 0
            };
            sliderAxis.root.position = calcSpace.transform.TransformPoint(sliderOffset + move);
            // 逆解
            var result = InverseKinematics(new Vector3(initSliderZeroX + m, yOffset, 0));
            if (result.valid)
            {
                var th = result.theta[0] > result.theta[1] ? result.theta[0] : result.theta[1];
                mainAxis.root.localEulerAngles = new Vector3(0, 0, th - initMainAngleOffset);
            }
            // スライダ位置
            nowPos.x = m;
        }
        else
        {
            // 2次元で計算
            var y = pntAGuidePos.y - yOffset;
            var x = MathF.Sqrt(armM * armM - y * y);
            pntBGuidePos = new Vector2(pntAGuidePos.x + x, yOffset);
            // スライダの位置
            sliderAxis.root.position = guideSpace.transform.TransformPoint(sliderOffset + Vector3.Scale(movePos, guideDir));
            nowPos.x = x - initSliderOffset;
        }
        // コンロッド端の取得
        var posA = Vector3.Scale(guideSpace.transform.InverseTransformPoint(pntAObject.transform.position), moveMask);
        var posB = Vector3.Scale(guideSpace.transform.InverseTransformPoint(pntBObject.transform.position), moveMask);
        if (modeB)
        {
            pntAAxis.root.position = pntBObject.transform.position;
            // コンロッドの向き
            var rot = Quaternion.FromToRotation(rodDir, posA - posB) * Quaternion.Inverse(initRotRotation);
            pntAAxis.root.localRotation = rot;
        }
        else
        {
            pntAAxis.root.position = pntAObject.transform.position;
            // コンロッドの向き
            var rot = Quaternion.FromToRotation(rodDir, posA - posB) * Quaternion.Inverse(initRotRotation);
            pntAAxis.root.localRotation = rot;
        }
        // 角度と座標取得
        nowAngle.z = NormalizeAngle(90 - (Quaternion.Inverse(calcSpace.transform.rotation) * mainAxis.root.rotation).eulerAngles.z - initMainAngleOffset);
    }

    /// <summary>
    /// 逆解を解く
    /// </summary>
    /// <param name="pos"></param>
    /// <returns></returns>
    protected override SolveResult InverseKinematics(Vector3 pos)
    {
        SolveResult res = new SolveResult();
        // 基本検査
        if (armL <= 0f || armM <= 0f)
        {
            res.valid = false;
            res.message = "r または l が正でない";
            return res;
        }

        float A = pos.x;
        float B = pos.y;
        float C = (pos.x * pos.x + pos.y * pos.y + armL * armL - armM * armM) / (2f * armL);

        float R = Mathf.Sqrt(A * A + B * B);
        const float EPS = 1e-6f;

        if (R <= EPS)
        {
            // R が0に近い = (x,h) が (0,0) に非常に近い（稀）
            // その場合 AとBが小さく解の安定性が悪い
            res.valid = false;
            res.message = "R が小さすぎて不安定（x,h が原点近傍）";
            return res;
        }

        float ratio = C / R;
        // 数値誤差で +-1 を少し超える可能性があるためクランプ
        if (ratio > 1f + 1e-5f || ratio < -1f - 1e-5f)
        {
            res.valid = false;
            res.message = "物理的に解なし (|C| > R)";
            return res;
        }

        ratio = Mathf.Clamp(ratio, -1f, 1f);

        float alpha = Mathf.Atan2(B, A); // 基準角
        float delta = Mathf.Acos(ratio); // 0..π

        // 二つの解
        float theta1 = alpha + delta;
        float theta2 = alpha - delta;

        // 正規化（-π..π）
        theta1 = NormalizeRad(theta1);
        theta2 = NormalizeRad(theta2);

        res.valid = true;
        res.theta = new();
        res.theta.Add(theta1 * Mathf.Rad2Deg);
        res.theta.Add(theta2 * Mathf.Rad2Deg);
        res.message = "ok";

        // chosen の選択ロジック
        var prevThetaRad = mainAxis.root.localEulerAngles.z - initMainAngleOffset;
        if (!float.IsNaN(prevThetaRad))
        {
            // 正規化して差の小さい方を選ぶ
            float p = NormalizeRad(prevThetaRad);
            float d1 = Mathf.Abs(NormalizeRad(theta1 - p));
            float d2 = Mathf.Abs(NormalizeRad(theta2 - p));
            res.chosen = ((d1 <= d2) ? theta1 : theta2) * Mathf.Rad2Deg;
        }
        else
        {
            // prev が無ければ、物理的に「クランク角がより上死点寄り（小さい絶対値）」などの基準で選ぶ
            // ここでは |theta| が小さい方を選ぶ（用途によって変更可）
            res.chosen = ((Mathf.Abs(theta1) <= Mathf.Abs(theta2)) ? theta1 : theta2) * Mathf.Rad2Deg;
        }
        return res;
    }

    /// <summary>
    /// 根元がBかチェック
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    private bool CheckRod(Vector3 a, Vector3 b)
    {
        var dataA = new List<float>
            {
                Math.Abs(a.x + a.y),
                Math.Abs(a.x + a.z),
                Math.Abs(a.y + a.z)
            };
        var dataB = new List<float>
            {
                Math.Abs(b.x + b.y),
                Math.Abs(b.x + b.z),
                Math.Abs(b.y + b.z)
            };
        var minA = dataA.Min();
        var minB = dataB.Min();
        return minA > minB;
    }
}