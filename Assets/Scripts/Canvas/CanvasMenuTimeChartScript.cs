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

public class CanvasMenuTimeChartScript : CanvasMenuBaseScript
{
    private MachineTimeChart machineTimeChart;

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        var timeChartView = GetComponentsInChildren<Transform>().Where(d => d.name == "TimeChartView").ToList()[0];
        machineTimeChart = timeChartView.AddComponent<MachineTimeChart>();
        var text = machineTimeChart.transform.parent.GetComponentInChildren<TextMeshProUGUI>();
        machineTimeChart.SetParameter(text.font, TimeChartController.ChartMode.History);

        // TimeChartSettingのRectTransformを取得
        var settingRT = GetComponent<RectTransform>();  // または適切な取得方法

        // サイズ変化を購読してTimeChartSettingの幅を同期
        machineTimeChart.View.OnSizeChanged += (w, h) =>
        {
            if (settingRT != null)
                settingRT.sizeDelta = new Vector2(w, settingRT.sizeDelta.y);
        };
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void Update()
    {
        base.Update();
    }

    /// <summary>
    /// イベント登録
    /// </summary>
    public override void SetEvents()
    {
        base.SetEvents();
        // タイムチャートデータ表示
        machineTimeChart.SwitchHistoryData(true);
    }

    /// <summary>
    /// イベント解除
    /// </summary>
    public override void ResetEvents()
    {
        base.ResetEvents();
    }

    #region イベント処理
    #endregion イベント処理
}
