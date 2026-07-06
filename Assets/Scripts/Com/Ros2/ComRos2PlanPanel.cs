using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 経路計画の実行時UI（画面上でゴール姿勢設定→計画→計画中/残り時間/成否→OK/NG）。
///
/// フロー：
///   1) Set Goal トグル：isManual=ON にして J1..J6 スライダーで目標姿勢を作る（実機モデルが目標を表示）。
///   2) Plan：現在姿勢(start)→スライダーのゴール で計画開始（障害物/ヘッドも送る）。
///   3) 計画中は「Planning… 残りX秒」、成功でプレビュー→OK/NG、失敗は理由表示。
///   4) OK=確定して再生（Unity表示のみ） / NG=破棄。
///
/// 同一 GameObject の ComRos2PathPlanner（状態機械・計画・再生）を使う。ParameterLoader が自動アタッチ。
/// ※ Stage1: Screen-space overlay・英数ラベル。ゴースト再生とロボット近傍配置は Stage2 で対応。
/// 前提：Standalone のみ（WebGL/Android/iPhone は無効化）。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ComRos2PathPlanner))]
public class ComRos2PlanPanel : MonoBehaviour
{
    [SerializeField] private double displayBudgetSec = 10.0;   // 計画予算＝残り時間表示の総量
    [SerializeField] private float jointMin = -180f;
    [SerializeField] private float jointMax = 180f;
    private static readonly string[] JointNames = { "J1", "J2", "J3", "J4", "J5", "J6" };

    private ComRos2PathPlanner planner;
    private Kinematics6D kin;
    private bool goalSetMode;
    private readonly double[] goalDeg = new double[6];

    private Text statusText;
    private Text goalText;
    private readonly Slider[] sliders = new Slider[6];
    private readonly Text[] sliderVals = new Text[6];
    private Button setGoalBtn, planBtn, okBtn, ngBtn;
    private Font uiFont;

    private void Start()
    {
#if (UNITY_WEBGL || UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        enabled = false;
        return;
#else
        if (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer)
        {
            enabled = false;
            return;
        }
        planner = GetComponent<ComRos2PathPlanner>();
        if (planner == null)
        {
            Debug.LogWarning("[ComRos2PlanPanel] 同一 GameObject に ComRos2PathPlanner が必要です。無効化します。");
            enabled = false;
            return;
        }
        planner.StateChanged += OnPlanState;
        BuildUI();
        RefreshButtons(planner.State);
#endif
    }

    private void OnDestroy()
    {
        if (planner != null)
        {
            planner.StateChanged -= OnPlanState;
        }
    }

    private void Update()
    {
        if (planner == null)
        {
            return;
        }
        // 計画中は残り時間を更新表示。
        if (planner.State == ComRos2PathPlanner.PlanState.Planning && statusText != null)
        {
            float rem = Mathf.Max(0f, (float)displayBudgetSec - planner.PlanElapsedSec);
            statusText.text = $"計画中…  残り {rem:F1}s";
        }
    }

    #region UI 構築
    private void BuildUI()
    {
        uiFont = GetFont();
        EnsureEventSystem();

        // Canvas（Screen-space overlay）
        var canvasGo = new GameObject("Ros2PlanPanelCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        // ★等倍(ConstantPixelSize)。ScaleWithScreenSize だと参照解像度との差で
        //   レガシーText が拡大縮小されて滲む。等倍ならピクセル1:1で描画されくっきり。
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
        scaler.dynamicPixelsPerUnit = 1f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // パネル（左上に縦積み）
        var panel = MakeRect("Panel", canvasGo.transform);
        panel.anchorMin = new Vector2(0f, 1f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 1f);
        panel.anchoredPosition = new Vector2(16f, -16f);
        panel.sizeDelta = new Vector2(360f, 470f);
        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.6f);

        float y = -8f;
        statusText = MakeLabel(panel, "status", "待機", 18, new Vector2(8f, y), 344f, 28f);
        y -= 32f;
        goalText = MakeLabel(panel, "goal", "ゴール: -", 14, new Vector2(8f, y), 344f, 22f);
        y -= 28f;

        // J1..J6 スライダー
        for (int i = 0; i < 6; i++)
        {
            int idx = i;
            MakeLabel(panel, $"lbl{i}", JointNames[i], 14, new Vector2(8f, y), 30f, 22f);
            var s = MakeSlider(panel, $"sld{i}", new Vector2(44f, y), 240f, 22f);
            s.minValue = jointMin;
            s.maxValue = jointMax;
            s.value = 0f;
            s.onValueChanged.AddListener(v => OnSlider(idx, v));
            sliders[i] = s;
            sliderVals[i] = MakeLabel(panel, $"val{i}", "0", 14, new Vector2(290f, y), 62f, 22f);
            y -= 26f;
        }
        y -= 6f;

        // ボタン
        setGoalBtn = MakeButton(panel, "SetGoal", "ゴール設定", new Vector2(8f, y), 168f, 34f, ToggleGoalSet);
        planBtn = MakeButton(panel, "Plan", "計画", new Vector2(184f, y), 168f, 34f, OnPlan);
        y -= 40f;
        okBtn = MakeButton(panel, "OK", "OK 実行", new Vector2(8f, y), 168f, 34f, OnOk);
        ngBtn = MakeButton(panel, "NG", "NG 破棄", new Vector2(184f, y), 168f, 34f, OnNg);
    }

