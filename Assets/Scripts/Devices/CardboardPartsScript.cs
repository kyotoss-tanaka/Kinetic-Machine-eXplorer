using Parameters;
using System;
using System.Collections.Generic;
using System.Drawing;
using Unity.VisualScripting;
using UnityEngine;

/// <summary>
/// �i�{�[���p�X�N���v�g
/// </summary>
public class CardboardPartsScript: KssBaseScript
{
    /// <summary>
    /// �t���b�v����
    /// </summary>
    public bool isFlap = false;

    /// <summary>
    /// ���b�V���R���C�_�[
    /// </summary>
    public MeshCollider meshCollider;

    /// <summary>
    /// �{�b�N�X�R���C�_�[
    /// </summary>
    public BoxCollider boxCollider;

    /// <summary>
    /// �ȑO�̐e
    /// </summary>
    private GameObject prvParent;

    /// <summary>
    /// �����ʒu
    /// </summary>
    private Vector3 initPosition;

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
        base.FixedUpdate();

        if (prvParent != transform.parent.gameObject)
        {
            initPosition = transform.localPosition;
            prvParent = transform.parent.gameObject;
        }
        // �ʒu�͕ς����p�x������ς���
        this.transform.localPosition = initPosition;
    }

    /// <summary>
    /// ����������
    /// </summary>
    private void Initialize()
    {
        // �Փˌ��m�p
        var mr = GetComponentInChildren<MeshRenderer>();
        if (mr != null)
        {
            boxCollider = mr.gameObject.GetComponent<BoxCollider>();
            /*
            if (transform.localScale.x > 0)
            {
                if (boxCollider == null)
                {
                    boxCollider = mr.gameObject.AddComponent<BoxCollider>();
                }
            }
            else
            {
                if (boxCollider != null)
                {
                    Destroy(boxCollider);
                }
                meshCollider = mr.gameObject.AddComponent<MeshCollider>();
                meshCollider.convex = true;
            }
            */
            if (boxCollider != null)
            {
                Destroy(boxCollider);
            }
            meshCollider = mr.gameObject.AddComponent<MeshCollider>();
            meshCollider.convex = true;
        }
    }

    /// <summary>
    /// �p�����[�^�Z�b�g
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="obj"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);
    }

    /// <summary>
    /// �Փˎ�����
    /// </summary>
    /// <param name="collision"></param>
    protected override void OnCollisionEnter(Collision collision)
    {
    }

    protected override void OnCollisionStay(Collision collision)
    {
    }
}
