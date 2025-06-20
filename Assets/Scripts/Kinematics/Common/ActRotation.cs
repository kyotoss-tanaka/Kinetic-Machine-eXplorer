using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;

public class ActRotation : Kinematics1D
{
    [SerializeField]
    public GameObject RotateObject;

    float tx = 0;

    #region 関数
    // Start is called before the first frame update
    protected override void Start()
    {
        
    }

    // Update is called once per frame
    protected override void MyFixedUpdate()
    {
        tx = GlobalScript.GetTagData(X) / 1000f;

        setTarget(tx);
    }

    /// <summary>
    /// 目標点セット
    /// </summary>
    /// <param name="x"></param>
    public override void setTarget(float x)
    {
        if (RotateObject == null)
        {
            // 自分を回す
            this.transform.localEulerAngles = new Vector3 (0, x, 0);
        }
        else
        {
            // 子供を回す
            RotateObject.transform.localEulerAngles = new Vector3(0, x, 0);
        }
    }

    /// <summary>
    /// パラメータをセットする
    /// </summary>
    /// <param name="components"></param>
    /// <param name="scriptables"></param>
    /// <param name="kssInstanceIds"></param>
    /// <param name="root"></param>
    public override void SetParameter(List<Component> components, List<KssPartsBase> scriptables, List<KssInstanceIds> kssInstanceIds, JsonElement root)
    {
        base.SetParameter(components, scriptables, kssInstanceIds, root);
        RotateObject = GetGameObjectFromPrm(components, kssInstanceIds, root, "RotateObject");
    }
    #endregion 関数
}
