using Parameters;
using System.Collections.Generic;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;

public class UseHeadBase3DScript : Kinematics3D
{
    [SerializeField]
    public GameObject HeadObject;

    [SerializeField]
    public bool IsSuck;

    [SerializeField]
    public TagInfo SuckTag;

    [SerializeField]
    public bool UseSuckOffset;

    [SerializeField]
    public Vector3 SuckOffset;

    /// <summary>
    /// 吸引中オブジェクト
    /// </summary>
    private List<GameObject> SuckObjects = new List<GameObject>();

    protected override void MyFixedUpdate()
    {
        base.MyFixedUpdate();
        if (SuckObjects.Count > 0 && (GlobalScript.GetTagData(SuckTag) == 0))
        {
            // 吸引OFF
            foreach (var suck in SuckObjects)
            {
                suck.transform.parent = null;
                var rigi = suck.GetComponentInChildren<Rigidbody>();
                rigi.useGravity = true;
                rigi.isKinematic = false;
            }
            SuckObjects.Clear();
        }
    }

    /// <summary>
    /// 衝突判定
    /// </summary>
    protected override void SetCollision()
    {
        base.SetCollision();
        if (IsSuck && HeadObject != null)
        {
            // 衝突可能に変更
            foreach (var col in HeadObject.GetComponentsInChildren<MeshCollider>())
            {
                col.isTrigger = false;
            }
        }
    }

    /// <summary>
    /// 使用しているタグを取得する
    /// </summary>
    /// <returns></returns>
    public virtual GameObject GetHeadObject()
    {
        return HeadObject;
    }

    protected override void OnCollisionEnter(Collision collision)
    {
        if (IsSuck && (SuckTag != null))
        {
            if (GlobalScript.GetTagData(SuckTag) >= 1)
            {
                var script = collision.gameObject.GetComponentInParent<ObjectScript>();
                if (script != null)
                {
                    script.transform.parent = HeadObject.transform;
                    var rigi = script.GetComponentInChildren<Rigidbody>();
                    rigi.useGravity = false;
                    rigi.isKinematic = true;
                    if (UseSuckOffset)
                    {
                        script.transform.localPosition = new Vector3();
                        rigi.transform.localPosition = SuckOffset;
                        rigi.transform.localEulerAngles = new Vector3();
                    }
                    SuckObjects.Add(script.gameObject);
                }
            }
        }
    }

    public override void SetParameter(List<Component> components, List<KssPartsBase> scriptables, List<KssInstanceIds> kssInstanceIds, JsonElement root)
    {
        base.SetParameter(components, scriptables, kssInstanceIds, root);
        HeadObject = GetGameObjectFromPrm(components, kssInstanceIds, root, "HeadObject");
        IsSuck = GetBooleanFromPrm(root, "IsSuck");
        UseSuckOffset = GetBooleanFromPrm(root, "UseSuckOffset");
        SuckOffset = GetVector3FromPrm(root, "SuckOffset");
        SuckTag = GetTagInfoFromPrm(scriptables, kssInstanceIds, root, "SuckTag");
    }

    /// <summary>
    /// パラメータセット
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="robo"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);
        RobotSetting robo = (RobotSetting)obj;
        if (robo.headUnit != null)
        {
            HeadObject = robo.headUnit.unitObject;
        }
    }
}
