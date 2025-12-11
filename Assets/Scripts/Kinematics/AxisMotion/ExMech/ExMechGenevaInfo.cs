using Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst.Intrinsics;
using UnityEngine;

/// <summary>
/// ゼネバ機構
/// </summary>
public class ExMechGenevaInfo : ExMechInfo
{
    public GameObject pntAObject;
    private Quaternion mainOffsetRot;
    private float initDrivenOffset;
    private Vector3 initPosition;
    private Vector3 initDirvenAng;
    private Vector3 initSliderAng;

    /// <summary>
    /// 初期化処理
    /// </summary>
    public override void Initialize()
    {
        // 制御対象オブジェクトを作成
        pntAObject = new GameObject("PointA");
        pntAObject.transform.parent = mainAxis.model.transform;
        pntAObject.transform.position = sliderAxis.model.transform.position;

        //　主軸
        mainOffsetRot = mainAxis.model.transform.rotation;

        // 計算空間作成
        calcSpace = new GameObject("CalcSpace");
        calcSpace.transform.parent = workSpace.transform.parent;
        calcSpace.transform.position = workSpace.transform.position;
        calcSpace.transform.localRotation = Quaternion.FromToRotation(mainAxis.model.transform.localRotation * Vector3.right, Vector3.Scale((pntAObject.transform.localPosition - mainAxis.model.transform.localPosition), mainMask).normalized) * mainAxis.model.transform.localRotation;
        moveDir = new Vector3(0, 0, 1);

        // 従動軸
        guideDir = guideAxis.model.transform.InverseTransformDirection(calcSpace.transform.forward);
        initDrivenOffset = GetDriveAngle();
        initDirvenAng = guideAxis.model.transform.localEulerAngles;

        // スライダ軸
        sliderDir = sliderAxis.model.transform.InverseTransformDirection(calcSpace.transform.forward);
        initSliderAng = sliderAxis.model.transform.localEulerAngles;

        // 初期位置
        initPosition = calcSpace.transform.InverseTransformPoint(pntAObject.transform.position);
    }

    /// <summary>
    /// スライダー位置セット
    /// </summary>
    public override void RenewPos()
    {
        base.RenewPos();

        // 角度計算
        var th = (GetDriveAngle() - initDrivenOffset);

        // 従動軸の計算
        guideAxis.model.transform.localEulerAngles = th * guideDir + Vector3.Scale(initDirvenAng, guideMask);

        // スライダ軸の計算
        Quaternion deltaMain = mainAxis.model.transform.rotation * Quaternion.Inverse(mainOffsetRot);
        sliderAxis.model.transform.position = pntAObject.transform.position;
        sliderAxis.model.transform.localEulerAngles = th * sliderDir + Vector3.Scale(initSliderAng, sliderMask);

        // 座標更新
        nowPos = Vector3.Scale(calcSpace.transform.InverseTransformPoint(pntAObject.transform.position) - initPosition, new Vector3(-1, 1, 0));
        nowAngle = mainAxis.model.transform.localEulerAngles;
    }

    /// <summary>
    /// 従動軸角度取得
    /// </summary>
    /// <returns></returns>
    private float GetDriveAngle()
    {
        var pntA = Vector3.Scale(calcSpace.transform.InverseTransformPoint(pntAObject.transform.position), moveMask);
        var pntG = pntA - Vector3.Scale(calcSpace.transform.InverseTransformPoint(guideAxis.model.transform.position), moveMask);
        return Mathf.Atan2(pntG.y, pntG.x) * Mathf.Rad2Deg;
    }
}
