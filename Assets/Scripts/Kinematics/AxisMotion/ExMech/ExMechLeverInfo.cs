using Parameters;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// レバー機構
/// </summary>
public class ExMechLeverInfo : ExMechInfo
{
    public override void Initialize()
    {
        base.Initialize();

        if (exModeChange)
        {
            initExPos = sliderAxis.model.transform.localPosition;
        }

        // カムフォロアの親を主軸に
        pntAAxis.model.transform.parent = mainAxis.model.transform;
    }

    /// <summary>
    /// スライダー位置セット
    /// </summary>
    public override void RenewPos()
    {
        base.RenewPos();
        if (exModeChange)
        {
        }
        else
        {
            sliderAxis.model.transform.position = guideSpace.transform.TransformPoint(sliderOffset + Vector3.Scale(movePos, guideDir));
        }
    }
}
