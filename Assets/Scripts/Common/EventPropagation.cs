using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class EventPropagation : KssBaseScript
{
    /// <summary>
    /// 親スクリプト
    /// </summary>
    private List<KssBaseScript> parentScripts = new List<KssBaseScript>();
    protected override void Start()
    {
        base.Start();

        // 親スクリプト
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
