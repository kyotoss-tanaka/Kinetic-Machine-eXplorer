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
    /// スイッチの動作タイプ
    /// </summary>
    public enum SwitchType
    {
        TagOutput,
        ObjectClear,
        ModelVisible
    }

    /// <summary>
    /// スイッチカラー
    /// </summary>
    public enum SwitchColor
    {
        Red, Green, Blue, Yellow
    }

    /// <summary>
    /// オブジェクトクリアモード
    /// </summary>
    [SerializeField]
    private SwitchType switchType = SwitchType.TagOutput;

    /// <summary>
    /// オブジェクトクリアモード
    /// </summary>
    [SerializeField]
    private SwitchColor switchColor = SwitchColor.Red;

    /// <summary>
    /// タグ
    /// </summary>
    [SerializeField]
    private TagInfo Tag;

    /// <summary>
    /// オルタネートモード
    /// </summary>
    [SerializeField]
    private bool isAlternate = false;

    /// <summary>
    /// スイッチの状態
    /// </summary>
    private bool isOn = false;

    /// <summary>
    /// 初回フラグ
    /// </summary>
    private bool isFirst = true;

    /// <summary>
    /// 表示モデル
    /// </summary>
    private List<GameObject> lstVisible = new List<GameObject>();

    /// <summary>
    /// 操作をするトランスフォーム
    /// </summary>
    private Transform switchTransform;

    /// <summary>
    /// メッシュレンダラー
    /// </summary>
    private MeshRenderer meshRenderer;

    /// <summary>
    /// VR用カメラ
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
            // 初回処理
            isOn = ((Application.platform == RuntimePlatform.Android) || (Application.platform == RuntimePlatform.IPhonePlayer));
            RenewView();
            isFirst = false;
        }
    }

    /// <summary>
    /// 手でタップ
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
    /// 手でタップ
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
    /// マウスダウン
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
    /// マウスアップ
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
    /// スイッチ処理
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
        // スイッチの見た目を変える
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

        // 処理
        SwitchProcess();
    }

    /// <summary>
    /// パラメータセット
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
                    // 未入力の場合はプレハブモデル
                    lstVisible.AddRange(GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(d => d.name == "PrefabObjects").ToList());
                }
                else
                {
                    // カンマ区切りで非表示モデルを定義
                    foreach (var name in sw.tag.Split(","))
                    {
                        lstVisible.AddRange(GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(d => d.name == name).ToList());
                    }
                }
            }
        }
    }
}
