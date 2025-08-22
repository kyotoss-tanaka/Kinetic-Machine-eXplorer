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
    private TagInfo Output;

    [SerializeField]
    public Vector3 posOffset;

    [SerializeField]
    public Vector3 rotOffset;

    [SerializeField]
    public Vector3 posFixed;

    [SerializeField]
    public Vector3 rotFixed;

    [SerializeField]
    public bool IsDebug;

    [SerializeField]
    public bool IsNowSuck;

    [SerializeField]
    public bool IsSuck;

    /// <summary>
    /// 吸引中オブジェクト
    /// </summary>
    private List<GameObject> SuckObjects = new List<GameObject>();

    /// <summary>
    /// 吸引中段ボール
    /// </summary>
    private List<CardboardScript> SuckCardboards = new List<CardboardScript>();

    /// <summary>
    /// 衝突検知用
    /// </summary>
    private Rigidbody rigi;

    /// <summary>
    /// 起動時処理
    /// </summary>
    protected override void Start()
    {
        base.Start();
    }

    /// <summary>
    /// サイクル処理
    /// </summary>
    protected override void MyFixedUpdate()
    {
        IsSuck = (GlobalScript.GetTagData(Tag) != 0) || IsDebug;
        if (rigi.IsSleeping())
        {
            rigi.WakeUp();
        }

        // 吸引時のみ衝突有
        //        this.rigi.detectCollisions = IsSuck;
        // ワーク処理
        SuckObjects.RemoveAll(d => d == null);
        if ((SuckObjects.Count > 0) && !IsSuck)
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
        // 段ボール処理
        SuckCardboards.RemoveAll(d => d == null);
        if ((SuckCardboards.Count > 0) && !IsSuck)
        {
            // 吸引OFF
            foreach (var suck in SuckCardboards)
            {
                suck.ResetSuction(this);
            }
            SuckCardboards.Clear();
        }
        IsNowSuck = (IsSuck && ((SuckObjects.Count > 0) || (SuckCardboards.Count > 0))) ? true : false;
        GlobalScript.SetTagData(Output, SuckObjects.Count > 0 ? 1 : 0);
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
        if (IsSuck)
        {
            // 通常ワーク
            var objScript = collision.gameObject.GetComponentInParent<ObjectScript>();
            var cbScript = collision.gameObject.GetComponentInParent<CardboardScript>();
            if ((objScript != null) && !SuckObjects.Contains(objScript.gameObject))
            {
                if ((cbScript != null) && (SuckCardboards.Contains(cbScript)))
                {
                    return;
                }
                //                script.transform.parent = unitSetting.moveObject.transform;
                objScript.transform.parent = transform;
                var rigi = objScript.GetComponentInChildren<Rigidbody>();
                rigi.useGravity = false;
                rigi.isKinematic = true;
                objScript.transform.localPosition = new Vector3
                {
                    x = posFixed.x == 1 ? posOffset.x * objScript.transform.localScale.x : objScript.transform.localPosition.x,
                    y = posFixed.y == 1 ? posOffset.y * objScript.transform.localScale.y : objScript.transform.localPosition.y,
                    z = posFixed.z == 1 ? posOffset.z * objScript.transform.localScale.z : objScript.transform.localPosition.z,
                };
                objScript.transform.localEulerAngles = new Vector3
                {
                    x = rotFixed.x == 1 ? rotOffset.x * objScript.transform.localScale.x : objScript.transform.localEulerAngles.x,
                    y = rotFixed.y == 1 ? rotOffset.y * objScript.transform.localScale.y : objScript.transform.localEulerAngles.y,
                    z = rotFixed.z == 1 ? rotOffset.z * objScript.transform.localScale.z : objScript.transform.localEulerAngles.z,
                };
                SuckObjects.Add(objScript.gameObject);
                if (cbScript != null)
                {
                    // 段ボールなら
                    foreach (ContactPoint contact in collision.contacts)
                    {
                        var parts = contact.otherCollider.transform.parent.gameObject;
                        if (parts != null)
                        {
                            if (cbScript.SetSuction(this, parts))
                            {
                                SuckCardboards.Add(cbScript);
                            }
                            break;
                        }
                    }
                }
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

        rigi = GetComponent<Rigidbody>();
        rigi.sleepThreshold = 0;

        var s = (SuctionSetting)obj;
        Tag = ScriptableObject.CreateInstance<TagInfo>();
        Tag.Database = unitSetting.Database;
        Tag.MechId = unitSetting.mechId;
        Tag.Tag = s.tag;
        Output = ScriptableObject.CreateInstance<TagInfo>();
        Output.Database = unitSetting.Database;
        Output.MechId = unitSetting.mechId;
        Output.Tag = s.tag_output;
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
            x = s.pos[0],
            y = s.pos[1],
            z = s.pos[2]
        };
        rotOffset = new Vector3
        {
            x = s.rot[0],
            y = s.rot[1],
            z = s.rot[2]
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
