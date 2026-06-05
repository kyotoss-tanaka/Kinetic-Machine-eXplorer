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
            return (unitSetting != null) && (unitSetting.backetSetting != null) && (unitSetting.backetSetting.gameObject != null);
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
                // スイッチとシグナルタワーの座標は親からのオフセットに変更
                if ((motion.moveObject.GetComponent<SwitchScript>() != null) || (motion.moveObject.GetComponent<SignalTowerScript>() != null))
                {
                    child.transform.localPosition += unit.transform.localPosition;
                    child.transform.localEulerAngles += unit.transform.localEulerAngles;
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
            if (!GlobalScript.buildConfig.isCollision && unitSetting.isCollision)
            {
                // 当たり判定追加
                /*
                foreach (var mesh in this.GetComponentsInChildren<MeshFilter>())
                {
                    if (mesh.GetComponentInChildren<Collider>() == null)
                    {
                        var col = mesh.AddComponent<MeshCollider>();
                        col.convex = true;
                        col.isTrigger = true;
                    }
                }
                */

                if ((Application.platform == RuntimePlatform.Android) || (Application.platform == RuntimePlatform.IPhonePlayer))
                {
                    // VRでは無視
                }
                else
                {
                    // WindowsではCollider作成
                    GlobalScript.CreateCollider(unitSetting.moveObject);
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
                var instance = unitSetting.moveObject.GetComponent<ShapeScript>();
                if (instance != null)
                {
                    foreach (var c in instance.GetComponents<Collider>())
                    {
                        Destroy(c);
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
                foreach (var backet in backets)
                {
                    foreach (var wk in unitSetting.workDeleteSettings)
                    {
                        objectFactory.SetObjectParameter(unitSetting, wk, backet);
                    }
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
    /// バケットオブジェクト作成
    /// </summary>
    private void CreateBacketObject()
    {
        // 既存の動作オブジェクトを無効化
        moveObject.SetActive(false);
        if (loopPathPoints.Count > 4)
        {
            // パスの総距離を計算
            float totalLength = 0f;
            for (int i = 0; i < loopPathPoints.Count - 1; i++)
            {
                totalLength += Vector3.Distance(loopPathPoints[i], loopPathPoints[i + 1]);
            }
            // バケット間隔
            backetPitch = unitSetting.backetSetting.pitch / 1000f;
            backetOffset = unitSetting.backetSetting.offset / 1000f;
            /*
            backetLength = unitSetting.backetSetting.count * unitSetting.backetSetting.pitch / 1000f;
            backetCountMax = (int)Math.Round(backetLength / backetPitch);
            */
            backetCountMax = (int)Math.Round(totalLength / backetPitch);
            backetLength = backetCountMax * backetPitch;
            backetCenter = new Vector3(loopPathPoints.Average(d => d.x), loopPathPoints.Average(d => d.y), loopPathPoints.Average(d => d.z));
            var count = unitSetting.backetSetting.count == 0 ? backetCountMax : unitSetting.backetSetting.count;
            for (var i = 0; i < count; i++)
            {
                var backet = new BacketInfo
                {
                    obj = unitSetting.backetSetting.visible ? Instantiate(moveObject) : new GameObject(),
                    offset = backetPitch * i,
                };
                // パス上のその距離の位置を取得
                GetPositionOnPath(backet.offset, out Vector3 pos, out Vector3 dir);
                backet.obj.transform.parent = moveObject.transform.parent;
                backet.obj.transform.localPosition = pos;
                if (dir != Vector3.zero)
                {
                    // 円弧の中心からposへの方向を「上」として使う
                    Vector3 toPos = (pos - new Vector3(backetCenter.x, pos.y, backetCenter.z)).normalized;
                    Quaternion rot = Quaternion.LookRotation(dir, toPos);
                    backet.obj.transform.localRotation = rot * Quaternion.Euler(Vector3.zero);
                }
                backet.obj.SetActive(true);
                backets.Add(backet);
            }
            // 初期値セット
            MoveBacket(0);
        }
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
    /// バケット移動
    /// </summary>
    /// <param name="pos"></param>
    protected void MoveBacket(float distance)
    {
        var length = distance - backetPos;
        if (Math.Abs(length) > 0.0001f)
        {
            // 動作中
            // 回転方向が変わってないかチェック
            if (isBacketMoveRvs != (length < 0))
            {
                if (!isBacketMoveRvs)
                {
                    backetCounter += (int)Math.Round(backetPos / unitSetting.backetSetting.pitch);
                    if (backetCounter >= backetCountMax)
                    {
                        backetCounter = 0;
                    }
                }
                else
                {
                    backetCounter -= (int)Math.Round(backetPos / unitSetting.backetSetting.pitch);
                    if (backetCounter < 0)
                    {
                        backetCounter = backetCountMax - 1;
                    }
                }
            }
            else
            {
                isBacketMoveRvs = length < 0;
            }
        }
        backetPos = distance;
        //　動作オフセット
        var backetNext = backetCounter * backetPitch + backetPos / 1000f + backetOffset;
        foreach (var backet in backets)
        {
            var p = (backet.offset + backetNext) % backetLength;
            backet.backetno = (int)(p / backetPitch);
            // パス上のその距離の位置を取得
            GetPositionOnPath(p, out Vector3 pos, out Vector3 dir);
            backet.obj.transform.localPosition = pos;
            if (dir != Vector3.zero)
            {
                // 円弧の中心からposへの方向を「上」として使う
                Vector3 toPos = (pos - new Vector3(backetCenter.x, pos.y, backetCenter.z)).normalized;
                Quaternion rot = Quaternion.LookRotation(dir, toPos);
                backet.obj.transform.localRotation = rot * Quaternion.Euler(Vector3.zero);
            }
        }
    }
    #endregion バケット関連
}