    private void EnsureEventSystem()
    {
        if (FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length == 0)
        {
            var es = new GameObject("EventSystem");
            es.transform.SetParent(transform, false);
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
    }

    private static Font GetFont()
    {
        var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
        if (f == null) { f = Font.CreateDynamicFontFromOSFont("Arial", 14); }
        return f;
    }

    private static RectTransform MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private Text MakeLabel(RectTransform parent, string name, string text, int size, Vector2 pos, float w, float h)
    {
        var rt = MakeRect(name, parent);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(w, h);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = uiFont;
        t.fontSize = size;
        t.color = Color.white;
        t.alignment = TextAnchor.MiddleLeft;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.text = text;
        return t;
    }

    private Slider MakeSlider(RectTransform parent, string name, Vector2 pos, float w, float h)
    {
        var rt = MakeRect(name, parent);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(w, h);
        var slider = rt.gameObject.AddComponent<Slider>();

        var bg = MakeRect("BG", rt);
        Stretch(bg);
        var bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.color = new Color(1f, 1f, 1f, 0.25f);

        var fillArea = MakeRect("Fill Area", rt);
        Stretch(fillArea);
        var fill = MakeRect("Fill", fillArea);
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(0f, 1f);
        fill.sizeDelta = new Vector2(10f, 0f);
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.color = new Color(0.1f, 0.7f, 1f, 0.9f);

        var handleArea = MakeRect("Handle Slide Area", rt);
        Stretch(handleArea);
        var handle = MakeRect("Handle", handleArea);
        handle.sizeDelta = new Vector2(14f, 0f);
        var handleImg = handle.gameObject.AddComponent<Image>();
        handleImg.color = Color.white;

        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.wholeNumbers = false;
        return slider;
    }

    private Button MakeButton(RectTransform parent, string name, string label, Vector2 pos, float w, float h, UnityEngine.Events.UnityAction onClick)
    {
        var rt = MakeRect(name, parent);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(w, h);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(0.2f, 0.4f, 0.7f, 0.95f);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        var t = MakeLabel(rt, "Text", label, 15, Vector2.zero, w, h);
        t.alignment = TextAnchor.MiddleCenter;
        // ラベルを中央に伸ばす
        var trt = (RectTransform)t.transform;
        Stretch(trt);
        return btn;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
    #endregion UI 構築

    #region 操作
    private void ToggleGoalSet()
    {
        goalSetMode = !goalSetMode;
        EnsureKin();
        if (goalSetMode)
        {
            // 現在姿勢をゴール初期値にしてスライダーへ、isManual ON で目標を表示。
            var cur = planner.ReadCurrentDeg();
            for (int i = 0; i < 6; i++)
            {
                goalDeg[i] = (cur != null && i < cur.Length) ? cur[i] : 0d;
                if (sliders[i] != null) { sliders[i].SetValueWithoutNotify((float)goalDeg[i]); }
                if (sliderVals[i] != null) { sliderVals[i].text = goalDeg[i].ToString("F1"); }
            }
            if (kin != null)
            {
                kin.SetManual(true);
                kin.SetManualJointsDeg(goalDeg);
            }
            UpdateGoalText();
        }
        else
        {
            // 設定終了：実機現在姿勢の表示へ戻す。
            if (kin != null) { kin.SetManual(false); }
        }
        SetButtonColor(setGoalBtn, goalSetMode ? new Color(0.8f, 0.5f, 0.1f, 0.95f) : new Color(0.2f, 0.4f, 0.7f, 0.95f));
    }

    private void OnSlider(int i, float v)
    {
        goalDeg[i] = v;
        if (sliderVals[i] != null) { sliderVals[i].text = v.ToString("F1"); }
        UpdateGoalText();
        if (goalSetMode && kin != null)
        {
            kin.SetManualJointsDeg(goalDeg);   // 目標姿勢を実機モデルに表示
        }
    }

    private void OnPlan()
    {
        // 設定モードなら抜けて現在姿勢の表示へ（start=実機現在）。
        if (goalSetMode) { ToggleGoalSet(); }
        planner.PlanTimeBudget = displayBudgetSec;                 // 残り時間表示＆ROS2予算
        var start = planner.ReadCurrentDeg();
        var goal = (double[])goalDeg.Clone();
        planner.RequestPlanWithScene(start, goal);                 // 障害物/ヘッドも送って計画
    }

    private void OnOk()
    {
        planner.ApprovePlan();
    }

    private void OnNg()
    {
        planner.CancelPlan();
    }

    private void OnPlanState(ComRos2PathPlanner.PlanState s, string msg)
    {
        if (statusText != null && s != ComRos2PathPlanner.PlanState.Planning)
        {
            statusText.text = msg;   // Planning は Update で残り時間を出すのでここでは上書きしない
        }
        RefreshButtons(s);
    }

    private void RefreshButtons(ComRos2PathPlanner.PlanState s)
    {
        bool preview = s == ComRos2PathPlanner.PlanState.Preview;
        if (okBtn != null) { okBtn.gameObject.SetActive(preview); }
        if (ngBtn != null) { ngBtn.gameObject.SetActive(preview); }
        if (planBtn != null) { planBtn.interactable = s != ComRos2PathPlanner.PlanState.Planning; }
    }

    private void UpdateGoalText()
    {
        if (goalText == null)
        {
            return;
        }
        goalText.text = $"ゴール: {goalDeg[0]:F0},{goalDeg[1]:F0},{goalDeg[2]:F0},{goalDeg[3]:F0},{goalDeg[4]:F0},{goalDeg[5]:F0}";
    }

    private void EnsureKin()
    {
        if (kin != null)
        {
            return;
        }
        var kins = FindObjectsByType<Kinematics6D>(FindObjectsSortMode.None);
        if (kins != null && kins.Length > 0)
        {
            kin = kins[0];
        }
    }

    private static void SetButtonColor(Button b, Color c)
    {
        if (b != null && b.targetGraphic is Image img)
        {
            img.color = c;
        }
    }
    #endregion 操作
}
