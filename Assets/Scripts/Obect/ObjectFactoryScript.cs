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
    /// 掴むことが可能か
    /// </summary>
    [SerializeField]
    private bool IsGrabbable = true;

    /// <summary>
    /// 重力を使用するか
    /// </summary>
    [SerializeField]
    private bool IsGravity = true;

    /// <summary>
    /// 接触可能か
    /// </summary>
    [SerializeField]
    private bool IsTouch = true;

    /// <summary>
    ///  タイマー
    /// </summary>
    [SerializeField]
    private bool IsTimer = true;

    /// <summary>
    /// 生成周期
    /// </summary>
    [SerializeField]
    private int Interval = 1000;

    /// <summary>
    /// 生成タイミング
    /// </summary>
    [SerializeField]
    private TagInfo CreateTag;

    /// <summary>
    /// オブジェクト生成ポイント
    /// </summary>
    [SerializeField]
    private Vector3 CreatePoint;

    /// <summary>
    /// オブジェクト生成角度
    /// </summary>
    [SerializeField]
    private Vector3 CreateRotate;

    /// <summary>
    /// ワークオブジェクト
    /// </summary>
    [SerializeField]
    private GameObject WorkObject;

    /// <summary>
    /// ワーク名
    /// </summary>
    [SerializeField]
    private string WorkName;

    /// <summary>
    /// ワークが生存している距離
    /// </summary>
    [SerializeField]
    private float AliveDistance = 10f;

    /// <summary>
    /// バケット番号
    /// </summary>
    [SerializeField]
    private int BacketNo = -1;

    /// <summary>
    /// ワーク変更
    /// </summary>
    [SerializeField]
    private bool IsChange = false;

    /// <summary>
    /// オブジェクト生成用
    /// </summary>
    private GameObject objBase;

    /// <summary>
    /// タグの状態
    /// </summary>
    private bool tagStat = false;

    /// <summary>
    /// タグ名
    /// </summary>
    private string tagName = "";

    /// <summary>
    /// ワークーオブジェクト
    /// </summary>
    private GameObject work;

    /// <summary>
    /// バケット情報
    /// </summary>
    private AxisMotionBase.BacketInfo backetInfo;

    /// <summary>
    /// バケットか
    /// </summary>

    private bool isBacket
    {
        get
        {
            return backetInfo != null;
        }
    }
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
        if (GlobalScript.isLoaded)
        {
            if (isBacket)
            {
                if ((backetInfo.backetno < 0) || (backetInfo.backetno != BacketNo))
                {
                    return;
                }
            }
            var change = false;
            var obj = Instantiate(work);
            obj.transform.parent = objBase.transform;
            obj.transform.localPosition = CreatePoint;
            obj.transform.localEulerAngles = CreateRotate;
            // 既に生成済みかチェック(平面距離が1mm以下なら同一オブジェクトとみなす)
            var near = objBase.transform.GetComponentsInChildren<ObjectScript>().ToList().Find(d => Vector2.Distance(new Vector2(d.transform.localPosition.x, d.transform.localPosition.z), new Vector2(obj.transform.localPosition.x, obj.transform.localPosition.z)) < 0.001f);
            if (near != null)
            {
                if (near.name.Contains(work.name) || !IsChange)
                {
                    DestroyImmediate(obj);
                }
                else
                {
                    DestroyImmediate(near.gameObject);
                    change = true;
                }
            }
            if ((IsChange && change) || (!IsChange && (near == null)))
            {
                obj.SetActive(true);
                var script = obj.AddComponent<ObjectScript>();
                script.AliveDistance = AliveDistance;
                script.IsGrabbable = IsGrabbable;
                script.IsGravity = IsGravity;
                script.IsTouch = IsTouch;
                var cbs = obj.GetComponent<CardboardScript>();
                if (cbs != null)
                {
                    // 設定をコピー
                    var org = work.GetComponent<CardboardScript>();
                    cbs.SetParameter(org);
                }
            }
        }
    }

    /// <summary>
    /// 使用しているタグを取得する
    /// </summary>
    /// <returns></returns>
    public override List<TagInfo> GetUseTags()
    {
        return new List<TagInfo> { CreateTag };
    }

    /// <summary>
    /// パラメータをセットする
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="obj"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);

        var wk = (WorkCreateSetting)obj;
        IsGrabbable = wk.isGrabbable;
        IsGravity = wk.gravity;
        IsTouch = wk.isTouch;
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
        BacketNo = wk.backetno;
        IsChange = wk.change;
    }

    /// <summary>
    /// バケット情報セット
    /// </summary>
    /// <param name="backetInfo"></param>
    public void SetBacketInfo(AxisMotionBase.BacketInfo backetInfo)
    {
        this.backetInfo = backetInfo;
    }
}
