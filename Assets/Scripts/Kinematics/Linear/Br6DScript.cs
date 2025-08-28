using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.XR.OpenXR.Input;

public class Br6DScript : PlanarMotor
{
    public class ShuttleTagInfo
    {
        public TagInfo X;
        public TagInfo Y;
        public TagInfo Z;
        public TagInfo RX;
        public TagInfo RY;
        public TagInfo RZ;
    }

    /// <summary>
    /// 座標セット
    /// </summary>
    /// <param name="index"></param>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    /// <param name="rx"></param>
    /// <param name="ry"></param>
    /// <param name="rz"></param>
    public override void SetTarget(int index, float x, float y, float z, float rx, float ry, float rz)
    {
        shuttles[index].transform.localPosition = new Vector3(x, y, z);
        shuttles[index].transform.localEulerAngles = new Vector3(rx, ry, rz);
    }

    public GameObject GetNearShuttle(Vector3 vector)
    {
        GameObject ret = null;
        float distance = 1000;
        foreach (var sh in shuttles)
        {
            float dis = Vector3.Distance(sh.transform.position, vector);
            if (distance > dis)
            {
                ret = sh;
                distance = dis;
            }
        }
        return ret;
    }

    protected override void OnCollisionEnter(Collision other)
    {
        base.OnCollisionEnter(other);
    }

    protected override void OnDestroy()
    {
        Destroy(objBase);
        base.OnDestroy();
    }
}
