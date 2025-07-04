using Parameters;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

public class MotionActionTable : AxisMotionBase
{
    /// <summary>
    /// �L�����o�X�\��
    /// </summary>
    protected override bool isCanvas { get { return true; } }

    /// <summary>
    /// �f�[�^�{��
    /// </summary>
    protected float rate;

    /// <summary>
    /// �蓮�ݒ�
    /// </summary>
    [SerializeField]
    protected bool isManual;

    /// <summary>
    /// ���ݎ���
    /// </summary>
    [SerializeField]
    protected int time;

    /// <summary>
    /// ���݃T�C�N��
    /// </summary>
    [SerializeField]
    protected int cycle;

    /// <summary>
    /// �e�[�u���ʒu
    /// </summary>
    [SerializeField]
    protected decimal value;

    /// <summary>
    /// ���݈ʒu
    /// </summary>
    [SerializeField]
    protected float position;

    /// <summary>
    /// �I�t�Z�b�g
    /// </summary>
    [SerializeField]
    protected int offset;

    /// <summary>
    /// ����e�[�u��
    /// </summary>
    [SerializeField]
    protected ActionTableData? actionTableData;

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

        // �T�C�N���^�O�ݒ�
        var tag = GlobalScript.callbackTags.Find(d => d.database == unitSetting.Database);
        cycleTag = tag == null ? null : tag.cycle;

        rate = (float)unitSetting.actionSetting.rate;

        offset = unitSetting.actionSetting.offset;

        // �e�[�u���擾
        actionTableData = GlobalScript.actionTableDatas.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
        // ���Ԃ��ƂɃ\�[�g
        actionTableData.datas = actionTableData.datas.OrderBy(d => d.time).ToList();
    }

    /// <summary>
    /// �X�V����
    /// </summary>
    protected override void MyFixedUpdate()
    {  
        time = GlobalScript.GetTagData(cycleTag);
        if (!isManual)
        {
            cycle = time % unitSetting.actionSetting.cycle;
        }
        if ((actionTableData != null) && (actionTableData.datas.Count > 0))
        {
            value = 0;
            var before = actionTableData.datas.LastOrDefault(d => d.time <= cycle);
            var after = actionTableData.datas.FirstOrDefault(d => d.time >= cycle);
            if (before != null && after != null && before.time != after.time)
            {
                value = before.value + (after.value - before.value) * (cycle - before.time) / (after.time - before.time);
            }
            else
            {
                value = before != null ? before.value : (after != null ? after.value : value);
            }
            position = (float)(value + offset) / (rate == 0 ? 1000f : rate) * unitSetting.actionSetting.dir;
            if (isRotate)
            {
                moveObject.transform.localEulerAngles = moveDir * position;
            }
            else
            {
                moveObject.transform.localPosition = moveDir * position;
            }
        }
    }
}
