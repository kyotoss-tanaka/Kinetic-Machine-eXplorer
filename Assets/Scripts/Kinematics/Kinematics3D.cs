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
    #endregion プロパティ

    #region 変数
    /// <summary>
    /// 目標座標
    /// </summary>
    protected float tx = 0, ty = 0, tz = 620;

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
        tx = CheckRangeF(GlobalScript.GetTagData(X) / 1000f, txMin, txMax);
        ty = CheckRangeF(GlobalScript.GetTagData(Y) / 1000f, tyMin, tyMax);
        tz = CheckRangeF(GlobalScript.GetTagData(Z) / 1000f, tzMin, tzMax);

        setTarget(tx, ty, tz);
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
        setTarget(target.x, target.y, target.z);
    }

    /// <summary>
    /// 目標位置セット
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    public virtual void setTarget(float x, float y, float z)
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
        RobotSetting robo = (RobotSetting)obj;
        base.SetParameter(unitSetting, robo);
        if (robo.tags.Count >= 3)
        {
            X = ScriptableObject.CreateInstance<TagInfo>();
            X.Database = unitSetting.Database;
            X.MechId = unitSetting.mechId;
            X.Tag = robo.tags[0];
            Y = ScriptableObject.CreateInstance<TagInfo>();
            Y.Database = unitSetting.Database;
            Y.MechId = unitSetting.mechId;
            Y.Tag = robo.tags[1];
            Z = ScriptableObject.CreateInstance<TagInfo>();
            Z.Database = unitSetting.Database;
            Z.MechId = unitSetting.mechId;
            Z.Tag = robo.tags[2];
        }
    }
    #endregion 関数
}
