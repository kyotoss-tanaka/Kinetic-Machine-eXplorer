using Parameters;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;

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
            if (s.auto)
            {
                // 自動生成
                foreach (var r in unitSetting.moveObject.GetComponentsInChildren<Renderer>())
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null)
                        continue;

                    if (r.GetComponent<Collider>() == null)
                    {
                        // ローカル bounds を使用
                        Bounds b = mf.sharedMesh.bounds;
                        var box = r.gameObject.AddComponent<BoxCollider>();
                        box.center = b.center;
                        box.size = b.size;
                        box.isTrigger = false;
                    }
                }
            }
            else
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
        }
        // 親から設定されることを回避するためにセット
        /* 不必要？
        foreach (var mesh in this.GetComponentsInChildren<MeshFilter>())
        {
            if (mesh.GetComponentInChildren<Collider>() == null)
            {
                var col = mesh.AddComponent<BoxCollider>();
                col.center = new Vector3();
                col.size = new Vector3();
            }
        }
        */
    }
}
