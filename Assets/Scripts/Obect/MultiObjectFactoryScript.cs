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
        /// 反転入力（タグ名が-始まり。OFFで動作する）
        /// </summary>
        public bool isReverse = false;

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

        /// <summary>
        /// ワーク受渡設定（アタッチ/変換）
        /// </summary>
        public List<MultiObjectInfo> transferSettings = new List<MultiObjectInfo>();
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
        /// 受渡モード（0=アタッチ、1=変換）
        /// </summary>
        public int Mode = -1;

        /// <summary>
        /// 変換先ワーク名
        /// </summary>
        public string WorkTo = "";

        /// <summary>
        /// アタッチ中のワーク
        /// </summary>
        public List<GameObject> Attached = new List<GameObject>();

        /// <summary>
        /// 設計位置を使用
        /// </summary>
        public bool IsDesignPos = false;

        /// <summary>
        /// 設計配置テンプレート（ワークモデル設定の元モデル）
        /// </summary>
        public GameObject DesignTemplate;

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

        /// <summary>バケット削除の発動位置（経路上の固定点・ワールド）を使うか</summary>
        public bool IsFixedDeletePos = false;

        /// <summary>バケット削除の発動位置（ワールド）</summary>
        public Vector3 FixedDeletePos;

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

    /// <summary>
    /// 自身のインスタンス（コンベア等がアクティブワークを列挙するために使用）
    /// </summary>
    private static MultiObjectFactoryScript instance;

    /// <summary>
    /// 全プールのアクティブワークを列挙する（コンベア搬送等の走査用）
    /// </summary>
    public static IEnumerable<GameObject> EnumerateActiveWorks()
    {
        if (instance == null)
        {
            yield break;
        }
        foreach (var pool in instance.works)
        {
            foreach (var obj in pool.Value.activeObjects)
            {
                if (obj != null)
                {
                    yield return obj;
                }
            }
        }
    }

    // Start is called before the first frame update
    protected override void Start()
    {
        instance = this;
    }

    /// <summary>
    /// 有効化時（エディタのドメインリロード後はStartが再実行されずstaticが消えるため、
    /// OnEnableでも復元してF5リロードで復旧できるようにする）
    /// </summary>
    protected override void OnEnable()
    {
        base.OnEnable();
        instance = this;
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
                    // -始まりは反転入力（OFFで動作）
                    var name = tag.Key;
                    if ((name != "") && (name[0] == '-'))
                    {
                        tag.Value.isReverse = true;
                        name = name.Substring(1);
                    }
                    tag.Value.CreateTag = GlobalScript.GetTagInfo(tag.Value.Database, tag.Value.MechId, name);
                    if ((tag.Value.CreateTag != null) && tag.Value.isReverse)
                    {
                        // 反転入力は初期状態(OFF)で即動作しないよう発火済み扱いにする
                        tag.Value.tagStat = true;
                    }
                }
                else
                {
                    var stat = tag.Value.isReverse ? (tag.Value.CreateTag.Value < 1) : (tag.Value.CreateTag.Value >= 1);
                    if (stat && !tag.Value.tagStat)
                    {
                        UpdateObject(tag.Value);
                    }
                    tag.Value.tagStat = stat;
                    // アタッチはレベル動作（ON中は範囲内のワークを保持、OFFで解放）
                    foreach (var transfer in tag.Value.transferSettings)
                    {
                        if (transfer.Mode == 0)
                        {
                            if (transfer.isBacket && transfer.isIgnoreBacket)
                            {
                                continue;
                            }
                            ProcessAttach(transfer, stat);
                        }
                    }
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
            // オブジェクト削除処理
            // ※同一タグにワーク切り替え（旧ワーク削除＋新ワーク生成）を割り付けられるよう、削除を先に処理する
            foreach (var setting in tag.deleteSettings)
            {
                if (setting.isBacket)
                {
                    if (setting.isIgnoreBacket)
                    {
                        continue;
                    }
                }
                // クリアフラグON：削除位置から範囲内にあるワークのみ削除する
                // ※親子関係に依存せず全アクティブワークから探す（受渡・物理搬送などでobjBase配下にいないワークも対象。変換処理と同方式）
                // 削除位置・範囲は実寸(m)。objBaseにスケールが掛かっていても実寸で判定できるよう、
                // 削除位置はobjBase原点からの回転付きオフセット（スケール除外）でワールドへ変換して比較する
                // バケット削除は経路上の固定点（表示球と同一）、それ以外はobjBase基準の実寸オフセットで判定する
                var worldDelete = setting.IsFixedDeletePos
                    ? setting.FixedDeletePos
                    : setting.objBase.transform.position + setting.objBase.transform.rotation * setting.CreatePoint;
                var deleted = 0;
                var candidates = 0;
                foreach (var pool in works.ToList())
                {
                    // ワーク名指定ありなら対象ワークのみ削除（空欄=全ワーク）
                    if ((setting.WorkName != null) && (setting.WorkName != "") && (pool.Key != setting.WorkName))
                    {
                        continue;
                    }
                    foreach (var obj in pool.Value.activeObjects.ToList())
                    {
                        if (obj == null)
                        {
                            continue;
                        }
                        candidates++;
                        // 「球（削除位置＋範囲）がワークの見た目に触れていれば削除」とするため、
                        // ワークのレンダラ境界ボックス上の最近点と削除位置の距離で判定する
                        // （中心点判定だと背の高いワークの下部に球が重なっていても中心が範囲外で消えない）
                        var nearest = obj.transform.position;
                        var rends = obj.GetComponentsInChildren<Renderer>();
                        if (rends.Length > 0)
                        {
                            var bounds = rends[0].bounds;
                            for (var ri = 1; ri < rends.Length; ri++)
                            {
                                bounds.Encapsulate(rends[ri].bounds);
                            }
                            nearest = bounds.ClosestPoint(worldDelete);
                        }
                        var dis = Vector3.Distance(nearest, worldDelete);
                        if (dis >= setting.AliveDistance)
                        {
                            continue;
                        }
                        pool.Value.pool.Release(obj);
                        deleted++;
                    }
                }
                Debug.Log($"[WorkDelete] {setting.objBase.name} 削除位置={worldDelete} 範囲={setting.AliveDistance:F3} 候補={candidates} 削除={deleted}");
            }
            // ワーク変換処理（削除の後、生成の前に行う）
            foreach (var setting in tag.transferSettings.FindAll(d => d.Mode == 1))
            {
                if (setting.isBacket && setting.isIgnoreBacket)
                {
                    continue;
                }
                ProcessChange(setting);
            }
            // オブジェクト作成処理
            foreach (var setting in tag.createSettings.FindAll(d => !d.isBacket || !d.isIgnoreBacket))
            {
                // 生成座標
                var createPoint = setting.CreatePoint;
                var createRotate = setting.CreateRotate;
                if (setting.IsDesignPos && (setting.DesignTemplate != null))
                {
                    // 設計位置を使用：生成タイミング時点の生成元モデルとの位置関係で算出する
                    // （生成元モデルは動作しているため、ロード時の初期姿勢基準では位置がずれる）
                    // X,Y,Z/RX,RY,RZが設定されている場合は設計位置からの相対オフセットとして加算する（設計位置の姿勢基準）
                    var designRot = setting.DesignTemplate.transform.rotation;
                    var worldPos = setting.DesignTemplate.transform.position + designRot * setting.CreatePoint;
                    var worldRot = designRot * Quaternion.Euler(setting.CreateRotate);
                    createPoint = setting.objBase.transform.InverseTransformPoint(worldPos);
                    createRotate = (Quaternion.Inverse(setting.objBase.transform.rotation) * worldRot).eulerAngles;
                }
                else
                {
                    // 手入力オフセットは実寸(m・生成元ユニットの姿勢基準)。
                    // 親ローカルへ直接代入すると親のスケール(1/25.4等)が掛かって縮むため、
                    // ワールド位置を経由してスケールを打ち消す（削除位置の判定と同じ規約）
                    createPoint = setting.objBase.transform.InverseTransformPoint(
                        setting.objBase.transform.position + setting.objBase.transform.rotation * setting.CreatePoint);
                }
                var change = false;
                // 生成前にチェック
                var near = setting.objBase.transform.GetComponentsInChildren<ObjectScript>()
                    .ToList()
                    .Find(d => Vector2.Distance(
                        new Vector2(d.transform.localPosition.x, d.transform.localPosition.z),
                        new Vector2(createPoint.x, createPoint.z)
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
                    obj.transform.localPosition = createPoint;
                    obj.transform.localEulerAngles = createRotate;
                    obj.transform.localScale = Vector3.one;
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
        }
    }

    /// <summary>
    /// アタッチ処理（レベル動作）
    /// タグON中は範囲内のワークを自ユニットの子として保持し、OFFで解放する。
    /// </summary>
    /// <param name="setting"></param>
    /// <param name="stat"></param>
    private void ProcessAttach(MultiObjectInfo setting, bool stat)
    {
        if (stat)
        {
            // 範囲内のワークを取り込む（実位置・実姿勢のまま子化）
            var center = setting.objBase.transform.TransformPoint(setting.CreatePoint);
            foreach (var pool in works)
            {
                if ((setting.WorkName != null) && (setting.WorkName != "") && (pool.Key != setting.WorkName))
                {
                    continue;
                }
                foreach (var obj in pool.Value.activeObjects.ToList())
                {
                    if (obj == null)
                    {
                        continue;
                    }
                    if (obj.transform.parent == setting.objBase.transform)
                    {
                        continue;
                    }
                    if (Vector3.Distance(obj.transform.position, center) <= setting.AliveDistance)
                    {
                        obj.transform.SetParent(setting.objBase.transform, true);
                        var rigi = obj.GetComponentInChildren<Rigidbody>();
                        if (rigi != null)
                        {
                            rigi.useGravity = false;
                            rigi.isKinematic = true;
                        }
                        if (!setting.Attached.Contains(obj))
                        {
                            setting.Attached.Add(obj);
                        }
                    }
                }
            }
        }
        else if (setting.Attached.Count > 0)
        {
            // 解放（既に他所＝次工程などに掴まれているワークはそのまま）
            foreach (var obj in setting.Attached)
            {
                if (obj == null)
                {
                    continue;
                }
                if (obj.transform.parent != setting.objBase.transform)
                {
                    continue;
                }
                obj.transform.SetParent(null, true);
                var rigi = obj.GetComponentInChildren<Rigidbody>();
                if (rigi != null)
                {
                    rigi.useGravity = true;
                    rigi.isKinematic = false;
                }
            }
            setting.Attached.Clear();
        }
    }

    /// <summary>
    /// ワーク変換処理（タグ立ち上がりで実行）
    /// 範囲内の対象ワークを、実位置・実姿勢・親子関係・物理状態を引き継いで変換先ワークに置き換える。
    /// </summary>
    /// <param name="setting"></param>
    private void ProcessChange(MultiObjectInfo setting)
    {
        var toPool = GetOrCreatePool(setting.WorkTo);
        if (toPool == null)
        {
            return;
        }
        var center = setting.objBase.transform.TransformPoint(setting.CreatePoint);
        foreach (var pool in works.ToList())
        {
            if (pool.Key == setting.WorkTo)
            {
                // 変換先と同名ワークは対象外（自己置換防止）
                continue;
            }
            if ((setting.WorkName != null) && (setting.WorkName != "") && (pool.Key != setting.WorkName))
            {
                continue;
            }
            foreach (var old in pool.Value.activeObjects.ToList())
            {
                if (old == null)
                {
                    continue;
                }
                if (Vector3.Distance(old.transform.position, center) > setting.AliveDistance)
                {
                    continue;
                }
                // 新ワークを実位置・親を引き継いで生成
                var newObj = toPool.pool.Get();
                newObj.transform.SetParent(old.transform.parent, false);
                newObj.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation);
                newObj.transform.localScale = Vector3.one;
                var newScript = newObj.GetComponent<ObjectScript>();
                if (newScript == null)
                {
                    newScript = newObj.AddComponent<ObjectScript>();
                }
                var oldScript = old.GetComponent<ObjectScript>();
                if (oldScript != null)
                {
                    newScript.AliveDistance = oldScript.AliveDistance;
                    newScript.IsGrabbable = oldScript.IsGrabbable;
                    newScript.IsGravity = oldScript.IsGravity;
                    newScript.IsTouch = oldScript.IsTouch;
                }
                // 物理状態を引き継ぐ（保持中に変換された場合もそのまま保持される）
                var oldRigi = old.GetComponentInChildren<Rigidbody>();
                var newRigi = newObj.GetComponentInChildren<Rigidbody>();
                if ((oldRigi != null) && (newRigi != null))
                {
                    newRigi.useGravity = oldRigi.useGravity;
                    newRigi.isKinematic = oldRigi.isKinematic;
                }
                // アタッチ保持リストの参照も差し替える
                ReplaceAttached(old, newObj);
                pool.Value.pool.Release(old);
            }
        }
    }

    /// <summary>
    /// 全受渡設定のアタッチ保持リストの参照を差し替える（変換時）
    /// </summary>
    /// <param name="oldObj"></param>
    /// <param name="newObj"></param>
    private void ReplaceAttached(GameObject oldObj, GameObject newObj)
    {
        foreach (var setting in multiObjects)
        {
            foreach (var tag in setting.Value)
            {
                foreach (var transfer in tag.Value.transferSettings)
                {
                    var index = transfer.Attached.IndexOf(oldObj);
                    if (index >= 0)
                    {
                        transfer.Attached[index] = newObj;
                    }
                }
            }
        }
    }

    /// <summary>
    /// ワークをプールへ返却して削除する（プール管理外のワークはDestroy）。手動削除（Deleteキー）用。
    /// 子ノードが渡されてもワーク本体（ObjectScript）を辿って返却する。
    /// </summary>
    /// <param name="work"></param>
    public void ReleaseWork(GameObject work)
    {
        if (work == null)
        {
            return;
        }
        var objScript = work.GetComponentInParent<ObjectScript>();
        var root = objScript != null ? objScript.gameObject : work;
        if (works.ContainsKey(root.name))
        {
            if (works[root.name].activeObjects.Contains(root))
            {
                works[root.name].pool.Release(root);
            }
            // 返却済み（二重削除）の場合は何もしない。
            // ※ここでDestroyするとプール在庫のインスタンスが破壊され、以後のGetが壊れて生成不能になる
        }
        else
        {
            // プール名に該当しない（管理外の）ワークのみ破棄する
            Destroy(root);
        }
    }

    /// <summary>
    /// ワークプールを取得（未作成なら作成）
    /// </summary>
    /// <param name="workName"></param>
    /// <returns></returns>
    private WorkPool GetOrCreatePool(string workName)
    {
        if ((workName == null) || (workName == ""))
        {
            return null;
        }
        if (works.ContainsKey(workName))
        {
            return works[workName];
        }
        var pool = new WorkPool
        {
            work = GlobalScript.CreateWork(null, workName),
        };
        pool.work.name = workName;
        pool.pool = new ObjectPool<GameObject>(
            createFunc: () =>
            {
                var obj = Instantiate(pool.work);
                obj.name = workName;
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
        works.Add(workName, pool);
        return pool;
    }

    /// <summary>
    /// 出力先のObjectFactoryオブジェクトを取得（未作成なら作成）
    /// </summary>
    /// <param name="objFactoryObj"></param>
    /// <returns></returns>
    private GameObject GetObjectFactoryBase(GameObject objFactoryObj)
    {
        var objFactory = objFactoryObj.transform.GetComponentsInChildren<Transform>().ToList().Find(d => d.name == "ObjectFactory" && (d.parent == objFactoryObj.transform));
        var objBase = objFactory == null ? new GameObject("ObjectFactory") : objFactory.gameObject;
        objBase.transform.parent = objFactoryObj.transform;
        objBase.transform.localPosition = Vector3.zero;
        objBase.transform.localEulerAngles = Vector3.zero;
        objBase.transform.localScale = Vector3.one;
        return objBase;
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
            GetOrCreatePool(wk.work);
            // 出力先オブジェクト
            var objFactoryObj = backetInfo != null ? backetInfo.obj : (wk.ignoreMove ? unitSetting.unitObject : unitSetting.moveObject);
            var objBase = GetObjectFactoryBase(objFactoryObj);
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
            if (wk.isDesignPos && GlobalScript.workModels.TryGetValue(wk.work, out var template) && (template != null))
            {
                // 設計位置を使用：テンプレートを保持し、相対座標は生成タイミングで算出する
                setting.IsDesignPos = true;
                setting.DesignTemplate = template;
            }
            multiObject.createSettings.Add(setting);
        }
        else if (obj.GetType() == typeof(WorkTransferSetting))
        {
            var wk = (WorkTransferSetting)obj;
            // 変換先ワークのプールを準備（対象ワークは既存プールを参照するのみ）
            if (wk.mode == 1)
            {
                GetOrCreatePool(wk.workTo);
            }
            // 保持先（動作部）のObjectFactory
            var objFactoryObj = unitSetting.moveObject != null ? unitSetting.moveObject : unitSetting.unitObject;
            var objBase = GetObjectFactoryBase(objFactoryObj);
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
                Mode = wk.mode,
                WorkName = wk.work != null ? wk.work : "",
                WorkTo = wk.workTo != null ? wk.workTo : "",
                CreatePoint = new Vector3
                {
                    x = wk.pos[0],
                    y = wk.pos[1],
                    z = wk.pos[2]
                },
                AliveDistance = wk.range,
                objBase = objBase
            };
            multiObject.transferSettings.Add(setting);
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
                WorkName = wk.work,
                CreatePoint = new Vector3
                {
                    x = wk.pos[0],
                    y = wk.pos[1],
                    z = wk.pos[2]
                },
                AliveDistance = wk.distance,
                backetInfo = backetInfo,
                BacketNo = backetInfo != null ? wk.backetno : -1,
                objBase = backetInfo != null ? backetInfo.obj : unitSetting.unitObject,
                // バケット削除は経路上の固定点（AxisMotionBaseが算出）で判定する
                IsFixedDeletePos = (backetInfo != null) && wk.isFixedPos,
                FixedDeletePos = wk.fixedWorldPos
            };
            multiObject.deleteSettings.Add(setting);
            // 削除範囲の確認表示（Ctrl+Shift押下中のみ表示）を生成する
            CreateDeleteZone(setting, unitSetting, wk);
        }
    }

    /// <summary>
    /// ワーク削除範囲（削除位置中心・半径=範囲の球）を半透明で可視化するオブジェクトを生成する。
    /// 表示切替（Ctrl+Shift押下中のみ）はBacketPathOverlayが行う。再Setup時は同名の旧表示を作り直す。
    /// </summary>
    private void CreateDeleteZone(MultiObjectInfo setting, UnitSetting unitSetting, WorkDeleteSetting wk)
    {
        if (setting.backetInfo != null)
        {
            // バケットの削除はバケット番号で経路上の固定位置に発動するため、
            // 確認表示はAxisMotionBase側が固定位置（経路開始＋番号×ピッチ）に1個だけ生成する
            return;
        }
        if ((setting.AliveDistance <= 0f) || (setting.objBase == null))
        {
            Debug.Log($"[WorkDeleteZone] {unitSetting.name} 生成スキップ 範囲={setting.AliveDistance} objBase={(setting.objBase == null ? "null" : setting.objBase.name)}");
            return;
        }
        Debug.Log($"[WorkDeleteZone] {unitSetting.name} objBase={setting.objBase.name} スケール={setting.objBase.transform.lossyScale.x:F4} " +
            $"削除位置={setting.CreatePoint} 範囲={setting.AliveDistance}");
        var zoneName = $"WorkDeleteZone_{unitSetting.name}_{wk.tag}_{wk.pos[0]}_{wk.pos[1]}_{wk.pos[2]}";
        var old = setting.objBase.transform.Find(zoneName);
        if (old != null)
        {
            Destroy(old.gameObject);
        }
        var zone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        zone.name = zoneName;
        // 削除判定は距離比較なのでコライダは不要（ワークとの物理干渉を避けるため必ず除去する）
        var col = zone.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }
        zone.transform.SetParent(setting.objBase.transform, false);
        // 削除位置・範囲は実寸(m)。objBaseのスケール（バケットクローンは約1/25）を打ち消して実寸で表示する
        var ls = setting.objBase.transform.lossyScale;
        var inv = new Vector3(
            1f / Mathf.Max(Mathf.Abs(ls.x), 1e-6f),
            1f / Mathf.Max(Mathf.Abs(ls.y), 1e-6f),
            1f / Mathf.Max(Mathf.Abs(ls.z), 1e-6f));
        zone.transform.localPosition = Vector3.Scale(setting.CreatePoint, inv);
        zone.transform.localScale = Vector3.Scale(inv, Vector3.one * setting.AliveDistance * 2f);
        var rend = zone.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.sharedMaterial = SafetyZoneScript.MakeZoneMaterial(new Color(1f, 0.2f, 0.2f, 0.3f));
        }
        zone.SetActive(false);
        BacketPathOverlay.RegisterLine($"{zoneName}_{zone.GetInstanceID()}", zone);
    }
}
