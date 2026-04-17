using KssColliderHullReducer;
using NUnit.Framework;
using Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// レバー機構
/// </summary>
public class ExMechParallelLinkInfo : ExMechInfo
{
    /// <summary>
    /// ダブル並行リンク
    /// </summary>
    public bool isDouble;

    /// <summary>
    /// オフセット
    /// </summary>
    public List<Vector3> offsets = new();

    /// <summary>
    /// 回転方向
    /// </summary>
    public List<Vector3> dirs = new();

    /// <summary>
    /// 制御対象オブジェクト
    /// </summary>
    public GameObject pntObj0;
    public GameObject pntObj2_0;
    public GameObject pntObj2_1;
    public GameObject pntObj3;
    public GameObject pntObj4;
    public GameObject pntObj5_3;
    public GameObject pntObj5_4;

    /// <summary>
    /// 初期化
    /// </summary>
    public override void Initialize()
    {
        base.Initialize();

        // 制御対象オブジェクトを作成
        pntObj0 = new GameObject("Point0");
        pntObj0.transform.parent = mainAxis.model.transform;
        pntObj0.transform.position = axisInfos[0].model.transform.position;
        pntObj2_0 = new GameObject("Point2_0");
        pntObj2_0.transform.parent = axisInfos[0].model.transform;
        pntObj2_0.transform.position = axisInfos[2].model.transform.position;
        pntObj2_1 = new GameObject("Point2_1");
        pntObj2_1.transform.parent = axisInfos[1].model.transform;
        pntObj2_1.transform.position = axisInfos[2].model.transform.position;
        if (isDouble)
        {
            pntObj3 = new GameObject("Point3");
            pntObj3.transform.parent = axisInfos[1].model.transform;
            pntObj3.transform.position = axisInfos[3].model.transform.position;
            pntObj4 = new GameObject("Point4");
            pntObj4.transform.parent = axisInfos[0].model.transform;
            pntObj4.transform.position = axisInfos[4].model.transform.position;
            pntObj5_3 = new GameObject("Point5_3");
            pntObj5_3.transform.parent = axisInfos[3].model.transform;
            pntObj5_3.transform.position = axisInfos[5].model.transform.position;
            pntObj5_4 = new GameObject("Point5_4");
            pntObj5_4.transform.parent = axisInfos[4].model.transform;
            pntObj5_4.transform.position = axisInfos[5].model.transform.position;
        }
        // 回転方向取得と初期角度オフセット
        for (var i = 0; i < axisInfos.Count; i++)
        {
            offsets.Add(new());
            dirs.Add(new());
            if (axisInfos[i].model != null)
            {
                dirs[i] = GetRotationAxis(axisInfos[i].model);
                offsets[i] = axisInfos[i].model.transform.localEulerAngles;
            }
        }
    }

    /// <summary>
    /// 動作軸取得
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    public Vector3 GetRotationAxis(GameObject obj)
    {
        var pnt0 = obj.transform.InverseTransformPoint(mainAxis.model.transform.TransformPoint(Vector3.zero));
        var pnt1 = obj.transform.InverseTransformPoint(mainAxis.model.transform.TransformPoint(mainDir));
        var ret = pnt1 - pnt0;
        return ret;
    }

    /// <summary>
    /// 有効角度取得
    /// </summary>
    /// <param name="point"></param>
    /// <param name="dir"></param>
    /// <returns></returns>
    public float GetMaskAngle(Vector3 point, Vector3 dir)
    {
        var tmp = Vector3.Scale(point, dir);
        return tmp.x + tmp.y + tmp.z;
    }

    /// <summary>
    /// 位置更新
    /// </summary>
    public override void RenewPos()
    {
        base.RenewPos();

        var ang = GetMaskAngle(mainAxis.model.transform.localEulerAngles, mainDir);

        // 姿勢保持のため位置だけ
        axisInfos[0].model.transform.position = pntObj0.transform.position;
        axisInfos[1].model.transform.localEulerAngles = GetNextAngle(offsets[1], ang, dirs[1]);
        if (Vector3.Distance(pntObj2_0.transform.position, pntObj2_1.transform.position) > 0.001f)
        {
            // 1mm以上誤差があれば角度反転
            axisInfos[1].model.transform.localEulerAngles = GetNextAngle(offsets[1], -ang, dirs[1]);
        }
        if (isDouble)
        {
            var l3Ang = offsets[3];
            axisInfos[3].model.transform.position = pntObj3.transform.position;
            axisInfos[4].model.transform.localEulerAngles = GetNextAngle(offsets[4], ang, dirs[4]);
            axisInfos[4].model.transform.position = pntObj4.transform.position;
            if (Vector3.Distance(pntObj5_3.transform.position, pntObj5_4.transform.position) > 0.001f)
            {
                // 1mm以上誤差があれば角度反転
                axisInfos[4].model.transform.localEulerAngles = GetNextAngle(offsets[4], -ang, dirs[4]);
            }
        }
    }

    /// <summary>
    /// 次の角度を取得
    /// </summary>
    /// <param name="offset"></param>
    /// <param name="ang"></param>
    /// <param name="dir"></param>
    /// <returns></returns>
    private Vector3 GetNextAngle(Vector3 offset, float ang, Vector3 dir)
    {
        if ((dir == Vector3.right) || (dir == Vector3.left))
        {
            offset.x -= ang;
        }
        else if ((dir == Vector3.up) || (dir == Vector3.down))
        {
            offset.y -= ang;
        }
        else
        {
            offset.z -= ang;
        }
        return offset;
    }
}
