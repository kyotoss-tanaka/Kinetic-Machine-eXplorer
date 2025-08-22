using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class EventPropagation : KssBaseScript
{
    /// <summary>
    /// �e�X�N���v�g
    /// </summary>
    private List<KssBaseScript> parentScripts = new List<KssBaseScript>();

    /// <summary>
    /// ���[�h����
    /// </summary>
    private bool isLoaded = false;

    /// <summary>
    /// �X�V����
    /// </summary>
    protected override void MyFixedUpdate()
    {
        base.MyFixedUpdate();

        if (!GlobalScript.isLoaded || GlobalScript.isReqLoadEvent)
        {
            isLoaded = false;
            GlobalScript.isReqLoadEvent = false;
        }
        else if (!isLoaded)
        {
            isLoaded = true;
            // �e�X�N���v�g
            parentScripts = transform.parent.GetComponentsInChildren<KssBaseScript>().Where(d => d.transform == transform.parent).ToList();
        }
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

    public override void OnMouseEnter()
    {
        base.OnMouseEnter();
        foreach (var script in parentScripts)
        {
            script.OnMouseEnter();
        }
    }

    public override void OnMouseExit()
    {
        base.OnMouseExit();
        foreach (var script in parentScripts)
        {
            script.OnMouseExit();
        }
    }
}
