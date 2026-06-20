using Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.HID;
using UnityEngine.UIElements;

public class SensorScript : UseTagBaseScript
{
    [SerializeField]
    private float LeftOffset;
    [SerializeField]

    private float RightOffset;

    [SerializeField]
    private float HeightOffset;

    [SerializeField]
    private TagInfo Tag;

    private Transform parent;

    private List<Collider> colliders = new List<Collider>();

    private MeshRenderer meshRenderer;
    private Material RedMaterial;
    private Material GreenMaterial;

    public bool Status
    {
        get
        {
            return colliders.Count > 0;
        }
    }

    protected override void Start()
    {
        base.Start();
        meshRenderer = GetComponentsInChildren<MeshRenderer>().First();
        RedMaterial = (Material)Resources.Load("Materials/RedMaterial");
        GreenMaterial = (Material)Resources.Load("Materials/GreenMaterial");
    }

    // Update is called once per frame
    protected override void Update()
    {
        colliders.RemoveAll(d => d == null);
        meshRenderer.material = colliders.Count == 0 ? GreenMaterial : RedMaterial;
        GlobalScript.SetTagData(Tag, Status ? 1 : 0);
    }

    /// <summary>
    /// 使用しているタグを取得する
    /// </summary>
    /// <returns></returns>
    public override List<TagInfo> GetUseTags()
    {
        var ret = base.GetUseTags();
        ret.Add(Tag);
        return ret;
    }

    protected override void OnTriggerEnter(Collider collider)
    {
        if (collider.GetComponent<IgnoreCollisionScript>() == null)
        {
            if (!colliders.Contains(collider))
            {
                colliders.Add(collider);
            }
        }
    }

    protected override void OnTriggerStay(Collider collider)
    {
        /*
        if (collider.GetComponent<IgnoreCollisionScript>() == null)
        {
            if (!colliders.Contains(collider))
            {
                colliders.Add(collider);
            }
        }
        */
    }

    protected override void OnTriggerExit(Collider collider)
    {
        colliders.Remove(collider);
    }

    /// <summary>
    /// パラメータをセットする
    /// </summary>
    /// <param name="components"></param>
    /// <param name="scriptables"></param>
    /// <param name="kssInstanceIds"></param>
    /// <param name="root"></param>
    public override void SetParameter(List<Component> components, List<KssPartsBase> scriptables, List<KssInstanceIds> kssInstanceIds, JsonElement root)
    {
        base.SetParameter(components, scriptables, kssInstanceIds, root);
        LeftOffset = GetFloatFromPrm(root, "LeftOffset");
        RightOffset = GetFloatFromPrm(root, "RightOffset");
        HeightOffset = GetFloatFromPrm(root, "HeightOffset");
        Tag = GetTagInfoFromPrm(scriptables, kssInstanceIds, root, "Tag");
    }

    /// <summary>
    /// パラメータをセットする
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="obj"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);

        var s = (SensorSetting)obj;
        Tag = ScriptableObject.CreateInstance<TagInfo>();
        Tag.Database = unitSetting.Database;
        Tag.MechId = unitSetting.mechId;
        Tag.Tag = s.tag;
    }
}
