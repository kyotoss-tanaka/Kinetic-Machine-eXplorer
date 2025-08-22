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
using static KssMeshColliderEditorCommon;
using static OVRPlugin;
using UnityEngine.UIElements;
using System.Security.Cryptography;

public class AxisMotionBase : KinematicsBase
{
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
    /// 回転動作
    /// </summary>
    public bool isRotate
    {
        get
        {
            return unitSetting.actionSetting.mode == 1 || unitSetting.actionSetting.mode == 3;
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
    protected void PreModelRestruct()
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
            }
        }
        // チャックオブジェクト設定
        if (chuckSetting != null)
        {
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
                    // ユニット削除
                    //                Destroy(chuck.setting.unitObject);
                }
                else
                {
                }
            }
        }
        // 衝突セット
        SetCollision(unitSetting);
    }

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
                unitSetting.moveObject.transform.AddComponent<CollisionScript>();
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
    /// メッシュが3Dかチェック
    /// </summary>
    /// <param name="mesh"></param>
    /// <returns></returns>
    private bool IsMesh3D(UnityEngine.Mesh mesh, ref string message)
    {
        if (mesh == null || mesh.vertexCount < 4) return false;

        var verts = mesh.vertices;
        var min = verts[0];
        var max = verts[0];

        foreach (var v in verts)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }

        float thicknessZ = Mathf.Abs(max.z - min.z);
        float thicknessY = Mathf.Abs(max.y - min.y);
        float thicknessX = Mathf.Abs(max.x - min.x);

        message = (thicknessX * thicknessX * 1000000 + thicknessY * thicknessY * 1000000 + thicknessZ * thicknessZ * 1000000).ToString();

        // 最小でも3方向にある程度の広がりがないと凸包は失敗する可能性
        return (thicknessX > 1e-4f && thicknessY > 1e-4f && thicknessZ > 1e-4f);
    }

    /// <summary>
    /// 厚みを加える
    /// </summary>
    /// <param name="mesh"></param>
    /// <param name="offset"></param>
    private void AddFakeThickness(UnityEngine.Mesh mesh, float offset = 0.0001f)
    {
        Vector3[] verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++)
        {
            verts[i].z += UnityEngine.Random.Range(-offset, offset); // Z方向に厚み
        }
        mesh.vertices = verts;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
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
                chuck.setting.unitObject.transform.parent = transform.parent;
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
        // 親子関係設定
        foreach (var data in unitSetting.exMechSetting.datas)
        {
            if (data.gameObject != null)
            {
                data.gameObject.transform.parent = exObj.transform;
                foreach (var child in data.children)
                {
                    child.gameObject.transform.parent = data.gameObject.transform;
                }
            }
            else
            {
                Debug.Log($"エラー：ユニット名「{unitSetting.name}」の拡張モデルが存在しません。");
                return;
            }
        }
        var ex = unitSetting.exMechSetting.datas[0].gameObject.AddComponent<ExMechScript>();
        ex.SetParameter(unitSetting, unitSetting.exMechSetting);
    }

    /// <summary>
    /// ユニット情報を外部から設定する
    /// </summary>
    /// <param name="unitSetting"></param>
    public void SetUnitSettings(UnitSetting unitSetting, ChuckUnitSetting chuckSetting)
    {
        this.unitSetting = unitSetting;
        this.chuckSetting = chuckSetting;
        this.moveObject = unitSetting.moveObject;
        if (moveObject == null)
        {
            return;
        }
        PreModelRestruct();

        // ユニット設定
        RenewUnitSetting();
    }

    /// <summary>
    /// 動作設定
    /// </summary>
    /// <param name="unitSetting"></param>

    public virtual void RenewUnitSetting(bool reload = false)
    {
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
            // ワーク生成設定あり
            foreach (var wk in unitSetting.workSettings)
            {
                var work = transform.GetComponent<ObjectFactoryScript>();
                if (work != null)
                {
                    Destroy(work);
                }
                work = transform.AddComponent<ObjectFactoryScript>();
                work.SetParameter(unitSetting, wk);
            }
        }
        // ワーク削除設定
        if (isWorkDelete)
        {
            // ワーク削除設定あり
            foreach (var wk in unitSetting.workSettings)
            {
                var work = transform.GetComponent<ObjectDeleteScript>();
                if (work != null)
                {
                    Destroy(work);
                }
                work = transform.AddComponent<ObjectDeleteScript>();
                work.SetParameter(unitSetting, wk);
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
        if (!reload)
        {
            // 機構拡張設定
            if (isExMech)
            {
                // 機構拡張
                SetExMechSetting();
            }
            // センサ生成設定
            foreach (var sensor in unitSetting.sensorSettings)
            {
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
        }
    }
}
