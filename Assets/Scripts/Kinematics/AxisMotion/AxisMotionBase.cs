using Parameters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;
using System.Reflection;
using UnityEngine.UI;
//using static OVRPlugin;
using Pipelines.Sockets.Unofficial.Arenas;
using MongoDB.Driver;

public class AxisMotionBase : KinematicsBase
{
    /// <summary>
    /// バケット情報
    /// </summary>
    public class BacketInfo
    {
        public GameObject obj;
        public float offset;
        public int backetno = -1;
    }

    /// <summary>
    /// 頂点情報
    /// </summary>
    public class VerticeInfo
    {
        public int id;
        public int meshId;
        public Vector3 vertice;
        public Vector3 normal;
    }

    /// <summary>
    /// 定数
    /// </summary>
    protected const float Thousand = 1000f;
    protected const float Million = 1000000f;

    /// <summary>
    /// チャックユニット設定
    /// </summary>
    protected ChuckUnitSetting chuckSetting;

    /// <summary>
    /// リニア設定
    /// </summary>
    protected LinearSetting linearSetting;

    /// <summary>
    /// 動作対象オブジェクト
    /// </summary>
    protected GameObject moveObject;

    /// <summary>
    /// 動作対象のチャックオブジェクト
    /// </summary>
    protected List<GameObject> chuckObjects = new List<GameObject>();

    /// <summary>
    /// 動作方向
    /// </summary>
    protected Vector3 moveDir;

    /// <summary>
    /// 動作用
    /// </summary>
    protected Rigidbody rb;

    /// <summary>
    /// サイクルタグ
    /// </summary>
    protected TagInfo cycleTag;

    /// <summary>
    /// 拡張機構スクリプト
    /// </summary>
    public ExMechScript exScript;

    /// <summary>
    /// 拡張機構モード変更
    /// </summary>
    protected bool exModeChange;

    #region バケット関連
    /// <summary>
    /// ループライン
    /// </summary>
    protected List<Vector3> loopPathPoints = new List<Vector3>();

    /// <summary>
    /// バケット情報
    /// </summary>
    private List<BacketInfo> backets = new List<BacketInfo>();

    /// <summary>
    /// バケット中心
    /// </summary>
    private Vector3 backetCenter;

    /// <summary>
    /// バケット動作方向
    /// </summary>
    private Vector3 backetDir;

    /// <summary>
    /// バケット経路のローカル単位→ワールド(m)換算スケール
    /// </summary>
    private float backetScale = 1f;

    /// <summary>
    /// 設計位置（元モデル位置）に最も近い経路点での姿勢（バケットの向きの基準）
    /// </summary>
    private Quaternion backetBaseRot = Quaternion.identity;

    /// <summary>
    /// 設計位置に最も近い経路点（moveObjectローカル）。モデル原点と経路の取付オフセットの基準
    /// </summary>
    private Vector3 backetBasePos = Vector3.zero;

    /// <summary>
    /// 幾何経路の全長（moveObjectローカル単位）。周長指定時の名目距離→経路距離の換算に使用
    /// </summary>
    private float backetPathLength;

    /// <summary>
    /// バケット長
    /// </summary>
    private float backetPitch;

    /// <summary>
    /// バケットオフセット
    /// </summary>
    private float backetOffset;

    /// <summary>
    /// バケットループ長
    /// </summary>
    private float backetLength;

    /// <summary>
    /// バケット位置
    /// </summary>
    private float backetPos;

    /// <summary>
    /// バケット動作カウンタ
    /// </summary>
    private int backetCounter;

    /// <summary>
    /// 駆動値リセット（1サイクルで0に戻る軸）で確定した累積移動距離(mm)
    /// </summary>
    private float backetAccum;

    /// <summary>
    /// 最大バケット数
    /// </summary>
    private int backetCountMax;

    /// <summary>
    /// バケット動作方向
    /// </summary>
    private bool isBacketMoveRvs;

    /// <summary>
    /// バケット逆転
    /// </summary>
    private bool isBacketRvs;

    /// <summary>
    /// バケット表示設定
    /// </summary>
    private bool backetVisible;
    #endregion バケット関連
    /// <summary>
    /// 動作あり
    /// </summary>
    public bool isAction
    {
        get
        {
            return (unitSetting != null) && (unitSetting.actionSetting != null);
        }
    }

    /// <summary>
    /// オブジェクト形状あり
    /// </summary>
    public bool isShape
    {
        get
        {
            return (unitSetting != null) && (unitSetting.shapeSetting != null);
        }
    }

    /// <summary>
    /// 衝突あり(collision==1)ユニットか。形状設定ユニットは ShapeScript が担うため除く。
    /// ROS2 障害物として送るため、CreateBoxCollider がこの配下のコライダーを実体化(isTrigger=false)する。
    /// </summary>
    public bool IsCollisionUnit
    {
        get
        {
            return (unitSetting != null) && unitSetting.isCollision && !isShape;
        }
    }

    /// <summary>
    /// 吸引あり
    /// </summary>
    public bool isSuction
    {
        get
        {
            return (unitSetting != null) && (unitSetting.suctionSetting != null);
        }
    }

    /// <summary>
    /// ワーク生成あり
    /// </summary>
    public bool isWorkCreate
    {
        get
        {
            return (unitSetting != null) && (unitSetting.workSettings.Count > 0);
        }
    }

    /// <summary>
    /// ワーク削除あり
    /// </summary>
    public bool isWorkDelete
    {
        get
        {
            return (unitSetting != null) && (unitSetting.workDeleteSettings.Count > 0);
        }
    }

    /// <summary>
    /// スイッチ
    /// </summary>
    public bool isSwitch
    {
        get
        {
            return (unitSetting != null) && (unitSetting.switchSetting != null);
        }
    }

    /// <summary>
    /// シグナルタワー
    /// </summary>
    public bool isSignalTower
    {
        get
        {
            return (unitSetting != null) && (unitSetting.towerSetting != null);
        }
    }

    /// <summary>
    /// LED
    /// </summary>
    public bool isLed
    {
        get
        {
            return (unitSetting != null) && (unitSetting.ledSetting != null);
        }
    }

    /// <summary>
    /// 機構拡張設定
    /// </summary>
    public bool isExMech
    {
        get
        {
            return (unitSetting != null) && (unitSetting.exMechSetting != null) && (unitSetting.exMechSetting.datas.Count > 0);
        }
    }

    /// <summary>
    /// バケット設定
    /// </summary>
    public bool isBacket
    {
        get
        {
            return (unitSetting != null) && (unitSetting.backetSetting != null) &&
                   ((unitSetting.backetSetting.gameObject != null) ||
                    ((unitSetting.backetSetting.pathElements != null) && (unitSetting.backetSetting.pathElements.Count >= 2)));
        }
    }

    /// <summary>
    /// 回転動作
    /// </summary>
    public bool isRotate
    {
        get
        {
            return exModeChange ? (unitSetting.actionSetting.mode == 2 || unitSetting.actionSetting.mode == 4) : (unitSetting.actionSetting.mode == 1 || unitSetting.actionSetting.mode == 3);
        }
    }

    protected override void Start()
    {
        base.Start();
        if (unitSetting != null)
        {
            // ユニット情報更新
            RenewMoveDir();

            /*
            // 動作用Rigitbodyセット
            rb = unitSetting.moveObject.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = unitSetting.moveObject.transform.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
            */
        }
    }

