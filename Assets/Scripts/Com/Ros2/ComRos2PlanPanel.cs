using System.Collections.Generic;
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
    private Text seekTimeLabel;   // 再生中の時間（現在/総 秒）。シークバー右
    private Slider returnSpeedSlider;   // 復帰(通常計画)の速度倍率スライダー（実行中に調整可）
    private Text returnSpeedLabel;      // 「復帰速度 XX%」
    private Text goalText;                                            // 旧ゴール表示（撤去・未使用）
    private Text curText;                                             // ロボットの現在関節角（ライブ表示・旧ゴール行の位置）
    private Text commText;                                            // ROS 状態（ROS2起動＋TCP接続 統合・タイトルバー右）
    private Button startBtn, stopBtn, restartBtn;                     // 起動/停止/再起動
    private Slider[] sliders = new Slider[0];
    private InputField[] sliderInputs = new InputField[0];   // 角度の直接入力（関節数ぶん）
    private Button setGoalBtn, planBtn, okBtn, ngBtn, stopSearchBtn;
    private bool stopSearchLatched;   // 探索停止押下→データ返信まで再押下を無効化するラッチ
    private const int IconPlay = 0xe037;                             // MaterialIcons play_arrow
    private const int IconPause = 0xe034;                            // MaterialIcons pause
    private Slider seekSlider;                                       // ゴースト再生のシーク（経路スクラブ確認・Preview中のみ）
    private Button seekPlayBtn;                                      // 再生/一時停止（シークバー左）
    private Text seekPlayLabel;                                      // ▶（一時停止中に表示・再生アイコン）
    private GameObject seekPauseIcon;                                // 一時停止アイコン（縦2本バー・MaterialIcons未取得時のフォールバック）
    private bool seekUseIconFont;                                    // MaterialIcons で / を使うか
    private Toggle registerModeToggle;                               // 自動再生/登録 の切替
    private Text registerModeLabel;                                  // トグルのラベル（選択テーブル名を表示）
    private int selectedStep = 0;                                    // 登録モードで選択中のテーブル（既定=先頭）
    private readonly List<GameObject> stepRows = new();              // ステップ一覧の行（登録モードで表示）
    private readonly List<Text> stepStatusTexts = new();             // 各行の登録状態ラベル
    private readonly List<Button> stepButtons = new();               // 各行の 登録/解除/再生 ボタン（登録保留中は無効化）
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
        // ロボットの現在関節角（ライブ）を表示（旧ゴール行の位置）。
        if (curText != null && GlobalScript.isLoaded)
        {
            var cur = planner.ReadCurrentDeg();
            var sb = new System.Text.StringBuilder("現在: ");
            if (cur != null)
            {
                for (int i = 0; i < cur.Length; i++)
                {
                    if (i > 0) { sb.Append(','); }
                    sb.Append(cur[i].ToString("F0"));
                }
            }
            curText.text = sb.ToString();
        }
        // ROS 状態（タイトルバー右）：ROS2 起動状態＋TCP接続 を1つに統合表示。
        if (commText != null)
        {
            bool up = planner.IsLinkUp;
            string lbl;
            Color c;
            if (launcher != null)
            {
                switch (launcher.State)
                {
                    case ComRos2Launcher.LaunchState.RunningFull:
                        lbl = up ? "ROS2 ●稼働・接続" : "ROS2 ●稼働・未接続";
                        c = up ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.85f, 0.2f);
                        break;
                    case ComRos2Launcher.LaunchState.Starting:
                        lbl = "ROS2 ●起動中…"; c = new Color(1f, 0.85f, 0.2f); break;
                    case ComRos2Launcher.LaunchState.Stopped:
                        lbl = "ROS2 ●停止"; c = new Color(0.7f, 0.7f, 0.7f); break;
                    default:
                        lbl = "ROS2 ●不明"; c = new Color(0.6f, 0.6f, 0.6f); break;
                }
                if (launcher.Busy) { lbl += "(処理中)"; }
            }
            else
            {
                lbl = up ? "ROS ●接続" : "ROS ●未接続";
                c = up ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.45f, 0.45f);
            }
            commText.text = lbl;
            commText.color = c;
        }
        // ROS2 起動/停止/再起動ボタンの活性のみ更新（状態表示は上の commText に統合）。
        if (launcher != null)
        {
            UpdateLaunchUi();
        }
        // ゴースト再生シークバーを進捗に追従（手動スクラブ/一時停止中はその位置で固定）＋再生ボタン表示更新。
        if (planner.State == ComRos2PathPlanner.PlanState.Preview && planner.GhostActive)
        {
            if (seekSlider != null) { seekSlider.SetValueWithoutNotify(planner.GhostPreviewT01); }
            // 現在 / 所要時間(=動作時間)。種別ラベル(設定/最短)は付けない（ステータス行に出る）。
            if (seekTimeLabel != null) { seekTimeLabel.text = $"{planner.GhostTimeSec:F1}/{planner.GhostDurationSec:F1}s"; }
            bool gplaying = planner.GhostPlaying;
            if (seekUseIconFont)
            {
                // MaterialIcons: 再生中=pause() / 一時停止中=play_arrow()
                if (seekPlayLabel != null) { seekPlayLabel.text = gplaying ? ((char)IconPause).ToString() : ((char)IconPlay).ToString(); }
            }
            else
            {
                if (seekPlayLabel != null) { seekPlayLabel.enabled = !gplaying; }   // 一時停止中=▶
                if (seekPauseIcon != null) { seekPauseIcon.SetActive(gplaying); }   // 再生中=||バー
            }
        }
        // 計画中の表示。登録最適化の途中経過(opt行)受信中はそれを優先表示、
        // それ以外は 予算>0 なら残り時間、0(ROS2既定で総量不明)なら経過時間。
        if (planner.State == ComRos2PathPlanner.PlanState.Planning && statusText != null)
        {
            if (planner.OptActive)
            {
                // 探索/最適化の途中経過＋総経過時間（毎フレーム更新）。例: 探索中 最良3.20s (42回) 経過18s
                statusText.text = $"{planner.OptProgress} 経過{planner.PlanElapsedSec:F0}s";
            }
            else
            {
                float el = planner.PlanElapsedSec;
                statusText.text = displayBudgetSec > 0.0
                    ? $"計画中…  残り {Mathf.Max(0f, (float)displayBudgetSec - el):F1}s"
                    : $"計画中…  {el:F1}s 経過";
            }
            // 探索中は停止ボタンを表示（OptSearching は状態遷移なしで変わり得るため毎フレーム反映）。
            // 停止押下後(ラッチ)はデータ返信まで無効化。探索が終われば(OptSearching=false)ラッチ解除。
            if (!planner.OptSearching) { stopSearchLatched = false; }
            if (stopSearchBtn != null)
            {
                stopSearchBtn.gameObject.SetActive(planner.OptSearching);
                stopSearchBtn.interactable = planner.OptSearching && !stopSearchLatched;
            }
        }

        // 登録の保留中（登録押下→OK実行/NGキャンセルまで）は 登録/解除/再生 を無効化（多重実行・誤操作防止）。
        if (stepButtons.Count > 0)
        {
            bool lockSteps = planner != null && planner.RegisterPending;
            for (int i = 0; i < stepButtons.Count; i++)
            {
                if (stepButtons[i] != null) { stepButtons[i].interactable = !lockSteps; }
            }
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
            y -= 32f;
        }

        // ロボットの現在関節角（ライブ）。スライダー=ゴール、この行=現在 の対比用。
        curText = MakeLabel(panel, "cur", "現在: -", 13, new Vector2(8f, y), W - 16f, 20f);
        y -= 24f;

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

        // 復帰(通常計画)の速度倍率。実行中も調整可。計画発行時に speed_scale として送る（登録には送らない）。
        float rs = planner != null ? planner.ReturnSpeedScale : 0.25f;
        returnSpeedLabel = MakeLabel(panel, "retSpdLbl", $"復帰速度 {rs * 100f:F0}%", 13, new Vector2(8f, y), 108f, 22f);
        returnSpeedSlider = MakeSlider(panel, "retSpd", new Vector2(118f, y), W - 16f - 110f, 18f);
        returnSpeedSlider.minValue = 0.05f;
        returnSpeedSlider.maxValue = 1.0f;
        returnSpeedSlider.SetValueWithoutNotify(rs);
        returnSpeedSlider.onValueChanged.AddListener(OnReturnSpeedChanged);
        y -= 28f;

        // ボタン
        setGoalBtn = MakeButton(panel, "SetGoal", "ゴール設定", new Vector2(8f, y), 168f, 34f, ToggleGoalSet);
        planBtn = MakeButton(panel, "Plan", "計画", new Vector2(184f, y), 168f, 34f, OnPlan);
        y -= 40f;
        // 計画/再生の状態＋Step A 速度解析（空のときは非表示同然）。OK/NG の直上に置く。
        statusText = MakeLabel(panel, "status", "", 14, new Vector2(8f, y), W - 16f, 22f);
        // 解析結果は長くなりがち（所要/設定/軸速/加速G/警告）。枠からはみ出さないよう自動縮小。
        statusText.resizeTextForBestFit = true;
        statusText.resizeTextMinSize = 9;
        statusText.resizeTextMaxSize = 14;
        y -= 24f;
        // ゴースト再生の 再生/一時停止 ＋ シークバー（経路スクラブ確認）。Preview 中のみ表示。
        seekPlayBtn = MakeButton(panel, "seekPlay", "▶", new Vector2(8f, y), 34f, 18f, OnSeekPlayToggle);
        seekPlayLabel = seekPlayBtn.GetComponentInChildren<Text>();
        var iconFont = GetIconFont();
        if (iconFont != null)
        {
            // MaterialIcons で play_arrow()/pause() を表示。
            seekUseIconFont = true;
            seekPlayLabel.font = iconFont;
            seekPlayLabel.fontSize = 18;
            seekPlayLabel.text = "";
        }
        else
        {
            // フォント未取得時のフォールバック：▶(テキスト)＋一時停止は縦2本バー(Image)。
            seekPauseIcon = MakePauseIcon((RectTransform)seekPlayBtn.transform);
            seekPauseIcon.SetActive(false);
        }
        // シークバー（右に再生時間ラベル 68px 分を確保して幅を詰める。ラベルは 現在/種別+総 秒）。
        seekSlider = MakeSlider(panel, "seek", new Vector2(46f, y), W - 16f - 38f - 68f, 18f);
        seekSlider.minValue = 0f;
        seekSlider.maxValue = 1f;
        seekSlider.value = 0f;
        seekSlider.onValueChanged.AddListener(OnSeek);
        // 再生中の時間（現在 / 動作時間[最短 or 設定]）。スライダー右端。長い時は自動縮小。
        seekTimeLabel = MakeLabel(panel, "seekTime", "", 12, new Vector2(W - 72f, y), 68f, 18f);
        seekTimeLabel.alignment = TextAnchor.MiddleRight;
        seekTimeLabel.resizeTextForBestFit = true;
        seekTimeLabel.resizeTextMinSize = 8;
        seekTimeLabel.resizeTextMaxSize = 12;
        y -= 24f;
        okBtn = MakeButton(panel, "OK", "OK 実行", new Vector2(8f, y), 168f, 34f, OnOk);
        ngBtn = MakeButton(panel, "NG", "NG 破棄", new Vector2(184f, y), 168f, 34f, OnNg);
        // 登録の長時間探索中に表示（OK/NG と排他）。押すと現在の最良(最短)で確定。
        stopSearchBtn = MakeButton(panel, "StopSearch", "探索停止（最良を採用）", new Vector2(8f, y), W - 16f, 34f, OnStopSearch);
        stopSearchBtn.gameObject.SetActive(false);
        y -= 44f;

        // --- robotSteps シーケンス（自動再生／登録モード切替＋ステップ一覧） ---
        var seqSep = MakeLabel(panel, "seqSep", "― ステップ再生 ―", 13, new Vector2(8f, y), W - 16f, 20f);
        seqSep.alignment = TextAnchor.MiddleCenter;
        y -= 24f;
        registerModeToggle = MakeToggle(panel, "togRegister", "登録モード（ロボ停止・教示）", false,
            new Vector2(8f, y), OnRegisterModeChanged);
        registerModeLabel = registerModeToggle.GetComponentInChildren<Text>();   // ラベル（選択テーブル名を出す）
        y -= 28f;
        BuildStepRows(panel, ref y);
        UpdateRegisterLabel();

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

    private static Font iconFontCache;
    /// <summary>MaterialIcons フォントを実行時取得（プレハブ等で読込済みのものを探す）。無ければ null。</summary>
    private static Font GetIconFont()
    {
        if (iconFontCache != null)
        {
            return iconFontCache;
        }
        foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
        {
            if (f != null && f.name.IndexOf("MaterialIcons", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                iconFontCache = f;
                return f;
            }
        }
        return null;   // 未検出（フォールバックへ）
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

    /// <summary>一時停止アイコン（縦2本バー）を Image で作りボタン中央に置く（フォント非依存）。</summary>
    private GameObject MakePauseIcon(RectTransform parent)
    {
        var go = MakeRect("PauseIcon", parent);
        go.anchorMin = new Vector2(0.5f, 0.5f);
        go.anchorMax = new Vector2(0.5f, 0.5f);
        go.pivot = new Vector2(0.5f, 0.5f);
        go.anchoredPosition = Vector2.zero;
        go.sizeDelta = new Vector2(12f, 12f);
        for (int k = 0; k < 2; k++)
        {
            var bar = MakeRect("bar", go);
            bar.anchorMin = new Vector2(0.5f, 0.5f);
            bar.anchorMax = new Vector2(0.5f, 0.5f);
            bar.pivot = new Vector2(0.5f, 0.5f);
            bar.anchoredPosition = new Vector2(k == 0 ? -3f : 3f, 0f);
            bar.sizeDelta = new Vector2(3.5f, 12f);
            var img = bar.gameObject.AddComponent<Image>();
            img.color = Color.white;
            img.raycastTarget = false;   // クリックはボタン本体で拾う
        }
        return go.gameObject;
    }
    #endregion UI 構築

    #region 操作
    private void ToggleGoalSet()
    {
        // 登録モード：選択テーブルの ゴール位置(poseDeg) と 開始位置(前step終了) をトグルで切替表示して確認。
        //   1回目＝ゴール姿勢 / もう一度＝解除して開始姿勢へ戻す。
        if (registerModeToggle != null && registerModeToggle.isOn)
        {
            if (planner != null) { planner.CancelPlan(); }   // ゴースト消去
            EnsureKin();
            goalSetMode = !goalSetMode;
            var steps = SelectedSteps();
            if (steps != null && steps.Count > 0 && selectedStep >= 0 && selectedStep < steps.Count)
            {
                if (goalSetMode)
                {
                    // ゴール姿勢（このテーブルの終了姿勢）
                    PoseRobotAt(steps[selectedStep] != null ? steps[selectedStep].poseDeg : null);
                }
                else
                {
                    // 解除 → 開始姿勢（前stepの終了・循環）へ戻す
                    int prev = (selectedStep - 1 + steps.Count) % steps.Count;
                    PoseRobotAt(steps[prev] != null ? steps[prev].poseDeg : null);
                }
            }
            SetButtonColor(setGoalBtn, goalSetMode
                ? new Color(0.8f, 0.5f, 0.1f, 0.95f)     // ゴール表示中=オレンジ
                : new Color(0.2f, 0.4f, 0.7f, 0.95f));   // 開始表示=通常
            return;
        }
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
        if ((goalSetMode || (registerModeToggle != null && registerModeToggle.isOn)) && targetKin != null)
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
            if ((goalSetMode || (registerModeToggle != null && registerModeToggle.isOn)) && targetKin != null)
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

    /// <summary>探索停止：ROS2 に現在の最良(最短)で確定させる（登録の長時間探索中のみ）。</summary>
    private void OnStopSearch()
    {
        if (planner != null)
        {
            planner.RequestStopSearch();
        }
        // 確定→軌道受信まで少し掛かるので、その間は再押下不可にする（二重送信・誤操作防止）。
        stopSearchLatched = true;
        if (stopSearchBtn != null) { stopSearchBtn.interactable = false; }
    }

    /// <summary>復帰速度スライダー：復帰(通常計画)の速度倍率を実行中に設定（次の計画発行から反映）。</summary>
    private void OnReturnSpeedChanged(float v)
    {
        if (planner != null) { planner.ReturnSpeedScale = v; }
        if (returnSpeedLabel != null) { returnSpeedLabel.text = $"復帰速度 {v * 100f:F0}%"; }
    }

    /// <summary>シークバー操作：ゴーストを 0..1 の位置へ手動スクラブ（＝一時停止）。</summary>
    private void OnSeek(float v)
    {
        if (planner != null)
        {
            planner.SetGhostSeek(v);   // シーク使用時は一時停止（自動送り停止）
        }
    }

    /// <summary>再生/一時停止トグル（シークバー左のボタン）。</summary>
    private void OnSeekPlayToggle()
    {
        if (planner != null)
        {
            planner.SetGhostPlaying(!planner.GhostPlaying);
        }
    }

    private void OnPlanState(ComRos2PathPlanner.PlanState s, string msg)
    {
        if (statusText != null && s != ComRos2PathPlanner.PlanState.Planning)
        {
            // Idle（待機/モード切替/キャンセル/完了）は空表示。計画中/成功/再生+速度のみ出す。
            statusText.text = (s == ComRos2PathPlanner.PlanState.Idle) ? "" : msg;
        }
        RefreshButtons(s);
        // 登録モードなら、登録/削除の反映（状態遷移＝保存完了 等）でステップ状態を更新。
        if (registerModeToggle != null && registerModeToggle.isOn)
        {
            RefreshStepRows();
        }
    }

    private void RefreshButtons(ComRos2PathPlanner.PlanState s)
    {
        bool preview = s == ComRos2PathPlanner.PlanState.Preview;
        if (okBtn != null) { okBtn.gameObject.SetActive(preview); }
        if (ngBtn != null) { ngBtn.gameObject.SetActive(preview); }
        if (seekSlider != null) { seekSlider.gameObject.SetActive(preview); }   // シークバーは Preview 中のみ
        if (seekPlayBtn != null) { seekPlayBtn.gameObject.SetActive(preview); }   // 再生/一時停止も Preview 中のみ
        if (seekTimeLabel != null) { seekTimeLabel.gameObject.SetActive(preview); }   // 再生時間も Preview 中のみ
        if (planBtn != null) { planBtn.interactable = s != ComRos2PathPlanner.PlanState.Planning; }
        // 停止ボタンは「登録の探索中」のみ表示（実際の可視制御は Update でも毎フレーム更新）。
        bool searching = s == ComRos2PathPlanner.PlanState.Planning && planner != null && planner.OptSearching;
        if (!searching) { stopSearchLatched = false; }   // 探索終了でラッチ解除（次回は押せる）
        if (stopSearchBtn != null)
        {
            stopSearchBtn.gameObject.SetActive(searching);
            stopSearchBtn.interactable = searching && !stopSearchLatched;
        }
    }

    /// <summary>起動/停止/再起動ボタンの活性のみ更新（状態表示は commText に統合）。</summary>
    private void UpdateLaunchUi()
    {
        bool busy = launcher.Busy;
        var st = launcher.State;
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
        jointNames = names;
        // 関節数・ステップ一覧を選択ロボに追従させるため毎回全再構築（表示状態は復元）。
        bool wasVisible = IsVisible;
        BuildUI();
        SetVisible(wasVisible);
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

    // --- ステップ再生（自動再生／登録モード） ---
    private void OnRegisterModeChanged(bool on)
    {
        if (planner != null)
        {
            planner.SetMode(on ? ComRos2PathPlanner.SeqMode.Register : ComRos2PathPlanner.SeqMode.Auto);
        }
        SetStepRowsVisible(on);
        EnsureKin();
        if (on)
        {
            RefreshStepRows();
            var steps = SelectedSteps();
            if (steps != null && steps.Count > 0)
            {
                if (selectedStep < 0 || selectedStep >= steps.Count) { selectedStep = 0; }
                OnSelectStep(selectedStep);   // 実位置でなくテーブル位置(選択=既定先頭の開始点)へ＋ラベル更新
            }
            else
            {
                UpdateRegisterLabel();
            }
        }
        else
        {
            // 自動再生へ戻る：手動姿勢を解除して実位置へ（ゴーストは SetMode 側で消える）。
            if (targetKin != null) { targetKin.SetManual(false); }
            goalSetMode = false;
            SetButtonColor(setGoalBtn, new Color(0.2f, 0.4f, 0.7f, 0.95f));
            UpdateRegisterLabel();
        }
    }

    /// <summary>テーブル名クリック：そのテーブルを選択し、ロボを開始点(前step終了)へ置いて確認する。</summary>
    private void OnSelectStep(int stepIdx)
    {
        selectedStep = stepIdx;
        if (planner != null) { planner.CancelPlan(); }   // 再生中のゴーストを消して実機表示へ
        EnsureKin();
        goalSetMode = false;
        SetButtonColor(setGoalBtn, new Color(0.2f, 0.4f, 0.7f, 0.95f));
        var steps = SelectedSteps();
        if (steps != null && steps.Count > 0 && stepIdx >= 0 && stepIdx < steps.Count)
        {
            int prev = (stepIdx - 1 + steps.Count) % steps.Count;   // 開始点＝前stepの終了（循環）
            PoseRobotAt(steps[prev] != null ? steps[prev].poseDeg : null);
        }
        UpdateRegisterLabel();
    }

    private void OnRegisterStep(int robotIdx, int stepIdx)
    {
        if (planner != null)
        {
            planner.RegisterStep(robotIdx, stepIdx);   // 計画→ゴーストプレビュー→OK で保存
        }
    }

    private void OnDeleteStep(int robotIdx, int stepIdx)
    {
        if (planner != null)
        {
            planner.DeleteStepCache(robotIdx, stepIdx);   // 解除（登録キャッシュ削除）
        }
        RefreshStepRows();
    }

    private void OnPlayStep(int robotIdx, int stepIdx)
    {
        if (planner != null)
        {
            planner.PlayStepGhost(robotIdx, stepIdx);   // ゴーストでレビュー再生（実機は動かさない）
        }
    }

    /// <summary>選択ロボの robotSteps 行を構築（登録モードで表示）。</summary>
    private void BuildStepRows(RectTransform panel, ref float y)
    {
        stepRows.Clear();
        stepStatusTexts.Clear();
        stepButtons.Clear();
        const float W = 360f;
        var sel = registry != null ? registry.Selected : null;
        var steps = (sel != null && sel.Target != null) ? sel.Target.PlanSteps : null;
        int robotIdx = registry != null ? registry.SelectedIndex : -1;
        if (steps == null || steps.Count == 0)
        {
            var none = MakeLabel(panel, "stepNone", "（このロボにステップ未定義）", 12, new Vector2(8f, y), W - 16f, 20f);
            stepRows.Add(none.gameObject);
            y -= 22f;
            SetStepRowsVisible(false);
            return;
        }
        if (selectedStep < 0 || selectedStep >= steps.Count) { selectedStep = 0; }   // 既定=先頭
        for (int i = 0; i < steps.Count; i++)
        {
            int si = i;
            var row = MakeRect($"stepRow{i}", panel);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(0f, 1f);
            row.pivot = new Vector2(0f, 1f);
            row.anchoredPosition = new Vector2(0f, y);
            row.sizeDelta = new Vector2(W, 24f);
            string nm = (steps[i] != null && !string.IsNullOrEmpty(steps[i].name)) ? steps[i].name : $"step{i}";
            MakeLabel(row, "no", i.ToString(), 12, new Vector2(8f, 0f), 18f, 24f);
            MakeButton(row, "nm", nm, new Vector2(26f, 1f), 90f, 22f, () => OnSelectStep(si));   // 名前クリック=開始点へ
            var st = MakeLabel(row, "st", "", 12, new Vector2(118f, 0f), 42f, 24f);
            stepButtons.Add(MakeButton(row, "reg", "登録", new Vector2(162f, 1f), 62f, 22f, () => OnRegisterStep(robotIdx, si)));
            stepButtons.Add(MakeButton(row, "rel", "解除", new Vector2(226f, 1f), 62f, 22f, () => OnDeleteStep(robotIdx, si)));
            stepButtons.Add(MakeButton(row, "play", "再生", new Vector2(290f, 1f), 62f, 22f, () => OnPlayStep(robotIdx, si)));
            stepRows.Add(row.gameObject);
            stepStatusTexts.Add(st);
            y -= 26f;
        }
        RefreshStepRows();
        SetStepRowsVisible(false);   // 既定=自動再生モードなので隠す
    }

    /// <summary>各ステップ行の登録状態（登録済/未登録）を更新する。</summary>
    private void RefreshStepRows()
    {
        int robotIdx = registry != null ? registry.SelectedIndex : -1;
        for (int i = 0; i < stepStatusTexts.Count; i++)
        {
            bool has = planner != null && planner.HasStepCache(robotIdx, i);
            if (stepStatusTexts[i] != null)
            {
                stepStatusTexts[i].text = has ? "登録済" : "未登録";
                stepStatusTexts[i].color = has ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.6f, 0.3f);
            }
        }
    }

    private void SetStepRowsVisible(bool v)
    {
        foreach (var g in stepRows)
        {
            if (g != null)
            {
                g.SetActive(v);
            }
        }
    }

    private IReadOnlyList<Parameters.Ros2RobotStep> SelectedSteps()
    {
        var sel = registry != null ? registry.Selected : null;
        return (sel != null && sel.Target != null) ? sel.Target.PlanSteps : null;
    }

    /// <summary>指定関節角(度)にロボを表示し、J1~J6 スライダー/入力/goalDeg も同値に更新する
    /// （確認用。以後スライダーを触ると微調整でき、ロボも追従する）。</summary>
    private void PoseRobotAt(List<float> poseDeg)
    {
        EnsureKin();
        if (poseDeg == null || poseDeg.Count == 0)
        {
            return;
        }
        int n = (jointNames != null && jointNames.Length > 0) ? jointNames.Length : 6;
        if (goalDeg == null || goalDeg.Length != n)
        {
            goalDeg = new double[n];
        }
        for (int i = 0; i < n; i++)
        {
            double v = (i < poseDeg.Count) ? poseDeg[i] : 0d;
            goalDeg[i] = v;
            if (i < sliders.Length && sliders[i] != null) { sliders[i].SetValueWithoutNotify((float)v); }
            if (i < sliderInputs.Length && sliderInputs[i] != null) { sliderInputs[i].SetTextWithoutNotify(v.ToString("F1")); }
        }
        if (targetKin != null)
        {
            targetKin.SetManual(true);
            targetKin.SetManualJointsDeg(goalDeg);
        }
    }

    /// <summary>登録モードのラベルを "登録モード(ロボ停止・教示)：テーブル名" に更新する。</summary>
    private void UpdateRegisterLabel()
    {
        if (registerModeLabel == null)
        {
            return;
        }
        bool on = registerModeToggle != null && registerModeToggle.isOn;
        if (!on)
        {
            registerModeLabel.text = "登録モード（ロボ停止・教示）";
            return;
        }
        var steps = SelectedSteps();
        string nm = "-";
        if (steps != null && selectedStep >= 0 && selectedStep < steps.Count)
        {
            var st = steps[selectedStep];
            nm = (st != null && !string.IsNullOrEmpty(st.name)) ? st.name : $"step{selectedStep}";
        }
        registerModeLabel.text = $"登録モード(ロボ停止・教示)：{nm}";
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
