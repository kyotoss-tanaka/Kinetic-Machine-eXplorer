using Oculus.Interaction;
using Parameters;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using XCharts.Runtime;
using static KssBaseScript;

public class XRCanvasScript : CanvasBaseScript
{
    private Canvas canvas;

    protected override void Start()
    {
        base.Start();

        // キャンバス取得
        canvas = GetComponent<Canvas>();
    }

    /// <summary>
    /// 有効時
    /// </summary>
    protected override void OnEnable()
    {
        base.OnEnable();
    }

    /// <summary>
    /// 無効時
    /// </summary>
    protected override void OnDisable()
    {
        base.OnDisable();
    }

    protected override void MyFixedUpdate()
    {
        base.MyFixedUpdate();
    }

    #region イベント
    #endregion イベント
}
