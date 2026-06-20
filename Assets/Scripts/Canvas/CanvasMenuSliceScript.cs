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
    private Toggle viewXToggle;
    private Toggle viewYToggle;
    private Toggle viewZToggle;
    private Toggle viewRvsToggle;
    private Slider viewSlider;
    private TextMeshProUGUI viewText;

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        viewXToggle = GetComponentsInChildren<Toggle>().ToList().Find(d => d.name == "ClipXToggle");
        viewYToggle = GetComponentsInChildren<Toggle>().ToList().Find(d => d.name == "ClipYToggle");
        viewZToggle = GetComponentsInChildren<Toggle>().ToList().Find(d => d.name == "ClipZToggle");
        viewRvsToggle = GetComponentsInChildren<Toggle>().ToList().Find(d => d.name == "ClipRvsToggle");
        viewSlider = GetComponentInChildren<Slider>();
        viewText = GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "ClipText");
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
        GlobalScript.clipInfo.mode = GlobalScript.ClipInfo.SlideMode.None;
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
        GlobalScript.clipInfo.mode = viewXToggle.isOn ? GlobalScript.ClipInfo.SlideMode.X : (viewYToggle.isOn ? GlobalScript.ClipInfo.SlideMode.Y : GlobalScript.ClipInfo.SlideMode.Z);
        GlobalScript.clipInfo.isRvs = viewRvsToggle.isOn;
        GlobalScript.clipInfo.value = viewSlider.value;
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
        GlobalScript.clipInfo.value = value;
        viewText.text = value.ToString("0.00");
    }
}
