using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>タイトルバーをドラッグして親パネル(target)を移動する（overlay・等倍前提でdelta=1:1）。</summary>
public sealed class Ros2PanelDrag : MonoBehaviour, IDragHandler
{
    public RectTransform target;
    public void OnDrag(PointerEventData e)
    {
        if (target != null)
        {
            target.anchoredPosition += e.delta;
        }
    }
}

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
    [SerializeField] private double displayBudgetSec = 0.0;   // 計画予算(0=ROS2既定)。>0 なら残り時間表示に使う
    [SerializeField] private float jointMin = -180f;
    [SerializeField] private float jointMax = 180f;
    private static readonly string[] DefaultJointNames = { "J1", "J2", "J3", "J4", "J5", "J6" };
    private string[] jointNames = DefaultJointNames;   // 選択ロボットの関節名（可変長・6以上）

    private ComRos2PathPlanner planner;
    private ComRos2Obstacles obstacles;   // ヘッド設定(1箱/間引き)の切替用
    private ComRos2Launcher launcher;     // ROS2 起動/停止/再起動（wsl.exe）
    private Ros2PlanTargetRegistry registry;   // 複数ロボットの列挙/選択
    private IRos2PlanTarget targetKin;    // 選択ロボの kinematics（ゴール姿勢の手動表示用）
    private bool goalSetMode;
    private bool goalInitialized;                  // 起動時に一度だけ現在姿勢で初期化したか
    private double[] goalDeg = new double[6];       // 長さは jointNames.Length に追従

    private Text statusText;
    private Text goalText;
    private Text commText;                                            // ROS通信状態（タイトルバー右）
    private Text launchStateText;                                     // ROS2 起動状態（stopped/starting/running_full）
    private Button startBtn, stopBtn, restartBtn;                     // 起動/停止/再起動
    private Slider[] sliders = new Slider[0];
    private InputField[] sliderInputs = new InputField[0];   // 角度の直接入力（関節数ぶん）
    private Button setGoalBtn, planBtn, okBtn, ngBtn;
    private Text robotNameText;                     // 選択中ロボット名（◀ 名前 ▶）
    private Button robotPrevBtn, robotNextBtn;
    private InputField budgetInput, ratioInput;                       // 時間予算/大回り許容比の入力
    private double planGoodRatioVal;                                  // 大回り許容比（0=ROS2既定）
    private Font uiFont;
    private GameObject canvasGo;                                      // 生成した Canvas（破棄/掃除用）
    private GameObject panelRootGo;                                   // パネル本体（表示トグル対象。Canvas/EventSystem は常時活性）
    private const string CanvasName = "Ros2PlanPanelCanvas";

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
        obstacles = GetComponent<ComRos2Obstacles>();   // 無くても可（トグルは非表示相当）
        launcher = GetComponent<ComRos2Launcher>();      // 無くても可（起動行は非表示相当）
        registry = GetComponent<Ros2PlanTargetRegistry>();   // 複数ロボット（無ければ従来の単一ロボット動作）
        planner.StateChanged += OnPlanState;
        if (registry != null) { registry.Changed += OnRegistryChanged; }
        BuildUI();
        RefreshButtons(planner.State);
        SetVisible(false);   // 初期は非表示。InfoMenu の BtnRoboPath で表示する（他メニューと同様）
