using MongoDB.Driver;
using Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Pool;
using static AxisMotionBase;

public class MultiObjectFactoryScript : UseTagBaseScript
{
    private class MutiObjectTag
    {
        /// <summary>
        /// データベース
        /// </summary>
        public string Database;

        /// <summary>
        /// 機番
        /// </summary>
        public string MechId;

        /// <summary>
        /// 生成タイミング
        /// </summary>
        public TagInfo CreateTag;

        /// <summary>
        /// タグの状態
        /// </summary>
        public bool tagStat = false;

        /// <summary>
        /// オブジェクト作成設定
        /// </summary>
        public List<MultiObjectInfo> createSettings = new List<MultiObjectInfo>();

        /// <summary>
        /// オブジェクト削除設定
        /// </summary>
        public List<MultiObjectInfo> deleteSettings = new List<MultiObjectInfo>();
    }

    private class MultiObjectInfo
    {
        /// <summary>
        /// 削除モード
        /// </summary>
        public bool IsDelete = false;

        /// <summary>
        /// 掴むことが可能か
        /// </summary>
        public bool IsGrabbable = true;

        /// <summary>
        /// 重力を使用するか
        /// </summary>
        public bool IsGravity = true;

        /// <summary>
        /// 接触可能か
        /// </summary>
        public bool IsTouch = true;

        /// <summary>
        /// オブジェクト生成ポイント
        /// </summary>
        public Vector3 CreatePoint;

        /// <summary>
        /// オブジェクト生成角度
        /// </summary>
        public Vector3 CreateRotate;

        /// <summary>
        /// ワークオブジェクト
        /// </summary>
        public GameObject WorkObject;

        /// <summary>
        /// ワーク名
        /// </summary>
        public string WorkName;

        /// <summary>
        /// ワークが生存している距離
        /// </summary>
        public float AliveDistance = 10f;

        /// <summary>
        /// バケット番号
        /// </summary>
        public int BacketNo = -1;

        /// <summary>
        /// ワーク変更
        /// </summary>
        public bool IsChange = false;

        /// <summary>
        /// 出力先親モデル
        /// </summary>
        public GameObject objBase;

        /// <summary>
        /// バケット情報
        /// </summary>
        public AxisMotionBase.BacketInfo backetInfo;

        /// <summary>
        /// バケットか
        /// </summary>

        public bool isBacket
        {
            get
            {
                return backetInfo != null;
            }
        }

        public bool isIgnoreBacket
        {
            get
            {
                return (backetInfo.backetno < 0) || (backetInfo.backetno != BacketNo);
            }
        }
    }

    private class WorkPool
    {
        public GameObject work;
        public ObjectPool<GameObject> pool;
        public List<GameObject> activeObjects = new List<GameObject>();
    }

    private Dictionary<string, Dictionary<string, MutiObjectTag>> multiObjects = new Dictionary<string, Dictionary<string, MutiObjectTag>>();
    private Dictionary<string, WorkPool> works = new Dictionary<string, WorkPool>();

    // Start is called before the first frame update
    protected override void Start()
    {
    }

    public void DeleteSetting()
    {
        foreach (var setting in multiObjects)
        {
            foreach (var obj in setting.Value)
            {
                obj.Value.CreateTag = null;
            }
        }
        multiObjects.Clear();
        foreach (var work in works)
        {
            work.Value.pool.Clear();
            work.Value.pool.Dispose();

        }
        works.Clear();
    }

    // Update is called once per frame
    protected override void MyFixedUpdate()
    {
        foreach (var setting in multiObjects)
        {
            foreach (var tag in setting.Value)
            {
                if (tag.Value.CreateTag == null)
                {
                    tag.Value.CreateTag = GlobalScript.GetTagInfo(tag.Value.Database, tag.Value.MechId, tag.Key);
                }
                else
                {
                    var stat = tag.Value.CreateTag.Value >= 1;
                    if (stat && !tag.Value.tagStat)
                    {
                        UpdateObject(tag.Value);
                    }
                    tag.Value.tagStat = stat;
                }
            }
        }
    }

