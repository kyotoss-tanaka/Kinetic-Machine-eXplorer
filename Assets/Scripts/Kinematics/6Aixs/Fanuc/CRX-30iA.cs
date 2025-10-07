using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;

public class CRX_30iA: Kinematics6D
{
    #region �ϐ�
    [SerializeField]
    protected List<float> angle;

    protected GameObject crx;

    protected Transform arm1;
    protected Transform arm2;
    protected Transform arm3;
    protected Transform arm4;
    protected Transform arm5;
    protected Transform arm6;

    private Vector3 ang1;
    private Vector3 ang2;
    private Vector3 ang3;
    private Vector3 ang4;
    private Vector3 ang5;
    private Vector3 ang6;

    protected bool isChgPrm = true;

    protected int axisType = 0;

    #endregion �ϐ�

    protected override void Start()
    {
        base.Start();
    }

    /// <summary>
    /// �p�����[�^�X�V
    /// </summary>
    protected override void RenewParameter()
    {
        if (isChgPrm)
        {
            isChgPrm = false;
        }
    }

    /// <summary>
    /// �ڕW�ʒu�Z�b�g
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    public override void SetTarget(float x, float y, float z, float rx, float ry, float rz)
    {
        arm1.localEulerAngles = new Vector3(ang1.x, 0, x);
        arm2.localEulerAngles = new Vector3(0, -y, 0);
        arm3.localEulerAngles = new Vector3(0, y + z, 0);
        arm4.localEulerAngles = new Vector3(rx, 0, 0);
        arm5.localEulerAngles = new Vector3(0, ry, 0);
        arm6.localEulerAngles = new Vector3(rz, 0, 0);
    }

    /// <summary>
    /// ���f���č\�z
    /// </summary>
    /// <param name="instance"></param>
    protected override void ModelRestructProcess()
    {
        base.ModelRestructProcess();

        crx = new GameObject("CRX-30iA");
        crx.transform.parent = unitSetting.moveObject.transform;
        crx.transform.localPosition = Vector3.zero;
        crx.transform.localEulerAngles = Vector3.zero;

        var children = unitSetting.moveObject.GetComponentsInChildren<Transform>().ToList();

        // �A�[��1 Y��
        arm1 = children.Find(d => d.name.Contains("J2BASE"));
        // �A�[��2 Y��
        arm2 = children.Find(d => d.name.Contains("J2ARM"));
        // �A�[��3 Y��
        arm3 = children.Find(d => d.name.Contains("J3CASING"));
        // �A�[��4 X��
        arm4 = children.Find(d => d.name.Contains("J3ARM"));
        // �A�[��5 Y��
        arm5 = children.Find(d => d.name.Contains("J6CASING"));
        // �A�[��6 X��
        arm6 = children.Find(d => d.name.Contains("J6FLANGE"));

        // �e�q�֌W�Z�b�g
        arm1.parent = crx.transform;
        arm2.parent = arm1;
        arm3.parent = arm2;
        arm4.parent = arm3;
        arm5.parent = arm4;
        arm6.parent = arm5;

        // �����p�x�Z�b�g
        ang1 = arm1.localEulerAngles;
        ang2 = arm2.localEulerAngles;
        ang3 = arm3.localEulerAngles;
        ang4 = arm4.localEulerAngles;
        ang5 = arm5.localEulerAngles;
        ang6 = arm6.localEulerAngles;

        // �w�b�h�Z�b�g
        if (HeadObject != null)
        {
            HeadObject.transform.parent = arm6.transform;
        }
    }
}
