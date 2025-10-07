using Parameters;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;

public class Kinematics6D : Kinematics3D
{
    #region プロパティ
    [SerializeField]
    protected TagInfo RX;

    [SerializeField]
    protected TagInfo RY;

    [SerializeField]
    protected TagInfo RZ;

    [SerializeField]
    protected Vector3 rotate;

    [SerializeField]
    protected GameObject HeadObject;

    #endregion プロパティ

    #region 変数
    protected float trxMax = 0;
    protected float trxMin = 0;
    protected float tryMax = 0;
    protected float tryMin = 0;
    protected float trzMax = 0;
    protected float trzMin = 0;
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
            setTarget(target, rotate);
        }
        else
        {
            if (robo.tags.Count >= 6)
            {
                var x = GetTagValueF(robo.tags[0], ref X);
                var y = GetTagValueF(robo.tags[1], ref Y);
                var z = GetTagValueF(robo.tags[2], ref Z);
                var rx = GetTagValueF(robo.tags[3], ref RX);
                var ry = GetTagValueF(robo.tags[4], ref RY);
                var rz = GetTagValueF(robo.tags[5], ref RZ);
                target.x = CheckRangeF(x / (robo.rates[0] == 0 ? 1000f : robo.rates[0]), txMin, txMax);
                target.y = CheckRangeF(y / (robo.rates[1] == 0 ? 1000f : robo.rates[1]), tyMin, tyMax);
                target.z = CheckRangeF(z / (robo.rates[2] == 0 ? 1000f : robo.rates[2]), tzMin, tzMax);
                rotate.x = CheckRangeF(rx / (robo.rates[3] == 0 ? 1000f : robo.rates[3]), trxMin, trxMax);
                rotate.y = CheckRangeF(ry / (robo.rates[4] == 0 ? 1000f : robo.rates[4]), tryMin, tryMax);
                rotate.z = CheckRangeF(rz / (robo.rates[5] == 0 ? 1000f : robo.rates[5]), trzMin, trzMax);
                setTarget(target, rotate);
            }
        }
    }

    /// <summary>
    /// 使用しているタグを取得する
    /// </summary>
    /// <returns></returns>
    public override List<TagInfo> GetUseTags()
    {
        return new List<TagInfo> { X, Y, Z, RX, RY, RZ };
    }

    /// <summary>
    /// 目標位置セット
    /// </summary>
    /// <param name="target"></param>
    public virtual void setTarget(Vector3 targe, Vector3 rotate)
    {
        SetTarget(target.x, target.y, target.z, rotate.x, rotate.y, rotate.z);
    }

    /// <summary>
    /// 目標位置セット
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    /// <param name="rx"></param>
    /// <param name="ry"></param>
    /// <param name="rz"></param>
    public virtual void SetTarget(float x, float y, float z, float rx, float ry, float rz)
    {
    }

    /// <summary>
    /// 当たり判定追加
    /// </summary>
    protected override void SetCollision()
    {
    }

    /// <summary>
    /// パラメータセット
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="robo"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);
    }

    /// <summary>
    /// モデル再構築
    /// </summary>
    /// <param name="instance"></param>
    protected override void ModelRestructProcess()
    {
        if (robo.headUnit != null)
        {
            HeadObject = robo.headUnit.unitObject;
        }
    }
    #endregion 関数
}