    /// <summary>
    /// パラメータロードスクリプトからの情報に基づきモデル再構築
    /// </summary>
    protected virtual void PreModelRestruct()
    {
        // ユニット名のオブジェクト作成
        var unit = unitSetting.unitObject;
        // 親子関係作成
        unit.transform.parent = moveObject.transform.parent;
        unit.transform.localPosition = moveObject.transform.localPosition;
        unit.transform.localEulerAngles = moveObject.transform.localEulerAngles;
        moveObject.transform.parent = unit.transform;
        moveObject.transform.localPosition = new Vector3(0, 0, 0);
        moveObject.transform.localEulerAngles = new Vector3(0, 0, 0);
        // 子オブジェクトを
        foreach (var child in unitSetting.childrenObject)
        {
            // 子オブジェクト移動
            child.transform.parent = moveObject.transform;
            // 子オブジェクトのチャックユニットも移動する必要がある
            var motion = child.GetComponent<AxisMotionBase>();
            if (motion != null)
            {
                motion.SetChuckParent();
                // スイッチとシグナルタワーの座標は親からのオフセットに変更（プレハブ生成デバイスのみ）。
                // ※既存モデル流用のモデルスイッチ（SwitchMain 無し・group 設定）は、モデルが既に正しい位置に
                //   置かれているため補正しない（補正すると親位置ぶん二重にずれる）。
                bool isSwitchDev = motion.moveObject.GetComponent<SwitchScript>() != null;
                bool isTowerDev = motion.moveObject.GetComponent<SignalTowerScript>() != null;
                if (isSwitchDev || isTowerDev)
                {
                    bool isModelSwitch = false;
                    if (isSwitchDev)
                    {
                        isModelSwitch = true;
                        foreach (var t in motion.moveObject.GetComponentsInChildren<Transform>(true))
                        {
                            if (t.name == "SwitchMain") { isModelSwitch = false; break; }
                        }
                    }
                    if (!isModelSwitch)
                    {
                        child.transform.localPosition += unit.transform.localPosition;
                        child.transform.localEulerAngles += unit.transform.localEulerAngles;
                    }
                }
            }
        }
        // チャックオブジェクト設定
        if (chuckSetting != null)
        {
            // オブジェクト取得
            var objectFactory = GameObject.FindObjectsByType<MultiObjectFactoryScript>(FindObjectsSortMode.None)[0];
            foreach (var chuck in chuckSetting.children)
            {
                // 一旦ユニットの親子関係を生成
                if (chuck.setting.moveObject != null)
                {
                    chuck.setting.unitObject.transform.parent = chuck.setting.moveObject.transform.parent;
                    chuck.setting.unitObject.transform.localPosition = chuck.setting.moveObject.transform.localPosition;
                    chuck.setting.unitObject.transform.localEulerAngles = chuck.setting.moveObject.transform.localEulerAngles;

                    // 動作オブジェクトを移動
                    chuck.setting.moveObject.transform.parent = chuck.setting.unitObject.transform;
                    chuck.setting.moveObject.transform.localPosition = new Vector3(0, 0, 0);
                    chuck.setting.moveObject.transform.localEulerAngles = new Vector3(0, 0, 0);
                    foreach (var child in chuck.setting.childrenObject)
                    {
                        // 子オブジェクト移動
                        child.transform.parent = chuck.setting.moveObject.transform;
                    }
                    SetCollision(chuck.setting);

                    // ワーク生成設定
                    if (chuck.setting.workSettings.Count > 0)
                    {
                        // ワーク生成設定あり
                        foreach (var wk in chuck.setting.workSettings)
                        {
                            objectFactory.SetObjectParameter(chuck.setting, wk);
                        }
                    }
                    // ワーク削除設定
                    if (chuck.setting.workDeleteSettings.Count > 0)
                    {
                        foreach (var wk in chuck.setting.workDeleteSettings)
                        {
                            objectFactory.SetObjectParameter(chuck.setting, wk);
                        }
                    }

                    // ユニット削除
                    //                Destroy(chuck.setting.unitObject);
                }
                else
                {
                }
            }
        }
        if (isBacket)
        {
            // バケットクリア
            foreach (var backet in backets)
            {
                Destroy(backet.obj);
            }
            backets.Clear();

            // バケット設定あり
            CreateBacketPathPoints();

            // バケットオブジェクト作成
            CreateBacketObject();
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (loopPathPoints == null || loopPathPoints.Count < 2) return;
        Gizmos.color = Color.yellow;
        for (int i = 0; i < loopPathPoints.Count; i++)
        {
            Vector3 p0 = moveObject.transform.transform.TransformPoint(loopPathPoints[i]);
            if (i + 1 >= loopPathPoints.Count)
            {
                Gizmos.DrawSphere(p0, 0.001f);
            }
            else
            {
                Vector3 p1 = moveObject.transform.transform.TransformPoint(loopPathPoints[i + 1]);
                Gizmos.DrawLine(p0, p1);
                Gizmos.DrawSphere(p0, 0.001f);
                // 番号を表示
                UnityEditor.Handles.Label(p0, i.ToString());
            }
        }
    }
#endif

    /// <summary>
    /// ユニット設定から動作設定更新
    /// </summary>
    public virtual void RenewMoveDir()
    {
        if (isAction)
        {
            switch (unitSetting.actionSetting.axis)
            {
                /*
                case 0:
                    // X
                    moveDir = Vector3.right;
                    break;
                case 1:
                    // Y
                    moveDir = Vector3.forward;
                    break;

                case 2:
                    // Z
                    moveDir = Vector3.up;
                    break;
                */
                case 0:
                    // X
                    moveDir = Vector3.right;
                    break;
                case 1:
                    // Y
                    moveDir = Vector3.up;
                    break;

                case 2:
                    // Z
                    moveDir = Vector3.forward;
                    break;
            }
        }
    }

    /// <summary>
    /// 衝突された
    /// </summary>
    /// <param name="other"></param>
    protected override void OnCollisionEnter(Collision other)
    {
        base.OnCollisionEnter(other);
    }

    protected override void OnTriggerEnter(Collider other)
    {
        base.OnTriggerEnter(other);
    }

    /// <summary>
    /// 当たり判定追加
    /// </summary>
    protected override void SetCollision(UnitSetting unitSetting)
    {
        base.SetCollision(unitSetting);

        // 物体形状設定
        if (!isShape)
        {
            // isCollision ユニットは BoxCollider を実体化(isTrigger=false)。
            // ★これが ROS2 障害物検知(OverlapSphere は trigger を無視)に必須。buildConfig.isCollision には依存させない。
            // ★重い MeshCollider(GlobalScript.CreateCollider/SAColliderBuilder)は廃止。
            //   機械干渉の可視化は MachineInterferenceChecker(メッシュ直読み)で行う。
            if (unitSetting.isCollision)
            {
                foreach (var bc in this.GetComponentsInChildren<BoxCollider>())
                {
                    bc.isTrigger = false;
                }
            }
        }

        if (unitSetting.moveObject != null)
        {
            rb = unitSetting.moveObject.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = unitSetting.moveObject.transform.AddComponent<Rigidbody>();
            }
            if (unitSetting.isCollision || GlobalScript.buildConfig.isCollision)
            {
                unitSetting.moveObject.transform.AddComponent<WorkCollisionScript>();
            }
        }
        else
        {
            rb = this.AddComponent<Rigidbody>();
        }
        if (rb != null)
        {
            rb.useGravity = false;
            rb.isKinematic = true;
            //            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            //            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    /// <summary>
    /// チャックユニットの親を設定する
    /// </summary>
    public void SetChuckParent()
    {
        // チャックオブジェクト設定
        if (chuckSetting != null)
        {
            foreach (var chuck in chuckSetting.children)
            {
                // 自分と同じ親に
                if (chuck.setting.unitObject != null)
                {
                    chuck.setting.unitObject.transform.parent = transform.parent;
                }
            }
        }
    }

    /// <summary>
    /// チャック設定を行う
    /// </summary>
    public void RenewChuckSetting(ChuckUnitSetting chuckSetting)
    {
        if ((chuckSetting != null) && (this.chuckSetting != null))
        {
            foreach (var child in this.chuckSetting.children)
            {
                var tmp = chuckSetting.children.Find(d => d.name == child.name);
                if (tmp != null)
                {
                    child.offset = tmp.offset;
                    child.dir = tmp.dir;
                    child.rate = tmp.rate;
                }
            }
        }
    }

    /// <summary>
    /// 機構拡張設定
    /// </summary>
    private void SetExMechSetting()
    {
        // 主軸の回転中心指定（主軸タブの種別=回転中心の子モデル）があれば、動作部モデルをピボット空間で包んで差し替える。
        // 主軸はMotionInternalがmoveObjectを直接回すため、moveObject自体をピボットにする必要がある。
        var mainPivot = unitSetting.exMechSetting.main?.children?.Find(d => (d.type >= 1) && (d.gameObject != null));
        if (mainPivot != null)
        {
            // 回転中心＝指定モデルの原点（KMXの共通規約。原点が関節/軸中心にあるノードを指定する）
            var center = mainPivot.gameObject.transform.position;
            var pivotGo = new GameObject(unitSetting.moveObject.name + "_Pivot");
            pivotGo.transform.SetParent(unitSetting.moveObject.transform.parent, false);
            // 元モデルと同じローカル姿勢で挿入する（既存のlocalEulerAngles指定の動作コードがそのまま効く）
            pivotGo.transform.localRotation = unitSetting.moveObject.transform.localRotation;
            pivotGo.transform.localScale = Vector3.one;
            pivotGo.transform.position = center;
            unitSetting.moveObject.transform.SetParent(pivotGo.transform, true);
            Debug.Log($"拡張機構: {unitSetting.name} 主軸の回転中心を {mainPivot.gameObject.name} の原点 {center} に設定");
            unitSetting.moveObject = pivotGo;
            moveObject = pivotGo;
        }
        // ユニット追加
        var exObj = new GameObject(unitSetting.name + "(ExMech)");
        exObj.transform.parent = unitSetting.unitObject.transform;
        exObj.transform.localPosition = Vector3.zero;
        exObj.transform.localEulerAngles = Vector3.zero;
        exObj.transform.localScale = new(1, 1, 1);
        exScript = unitSetting.exMechSetting.datas[0].gameObject.AddComponent<ExMechScript>();
        exScript.SetParameter(unitSetting, unitSetting.exMechSetting);
        // 親子関係チェック
        var datas = unitSetting.exMechSetting.datas.Where(d => d.gameObject != null).ToList();
        foreach (var data in datas)
        {
            data.isChild = datas.Find(d => (d != data) && data.gameObject.transform.IsChildOf(d.gameObject.transform)) != null;
        }
        // 親子関係設定
        foreach (var data in datas)
        {
            if (!data.isChild)
            {
                data.gameObject.transform.parent = exObj.transform;
                foreach (var child in data.children)
                {
                    if ((child.gameObject == null) || (child.type == 2))
                    {
                        // 回転中心(固定)は中心参照のみ（親子付け替えせず据え置き）
                        continue;
                    }
                    child.gameObject.transform.parent = data.gameObject.transform;
                }
            }
        }
        // 拡張機構なら子供は拡張機構の動作端へ
        if (exScript.parentModel != null)
        {
            foreach (var child in unitSetting.childrenObject)
            {
                // 子オブジェクト移動
                var isUnit = false;
                for (int i = 0; i < child.transform.childCount; i++)
                {
                    if (child.transform.GetChild(i).name.StartsWith("MovableObject_"))
                    {
                        isUnit = true;
                        break;
                    }
                }
                if (isUnit)
                {
                    // ユニットなら動作端へ
                    child.transform.parent = exScript.parentModel.transform;
                }
            }
        }
    }

    /// <summary>
    /// ユニット情報を外部から設定する
    /// </summary>
    /// <param name="unitSetting"></param>
    public void SetUnitSettings(UnitSetting unitSetting, ChuckUnitSetting chuckSetting, LinearSetting linearSetting = null)
    {
        this.unitSetting = unitSetting;
        this.chuckSetting = chuckSetting;
        this.linearSetting = linearSetting;

        moveObject = unitSetting.moveObject;
        if (moveObject == null)
        {
            return;
        }
        // 初回モデル再構築
        PreModelRestruct();

        // 衝突セット
        SetCollision(unitSetting);

        // ユニット設定
        RenewUnitSetting();
    }

    /// <summary>DCS安全ゾーンの可視化(SafetyZonesコンテナ配下)かどうか。祖先に "SafetyZones" があれば true。</summary>
    private static bool IsUnderSafetyZones(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
        {
            if (p.name == "SafetyZones") { return true; }
        }
        return false;
    }

    /// <summary>
    /// 動作設定
    /// </summary>
    /// <param name="unitSetting"></param>

    public virtual void RenewUnitSetting(bool reload = false)
    {
        // オブジェクト取得
        var objectFactory = GameObject.FindObjectsByType<MultiObjectFactoryScript>(FindObjectsSortMode.None)[0];

        // コライダーの2登録回避のため削除
        {
            if (isShape)
            {
                var sset = unitSetting.shapeSetting;
                // auto: メッシュ(レンダラー)の起動時 Collider を削除（ShapeScript が mesh-bounds で張り直す）
                if (sset.auto)
                {
                    foreach (var r in unitSetting.moveObject.GetComponentsInChildren<Renderer>())
                    {
                        // DCS安全ゾーンの可視化(SafetyZones配下)はロボットのシェイプ管理外。Collider を消さない。
                        if (IsUnderSafetyZones(r.transform)) { continue; }
                        foreach (var c in r.GetComponents<Collider>())
                        {
                            Destroy(c);
                        }
                    }
                }
                // create が全て ON のシェイプは、初期設定の Collider を残したいので削除しない
                bool allCreate = (sset.datas != null) && (sset.datas.Count > 0)
                                 && sset.datas.TrueForAll(d => d.create);
                if (!allCreate)
                {
                    var instance = unitSetting.moveObject.GetComponent<ShapeScript>();
                    if (instance != null)
                    {
                        foreach (var c in instance.GetComponents<Collider>())
                        {
                            Destroy(c);
                        }
                    }
                }
            }
            if (isSuction)
            {
                var instance = unitSetting.moveObject.GetComponent<SuctionScript>();
                if (instance != null)
                {
                    foreach (var c in instance.GetComponents<Collider>())
                    {
                        Destroy(c);
                    }
                }
            }
        }
        // 形状設定
        if (isShape)
        {
            var instance = unitSetting.moveObject.GetComponent<ShapeScript>();
            if (instance != null)
            {
                Destroy(instance);
            }
            instance = unitSetting.moveObject.AddComponent<ShapeScript>();
            instance.SetParameter(unitSetting, unitSetting.shapeSetting);
            if (isBacket)
            {
                foreach (var backet in backets)
                {
                    instance = backet.obj.GetComponent<ShapeScript>();
                    if (instance != null)
                    {
                        Destroy(instance);
                    }
                    instance = backet.obj.AddComponent<ShapeScript>();
                    instance.SetParameter(unitSetting, unitSetting.shapeSetting);
                }
            }
        }
        // 吸引設定
        if (isSuction)
        {
            var instance = unitSetting.moveObject.GetComponent<SuctionScript>();
            if (instance != null)
            {
                Destroy(instance);
            }
            instance = unitSetting.moveObject.AddComponent<SuctionScript>();
            instance.SetParameter(unitSetting, unitSetting.suctionSetting);
        }
        // ワーク生成設定
        if (isWorkCreate)
        {
            if (isBacket)
            {
                foreach (var backet in backets)
                {
                    foreach (var wk in unitSetting.workSettings)
                    {
                        objectFactory.SetObjectParameter(unitSetting, wk, backet);
                    }
                }
            }
            else
            {
                // ワーク生成設定あり
                foreach (var wk in unitSetting.workSettings)
                {
                    objectFactory.SetObjectParameter(unitSetting, wk);
                }
            }
        }
        // ワーク削除設定
        if (isWorkDelete)
        {
            if (isBacket)
            {
                // 発動位置（経路開始＋バケット番号×ピッチ＋オフセットの固定点）を先に算出しておく
                // （判定・確認表示ともこの固定点を使う。トリガ時のバケット現在位置はスロット内で最大1ピッチ動くため使わない）
                foreach (var wk in unitSetting.workDeleteSettings)
                {
                    wk.isFixedPos = TryGetBacketDeletePoint(wk, out var fixedPos, out _);
                    wk.fixedWorldPos = fixedPos;
                }
                foreach (var backet in backets)
                {
                    foreach (var wk in unitSetting.workDeleteSettings)
                    {
                        objectFactory.SetObjectParameter(unitSetting, wk, backet);
                    }
                }
                // 削除範囲の確認表示：発動位置の固定点に1個だけ生成する
                foreach (var wk in unitSetting.workDeleteSettings)
                {
                    CreateBacketDeleteZone(wk);
                }
            }
            else
            {
                // ワーク生成設定あり
                foreach (var wk in unitSetting.workDeleteSettings)
                {
                    objectFactory.SetObjectParameter(unitSetting, wk);
                }
            }
        }
        // ワーク受渡設定
        if ((unitSetting.workTransferSettings != null) && (unitSetting.workTransferSettings.Count > 0))
        {
            foreach (var wk in unitSetting.workTransferSettings)
            {
                objectFactory.SetObjectParameter(unitSetting, wk);
            }
        }
        // スイッチ設定
        if (isSwitch)
        {
            // スイッチ
            var sw = unitSetting.moveObject.GetComponent<SwitchScript>();
            if (sw != null)
            {
                Destroy(sw);
            }
            sw = unitSetting.moveObject.AddComponent<SwitchScript>();
            sw.SetParameter(unitSetting, unitSetting.switchSetting);
        }
        // シグナルタワー設定
        if (isSignalTower)
        {
            // シグナルタワー
            var st = unitSetting.moveObject.GetComponent<SignalTowerScript>();
            if (st != null)
            {
                Destroy(st);
            }
            st = unitSetting.moveObject.AddComponent<SignalTowerScript>();
            st.SetParameter(unitSetting, unitSetting.towerSetting);
        }
        // LED設定
        if (isLed)
        {
            // LED
            var led = unitSetting.moveObject.GetComponent<LedScript>();
            if (led != null)
            {
                Destroy(led);
            }
            led = unitSetting.moveObject.AddComponent<LedScript>();
            led.SetParameter(unitSetting, unitSetting.ledSetting);
        }
        exModeChange = false;
        if (!reload)
        {
            // 機構拡張設定
            if (isExMech)
            {
                // 機構拡張
                exModeChange = unitSetting.actionSetting.exModeChange;
                SetExMechSetting();
            }
            // センサ生成設定
            if (this.transform.parent != null)
            {
                foreach (var sensor in unitSetting.sensorSettings)
                {
                    if (sensor.isCreate)
                    {
                        // センサ形状生成
                        var o = GlobalScript.CreateSensor(this.transform.parent.gameObject, sensor, "CvSensor");
                        o.transform.parent = unitSetting.unitObject.transform;
                        o.transform.localPosition = new Vector3
                        {
                            x = sensor.pos[0] * transform.localScale.x,
                            y = sensor.pos[2] * transform.localScale.y,
                            z = sensor.pos[1] * transform.localScale.z
                        };
                        o.transform.localEulerAngles = new Vector3
                        {
                            x = sensor.rot[0] * transform.localScale.x,
                            y = sensor.rot[2] * transform.localScale.y,
                            z = sensor.rot[1] * transform.localScale.z
                        };
                        var ss = o.AddComponent<SensorScript>();
                        ss.SetParameter(unitSetting, sensor);
                    }
                    else
                    {
                        // 形状をそのまま使用
                        var ss = unitSetting.moveObject.AddComponent<SensorScript>();
                        ss.SetParameter(unitSetting, sensor);
                        break;
                    }
                }
            }
        }
    }
    #region バケット関連
    /// <summary>
    /// バケットループのポイント作成
    /// </summary>
    private void CreateBacketPathPoints()
    {
        loopPathPoints.Clear();
        var elements = unitSetting.backetSetting.pathElements;
        if ((elements != null) && (elements.Count >= 2))
        {
            // スプロケット/経由点から経路を自動生成（ベルトモデル不要）
            CreatePathPointsFromElements(elements);
            return;
        }
        if (unitSetting.backetSetting.gameObject != null)
        {
            MeshFilter[] meshFilters = unitSetting.backetSetting.gameObject.GetComponentsInChildren<MeshFilter>();
            if (meshFilters.Length > 0)
            {
                if (meshFilters.Length == 1)
                {
                    // ベルト系
                    var vertices = meshFilters[0].sharedMesh.vertices.ToList();
                    var normals = meshFilters[0].sharedMesh.normals.ToList();
                    loopPathPoints.AddRange(ClusterToCenterLine(vertices, normals).Select(v => meshFilters[0].transform.TransformPoint(v)).Select(v => moveObject.transform.InverseTransformPoint(v)));
                    if (loopPathPoints.Count > 4)
                    {
                        var x = loopPathPoints[1].x - loopPathPoints[0].x;
                        var y = loopPathPoints[1].y - loopPathPoints[0].y;
                        var z = loopPathPoints[1].z - loopPathPoints[0].z;
                        var mx = loopPathPoints.Max(d => d.x) - loopPathPoints.Min(d => d.x);
                        var my = loopPathPoints.Max(d => d.y) - loopPathPoints.Min(d => d.y);
                        var mz = loopPathPoints.Max(d => d.z) - loopPathPoints.Min(d => d.z);
                        if ((Math.Abs(x) > Math.Abs(y)) && (Math.Abs(x) > Math.Abs(z)))
                        {
                            // 流れ面がX方向
                            backetDir = x > 0 ? Vector3.right : Vector3.left;
                            if (my > mz)
                            {
                                // 高さ方向がY
                                loopPathPoints = loopPathPoints.Select(v => new Vector3(v.x, v.y, 0)).ToList();
                            }
                            else
                            {
                                // 高さ方向がZ
                                loopPathPoints = loopPathPoints.Select(v => new Vector3(v.x, 0, v.z)).ToList();
                            }
                        }
                        else if ((Math.Abs(y) > Math.Abs(x)) && (Math.Abs(y) > Math.Abs(z)))
                        {
                            // 流れ面がY方向
                            backetDir = y > 0 ? Vector3.up : Vector3.down;
                            if (mx > mz)
                            {
                                // 高さ方向がX
                                loopPathPoints = loopPathPoints.Select(v => new Vector3(v.x, v.y, 0)).ToList();
                            }
                            else
                            {
                                // 高さ方向がZ
                                loopPathPoints = loopPathPoints.Select(v => new Vector3(0, v.y, v.z)).ToList();
                            }
                        }
                        else
                        {
                            // 流れ面がZ方向
                            backetDir = z > 0 ? Vector3.forward : Vector3.back;
                            if (mx > my)
                            {
                                // 高さ方向がX
                                loopPathPoints = loopPathPoints.Select(v => new Vector3(v.x, 0, v.z)).ToList();
                            }
                            else
                            {
                                // 高さ方向がY
                                loopPathPoints = loopPathPoints.Select(v => new Vector3(0, v.y, v.z)).ToList();
                            }
                        }
                    }
                    loopPathPoints.Add(loopPathPoints[0]);
                }
            }
        }
    }

    /// <summary>
    /// 経路要素（スプロケット/経由点）から循環経路を生成する
    /// 登録順の中心点を結び、各要素を半径の円弧（外周側）で回る角丸多角形の閉ループを作る
    /// スプロケット2個なら直線2本＋半円2つのスタジアム形になる
    /// </summary>
    private void CreatePathPointsFromElements(List<BacketSetting.PathElement> elements)
    {
        // 外形ラップ方式：各要素のメッシュ外形（ループ面に投影した2D凸包）に張ったチェーンとして経路を作る
        // 計算はすべてワールド座標(m)で行い、最後にmoveObjectローカルへ変換する

        // 要素ごとの頂点（ワールド）と代表点を収集し、ループ面の法線軸を決める
        var vertsList = new List<List<Vector3>>();
        var repPoints = new List<Vector3>();
        var depthAxis = -1;
        foreach (var element in elements)
        {
            List<Vector3> verts = null;
            var rep = Vector3.zero;
            if (element.gameObject != null)
            {
                var meshFilters = element.gameObject.GetComponentsInChildren<MeshFilter>();
                var bounds = new Bounds();
                var first = true;
                verts = new List<Vector3>();
                foreach (var mf in meshFilters)
                {
                    if (mf.sharedMesh == null)
                    {
                        continue;
                    }
                    foreach (var v in mf.sharedMesh.vertices)
                    {
                        var w = mf.transform.TransformPoint(v);
                        verts.Add(w);
                        if (first)
                        {
                            bounds = new Bounds(w, Vector3.zero);
                            first = false;
                        }
                        else
                        {
                            bounds.Encapsulate(w);
                        }
                    }
                }
                if (verts.Count == 0)
                {
                    verts = null;
                    rep = element.gameObject.transform.position;
                }
                else
                {
                    // 中心はモデルの原点を使う（KMXの共通規約。外形中心は使わない）
                    rep = element.gameObject.transform.position;
                    if ((depthAxis < 0) && (element.type == 0))
                    {
                        // スプロケットの最薄軸=回転軸=ループ面の法線
                        var size = bounds.size;
                        depthAxis = ((size.x <= size.y) && (size.x <= size.z)) ? 0 : (size.y <= size.z ? 1 : 2);
                    }
                }
            }
            else
            {
                // 手入力座標（KMX座標系X,Y,Z→Unity X,Z,Y。動作部モデル位置からのワールド軸オフセット）
                rep = moveObject.transform.position + new Vector3(element.pos[0], element.pos[2], element.pos[1]);
            }
            vertsList.Add(verts);
            repPoints.Add(rep);
            // 経路で参照しているモデルはPrefab非表示時にも表示を維持する
            if (element.gameObject != null)
            {
                BacketPathOverlay.KeepVisibleModels.Add(element.gameObject.transform);
            }
        }
        if (depthAxis < 0)
        {
            // スプロケットモデルがない場合は代表点の広がりが最小の軸を法線とする
            var ex = repPoints.Max(d => d.x) - repPoints.Min(d => d.x);
            var ey = repPoints.Max(d => d.y) - repPoints.Min(d => d.y);
            var ez = repPoints.Max(d => d.z) - repPoints.Min(d => d.z);
            depthAxis = ((ez <= ex) && (ez <= ey)) ? 2 : (((ex <= ey) && (ex <= ez)) ? 0 : 1);
        }
        // ループ面の深さ位置は代表点の平均を維持する
        var depth = depthAxis == 0 ? repPoints.Average(d => d.x) : (depthAxis == 1 ? repPoints.Average(d => d.y) : repPoints.Average(d => d.z));

        // 要素ごとの外形（2D凸包）を作り、オフセットで膨張/収縮する
        var outlines = new List<List<Vector2>>();
        var centroids = new List<Vector2>();
        for (var i = 0; i < elements.Count; i++)
        {
            List<Vector2> outline;
            if (vertsList[i] != null)
            {
                outline = ConvexHull2D(vertsList[i].Select(v => To2D(v, depthAxis)).ToList());
            }
            else
            {
                outline = new List<Vector2> { To2D(repPoints[i], depthAxis) };
            }
            if ((outline.Count == 1) && (elements[i].offset > 1e-6f))
            {
                // 1点要素に丸め半径指定があれば円形の外形にする
                var c = outline[0];
                outline = new List<Vector2>();
                for (var k = 0; k < 36; k++)
                {
                    var ang = k * Mathf.PI * 2f / 36f;
                    outline.Add(c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * elements[i].offset);
                }
            }
            else if ((outline.Count > 2) && (Math.Abs(elements[i].offset) > 1e-6f))
            {
                // 半径オフセットの膨張/収縮はモデル原点を放射中心にする
                outline = OffsetOutline(outline, elements[i].offset, To2D(repPoints[i], depthAxis));
            }
            outlines.Add(outline);
            // 要素の中心はモデル原点（外形重心は使わない）
            centroids.Add(To2D(repPoints[i], depthAxis));
        }
        Debug.Log($"バケット経路生成: {name} 要素数={elements.Count} 法線軸={"XYZ"[depthAxis]} 深さ={depth:F3} " +
                  string.Join(" ", outlines.Select((o, i) => $"[{i}]外形点数={o.Count}/重心={centroids[i]:F3}/幅={(o.Max(p => p.x) - o.Min(p => p.x)):F3}x{(o.Max(p => p.y) - o.Min(p => p.y)):F3}")));

        // 巻き方向（外形重心の符号付き面積。2要素では0になるので正扱い）
        var n = outlines.Count;
        var area = 0f;
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            area += centroids[i].x * centroids[j].y - centroids[j].x * centroids[i].y;
        }
        var winding = area >= 0f ? 1 : -1;

        // スプロケット回転の登録（搬送に同期して見た目を回す）
        // 同一経路を複数ユニットが参照する場合（前爪/後爪・同期ユニット等）は先勝ちで1本化する
        sprockets.Clear();
        var staleDrivers = sprocketDrivers.Where(kv => (kv.Value == null) || (kv.Value == this)).Select(kv => kv.Key).ToList();
        foreach (var key in staleDrivers)
        {
            sprocketDrivers.Remove(key);
        }
        if (!sprocketDrivers.TryGetValue(elements, out var spDriver) || (spDriver == null))
        {
            sprocketDrivers[elements] = this;
            // 2D平面のCCW回転が対応するワールド軸まわりの符号（(x,z)平面のCCWは-Y回り）
            var axisSign = depthAxis == 1 ? -1f : 1f;
            var axisWorld = depthAxis == 0 ? Vector3.right : (depthAxis == 1 ? Vector3.up : Vector3.forward);
            for (var i = 0; i < elements.Count; i++)
            {
                if ((elements[i].type != 0) || (elements[i].gameObject == null) || (outlines[i].Count < 3))
                {
                    continue;
                }
                // ピッチ円半径 = 外形ラップ点の重心からの平均距離（チェーンが乗る半径）
                var radius = outlines[i].Average(p => Vector2.Distance(p, centroids[i]));
                if (radius < 1e-4f)
                {
                    continue;
                }
                var t = elements[i].gameObject.transform;
                sprockets.Add(new SprocketInfo
                {
                    obj = elements[i].gameObject,
                    centerLocal = moveObject.transform.InverseTransformPoint(To3D(centroids[i], depthAxis, depth)),
                    axisLocal = moveObject.transform.InverseTransformDirection(axisWorld),
                    radius = radius,
                    sign = winding * axisSign,
                    basePosLocal = moveObject.transform.InverseTransformPoint(t.position),
                    baseRotLocal = Quaternion.Inverse(moveObject.transform.rotation) * t.rotation,
                });
            }
        }

        // 隣接要素間の共通接線（進行方向に対し両外形が内側に来る接線）を求める
        var tOutIdx = new int[n];
        var tInIdx = new int[n];
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            if (!FindTangent(outlines[i], outlines[j], winding, out var ai, out var bj))
            {
                // 接線が見つからない（外形同士が重なっている等）場合は相手側重心に最も近い頂点で代用
                ai = NearestIndex(outlines[i], centroids[j]);
                bj = NearestIndex(outlines[j], centroids[i]);
                Debug.LogWarning($"バケット経路生成: {name} 要素{i}→{j}の接線が見つからないため近似します（外形が重なっていないか確認してください）");
            }
            tOutIdx[i] = ai;
            tInIdx[j] = bj;
        }

        // 到着接点→出発接点まで外形の縁に沿って歩く（要素間の直線は隣接する縁の端点間で自動的にできる）
        var points = new List<Vector2>();
        for (var i = 0; i < n; i++)
        {
            var outline = outlines[i];
            var cnt = outline.Count;
            var idx = tInIdx[i];
            for (var guard = 0; guard <= cnt; guard++)
            {
                points.Add(outline[idx]);
                if (idx == tOutIdx[i])
                {
                    break;
                }
                idx = (idx + winding + cnt) % cnt;
            }
        }

        // 3D（ワールド）に戻してmoveObjectローカルへ変換し、閉ループにする
        foreach (var p in points)
        {
            var v = moveObject.transform.InverseTransformPoint(To3D(p, depthAxis, depth));
            if ((loopPathPoints.Count == 0) || (Vector3.Distance(loopPathPoints[loopPathPoints.Count - 1], v) > 1e-6f))
            {
                loopPathPoints.Add(v);
            }
        }
        if (loopPathPoints.Count < 2)
        {
            loopPathPoints.Clear();
            return;
        }
        // 流れ方向（最初の要素間直線の主軸）
        var dir3 = To3D(outlines[1 % n][tInIdx[1 % n]], depthAxis, depth) - To3D(outlines[0][tOutIdx[0]], depthAxis, depth);
        if (dir3 == Vector3.zero)
        {
            dir3 = loopPathPoints[1] - loopPathPoints[0];
        }
        if ((Math.Abs(dir3.x) >= Math.Abs(dir3.y)) && (Math.Abs(dir3.x) >= Math.Abs(dir3.z)))
        {
            backetDir = dir3.x > 0 ? Vector3.right : Vector3.left;
        }
        else if (Math.Abs(dir3.y) >= Math.Abs(dir3.z))
        {
            backetDir = dir3.y > 0 ? Vector3.up : Vector3.down;
        }
        else
        {
            backetDir = dir3.z > 0 ? Vector3.forward : Vector3.back;
        }
        loopPathPoints.Add(loopPathPoints[0]);
        if (unitSetting.backetSetting.pathReverse)
        {
            // 逆回り：点列を反転して進行方向を逆にする（閉ループなので先頭/末尾の一致は保たれる）
            loopPathPoints.Reverse();
            backetDir = -backetDir;
        }
    }

