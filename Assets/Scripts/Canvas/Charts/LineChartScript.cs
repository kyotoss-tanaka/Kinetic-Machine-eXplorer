using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XCharts.Runtime;

public class LineChartScript : MonoBehaviour
{
    private LineChart chart;
    private Serie serie;

    private int maxCount = 1000;
    private long count = 0;

    void Start()
    {
        InitChart();
    }

    private void InitChart()
    {
        chart = gameObject.GetComponent<LineChart>();
        if (chart == null)
        {
            chart = gameObject.AddComponent<LineChart>();
            chart.Init();
        }
        chart.GetChartComponent<Title>().text = "タイトル";
        chart.GetChartComponent<Title>().subText = "サブタイトル";

        var yAxis = chart.GetChartComponent<YAxis>();
        yAxis.minMaxType = Axis.AxisMinMaxType.Custom;
        yAxis.min = 0;
        yAxis.max = 200;

        var xAxis = chart.GetChartComponent<XAxis>();
        xAxis.type = Axis.AxisType.Time;

        chart.RemoveData();
        serie = chart.AddSerie<Line>("Line");

        chart.SetMaxCache(maxCount);
    }

    public void SetValue(float value)
    {
        if (chart != null)
        {
            chart.AddData(0, count++, value);
        }
    }
}
