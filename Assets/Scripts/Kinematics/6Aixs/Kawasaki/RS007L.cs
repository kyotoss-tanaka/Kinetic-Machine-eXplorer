using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class RS007L : Robo6Axis
{

    #region 関数
    /// <summary>
    /// モデル再構築
    /// </summary>
    /// <param name="instance"></param>
    protected override void ModelRestructProcess()
    {
        var children = GetComponentsInChildren<Transform>();
        // ベースオブジェクト取得
        baseObject = children.FirstOrDefault(d => d.name.Contains("ベース")).gameObject;
        j1Object = children.FirstOrDefault(d => d.name.Contains("_J1")).gameObject;
        j2Object = children.FirstOrDefault(d => d.name.Contains("_J2")).gameObject;
        j3Object = children.FirstOrDefault(d => d.name.Contains("_J3")).gameObject;
        j4Object = children.FirstOrDefault(d => d.name.Contains("_J4")).gameObject;
        j5Object = children.FirstOrDefault(d => d.name.Contains("_J5")).gameObject;
        j6Object = children.FirstOrDefault(d => d.name.Contains("_J6")).gameObject;
        j6Object.transform.parent = j5Object.transform;
        j5Object.transform.parent = j4Object.transform;
        j4Object.transform.parent = j3Object.transform;
        j3Object.transform.parent = j2Object.transform;
        j2Object.transform.parent = j1Object.transform;
        j1Object.transform.parent = baseObject.transform;
    }
    #endregion 関数
}