    /// <summary>
    /// ループ面の2D座標へ変換（depthAxis=法線軸 0:X 1:Y 2:Z）
    /// </summary>
    private static Vector2 To2D(Vector3 v, int depthAxis)
    {
        return depthAxis == 0 ? new Vector2(v.y, v.z) : (depthAxis == 1 ? new Vector2(v.x, v.z) : new Vector2(v.x, v.y));
    }

    /// <summary>
    /// 2D座標をループ面の3D座標へ戻す（深さ成分=depth）
    /// </summary>
    private static Vector3 To3D(Vector2 v, int depthAxis, float depth)
    {
        return depthAxis == 0 ? new Vector3(depth, v.x, v.y) : (depthAxis == 1 ? new Vector3(v.x, depth, v.y) : new Vector3(v.x, v.y, depth));
    }

    /// <summary>
    /// 2D凸包を求める（Andrewのmonotone chain、反時計回り）
    /// </summary>
    private static List<Vector2> ConvexHull2D(List<Vector2> pts)
    {
        var sorted = pts.OrderBy(p => p.x).ThenBy(p => p.y).ToList();
        // 同一点の除去
        var uniq = new List<Vector2>();
        foreach (var p in sorted)
        {
            if ((uniq.Count == 0) || (Vector2.Distance(uniq[uniq.Count - 1], p) > 1e-7f))
            {
                uniq.Add(p);
            }
        }
        if (uniq.Count <= 2)
        {
            return uniq;
        }
        var hull = new List<Vector2>();
        // 下側凸包
        foreach (var p in uniq)
        {
            while ((hull.Count >= 2) && (Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f))
            {
                hull.RemoveAt(hull.Count - 1);
            }
            hull.Add(p);
        }
        // 上側凸包
        var lowerCount = hull.Count + 1;
        for (var i = uniq.Count - 2; i >= 0; i--)
        {
            var p = uniq[i];
            while ((hull.Count >= lowerCount) && (Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f))
            {
                hull.RemoveAt(hull.Count - 1);
            }
            hull.Add(p);
        }
        hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    /// <summary>
    /// 外積（o→a と o→b）
    /// </summary>
    private static float Cross(Vector2 o, Vector2 a, Vector2 b)
    {
        return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    }

    /// <summary>
    /// 外形頂点の重心
    /// </summary>
    private static Vector2 Centroid(List<Vector2> pts)
    {
        return new Vector2(pts.Average(p => p.x), pts.Average(p => p.y));
    }

    /// <summary>
    /// 外形をオフセットぶん膨張/収縮する（重心から放射方向へ移動する近似）
    /// </summary>
    private static List<Vector2> OffsetOutline(List<Vector2> outline, float offset, Vector2 center)
    {
        // 放射中心はモデル原点（呼び出し側から渡す。外形重心は使わない）
        var c = center;
        var result = new List<Vector2>();
        foreach (var p in outline)
        {
            var dir = p - c;
            var len = dir.magnitude;
            if (len < 1e-6f)
            {
                result.Add(p);
                continue;
            }
            result.Add(c + dir / len * Mathf.Max(0f, len + offset));
        }
        return result;
    }

    /// <summary>
    /// 凸外形A→Bの共通接線を求める（進行方向に対し両外形が内側=windingの側に来るもの）
    /// </summary>
    private static bool FindTangent(List<Vector2> a, List<Vector2> b, int winding, out int ai, out int bi)
    {
        ai = 0;
        bi = 0;
        var best = float.MaxValue;
        var found = false;
        for (var i = 0; i < a.Count; i++)
        {
            for (var j = 0; j < b.Count; j++)
            {
                var dir = b[j] - a[i];
                var len = dir.magnitude;
                if (len < 1e-9f)
                {
                    continue;
                }
                // 許容誤差：接線から0.1mmまでのはみ出しは無視
                var eps = len * 1e-4f;
                var ok = true;
                foreach (var p in a)
                {
                    if ((dir.x * (p.y - a[i].y) - dir.y * (p.x - a[i].x)) * winding < -eps)
                    {
                        ok = false;
                        break;
                    }
                }
                if (!ok)
                {
                    continue;
                }
                foreach (var p in b)
                {
                    if ((dir.x * (p.y - a[i].y) - dir.y * (p.x - a[i].x)) * winding < -eps)
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok && (len < best))
                {
                    best = len;
                    ai = i;
                    bi = j;
                    found = true;
                }
            }
        }
        return found;
    }

    /// <summary>
    /// 経路上の姿勢を作る（経路接線を前方、ループ中心から外向きの接線直交成分を上とする）
    /// </summary>
    private Quaternion PathRotation(Vector3 pos, Vector3 dir)
    {
        var up = pos - backetCenter;
        up -= dir * Vector3.Dot(up, dir);
        if (up.sqrMagnitude < 1e-12f)
        {
            up = Vector3.up;
        }
        return Quaternion.LookRotation(dir, up.normalized);
    }

    /// <summary>
    /// 指定点に最も近い外形頂点の番号
    /// </summary>
    private static int NearestIndex(List<Vector2> outline, Vector2 target)
    {
        var index = 0;
        var best = float.MaxValue;
        for (var i = 0; i < outline.Count; i++)
        {
            var d = Vector2.SqrMagnitude(outline[i] - target);
            if (d < best)
            {
                best = d;
                index = i;
            }
        }
        return index;
    }

    /// <summary>
    /// バケットオブジェクト作成
    /// </summary>
    private void CreateBacketObject()
    {
        // 既存の動作オブジェクトを無効化
        moveObject.SetActive(false);
        if (loopPathPoints.Count <= 4)
        {
            Debug.LogWarning($"バケット生成: {name} 経路点数が不足しています（{loopPathPoints.Count}点）");
        }
        if (loopPathPoints.Count > 4)
        {
            // パスの総距離を計算
            float totalLength = 0f;
            for (int i = 0; i < loopPathPoints.Count - 1; i++)
            {
                totalLength += Vector3.Distance(loopPathPoints[i], loopPathPoints[i + 1]);
            }
            // バケット間隔（経路点はmoveObjectローカル単位のため、スケールぶん換算する）
            backetScale = moveObject.transform.lossyScale.x;
            if (backetScale < 1e-9f)
            {
                backetScale = 1f;
            }
            backetPitch = unitSetting.backetSetting.pitch / 1000f / backetScale;
            backetOffset = unitSetting.backetSetting.offset / 1000f / backetScale;
            /*
            backetLength = unitSetting.backetSetting.count * unitSetting.backetSetting.pitch / 1000f;
            backetCountMax = (int)Math.Round(backetLength / backetPitch);
            */
            backetPathLength = totalLength;
            if (unitSetting.backetSetting.loopLength > 0f)
            {
                // 周長指定あり：この距離でちょうど1周して初期位置に戻る（幾何経路長との差は経路上に均等配分される）
                backetLength = unitSetting.backetSetting.loopLength / 1000f / backetScale;
                backetCountMax = (int)Math.Round(unitSetting.backetSetting.loopLength / unitSetting.backetSetting.pitch);
            }
            else
            {
                backetCountMax = (int)Math.Round(totalLength / backetPitch);
                backetLength = backetCountMax * backetPitch;
            }
            backetCenter = new Vector3(loopPathPoints.Average(d => d.x), loopPathPoints.Average(d => d.y), loopPathPoints.Average(d => d.z));
            // 設計位置（moveObjectローカル原点）に最も近い経路上の点を探し、その姿勢を基準にする
            // （基準位置ではバケットが元モデルと同じ向きになり、経路に沿って回転していく）
            var accumulated = 0f;
            var bestDistance = float.MaxValue;
            var bestOffset = 0f;
            for (int i = 0; i < loopPathPoints.Count - 1; i++)
            {
                var a = loopPathPoints[i];
                var ab = loopPathPoints[i + 1] - a;
                var segLen = ab.magnitude;
                if (segLen > 1e-9f)
                {
                    var t = Mathf.Clamp01(Vector3.Dot(-a, ab) / (segLen * segLen));
                    var d = (a + ab * t).sqrMagnitude;
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        bestOffset = accumulated + segLen * t;
                    }
                }
                accumulated += segLen;
            }
            GetPositionOnPath(bestOffset, out Vector3 basePos, out Vector3 baseDir);
            backetBaseRot = baseDir != Vector3.zero ? PathRotation(basePos, baseDir) : Quaternion.identity;
            backetBasePos = basePos;
            var count = unitSetting.backetSetting.count == 0 ? backetCountMax : unitSetting.backetSetting.count;
            Debug.Log($"バケット生成: {name} 経路長={totalLength:F3} ピッチ={backetPitch:F3} スケール={backetScale:F4} 生成数={count}(最大{backetCountMax}) 表示={unitSetting.backetSetting.visible}");
            // 画面オーバーレイ（Ctrl+Shift押下中表示）へ幾何周長(mm)を登録：周長設定の値決めに使う
            // ※同期ユニットは同期元と同じ経路のため重複登録しない
            if (syncMasterSetting == null)
            {
                BacketPathOverlay.Register(name, unitSetting.backetSetting.pathName,
                    totalLength * backetScale * 1000f, unitSetting.backetSetting.loopLength);
            }
            if (count <= 0)
            {
                Debug.LogWarning($"バケット生成: {name} 生成数が0です（経路長とバケットピッチの設定を確認してください）");
            }
            for (var i = 0; i < count; i++)
            {
                var backet = new BacketInfo
                {
                    // 親を指定して複製する（親なし複製だと祖先のスケールが失われ、巨大なクローンになる）
                    obj = unitSetting.backetSetting.visible ? Instantiate(moveObject, moveObject.transform.parent) : new GameObject(),
                    offset = backetPitch * i,
                };
                if (!unitSetting.backetSetting.visible)
                {
                    backet.obj.transform.parent = moveObject.transform.parent;
                }
                // パス上のその距離の位置を取得（経路点はmoveObjectローカルなのでmoveObject基準でワールドへ変換）
                GetPositionOnLoop(backet.offset, out Vector3 pos, out Vector3 dir);
                // 基準姿勢からの相対回転（常に上向き時は姿勢を変えない）
                var delta = ((dir != Vector3.zero) && !unitSetting.backetSetting.upright)
                    ? PathRotation(pos, dir) * Quaternion.Inverse(backetBaseRot)
                    : Quaternion.identity;
                // モデル原点と経路取付点のオフセット（設計位置での関係）を維持して配置する
                backet.obj.transform.position = moveObject.transform.TransformPoint(pos - delta * backetBasePos);
                backet.obj.transform.rotation = moveObject.transform.rotation * delta;
                backet.obj.SetActive(true);
                backets.Add(backet);
            }
            // 初期値セット
            MoveBacket(0);
            // 経路ライン（Ctrl+Shift押下中のみ表示）を生成：ビルド版でも経路を目視確認できるようにする
            // ※バケットのクローン生成後に作る（moveObjectの子にするため、先に作るとクローンへ複製されてしまう）
            // ※同期ユニットは同期元と同じ経路のため生成しない（二重表示防止）
            if (syncMasterSetting == null)
            {
                CreatePathLine();
            }
        }
    }

