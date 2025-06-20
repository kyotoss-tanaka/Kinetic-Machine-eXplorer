using Parameters;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CanvasBaseScript : BaseBehaviour
{
    protected UnitSetting unitSetting;

    protected override void Update()
    {
        base.Update();
        transform.rotation = Camera.main.transform.rotation;
    }

    public virtual void SetUnitSetting(UnitSetting unitSetting)
    {
        this.unitSetting = unitSetting;
    }
}
