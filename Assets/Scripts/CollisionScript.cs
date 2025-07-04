using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using static KssBaseScript;

public class CollisionScript : KssBaseScript
{
    private Material redMaterial;
    private Material yellowMaterial;

    protected override void Start()
    {
        base.Start();

        redMaterial = (Material)Resources.Load("Materials/RedMaterial");
        yellowMaterial = (Material)Resources.Load("Materials/YellowMaterial");
    }

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
            else
            {
            }
        }
    }

    protected override void OnTriggerEnter(Collider collider)
    {
        var mesh = collider.gameObject.GetComponentInChildren<MeshRenderer>();
        if (mesh == null)
        {
            mesh = FindNearestParentMeshRenderer(collider.transform);
        }
        SetMaterial(mesh, redMaterial);
    }

    protected override void OnTriggerExit(Collider collider)
    {
        var mesh = collider.gameObject.GetComponentInChildren<MeshRenderer>();
        if (mesh == null)
        {
            mesh = FindNearestParentMeshRenderer(collider.transform);
        }
        SetMaterial(mesh, yellowMaterial);
    }

    private void SetMaterial(MeshRenderer mesh, Material obj)
    {
        if (GlobalScript.isCollision && (mesh != null))
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

    private MeshRenderer FindNearestParentMeshRenderer(Transform start)
    {
        Transform current = start.parent;
        while (current != null)
        {
            var renderer = current.GetComponent<MeshRenderer>();
            if (renderer != null)
                return renderer;

            current = current.parent;
        }
        return null;
    }
}
