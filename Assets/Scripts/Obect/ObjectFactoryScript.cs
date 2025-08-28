using Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
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
    /// �d�͂��g�p���邩
    /// </summary>
    [SerializeField]
    private bool IsGravity = true;

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
    /// �^�O��
    /// </summary>
    private string tagName = "";
    /// <summary>
    /// ���[�N�[�I�u�W�F�N�g
    /// </summary>
    private GameObject work;
    // Start is called before the first frame update
    protected override void Start()
    {
        var objFactory = transform.GetComponentsInChildren<Transform>().ToList().Find(d => d.name == "ObjectFuctory");
        if (objFactory == null)
        {
            objBase = new GameObject("ObjectFuctory");
        }
        else
        {
            objBase = objFactory.gameObject;
        }
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
        if (CreateTag == null)
        {
            CreateTag = GlobalScript.GetTagInfo(unitSetting.Database, unitSetting.mechId, tagName);
        }
        else
        {
            var stat = CreateTag.Value >= 1;
            if (!IsTimer && (CreateTag != null) && stat)
            {
                if (!tagStat)
                {
                    CreateObject();
                }
            }
            tagStat = stat;
        }
    }

    void CreateObject()
    {
        var obj = Instantiate(work);
        obj.transform.parent = objBase.transform;
        obj.transform.localPosition = CreatePoint;
        obj.transform.localEulerAngles = CreateRotate;
        // ���ɐ����ς݂��`�F�b�N(���ʋ�����1mm�ȉ��Ȃ瓯��I�u�W�F�N�g�Ƃ݂Ȃ�)
        var near = objBase.transform.GetComponentsInChildren<ObjectScript>().ToList().Find(d => Vector2.Distance(new Vector2(d.transform.localPosition.x, d.transform.localPosition.z), new Vector2(obj.transform.localPosition.x, obj.transform.localPosition.z)) < 0.001f);
        if (near == null)
        {
            obj.SetActive(true);
            var script = obj.AddComponent<ObjectScript>();
            script.AliveDistance = AliveDistance;
            script.IsGrabbable = IsGrabbable;
            script.IsGravity = IsGravity;
            var cbs = obj.GetComponent<CardboardScript>();
            if (cbs != null)
            {
                // �ݒ���R�s�[
                var org = work.GetComponent<CardboardScript>();
                cbs.SetParameter(org);
            }
        }
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
    /// �p�����[�^���Z�b�g����
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="obj"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);

        var wk = (WorkCreateSetting)obj;
        IsGrabbable = wk.isGrabbable;
        IsGravity = wk.gravity;
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
        tagName = wk.tag;
        AliveDistance = wk.alive;
    }
}
