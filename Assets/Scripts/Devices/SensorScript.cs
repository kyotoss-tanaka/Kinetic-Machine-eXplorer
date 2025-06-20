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

    public void RenewPosition(Transform parent, Vector3 size)
    {
        /*
        this.parent = parent;
        transform.eulerAngles = new Vector3(90, parent.eulerAngles.y, 0);
        transform.parent = parent;
        var offset = RightOffset - LeftOffset;
        // 角度算出
        var rad = Mathf.Atan2(offset, size.z);
        // 長さ算出
        var m = MathF.Sqrt(offset * offset + size.z * size.z);
        // 位置調整
        transform.localScale = new Vector3(transform.localScale.x, m, transform.localScale.z);
        transform.localPosition = new Vector3(-LeftOffset - offset / 2, size.y / 2 + HeightOffset, 0);
        transform.localEulerAngles = new Vector3(transform.localEulerAngles.x, rad * 180 / MathF.PI, transform.localEulerAngles.z);
        */
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
    }

    protected override void OnTriggerStay(Collider collider)
    {
        if (!colliders.Contains(collider))
        {
            colliders.Add(collider);
        }
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
