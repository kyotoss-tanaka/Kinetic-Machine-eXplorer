using NUnit.Framework;
using Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.Windows;
using static System.Net.Mime.MediaTypeNames;

public class CanvasMenuSettingScript : CanvasMenuBaseScript
{
    private TextMeshProUGUI fpsText;
    private TextMeshProUGUI timeText;
    private Toggle useLiensToggle;
    private Toggle usePhysicsToggle;
    private Toggle useColliderToggle;
    private List<float> times = new();
    private List<float> fpss = new();
    private float fpsRefreshTimer;   // FPS表示の更新間引き用

    #region 初期化処理
    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        fpsText = GetComponentsInChildren<TextMeshProUGUI>().Where(d => d.name == "FpsText").ToList()[0];
        timeText = GetComponentsInChildren<TextMeshProUGUI>().Where(d => d.name == "TimeText").ToList()[0];
        useLiensToggle = GetComponentsInChildren<Toggle>().ToList().Find(d => d.name == "UseLinesToggle");
        usePhysicsToggle = GetComponentsInChildren<Toggle>().ToList().Find(d => d.name == "UsePhysicsToggle");
        useColliderToggle = GetComponentsInChildren<Toggle>().ToList().Find(d => d.name == "UseColliderToggle");
    }

    /// <summary>
    /// 初期化処理
    /// </summary>
    private void Initialize()
    {
    }

    /// <summary>
    /// 有効時
    /// </summary>
    protected override void OnEnable()
    {
        base.OnEnable();
        useLiensToggle.onValueChanged.AddListener(useLiensToggle_onValueChanged);
        usePhysicsToggle.onValueChanged.AddListener(usePhysicsToggle_onValueChanged);
        useColliderToggle.onValueChanged.AddListener(useColliderToggle_onValueChanged);
    }

    /// <summary>
    /// 無効時
    /// </summary>
    protected override void OnDisable()
    {
        base.OnDisable();
        useLiensToggle.onValueChanged.RemoveAllListeners();
        usePhysicsToggle.onValueChanged.RemoveAllListeners();
        useColliderToggle.onValueChanged.RemoveAllListeners();
    }
    #endregion 初期化処理

    #region イベント
    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void Update()
    {
        base.Update();
        float dt = Time.deltaTime;
        fpss.Add(1f / dt);
        times.Add(dt * 1000f);
        if (fpss.Count > 100)
        {
            fpss.RemoveAt(0);
            times.RemoveAt(0);
        }
        // 表示更新は間引き（毎フレームのTMP再生成＋LINQ Average を避ける。平均は手動合計）
        fpsRefreshTimer += dt;
        if (fpsRefreshTimer >= 0.25f)
        {
            fpsRefreshTimer = 0f;
            fpsText.text = Avg(fpss).ToString("0");
            timeText.text = "(" + Avg(times).ToString("0") + "msec)";
        }
    }

    /// <summary>List の平均（LINQ Average を使わずアロケーション回避）。</summary>
    private static float Avg(List<float> v)
    {
        if (v.Count == 0)
        {
            return 0f;
        }
        float s = 0f;
        for (int i = 0; i < v.Count; i++)
        {
            s += v[i];
        }
        return s / v.Count;
    }

    /// <summary>
    /// 物理使用トグル変更イベント
    /// </summary>
    /// <param name="value"></param>
    public void useLiensToggle_onValueChanged(bool value)
    {
        GlobalScript.isLiens = value;
    }

    /// <summary>
    /// 物理使用トグル変更イベント
    /// </summary>
    /// <param name="value"></param>
    public void usePhysicsToggle_onValueChanged(bool value)
    {
        Physics.simulationMode = value ? SimulationMode.FixedUpdate : SimulationMode.Script;
    }

    /// <summary>
    /// 衝突使用トグル変更イベント
    /// </summary>
    /// <param name="value"></param>
    public void useColliderToggle_onValueChanged(bool value)
    {
        GlobalScript.isCollision = value;
    }
    #endregion イベント

    #region メソッド
    #endregion メソッド
}
