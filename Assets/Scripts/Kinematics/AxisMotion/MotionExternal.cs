using Parameters;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MotionExternal : AxisMotionBase
{
    /// <summary>
    /// キャンバス表示
    /// </summary>
    protected override bool isCanvas { get { return true; } }

    protected TagInfo actTag;

    protected float rate;

    // Start is called before the first frame update
    protected override void Start()
    {
        base.Start();

        // ユニット設定更新
        renewUnitSetting();
    }

    /// <summary>
    /// ユニット設定から動作設定更新
    /// </summary>
    protected override void renewUnitSetting()
    {
        base.renewUnitSetting();

        // 動作タグ設定
        actTag = ScriptableObject.CreateInstance<TagInfo>();
        actTag.Database = unitSetting.Database;
        actTag.MechId = unitSetting.mechId;
        actTag.Tag = unitSetting.actionSetting.tag;

        rate = (float)unitSetting.actionSetting.rate;
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void MyFixedUpdate()
    {
        var data = GlobalScript.GetTagData(actTag) / (rate == 0 ? 1000f : rate);
        if (isRotate)
        {
            moveObject.transform.localEulerAngles = moveDir * data;
        }
        else
        {
            moveObject.transform.localPosition = moveDir * data;
        }
    }
}
