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

    /// <summary>
    /// 動作タグ
    /// </summary>
    [SerializeField]
    protected TagInfo actTag;

    /// <summary>
    /// 比率
    /// </summary>
    protected float rate;

    // Start is called before the first frame update
    protected override void Start()
    {
        base.Start();

        // ユニット設定更新
        RenewMoveDir();
    }

    /// <summary>
    /// ユニット設定から動作設定更新
    /// </summary>
    public override void RenewMoveDir()
    {
        base.RenewMoveDir();

        rate = (float)unitSetting.actionSetting.rate;
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void MyFixedUpdate()
    {
        var data = (GetTagValue(unitSetting.actionSetting.tag, ref actTag) / (rate == 0 ? 1000f : rate) + unitSetting.actionSetting.offset / (isRotate ? 1f : 1000f)) * unitSetting.actionSetting.dir;
        if (isRotate)
        {
            moveObject.transform.localEulerAngles = moveDir * data;
            if (chuckSetting != null)
            {
                foreach (var child in chuckSetting.children)
                {
                    child.setting.moveObject.transform.localEulerAngles = moveObject.transform.localEulerAngles * child.dir * child.rate + child.offset * moveDir;
                }
            }
        }
        else
        {
            moveObject.transform.localPosition = moveDir * data;
            if (chuckSetting != null)
            {
                foreach (var child in chuckSetting.children)
                {
                    child.setting.moveObject.transform.localPosition = moveObject.transform.localPosition * child.dir * child.rate + child.offset * moveDir / Thousand;
                }
            }
        }
    }
}
