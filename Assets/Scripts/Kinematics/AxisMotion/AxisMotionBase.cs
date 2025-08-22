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
    /// �T�C�N���^�O
    /// </summary>
    protected TagInfo cycleTag;

    /// <summary>
    /// ���삠��
    /// </summary>
    public bool isAction
    {
        get
        {
            return (unitSetting != null) && (unitSetting.actionSetting != null);
        }
    }

    /// <summary>
    /// �I�u�W�F�N�g�`�󂠂�
    /// </summary>
    public bool isShape
    {
        get
        {
            return (unitSetting != null) && (unitSetting.shapeSetting != null);
        }
    }

    /// <summary>
    /// �z������
    /// </summary>
    public bool isSuction
    {
        get
        {
            return (unitSetting != null) && (unitSetting.suctionSetting != null);
        }
    }

    /// <summary>
    /// ���[�N��������
    /// </summary>
    public bool isWorkCreate
    {
        get
        {
            return (unitSetting != null) && (unitSetting.workSettings.Count > 0);
        }
    }

    /// <summary>
    /// ���[�N�폜����
    /// </summary>
    public bool isWorkDelete
    {
        get
        {
            return (unitSetting != null) && (unitSetting.workDeleteSettings.Count > 0);
        }
    }

    /// <summary>
    /// �X�C�b�`
    /// </summary>
    public bool isSwitch
    {
        get
        {
            return (unitSetting != null) && (unitSetting.switchSetting != null);
        }
    }

    /// <summary>
    /// �V�O�i���^���[
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
    /// �@�\�g���ݒ�
    /// </summary>
    public bool isExMech
    {
        get
        {
            return (unitSetting != null) && (unitSetting.exMechSetting != null) && (unitSetting.exMechSetting.datas.Count > 0);
        }
    }

    /// <summary>
    /// ��]����
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
            // ���j�b�g���X�V
            RenewMoveDir();

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
            // �q�I�u�W�F�N�g�̃`���b�N���j�b�g���ړ�����K�v������
            var motion = child.GetComponent<AxisMotionBase>();
            if (motion != null)
            {
                motion.SetChuckParent();
            }
        }
        // �`���b�N�I�u�W�F�N�g�ݒ�
        if (chuckSetting != null)
        {
            foreach (var chuck in chuckSetting.children)
            {
                // ��U���j�b�g�̐e�q�֌W�𐶐�
                if (chuck.setting.moveObject != null)
                {
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
                else
                {
                }
            }
        }
        // �Փ˃Z�b�g
        SetCollision(unitSetting);
    }

    /// <summary>
    /// ���j�b�g�ݒ肩�瓮��ݒ�X�V
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
            if (!GlobalScript.buildConfig.isCollision && unitSetting.isCollision)
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

                if ((Application.platform == RuntimePlatform.Android) || (Application.platform == RuntimePlatform.IPhonePlayer))
                {
                    // VR�ł͖���
                }
                else
                {
                    // Windows�ł�Collider�쐬
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
    /// ���b�V����3D���`�F�b�N
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

        // �ŏ��ł�3�����ɂ�����x�̍L���肪�Ȃ��Ɠʕ�͎��s����\��
        return (thicknessX > 1e-4f && thicknessY > 1e-4f && thicknessZ > 1e-4f);
    }

    /// <summary>
    /// ���݂�������
    /// </summary>
    /// <param name="mesh"></param>
    /// <param name="offset"></param>
    private void AddFakeThickness(UnityEngine.Mesh mesh, float offset = 0.0001f)
    {
        Vector3[] verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++)
        {
            verts[i].z += UnityEngine.Random.Range(-offset, offset); // Z�����Ɍ���
        }
        mesh.vertices = verts;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
    }

    /// <summary>
    /// �`���b�N���j�b�g�̐e��ݒ肷��
    /// </summary>
    public void SetChuckParent()
    {
        // �`���b�N�I�u�W�F�N�g�ݒ�
        if (chuckSetting != null)
        {
            foreach (var chuck in chuckSetting.children)
            {
                // �����Ɠ����e��
                chuck.setting.unitObject.transform.parent = transform.parent;
            }
        }
    }

    /// <summary>
    /// �`���b�N�ݒ���s��
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
    /// �@�\�g���ݒ�
    /// </summary>
    private void SetExMechSetting()
    {
        // ���j�b�g�ǉ�
        var exObj = new GameObject(unitSetting.name + "(ExMech)");
        exObj.transform.parent = unitSetting.unitObject.transform;
        // �e�q�֌W�ݒ�
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
                Debug.Log($"�G���[�F���j�b�g���u{unitSetting.name}�v�̊g�����f�������݂��܂���B");
                return;
            }
        }
        var ex = unitSetting.exMechSetting.datas[0].gameObject.AddComponent<ExMechScript>();
        ex.SetParameter(unitSetting, unitSetting.exMechSetting);
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

        // ���j�b�g�ݒ�
        RenewUnitSetting();
    }

    /// <summary>
    /// ����ݒ�
    /// </summary>
    /// <param name="unitSetting"></param>

    public virtual void RenewUnitSetting(bool reload = false)
    {
        // �R���C�_�[��2�o�^����̂��ߍ폜
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
        // �`��ݒ�
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
        // �z���ݒ�
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
        // ���[�N�����ݒ�
        if (isWorkCreate)
        {
            // ���[�N�����ݒ肠��
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
        // ���[�N�폜�ݒ�
        if (isWorkDelete)
        {
            // ���[�N�폜�ݒ肠��
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
        // �X�C�b�`�ݒ�
        if (isSwitch)
        {
            // �X�C�b�`
            var sw = unitSetting.moveObject.GetComponent<SwitchScript>();
            if (sw != null)
            {
                Destroy(sw);
            }
            sw = unitSetting.moveObject.AddComponent<SwitchScript>();
            sw.SetParameter(unitSetting, unitSetting.switchSetting);
        }
        // �V�O�i���^���[�ݒ�
        if (isSignalTower)
        {
            // �V�O�i���^���[
            var st = unitSetting.moveObject.GetComponent<SignalTowerScript>();
            if (st != null)
            {
                Destroy(st);
            }
            st = unitSetting.moveObject.AddComponent<SignalTowerScript>();
            st.SetParameter(unitSetting, unitSetting.towerSetting);
        }
        // LED�ݒ�
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
            // �@�\�g���ݒ�
            if (isExMech)
            {
                // �@�\�g��
                SetExMechSetting();
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
}
