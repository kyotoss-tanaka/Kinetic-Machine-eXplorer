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
        /// 変換先の配置オフセット(m)。変換元と変換先でモデル原点が違う場合の補正（変換モードのみ）
        /// </summary>
        public Vector3 ChangeOffset;

        /// <summary>
        /// 変換先の配置オフセット角度(度)（変換モードのみ）
        /// </summary>
        public Vector3 ChangeOffsetRotate;

        /// <summary>
        /// 変換元の基準オフセット(m)。前工程で作られたワークに灰色の変換元ゴーストを重ねるための補正で、
        /// 変換後の配置にも加算される（実配置＝ChangeFromOffset＋ChangeOffset）。
        /// </summary>
        public Vector3 ChangeFromOffset;

        /// <summary>
        /// 変換元の基準オフセット角度(度)
        /// </summary>
        public Vector3 ChangeFromOffsetRotate;

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

        /// <summary>
        /// コンベアユニットなら搬送面基準（最上流×天面×幅中央）で削除位置を解釈するための参照。
        /// ConveyorScript は削除設定の登録より後に付与されるため、初回判定時に遅延解決する
        /// </summary>
        public ConveyorScript conveyor;

        /// <summary>コンベア参照の解決済みフラグ（未装着でも毎回GetComponentしないため）</summary>
        public bool isConveyorResolved = false;

        /// <summary>削除範囲の確認表示（球）。基準が動的なコンベア用に毎フレーム追従させる</summary>
        public GameObject zoneObj;

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
    /// ワークをプールへ返却する（static版。プール管理外ならDestroy）。
    /// ワークの破棄は必ずここを通すこと：Destroyするとプールのアクティブリストにnullが残り、
    /// 長時間運転でリストが肥大化してフレームレートが劣化する
    /// </summary>
    public static void ReleaseWorkStatic(GameObject work)
    {
        if (instance != null)
        {
            instance.ReleaseWork(work);
        }
        else
        {
            Destroy(work);
        }
    }

    /// <summary>
    /// アクティブリストのnull要素（Destroy等でプールを経由せず消えたワーク）を定期的に掃除する
    /// </summary>
    private float nextPurgeTime;

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
            // アクティブワークを破棄する（バケット生成ワークはファクトリ配下でリロード後も残るため明示的に消す）
            foreach (var obj in work.Value.activeObjects)
            {
                if (obj != null)
                {
                    Destroy(obj);
                }
            }
            work.Value.activeObjects.Clear();
            work.Value.pool.Clear();
            work.Value.pool.Dispose();

        }
        works.Clear();
        // ドメインリロード（エディタの再コンパイル）でプール台帳が消えた後の残骸も掃除する：
        // バケット生成ワーク・プール在庫はファクトリ配下に親付けされるため、台帳に頼らず直接破棄する
        for (var i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (child.GetComponent<ObjectScript>() != null)
            {
                Destroy(child);
            }
        }
        // 所有権も全消去（リロード後に旧参照が残らないように）
        WorkOwnership.Clear();
    }

    // Update is called once per frame
    protected override void MyFixedUpdate()
    {
        // アクティブリストのnull要素を定期掃除する（プールを経由せず破棄されたワークの残骸。
        // 放置すると長時間運転で全ワーク走査のコストが際限なく増える）
        if (Time.time >= nextPurgeTime)
        {
            nextPurgeTime = Time.time + 5f;
            foreach (var pool in works)
            {
                pool.Value.activeObjects.RemoveAll(d => d == null);
            }
        }
        foreach (var setting in multiObjects)
        {
            foreach (var tag in setting.Value)
            {
                // 削除範囲の確認表示を基準へ追従させる。コンベアは搬送面基準（毎フレーム算出）のため
                // 親子付けだけでは追従できない。表示中のみなので非表示時のコストは無い
                foreach (var del in tag.Value.deleteSettings)
                {
                    if ((del.zoneObj != null) && del.zoneObj.activeSelf)
                    {
                        ApplyDeleteZonePose(del);
                    }
                }
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
                GetDeleteBase(setting, out var basePos, out var baseRot);
                var worldDelete = setting.IsFixedDeletePos
                    ? setting.FixedDeletePos
                    : basePos + baseRot * setting.CreatePoint;
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
                var work = works[setting.WorkName];
                var isBucket = setting.backetInfo != null;
                ObjectScript near = null;
                var bucketWorldPos = Vector3.zero;
                var bucketWorldRot = Quaternion.identity;
                var bucketBlocked = false;
                if (isBucket)
                {
                    // バケット生成: 爪の子にせず、ワールド配置＋経路搬送の論理紐づけを使う。
                    // 重複チェックは紐づけ済みワークの実位置で行う
                    bucketWorldPos = setting.objBase.transform.TransformPoint(createPoint);
                    bucketWorldRot = setting.objBase.transform.rotation * Quaternion.Euler(createRotate);
                    var bu = setting.backetInfo.unit;
                    bucketBlocked = (bu != null) && bu.HasBoundWorkNear(setting.backetInfo, bucketWorldPos);
                }
                else
                {
                    // 生成前にチェック
                    near = setting.objBase.transform.GetComponentsInChildren<ObjectScript>()
                        .ToList()
                        .Find(d => Vector2.Distance(
                            new Vector2(d.transform.localPosition.x, d.transform.localPosition.z),
                            new Vector2(createPoint.x, createPoint.z)
                        ) < 0.001f);
                }
                if (near != null)
                {
                    if (setting.IsChange)
                    {
                        if (!near.name.Contains(work.work.name) && setting.IsChange)
                        {
                            // Destroyでなくプールへ返却する（アクティブリストにnullを残さない）
                            ReleaseWork(near.gameObject);
                            change = true;
                        }
                    }
                }
                if (!bucketBlocked && ((setting.IsChange && change) || (!setting.IsChange && (near == null))))
                {
                    var obj = work.pool.Get();
                    if (isBucket)
                    {
                        // ワールド直接配置（親はファクトリ＝静止。スケールは従来の「爪の子」と同じ見た目に合わせる）
                        obj.transform.parent = transform;
                        obj.transform.position = bucketWorldPos;
                        obj.transform.rotation = bucketWorldRot;
                        obj.transform.localScale = setting.objBase.transform.lossyScale;
                        if (setting.backetInfo.unit != null)
                        {
                            if (setting.backetInfo.unit.UsesPusherFeed)
                            {
                                // プッシャー搬送方式: 生成位置で静止させ、爪の押し面が届いたら押される
                                setting.backetInfo.unit.RegisterFreeWork(obj);
                            }
                            else
                            {
                                // 爪への論理紐づけ（経路の角度に沿って追従。搬送区間を抜けたら手放す）
                                setting.backetInfo.unit.BindWorkToBacket(obj, setting.backetInfo);
                            }
                        }
                    }
                    else
                    {
                        obj.transform.parent = setting.objBase.transform;
                        obj.transform.localPosition = createPoint;
                        obj.transform.localEulerAngles = createRotate;
                        obj.transform.localScale = Vector3.one;
                    }
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
            // 中心オフセットは実寸(m)。TransformPointだと親のスケール(1/25.4等)が掛かって縮むため、
            // 生成・削除の判定と同じ「ワールド位置＋姿勢回転」で求める
            var center = setting.objBase.transform.position
                + setting.objBase.transform.rotation * setting.CreatePoint;
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
        // 中心オフセットは実寸(m)。TransformPointだと親のスケール(1/25.4等)が掛かって縮むため、
        // 生成・削除の判定と同じ「ワールド位置＋姿勢回転」で求める
        var center = setting.objBase.transform.position
            + setting.objBase.transform.rotation * setting.CreatePoint;
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
                // 新ワークを実位置・親を引き継いで生成。
                // 変換元と変換先でモデル原点が違う場合はオフセットで補正する
                // （オフセットは変換元ワークの姿勢基準・実寸m。0なら原点一致＝従来動作）
                var newObj = toPool.pool.Get();
                newObj.transform.SetParent(old.transform.parent, false);
                // 実配置に効くのは変換先オフセットのみ。変換元オフセットはKMX上の表示合わせ専用で
                // 保存されないため、挙動には影響させない（→ ChangeFromOffset の説明を参照）
                var newRot = old.transform.rotation * Quaternion.Euler(setting.ChangeOffsetRotate);
                var newPos = old.transform.position + old.transform.rotation * setting.ChangeOffset;
                newObj.transform.SetPositionAndRotation(newPos, newRot);
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
                // 搬送記憶（紐づけ/自由ワーク登録/所有権）を消す。プールで使い回すため前の人生を残さない
                AxisMotionBase.ForgetWorkStatic(obj);
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
    /// <summary>
    /// 削除位置オフセットの基準（原点と姿勢）を求める。機構の種類で基準が違う。
    ///  ・コンベア  = 搬送面の最上流×天面×幅中央（X=横/Y=上/Z=流れ）。面や領域を変えても追従する
    ///  ・通常機構  = 動作部（取出し等の動きに追従する）
    ///  ・バケット  = 経路上の固定点（呼び出し側の IsFixedDeletePos で処理するためここは通らない）
    /// </summary>
    private static void GetDeleteBase(MultiObjectInfo setting, out Vector3 pos, out Quaternion rot)
    {
        if (!setting.isConveyorResolved)
        {
            // ConveyorScriptは削除設定の登録より後に付与されるため、初回判定時に解決する
            setting.isConveyorResolved = true;
            setting.conveyor = (setting.objBase != null)
                ? setting.objBase.GetComponent<ConveyorScript>()
                : null;
        }
        if ((setting.conveyor != null) && setting.conveyor.TryGetSurfaceOrigin(out pos, out rot))
        {
            return;
        }
        pos = setting.objBase.transform.position;
        rot = setting.objBase.transform.rotation;
    }

    /// <summary>
    /// JSONの座標リストをVector3へ変換する。後から追加した任意項目は旧JSONに存在しないため、
    /// null・要素不足はゼロ扱いにしてロードを壊さない。
    /// </summary>
    private static Vector3 ToVector3(List<float> values)
    {
        if ((values == null) || (values.Count < 3))
        {
            return Vector3.zero;
        }
        return new Vector3(values[0], values[1], values[2]);
    }

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
            // 生成位置の確認表示（Ctrl+Shift押下中のみ表示）を生成する
            CreateCreateGhost(setting, unitSetting, wk);
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
                // オフセットは後から追加した任意項目。旧JSONでは欠落するためnull/要素不足を許容する
                ChangeOffset = ToVector3(wk.offset),
                ChangeOffsetRotate = ToVector3(wk.offsetRot),
                ChangeFromOffset = ToVector3(wk.fromOffset),
                ChangeFromOffsetRotate = ToVector3(wk.fromOffsetRot),
                objBase = objBase
            };
            multiObject.transferSettings.Add(setting);
            if (wk.mode == 1)
            {
                // 変換範囲の確認表示（Ctrl+Shift押下中のみ表示）を生成する
                CreateChangeZone(setting, unitSetting, wk);
            }
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
                // 通常機構の削除位置は動作部基準（生成・変換と同じ）。ユニット根本を基準にすると
                // 取出し等が動いても削除位置が固定されたままになる。
                // バケットは経路上の固定点で判定するため爪オブジェクトを基準にする
                objBase = backetInfo != null
                    ? backetInfo.obj
                    : (unitSetting.moveObject != null ? unitSetting.moveObject : unitSetting.unitObject),
                // バケット削除は経路上の固定点（AxisMotionBaseが算出）で判定する
                IsFixedDeletePos = (backetInfo != null) && wk.isFixedPos,
                FixedDeletePos = wk.fixedWorldPos
            };
            multiObject.deleteSettings.Add(setting);
            // 削除範囲の確認表示（Ctrl+Shift押下中のみ表示）を生成する
            CreateDeleteZone(setting, unitSetting, wk);
        }
    }

    #region ワーク操作の確認表示（Ctrl+Shift押下中のみ）
    /// <summary>削除範囲の色。KMXToolのワーク図と一致させる（IndianRed）</summary>
    private static readonly Color ZoneColorDelete = new Color(205f / 255f, 92f / 255f, 92f / 255f, 0.3f);

    /// <summary>変換範囲の色。KMXToolのワーク図と一致させる（DarkOrange）</summary>
    private static readonly Color ZoneColorChange = new Color(255f / 255f, 140f / 255f, 0f, 0.3f);

    /// <summary>生成位置ゴーストの色。KMXToolのワーク図と一致させる（SeaGreen）</summary>
    private static readonly Color ZoneColorCreate = new Color(46f / 255f, 139f / 255f, 87f / 255f, 0.35f);

    /// <summary>変換元ゴーストの色。変換先（オレンジ）と対比させる中立色（KMXToolの無効カードと同じグレー）</summary>
    private static readonly Color ZoneColorChangeFrom = new Color(0.62f, 0.62f, 0.62f, 0.3f);

    /// <summary>
    /// 確認表示用の半透明球を1個生成する。
    /// 表示切替（Ctrl+Shift押下中のみ）はBacketPathOverlayが行う。再Setup時は同名の旧表示を作り直す。
    /// </summary>
    /// <param name="zoneName">オブジェクト名（再Setup時の作り直し判定に使う）</param>
    /// <param name="parent">親。この配下のローカル座標へ配置する</param>
    /// <param name="pointM">親の姿勢基準での中心オフセット（実寸m）</param>
    /// <param name="diameterM">直径（実寸m）</param>
    /// <param name="color">表示色</param>
    private GameObject CreateZoneSphere(string zoneName, Transform parent, Vector3 pointM, float diameterM, Color color)
    {
        if ((parent == null) || (diameterM <= 0f))
        {
            return null;
        }
        var old = parent.Find(zoneName);
        if (old != null)
        {
            Destroy(old.gameObject);
        }
        var zone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        zone.name = zoneName;
        // 判定は距離比較なのでコライダは不要（ワークとの物理干渉を避けるため必ず除去する）
        var col = zone.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }
        zone.transform.SetParent(parent, false);
        // 位置・範囲は実寸(m)。親のスケール（バケットクローンは約1/25）を打ち消して実寸で表示する
        var ls = parent.lossyScale;
        var inv = new Vector3(
            1f / Mathf.Max(Mathf.Abs(ls.x), 1e-6f),
            1f / Mathf.Max(Mathf.Abs(ls.y), 1e-6f),
            1f / Mathf.Max(Mathf.Abs(ls.z), 1e-6f));
        zone.transform.localPosition = Vector3.Scale(pointM, inv);
        zone.transform.localScale = Vector3.Scale(inv, Vector3.one * diameterM);
        var rend = zone.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.sharedMaterial = SafetyZoneScript.MakeZoneMaterial(color);
        }
        zone.SetActive(false);
        BacketPathOverlay.RegisterLine($"{zoneName}_{zone.GetInstanceID()}", zone);
        return zone;
    }

    /// <summary>
    /// ワーク削除範囲（削除位置中心・半径=範囲の球）を半透明で可視化する。
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
            return;
        }
        var zoneName = $"WorkDeleteZone_{unitSetting.name}_{wk.tag}_{wk.pos[0]}_{wk.pos[1]}_{wk.pos[2]}";
        setting.zoneObj = CreateZoneSphere(zoneName, setting.objBase.transform, setting.CreatePoint,
            setting.AliveDistance * 2f, ZoneColorDelete);
        if (setting.zoneObj != null)
        {
            // 球だけでは基準の向きと選択状態が分からないため、ゴーストと同じ原点軸を付ける
            AddOriginAxes(setting.zoneObj, 0.05f);
        }
        // F9パネルからの調整対象に登録（削除は位置のみ。角度設定を持たない）
        WorkAdjustPanel.Register(new WorkAdjustPanel.Target
        {
            label = $"{unitSetting.name} / {wk.work} / 削除位置",
            getPos = () => setting.CreatePoint,
            setPos = v => setting.CreatePoint = v,
            apply = () => ApplyDeleteZonePose(setting),
            hitObject = setting.zoneObj,
        });
    }

    /// <summary>
    /// 削除範囲の確認表示を現在の基準（コンベア＝搬送面／通常＝動作部）へ合わせる
    /// </summary>
    private static void ApplyDeleteZonePose(MultiObjectInfo setting)
    {
        if ((setting.zoneObj == null) || setting.IsFixedDeletePos)
        {
            return;
        }
        GetDeleteBase(setting, out var basePos, out var baseRot);
        setting.zoneObj.transform.SetPositionAndRotation(basePos + baseRot * setting.CreatePoint, baseRot);
    }

    /// <summary>
    /// ワーク形状の半透明ゴーストを1個生成する（生成位置=緑／変換先=オレンジ）。
    /// 実ワークは objBase 配下で localScale=1 のため、ワールド倍率をそれに合わせる。
    /// </summary>
    /// <param name="zoneName">オブジェクト名（再Setup時の作り直し判定に使う）</param>
    /// <param name="workName">表示するワーク名（プールの原型を複製する）</param>
    /// <param name="parent">吊り先。非アクティブな親だと表示できないため必ずアクティブなものを渡す</param>
    /// <param name="worldPos">配置位置（ワールド）</param>
    /// <param name="worldRot">配置姿勢（ワールド）</param>
    /// <param name="objBase">実ワークの親。ワールド倍率の基準に使う</param>
    /// <param name="color">表示色</param>
    private GameObject CreateWorkGhost(string zoneName, string workName, Transform parent,
        Vector3 worldPos, Quaternion worldRot, Transform objBase, Color color)
    {
        if ((parent == null) || (objBase == null)
            || !works.TryGetValue(workName, out var pool) || (pool.work == null))
        {
            Debug.Log($"[WorkZone] ゴースト作成不可 {zoneName} work={workName} プール有無={works.ContainsKey(workName)}");
            return null;
        }
        var old = parent.Find(zoneName);
        if (old != null)
        {
            Destroy(old.gameObject);
        }
        var ghost = Instantiate(pool.work);
        ghost.name = zoneName;
        ghost.transform.SetParent(parent, false);
        // 実ワークは objBase 配下で localScale=1 → ワールド倍率 = objBase.lossyScale。親が違うぶんを割り戻す
        var pls = parent.lossyScale;
        var target = objBase.lossyScale;
        ghost.transform.localScale = new Vector3(
            target.x / Mathf.Max(Mathf.Abs(pls.x), 1e-6f),
            target.y / Mathf.Max(Mathf.Abs(pls.y), 1e-6f),
            target.z / Mathf.Max(Mathf.Abs(pls.z), 1e-6f));
        ghost.transform.SetPositionAndRotation(worldPos, worldRot);
        // 表示専用にする（物理・ロジックへ一切参加させない）
        foreach (var mb in ghost.GetComponentsInChildren<MonoBehaviour>(true))
        {
            Destroy(mb);
        }
        foreach (var rb in ghost.GetComponentsInChildren<Rigidbody>(true))
        {
            Destroy(rb);
        }
        foreach (var c in ghost.GetComponentsInChildren<Collider>(true))
        {
            Destroy(c);
        }
        var mat = SafetyZoneScript.MakeZoneMaterial(color);
        var rends = ghost.GetComponentsInChildren<Renderer>(true);
        foreach (var rend in rends)
        {
            // 元モデルは非表示化されていることがある。ルートだけ有効化しても子が無効なら描画されないため、
            // 描画ノードまでの経路を全て有効化し、Renderer自体も有効に戻す
            for (var t = rend.transform; (t != null) && (t != ghost.transform); t = t.parent)
            {
                if (!t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(true);
                }
            }
            rend.enabled = true;
            var mats = new Material[rend.sharedMaterials.Length];
            for (var i = 0; i < mats.Length; i++)
            {
                mats[i] = mat;
            }
            rend.sharedMaterials = mats;
        }
        var size = (rends.Length > 0) ? rends[0].bounds.size : Vector3.zero;
        // モデル原点の位置・姿勢が分かるようXYZ軸を付ける（マテリアル差し替えの後に追加する）
        AddOriginAxes(ghost, 0.05f);
        ghost.SetActive(false);
        BacketPathOverlay.RegisterLine($"{zoneName}_{ghost.GetInstanceID()}", ghost);
        Debug.Log($"[WorkZone] {zoneName} 親={parent.name} 親有効={parent.gameObject.activeInHierarchy}"
            + $" 位置={worldPos:F3} 描画数={rends.Length} 外形={size:F3}m"
            + $" 倍率(ghost/parent/objBase)={ghost.transform.lossyScale:F4}/{pls:F4}/{target:F4}");
        return ghost;
    }

    /// <summary>
    /// ワーク変換の確認表示。判定範囲（オレンジの球）と、変換先ワークの形状（オレンジ半透明）を出す。
    /// 判定中心・半径はProcessChangeと同一（objBase基準のCreatePoint／AliveDistance）。
    /// </summary>
    private void CreateChangeZone(MultiObjectInfo setting, UnitSetting unitSetting, WorkTransferSetting wk)
    {
        if ((setting.AliveDistance <= 0f) || (setting.objBase == null))
        {
            Debug.Log($"[WorkZone] 変換表示スキップ {unitSetting.name}/{wk.work} range={setting.AliveDistance} objBase={(setting.objBase == null ? "null" : setting.objBase.name)}");
            return;
        }
        var baseTr = setting.objBase.transform;
        // 判定範囲（球）
        CreateZoneSphere($"WorkChangeZone_{unitSetting.name}_{wk.tag}_{wk.work}",
            baseTr, setting.CreatePoint, setting.AliveDistance * 2f, ZoneColorChange);
        // 変換元ワークの形状（グレー）。変換元オフセットを効かせて前工程のワークに重ねる基準にする
        var fromGhost = CreateWorkGhost($"WorkChangeFromGhost_{unitSetting.name}_{wk.tag}_{wk.work}",
            setting.WorkName, baseTr, FromGhostPos(setting, baseTr), FromGhostRot(setting, baseTr),
            baseTr, ZoneColorChangeFrom);
        // 変換先ワークの形状（何に変わるかを示す）。変換元＋変換先オフセットの位置＝実際の配置と一致する
        var toGhost = CreateWorkGhost($"WorkChangeGhost_{unitSetting.name}_{wk.tag}_{wk.workTo}",
            setting.WorkTo, baseTr, ToGhostPos(setting, baseTr), ToGhostRot(setting, baseTr),
            baseTr, ZoneColorChange);
        // 両ゴーストを現在値で再配置する（変換元を動かすと変換先も連動する）
        Action apply = () =>
        {
            if (fromGhost != null)
            {
                fromGhost.transform.SetPositionAndRotation(FromGhostPos(setting, baseTr), FromGhostRot(setting, baseTr));
            }
            if (toGhost != null)
            {
                toGhost.transform.SetPositionAndRotation(ToGhostPos(setting, baseTr), ToGhostRot(setting, baseTr));
            }
        };
        // F9パネルからの調整対象に登録（実行中に見ながらオフセットを決めるため）
        WorkAdjustPanel.Register(new WorkAdjustPanel.Target
        {
            label = $"{unitSetting.name} / {wk.work} / 変換元オフセット（変換先にも加算）",
            getPos = () => setting.ChangeFromOffset,
            setPos = v => setting.ChangeFromOffset = v,
            getRot = () => setting.ChangeFromOffsetRotate,
            setRot = v => setting.ChangeFromOffsetRotate = v,
            apply = apply,
            hitObject = fromGhost,
        });
        WorkAdjustPanel.Register(new WorkAdjustPanel.Target
        {
            label = $"{unitSetting.name} / {wk.workTo} / 変換先オフセット",
            getPos = () => setting.ChangeOffset,
            setPos = v => setting.ChangeOffset = v,
            getRot = () => setting.ChangeOffsetRotate,
            setRot = v => setting.ChangeOffsetRotate = v,
            apply = apply,
            hitObject = toGhost,
        });
    }

    /// <summary>
    /// ゴーストの原点にXYZ軸マーカー（X=赤/Y=緑/Z=青）を付ける。
    /// 変換元と変換先でモデル原点がどこにあるか分かりにくいため、原点と姿勢を目視できるようにする。
    /// ゴーストの子なので位置・姿勢はそのまま追従する。
    /// </summary>
    /// <param name="ghost">対象ゴースト</param>
    /// <param name="lengthM">軸の長さ（ワールド実寸m）</param>
    private static void AddOriginAxes(GameObject ghost, float lengthM)
    {
        var root = new GameObject("OriginAxes");
        root.transform.SetParent(ghost.transform, false);
        // LineRendererの座標はローカル（親スケールが掛かる）。ゴーストは実ワークと同倍率（約1/25）のため
        // 打ち消して軸長をワールド実寸にする（線幅はスケール非適用なので補正不要）
        var ls = ghost.transform.lossyScale;
        root.transform.localScale = new Vector3(
            1f / Mathf.Max(Mathf.Abs(ls.x), 1e-6f),
            1f / Mathf.Max(Mathf.Abs(ls.y), 1e-6f),
            1f / Mathf.Max(Mathf.Abs(ls.z), 1e-6f));
        AddAxisLine(root.transform, Vector3.right * lengthM, Color.red);
        AddAxisLine(root.transform, Vector3.up * lengthM, Color.green);
        AddAxisLine(root.transform, Vector3.forward * lengthM, Color.blue);
    }

    /// <summary>原点マーカーの軸1本を作る</summary>
    private static void AddAxisLine(Transform parent, Vector3 to, Color color)
    {
        var go = new GameObject("Axis");
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.positionCount = 2;
        lr.SetPosition(0, Vector3.zero);
        lr.SetPosition(1, to);
        lr.widthMultiplier = 0.003f;
        lr.numCornerVertices = 0;
        lr.numCapVertices = 0;
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null)
        {
            sh = Shader.Find("Sprites/Default");
        }
        if (sh != null)
        {
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }
            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", color);
            }
            lr.sharedMaterial = mat;
        }
        lr.startColor = color;
        lr.endColor = color;
    }

    /// <summary>変換元ゴーストの表示位置（判定中心＋変換元オフセット）</summary>
    private static Vector3 FromGhostPos(MultiObjectInfo setting, Transform baseTr)
    {
        return baseTr.position + baseTr.rotation * (setting.CreatePoint + setting.ChangeFromOffset);
    }

    /// <summary>変換元ゴーストの表示姿勢</summary>
    private static Quaternion FromGhostRot(MultiObjectInfo setting, Transform baseTr)
    {
        return baseTr.rotation * Quaternion.Euler(setting.ChangeFromOffsetRotate);
    }

    /// <summary>変換先ゴーストの表示位置（判定中心＋変換元表示合わせ＋変換先オフセット）。
    /// 変換元を実ワークへ重ねてあれば、この位置が実際の着地点と一致する</summary>
    private static Vector3 ToGhostPos(MultiObjectInfo setting, Transform baseTr)
    {
        return baseTr.position
            + baseTr.rotation * (setting.CreatePoint + setting.ChangeFromOffset + setting.ChangeOffset);
    }

    /// <summary>変換先ゴーストの表示姿勢</summary>
    private static Quaternion ToGhostRot(MultiObjectInfo setting, Transform baseTr)
    {
        return baseTr.rotation * Quaternion.Euler(setting.ChangeFromOffsetRotate + setting.ChangeOffsetRotate);
    }

    /// <summary>
    /// ワーク生成位置に、生成されるワークそのものを半透明（緑）で表示する。
    /// 姿勢はUpdateObjectの生成座標算出と同じ式で求める。
    /// </summary>
    private void CreateCreateGhost(MultiObjectInfo setting, UnitSetting unitSetting, WorkCreateSetting wk)
    {
        if (setting.backetInfo != null)
        {
            // バケット生成は経路上へ配置されるため、経路表示（AxisMotionBase）側で確認する
            Debug.Log($"[WorkZone] 生成表示スキップ(バケット) {unitSetting.name}/{wk.work}");
            return;
        }
        if (setting.objBase == null)
        {
            Debug.Log($"[WorkZone] 生成表示スキップ {unitSetting.name}/{wk.work} objBase=null");
            return;
        }
        var useDesign = setting.IsDesignPos && (setting.DesignTemplate != null);
        // 親の選び方:
        //  ・設計位置指定 → 生成位置はワールド固定（設計モデルの位置）。テンプレート自体は非表示化されて
        //    いることがあり、その配下だとSetActive(true)しても activeInHierarchy が false で見えないため、
        //    常にアクティブなファクトリ直下に吊って姿勢だけ合わせる。
        //  ・手入力オフセット → 生成元ユニット基準なので objBase 配下に吊り、ユニットの動きに追従させる。
        var parent = useDesign ? transform : setting.objBase.transform;
        var baseRot = useDesign ? setting.DesignTemplate.transform.rotation : setting.objBase.transform.rotation;
        var basePos = useDesign ? setting.DesignTemplate.transform.position : setting.objBase.transform.position;
        var worldPos = basePos + baseRot * setting.CreatePoint;
        var worldRot = baseRot * Quaternion.Euler(setting.CreateRotate);
        var ghost = CreateWorkGhost($"WorkCreateGhost_{unitSetting.name}_{wk.tag}_{wk.work}",
            setting.WorkName, parent, worldPos, worldRot, setting.objBase.transform, ZoneColorCreate);
        // F9パネルからの調整対象に登録（実行中に見ながら生成位置を決めるため）。
        // 設計位置指定でもX/Y/Z・RX/RY/RZは「設計位置からの相対オフセット」として効くため対象に含める
        // （UpdateObjectの生成座標算出と同じ扱い）
        var baseTr = useDesign ? setting.DesignTemplate.transform : setting.objBase.transform;
        WorkAdjustPanel.Register(new WorkAdjustPanel.Target
        {
            label = $"{unitSetting.name} / {wk.work} / 生成位置",
            getPos = () => setting.CreatePoint,
            setPos = v => setting.CreatePoint = v,
            getRot = () => setting.CreateRotate,
            setRot = v => setting.CreateRotate = v,
            hitObject = ghost,
            apply = () =>
            {
                if (ghost != null)
                {
                    ghost.transform.SetPositionAndRotation(
                        baseTr.position + baseTr.rotation * setting.CreatePoint,
                        baseTr.rotation * Quaternion.Euler(setting.CreateRotate));
                }
            },
        });
    }
    #endregion ワーク操作の確認表示（Ctrl+Shift押下中のみ）
}
