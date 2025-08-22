using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.Windows;

public class CanvasMenuViewScript : CanvasMenuBaseScript
{
    private Toggle viewCollision;
    private Toggle viewClip;
    private Toggle viewXToggle;
    private Toggle viewYToggle;
    private Toggle viewZToggle;
    private Toggle viewRvsToggle;
    private Slider viewSlider;
    private TextMeshProUGUI viewText;

    // シェーダー
    private HashSet<Material> allMaterials = new HashSet<Material>();
    private Shader clipShader;
    private Shader standardShader;

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        viewCollision = GetComponentsInChildren<Toggle>().ToList().Find(d => d.name == "ColliderToggle");
        viewClip = GetComponentsInChildren<Toggle>().ToList().Find(d => d.name == "ClipToggle");
        viewXToggle = GetComponentsInChildren<Toggle>().ToList().Find(d => d.name == "ClipXToggle");
        viewYToggle = GetComponentsInChildren<Toggle>().ToList().Find(d => d.name == "ClipYToggle");
        viewZToggle = GetComponentsInChildren<Toggle>().ToList().Find(d => d.name == "ClipZToggle");
        viewRvsToggle = GetComponentsInChildren<Toggle>().ToList().Find(d => d.name == "ClipRvsToggle");
        viewSlider = GetComponentInChildren<Slider>();
        viewText = GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "ClipText");
    }

    /// <summary>
    /// イベントセット
    /// </summary>
    public virtual void SetEvents(HashSet<Material> allMaterials, Shader standardShader, Shader clipShader)
    {
        this.allMaterials = allMaterials;
        this.standardShader = standardShader;
        this.clipShader = clipShader;

        SetEvents();
        viewCollision.onValueChanged.AddListener(collisionToggle_onValueChanged);
        viewClip.onValueChanged.AddListener(clipToggle_onValueChanged);
        viewXToggle.onValueChanged.AddListener(clipToggle_onValueChanged);
        viewYToggle.onValueChanged.AddListener(clipToggle_onValueChanged);
        viewZToggle.onValueChanged.AddListener(clipToggle_onValueChanged);
        viewRvsToggle.onValueChanged.AddListener(clipToggle_onValueChanged);
        viewSlider.onValueChanged.AddListener(clipSlider_onValueChanged);
    }

    /// <summary>
    /// イベントリセット
    /// </summary>
    public override void ResetEvents()
    {
        base.ResetEvents();
        viewCollision.onValueChanged.RemoveAllListeners();
        viewClip.onValueChanged.RemoveAllListeners();
        viewXToggle.onValueChanged.RemoveAllListeners();
        viewYToggle.onValueChanged.RemoveAllListeners();
        viewZToggle.onValueChanged.RemoveAllListeners();
        viewRvsToggle.onValueChanged.RemoveAllListeners();
        viewSlider.onValueChanged.RemoveAllListeners();
    }

    /// <summary>
    /// 衝突検知トグル変更イベント
    /// </summary>
    /// <param name="value"></param>
    private void collisionToggle_onValueChanged(bool value)
    {
        // 衝突
        GlobalScript.isCollision = value;
    }

    /// <summary>
    /// 断面トグル変更イベント
    /// </summary>
    /// <param name="value"></param>
    public void clipToggle_onValueChanged(bool value)
    {
        // 有効/無効
        viewXToggle.enabled = viewClip.isOn;
        viewYToggle.enabled = viewClip.isOn;
        viewZToggle.enabled = viewClip.isOn;
        viewRvsToggle.enabled = viewClip.isOn;
        viewSlider.enabled = viewClip.isOn;
        // 範囲変更
        if (viewXToggle.isOn)
        {
            // Xに変更
            viewSlider.minValue = GlobalScript.clipInfo.bounds.min.x;
            viewSlider.maxValue = GlobalScript.clipInfo.bounds.max.x;
            viewSlider.value = GlobalScript.clipInfo.x;
        }
        else if (viewYToggle.isOn)
        {
            // Yに変更
            viewSlider.minValue = GlobalScript.clipInfo.bounds.min.y;
            viewSlider.maxValue = GlobalScript.clipInfo.bounds.max.y;
            viewSlider.value = GlobalScript.clipInfo.y;
        }
        else if (viewZToggle.isOn)
        {
            // Zに変更
            viewSlider.minValue = GlobalScript.clipInfo.bounds.min.z;
            viewSlider.maxValue = GlobalScript.clipInfo.bounds.max.z;
            viewSlider.value = GlobalScript.clipInfo.z;
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
        var clipInfo = GlobalScript.clipInfo;
        // 平面の向き
        Vector3 planeNormal = Vector3.down;
        // 平面が通る点
        Vector3 planePoint = Vector3.zero;
        // 削除済みマテリアルを削除
        allMaterials.RemoveWhere(d => d.IsDestroyed());
        if (viewClip.isOn)
        {
            if (viewRvsToggle.isOn)
            {
                planeNormal = viewXToggle.isOn ? Vector3.left : (viewYToggle.isOn ? Vector3.down : Vector3.back);
            }
            else
            {
                planeNormal = viewXToggle.isOn ? Vector3.right : (viewYToggle.isOn ? Vector3.up : Vector3.forward);
            }
            planePoint = new Vector3(viewXToggle.isOn ? clipInfo.x : 0, viewYToggle.isOn ? clipInfo.y : 0, viewZToggle.isOn ? clipInfo.z : 0);
            // シェーダー切り替え
            foreach (Material mat in allMaterials)
            {
                mat.shader = clipShader;
            }
        }
        else
        {
            // シェーダー通常
            foreach (Material mat in allMaterials)
            {
                mat.shader = standardShader;
            }
        }
        Vector4 clipPlane = new Vector4(planeNormal.x, planeNormal.y, planeNormal.z, -Vector3.Dot(planeNormal, planePoint));
        Shader.SetGlobalVector("_ClipPlane", clipPlane);
    }
}
