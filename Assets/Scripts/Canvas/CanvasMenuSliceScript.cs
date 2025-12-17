using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.Windows;

public class CanvasMenuSliceScript : CanvasMenuBaseScript
{
    private GameObject slicePlane;
    private Toggle viewXToggle;
    private Toggle viewYToggle;
    private Toggle viewZToggle;
    private Toggle viewRvsToggle;
    private Slider viewSlider;
    private TextMeshProUGUI viewText;

    // シェーダー
    private HashSet<Material> allMaterials = new HashSet<Material>();
    private Shader opaqueDanmen;
    private Shader transparentDanmen;
    private Shader opaqueShader;
    private Shader transparentShader;
    private Shader standardShader;

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        slicePlane = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(d => d.name == "SlicePlane").ToList()[0];

        viewXToggle = GetComponentsInChildren<Toggle>().ToList().Find(d => d.name == "ClipXToggle");
        viewYToggle = GetComponentsInChildren<Toggle>().ToList().Find(d => d.name == "ClipYToggle");
        viewZToggle = GetComponentsInChildren<Toggle>().ToList().Find(d => d.name == "ClipZToggle");
        viewRvsToggle = GetComponentsInChildren<Toggle>().ToList().Find(d => d.name == "ClipRvsToggle");
        viewSlider = GetComponentInChildren<Slider>();
        viewText = GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "ClipText");

        opaqueDanmen = Shader.Find("Shader Graphs/OpaqueDANMEN");
        transparentDanmen = Shader.Find("Shader Graphs/TransparentDANMEN");
        opaqueShader = Shader.Find("Shader Graphs/OpaqueShader");
        transparentShader = Shader.Find("Shader Graphs/TransparentShader");
    }

    /// <summary>
    /// 有効時
    /// </summary>
    protected override void OnEnable()
    {
        base.OnEnable();
        viewXToggle.onValueChanged.AddListener(clipToggle_onValueChanged);
        viewYToggle.onValueChanged.AddListener(clipToggle_onValueChanged);
        viewZToggle.onValueChanged.AddListener(clipToggle_onValueChanged);
        viewRvsToggle.onValueChanged.AddListener(clipToggle_onValueChanged);
        viewSlider.onValueChanged.AddListener(clipSlider_onValueChanged);
        GlobalScript.clipInfo.isOn = true;
        clipToggle_onValueChanged(true);
    }

    /// <summary>
    /// 無効時
    /// </summary>
    protected override void OnDisable()
    {
        base.OnDisable();
        viewXToggle.onValueChanged.RemoveAllListeners();
        viewYToggle.onValueChanged.RemoveAllListeners();
        viewZToggle.onValueChanged.RemoveAllListeners();
        viewRvsToggle.onValueChanged.RemoveAllListeners();
        viewSlider.onValueChanged.RemoveAllListeners();
        GlobalScript.clipInfo.isOn = false;
        UpdateClip();
    }

    /// <summary>
    /// イベントセット
    /// </summary>
    public virtual void SetEvents(HashSet<Material> allMaterials)
    {
        this.allMaterials = allMaterials;
        standardShader = allMaterials.Count > 0 ? allMaterials.First().shader : Shader.Find("Universal Render Pipeline/Lit");

        SetEvents();
    }

    /// <summary>
    /// イベントリセット
    /// </summary>
    public override void ResetEvents()
    {
        base.ResetEvents();
    }

    /// <summary>
    /// 断面トグル変更イベント
    /// </summary>
    /// <param name="value"></param>
    public void clipToggle_onValueChanged(bool value)
    {
        // 有効/無効
        viewXToggle.enabled = GlobalScript.clipInfo.isOn;
        viewYToggle.enabled = GlobalScript.clipInfo.isOn;
        viewZToggle.enabled = GlobalScript.clipInfo.isOn;
        viewRvsToggle.enabled = GlobalScript.clipInfo.isOn;
        viewSlider.enabled = GlobalScript.clipInfo.isOn;
        // 範囲変更
        if (viewXToggle.isOn)
        {
            // Xに変更
            viewSlider.minValue = GlobalScript.clipInfo.bounds.min.x;
            viewSlider.maxValue = GlobalScript.clipInfo.bounds.max.x;
            viewSlider.value = GlobalScript.clipInfo.x;
            slicePlane.transform.localEulerAngles = new Vector3(0, 0, viewRvsToggle.isOn ? -90 : 90);
        }
        else if (viewYToggle.isOn)
        {
            // Yに変更
            viewSlider.minValue = GlobalScript.clipInfo.bounds.min.y;
            viewSlider.maxValue = GlobalScript.clipInfo.bounds.max.y;
            viewSlider.value = GlobalScript.clipInfo.y;
            slicePlane.transform.localEulerAngles = new Vector3(viewRvsToggle.isOn ? 180 : 0, 0, 0);
        }
        else if (viewZToggle.isOn)
        {
            // Zに変更
            viewSlider.minValue = GlobalScript.clipInfo.bounds.min.z;
            viewSlider.maxValue = GlobalScript.clipInfo.bounds.max.z;
            viewSlider.value = GlobalScript.clipInfo.z;
            slicePlane.transform.localEulerAngles = new Vector3(viewRvsToggle.isOn ? 90 : -90, 0, 0);
        }
        UpdateClip();
    }

    /// <summary>
    /// 断面スライダー値変更イベント
    /// </summary>
    /// <param name="value"></param>
    private void clipSlider_onValueChanged(float value)
    {
        // 値セット
        if (viewXToggle.isOn)
        {
            GlobalScript.clipInfo.x = value;
        }
        else if (viewYToggle.isOn)
        {
            GlobalScript.clipInfo.y = value;
        }
        else if (viewZToggle.isOn)
        {
            GlobalScript.clipInfo.z = value;
        }
        viewText.text = value.ToString("0.00");
        UpdateClip();
    }

    /// <summary>
    /// 断面更新
    /// </summary>
    private void UpdateClip()
    {
        if (GlobalScript.clipInfo.isOn)
        {
            // シェーダー切り替え
            foreach (Material mat in allMaterials)
            {
                if (mat.shader.name.Contains("Transparent"))
                {
                    mat.shader = transparentDanmen;
                }
                else
                {
                    mat.shader = opaqueDanmen;
                }
            }
            slicePlane.transform.transform.localPosition = new Vector3(GlobalScript.clipInfo.x, GlobalScript.clipInfo.y, GlobalScript.clipInfo.z);
        }
        else
        {
            // シェーダー通常
            foreach (Material mat in allMaterials)
            {
                if (mat.shader.name.Contains("Transparent"))
                {
                    mat.shader = transparentShader;
                }
                else
                {
                    mat.shader = opaqueShader;
                }
            }
            slicePlane.transform.transform.localPosition = Vector3.zero;
            slicePlane.transform.localEulerAngles = Vector3.zero;
        }
    }
}
