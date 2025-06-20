using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using static KssBaseScript;

public class CollisionScript : KssBaseScript
{
    protected override void MyFixedUpdate()
    {
        base.MyFixedUpdate();
        lock (GlobalScript.objColLock)
        {
            if (!GlobalScript.isCollision)
            {
                var changes = GlobalScript.dctMaterial.Where(d => d.Value.isChange).ToList();
                //Å@è’ìÀåüímñ≥å¯
                foreach (var m in changes)
                {
                    if (m.Key.IsDestroyed())
                    {
                        GlobalScript.dctMaterial.Remove(m.Key);
                    }
                    else
                    {
                        m.Key.material = m.Value.material;
                        m.Value.isChange = false;
                    }
                }
            }
        }
    }

    protected override void OnTriggerEnter(Collider collider)
    {
        var mesh = collider.gameObject.GetComponentInChildren<MeshRenderer>();
        var obj = (Material)Resources.Load("Materials/RedMaterial");
        SetMaterial(mesh, obj);
    }

    protected override void OnTriggerExit(Collider collider)
    {
        var mesh = collider.gameObject.GetComponentInChildren<MeshRenderer>();
        var obj = (Material)Resources.Load("Materials/YellowMaterial");
        SetMaterial(mesh, obj);
    }

    private void SetMaterial(MeshRenderer mesh, Material obj)
    {
        if (GlobalScript.isCollision)
        {
            if (!GlobalScript.dctMaterial.ContainsKey(mesh))
            {
                GlobalScript.dctMaterial.Add(mesh, new GlobalScript.ChangeMaterial());
            }
            var data = GlobalScript.dctMaterial[mesh];
            if (data.material == null)
            {
                data.material = mesh.material;
            }
            lock (GlobalScript.objColLock)
            {
                data.isChange = true;
                mesh.material = obj;
            }
        }
    }
}