#endif
    }

    private void OnDestroy()
    {
        if (planner != null)
        {
            planner.StateChanged -= OnPlanState;
        }
        if (registry != null)
        {
            registry.Changed -= OnRegistryChanged;
        }
        // 生成した UI(Canvas + 配下のEventSystem) を破棄。残さないとリロード/再コンパイルで重複する。
        if (canvasGo != null)
        {
            Destroy(canvasGo);
            canvasGo = null;
        }
    }

    /// <summary>パネル本体の表示/非表示（Canvas と EventSystem は常時活性のまま＝他UIの入力を止めない）。</summary>
    public void SetVisible(bool v)
    {
        if (panelRootGo != null)
        {
            panelRootGo.SetActive(v);
        }
    }

    /// <summary>パネルが表示中か。</summary>
    public bool IsVisible => panelRootGo != null && panelRootGo.activeSelf;

    /// <summary>表示/非表示をトグルする（InfoMenu の BtnRoboPath 用）。</summary>
    public void ToggleVisible() => SetVisible(!IsVisible);

    private void Update()
    {
        if (planner == null)
        {
            return;
        }
        // 起動後（ロード完了）に一度だけ、ゴール初期値を現在姿勢にする（以後は保持・上書きしない）。
        if (!goalInitialized && GlobalScript.isLoaded)
        {
            InitGoalFromCurrent();
            goalInitialized = true;
        }
        // ROS通信状態（タイトルバー右）。
        if (commText != null)
        {
            bool up = planner.IsLinkUp;
            commText.text = up ? "ROS ●接続" : "ROS ●未接続";
            commText.color = up ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.45f, 0.45f);
        }
        // ROS2 起動状態ランプ＋ボタン活性（起動中は押下抑止）。
        if (launcher != null && launchStateText != null)
        {
            UpdateLaunchUi();
        }
        // 計画中の表示。予算>0 なら残り時間、0(ROS2既定で総量不明)なら経過時間。
        if (planner.State == ComRos2PathPlanner.PlanState.Planning && statusText != null)
        {
            float el = planner.PlanElapsedSec;
            statusText.text = displayBudgetSec > 0.0
                ? $"計画中…  残り {Mathf.Max(0f, (float)displayBudgetSec - el):F1}s"
                : $"計画中…  {el:F1}s 経過";
        }
    }

    #region UI 構築
    private void BuildUI()
    {
        uiFont = GetFont();
        DestroyExistingPanels();   // リロード/再コンパイルで残った古いUIを先に掃除（重複防止）

        // Canvas（Screen-space overlay）。EventSystem も配下に置きライフタイムを揃える。
        canvasGo = new GameObject(CanvasName);
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
        EnsureEventSystem();   // 無ければ Canvas 配下に作る（Canvas と一緒に破棄される）

        // パネル（左上・タイトルバーでドラッグ移動可。高さは内容に合わせて末尾で確定）
        const float W = 360f;
        var panel = MakeRect("Panel", canvasGo.transform);
        panelRootGo = panel.gameObject;   // 表示トグル対象（Canvas/EventSystem は残す）
        panel.anchorMin = new Vector2(0f, 1f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 1f);
        panel.anchoredPosition = new Vector2(16f, -16f);
        panel.sizeDelta = new Vector2(W, 400f);
        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.6f);

        // タイトルバー（ドラッグで移動）
        var title = MakeRect("TitleBar", panel);
        title.anchorMin = new Vector2(0f, 1f);
        title.anchorMax = new Vector2(0f, 1f);
        title.pivot = new Vector2(0f, 1f);
        title.anchoredPosition = new Vector2(0f, 0f);
        title.sizeDelta = new Vector2(W, 26f);
        var titleImg = title.gameObject.AddComponent<Image>();
        titleImg.color = new Color(0.15f, 0.3f, 0.55f, 0.98f);
        title.gameObject.AddComponent<Ros2PanelDrag>().target = panel;
        var titleLbl = MakeLabel(title, "TitleText", "≡ 経路計画", 15, new Vector2(8f, 0f), 180f, 26f);
        titleLbl.alignment = TextAnchor.MiddleLeft;
        titleLbl.raycastTarget = false;   // タイトルバー(Image)でドラッグを拾わせる
        // ROS通信状態（タイトルバー右）。Update で色/文言を更新。
        commText = MakeLabel(title, "Comm", "ROS ●", 13, new Vector2(W - 130f, 0f), 122f, 26f);
        commText.alignment = TextAnchor.MiddleRight;
        commText.raycastTarget = false;

        float y = -30f;   // タイトルバーの下から積む

        // ロボット選択（◀ 名前 ▶）。複数ロボット時に切替。1台でも現機体を表示。
        robotPrevBtn = MakeButton(panel, "RobotPrev", "◀", new Vector2(8f, y), 30f, 24f, OnRobotPrev);
        robotNameText = MakeLabel(panel, "RobotName", "ロボット: -", 14, new Vector2(42f, y), W - 42f - 38f, 24f);
        robotNameText.alignment = TextAnchor.MiddleCenter;
        robotNextBtn = MakeButton(panel, "RobotNext", "▶", new Vector2(W - 38f, y), 30f, 24f, OnRobotNext);
        y -= 28f;

        // ROS2 起動制御（起動/停止/再起動＋状態ランプ）。ランチャがある時だけ表示。
        if (launcher != null)
        {
            startBtn = MakeButton(panel, "RosStart", "起動", new Vector2(8f, y), 60f, 26f, OnStartRos2);
            stopBtn = MakeButton(panel, "RosStop", "停止", new Vector2(72f, y), 60f, 26f, OnStopRos2);
            restartBtn = MakeButton(panel, "RosRestart", "再起動", new Vector2(136f, y), 72f, 26f, OnRestartRos2);
            launchStateText = MakeLabel(panel, "RosState", "ROS2: -", 13, new Vector2(214f, y), W - 222f, 26f);
            launchStateText.alignment = TextAnchor.MiddleRight;
            y -= 32f;
        }

        statusText = MakeLabel(panel, "status", "待機", 16, new Vector2(8f, y), W - 16f, 24f);
        y -= 28f;
        goalText = MakeLabel(panel, "goal", "ゴール: -", 13, new Vector2(8f, y), W - 16f, 20f);
        y -= 26f;

        // 関節スライダー＋直接入力（選択ロボの関節数ぶん・可変。6軸以上）
        int nJoints = (jointNames != null && jointNames.Length > 0) ? jointNames.Length : DefaultJointNames.Length;
        if (goalDeg == null || goalDeg.Length != nJoints) { goalDeg = new double[nJoints]; }
        sliders = new Slider[nJoints];
        sliderInputs = new InputField[nJoints];
        for (int i = 0; i < nJoints; i++)
        {
            int idx = i;
            string jn = (jointNames != null && i < jointNames.Length) ? jointNames[i] : $"J{i + 1}";
            MakeLabel(panel, $"lbl{i}", jn, 14, new Vector2(8f, y), 34f, 22f);
            var s = MakeSlider(panel, $"sld{i}", new Vector2(46f, y), 230f, 22f);
            s.minValue = jointMin;
            s.maxValue = jointMax;
            s.value = (i < goalDeg.Length) ? (float)goalDeg[i] : 0f;
            s.onValueChanged.AddListener(v => OnSlider(idx, v));
            sliders[i] = s;
            var inp = MakeInput(panel, $"inp{i}", new Vector2(282f, y), 70f, 22f);
            inp.SetTextWithoutNotify(((i < goalDeg.Length) ? goalDeg[i] : 0d).ToString("F1"));
            inp.onEndEdit.AddListener(v => OnInput(idx, v));
            sliderInputs[i] = inp;
            y -= 26f;
        }
        y -= 6f;

        // 計画パラメータ（時間予算 / 大回り許容比。0=ROS2ノード既定）
        MakeLabel(panel, "lblBudget", "時間予算(秒)", 13, new Vector2(8f, y), 110f, 22f);
        budgetInput = MakeInput(panel, "inpBudget", new Vector2(120f, y), 80f, 22f);
        budgetInput.SetTextWithoutNotify(displayBudgetSec.ToString("F0"));
        budgetInput.onEndEdit.AddListener(OnBudgetInput);
        MakeLabel(panel, "hintBudget", "0=既定", 12, new Vector2(206f, y), 150f, 22f);
        y -= 26f;
        MakeLabel(panel, "lblRatio", "大回り許容比", 13, new Vector2(8f, y), 110f, 22f);
        ratioInput = MakeInput(panel, "inpRatio", new Vector2(120f, y), 80f, 22f);
        ratioInput.SetTextWithoutNotify(planGoodRatioVal.ToString("F1"));
        ratioInput.onEndEdit.AddListener(OnRatioInput);
        MakeLabel(panel, "hintRatio", "0=既定/小=短経路", 12, new Vector2(206f, y), 150f, 22f);
        y -= 26f;

        // ヘッド形状トグル（1箱＝把持開口なし / OFF＝グリッド間引きで開口を残す）
        if (obstacles != null)
        {
            MakeToggle(panel, "togHeadSingle", "ヘッド1箱(把持開口なし)", obstacles.HeadAsSingleBox,
                new Vector2(8f, y), v => { if (obstacles != null) { obstacles.HeadAsSingleBox = v; } });
            y -= 28f;
        }

        // ボタン
        setGoalBtn = MakeButton(panel, "SetGoal", "ゴール設定", new Vector2(8f, y), 168f, 34f, ToggleGoalSet);
        planBtn = MakeButton(panel, "Plan", "計画", new Vector2(184f, y), 168f, 34f, OnPlan);
        y -= 40f;
        okBtn = MakeButton(panel, "OK", "OK 実行", new Vector2(8f, y), 168f, 34f, OnOk);
        ngBtn = MakeButton(panel, "NG", "NG 破棄", new Vector2(184f, y), 168f, 34f, OnNg);

        // 内容に合わせて背景の高さを確定（下の余白を詰める）
        panel.sizeDelta = new Vector2(W, -y + 34f + 8f);
        UpdateRobotRow();   // ロボット名ラベル/ボタン活性を現状に合わせる
    }

    private void EnsureEventSystem()
    {
        if (FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length == 0)
        {
            var es = new GameObject("EventSystem");
            es.transform.SetParent(canvasGo.transform, false);   // Canvas 配下＝掃除時に一緒に消える
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
    }

    /// <summary>既存の計画パネル Canvas（リロード/再コンパイルの残骸含む）を全て破棄する。</summary>
    private void DestroyExistingPanels()
    {
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c != null && c.gameObject.name == CanvasName)
            {
                Destroy(c.gameObject);
            }
        }
    }

