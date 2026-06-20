using KyotoSS.TimingChart.Example;
using SFB;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using KyotoSS.TimingChart;

public class CanvasMenuSysRecScript : CanvasMenuBaseScript
{
    private ComMcProtocol comMcProtocol;

    private Slider SysRecSlider;
    private Slider SysRecSpdSlider;
    private TextMeshProUGUI SysRecFileText;
    private TextMeshProUGUI SysRecPlayNowText;
    private TextMeshProUGUI SysRecPlayMaxNext;
    private TextMeshProUGUI SysRecDateTimeText;
    private TextMeshProUGUI SysRecSpdText;
    private TMP_InputField inputStep;
    private Button SysRecSelectBtn;
    private Button SysRecSpdBtn;
    private Button SysRecPlayBtn;
    private Button SysRecPrevBtn;
    private Button SysRecNextBtn;

    private MachineTimeChart machineTimeChart;

    private bool isRead = false;
    private int lapsWriteMode = 0;
    private System.Diagnostics.Stopwatch sw = new();
    private float spd = 1;
    private long laps = 0;
    private long lapsMax = 0;

    public bool IsEnabled
    {
        get
        {
            return comMcProtocol != null;
        }
    }

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        // コンポネント取得
        SysRecSlider = GetComponentsInChildren<Slider>().Where(d => d.name == "SysRecSlider").ToList()[0];
        SysRecSpdSlider = GetComponentsInChildren<Slider>().Where(d => d.name == "SysRecSpdSlider").ToList()[0];
        SysRecFileText = GetComponentsInChildren<TextMeshProUGUI>().Where(d => d.name == "SysRecFileText").ToList()[0];
        SysRecPlayNowText = GetComponentsInChildren<TextMeshProUGUI>().Where(d => d.name == "SysRecPlayNowText").ToList()[0];
        SysRecPlayMaxNext = GetComponentsInChildren<TextMeshProUGUI>().Where(d => d.name == "SysRecPlayMaxNext").ToList()[0];
        SysRecDateTimeText = GetComponentsInChildren<TextMeshProUGUI>().Where(d => d.name == "SysRecDateTimeText").ToList()[0];
        SysRecSpdText = GetComponentsInChildren<TextMeshProUGUI>().Where(d => d.name == "SysRecSpdText").ToList()[0];
        SysRecSelectBtn = GetComponentsInChildren<Button>().Where(d => d.name == "SysRecSelectBtn").ToList()[0];
        SysRecSpdBtn = GetComponentsInChildren<Button>().Where(d => d.name == "SysRecSpdBtn").ToList()[0];
        SysRecPlayBtn = GetComponentsInChildren<Button>().Where(d => d.name == "SysRecPlayBtn").ToList()[0];
        SysRecPrevBtn = GetComponentsInChildren<Button>().Where(d => d.name == "SysRecPrevBtn").ToList()[0];
        SysRecNextBtn = GetComponentsInChildren<Button>().Where(d => d.name == "SysRecNextBtn").ToList()[0];
        inputStep = GetComponentsInChildren<TMP_InputField>().Where(d => d.name == "SysRecStepText").ToList()[0];
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void Update()
    {
        base.Update();

        if (isRead)
        {
            if (sw.IsRunning)
            {
                laps += (long)(sw.ElapsedMilliseconds * SysRecSpdSlider.value);
                sw.Restart();
                if (lapsWriteMode == 0)
                {
                    lapsWriteMode = 1;
                    SysRecSlider.value = (float)laps;
                    lapsWriteMode = 0;
                }
            }
        }
    }

    /// <summary>
    /// イベント登録
    /// </summary>
    public override void SetEvents()
    {
        base.SetEvents();

        var comMcProtocols = GameObject.FindObjectsByType<ComMcProtocol>(FindObjectsSortMode.None).ToList();
        comMcProtocol = comMcProtocols.Count == 0 ? null : comMcProtocols[0];
        if (IsEnabled)
        {
            SysRecSlider.onValueChanged.AddListener(slider_onValueChanged);
            SysRecSpdSlider.onValueChanged.AddListener(spdSlider_onValueChanged);
            SysRecPlayBtn.onClick.AddListener(buttonPlay_onClick);
            SysRecSelectBtn.onClick.AddListener(buttonSelect_onClick);
            SysRecPrevBtn.onClick.AddListener(buttonPrev_onClick);
            SysRecNextBtn.onClick.AddListener(buttonNext_onClick);
            SysRecSpdBtn.onClick.AddListener(buttonSpd_onClick);
            inputStep.onValueChanged.AddListener(inputStep_onValueChanged);

            // 初期値セット
            SysRecSlider.maxValue = 10000;
            SysRecSlider.minValue = 0;
            SysRecSlider.value = 0;
            SysRecSpdSlider.maxValue = 5;
            SysRecSpdSlider.minValue = 0;
            SysRecSpdSlider.value = 1;
            inputStep.text = "10";

            SysRecSelectBtn.enabled = IsEnabled;
        }

    }

    /// <summary>
    /// イベント解除
    /// </summary>
    public override void ResetEvents()
    {
        base.ResetEvents();

        SysRecSlider.onValueChanged.RemoveAllListeners();
        SysRecSpdSlider.onValueChanged.RemoveAllListeners();
        SysRecPlayBtn.onClick.RemoveAllListeners();
        SysRecSelectBtn.onClick.RemoveAllListeners();
        SysRecPrevBtn.onClick.RemoveAllListeners();
        SysRecNextBtn.onClick.RemoveAllListeners();
        SysRecSpdBtn.onClick.RemoveAllListeners();
        inputStep.onValueChanged.RemoveAllListeners();
        sw.Stop();
    }

