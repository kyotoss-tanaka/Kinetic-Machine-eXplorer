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

public class AxisMotionBase : KinematicsBase
{
    /// <summary>
    /// �萔
    /// </summary>
    protected const float Thousand = 1000f;
    protected const float Million = 1000000f;

    /// <summary>
    /// �`���b�N���j�b�g�ݒ�
    /// </summary>
    protected ChuckUnitSetting chuckSetting;

    /// <summary>
    /// ����ΏۃI�u�W�F�N�g
    /// </summary>
    protected GameObject moveObject;

    /// <summary>
    /// ����Ώۂ̃`���b�N�I�u�W�F�N�g
    /// </summary>
    protected List<GameObject> chuckObjects = new List<GameObject>();

    /// <summary>
    /// �������
    /// </summary>
    protected Vector3 moveDir;

    /// <summary>
    /// ����p
    /// </summary>
    protected Rigidbody rb;

    /// <summary>
    /// ���삠��
    /// </summary>
    protected bool isAction
    {
        get
        {
            return (unitSetting != null) && (unitSetting.actionSetting != null);
        }
    }

    /// <summary>
    /// �I�u�W�F�N�g�`�󂠂�
    /// </summary>
    protected bool isShape
    {
        get
        {
            return (unitSetting != null) && (unitSetting.shapeSetting != null);
        }
    }

    /// <summary>
    /// �z������
    /// </summary>
    protected bool isSuction
    {
        get
        {
            return (unitSetting != null) && (unitSetting.suctionSetting != null);
        }
    }

    /// <summary>
    /// ���[�N��������
    /// </summary>
    protected bool isWorkCreate
    {
        get
        {
            return (unitSetting != null) && (unitSetting.workSetting != null);
        }
    }

    /// <summary>
    /// ���[�N�폜����
    /// </summary>
    protected bool isWorkDelete
    {
        get
        {
            return (unitSetting != null) && (unitSetting.workDeleteSetting != null);
        }
    }

    /// <summary>
    /// �X�C�b�`
    /// </summary>
    protected bool isSwitch
    {
        get
        {
            return (unitSetting != null) && (unitSetting.switchSetting != null);
        }
    }
    /// <summary>
    /// �V�O�i���^���[
    /// </summary>
    protected bool isSignalTower
    {
        get
        {
            return (unitSetting != null) && (unitSetting.towerSetting != null);
        }
    }

    /// <summary>
    /// ��]����
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
            // ���j�b�g���X�V
            renewUnitSetting();

            /*
            // ����pRigitbody�Z�b�g
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
    /// �p�����[�^���[�h�X�N���v�g����̏��Ɋ�Â����f���č\�z
    /// </summary>
    protected void PreModelRestruct()
    {
        // ���j�b�g���̃I�u�W�F�N�g�쐬
        var unit = unitSetting.unitObject;
        // �e�q�֌W�쐬
        unit.transform.parent = moveObject.transform.parent;
        unit.transform.localPosition = moveObject.transform.localPosition;
        unit.transform.localEulerAngles = moveObject.transform.localEulerAngles;
        moveObject.transform.parent = unit.transform;
        moveObject.transform.localPosition = new Vector3(0, 0, 0);
        moveObject.transform.localEulerAngles = new Vector3(0, 0, 0);
        // �q�I�u�W�F�N�g��
        foreach (var child in unitSetting.childrenObject)
        {
            // �q�I�u�W�F�N�g�ړ�
            child.transform.parent = moveObject.transform;
        }

        // �`���b�N�I�u�W�F�N�g�ݒ�
        if (chuckSetting != null)
        {
            foreach (var chuck in chuckSetting.children)
            {
                // ��U���j�b�g�̐e�q�֌W�𐶐�
                chuck.setting.unitObject.transform.parent = chuck.setting.moveObject.transform.parent;
                chuck.setting.unitObject.transform.localPosition = chuck.setting.moveObject.transform.localPosition;
                chuck.setting.unitObject.transform.localEulerAngles = chuck.setting.moveObject.transform.localEulerAngles;

                // ����I�u�W�F�N�g���ړ�
                chuck.setting.moveObject.transform.parent = chuck.setting.unitObject.transform;
                chuck.setting.moveObject.transform.localPosition = new Vector3(0, 0, 0);
                chuck.setting.moveObject.transform.localEulerAngles = new Vector3(0, 0, 0);
                foreach (var child in chuck.setting.childrenObject)
                {
                    // �q�I�u�W�F�N�g�ړ�
                    child.transform.parent = chuck.setting.moveObject.transform;
                }
                SetCollision(chuck.setting);
                // ���j�b�g�폜
                //                Destroy(chuck.setting.unitObject);
            }
        }

        // �Փ˃Z�b�g
        SetCollision(unitSetting);
    }

    /// <summary>
    /// ���j�b�g�ݒ肩�瓮��ݒ�X�V
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
    /// �Փ˂��ꂽ
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
    /// �����蔻��ǉ�
    /// </summary>
    protected override void SetCollision(UnitSetting unitSetting)
    {
        base.SetCollision(unitSetting);

        // ���̌`��ݒ�
        if (!isShape)
        {
            if (unitSetting.isCollision)
            {
                // �����蔻��ǉ�
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
                var meshColliderBuilder = unitSetting.moveObject.AddComponent<SAMeshColliderBuilder>();
                meshColliderBuilder.reducerProperty.shapeType = SAColliderBuilderCommon.ShapeType.Mesh;
                meshColliderBuilder.reducerProperty.meshType = SAColliderBuilderCommon.MeshType.Raw;
                meshColliderBuilder.rigidbodyProperty.isCreate = false;
                meshColliderBuilder.colliderProperty.convex = false;
                KssMeshColliderBuilderInspector.Process(meshColliderBuilder);
            }
        }
        if (unitSetting.moveObject != null)
        {
            rb = unitSetting.moveObject.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = unitSetting.moveObject.transform.AddComponent<Rigidbody>();
            }
            if (IsCollision)
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
    /// ���j�b�g�����O������ݒ肷��
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

        // �`��ݒ�
        if (isShape)
        {
            var instance = unitSetting.moveObject.AddComponent<ShapeScript>();
            instance.SetParameter(unitSetting, unitSetting.shapeSetting);
        }
        // �z���ݒ�
        if (isSuction)
        {
            var instance = unitSetting.moveObject.AddComponent<SuctionScript>();
            instance.SetParameter(unitSetting, unitSetting.suctionSetting);
        }
        // ���[�N�����ݒ�
        if (isWorkCreate)
        {
            // ���[�N�����ݒ肠��
            var work = transform.AddComponent<ObjectFactoryScript>();
            work.SetParameter(unitSetting, unitSetting.workSetting);
        }
        // ���[�N�폜�ݒ�
        if (isWorkDelete)
        {
            // ���[�N�����ݒ肠��
            var work = transform.AddComponent<ObjectDeleteScript>();
            work.SetParameter(unitSetting, unitSetting.workDeleteSetting);
        }
        // �X�C�b�`�ݒ�
        if (isSwitch)
        {
            // �X�C�b�`
            var sw = unitSetting.moveObject.AddComponent<SwitchScript>();
            sw.SetParameter(unitSetting, unitSetting.switchSetting);
        }
        // �V�O�i���^���[�ݒ�
        if (isSignalTower)
        {
            // �V�O�i���^���[
            var st = unitSetting.moveObject.AddComponent<SignalTowerScript>();
            st.SetParameter(unitSetting, unitSetting.towerSetting);
        }
        // �Z���T�����ݒ�
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
