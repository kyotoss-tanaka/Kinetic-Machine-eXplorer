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
    /// <summary>
    /// 親をセットする
    /// </summary>
    /// <param name="parent"></param>
    public void SetParent(GameObject parent)
    {
        model.transform.parent = parent.transform;
        foreach (var child in children)
        {
            child.transform.parent = parent.transform;
        }
    }
}
