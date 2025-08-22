using Parameters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.OpenXR.Input;
using static OVRPlugin;

public class ExMechScript : UseTagBaseScript
{
    [Serializable]
    class AxisInfo
    {
        [SerializeField]
        public GameObject model;
        [SerializeField]
        public List<GameObject> children;
    }

    /// <summary>
    /// ���j�b�g�ݒ�
    /// </summary>
    [SerializeField]
    protected ExMechSetting exMechSetting;

    /// <summary>
    /// �@�\�^�C�v 0:�X���C�_�[�N�����N 1:�[�l�o�@�\
    /// </summary>
    [SerializeField]
    int mechType;

    /// <summary>
    /// �������
    /// </summary>
    [SerializeField]
    Vector3 moveDir;

    /// <summary>
    /// �厲(�e)
    /// </summary>
    [SerializeField]
    AxisInfo mainAxis;

    /// <summary>
    /// �]����(����)
    /// </summary>
    [SerializeField]
    AxisInfo drivenAxis;

    /// <summary>
    /// ���Ԏ�
    /// </summary>
    [SerializeField]
    List<AxisInfo> intermediateAxis;

    /// <summary>
    /// �A�[����L
    /// </summary>
    [SerializeField]
    float armL;

    /// <summary>
    /// �A�[����M
    /// </summary>
    [SerializeField]
    float armM;

    /// <summary>
    /// �]�����I�t�Z�b�g�p�x
    /// </summary>
    [SerializeField]
    float drivenOffset;

    /// <summary>
    /// ���Ԏ��I�t�Z�b�g�p�x
    /// </summary>
    [SerializeField]
    List<float> intermediateOffset;

    /// <summary>
    /// �}�X�N
    /// </summary>
    Vector3 maskDir1;

    /// <summary>
    /// �}�X�N
    /// </summary>
    Vector3 maskDir2;

    /// <summary>
    /// �A�[������
    /// </summary>
    Vector3 armDir;

    /// <summary>
    /// �O��̎p���ێ�
    /// </summary>
    Vector3 drivenEulerAngles;

    /// <summary>
    /// �O��̎p���ێ�
    /// </summary>
    List<Vector3> intermediateEulerAngles;

    /// <summary>
    /// �]�����̃x�[�X���
    /// </summary>
    GameObject drivenBase;

    /// <summary>
    /// ���Ԏ��̃x�[�X���
    /// </summary>
    List<GameObject> intermediateBase;

    /// <summary>
    /// �J�n����
    /// </summary>
    protected override void Start()
    {
        base.Start();

        // ����������
        Initialize();
    }

    /// <summary>
    /// ��������
    /// </summary>
    protected override void FixedUpdate()
    {
        // �쓮���̍��W�n�ɕϊ�
        var mainPos = Vector3.zero;
        var sliderPos = mainAxis.model.transform.InverseTransformPoint(intermediateAxis[0].model.transform.position);

        // �V���t�g�ʒu�v�Z
        Vector3 point = armDir * armL + Vector3.Scale(sliderPos, maskDir2);

        // ���f��1�̃��[�J�����W�����[���h���W�ɕϊ�
        intermediateAxis[0].model.transform.position = mainAxis.model.transform.TransformPoint(point);

        if (mechType == 0)
        {
            // �X���C�_�[�N�����N�@�\
        }
        else if (mechType == 1)
        {
            // �]�����p�x�ݒ�
            sliderPos = drivenBase.transform.InverseTransformPoint(intermediateAxis[0].model.transform.position);
            var mSliderPos = Vector3.Scale(sliderPos, maskDir1);
            var angle = GetAngle(Vector3.zero, mSliderPos) - drivenOffset;
            var eulerAngle = Vector3.Scale(new Vector3(angle, angle, angle), drivenBase.transform.localScale);
            drivenAxis.model.transform.localEulerAngles = Vector3.Scale(eulerAngle, maskDir2) + Vector3.Scale(drivenEulerAngles, maskDir1);

            // ���Ԏ��p�x�ݒ�
            var drivenPos = intermediateBase[0].transform.InverseTransformPoint(drivenAxis.model.transform.position);
            var mDrivenPos = Vector3.Scale(sliderPos, maskDir1);
            angle = GetAngle(Vector3.zero, mDrivenPos) - intermediateOffset[0];
            eulerAngle = Vector3.Scale(new Vector3(angle, angle, angle), intermediateBase[0].transform.localScale);
            intermediateAxis[0].model.transform.localEulerAngles = Vector3.Scale(eulerAngle, maskDir2) + Vector3.Scale(intermediateBase[0].transform.localEulerAngles, maskDir1);
        }
    }

