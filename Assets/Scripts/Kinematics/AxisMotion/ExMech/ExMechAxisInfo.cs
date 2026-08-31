using Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
public class ExMechAxisInfo
{
    [SerializeField]
    public GameObject model;
    [SerializeField]
    public List<GameObject> children;

    /// <summary>回転中心の参照モデル（種別=回転中心の子モデル。バウンズ中心を回転中心とする）</summary>
    public GameObject pivotSource;

    /// <summary>回転中心に挿入したピボット空間（未指定モデルはnull＝従来どおり原点回転）</summary>
    public GameObject pivot;

    /// <summary>機構計算で扱う基準Transform（ピボットがあればピボット、なければモデル本体）</summary>
    public Transform root
    {
        get
        {
            return pivot != null ? pivot.transform : model.transform;
        }
    }

    /// <summary>
    /// 親をセットする
    /// </summary>
    /// <param name="parent"></param>
    public void SetParent(GameObject parent)
    {
        root.parent = parent.transform;
        foreach (var child in children)
        {
            child.transform.parent = parent.transform;
        }
    }
}
