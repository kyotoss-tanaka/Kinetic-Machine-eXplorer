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

    /// <summary>
    /// 3D表示の描画解像度の選択肢（URP の Render Scale）
    /// </summary>
    private static readonly float[] RenderScales = { 1f, 0.75f, 0.5f };
    private readonly List<Button> renderScaleButtons = new();

    #region 初期化処理
    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Awake()
    {
        // 履歴のチェックを足す。枠の組み立て（base.Awake）より前に足し、色・フォント・余白を他のチェックとそろえる
        AddHistoryToggle();
        // 描画解像度の行を履歴の下に足す（同じく枠の組み立て前）
        AddRenderScaleRow();

        base.Awake();

        // 枠の組み立てでボタンの色が通常の色にそろうので、選択中の色はその後に付ける
        RenewRenderScaleButtons();

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
    /// 描画解像度（Render Scale）の行を足す（Prefab は変えず、最後の行の下に「ラベル＋選択肢のボタン」を1行足す）
    /// </summary>
    private void AddRenderScaleRow()
    {
        var last = GetComponentsInChildren<Toggle>(true).FirstOrDefault(d => d.name == "UseHistoryToggle")
            ?? GetComponentsInChildren<Toggle>(true).FirstOrDefault(d => d.name == "UseColliderToggle");
        if (last == null)
        {
            return;
        }
        var lastRt = (RectTransform)last.transform;
        var contents = lastRt.parent as RectTransform;
        var rowHeight = lastRt.sizeDelta.y;
        var y = lastRt.anchoredPosition.y - rowHeight;
        KmxUiStyle.AddText(contents, "RenderScaleLabel", new Vector2(KmxUiStyle.PanelPadding, y), new Vector2(105f, rowHeight), "Render Scale", TextAlignmentOptions.MidlineLeft);
        const float buttonWidth = 48f;
        const float gap = 4f;
        for (int i = 0; i < RenderScales.Length; i++)
        {
            var scale = RenderScales[i];
            var btn = KmxUiStyle.AddButton(contents, "BtnRenderScale" + i, new Vector2(115f + (buttonWidth + gap) * i, y - 3f), new Vector2(buttonWidth, rowHeight - 6f),
                $"{scale * 100f:0}%", () =>
                {
                    AppSettings.RenderScale = scale;
                    RenewRenderScaleButtons();
                });
            renderScaleButtons.Add(btn);
        }
        // 1行分、中身の入れ物とパネルを伸ばす
        if (contents != null)
        {
            contents.sizeDelta += new Vector2(0f, rowHeight);
        }
        ((RectTransform)transform).sizeDelta += new Vector2(0f, rowHeight);
    }

    /// <summary>
    /// 選択中の描画解像度のボタンだけ強調色にする
    /// </summary>
    private void RenewRenderScaleButtons()
    {
        var now = AppSettings.RenderScale;
        for (int i = 0; i < renderScaleButtons.Count; i++)
        {
            // Image は using static MediaTypeNames の Image と重なるので名前空間を付ける
            var img = renderScaleButtons[i].targetGraphic as UnityEngine.UI.Image;
            if (img != null)
            {
                img.color = Mathf.Approximately(RenderScales[i], now) ? KmxUiStyle.ButtonActive : KmxUiStyle.Button;
            }
        }
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
        // 実時間で測る。Time.deltaTime は Maximum Allowed Timestep(0.06秒)で頭打ちになり、
        // 1フレームが60msを超えると常に「17fps(60msec)」と表示されて実際の遅さが分からないため。
        // （履歴の表示中は timeScale=0 で Time.deltaTime が0になり、表示が無限大になるのも防ぐ）
        float dt = Time.unscaledDeltaTime;
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
