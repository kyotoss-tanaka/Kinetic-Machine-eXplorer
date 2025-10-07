using Parameters;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;

public class Kinematics3D : KinematicsBase
{
    /// <summary>
    /// キャンバス表示
    /// </summary>
    protected override bool isCanvas { get { return true; } }

    #region プロパティ
    [SerializeField]
    protected TagInfo X;

    [SerializeField]
    protected TagInfo Y;

    [SerializeField]
    protected TagInfo Z;

    [SerializeField]
    protected Vector3 target;
    #endregion プロパティ

    #region 変数
    protected RobotSetting robo;
    protected float txMax = 0;
    protected float txMin = 0;
    protected float tyMax = 0;
    protected float tyMin = 0;
    protected float tzMax = 0;
    protected float tzMin = 0;
    #endregion 変数

    #region 関数

    // Start is called before the first frame update
    protected override void Start()
    {
        if (baseObject == null)
        {
            ModelRestruct();
        }
    }

    protected override void MyFixedUpdate()
    {
        if (isManual)
        {
            setTarget(target);
        }
        else
        {
            if (robo.tags.Count >= 3)
            {
                var x = GetTagValueF(robo.tags[0], ref X);
                var y = GetTagValueF(robo.tags[1], ref Y);
                var z = GetTagValueF(robo.tags[2], ref Z);
                // mm単位系に変換
                target.x = CheckRangeF(x / (robo.rates[0] == 0 ? 1000f : robo.rates[0] / 1000f), txMin, txMax);
                target.y = CheckRangeF(y / (robo.rates[1] == 0 ? 1000f : robo.rates[1] / 1000f), tyMin, tyMax);
                target.z = CheckRangeF(z / (robo.rates[2] == 0 ? 1000f : robo.rates[2] / 1000f), tzMin, tzMax);
                setTarget(target);
            }
        }
    }

    /// <summary>
    /// 使用しているタグを取得する
    /// </summary>
    /// <returns></returns>
    public override List<TagInfo> GetUseTags()
    {
        return new List<TagInfo> { X, Y, Z };
    }

    /// <summary>
    /// 目標位置セット
    /// </summary>
    /// <param name="target"></param>
    public virtual void setTarget(Vector3 target)
    {
        SetTarget(target.x, target.y, target.z);
    }

    /// <summary>
    /// 目標位置セット
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    public virtual void SetTarget(float x, float y, float z)
    {
    }

    /// <summary>
    /// 当たり判定追加
    /// </summary>
    protected override void SetCollision()
    {
        /*
        // 当たり判定追加
        foreach (var mesh in this.GetComponentsInChildren<MeshRenderer>())
        {
            var mf = mesh.GetComponentsInChildren<MeshFilter>();
            int polygonCount = 0;
            foreach (var m in mf)
            {
                polygonCount += m.sharedMesh.triangles.Length / 3;
            }
            if (polygonCount < 256)
            {
                var col = mesh.AddComponent<MeshCollider>();
                col.convex = true;
                col.isTrigger = true;
            }
            else
            {
                var col = mesh.AddComponent<MeshCollider>();
                col.convex = true;
                col.isTrigger = true;
            }
        }
        */
        /*
        // 当たり判定追加
        foreach (var mesh in this.GetComponentsInChildren<MeshFilter>())
        {
            if (mesh.GetComponentInChildren<Collider>() == null)
            {
                var col = mesh.AddComponent<MeshCollider>();
                col.convex = true;
                col.isTrigger = true;
            }
        }
        var rigi = this.AddComponent<Rigidbody>();
        if (rigi != null)
        {
            rigi.useGravity = false;
            rigi.isKinematic = true;
            if (IsCollision)
            {
                this.AddComponent<CollisionScript>();
            }
        }
        */
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
        X = GetTagInfoFromPrm(scriptables, kssInstanceIds, root, "X");
        Y = GetTagInfoFromPrm(scriptables, kssInstanceIds, root, "Y");
        Z = GetTagInfoFromPrm(scriptables, kssInstanceIds, root, "Z");
    }

    /// <summary>
    /// パラメータセット
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="robo"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        robo = (RobotSetting)obj;
        base.SetParameter(unitSetting, robo);
    }
    #endregion 関数
}
