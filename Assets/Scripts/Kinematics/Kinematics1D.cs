using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;

public class Kinematics1D : KinematicsBase
{
    #region �v���p�e�B
    [SerializeField]
    protected TagInfo X;

    #endregion �v���p�e�B

    #region �֐�

    // Start is called before the first frame update
    protected override void Start()
    {
        base.Start();
        if (baseObject == null)
        {
            ModelRestruct();
        }
    }

    /// <summary>
    /// �g�p���Ă���^�O���擾����
    /// </summary>
    /// <returns></returns>
    public override List<TagInfo> GetUseTags()
    {
        return new List<TagInfo> { X };
    }

    /// <summary>
    /// �ڕW�ʒu�Z�b�g
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    public virtual void setTarget(float x)
    {
    }

    /// <summary>
    /// �p�����[�^�Z�b�g
    /// </summary>
    /// <param name="components"></param>
    /// <param name="scriptables"></param>
    /// <param name="kssInstanceIds"></param>
    /// <param name="root"></param>
    public override void SetParameter(List<Component> components, List<KssPartsBase> scriptables, List<KssInstanceIds> kssInstanceIds, JsonElement root)
    {
        base.SetParameter(components, scriptables, kssInstanceIds, root);
        if (X != null)
        {
            Destroy(X);
        }
        X = GetTagInfoFromPrm(scriptables, kssInstanceIds, root, "X");
    }
    #endregion �֐�
}
