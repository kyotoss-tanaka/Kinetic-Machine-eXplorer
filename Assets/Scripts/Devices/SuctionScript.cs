using Parameters;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.VisualScripting;
using UnityEngine;

public class SuctionScript : UseTagBaseScript
{
    [SerializeField]
    private TagInfo Tag;

    [SerializeField]
    public Vector3 posOffset;

    [SerializeField]
    public Vector3 rotOffset;

    [SerializeField]
    public Vector3 posFixed;

    [SerializeField]
    public Vector3 rotFixed;

    /// <summary>
    /// 吸引中オブジェクト
    /// </summary>
    private List<GameObject> SuckObjects = new List<GameObject>();

    /// <summary>
    /// サイクル処理
    /// </summary>
    protected override void MyFixedUpdate()
    {
        SuckObjects.RemoveAll(d => d == null);
        if (SuckObjects.Count > 0 && (GlobalScript.GetTagData(Tag) == 0))
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
    /// 衝突時処理
    /// </summary>
    /// <param name="collision"></param>
    protected override void OnCollisionEnter(Collision collision)
    {
    }

    protected override void OnCollisionStay(Collision collision)
    {
        if (GlobalScript.GetTagData(Tag) >= 1)
        {
            var script = collision.gameObject.GetComponentInParent<ObjectScript>();
            if ((script != null) && !SuckObjects.Contains(script.gameObject))
            {
//                script.transform.parent = unitSetting.moveObject.transform;
                script.transform.parent = transform;
                var rigi = script.GetComponentInChildren<Rigidbody>();
                rigi.useGravity = false;
                rigi.isKinematic = true;
                script.transform.localPosition = new Vector3
                {
                    x = posFixed.x == 1 ? posOffset.x : script.transform.localPosition.x,
                    y = posFixed.y == 1 ? posOffset.y : script.transform.localPosition.y,
                    z = posFixed.z == 1 ? posOffset.z : script.transform.localPosition.z,
                };
                script.transform.localEulerAngles = new Vector3
                {
                    x = rotFixed.x == 1 ? rotOffset.x : script.transform.localEulerAngles.x,
                    y = rotFixed.y == 1 ? rotOffset.y : script.transform.localEulerAngles.y,
                    z = rotFixed.z == 1 ? rotOffset.z : script.transform.localEulerAngles.z,
                };
                SuckObjects.Add(script.gameObject);
            }
        }
    }

    /// <summary>
    /// パラメータセット
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="robo"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);

        var s = (SuctionSetting)obj;
        Tag = ScriptableObject.CreateInstance<TagInfo>();
        Tag.Database = unitSetting.Database;
        Tag.MechId = unitSetting.mechId;
        Tag.Tag = s.tag;
        posFixed = new Vector3
        {
            x = s.pos_fixed[0],
            y = s.pos_fixed[1],
            z = s.pos_fixed[2]
        };
        rotFixed = new Vector3
        {
            x = s.rot_fixed[0],
            y = s.rot_fixed[1],
            z = s.rot_fixed[2]
        };
        posOffset = new Vector3
        {
            x = s.pos[0] * transform.localScale.x,
            y = s.pos[1] * transform.localScale.y,
            z = s.pos[2] * transform.localScale.z
        };
        rotOffset = new Vector3
        {
            x = s.rot[0] * transform.localScale.x,
            y = s.rot[1] * transform.localScale.y,
            z = s.rot[2] * transform.localScale.z
        };
        var meshs = transform.GetComponentsInChildren<MeshCollider>();
        if (meshs.Length == 0)
        {
            // 衝突可能
            var shapes = transform.GetComponentsInChildren<ShapeScript>();
            if (shapes.Length == 0)
            {
                // 物体形状がなければ
                foreach (var mesh in this.GetComponentsInChildren<MeshFilter>())
                {
                    if (mesh.GetComponentInChildren<Collider>() == null)
                    {
                        var col = mesh.AddComponent<MeshCollider>();
                        col.convex = true;
                        col.isTrigger = false;
                    }
                }
            }
        }
        else
        {
            // 衝突可能に変更
            foreach (var col in meshs)
            {
                col.isTrigger = false;
            }
        }
    }
}
