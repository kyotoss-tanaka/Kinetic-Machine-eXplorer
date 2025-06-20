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
    /// データベースキー
    /// </summary>
    public string Database;

    /// <summary>
    /// 機番
    /// </summary>
    public string MechId;

    /// <summary>
    /// タグ名
    /// </summary>
    public string Tag;

    /// <summary>
    /// デバイス名
    /// </summary>
    public string Device;

    /// <summary>
    /// 値
    /// </summary>
    public int Value;

    /// <summary>
    /// 値(float)
    /// </summary>
    public float fValue;

    /// <summary>
    /// 浮動小数点データ
    /// </summary>
    public bool isFloat;
}

[Serializable]
public class TagInfoCom
{
    /// <summary>
    /// 機番
    /// </summary>
    public string MechId { set; get; }

    /// <summary>
    /// タグ名
    /// </summary>
    public string Tag { set; get; }

    /// <summary>
    /// 値
    /// </summary>
    public int Value { set; get; }

    /// <summary>
    /// 値(float)
    /// </summary>
    public float fValue { set; get; }

    /// <summary>
    /// 浮動小数点データ
    /// </summary>
    public bool isFloat { set; get; }
}