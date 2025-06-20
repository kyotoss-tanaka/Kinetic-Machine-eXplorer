using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;

public class UseHeadBaseScript : KinematicsBase
{
    /// <summary>
    /// �L�����o�X�\��
    /// </summary>
    protected override bool isCanvas { get { return true; } }

    [SerializeField]
    protected GameObject HeadObject;

    /// <summary>
    /// �g�p���Ă���^�O���擾����
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