    /// <summary>
    /// バケット向けワーク削除範囲の確認表示を、発動する経路上の固定位置に生成する。
    /// 位置＝経路開始（開始オフセット込み）からバケット番号×ピッチ進んだ地点のバケット基準位置＋設定オフセット。
    /// 実際の削除はその番号のスロットにいるバケット基準で発動するため、バケットはこの球の位置（〜1ピッチ先まで）で削除される。
    /// </summary>
    /// <summary>
    /// バケット削除の発動位置（経路上の固定点・ワールド）を算出する。
    /// 位置＝経路開始（開始オフセット込み）からバケット番号×ピッチ進んだスロット先頭のバケット基準位置＋設定オフセット（バケット姿勢基準・実寸m）。
    /// </summary>
    private bool TryGetBacketDeletePoint(WorkDeleteSetting wk, out Vector3 center, out Quaternion rot)
    {
        center = Vector3.zero;
        rot = Quaternion.identity;
        if ((loopPathPoints.Count < 2) || (backetPitch <= 0f))
        {
            return false;
        }
        if (wk.backetno < 0)
        {
            Debug.LogWarning($"バケット削除: {name} バケット番号が未設定のため発動しません（タグ={wk.tag}）");
            return false;
        }
        // 指定スロット先頭の経路位置とバケット姿勢を求める（バケット配置と同じ計算）
        GetPositionOnLoop(wk.backetno * backetPitch, out Vector3 pos, out Vector3 dir);
        var delta = ((dir != Vector3.zero) && !unitSetting.backetSetting.upright)
            ? PathRotation(pos, dir) * Quaternion.Inverse(backetBaseRot)
            : Quaternion.identity;
        var basePos = moveObject.transform.TransformPoint(pos - delta * backetBasePos);
        rot = moveObject.transform.rotation * delta;
        center = basePos + rot * new Vector3(wk.pos[0], wk.pos[1], wk.pos[2]);
        return true;
    }

