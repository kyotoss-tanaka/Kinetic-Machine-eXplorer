using JetBrains.Annotations;
using System;
using UnityEngine;

[Serializable]
public class TagInfo : KssPartsBase
{
    public override string pathString
    {
        get
        {
            return $"{Database}/{MechId}/{Tag}";
        }
    }
    /// <summary>
    /// �f�[�^�x�[�X�L�[
    /// </summary>
    public string Database;

    /// <summary>
    /// �@��
    /// </summary>
    public string MechId;

    /// <summary>
    /// �^�O��
    /// </summary>
    public string Tag;

    /// <summary>
    /// �f�o�C�X��
    /// </summary>
    public string Device;

    /// <summary>
    /// �l
    /// </summary>
    public int Value;

    /// <summary>
    /// �l(float)
    /// </summary>
    public float fValue;

    /// <summary>
    /// ���������_�f�[�^
    /// </summary>
    public bool isFloat;
}

[Serializable]
public class TagInfoCom
{
    /// <summary>
    /// �@��
    /// </summary>
    public string MechId { set; get; }

    /// <summary>
    /// �^�O��
    /// </summary>
    public string Tag { set; get; }

    /// <summary>
    /// �l
    /// </summary>
    public int Value { set; get; }

    /// <summary>
    /// �l(float)
    /// </summary>
    public float fValue { set; get; }

    /// <summary>
    /// ���������_�f�[�^
    /// </summary>
    public bool isFloat { set; get; }
}