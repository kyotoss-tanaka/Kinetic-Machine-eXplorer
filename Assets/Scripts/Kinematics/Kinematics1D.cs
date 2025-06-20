using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;

public class Kinematics1D : KinematicsBase
{
    #region プロパティ
    [SerializeField]
    protected TagInfo X;

    #endregion プロパティ

    #region 関数

    // Start is called before the first frame update
    protected override void Start()
    {
        base.Start();
        if (baseObject == null)
        {
            ModelRestruct();
        }
    }

    /// <summary>
    /// 使用しているタグを取得する
    /// </summary>
    /// <returns></returns>
    public override List<TagInfo> GetUseTags()
    {
        return new List<TagInfo> { X };
    }

    /// <summary>
    /// 目標位置セット
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    public virtual void setTarget(float x)
    {
    }

    /// <summary>
    /// パラメータセット
    /// </summary>
    /// <param name="components"></param>
    /// <param name="scriptables"></param>
    /// <param name="kssInstanceIds"></param>
    /// <param name="root"></param>
    public override void SetParameter(List<Component> components, List<KssPartsBase> scriptables, List<KssInstanceIds> kssInstanceIds, JsonElement root)
    {
        base.SetParameter(components, scriptables, kssInstanceIds, root);
        if (X != null)
        {
            Destroy(X);
        }
        X = GetTagInfoFromPrm(scriptables, kssInstanceIds, root, "X");
    }
    #endregion 関数
}
