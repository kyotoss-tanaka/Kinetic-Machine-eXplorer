using Parameters;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;
using static OVRPlugin;

public class ShapeScript : UseTagBaseScript
{
    /// <summary>
    /// 衝突検知
    /// </summary>
    /// <param name="other"></param>
    protected override void OnCollisionEnter(Collision other)
    {
        base.OnCollisionEnter(other);
        var obj = other.transform.GetComponentInParent<ObjectScript>();
        if (obj != null)
        {
            if (obj.transform.parent == null)
            {
                obj.transform.parent = transform;
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
        
        var shape = (ShapeSetting)obj;
        foreach (var s in shape.datas)
        {
            var box = transform.AddComponent<BoxCollider>();
            box.isTrigger = false;
            box.center = new Vector3
            {
                x = s.center[0],
                y = s.center[1],
                z = s.center[2]
            };
            box.size = new Vector3
            {
                x = s.size[0],
                y = s.size[1],
                z = s.size[2]
            };
        }
        // 親から設定されることを回避するためにセット
        foreach (var mesh in this.GetComponentsInChildren<MeshFilter>())
        {
            if (mesh.GetComponentInChildren<Collider>() == null)
            {
                var col = mesh.AddComponent<BoxCollider>();
                col.center = new Vector3();
                col.size = new Vector3();
            }
        }
    }
}
