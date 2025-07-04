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
    protected TagInfo? cycleTag;

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

                if ((Application.platform == RuntimePlatform.Android) || (Application.platform == RuntimePlatform.IPhonePlayer))
                {
                    // VR�ł͖���
                }
                else
                {
                    // Windows�ł�Collider�쐬
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
                                    Debug.LogWarning($"Convex�ݒ�Ɏ��s: {col.name}, ���R: {ex.Message}");
                                    col.convex = false;
                                    col.isTrigger = false;
                                }
                            }
                            else
                            {
//                                Debug.Log($"convex�X�L�b�v: {col.name}, triangle: {triangleCount}, thickness: {thickness}");
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning($"Convex�ݒ�Ɏ��s: {col.name}, ���R: {ex.Message}");
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
