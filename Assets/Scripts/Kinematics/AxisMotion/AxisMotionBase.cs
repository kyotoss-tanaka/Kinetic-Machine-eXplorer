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
    protected TagInfo? cycleTag;

    /// <summary>
    /// 動作あり
    /// </summary>
    protected bool isAction
    {
        get
        {
            return (unitSetting != null) && (unitSetting.actionSetting != null);
        }
    }

    /// <summary>
    /// オブジェクト形状あり
    /// </summary>
    protected bool isShape
    {
        get
        {
            return (unitSetting != null) && (unitSetting.shapeSetting != null);
        }
    }

    /// <summary>
    /// 吸引あり
    /// </summary>
    protected bool isSuction
    {
        get
        {
            return (unitSetting != null) && (unitSetting.suctionSetting != null);
        }
    }

    /// <summary>
    /// ワーク生成あり
    /// </summary>
    protected bool isWorkCreate
    {
        get
        {
            return (unitSetting != null) && (unitSetting.workSetting != null);
        }
    }

    /// <summary>
    /// ワーク削除あり
    /// </summary>
    protected bool isWorkDelete
    {
        get
        {
            return (unitSetting != null) && (unitSetting.workDeleteSetting != null);
        }
    }

    /// <summary>
    /// スイッチ
    /// </summary>
    protected bool isSwitch
    {
        get
        {
            return (unitSetting != null) && (unitSetting.switchSetting != null);
        }
    }
    /// <summary>
    /// シグナルタワー
    /// </summary>
    protected bool isSignalTower
    {
        get
        {
            return (unitSetting != null) && (unitSetting.towerSetting != null);
        }
    }

    /// <summary>
    /// 回転動作
    /// </summary>
    protected bool isRotate
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
            renewUnitSetting();

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
        }

        // チャックオブジェクト設定
        if (chuckSetting != null)
        {
            foreach (var chuck in chuckSetting.children)
            {
                // 一旦ユニットの親子関係を生成
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
        }

        // 衝突セット
        SetCollision(unitSetting);
    }

    /// <summary>
    /// ユニット設定から動作設定更新
    /// </summary>
    protected virtual void renewUnitSetting()
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
            if (unitSetting.isCollision)
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
                    var meshColliderBuilder = unitSetting.moveObject.AddComponent<SAMeshColliderBuilder>();
                    meshColliderBuilder.reducerProperty.shapeType = SAColliderBuilderCommon.ShapeType.Mesh;
//                    meshColliderBuilder.reducerProperty.meshType = SAColliderBuilderCommon.MeshType.Raw;
                    meshColliderBuilder.reducerProperty.meshType = SAColliderBuilderCommon.MeshType.ConvexHull;
                    meshColliderBuilder.rigidbodyProperty.isCreate = false;
                    meshColliderBuilder.colliderProperty.convex = false;
                    meshColliderBuilder.colliderProperty.isTrigger = false;
                    KssMeshColliderBuilderInspector.Process(meshColliderBuilder);
                    foreach (var col in this.GetComponentsInChildren<MeshCollider>())
                    {
                        try
                        {
                            if (col == null || col.sharedMesh == null) continue;
                            AddFakeThickness(col.sharedMesh);
                            var verts = col.sharedMesh.vertices;
                            float minZ = verts.Min(v => v.z);
                            float maxZ = verts.Max(v => v.z);
                            float thickness = Mathf.Abs(maxZ - minZ);
                            int triangleCount = col.sharedMesh.triangles.Length / 3;
                            var message = "";
                            if (IsMesh3D(col.sharedMesh, ref message) && (triangleCount <= 255 * 10))
                            {
                                try
                                {
                                    col.convex = true;
                                    col.isTrigger = true;
                                }
                                catch (System.Exception ex)
                                {
                                    Debug.LogWarning($"Convex設定に失敗: {col.name}, 理由: {ex.Message}");
                                    col.convex = false;
                                    col.isTrigger = false;
                                }
                            }
                            else
                            {
//                                Debug.Log($"convexスキップ: {col.name}, triangle: {triangleCount}, thickness: {thickness}");
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning($"Convex設定に失敗: {col.name}, 理由: {ex.Message}");
                            col.convex = false;
                            col.isTrigger = false;
                        }
                    }
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
            if (unitSetting.isCollision)
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

        // 形状設定
        if (isShape)
        {
            var instance = unitSetting.moveObject.AddComponent<ShapeScript>();
            instance.SetParameter(unitSetting, unitSetting.shapeSetting);
        }
        // 吸引設定
        if (isSuction)
        {
            var instance = unitSetting.moveObject.AddComponent<SuctionScript>();
            instance.SetParameter(unitSetting, unitSetting.suctionSetting);
        }
        // ワーク生成設定
        if (isWorkCreate)
        {
            // ワーク生成設定あり
            var work = transform.AddComponent<ObjectFactoryScript>();
            work.SetParameter(unitSetting, unitSetting.workSetting);
        }
        // ワーク削除設定
        if (isWorkDelete)
        {
            // ワーク生成設定あり
            var work = transform.AddComponent<ObjectDeleteScript>();
            work.SetParameter(unitSetting, unitSetting.workDeleteSetting);
        }
        // スイッチ設定
        if (isSwitch)
        {
            // スイッチ
            var sw = unitSetting.moveObject.AddComponent<SwitchScript>();
            sw.SetParameter(unitSetting, unitSetting.switchSetting);
        }
        // シグナルタワー設定
        if (isSignalTower)
        {
            // シグナルタワー
            var st = unitSetting.moveObject.AddComponent<SignalTowerScript>();
            st.SetParameter(unitSetting, unitSetting.towerSetting);
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
