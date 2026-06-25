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
        var c = CommonFunction.MainCamera;   // Camera.main キャッシュ（ビルボード：毎フレーム×全Canvas）
        if (c != null) transform.rotation = c.transform.rotation;
    }

    public virtual void SetUnitSetting(UnitSetting unitSetting)
    {
        this.unitSetting = unitSetting;
    }
}
