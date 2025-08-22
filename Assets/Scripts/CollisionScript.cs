using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SocialPlatforms;
using static KssBaseScript;
using static OVRPlugin;

public class CollisionScript : KssBaseScript
{
    private bool isWork;

    private Material redMaterial;
    private Material yellowMaterial;

    protected override void Start()
    {
        base.Start();

        isWork = GetComponent<ObjectScript>() != null;

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
                //　衝突検知無効
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

    /*
    protected override void OnCollisionEnter(Collision other)
    {
        var work = this.GetComponent<ObjectScript>();
        if (work == null)
        {
            var unit = other.transform.parent.GetComponent<AxisMotionBase>();
            if (unit != null)
            {
                // ユニット取得
                var mesh = other.gameObject.GetComponentInChildren<MeshRenderer>();
                if (mesh != null)
                {
                    SetMaterial(mesh, redMaterial);
                }
            }
        }
    }

    protected override void OnCollisionExit(Collision other)
    {
        var work = this.GetComponent<ObjectScript>();
        if (work == null)
        {
            var unit = other.transform.parent.GetComponent<AxisMotionBase>();
            if (unit != null)
            {
                // ユニット取得
                var mesh = other.gameObject.GetComponentInChildren<MeshRenderer>();
                if (mesh != null)
                {
                    SetMaterial(mesh, yellowMaterial);
                }
            }
        }
    }
    */

    protected override void OnTriggerEnter(Collider collider)
    {
        if (GlobalScript.isCollision)
        {
            if (!collider.transform.parent.IsChildOf(this.transform) && !isWork)
            {
                var mesh = collider.gameObject.GetComponentInChildren<MeshRenderer>();
                if (mesh == null)
                {
                    mesh = FindNearestParentMeshRenderer(collider.transform);
                }
                SetMaterial(mesh, redMaterial);
            }
        }
    }

    protected override void OnTriggerStay(Collider collider)
    {
    }

    protected override void OnTriggerExit(Collider collider)
    {
        if (GlobalScript.isCollision)
        {
            if (!collider.transform.parent.IsChildOf(this.transform) && !isWork)
            {
                var mesh = collider.gameObject.GetComponentInChildren<MeshRenderer>();
                if (mesh == null)
                {
                    mesh = FindNearestParentMeshRenderer(collider.transform);
                }
                SetMaterial(mesh, yellowMaterial);
            }
        }
    }

    private void SetMaterial(MeshRenderer mesh, Material obj)
    {
        if (mesh != null)
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
