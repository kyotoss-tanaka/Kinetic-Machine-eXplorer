using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class EventPropagation : KssBaseScript
{
    /// <summary>
    /// �e�X�N���v�g
    /// </summary>
    private List<KssBaseScript> parentScripts = new List<KssBaseScript>();
    protected override void Start()
    {
        base.Start();

        // �e�X�N���v�g
        parentScripts = transform.parent.GetComponentsInChildren<KssBaseScript>().Where(d => d.transform == transform.parent).ToList();
    }

    public override void OnMouseDown()
    {
        base.OnMouseDown();
        foreach (var script in parentScripts)
        {
            script.OnMouseDown();
        }
    }

    public override void OnMouseUp()
    {
        base.OnMouseUp();
        foreach (var script in parentScripts)
        {
            script.OnMouseUp();
        }
    }
}