    private void CreateBacketDeleteZone(WorkDeleteSetting wk)
    {
        if ((wk.distance <= 0f) || !wk.isFixedPos)
        {
            return;
        }
        if (!TryGetBacketDeletePoint(wk, out var center, out var baseRot))
        {
            return;
        }
        var parent = moveObject.transform.parent;
        var zoneName = $"WorkDeleteZone_{unitSetting.name}_{wk.tag}_{wk.backetno}";
        var old = parent != null ? parent.Find(zoneName) : null;
        if (old != null)
        {
            Destroy(old.gameObject);
        }
        var zone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        zone.name = zoneName;
        var col = zone.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }
        zone.transform.SetParent(parent, false);
        zone.transform.position = center;
        zone.transform.rotation = baseRot;
        // 親スケールを打ち消して実寸（直径=範囲×2）で表示する
        var ls = parent != null ? parent.lossyScale : Vector3.one;
        zone.transform.localScale = new Vector3(
            wk.distance * 2f / Mathf.Max(Mathf.Abs(ls.x), 1e-6f),
            wk.distance * 2f / Mathf.Max(Mathf.Abs(ls.y), 1e-6f),
            wk.distance * 2f / Mathf.Max(Mathf.Abs(ls.z), 1e-6f));
        var rend = zone.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.sharedMaterial = SafetyZoneScript.MakeZoneMaterial(new Color(1f, 0.2f, 0.2f, 0.3f));
        }
        zone.SetActive(false);
        BacketPathOverlay.RegisterLine($"{zoneName}_{zone.GetInstanceID()}", zone);
    }

    /// <summary>経路確認ライン（表示切替はBacketPathOverlayが行う）</summary>
    private GameObject pathLineObj;

    /// <summary>
    /// 経路ラインを生成する（LineRenderer実描画。ギズモと違いビルド版でも見える）
    /// </summary>
    private void CreatePathLine()
    {
        if (pathLineObj != null)
        {
            Destroy(pathLineObj);
            pathLineObj = null;
        }
        if (loopPathPoints == null || loopPathPoints.Count < 2)
        {
            return;
        }
        pathLineObj = new GameObject($"BacketPathLine_{name}");
        // moveObject 本体はバケット生成時に無効化されるため、その親にぶら下げて
        // moveObject と同じローカル変換を複製する（経路点は moveObject ローカルのまま使える）
        pathLineObj.transform.SetParent(moveObject.transform.parent, false);
        pathLineObj.transform.localPosition = moveObject.transform.localPosition;
        pathLineObj.transform.localRotation = moveObject.transform.localRotation;
        pathLineObj.transform.localScale = moveObject.transform.localScale;
        var lr = pathLineObj.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = false;   // 経路点は終端に始点を追加済み（閉ループ）のため不要
        lr.numCornerVertices = 0;
        lr.numCapVertices = 0;
        // 幅はワールド単位（useWorldSpace=falseでもtransformスケールは掛からない）：実寸約2mm
        lr.widthMultiplier = 0.002f;
        var mat = MakePathLineMaterial(Color.yellow);
        if (mat != null)
        {
            lr.sharedMaterial = mat;
            lr.startColor = Color.yellow;
            lr.endColor = Color.yellow;
        }
        lr.positionCount = loopPathPoints.Count;
        lr.SetPositions(loopPathPoints.ToArray());
        pathLineObj.SetActive(false);
        BacketPathOverlay.RegisterLine(name, pathLineObj);
    }

    /// <summary>経路ライン用マテリアル（URP Unlit。安全ゾーンの枠線と同方式）</summary>
    private static Material MakePathLineMaterial(Color col)
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) { sh = Shader.Find("Sprites/Default"); }
        if (sh == null) { return null; }
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) { m.SetColor("_BaseColor", col); }
        if (m.HasProperty("_Color")) { m.SetColor("_Color", col); }
        return m;
    }

    /// <summary>
    /// コンベアライン算出(Y方向は無視)
    /// </summary>
    /// <param name="verts"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    private List<Vector3> ClusterToCenterLine(List<Vector3> verts, List<Vector3> norms)
    {
        float minX = verts.Min(v => v.x);
        float maxX = verts.Max(v => v.x);
        float minZ = verts.Min(v => v.z);
        float maxZ = verts.Max(v => v.z);
        float tolerance = 0.0001f; // 許容誤差（必要に応じて調整）

        List<Vector3> tmpV = new List<Vector3>();
        List<Vector3> tmpN = new List<Vector3>(); // 対応する法線も一緒に取得

        var isXDirection = Mathf.Abs(maxX - minX) > Mathf.Abs(maxZ - minZ);

        for (int i = 0; i < verts.Count; i++)
        {
            if (isXDirection)
            {
                if (Mathf.Abs(verts[i].z - maxZ) < tolerance)
                {
                    tmpV.Add(verts[i]);
                    tmpN.Add(norms[i]);
                }
            }
            else
            {
                if (Mathf.Abs(verts[i].x - maxX) < tolerance)
                {
                    tmpV.Add(verts[i]);
                    tmpN.Add(norms[i]);
                }
            }
        }

        // 円弧の中心をvertsから算出
        Vector3 center = new Vector3(verts.Average(v => v.x), 0f, verts.Average(v => v.z));

        // 法線が中心から外向きの頂点だけ抽出
        var outerVerts = new List<Vector3>();
        for (int i = 0; i < tmpV.Count; i++)
        {
            // 中心→頂点の方向ベクトル（XZ平面）
            Vector3 toVertex = new Vector3(
                tmpV[i].x - center.x,
                0f,
                tmpV[i].z - center.z
            ).normalized;

            // 法線もXZ平面に投影
            Vector3 normal = new Vector3(tmpN[i].x, 0f, tmpN[i].z).normalized;

            // 内積が正 = 法線が外向き = 外側の頂点
            if (Vector3.Dot(toVertex, normal) > 0.5f)
            {
                outerVerts.Add(tmpV[i]);
            }
        }

        // 開始点取得
        var firstPoint = outerVerts.OrderBy(v => isXDirection ? (isBacketRvs ? Math.Abs(v.x) : v.x) : (isBacketRvs ? Math.Abs(v.z) : v.z)).OrderByDescending(v => v.y).First();
        center = new Vector3(isXDirection ? outerVerts.Average(v => v.x) : 0f, verts.Average(v => v.y), !isXDirection ? outerVerts.Average(v => v.z) : 0f);
        var result = isBacketRvs ? outerVerts.OrderBy(v => Mathf.Atan2(v.y - center.y, isXDirection ? v.x - center.x : v.z - center.z)).ToList() : outerVerts.OrderByDescending(v => Mathf.Atan2(v.y - center.y, isXDirection ? v.x - center.x : v.z - center.z)).ToList();

        var index = result.IndexOf(firstPoint);
        return result.Skip(index).Concat(result.Take(index)).ToList();
    }

    /// <summary>
    /// ループ上のポイント取得
    /// 既定：名目距離（周長基準）をそのまま経路距離として使い、幾何経路長を超えたら先頭へ戻る
    /// 周長スケーリングON：周長と経路長の差を経路上に均等配分する（名目距離→経路距離を比例換算）
    /// </summary>
    private void GetPositionOnLoop(float distance, out Vector3 pos, out Vector3 dir)
    {
        float geom;
        if (unitSetting.backetSetting.loopScaling && (backetLength > 1e-9f))
        {
            geom = distance / backetLength * backetPathLength;
        }
        else
        {
            geom = distance;
        }
        // 経路の開始位置オフセット（この経路を参照する全ユニットに効く）
        geom += unitSetting.backetSetting.pathStartOffset / backetScale;
        if (backetPathLength > 1e-9f)
        {
            geom %= backetPathLength;
            if (geom < 0f)
            {
                geom += backetPathLength;
            }
        }
        GetPositionOnPath(geom, out pos, out dir);
    }

    /// <summary>
    /// パス上のポイント取得
    /// </summary>
    /// <param name="path"></param>
    /// <param name="distance"></param>
    /// <param name="pos"></param>
    /// <param name="dir"></param>
    private void GetPositionOnPath(float distance, out Vector3 pos, out Vector3 dir)
    {
        float accumulated = 0f;

        for (int i = 0; i < loopPathPoints.Count - 1; i++)
        {
            float segLen = Vector3.Distance(loopPathPoints[i], loopPathPoints[i + 1]);

            if (accumulated + segLen >= distance)
            {
                float t = (distance - accumulated) / segLen;
                pos = Vector3.Lerp(loopPathPoints[i], loopPathPoints[i + 1], t);
                dir = (loopPathPoints[i + 1] - loopPathPoints[i]).normalized;
                if (!isBacketRvs)
                {
                    dir = -dir;
                }
                return;
            }

            accumulated += segLen;
        }

        // パスの終端
        pos = loopPathPoints[loopPathPoints.Count - 1];
        dir = (loopPathPoints[loopPathPoints.Count - 1] - loopPathPoints[loopPathPoints.Count - 2]).normalized;
        if (!isBacketRvs)
        {
            dir = -dir;
        }
    }

    /// <summary>
    /// スプロケット回転情報（バケット搬送に同期して見た目を回す）
    /// </summary>
    private class SprocketInfo
    {
        public GameObject obj;
        /// <summary>回転中心（moveObjectローカル）</summary>
        public Vector3 centerLocal;
        /// <summary>回転軸（moveObjectローカル）</summary>
        public Vector3 axisLocal;
        /// <summary>ピッチ円半径（ワールドm。外形ラップ半径の平均）</summary>
        public float radius;
        /// <summary>回転方向（経路の巻き方向から決定）</summary>
        public float sign;
        /// <summary>初期姿勢（moveObjectローカル）</summary>
        public Vector3 basePosLocal;
        public Quaternion baseRotLocal;
    }

    /// <summary>
    /// 回転させるスプロケット（経路のスプロケット要素でモデル指定のあるもの）
    /// </summary>
    private readonly List<SprocketInfo> sprockets = new List<SprocketInfo>();

    /// <summary>
    /// 経路ごとのスプロケット駆動ユニット（同一経路を複数ユニットが参照する場合の二重回転防止。
    /// 経路名参照時はpathElementsのList実体が共有されるため、それをキーに先勝ちで1本化する）
    /// </summary>
    private static readonly Dictionary<object, AxisMotionBase> sprocketDrivers = new Dictionary<object, AxisMotionBase>();

    /// <summary>
    /// 同期元ユニット（同期機構＋バケット用）
    /// </summary>
    private UnitSetting syncMasterSetting;
    private AxisMotionBase syncMasterBase;
    /// <summary>同期設定のオフセット(mm)・倍率・方向</summary>
    private float syncOffset;
    private float syncRate = 1f;
    private int syncDir = 1;

    /// <summary>
    /// 同期元をセットする（同期ユニットがバケットを持つ場合、同期元のベルト送り量で爪を動かす）
    /// </summary>
    public void SetSyncMaster(UnitSetting master, ChuckUnit chuck = null)
    {
        syncMasterSetting = master;
        syncMasterBase = null;
        if (chuck != null)
        {
            syncOffset = chuck.offset;
            syncRate = chuck.rate == 0f ? 1f : chuck.rate;
            syncDir = chuck.dir == 0 ? 1 : chuck.dir;
        }
    }

    /// <summary>
    /// ベルト移動量(mm)。同期ユニットへのミラー用
    /// </summary>
    public float BacketTravelMm { get { return backetAccum + backetPos; } }

    /// <summary>
    /// 更新処理（同期機構のバケット: 同期元のベルト移動量をミラーして爪を動かす。
    /// 動作設定を持つユニットはMotionInternalが本メソッドをオーバーライドするため、ここは同期ユニット専用）
    /// </summary>
    protected override void MyFixedUpdate()
    {
        base.MyFixedUpdate();
        if ((syncMasterSetting == null) || !isBacket)
        {
            return;
        }
        if (syncMasterBase == null)
        {
            syncMasterBase = syncMasterSetting.unitObject != null ? syncMasterSetting.unitObject.GetComponent<AxisMotionBase>() : null;
            if (syncMasterBase == null)
            {
                return;
            }
        }
        // 同期設定のオフセット(mm)・倍率・方向を適用して同期元の送り量をミラーする
        backetAccum = syncMasterBase.BacketTravelMm * syncRate * syncDir + syncOffset;
        backetPos = 0f;
        MoveBacket(0f);
    }

    /// <summary>
    /// バケット移動
    /// </summary>
    /// <param name="pos"></param>
    protected void MoveBacket(float distance)
    {
        var length = distance - backetPos;
        if (Math.Abs(length) > 0.0001f)
        {
            // 動作中
            // 回転方向が変わってないかチェック（駆動値がリセットされる軸への対応）
            if (isBacketMoveRvs != (length < 0))
            {
                // 進んだ距離(mm)をそのまま積算する
                // （旧実装はピッチ単位の整数に丸めていたため、ピッチ未満のストローク軸では毎サイクル初期位置へ戻ってしまった）
                if (!isBacketMoveRvs)
                {
                    backetAccum += backetPos;
                }
                else
                {
                    backetAccum -= backetPos;
                }
                // 発散防止に周長で正規化しておく（位置は周長の剰余で決まるため挙動は不変）
                // 周長設定があればその値(mm)をそのまま使う（backetLength経由のfloat往復誤差を避ける）
                var loopMm = unitSetting.backetSetting.loopLength > 0f
                    ? unitSetting.backetSetting.loopLength
                    : backetLength * backetScale * 1000f;
                if (loopMm > 0.001f)
                {
                    backetAccum %= loopMm;
                    if (backetAccum < 0f)
                    {
                        backetAccum += loopMm;
                    }
                }
            }
            else
            {
                isBacketMoveRvs = length < 0;
            }
        }
        backetPos = distance;
        //　動作オフセット（backetAccum/backetPosはmm。経路のローカル単位へ換算）
        var backetNext = (backetAccum + backetPos) / 1000f / backetScale + backetOffset;
        foreach (var backet in backets)
        {
            var p = (backet.offset + backetNext) % backetLength;
            backet.backetno = (int)(p / backetPitch);
            // パス上のその距離の位置を取得（経路点はmoveObjectローカルなのでmoveObject基準でワールドへ変換）
            GetPositionOnLoop(p, out Vector3 pos, out Vector3 dir);
            // 基準姿勢からの相対回転（常に上向き時は姿勢を変えない）
            var delta = ((dir != Vector3.zero) && !unitSetting.backetSetting.upright)
                ? PathRotation(pos, dir) * Quaternion.Inverse(backetBaseRot)
                : Quaternion.identity;
            // モデル原点と経路取付点のオフセット（設計位置での関係）を維持して配置する
            backet.obj.transform.position = moveObject.transform.TransformPoint(pos - delta * backetBasePos);
            backet.obj.transform.rotation = moveObject.transform.rotation * delta;
        }
        // スプロケットを搬送量に同期して回す（見た目のみ。機構計算には影響しない）
        RotateSprockets();
    }

    /// <summary>
    /// スプロケットをベルト送り量に同期して回転させる（角度=送り量÷ピッチ円半径。半径違いも正しい速度比になる）
    /// </summary>
    private void RotateSprockets()
    {
        if (sprockets.Count == 0)
        {
            return;
        }
        var travelM = (backetAccum + backetPos) / 1000f;   // 実寸m
        foreach (var sp in sprockets)
        {
            if (sp.obj == null)
            {
                continue;
            }
            var angle = sp.sign * travelM / sp.radius * Mathf.Rad2Deg;
            var axisW = moveObject.transform.TransformDirection(sp.axisLocal);
            var centerW = moveObject.transform.TransformPoint(sp.centerLocal);
            var q = Quaternion.AngleAxis(angle, axisW);
            var baseW = moveObject.transform.TransformPoint(sp.basePosLocal);
            sp.obj.transform.position = centerW + q * (baseW - centerW);
            sp.obj.transform.rotation = q * (moveObject.transform.rotation * sp.baseRotLocal);
        }
    }
    #endregion バケット関連
}
