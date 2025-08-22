using Parameters;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;
using static UnityEngine.UI.CanvasScaler;

public class ConveyorScript : KssBaseScript
{
    /// <summary>
    /// キャンバス表示
    /// </summary>
    protected override bool isCanvas { get { return true; } }

    /// <summary>
    /// ベルトコンベアの設定速度
    /// </summary>
    [SerializeField]
    private float TargetDriveSpeed = 1.0f;

    /// <summary>
    /// ベルトコンベアが物体を動かす方向
    /// </summary>
    [SerializeField]
    private Vector3 DriveDirection = new Vector3(1, 0, 0);

    /// <summary>
    /// コンベアが物体を押す力（加速力）
    /// </summary>
    [SerializeField]
    private float _forcePower = 9.8f;

    /// <summary>
    /// 動摩擦係数(0～1)
    /// </summary>
    [SerializeField]
    private float dynamicFriction = 0.2f;

    /// <summary>
    /// 静摩擦係数(0～1)
    /// </summary>
    [SerializeField]
    private float staticFriction = 0.25f;

    /// <summary>
    /// 動作タグ
    /// </summary>
    [SerializeField]
    private TagInfo ActTag;

    /// <summary>
    /// 物体マテリアル
    /// </summary>
    private PhysicsMaterial physicMaterial;

    /// <summary>
    /// 現在のベルトコンベアの速度
    /// </summary>
    private float CurrentSpeed { get { return _currentSpeed; } }

    /// <summary>
    /// 現在速度
    /// </summary>
    private float _currentSpeed = 0;

    /// <summary>
    /// 接触しているオブジェクト
    /// </summary>
    private List<Rigidbody> _rigidbodies = new List<Rigidbody>();

    /// <summary>
    /// BoxCollider
    /// </summary>
    private BoxCollider boxCollider;

    /// <summary>
    /// コンベア設定
    /// </summary>
    private ConveyerSetting cv;

    /// <summary>
    /// 動作中
    /// </summary>
    private bool isMoving;

    // Start is called before the first frame update
    protected override void Start()
    {
        base.Start();
        boxCollider = GetComponentInChildren<BoxCollider>();
        if (boxCollider == null)
        {
            boxCollider = transform.AddComponent<BoxCollider>();
        }
    }

    protected override void MyFixedUpdate()
    {
        isMoving = true;
        if ((ActTag != null) && (ActTag.Tag != ""))
        {
            isMoving &= GlobalScript.GetTagData(ActTag) == 1;
        }
        // 摩擦係数セット
        boxCollider.material.staticFriction = staticFriction;
        boxCollider.material.dynamicFriction = dynamicFriction;

        //消滅したオブジェクトは除去する
        _rigidbodies.RemoveAll(r => r == null);
        /*
         オブジェクトの方で起床させた
        foreach (var rb in _rigidbodies)
        {
            if (rb.IsSleeping())
            {
                rb.WakeUp();
            }
        }
        */

        /*
        // 方向セット
        var direction = transform.TransformDirection(DriveDirection);

        foreach (var r in _rigidbodies)
        {
            //物体の移動速度のベルトコンベア方向の成分だけを取り出す
            var objectSpeed = Vector3.Dot(r.velocity, direction);

            //目標値以下なら加速する
            if (objectSpeed < Mathf.Abs(TargetDriveSpeed))
            {
                r.AddForce(direction * _forcePower, ForceMode.Acceleration);
                //r.AddForceAtPosition(direction * _forcePower, transform.position, ForceMode.Acceleration);
            }
        }
        */
    }

    protected override void OnCollisionEnter(Collision collision)
    {
        var rigidBody = collision.gameObject.GetComponent<Rigidbody>();
        rigidBody.freezeRotation = true;
        _rigidbodies.Add(rigidBody);
    }

    protected override void OnCollisionExit(Collision collision)
    {
        var rigidBody = collision.gameObject.GetComponent<Rigidbody>();
        rigidBody.freezeRotation = false;
        _rigidbodies.Remove(rigidBody);
    }

    protected override void OnCollisionStay(Collision collision)
    {
        var rigidBody = collision.rigidbody;
        if ((rigidBody != null) && isMoving)
        {
            // 方向セット
            var direction = transform.TransformDirection(DriveDirection);
            // 水平方向に力を加えて物体を移動
            rigidBody.linearVelocity = TargetDriveSpeed * direction;
            //rigidBody.velocity = new Vector3(conveyorSpeed, rigidBody.velocity.y, rigidBody.velocity.z);
        }
    }

    /// <summary>
    /// パラメータをセットする
    /// </summary>
    /// <param name="components"></param>
    /// <param name="scriptables"></param>
    /// <param name="kssInstanceIds"></param>
    /// <param name="root"></param>
    public override void SetParameter(List<Component> components, List<KssPartsBase> scriptables, List<KssInstanceIds> kssInstanceIds, JsonElement root)
    {
        TargetDriveSpeed = GetFloatFromPrm(root, "TargetDriveSpeed");
        DriveDirection = GetVector3FromPrm(root, "DriveDirection");
        _forcePower = GetFloatFromPrm(root, "_forcePower");
        dynamicFriction = GetFloatFromPrm(root, "dynamicFriction");
        staticFriction = GetFloatFromPrm(root, "staticFriction");
    }

    /// <summary>
    /// パラメータをセットする
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="robo"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);

        cv = (ConveyerSetting)obj;
        TargetDriveSpeed = cv.spd;
        _forcePower = cv.force;
        staticFriction = cv.staticFriction;
        dynamicFriction = cv.dynamicFriction;
        if (cv.axis == 0)
        {
            DriveDirection = new Vector3(1 * cv.dir * transform.localScale.x, 0, 0);
        }
        else if (cv.axis == 1)
        {
            DriveDirection = new Vector3(0, 0, 1 * cv.dir * transform.localScale.z);
        }
        else
        {
            DriveDirection = new Vector3(0, 1 * cv.dir * transform.localScale.y, 0);
        }
        // タグ設定
        if (cv.actTag != null)
        {
            ActTag = ScriptableObject.CreateInstance<TagInfo>();
            ActTag.Database = unitSetting.Database;
            ActTag.MechId = unitSetting.mechId;
            ActTag.Tag = cv.actTag;
        }

        // 衝突検知追加
        var rig = transform.GetComponent<Rigidbody>();
        if (rig == null)
        {
            rig = transform.AddComponent<Rigidbody>();
        }
        rig.useGravity = false;
        rig.isKinematic = true;

        var mesh = GetComponentInChildren<MeshFilter>();
        mesh.AddComponent<BoxCollider>();

        // コンベアのため接触を無効化
        var mc = GetComponentInChildren<MeshCollider>();
        if (mc != null)
        {
            mc.enabled = false;
        }
    }

    /// <summary>
    /// キャンバス表示用データ作成
    /// </summary>
    public override void RenewCanvasValues()
    {
        base.RenewCanvasValues();
        dctDispValue["Status"] = new CanvasValue
        {
            value = isMoving ? "Run" : "Stop"
        };
        dctDispValue["Speed"] = new CanvasValue
        {
            value = isMoving ? TargetDriveSpeed * 1000 : 0,
            unit = "mm/sec",
            format = "0.0"
        };
    }
}