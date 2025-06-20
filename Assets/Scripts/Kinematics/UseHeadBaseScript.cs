using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;

public class UseHeadBaseScript : KinematicsBase
{
    /// <summary>
    /// キャンバス表示
    /// </summary>
    protected override bool isCanvas { get { return true; } }

    [SerializeField]
    protected GameObject HeadObject;

    /// <summary>
    /// 使用しているタグを取得する
    /// </summary>
    /// <returns></returns>
    public virtual GameObject GetHeadObject()
    {
        return HeadObject;
    }

    public override void SetParameter(List<Component> components, List<KssPartsBase> scriptables, List<KssInstanceIds> kssInstanceIds, JsonElement root)
    {
        base.SetParameter(components, scriptables, kssInstanceIds, root);
        HeadObject = GetGameObjectFromPrm(components, kssInstanceIds, root, "HeadObject");
    }
}