    /// <summary>
    /// 値を更新
    /// </summary>
    public void Reflesh()
    {
        slider_onValueChanged(spd);
    }
    #region イベント処理

    /// <summary>
    /// スライダー値変更イベント
    /// </summary>
    /// <param name="value"></param>
    private void slider_onValueChanged(float value)
    {
        if (isRead)
        {
            if (lapsWriteMode == 0)
            {
                lapsWriteMode = 2;
                laps = (long)value;
                lapsWriteMode = 0;
            }
            if (laps >= lapsMax)
            {
                laps = lapsMax;
                buttonPlay_onClick();
            }
            else if (laps <= 0)
            {
                laps = 0;
            }
            // 現在の時間
            GlobalScript.sysRecMilliseconds = laps;
            var now = SysRecReader.dtStart.Timestamp.AddMilliseconds(laps);
            var ts = now - SysRecReader.dtStart.Timestamp;
            SysRecPlayNowText.text = $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
            SysRecDateTimeText.text = now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            try
            {
                // データセット
                foreach (var mechData in GlobalScript.useDeviceDatas)
                {
                    foreach (var area in mechData.devices)
                    {
                        if (SysRecReader.recordDatas.ContainsKey(area.dev) && (SysRecReader.recordDatas[area.dev].tagInfo != null))
                        {
                            // 現在の時間の値取得
                            var recordData = SysRecReader.recordDatas[area.dev];
                            uint values = 0;
                            for (var i = 0; i < area.size; i++)
                            {
                                var index = recordData.Record.FindIndex(d => now > d.Timestamp) - 1;
                                if (index < 0)
                                {
                                    index = 0;
                                }
                                values += recordData.Record[index].Value << (16 * i);
                                recordData = recordData.Next;
                            }
                            SysRecReader.recordDatas[area.dev].tagInfo.Value = (int)values;
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// 速度スライダー値変更イベント
    /// </summary>
    /// <param name="value"></param>
    private void spdSlider_onValueChanged(float value)
    {
        spd = value;
        SysRecSpdText.text = spd.ToString("0.0");
    }

    /// <summary>
    /// 運転ボタン
    /// </summary>
    private void buttonPlay_onClick()
    {
        var text = SysRecPlayBtn.transform.GetComponentInChildren<TextMeshProUGUI>();
        if (sw.IsRunning)
        {
            text.text = "\ue037";
            sw.Stop();
        }
        else
        {
            text.text = "\ue034";
            if (laps == lapsMax)
            {
                laps = 0;
            }
            sw.Start();
        }
    }

    /// <summary>
    /// 参照ボタンクリック
    /// </summary>
    private void buttonSelect_onClick()
    {
        if (IsEnabled)
        {
            var paths = StandaloneFileBrowser.OpenFolderPanel("Open SysRec Folder", "", false);
            if (paths.Length > 0)
            {
                if (SysRecReader.ReadRecoderData(paths[0]))
                {
                    var ts = SysRecReader.dtEnd.Timestamp - SysRecReader.dtStart.Timestamp;
                    SysRecSlider.maxValue = (float)ts.TotalMilliseconds;
                    SysRecSlider.minValue = 0;
                    SysRecSlider.value = SysRecSlider.minValue;
                    SysRecFileText.text = SysRecReader.dtStart.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") + " - " + SysRecReader.dtEnd.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                    SysRecPlayNowText.text = "00:00.000";
                    SysRecPlayMaxNext.text = $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
                    SysRecDateTimeText.text = SysRecReader.dtStart.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");

                    // デバイス名からタグ取得
                    foreach (var mechData in GlobalScript.useDeviceDatas)
                    {
                        foreach (var area in mechData.devices)
                        {
                            if (SysRecReader.recordDatas.ContainsKey(area.dev))
                            {
                                SysRecReader.recordDatas[area.dev].tagInfo = GlobalScript.GetTagInfoFromDev(comMcProtocol.Name, mechData.mechId, area.dev);
                            }
                        }
                    }

                    // タイマー初期化
                    sw.Stop();
                    sw.Reset();
                    laps = 0;
                    lapsMax = (long)ts.TotalMilliseconds;
                    isRead = true;

                    // タイムチャートデータ表示
                    machineTimeChart.SwitchHistoryData(true);
                }
            }
        }
    }

    /// <summary>
    /// ボタンクリックイベント
    /// </summary>
    private void buttonPrev_onClick()
    {
        int value = 0;
        if (int.TryParse(inputStep.text, out value))
        {
            laps -= value;
            SysRecSlider.value = (float)laps;
        }
        var text = SysRecPlayBtn.transform.GetComponentInChildren<TextMeshProUGUI>();
        text.text = "\ue037";
        sw.Stop();
    }

    /// <summary>
    /// ボタンクリックイベント
    /// </summary>
    private void buttonNext_onClick()
    {
        int value = 0;
        if (int.TryParse(inputStep.text, out value))
        {
            laps += value;
            SysRecSlider.value = laps < 0 ? 0 : (float)laps;
        }
        var text = SysRecPlayBtn.transform.GetComponentInChildren<TextMeshProUGUI>();
        text.text = "\ue037";
        sw.Stop();
    }

    /// <summary>
    /// 速度リセットボタン
    /// </summary>
    private void buttonSpd_onClick()
    {
        SysRecSpdSlider.value = 1;
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
