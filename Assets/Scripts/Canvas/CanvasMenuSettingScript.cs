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
    private Toggle useHistoryToggle;
    private List<float> times = new();
    private List<float> fpss = new();
    private float fpsRefreshTimer;   // FPS表示の更新間引き用

    #region 初期化処理
    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Awake()
    {
        // 履歴のチェックを足す。枠の組み立て（base.Awake）より前に足し、色・フォント・余白を他のチェックとそろえる
        AddHistoryToggle();

        base.Awake();

        fpsText = GetComponentsInChildren<TextMeshProUGUI>().Where(d => d.name == "FpsText").ToList()[0];
        timeText = GetComponentsInChildren<TextMeshProUGUI>().Where(d => d.name == "TimeText").ToList()[0];
        useLiensToggle = GetComponentsInChildren<Toggle>().ToList().Find(d => d.name == "UseLinesToggle");
        usePhysicsToggle = GetComponentsInChildren<Toggle>().ToList().Find(d => d.name == "UsePhysicsToggle");
        useColliderToggle = GetComponentsInChildren<Toggle>().ToList().Find(d => d.name == "UseColliderToggle");
        useHistoryToggle = GetComponentsInChildren<Toggle>().ToList().Find(d => d.name == "UseHistoryToggle");

        // 保存してある値でチェックを始める（Prefab の初期値ではなく前回の値。値そのものは起動時に AppSettings が反映済み）
        useLiensToggle.SetIsOnWithoutNotify(AppSettings.UseLines);
        usePhysicsToggle.SetIsOnWithoutNotify(AppSettings.UsePhysics);
        useColliderToggle.SetIsOnWithoutNotify(AppSettings.UseCollision);
        if (useHistoryToggle != null)
        {
            useHistoryToggle.SetIsOnWithoutNotify(AppSettings.UseHistory);
        }
    }

    /// <summary>
    /// 履歴のチェックを足す（Prefab は変えず、衝突のチェックを複製して下に1行足す）
    /// </summary>
    private void AddHistoryToggle()
    {
        var src = GetComponentsInChildren<Toggle>(true).FirstOrDefault(d => d.name == "UseColliderToggle");
        if (src == null)
        {
            return;
        }
        var srcRt = (RectTransform)src.transform;
        var clone = Instantiate(src.gameObject, srcRt.parent);
        clone.name = "UseHistoryToggle";
        var rt = (RectTransform)clone.transform;
        rt.anchoredPosition = srcRt.anchoredPosition - new Vector2(0f, srcRt.sizeDelta.y);
        const string label = "Use History (Prev / Next)";
        var legacy = clone.GetComponentInChildren<UnityEngine.UI.Text>(true);
        if (legacy != null)
        {
            legacy.text = label;
        }
        else
        {
            var tmp = clone.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                tmp.text = label;
            }
        }
        // 1行分、中身の入れ物とパネルを伸ばす
        var contents = srcRt.parent as RectTransform;
        if (contents != null)
        {
            contents.sizeDelta += new Vector2(0f, srcRt.sizeDelta.y);
        }
        ((RectTransform)transform).sizeDelta += new Vector2(0f, srcRt.sizeDelta.y);
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
        useHistoryToggle?.onValueChanged.AddListener(useHistoryToggle_onValueChanged);
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
        useHistoryToggle?.onValueChanged.RemoveAllListeners();
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
        // 次の起動でも同じ値で始めるよう保存する（以下同じ）
        AppSettings.UseLines = value;
    }

    /// <summary>
    /// 物理使用トグル変更イベント
    /// </summary>
    /// <param name="value"></param>
    public void usePhysicsToggle_onValueChanged(bool value)
    {
        AppSettings.UsePhysics = value;
    }

    /// <summary>
    /// 衝突使用トグル変更イベント
    /// </summary>
    /// <param name="value"></param>
    public void useColliderToggle_onValueChanged(bool value)
    {
        AppSettings.UseCollision = value;
    }

    /// <summary>
    /// 履歴使用トグル変更イベント（記録の分だけ処理が重くなるので既定は OFF）
    /// </summary>
    /// <param name="value"></param>
    public void useHistoryToggle_onValueChanged(bool value)
    {
        AppSettings.UseHistory = value;
    }
    #endregion イベント

    #region メソッド
    #endregion メソッド
}
