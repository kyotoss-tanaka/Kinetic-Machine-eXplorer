using Parameters;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MotionExternal : AxisMotionBase
{
    /// <summary>
    /// �L�����o�X�\��
    /// </summary>
    protected override bool isCanvas { get { return true; } }

    /// <summary>
    /// ����^�O
    /// </summary>
    [SerializeField]
    protected TagInfo actTag;

    /// <summary>
    /// �䗦
    /// </summary>
    protected float rate;

    // Start is called before the first frame update
    protected override void Start()
    {
        base.Start();

        // ���j�b�g�ݒ�X�V
        RenewMoveDir();
    }

    /// <summary>
    /// ���j�b�g�ݒ肩�瓮��ݒ�X�V
    /// </summary>
    public override void RenewMoveDir()
    {
        base.RenewMoveDir();

        rate = (float)unitSetting.actionSetting.rate;
    }

    /// <summary>
    /// �X�V����
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
