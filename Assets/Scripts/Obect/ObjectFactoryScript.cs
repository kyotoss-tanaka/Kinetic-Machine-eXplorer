using Parameters;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;

public class ObjectFactoryScript : UseTagBaseScript
{
    /// <summary>
    /// �͂ނ��Ƃ��\��
    /// </summary>
    [SerializeField]
    private bool IsGrabbable = true;

    /// <summary>
    ///  �^�C�}�[
    /// </summary>
    [SerializeField]
    private bool IsTimer = true;

    /// <summary>
    /// ��������
    /// </summary>
    [SerializeField]
    private int Interval = 1000;

    /// <summary>
    /// �����^�C�~���O
    /// </summary>
    [SerializeField]
    private TagInfo CreateTag;

    /// <summary>
    /// �I�u�W�F�N�g�����|�C���g
    /// </summary>
    [SerializeField]
    private Vector3 CreatePoint;

    /// <summary>
    /// �I�u�W�F�N�g�����p�x
    /// </summary>
    [SerializeField]
    private Vector3 CreateRotate;

    /// <summary>
    /// ���[�N�I�u�W�F�N�g
    /// </summary>
    [SerializeField]
    private GameObject WorkObject;

    /// <summary>
    /// ���[�N��
    /// </summary>
    [SerializeField]
    private string WorkName;

    /// <summary>
    /// ���[�N���������Ă��鋗��
    /// </summary>
    [SerializeField]
    private float AliveDistance = 10f;

    /// <summary>
    /// �I�u�W�F�N�g�����p
    /// </summary>
    private GameObject objBase;

    /// <summary>
    /// �^�O�̏��
    /// </summary>
    private bool tagStat = false;

    /// <summary>
    /// ���[�N�[�I�u�W�F�N�g
    /// </summary>
    private GameObject work;
    // Start is called before the first frame update
    protected override void Start()
    {
        objBase = new GameObject("ObjectFuctory");
        objBase.transform.parent = transform;
        objBase.transform.position = transform.position;
        objBase.transform.eulerAngles = transform.eulerAngles;

        work = GlobalScript.CreateWork(WorkObject, WorkName);

        if (IsTimer)
        {
            InvokeRepeating("CreateObject", 0, Interval / 1000f);
        }
    }

    // Update is called once per frame
    protected override void MyFixedUpdate()
    {
        var stat = GlobalScript.GetTagData(CreateTag) >= 1;
        if (!IsTimer && (CreateTag != null) && stat)
        {
            if (!tagStat)
            {
                CreateObject();
            }
        }
        tagStat = stat;
    }

    void CreateObject()
    {
        var obj = Instantiate(work);
        obj.transform.parent = objBase.transform;
        obj.transform.localPosition = Vector3.Scale(CreatePoint, transform.localScale);
        obj.transform.localEulerAngles = Vector3.Scale(CreateRotate, transform.localScale);
        obj.SetActive(true);
        var script = obj.AddComponent<ObjectScript>();
        script.AliveDistance = AliveDistance;
        script.IsGrabbable = IsGrabbable;
    }

    /// <summary>
    /// �g�p���Ă���^�O���擾����
    /// </summary>
    /// <returns></returns>
    public override List<TagInfo> GetUseTags()
    {
        return new List<TagInfo> { CreateTag };
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
        IsTimer = GetBooleanFromPrm(root, "IsTimer");
        Interval = GetInt32FromPrm(root, "Interval");
        CreateTag = GetTagInfoFromPrm(scriptables, kssInstanceIds, root, "CreateTag");
        CreatePoint = GetVector3FromPrm(root, "CreatePoint");
        WorkObject = GetGameObjectFromPrm(components, kssInstanceIds, root, "WorkObject");
        WorkName = GetStringFromPrm(root, "WorkName");
        AliveDistance = GetFloatFromPrm(root, "AliveDistance");
        IsGrabbable = GetBooleanFromPrm(root, "IsGrabbable");
    }

    /// <summary>
    /// �p�����[�^���Z�b�g����
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="obj"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);

        var wk = (WorkCreateSetting)obj;
        IsGrabbable = wk.isGrabbable;
        IsTimer = wk.isTimer;
        WorkName = wk.work;
        CreatePoint = new Vector3
        {
            x = wk.pos[0],
            y = wk.pos[1],
            z = wk.pos[2]
        };
        CreateRotate = new Vector3
        {
            x = wk.rot[0],
            y = wk.rot[1],
            z = wk.rot[2]
        };
        CreateTag = ScriptableObject.CreateInstance<TagInfo>();
        CreateTag.Database = unitSetting.Database;
        CreateTag.MechId = unitSetting.mechId;
        CreateTag.Tag = wk.tag;
        AliveDistance = wk.alive;
    }
}