    /// <summary>
    /// ����������
    /// </summary>
    private void Initialize()
    {
        maskDir1 = new Vector3
        {
            x = moveDir.x == 0 ? 1 : 0,
            y = moveDir.y == 0 ? 1 : 0,
            z = moveDir.z == 0 ? 1 : 0
        };
        maskDir2 = new Vector3
        {
            x = moveDir.x != 0 ? 1 : 0,
            y = moveDir.y != 0 ? 1 : 0,
            z = moveDir.z != 0 ? 1 : 0
        };
        // �쓮���̍��W�n�ɕϊ�
        var mainPos = Vector3.zero;
        var sliderPos = mainAxis.model.transform.InverseTransformPoint(intermediateAxis[0].model.transform.position);
        var mMainPos = Vector3.Scale(mainPos, maskDir1);
        var mSliderPos = Vector3.Scale(sliderPos, maskDir1);
        if (mechType == 0)
        {
            // �X���C�_�[�N�����N�@�\
        }
        else if (mechType == 1)
        {
            // �[�l�o�@�\
            armL = Vector3.Distance(mMainPos, mSliderPos);

            // ���̕����擾
            var xp = Vector3.Distance(armL * Vector3.right.normalized, mSliderPos);
            var xm = Vector3.Distance(armL * Vector3.left.normalized, mSliderPos);
            var yp = Vector3.Distance(armL * Vector3.up.normalized, mSliderPos);
            var ym = Vector3.Distance(armL * Vector3.down.normalized, mSliderPos);
            var zp = Vector3.Distance(armL * Vector3.forward.normalized, mSliderPos);
            var zm = Vector3.Distance(armL * Vector3.back.normalized, mSliderPos);
            if (xp < 0.001f)
            {
                armDir = Vector3.right.normalized;
            }
            else if (xm < 0.001f)
            {
                armDir = Vector3.left.normalized;
            }
            else if (yp < 0.001f)
            {
                armDir = Vector3.up.normalized;
            }
            else if (ym < 0.001f)
            {
                armDir = Vector3.down.normalized;
            }
            else if (zp < 0.001f)
            {
                armDir = Vector3.forward.normalized;
            }
            else if (zm < 0.001f)
            {
                armDir = Vector3.back.normalized;
            }

            // �]�����̍��W�n�ɕϊ�
            sliderPos = drivenAxis.model.transform.InverseTransformPoint(intermediateAxis[0].model.transform.position);
            mSliderPos = Vector3.Scale(sliderPos, maskDir1);
            // �p�x�I�t�Z�b�g�ݒ�
            drivenOffset = GetAngle(Vector3.zero, mSliderPos);
            drivenEulerAngles = drivenAxis.model.transform.localEulerAngles;
            drivenBase = new GameObject("drivenBase");
            drivenBase.transform.parent = drivenAxis.model.transform.parent;
            drivenBase.transform.localPosition = drivenAxis.model.transform.localPosition;
            drivenBase.transform.localEulerAngles = drivenAxis.model.transform.localEulerAngles;
            drivenBase.transform.localScale = drivenAxis.model.transform.localScale;

            // ���Ԏ��̍��W�n�ɕϊ�
            var drivenPos = intermediateAxis[0].model.transform.InverseTransformPoint(drivenAxis.model.transform.position);
            var mDrivenPos = Vector3.Scale(sliderPos, maskDir1);
            // �p�x�I�t�Z�b�g�ݒ�
            intermediateOffset = new();
            intermediateOffset.Add(GetAngle(Vector3.zero, mDrivenPos));
            intermediateEulerAngles = new();
            intermediateEulerAngles.Add(intermediateAxis[0].model.transform.localEulerAngles);
            intermediateBase = new();
            intermediateBase.Add(new GameObject("intermediateBase"));
            intermediateBase[0].transform.parent = intermediateAxis[0].model.transform.parent;
            intermediateBase[0].transform.localPosition = intermediateAxis[0].model.transform.localPosition;
            intermediateBase[0].transform.localEulerAngles = intermediateAxis[0].model.transform.localEulerAngles;
            intermediateBase[0].transform.localScale = intermediateAxis[0].model.transform.localScale;
        }
    }

    /// <summary>
    /// �p�x�擾
    /// </summary>
    /// <param name="pos1"></param>
    /// <param name="pos2"></param>
    /// <returns></returns>
    private float GetAngle(Vector3 pos1, Vector3 pos2)
    {
        Vector2 A;
        Vector2 B;
        if ((moveDir == Vector3.right) || (moveDir == Vector3.left))
        {
            A = new Vector2(pos1.y, pos1.z);
            B = new Vector2(pos2.y, pos2.z);
        }
        else if ((moveDir == Vector3.up) || (moveDir == Vector3.down))
        {
            A = new Vector2(pos1.x, pos1.z);
            B = new Vector2(pos2.x, pos2.z);
        }
        else
        {
            A = new Vector2(pos1.x, pos1.y);
            B = new Vector2(pos2.x, pos2.y);
        }
        var dir = B - A;
        // ���W�A������p�x�ɕϊ�
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        // �K�v�Ȃ�0�`360�x�ɐ��K��
        if (angle < 0) angle += 360f;

        return angle;
    }

    /// <summary>
    /// �p�����[�^���Z�b�g����
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="obj"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);
        exMechSetting = (ExMechSetting)obj;
        mechType = exMechSetting.type;
        switch (unitSetting.actionSetting.axis)
        {
            case 0:
                // X
                if (unitSetting.actionSetting.dir >= 0)
                {
                    moveDir = Vector3.right;
                }
                else
                {
                    moveDir = Vector3.left;
                }
                break;
            case 1:
                // Y
                if (unitSetting.actionSetting.dir >= 0)
                {
                    moveDir = Vector3.up;
                }
                else
                {
                    moveDir = Vector3.down;
                }
                break;

            case 2:
                // Z
                if (unitSetting.actionSetting.dir >= 0)
                {
                    moveDir = Vector3.forward;
                }
                else
                {
                    moveDir = Vector3.back;
                }
                break;
        }
        if (mechType == 0)
        {
        }
        else if (mechType == 1)
        {
            mainAxis = new AxisInfo
            {
                model = unitSetting.moveObject,
                children = new()
            };
            drivenAxis = new AxisInfo
            {
                model = exMechSetting.datas[0].gameObject,
                children = new()
            };
            foreach (var child in exMechSetting.datas[0].children)
            {
                drivenAxis.children.Add(child.gameObject);
            }
            intermediateAxis = new();
            intermediateAxis.Add(new AxisInfo
            {
                model = exMechSetting.datas[1].gameObject,
                children = new()
            });
            foreach (var child in exMechSetting.datas[1].children)
            {
                intermediateAxis[0].children.Add(child.gameObject);
            }
        }
    }
}
