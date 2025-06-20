using Parameters;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ExternalController : AxisMotionBase
{
    /// <summary>
    /// �L�����o�X�\��
    /// </summary>
    protected override bool isCanvas { get { return true; } }

    protected TagInfo actTag;

    // Start is called before the first frame update
    protected override void Start()
    {
        base.Start();

        // ���j�b�g�ݒ�X�V
        renewUnitSetting();
    }

    /// <summary>
    /// ���j�b�g�ݒ肩�瓮��ݒ�X�V
    /// </summary>
    protected override void renewUnitSetting()
    {
        base.renewUnitSetting();

        // ����^�O�ݒ�
        actTag = ScriptableObject.CreateInstance<TagInfo>();
        actTag.Database = unitSetting.Database;
        actTag.MechId = unitSetting.mechId;
        actTag.Tag = unitSetting.actionSetting.tag;
    }

    /// <summary>
    /// �X�V����
    /// </summary>
    protected override void MyFixedUpdate()
    {
        var data = GlobalScript.GetTagData(actTag);
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
