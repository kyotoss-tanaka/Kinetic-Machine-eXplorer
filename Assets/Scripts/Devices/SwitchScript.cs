using DnsClient.Protocol;
using Parameters;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public class SwitchScript : KssBaseScript
{
    /// <summary>
    /// �X�C�b�`�̓���^�C�v
    /// </summary>
    public enum SwitchType
    {
        TagOutput,
        ObjectClear,
        ModelVisible
    }

    /// <summary>
    /// �X�C�b�`�J���[
    /// </summary>
    public enum SwitchColor
    {
        Red, Green, Blue, Yellow
    }

    /// <summary>
    /// �I�u�W�F�N�g�N���A���[�h
    /// </summary>
    [SerializeField]
    private SwitchType switchType = SwitchType.TagOutput;

    /// <summary>
    /// �I�u�W�F�N�g�N���A���[�h
    /// </summary>
    [SerializeField]
    private SwitchColor switchColor = SwitchColor.Red;

    /// <summary>
    /// �^�O
    /// </summary>
    [SerializeField]
    private TagInfo Tag;

    /// <summary>
    /// �I���^�l�[�g���[�h
    /// </summary>
    [SerializeField]
    private bool isAlternate = false;

    /// <summary>
    /// �X�C�b�`�̏��
    /// </summary>
    private bool isOn = false;

    /// <summary>
    /// ����t���O
    /// </summary>
    private bool isFirst = true;

    /// <summary>
    /// �\�����f��
    /// </summary>
    private List<GameObject> lstVisible = new List<GameObject>();

    /// <summary>
    /// ���������g�����X�t�H�[��
    /// </summary>
    private Transform switchTransform;

    /// <summary>
    /// ���b�V�������_���[
    /// </summary>
    private MeshRenderer meshRenderer;

    /// <summary>
    /// VR�p�J����
    /// </summary>
    public Camera vrCamera;

    // Start is called before the first frame update
    protected override void Start()
    {
        switchTransform = transform.GetComponentsInChildren<Transform>().Where(d => d.name == "SwitchMain").ToList()[0];
        meshRenderer = switchTransform.GetComponent<MeshRenderer>();
        meshRenderer.material = Instantiate((Material)Resources.Load("Materials/Switch/" + switchColor.ToString()), switchTransform);
        var camera = GameObject.FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(d => d.name == "CenterEyeAnchor").ToList();
        if (camera.Count > 0)
        {
            vrCamera = camera[0];
        }
    }

    protected override void MyFixedUpdate()
    {
        base.MyFixedUpdate();
        if (isFirst)
        {
            // ���񏈗�
            isOn = ((Application.platform == RuntimePlatform.Android) || (Application.platform == RuntimePlatform.IPhonePlayer));
            RenewView();
            isFirst = false;
        }
    }

    /// <summary>
    /// ��Ń^�b�v
    /// </summary>
    /// <param name="other"></param>
    protected override void OnTriggerEnter(Collider other)
    {
        var parent = other.transform.parent;
        if (parent != null)
        {
            if (parent.name.Contains("PinchPoint"))
            {
                OnMouseDown();
            }
        }
    }

    /// <summary>
    /// ��Ń^�b�v
    /// </summary>
    /// <param name="other"></param>
    protected override void OnTriggerExit(Collider other)
    {
        var parent = other.transform.parent;
        if (parent != null)
        {
            if (parent.name.Contains("PinchPoint"))
            {
                OnMouseUp();
            }
        }
    }

    /// <summary>
    /// �}�E�X�_�E��
    /// </summary>
    public override void OnMouseDown()
    {
        if (isAlternate)
        {
            isOn = !isOn;
        }
        else
        {
            isOn = true;
        }
        RenewView();
    }

    /// <summary>
    /// �}�E�X�A�b�v
    /// </summary>
    public override void OnMouseUp()
    {
        if (!isAlternate)
        {
            isOn = false;
            RenewView();
        }
    }

    /// <summary>
    /// �X�C�b�`����
    /// </summary>
    private void SwitchProcess()
    {
        if (switchType == SwitchType.TagOutput)
        {
            GlobalScript.SetTagData(Tag, isOn ? 1 : 0);
        }
        else if (switchType == SwitchType.ObjectClear)
        {
            foreach (var obj in GameObject.FindObjectsByType<ObjectScript>(FindObjectsSortMode.None))
            {
                Destroy(obj.gameObject);
            }
        }
        else if (switchType == SwitchType.ModelVisible)
        {
            foreach (var obj in lstVisible)
            {
                obj.gameObject.SetActive(!isOn);
            }
        }
    }

    private void RenewView()
    {
        // �X�C�b�`�̌����ڂ�ς���
        if (isOn)
        {
            meshRenderer.material.EnableKeyword("_EMISSION");
        }
        else
        {
            meshRenderer.material.DisableKeyword("_EMISSION");
        }

        switchTransform.localPosition = new Vector3
        {
            x = 0,
            y = isOn ? 0.005f : 0.012f,
            z = 0
        };

        // ����
        SwitchProcess();
    }

    /// <summary>
    /// �p�����[�^�Z�b�g
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="obj"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);

        var sw = (SwitchSetting)obj;
        isAlternate = sw.alternate;
        if (sw.color == "Green")
        {
            switchColor = SwitchColor.Green;
        }
        else if (sw.color == "Blue")
        {
            switchColor = SwitchColor.Blue;
        }
        else if (sw.color == "Yellow")
        {
            switchColor = SwitchColor.Yellow;
        }
        if (sw.mode == 0)
        {
            switchType = SwitchType.TagOutput;
            if (sw.tag != null)
            {
                Tag = ScriptableObject.CreateInstance<TagInfo>();
                Tag.Database = unitSetting.Database;
                Tag.MechId = unitSetting.mechId;
                Tag.Tag = sw.tag;
            }
        }
        else if (sw.mode == 1)
        {
            switchType = SwitchType.ObjectClear;
        }
        else if (sw.mode == 2)
        {
            switchType = SwitchType.ModelVisible;
            if (sw.tag != null)
            {
                lstVisible = new();
                if (sw.tag == "")
                {
                    // �����͂̏ꍇ�̓v���n�u���f��
                    lstVisible.AddRange(GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(d => d.name == "PrefabObjects").ToList());
                }
                else
                {
                    // �J���}��؂�Ŕ�\�����f�����`
                    foreach (var name in sw.tag.Split(","))
                    {
                        lstVisible.AddRange(GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(d => d.name == name).ToList());
                    }
                }
            }
        }
    }
}
