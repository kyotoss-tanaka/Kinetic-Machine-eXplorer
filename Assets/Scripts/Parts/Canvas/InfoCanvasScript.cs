using Parameters;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using XCharts.Runtime;
using static KssBaseScript;

public class InfoCanvasScript : CanvasBaseScript
{
    private Canvas canvas;
    private InfoCanvasButtonScript buttonScript;
    private LineChartScript chartScript;

    private Transform chartTransform;

    protected override void Start()
    {
        base.Start();

        // �L�����o�X�擾
        canvas = GetComponent<Canvas>();

        // �{�^���X�N���v�g�擾
        buttonScript = transform.GetComponentInChildren<InfoCanvasButtonScript>();

        // �`���[�g�\��
        var c = (GameObject)Resources.Load("Charts/LineChart_Time");
        chartScript = Instantiate(c).GetComponent<LineChartScript>();
        chartScript.transform.parent = canvas.transform;
        chartScript.transform.localPosition = Vector3.zero;
        chartScript.transform.localEulerAngles = Vector3.zero;
        chartScript.gameObject.SetActive(false);
    }

    public override void SetUnitSetting(UnitSetting unitSetting)
    {
        base.SetUnitSetting(unitSetting);
        chartTransform = unitSetting.moveObject.transform;
    }

    protected override void MyFixedUpdate()
    {
        base.MyFixedUpdate();

        chartScript.gameObject.SetActive(buttonScript.isOpen);
        if (buttonScript.isOpen && (chartTransform != null))
        {
            chartScript.SetValue(Vector3.Distance(chartTransform.localPosition, Vector3.zero) * 1000);
        }
    }

    public void SetValues(Dictionary<string, CanvasValue> values)
    {
        if (buttonScript.isOpen)
        {
            buttonScript.SetValues(unitSetting.name, values);
        }
    }

}