    /// <summary>
    /// オブジェクトアップデート
    /// </summary>
    /// <param name="tag"></param>
    void UpdateObject(MutiObjectTag tag)
    {
        if (GlobalScript.isLoaded)
        {
            // オブジェクト作成処理
            foreach (var setting in tag.createSettings.FindAll(d => !d.isBacket || !d.isIgnoreBacket))
            {
                var change = false;
                // 生成前にチェック
                var near = setting.objBase.transform.GetComponentsInChildren<ObjectScript>()
                    .ToList()
                    .Find(d => Vector2.Distance(
                        new Vector2(d.transform.localPosition.x, d.transform.localPosition.z),
                        new Vector2(setting.CreatePoint.x, setting.CreatePoint.z)
                    ) < 0.001f);
                var work = works[setting.WorkName];
                if (near != null)
                {
                    if (setting.IsChange)
                    {
                        if (!near.name.Contains(work.work.name) && setting.IsChange)
                        {
                            DestroyImmediate(near.gameObject);
                            change = true;
                        }
                    }
                }
                if ((setting.IsChange && change) || (!setting.IsChange && (near == null)))
                {
                    var obj = work.pool.Get();
                    obj.transform.parent = setting.objBase.transform;
                    obj.transform.localPosition = setting.CreatePoint;
                    obj.transform.localEulerAngles = setting.CreateRotate;
                    var script = obj.GetComponent<ObjectScript>();
                    if (script == null)
                    {
                        script = obj.AddComponent<ObjectScript>();
                    }
                    script.AliveDistance = setting.AliveDistance;
                    script.IsGrabbable = setting.IsGrabbable;
                    script.IsGravity = setting.IsGravity;
                    script.IsTouch = setting.IsTouch;
                    var cbs = obj.GetComponent<CardboardScript>();
                    if (cbs != null)
                    {
                        // 設定をコピー
                        var org = work.work.GetComponent<CardboardScript>();
                        cbs.SetParameter(org);
                    }
                }
            }
            // オブジェクト削除処理
            foreach (var setting in tag.deleteSettings)
            {
                if (setting.isBacket)
                {
                    if (setting.isIgnoreBacket)
                    {
                        continue;
                    }
                }
                // クリアフラグON
                float dis = Vector3.Distance(transform.localPosition, setting.CreatePoint);
                if (dis < setting.AliveDistance)
                {
                    var dels = setting.objBase.GetComponentsInChildren<ObjectScript>();
                    foreach (var del in dels)
                    {
                        if (works[del.name].activeObjects.Contains(del.gameObject))
                        {
                            works[del.name].pool.Release(del.gameObject);
                        }
                    }
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
        var tags = new List<TagInfo>();
        foreach (var setting in multiObjects)
        {
            foreach (var obj in setting.Value)
            {
                if (obj.Value.CreateTag != null)
                {
                    tags.Add(obj.Value.CreateTag);
                }
            }
        }
        return tags;
    }

    /// <summary>
    /// 作成パラメータセット
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="obj"></param>
    /// <param name="backetInfo"></param>
    public void SetObjectParameter(UnitSetting unitSetting, object obj, AxisMotionBase.BacketInfo backetInfo = null)
    {
        if (obj.GetType() == typeof(WorkCreateSetting))
        {
            var wk = (WorkCreateSetting)obj;
            // ワーク名
            if (!works.ContainsKey(wk.work))
            {
                // ワーク作成
                var pool = new WorkPool
                {
                    work = GlobalScript.CreateWork(null, wk.work),
                };
                pool.work.name = wk.work;
                pool.pool = new ObjectPool<GameObject>(
                    createFunc: () =>
                    {
                        var obj = Instantiate(pool.work);
                        obj.name = wk.work;
                        return obj;
                    },
                    actionOnGet: obj =>
                    {
                        obj.SetActive(true);
                        pool.activeObjects.Add(obj);
                    },
                    actionOnRelease: obj =>
                    {
                        obj.SetActive(false);
                        obj.transform.parent = transform;
                        pool.activeObjects.Remove(obj);
                    },
                    actionOnDestroy: obj => DestroyImmediate(obj),
                    defaultCapacity: 250
                    );
                works.Add(wk.work, pool);
            }
            // 出力先オブジェクト
            var objFactoryObj = backetInfo != null ? backetInfo.obj : (wk.ignoreMove ? unitSetting.unitObject : unitSetting.moveObject);
            var objFactory = objFactoryObj.transform.GetComponentsInChildren<Transform>().ToList().Find(d => d.name == "ObjectFactory" && (d.parent == objFactoryObj.transform));
            var objBase = objFactory == null ? new GameObject("ObjectFactory") : objFactory.gameObject;
            objBase.transform.parent = objFactoryObj.transform;
            objBase.transform.localPosition = Vector3.zero;
            objBase.transform.localEulerAngles = Vector3.zero;
            // 設定追加
            var id = unitSetting.Database + ":" + unitSetting.mechId;
            if (!multiObjects.ContainsKey(id))
            {
                multiObjects.Add(id, new Dictionary<string, MutiObjectTag>());
            }
            if (!multiObjects[id].ContainsKey(wk.tag))
            {
                multiObjects[id].Add(wk.tag, new MutiObjectTag());
                multiObjects[id][wk.tag].Database = unitSetting.Database;
                multiObjects[id][wk.tag].MechId = unitSetting.mechId;
            }
            var multiObject = multiObjects[id][wk.tag];
            var setting = new MultiObjectInfo
            {
                IsGrabbable = wk.isGrabbable,
                IsGravity = wk.gravity,
                IsTouch = wk.isTouch,
                WorkName = wk.work,
                CreatePoint = new Vector3
                {
                    x = wk.pos[0],
                    y = wk.pos[1],
                    z = wk.pos[2]
                },
                CreateRotate = new Vector3
                {
                    x = wk.rot[0],
                    y = wk.rot[1],
                    z = wk.rot[2]
                },
                AliveDistance = wk.alive,
                IsChange = wk.change,
                backetInfo = backetInfo,
                BacketNo = backetInfo != null ? wk.backetno : -1,
                objBase = objBase
            };
            multiObject.createSettings.Add(setting);
        }
        else if (obj.GetType() == typeof(WorkDeleteSetting))
        {
            var wk = (WorkDeleteSetting)obj;
            // 設定追加
            var id = unitSetting.Database + ":" + unitSetting.mechId;
            if (!multiObjects.ContainsKey(id))
            {
                multiObjects.Add(id, new Dictionary<string, MutiObjectTag>());
            }
            if (!multiObjects[id].ContainsKey(wk.tag))
            {
                multiObjects[id].Add(wk.tag, new MutiObjectTag());
                multiObjects[id][wk.tag].Database = unitSetting.Database;
                multiObjects[id][wk.tag].MechId = unitSetting.mechId;
            }
            var multiObject = multiObjects[id][wk.tag];
            var setting = new MultiObjectInfo
            {
                CreatePoint = new Vector3
                {
                    x = wk.pos[0],
                    y = wk.pos[1],
                    z = wk.pos[2]
                },
                AliveDistance = wk.distance,
                backetInfo = backetInfo,
                BacketNo = backetInfo != null ? wk.backetno : -1,
                objBase = backetInfo != null ? backetInfo.obj : unitSetting.unitObject
            };
            multiObject.deleteSettings.Add(setting);
        }
    }
}
