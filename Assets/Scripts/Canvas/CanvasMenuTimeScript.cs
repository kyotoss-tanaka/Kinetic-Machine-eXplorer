using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class CanvasMenuTimeScript : CanvasMenuBaseScript
{
    private ComInner comInner;

    private Toggle toggle;
    private Slider slider;
    private TextMeshProUGUI text;
    private Button button;
    private TMP_InputField input;
    private TextMeshProUGUI cycle;
    private TMP_InputField inputStep;
    private Button buttonNext;
    private Button buttonPrev;

    public bool IsEnabled
    {
        get
        {
            return comInner != null;
        }
    }

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        // コンポネント取得
        toggle = GetComponentInChildren<Toggle>();
        slider = GetComponentInChildren<Slider>();
        text = GetComponentsInChildren<TextMeshProUGUI>().Where(d => d.name == "ComInnerText").ToList()[0];
        button = GetComponentsInChildren<Button>().Where(d => d.name == "ComInnerButton").ToList()[0];
        input = GetComponentsInChildren<TMP_InputField>().Where(d => d.name == "ComInnerInput").ToList()[0];
        cycle = GetComponentsInChildren<TextMeshProUGUI>().Where(d => d.name == "ComInnerCycle").ToList()[0]; ;
        buttonPrev = GetComponentsInChildren<Button>().Where(d => d.name == "ComInnerPrevButton").ToList()[0];
        buttonNext = GetComponentsInChildren<Button>().Where(d => d.name == "ComInnerNextButton").ToList()[0];
        inputStep = GetComponentsInChildren<TMP_InputField>().Where(d => d.name == "ComInnerStep").ToList()[0];
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void Update()
    {
        base.Update();

        // 表示セット
        if (comInner != null)
        {
            cycle.text = $"Cycle Time : {(comInner.time % comInner.viewCycle)} msec";
        }
    }

    /// <summary>
    /// イベント登録
    /// </summary>
    public override void SetEvents()
    {
        base.SetEvents();

        var comInners = GameObject.FindObjectsByType<ComInner>(FindObjectsSortMode.None).ToList();
        comInner = comInners.Count == 0 ? null : comInners[0];
        if (comInner != null)
        {
            toggle.onValueChanged.AddListener(toggle_onValueChanged);
            slider.onValueChanged.AddListener(slider_onValueChanged);
            button.onClick.AddListener(button_onClick);
            input.onValueChanged.AddListener(input_onValueChanged);
            buttonPrev.onClick.AddListener(buttonPrev_onClick);
            buttonNext.onClick.AddListener(buttonNext_onClick);
            inputStep.onValueChanged.AddListener(inputStep_onValueChanged);

            // 初期値セット
            toggle.isOn = false;
            slider.value = 1;
            slider.maxValue = 5;
            slider.minValue = 0;
            comInner.viewCycle = comInner.acts.Count > 0 ? comInner.acts[0].cycle : 1000;
            input.text = comInner.viewCycle.ToString();
            inputStep.text = "10";
        }
    }

    /// <summary>
    /// イベント解除
    /// </summary>
    public override void ResetEvents()
    {
        base.ResetEvents();

        toggle.onValueChanged.RemoveAllListeners();
        slider.onValueChanged.RemoveAllListeners();
        button.onClick.RemoveAllListeners();
        input.onValueChanged.RemoveAllListeners();
    }

    #region イベント処理
    /// <summary>
    /// トグル変更イベント
    /// </summary>
    /// <param name="value"></param>
    private void toggle_onValueChanged(bool value)
    {
        comInner.isStop = value;
    }

    /// <summary>
    /// スライダー値変更イベント
    /// </summary>
    /// <param name="value"></param>
    private void slider_onValueChanged(float value)
    {
        comInner.timeRate = value;
        text.text = value.ToString("0.00");
    }

    /// <summary>
    /// ボタンクリックイベント
    /// </summary>
    private void button_onClick()
    {
        slider.value = 1;
    }

    /// <summary>
    /// 値取得
    /// </summary>
    /// <param name="text"></param>
    private void input_onValueChanged(string text)
    {
        int value = 0;
        if (int.TryParse(text, out value))
        {
            if (value <= 0)
            {
                input.text = comInner.acts.Count > 0 ? comInner.acts[0].cycle.ToString() : "1000";
            }
            else
            {
                comInner.viewCycle = value;
            }
        }
    }

    /// <summary>
    /// ボタンクリックイベント
    /// </summary>
    private void buttonPrev_onClick()
    {
        toggle.isOn = true;
        int value = 0;
        if (int.TryParse(inputStep.text, out value))
        {
            comInner.step = -value;
        }
    }

    /// <summary>
    /// ボタンクリックイベント
    /// </summary>
    private void buttonNext_onClick()
    {
        toggle.isOn = true;
        int value = 0;
        if (int.TryParse(inputStep.text, out value))
        {
            comInner.step = value;
        }
    }

    /// <summary>
    /// 値取得
    /// </summary>
    /// <param name="text"></param>
    private void inputStep_onValueChanged(string text)
    {
        int value = 0;
        if (int.TryParse(text, out value))
        {
            if (value <= 0)
            {
                inputStep.text = "1";
            }
        }
    }
    #endregion イベント処理
}
