using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;

public class ActRotation : Kinematics1D
{
    [SerializeField]
    public GameObject RotateObject;

    float tx = 0;

    #region �֐�
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
    /// �ڕW�_�Z�b�g
    /// </summary>
    /// <param name="x"></param>
    public override void setTarget(float x)
    {
        if (RotateObject == null)
        {
            // ��������
            this.transform.localEulerAngles = new Vector3 (0, x, 0);
        }
        else
        {
            // �q������
            RotateObject.transform.localEulerAngles = new Vector3(0, x, 0);
        }
    }

    /// <summary>
    /// �p�����[�^���Z�b�g����
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
    #endregion �֐�
}