#if UNITY_EDITOR
    // ★再コンパイル(ドメインリロード)の直前にパネルCanvasを確実に破棄する。
    //   これが無いと、再生中の再コンパイルで onClick 等の非永続リスナが失われた「死んだUI」が残る。
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditorReloadCleanup()
    {
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= CleanupOnAssemblyReload;
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += CleanupOnAssemblyReload;
    }

    private static void CleanupOnAssemblyReload()
    {
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c != null && c.gameObject.name == CanvasName)
            {
                DestroyImmediate(c.gameObject);
            }
        }
    }
#endif

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

    private InputField MakeInput(RectTransform parent, string name, Vector2 pos, float w, float h)
    {
        var rt = MakeRect(name, parent);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(w, h);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.15f);
        var input = rt.gameObject.AddComponent<InputField>();

        var textRt = MakeRect("Text", rt);
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(4f, 0f);
        textRt.offsetMax = new Vector2(-4f, 0f);
        var text = textRt.gameObject.AddComponent<Text>();
        text.font = uiFont;
        text.fontSize = 14;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;
        text.supportRichText = false;

        input.textComponent = text;
        input.targetGraphic = img;
        input.contentType = InputField.ContentType.Standard;   // 符号/小数を許容（float.TryParse で検証）
        input.text = "0";
        return input;
    }

    private Toggle MakeToggle(RectTransform parent, string name, string label, bool init, Vector2 pos, UnityEngine.Events.UnityAction<bool> onChange)
    {
        var rt = MakeRect(name, parent);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(320f, 22f);
        // 行全体をクリック領域に（透明だが raycast 対象）。
        var rowImg = rt.gameObject.AddComponent<Image>();
        rowImg.color = new Color(0f, 0f, 0f, 0f);

        var boxRt = MakeRect("Box", rt);
        boxRt.anchorMin = new Vector2(0f, 0.5f);
        boxRt.anchorMax = new Vector2(0f, 0.5f);
        boxRt.pivot = new Vector2(0f, 0.5f);
        boxRt.anchoredPosition = new Vector2(2f, 0f);
        boxRt.sizeDelta = new Vector2(18f, 18f);
        var boxImg = boxRt.gameObject.AddComponent<Image>();
        boxImg.color = new Color(1f, 1f, 1f, 0.3f);

        var ckRt = MakeRect("Check", boxRt);
        ckRt.anchorMin = new Vector2(0.15f, 0.15f);
        ckRt.anchorMax = new Vector2(0.85f, 0.85f);
        ckRt.offsetMin = Vector2.zero;
        ckRt.offsetMax = Vector2.zero;
        var ckImg = ckRt.gameObject.AddComponent<Image>();
        ckImg.color = new Color(0.1f, 0.8f, 1f, 1f);

        var toggle = rt.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = boxImg;
        toggle.graphic = ckImg;
        toggle.isOn = init;
        toggle.onValueChanged.AddListener(onChange);

        var lbl = MakeLabel(rt, "Label", label, 13, new Vector2(26f, 0f), 290f, 22f);
        lbl.raycastTarget = false;
        return toggle;
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
            // ★ゴール値は保持（クリアしない）。現在の goalDeg を isManual で実機モデルに表示するだけ。
            if (targetKin != null)
            {
                targetKin.SetManual(true);
                targetKin.SetManualJointsDeg(goalDeg);
            }
            UpdateGoalText();
        }
        else
        {
            // 設定終了：実機現在姿勢の表示へ戻す（goalDeg は保持）。
            if (targetKin != null) { targetKin.SetManual(false); }
        }
        SetButtonColor(setGoalBtn, goalSetMode ? new Color(0.8f, 0.5f, 0.1f, 0.95f) : new Color(0.2f, 0.4f, 0.7f, 0.95f));
    }

    /// <summary>起動時の一度だけ：ゴール初期値を現在姿勢にしてスライダー/入力へ反映（以後は保持）。</summary>
    private void InitGoalFromCurrent()
    {
        var cur = planner.ReadCurrentDeg();
        int n = (jointNames != null && jointNames.Length > 0) ? jointNames.Length : 6;
        if (goalDeg == null || goalDeg.Length != n) { goalDeg = new double[n]; }
        for (int i = 0; i < n; i++)
        {
            goalDeg[i] = (cur != null && i < cur.Length) ? cur[i] : 0d;
            if (i < sliders.Length && sliders[i] != null) { sliders[i].SetValueWithoutNotify((float)goalDeg[i]); }
            if (i < sliderInputs.Length && sliderInputs[i] != null) { sliderInputs[i].SetTextWithoutNotify(goalDeg[i].ToString("F1")); }
        }
        UpdateGoalText();
    }

    private void OnSlider(int i, float v)
    {
        goalDeg[i] = v;
        if (sliderInputs[i] != null) { sliderInputs[i].SetTextWithoutNotify(v.ToString("F1")); }
        UpdateGoalText();
        if (goalSetMode && targetKin != null)
        {
            targetKin.SetManualJointsDeg(goalDeg);   // 目標姿勢を実機モデルに表示
        }
    }

    /// <summary>角度の直接入力（確定時）。パースしてスライダー/ゴールへ反映。無効入力は元に戻す。</summary>
    private void OnInput(int i, string s)
    {
        if (float.TryParse(s, out float val))
        {
            val = Mathf.Clamp(val, jointMin, jointMax);
            goalDeg[i] = val;
            if (sliders[i] != null) { sliders[i].SetValueWithoutNotify(val); }
            if (sliderInputs[i] != null) { sliderInputs[i].SetTextWithoutNotify(val.ToString("F1")); }
            UpdateGoalText();
            if (goalSetMode && targetKin != null)
            {
                targetKin.SetManualJointsDeg(goalDeg);
            }
        }
        else if (sliderInputs[i] != null)
        {
            sliderInputs[i].SetTextWithoutNotify(goalDeg[i].ToString("F1"));   // 無効 → 現在値へ戻す
        }
    }

    /// <summary>時間予算(秒)の直接入力。0以上。無効は元へ戻す。</summary>
    private void OnBudgetInput(string s)
    {
        if (float.TryParse(s, out float v))
        {
            displayBudgetSec = Mathf.Max(0f, v);
            if (budgetInput != null) { budgetInput.SetTextWithoutNotify(displayBudgetSec.ToString("F0")); }
        }
        else if (budgetInput != null)
        {
            budgetInput.SetTextWithoutNotify(displayBudgetSec.ToString("F0"));
        }
    }

    /// <summary>大回り許容比の直接入力。0以上。無効は元へ戻す。</summary>
    private void OnRatioInput(string s)
    {
        if (float.TryParse(s, out float v))
        {
            planGoodRatioVal = Mathf.Max(0f, v);
            if (ratioInput != null) { ratioInput.SetTextWithoutNotify(planGoodRatioVal.ToString("F1")); }
        }
        else if (ratioInput != null)
        {
            ratioInput.SetTextWithoutNotify(planGoodRatioVal.ToString("F1"));
        }
    }

    private void OnPlan()
    {
        // 設定モードなら抜けて現在姿勢の表示へ（start=実機現在）。
        if (goalSetMode) { ToggleGoalSet(); }
        // ゴースト再生(プレビュー/再生)中に計画を押したら、まずキャンセルしてゴースト/プレビューを
        // 片付けてから計画する（ゴースト複製が残ったまま送信して姿勢が崩れるのを防ぐ）。
        if (planner.State == ComRos2PathPlanner.PlanState.Preview
            || planner.State == ComRos2PathPlanner.PlanState.Playing)
        {
            planner.CancelPlan();
        }
        planner.PlanTimeBudget = displayBudgetSec;                 // 残り時間表示＆ROS2予算
        planner.PlanGoodRatio = planGoodRatioVal;                  // 大回り許容比
        var start = planner.ReadCurrentDeg();
        var goal = (double[])goalDeg.Clone();
        planner.RequestPlanWithScene(start, goal);                 // 障害物/ヘッドも送って計画
    }

    private void OnStartRos2()
    {
        if (launcher != null) { launcher.StartRos2(); }
    }

    private void OnStopRos2()
    {
        // 停止すると endpoint も落ち TCP が切れる。計画中/プレビューなら破棄しておく。
        if (planner.State == ComRos2PathPlanner.PlanState.Preview ||
            planner.State == ComRos2PathPlanner.PlanState.Planning)
        {
            planner.CancelPlan();
        }
        if (launcher != null) { launcher.StopRos2(); }
    }

    private void OnRestartRos2()
    {
        if (planner.State == ComRos2PathPlanner.PlanState.Preview ||
            planner.State == ComRos2PathPlanner.PlanState.Planning)
        {
            planner.CancelPlan();
        }
        if (launcher != null) { launcher.RestartRos2(); }
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

    /// <summary>ROS2 起動状態ランプ＋起動/停止/再起動ボタンの活性を更新する。</summary>
    private void UpdateLaunchUi()
    {
        bool busy = launcher.Busy;
        var st = launcher.State;
        string label;
        Color c;
        switch (st)
        {
            case ComRos2Launcher.LaunchState.RunningFull:
                label = "ROS2: ●稼働中"; c = new Color(0.3f, 1f, 0.4f); break;
            case ComRos2Launcher.LaunchState.Starting:
                label = "ROS2: ●起動中…"; c = new Color(1f, 0.85f, 0.2f); break;
            case ComRos2Launcher.LaunchState.Stopped:
                label = "ROS2: ●停止"; c = new Color(0.7f, 0.7f, 0.7f); break;
            default:
                label = "ROS2: ●不明"; c = new Color(0.6f, 0.6f, 0.6f); break;
        }
        if (busy) { label += " (処理中)"; }
        launchStateText.text = label;
        launchStateText.color = c;

        if (startBtn != null) { startBtn.interactable = !busy && st != ComRos2Launcher.LaunchState.RunningFull; }
        if (stopBtn != null) { stopBtn.interactable = !busy && st != ComRos2Launcher.LaunchState.Stopped; }
        if (restartBtn != null) { restartBtn.interactable = !busy; }
    }

    // --- ロボット選択（複数ロボット） ---
    private void OnRobotPrev() => CycleRobot(-1);
    private void OnRobotNext() => CycleRobot(+1);

    private void CycleRobot(int dir)
    {
        if (registry == null || registry.Robots.Count == 0)
        {
            return;
        }
        int n = registry.Robots.Count;
        int idx = ((registry.SelectedIndex + dir) % n + n) % n;
        registry.Select(idx);   // → Changed → OnRegistryChanged
    }

    /// <summary>レジストリ構築/選択変更時：選択ロボへ計画/障害物をリターゲットし UI を合わせる。</summary>
    private void OnRegistryChanged()
    {
        if (registry == null)
        {
            return;
        }
        var sel = registry.Selected;
        if (sel == null)
        {
            UpdateRobotRow();
            return;
        }
        // 計画/障害物を選択ロボへ。前のプレビュー/ゴーストは片付ける。
        planner.CancelPlan();
        planner.SetTarget(sel);
        if (obstacles != null) { obstacles.SetTarget(sel); }
        targetKin = sel.Target;
        var names = sel.JointNames ?? DefaultJointNames;
        if (jointNames == null || names.Length != jointNames.Length)
        {
            jointNames = names;
            BuildUI();   // 関節数が変わったら全再構築（末尾で UpdateRobotRow も呼ぶ）
        }
        else
        {
            jointNames = names;
            UpdateRobotRow();
        }
        InitGoalFromCurrent();
        RefreshButtons(planner.State);
    }

    /// <summary>ロボット名ラベル/前後ボタンの活性を現状に合わせる。</summary>
    private void UpdateRobotRow()
    {
        int n = registry != null ? registry.Robots.Count : 0;
        if (robotNameText != null)
        {
            var sel = registry != null ? registry.Selected : null;
            robotNameText.text = (sel != null) ? $"{sel.DisplayName}  ({registry.SelectedIndex + 1}/{n})" : "ロボット: -";
        }
        bool multi = n > 1;
        if (robotPrevBtn != null) { robotPrevBtn.interactable = multi; }
        if (robotNextBtn != null) { robotNextBtn.interactable = multi; }
    }

    private void UpdateGoalText()
    {
        if (goalText == null || goalDeg == null)
        {
            return;
        }
        var sb = new System.Text.StringBuilder("ゴール: ");
        for (int i = 0; i < goalDeg.Length; i++)
        {
            if (i > 0) { sb.Append(','); }
            sb.Append(goalDeg[i].ToString("F0"));
        }
        goalText.text = sb.ToString();
    }

    private void EnsureKin()
    {
        if (targetKin != null)
        {
            return;
        }
        if (registry != null && registry.Selected != null)
        {
            targetKin = registry.Selected.Target;
            return;
        }
        // 後方互換フォールバック：シーンの最初の Kinematics6D。
        var kins = FindObjectsByType<Kinematics6D>(FindObjectsSortMode.None);
        if (kins != null && kins.Length > 0)
        {
            targetKin = kins[0];
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
