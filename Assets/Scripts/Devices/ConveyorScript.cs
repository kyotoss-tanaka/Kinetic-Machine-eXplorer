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
    /// �L�����o�X�\��
    /// </summary>
    protected override bool isCanvas { get { return true; } }

    /// <summary>
    /// �x���g�R���x�A�̐ݒ葬�x
    /// </summary>
    [SerializeField]
    private float TargetDriveSpeed = 1.0f;

    /// <summary>
    /// �x���g�R���x�A�����̂𓮂�������
    /// </summary>
    [SerializeField]
    private Vector3 DriveDirection = new Vector3(1, 0, 0);

    /// <summary>
    /// �R���x�A�����̂������́i�����́j
    /// </summary>
    [SerializeField]
    private float _forcePower = 9.8f;

    /// <summary>
    /// �����C�W��(0�`1)
    /// </summary>
    [SerializeField]
    private float dynamicFriction = 0.2f;

    /// <summary>
    /// �Ö��C�W��(0�`1)
    /// </summary>
    [SerializeField]
    private float staticFriction = 0.25f;

    /// <summary>
    /// ����^�O
    /// </summary>
    [SerializeField]
    private TagInfo ActTag;

    /// <summary>
    /// ���̃}�e���A��
    /// </summary>
    private PhysicsMaterial physicMaterial;

    /// <summary>
    /// ���݂̃x���g�R���x�A�̑��x
    /// </summary>
    private float CurrentSpeed { get { return _currentSpeed; } }

    /// <summary>
    /// ���ݑ��x
    /// </summary>
    private float _currentSpeed = 0;

    /// <summary>
    /// �ڐG���Ă���I�u�W�F�N�g
    /// </summary>
    private List<Rigidbody> _rigidbodies = new List<Rigidbody>();

    /// <summary>
    /// BoxCollider
    /// </summary>
    private BoxCollider boxCollider;

    /// <summary>
    /// �R���x�A�ݒ�
    /// </summary>
    private ConveyerSetting cv;

    /// <summary>
    /// ���쒆
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
        // ���C�W���Z�b�g
        boxCollider.material.staticFriction = staticFriction;
        boxCollider.material.dynamicFriction = dynamicFriction;

        //���ł����I�u�W�F�N�g�͏�������
        _rigidbodies.RemoveAll(r => r == null);
        /*
         �I�u�W�F�N�g�̕��ŋN��������
        foreach (var rb in _rigidbodies)
        {
            if (rb.IsSleeping())
            {
                rb.WakeUp();
            }
        }
        */

        /*
        // �����Z�b�g
        var direction = transform.TransformDirection(DriveDirection);

        foreach (var r in _rigidbodies)
        {
            //���̂̈ړ����x�̃x���g�R���x�A�����̐������������o��
            var objectSpeed = Vector3.Dot(r.velocity, direction);

            //�ڕW�l�ȉ��Ȃ��������
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
            // �����Z�b�g
            var direction = transform.TransformDirection(DriveDirection);
            // ���������ɗ͂������ĕ��̂��ړ�
            rigidBody.linearVelocity = TargetDriveSpeed * direction;
            //rigidBody.velocity = new Vector3(conveyorSpeed, rigidBody.velocity.y, rigidBody.velocity.z);
        }
    }

    /// <summary>
    /// �p�����[�^���Z�b�g����
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
    /// �p�����[�^���Z�b�g����
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
        // �^�O�ݒ�
        if (cv.actTag != null)
        {
            ActTag = ScriptableObject.CreateInstance<TagInfo>();
            ActTag.Database = unitSetting.Database;
            ActTag.MechId = unitSetting.mechId;
            ActTag.Tag = cv.actTag;
        }

        // �Փˌ��m�ǉ�
        var rig = transform.GetComponent<Rigidbody>();
        if (rig == null)
        {
            rig = transform.AddComponent<Rigidbody>();
        }
        rig.useGravity = false;
        rig.isKinematic = true;

        var mesh = GetComponentInChildren<MeshFilter>();
        mesh.AddComponent<BoxCollider>();

        // �R���x�A�̂��ߐڐG�𖳌���
        var mc = GetComponentInChildren<MeshCollider>();
        if (mc != null)
        {
            mc.enabled = false;
        }
    }

    /// <summary>
    /// �L�����o�X�\���p�f�[�^�쐬
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