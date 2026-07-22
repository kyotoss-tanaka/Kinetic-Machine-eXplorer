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

    // Cartesian JOG（X/Y/Z/RX/RY/RZ をスライダーで数値IK目標設定・「現在」行の右端チェック）
    private Toggle jogToggle;
    private bool jogMode;
    private GameObject tcpMarker;                   // JOG中に TCP(ヘッドオフセット点=吸盤)の位置・向きを可視化する球＋XYZ軸
    private bool suppressRowCallbacks;              // 行の min/max/value 一括更新中はスライダー/入力の誤発火を無視
    private Text[] rowLabels = new Text[0];         // J1..J6 ↔ X/Y/Z/RX/RY/RZ でラベル切替
    private double[] cartVals = new double[6];       // JOG時の X,Y,Z(mm) / RX,RY,RZ(deg・base軸まわりの累積ジョグ量)
    private Quaternion cartRot = Quaternion.identity;  // JOG目標のTCP姿勢(base相対)。回転は絶対3角度でなくこれに base軸デルタを積む(ジンバルロック回避)
    private static readonly string[] CartLabels = { "X", "Y", "Z", "RX", "RY", "RZ" };
    private const float CartPosRange = 2500f;       // 位置スライダー範囲(±mm)
    private const float CartRotRange = 180f;        // 姿勢スライダー範囲(±deg)

    private Text statusText;
    private GameObject bestTooltipGo;     // 「最良」クリックで出す 経路時間ベスト10 ツールチップ
    private Text bestTooltipText;
    private Text seekTimeLabel;   // 再生中の時間（現在/総 秒）。シークバー右
    private Slider returnSpeedSlider;   // 復帰(通常計画)の速度倍率スライダー（実行中に調整可）
    private Text returnSpeedLabel;      // 「復帰速度 XX%」
    private Slider progressBar;         // 登録最適化の進捗バー（探索/STOMP候補・OptProgress01連動）
    private Text goalText;                                            // 旧ゴール表示（撤去・未使用）
    private Text curText;                                             // ロボットの現在関節角（ライブ表示・旧ゴール行の位置）
    private double[] curDisplayDeg;                                   // 「現在:」表示値。ゴール表示中は更新せず保持（復帰=現在/登録=始点）
    private Text commText;                                            // ROS 状態（ROS2起動＋TCP接続 統合・タイトルバー右）
    private Button startBtn, stopBtn, restartBtn;                     // 起動/停止/再起動
    private Slider[] sliders = new Slider[0];
    private InputField[] sliderInputs = new InputField[0];   // 角度の直接入力（関節数ぶん）
    private Button setGoalBtn, planBtn, okBtn, ngBtn, stopSearchBtn;
    private Button csvExportBtn;  // 現在の経路を FANUC 汎用再生用 CSV(関節角)に出力（Karel/TPが読む）
    private InputField csvProductInput, csvPathInput;   // CSV命名: 品番(R[89]) / パス番号(R[88]・0=復帰)
    private Button dcsReloadBtn;  // DCS安全ゾーン(SafetyZoneInfo.json)を再読込して再描画
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
    private Button switchRobotBtn;                   // 「この機種でROS起動/切替」
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
        if (tcpMarker != null) { Destroy(tcpMarker); tcpMarker = null; }   // TCPマーカーも掃除
    }

    /// <summary>パネル本体の表示/非表示（Canvas と EventSystem は常時活性のまま＝他UIの入力を止めない）。</summary>
    public void SetVisible(bool v)
    {
        if (panelRootGo != null)
        {
            panelRootGo.SetActive(v);
        }
        // パネルを開いたら DCS安全ゾーンを読み直す。F5直後のバインドでは base(crx) 未確定で MatchZone が
        // 成立せず結線できないため、ロード後のこのタイミングで確実に受信・再描画する（非破壊：取れなければ既存維持）。
        // あわせて対象ロボットの正面へカメラを寄せ、回転中心を TCP にする（表示時・機種切替の再表示時とも）。
        if (v) { ReceiveDcsZones(); FocusCameraOnRobot(); }
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
        UpdateTcpMarker();   // JOG中は TCP(吸盤点)マーカーを実TCP姿勢に追従表示（OFFで消す）。
        // ロボットの現在関節角を表示（旧ゴール行の位置）。
        // ★ゴール表示中(goalSetMode)はモデルが goalDeg 姿勢のため ReadCurrentDeg が goalDeg を返す（多ロボは Kinematics 直読み）。
        //   そこで「ゴール表示でない時だけ」値を更新して保持する＝復帰モードは現在角度／登録モードは始点(選択step開始姿勢)のまま。
        if (curText != null && GlobalScript.isLoaded)
        {
            // 登録モードは 現在:＝開始姿勢 を固定表示（OnSelectStep でセット）。復帰は goalSetMode 中のみ固定。
            bool regMode = registerModeToggle != null && registerModeToggle.isOn;
            if ((!goalSetMode && !regMode) || curDisplayDeg == null)
            {
                curDisplayDeg = planner.ReadCurrentDeg();
            }
            var cur = curDisplayDeg;
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
        // ROS 未接続（TCP未確立）なら計画不可。毎フレーム IsLinkUp を反映して計画ボタンを活性/非活性。
        if (planBtn != null)
        {
            planBtn.interactable = planner.IsLinkUp
                && planner.State != ComRos2PathPlanner.PlanState.Planning;
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
                string phase = planner.PlanPhaseText;
                if (!string.IsNullOrEmpty(phase))
                {
                    // ROS のフェーズ通知（経路計画1/2・後処理）＋総経過。
                    statusText.text = $"{phase}  経過{el:F0}s";
                }
                else
                {
                    statusText.text = displayBudgetSec > 0.0
                        ? $"計画中…  残り {Mathf.Max(0f, (float)displayBudgetSec - el):F1}s"
                        : $"計画中…  {el:F1}s 経過";
                }
            }
            // 探索中は停止ボタンを表示（OptSearching は状態遷移なしで変わり得るため毎フレーム反映）。
            // 停止押下後(ラッチ)はデータ返信まで無効化。探索が終われば(OptSearching=false)ラッチ解除。
            if (!planner.OptSearching) { stopSearchLatched = false; }
            if (stopSearchBtn != null)
            {
                stopSearchBtn.gameObject.SetActive(planner.OptSearching);
                stopSearchBtn.interactable = planner.OptSearching && !stopSearchLatched;
            }
            // 進捗バー：opt行受信中(OptActive)のみ表示し OptProgress01(探索=低め/STOMP=prog%)を反映。
            if (progressBar != null)
            {
                progressBar.gameObject.SetActive(planner.OptActive);
                if (planner.OptActive) { progressBar.SetValueWithoutNotify(planner.OptProgress01); }
            }
            // 探索が終わったらベスト10ツールチップは閉じる（バッファは次計画でリセット）。
            if (bestTooltipGo != null && bestTooltipGo.activeSelf && !planner.OptActive)
            {
                bestTooltipGo.SetActive(false);
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

        // 「この機種でROS起動/切替」：選択機体の robot_model で bringup を(再)起動→再接続→scene再送。
        if (launcher != null)
        {
            switchRobotBtn = MakeButton(panel, "RobotSwitch", "この機種でROS起動/切替", new Vector2(8f, y), W - 16f, 26f, OnSwitchRobot);
            y -= 30f;
        }

        // ROS2 起動制御（起動/停止/再起動＋状態ランプ）。ランチャがある時だけ表示。
        if (launcher != null)
        {
            startBtn = MakeButton(panel, "RosStart", "起動", new Vector2(8f, y), 60f, 26f, OnStartRos2);
            stopBtn = MakeButton(panel, "RosStop", "停止", new Vector2(72f, y), 60f, 26f, OnStopRos2);
            restartBtn = MakeButton(panel, "RosRestart", "再起動", new Vector2(136f, y), 72f, 26f, OnRestartRos2);
            // DCS安全ゾーン再読込は同じ行の一番右に寄せる（表記「DCS読込」）。
            dcsReloadBtn = MakeButton(panel, "DcsReload", "DCS読込", new Vector2(W - 92f, y), 84f, 26f, OnReloadDcs);
            y -= 32f;
        }

        // ロボットの現在関節角（ライブ）。スライダー=ゴール、この行=現在 の対比用。
        curText = MakeLabel(panel, "cur", "現在: -", 13, new Vector2(8f, y), W - 16f - 66f, 20f);
        // Cartesian JOG 切替（現在行の右端）。ON で J1..J6 行が X/Y/Z/RX/RY/RZ の数値IKジョグになる。
        jogToggle = MakeToggle(panel, "togJog", "JOG", false, new Vector2(W - 66f, y), OnJogToggle);
        y -= 24f;

        // 関節スライダー＋直接入力（選択ロボの関節数ぶん・可変。6軸以上）
        int nJoints = (jointNames != null && jointNames.Length > 0) ? jointNames.Length : DefaultJointNames.Length;
        if (goalDeg == null || goalDeg.Length != nJoints) { goalDeg = new double[nJoints]; }
        sliders = new Slider[nJoints];
        sliderInputs = new InputField[nJoints];
        rowLabels = new Text[nJoints];
        for (int i = 0; i < nJoints; i++)
        {
            int idx = i;
            string jn = (jointNames != null && i < jointNames.Length) ? jointNames[i] : $"J{i + 1}";
            rowLabels[i] = MakeLabel(panel, $"lbl{i}", jn, 14, new Vector2(8f, y), 40f, 22f);
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
        planBtn = MakeButton(panel, "Plan", "復帰計画", new Vector2(184f, y), 168f, 34f, OnPlan);
        y -= 40f;
        // 計画/再生の状態＋Step A 速度解析（空のときは非表示同然）。OK/NG の直上に置く。
        statusText = MakeLabel(panel, "status", "", 14, new Vector2(8f, y), W - 16f, 22f);
        // 解析結果は長くなりがち（所要/設定/軸速/加速G/警告）。枠からはみ出さないよう自動縮小。
        statusText.resizeTextForBestFit = true;
        statusText.resizeTextMinSize = 9;
        statusText.resizeTextMaxSize = 14;
        // 「最良」等のステータスをクリックすると、探索中バッファのベスト10（昇順）をツールチップ表示。
        var statusBtn = statusText.gameObject.AddComponent<Button>();
        statusBtn.transition = Selectable.Transition.None;
        statusBtn.targetGraphic = statusText;
        statusBtn.onClick.AddListener(OnBestTooltipToggle);
        var tipRt = MakeRect("bestTooltip", panel);
        tipRt.anchorMin = new Vector2(0f, 1f);
        tipRt.anchorMax = new Vector2(0f, 1f);
        tipRt.pivot = new Vector2(0f, 1f);
        tipRt.anchoredPosition = new Vector2(8f, y - 24f);
        tipRt.sizeDelta = new Vector2(170f, 156f);
        var tipBg = tipRt.gameObject.AddComponent<Image>();
        tipBg.color = new Color(0.05f, 0.05f, 0.08f, 0.96f);
        var tipTextRt = MakeRect("Text", tipRt);
        tipTextRt.anchorMin = Vector2.zero;
        tipTextRt.anchorMax = Vector2.one;
        tipTextRt.offsetMin = new Vector2(8f, 6f);
        tipTextRt.offsetMax = new Vector2(-8f, -6f);
        bestTooltipText = tipTextRt.gameObject.AddComponent<Text>();
        bestTooltipText.font = uiFont;
        bestTooltipText.fontSize = 12;
        bestTooltipText.color = Color.white;
        bestTooltipText.alignment = TextAnchor.UpperLeft;
        bestTooltipText.horizontalOverflow = HorizontalWrapMode.Overflow;
        bestTooltipText.verticalOverflow = VerticalWrapMode.Overflow;
        bestTooltipGo = tipRt.gameObject;
        bestTooltipGo.SetActive(false);
        y -= 22f;
        // 登録最適化の進捗バー（探索=低め固定/STOMP候補=prog%/完了=100%）。計画中(OptActive)のみ表示。ハンドルは隠す。
        progressBar = MakeSlider(panel, "optProg", new Vector2(8f, y), W - 16f, 8f);
        progressBar.minValue = 0f;
        progressBar.maxValue = 1f;
        progressBar.interactable = false;
        progressBar.SetValueWithoutNotify(0f);
        if (progressBar.handleRect != null) { progressBar.handleRect.gameObject.SetActive(false); }
        progressBar.gameObject.SetActive(false);
        y -= 14f;
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
        // 現在の経路を FANUC 汎用再生用 CSV(関節角)へ出力（FANUC側 Karel/TP が読む・FANUC_CSV_PLAY_SPEC.md 方式A）。
        // 命名 P<品番>_<パス番号>.CSV（品番=R[89] / パス番号=R[88]・0=復帰）。出力先は Ros2Info.json csvOutputDir。
        MakeLabel(panel, "lblCsvProd", "品種番号", 13, new Vector2(8f, y), 58f, 22f);
        csvProductInput = MakeInput(panel, "csvProd", new Vector2(68f, y), 42f, 22f);
        csvProductInput.contentType = InputField.ContentType.IntegerNumber;
        csvProductInput.text = "1";
        MakeLabel(panel, "lblCsvPath", "パス", 13, new Vector2(114f, y), 28f, 22f);
        csvPathInput = MakeInput(panel, "csvPath", new Vector2(144f, y), 42f, 22f);
        csvPathInput.contentType = InputField.ContentType.IntegerNumber;
        csvPathInput.text = "0";
        // モードから自動で入る（復帰=0 / 登録=テーブル番号1オリジン）が、手動で上書きも可。
        MakeLabel(panel, "hintCsvPath", "0=復帰/登録=表番号", 12, new Vector2(190f, y), 164f, 22f);
        y -= 28f;
        csvExportBtn = MakeButton(panel, "CsvExport", "FANUC CSV 出力", new Vector2(8f, y), W - 16f, 30f, OnExportCsv);
        y -= 40f;

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
        // 登録モード：J1~J6 は常にゴール（選択ステップの poseDeg）を表示（read-only・変えない）。
        //   ゴール設定ボタンは「ロボット3D表示」だけを ゴール/開始 でプレビュー切替する（スライダー/現在:は不変）。
        if (registerModeToggle != null && registerModeToggle.isOn)
        {
            if (planner != null) { planner.CancelPlan(); }   // ゴースト消去
            EnsureKin();
            goalSetMode = !goalSetMode;   // ボタン状態（ロボ3Dプレビューの ゴール/開始）
            var steps = SelectedSteps();
            if (steps != null && steps.Count > 0 && selectedStep >= 0 && selectedStep < steps.Count)
            {
                // ★スライダー(J1~J6=ゴール)は変えず、ロボット3D だけをプレビュー。
                if (goalSetMode)
                {
                    PoseRobotOnly(steps[selectedStep] != null ? steps[selectedStep].poseDeg : null);   // ゴール
                }
                else
                {
                    int prev = (selectedStep - 1 + steps.Count) % steps.Count;
                    PoseRobotOnly(steps[prev] != null ? steps[prev].poseDeg : null);                   // 開始
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
        if (suppressRowCallbacks) { return; }         // モード切替でのレンジ変更に伴うクランプ誤発火を無視
        if (jogMode) { OnCartValue(i, v); return; }   // JOG: X/Y/Z/RX/RY/RZ → 数値IK
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
        if (suppressRowCallbacks) { return; }
        if (jogMode)   // JOG: Cartesian 値の直接入力（範囲は位置±CartPosRange / 回転±CartRotRange）
        {
            if (double.TryParse(s, out double cv))
            {
                float lim = (i < 3) ? CartPosRange : CartRotRange;
                OnCartValue(i, Mathf.Clamp((float)cv, -lim, lim));
            }
            else if (sliderInputs[i] != null)
            {
                sliderInputs[i].SetTextWithoutNotify(cartVals[Mathf.Min(i, 5)].ToString(i < 3 ? "F0" : "F1"));
            }
            return;
        }
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

    // ===== Cartesian JOG（X/Y/Z/RX/RY/RZ を数値IKでゴール関節角に変換） =====

    /// <summary>JOG 切替。ON=各行スライダーが X/Y/Z/RX/RY/RZ（base フレーム・数値IK）、OFF=関節。</summary>
    private void OnJogToggle(bool on)
    {
        jogMode = on;
        if (on)
        {
            if (!goalSetMode) { ToggleGoalSet(); }   // ゴール姿勢表示に入る
            ReadCartFromCurrent();                   // 現在TCPを Cartesian 初期値に
        }
        // レンジ(min/max)変更はスライダー値をクランプして onValueChanged を発火させるため、
        // この一括更新中はコールバックを抑制する（誤発火で goalDeg が壊れるのを防ぐ）。
        suppressRowCallbacks = true;
        try
        {
            for (int i = 0; i < rowLabels.Length; i++)
            {
                bool posRow = i < 3;
                if (rowLabels[i] != null)
                {
                    rowLabels[i].text = on ? CartLabels[Mathf.Min(i, CartLabels.Length - 1)]
                                           : ((jointNames != null && i < jointNames.Length) ? jointNames[i] : $"J{i + 1}");
                }
                if (i < sliders.Length && sliders[i] != null)
                {
                    var s = sliders[i];
                    if (on)
                    {
                        s.minValue = posRow ? -CartPosRange : -CartRotRange;
                        s.maxValue = posRow ? CartPosRange : CartRotRange;
                        s.SetValueWithoutNotify((float)cartVals[Mathf.Min(i, 5)]);
                    }
                    else
                    {
                        s.minValue = jointMin; s.maxValue = jointMax;
                        s.SetValueWithoutNotify(i < goalDeg.Length ? (float)goalDeg[i] : 0f);
                    }
                }
                if (i < sliderInputs.Length && sliderInputs[i] != null)
                {
                    sliderInputs[i].SetTextWithoutNotify(on
                        ? cartVals[Mathf.Min(i, 5)].ToString(posRow ? "F0" : "F1")
                        : (i < goalDeg.Length ? goalDeg[i].ToString("F1") : "0.0"));
                }
            }
        }
        finally { suppressRowCallbacks = false; }
        if (!on) { UpdateGoalText(); }   // 関節へ戻したら goalDeg 表示を復元
    }

    /// <summary>現在の goalDeg 姿勢の TCP を base フレーム Cartesian(cartVals) に読み込む。</summary>
    private void ReadCartFromCurrent()
    {
        if (targetKin == null) { return; }
        targetKin.SetManualJointsDeg(goalDeg);
        if (!targetKin.GetTcpPoseWorld(out Vector3 p, out Quaternion r)) { return; }
        GetBaseAxes(out Vector3 ax, out Vector3 ay, out Vector3 az, out Vector3 origin);
        Vector3 rel = p - origin;
        // ★姿勢は base 相対の quaternion をそのまま保持（絶対3角度に分解しない＝ジンバルロック回避）。
        //   回転スライダーは「ここから base 軸まわりに何度回したか」の累積デルタなので原点=0。
        cartRot = Quaternion.Inverse(Quaternion.LookRotation(az, ay)) * r;
        cartVals[0] = Vector3.Dot(rel, ax) * 1000.0;
        cartVals[1] = Vector3.Dot(rel, ay) * 1000.0;
        cartVals[2] = Vector3.Dot(rel, az) * 1000.0;
        cartVals[3] = 0.0; cartVals[4] = 0.0; cartVals[5] = 0.0;
    }

    /// <summary>現在の TCP から 位置 cartVals[0..2]＋位置スライダー表示 と 姿勢 cartRot を同期する（回転値 RX/RY/RZ は保持）。
    /// J6 直接ジョグ後、次の 位置/RX/RY ジョグが正しい基準から始まるように使う。</summary>
    private void SyncCartFromCurrent()
    {
        if (targetKin == null) { return; }
        if (!targetKin.GetTcpPoseWorld(out Vector3 p, out Quaternion r)) { return; }
        GetBaseAxes(out Vector3 ax, out Vector3 ay, out Vector3 az, out Vector3 origin);
        Vector3 rel = p - origin;
        cartVals[0] = Vector3.Dot(rel, ax) * 1000.0;
        cartVals[1] = Vector3.Dot(rel, ay) * 1000.0;
        cartVals[2] = Vector3.Dot(rel, az) * 1000.0;
        cartRot = Quaternion.Inverse(Quaternion.LookRotation(az, ay)) * r;
        for (int k = 0; k < 3 && k < sliders.Length; k++)
        {
            if (sliders[k] != null) { sliders[k].SetValueWithoutNotify((float)cartVals[k]); }
            if (k < sliderInputs.Length && sliderInputs[k] != null) { sliderInputs[k].SetTextWithoutNotify(cartVals[k].ToString("F0")); }
        }
    }

    /// <summary>JOG時: 行 i の値変更。位置=絶対IK / RX,RY=tool軸デルタ+IK / RZ=J6直接（逆解を通さずアームを動かさない）。</summary>
    private void OnCartValue(int i, double v)
    {
        int idx = Mathf.Min(i, 5);
        if (idx == 5)
        {
            // ★RZ = J6 ロール：ツール approach 軸まわりの回転＝最終軸(J6)そのもの。数値IK(逆解)を通すと
            //   DLS が回転を全関節へ分配してアームが微妙にドリフトするので、J6 を直接回す（J1〜J5 は不動）。
            double d6 = v - cartVals[idx];
            cartVals[idx] = v;
            int last = (goalDeg != null && goalDeg.Length > 0) ? goalDeg.Length - 1 : -1;
            if (last >= 0 && targetKin != null)
            {
                goalDeg[last] += d6;
                targetKin.SetManualJointsDeg(goalDeg);
                SyncCartFromCurrent();   // J6 で TCP 位置/姿勢が変わり得るので cartVals(位置)/cartRot を同期(次の位置/RX/RY 用)
                UpdateGoalText();
                if (statusText != null) { statusText.text = "JOG: J6 ロール"; }
            }
            if (i < sliders.Length && sliders[i] != null) { sliders[i].SetValueWithoutNotify((float)v); }
            if (i < sliderInputs.Length && sliderInputs[i] != null) { sliderInputs[i].SetTextWithoutNotify(v.ToString("F1")); }
            return;
        }
        if (idx >= 3)
        {
            // RX/RY：tool(ヘッド)自身の軸まわりのデルタを cartRot に積む（post-multiply）→数値IK。
            //   RX→ツールX / RY→ツールZ。絶対3角度でないのでジンバルロック無し。
            double delta = v - cartVals[idx];
            Vector3 axis = (idx == 3) ? Vector3.right : Vector3.forward;
            cartRot *= Quaternion.AngleAxis((float)delta, axis);
        }
        cartVals[idx] = v;
        if (i < sliders.Length && sliders[i] != null) { sliders[i].SetValueWithoutNotify((float)v); }
        if (i < sliderInputs.Length && sliderInputs[i] != null) { sliderInputs[i].SetTextWithoutNotify(v.ToString(i < 3 ? "F0" : "F1")); }
        ApplyCartTarget();
    }

    /// <summary>cartVals(base フレーム) → world 姿勢 → 数値IK → goalDeg 更新＋プレビュー。</summary>
    private void ApplyCartTarget()
    {
        if (targetKin == null) { return; }
        GetBaseAxes(out Vector3 ax, out Vector3 ay, out Vector3 az, out Vector3 origin);
        Vector3 pos = origin
            + ax * (float)(cartVals[0] / 1000.0)
            + ay * (float)(cartVals[1] / 1000.0)
            + az * (float)(cartVals[2] / 1000.0);
        // ★姿勢は保持している cartRot（base相対）をそのまま使う。回転JOGは cartRot に base軸まわりのデルタを
        //   積んでいく方式（OnCartValue）なので、絶対3角度のジンバルロック(RX/RY/RZが同軸化)が原理的に起きない。
        Quaternion rot = Quaternion.LookRotation(az, ay) * cartRot;
        bool ok = targetKin.TrySolveIkWorld(pos, rot, goalDeg, out double[] sol) && sol != null;
        if (ok)
        {
            for (int k = 0; k < goalDeg.Length && k < sol.Length; k++) { goalDeg[k] = sol[k]; }
            targetKin.SetManualJointsDeg(goalDeg);
            UpdateGoalText();
            if (statusText != null) { statusText.text = "JOG: IK OK"; }
        }
        else if (statusText != null)
        {
            statusText.text = "JOG: 到達不能（IK未収束）";
        }
    }

    private static double Norm180(double d) { d %= 360.0; if (d > 180.0) { d -= 360.0; } if (d < -180.0) { d += 360.0; } return d; }

    /// <summary>ロボット base フレーム（FANUC: X前,Y左,Z上）の world 軸方向＋原点(arm1)。DCS と同じ P=baseRot·calInv。</summary>
    private void GetBaseAxes(out Vector3 ax, out Vector3 ay, out Vector3 az, out Vector3 origin)
    {
        ax = Vector3.right; ay = Vector3.up; az = Vector3.forward; origin = Vector3.zero;
        if (targetKin == null) { return; }
        origin = targetKin.GetRobotOriginWorldPosition();
        var baseT = targetKin.GetBaseTransform();
        if (baseT == null) { return; }
        Quaternion pf = baseT.rotation * Quaternion.Inverse(Quaternion.Euler(0f, -90f, 0f));
        ax = pf * Vector3.forward;    // FANUC X(前)
        ay = pf * (-Vector3.right);   // FANUC Y(左)
        az = pf * Vector3.up;         // FANUC Z(上)
    }

    // ===== TCP(ヘッドオフセット点=吸盤) 可視化マーカー =====

    /// <summary>JOG中は TCP の world 姿勢に球＋XYZ軸マーカーを追従表示。JOG OFF/対象無しなら消す。</summary>
    private void UpdateTcpMarker()
    {
        if (jogMode && targetKin != null && targetKin.GetTcpPoseWorld(out Vector3 pos, out Quaternion rot))
        {
            if (tcpMarker == null) { tcpMarker = BuildTcpMarker(); }
            tcpMarker.transform.SetPositionAndRotation(pos, rot);
        }
        else if (tcpMarker != null)
        {
            Destroy(tcpMarker); tcpMarker = null;
        }
    }

    /// <summary>TCPマーカー生成: 中心の小球＋ローカルXYZ軸（X赤/Y緑/Z青）。当たり判定なし・常時最前。</summary>
    private GameObject BuildTcpMarker()
    {
        var root = new GameObject("TcpMarker");
        // 中心球（黄）。当たり判定は不要なので Collider は破棄。
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "tcp";
        var col = ball.GetComponent<Collider>();
        if (col != null) { Destroy(col); }
        ball.transform.SetParent(root.transform, false);
        ball.transform.localScale = Vector3.one * 0.03f;   // 直径3cm
        var br = ball.GetComponent<Renderer>();
        if (br != null) { br.sharedMaterial = MakeMarkerMat(new Color(1f, 0.9f, 0.1f, 1f)); }
        // ローカル3軸（tip の向き）。X=赤/Y=緑/Z=青。長さ12cm。
        AddAxis(root.transform, "X", Vector3.right, new Color(1f, 0.25f, 0.2f));
        AddAxis(root.transform, "Y", Vector3.up, new Color(0.3f, 1f, 0.35f));
        AddAxis(root.transform, "Z", Vector3.forward, new Color(0.3f, 0.55f, 1f));
        return root;
    }

    /// <summary>マーカーの1軸（原点→dir×0.12m の線）。</summary>
    private void AddAxis(Transform parent, string name, Vector3 dir, Color col)
    {
        var go = new GameObject("axis" + name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.widthMultiplier = 0.006f;
        lr.numCornerVertices = 0;
        lr.numCapVertices = 0;
        lr.positionCount = 2;
        lr.SetPosition(0, Vector3.zero);
        lr.SetPosition(1, dir * 0.12f);
        var m = MakeMarkerMat(col);
        if (m != null) { lr.sharedMaterial = m; lr.startColor = col; lr.endColor = col; }
    }

    /// <summary>マーカー用の単色 URP Unlit マテリアル（深度無視で常に見えるように renderQueue を上げる）。</summary>
    private static Material MakeMarkerMat(Color col)
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) { sh = Shader.Find("Sprites/Default"); }
        if (sh == null) { return null; }
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) { m.SetColor("_BaseColor", col); }
        if (m.HasProperty("_Color")) { m.SetColor("_Color", col); }
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;   // 手前に描く
        return m;
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
        if (planner == null) { return; }
        // ★機種/コントローラ取り違え防止：計画は必ず「選択機体で ROS が稼働している」状態で行う。
        //   稼働中 robot_model・dcs_host が選択機体と違う（or 不明）なら、先に切替してから計画する（自動）。
        string want = SelRobotModel();
        string wantDcs = SelDcsHost();
        if (launcher != null && !string.IsNullOrEmpty(want))
        {
            bool modelOk = !string.IsNullOrEmpty(launcher.CurrentRobotModel) && launcher.CurrentRobotModel == want;
            bool dcsOk = !string.IsNullOrEmpty(launcher.CurrentDcsHost) && NormDcsHost(launcher.CurrentDcsHost) == wantDcs;
            if (!(modelOk && dcsOk))   // どちらか未確認/不一致 → 先に切替
            {
                if (launcher.Busy)
                {
                    if (statusText != null) { statusText.text = "ROS処理中… 完了後にもう一度計画してください"; }
                    return;
                }
                StartCoroutine(PlanWithModelRoutine(want, SelRobotIp()));   // 切替→稼働確認→計画
                return;
            }
        }
        // ROS 未接続では計画要求が届かない（ボタン無効化済みだが二重の保険）。
        if (!planner.IsLinkUp) { return; }
        DoPlanOrRegister();
    }

    /// <summary>選択機種へ ROS を切替（違う時のみ再起動）→ 稼働機種が一致したら計画。機種取り違えを構造的に防ぐ。</summary>
    private System.Collections.IEnumerator PlanWithModelRoutine(string model, string ip)
    {
        if (statusText != null) { statusText.text = $"計画準備：{model} へ切替中…"; }
        if (planner != null &&
            (planner.State == ComRos2PathPlanner.PlanState.Preview ||
             planner.State == ComRos2PathPlanner.PlanState.Playing))
        {
            planner.CancelPlan();
        }
        launcher.StartRos2(model, ip);   // 同一modelなら再起動なし
        yield return null;

        // スクリプト完了＋running_full 待ち（別modelは再起動で ~15-45s）。
        float deadline = Time.unscaledTime + 90f;
        while (Time.unscaledTime < deadline)
        {
            if (!launcher.Busy && launcher.State == ComRos2Launcher.LaunchState.RunningFull) { break; }
            yield return new WaitForSecondsRealtime(0.5f);
        }
        if (launcher.State != ComRos2Launcher.LaunchState.RunningFull)
        {
            if (statusText != null) { statusText.text = "計画中止：ROS が running_full になりません（タイムアウト）"; }
            yield break;
        }

        // TCP 再接続待ち → planning scene 再送。
        float linkDeadline = Time.unscaledTime + 20f;
        while (Time.unscaledTime < linkDeadline && (planner == null || !planner.IsLinkUp))
        {
            yield return new WaitForSecondsRealtime(0.3f);
        }
        if (obstacles != null) { obstacles.SendObstacles(); }

        // 稼働 robot_model / dcs_host が選択機体になったか確認（poll 反映待ち）。未対応機種は ROS 側で crx30ia フォールバック。
        string wantDcs = NormDcsHost(ip);
        float modelDeadline = Time.unscaledTime + 6f;
        while (Time.unscaledTime < modelDeadline &&
               (launcher.CurrentRobotModel != model || NormDcsHost(launcher.CurrentDcsHost) != wantDcs))
        {
            yield return new WaitForSecondsRealtime(0.3f);
        }
        if (launcher.CurrentRobotModel != model || NormDcsHost(launcher.CurrentDcsHost) != wantDcs)
        {
            if (statusText != null)
            {
                statusText.text = $"計画中止：稼働=({launcher.CurrentRobotModel},{launcher.CurrentDcsHost}) が選択=({model},{wantDcs}) になりません";
            }
            Debug.LogWarning($"[ComRos2PlanPanel] 機体切替失敗: 稼働=({launcher.CurrentRobotModel},{launcher.CurrentDcsHost}) 選択=({model},{wantDcs})");
            yield break;
        }
        if (planner == null || !planner.IsLinkUp)
        {
            if (statusText != null) { statusText.text = "計画中止：ROS-TCP 未接続"; }
            yield break;
        }
        DoPlanOrRegister();   // 機種一致を確認できたので計画実行（登録モードなら選択ステップを登録計画）
    }

    /// <summary>「計画」ボタンの実行：復帰モードは通常計画(DoPlan)、登録モードは選択ステップの登録計画。</summary>
    private void DoPlanOrRegister()
    {
        bool reg = registerModeToggle != null && registerModeToggle.isOn;
        if (reg) { RegisterSelectedStep(); }
        else { DoPlan(); }
    }

    /// <summary>登録モード：選択中のステップを計画する（→ゴーストプレビュー→OKで登録キャッシュ保存）。</summary>
    private void RegisterSelectedStep()
    {
        int robotIdx = registry != null ? registry.SelectedIndex : -1;
        var steps = SelectedSteps();
        if (robotIdx < 0 || steps == null || selectedStep < 0 || selectedStep >= steps.Count)
        {
            if (statusText != null) { statusText.text = "登録するステップを選択してください"; }
            return;
        }
        OnRegisterStep(robotIdx, selectedStep);   // 計画→ゴースト→OK で保存
    }

    /// <summary>計画本体（ゴール確定→障害物/ヘッド込みで計画要求）。機種一致は呼び出し側で保証する。</summary>
    private void DoPlan()
    {
        if (planner == null || !planner.IsLinkUp) { return; }
        // ★計画のたびに DCS安全ゾーンを必ず受信し直す（機種切替後や再配信漏れでも表示を最新化・受信忘れ防止）。
        ReceiveDcsZones();
        // ★start(計画開始姿勢)＝実現在。ゴール設定中はモデルが goalDeg 姿勢のため、
        //   ToggleGoalSet で抜けても腕は次フレームまで戻らず、ReadCurrentDeg(多ロボ=Kinematics直読み)が goalDeg を返す。
        //   → ゴール設定に入る前に凍結した実現在(curDisplayDeg)を start に使う（設定を抜ける前に確定）。
        double[] start = (goalSetMode && curDisplayDeg != null)
            ? (double[])curDisplayDeg.Clone()
            : planner.ReadCurrentDeg();
        // 設定モードなら抜けて現在姿勢の表示へ。
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
        var goal = (double[])goalDeg.Clone();
        // ★start==goal（全軸が閾値以下）なら動く必要がない＝計画を投げない。
        //   機種切替直後は goal=現在姿勢のまま（InitGoalFromCurrent）＝空振りで start==goal の1点計画が飛ぶのを防ぐ。
        if (JointsCloseDeg(start, goal, GoalSameEpsDeg))
        {
            if (statusText != null) { statusText.text = "計画スキップ: 目標が現在姿勢と同じ（ゴールを設定してください）"; }
            Debug.Log("[ComRos2PlanPanel] start==goal のため計画スキップ（目標未設定/現在姿勢と同一）");
            return;
        }
        planner.RequestPlanWithScene(start, goal);                 // 障害物/ヘッドも送って計画
    }

    /// <summary>全関節が eps(度)以下で一致するか（start==goal 判定）。長さ不一致/null は不一致扱い。</summary>
    private const double GoalSameEpsDeg = 0.5;
    private static bool JointsCloseDeg(double[] a, double[] b, double epsDeg)
    {
        if (a == null || b == null || a.Length == 0 || a.Length != b.Length) { return false; }
        for (int i = 0; i < a.Length; i++)
        {
            if (System.Math.Abs(a[i] - b[i]) > epsDeg) { return false; }
        }
        return true;
    }

    /// <summary>現在の経路を FANUC TPプログラム(.LS)へ出力（C:\KMX-Path）。.TP が要る場合はこの .LS を MAKETP で翻訳。</summary>
    private void OnExportLs()
    {
        const string progName = "KMXPATH";
        if (!planner.TryBuildCurrentLs(progName, out string ls, out string err))
        {
            if (statusText != null) { statusText.text = "LS出力: " + err; }
            Debug.LogWarning("[ComRos2PlanPanel] LS出力失敗: " + err);
            return;
        }
        try
        {
            string dir = @"C:\KMX-Path";   // .LS 出力先（ROBOGUIDE/コントローラで取り込みやすい固定フォルダ）
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, progName + ".LS");
            System.IO.File.WriteAllText(path, ls, new System.Text.UTF8Encoding(false));
            if (statusText != null) { statusText.text = "LS出力: " + path; }
            Debug.Log("[ComRos2PlanPanel] FANUC .LS 出力: " + path);
        }
        catch (System.Exception e)
        {
            if (statusText != null) { statusText.text = "LS出力エラー: " + e.Message; }
            Debug.LogWarning("[ComRos2PlanPanel] LS書込失敗: " + e.Message);
        }
    }

    /// <summary>
    /// 現在の経路を FANUC 汎用再生用 CSV(関節角)へ出力（FANUC_CSV_PLAY_UNITY_REQUEST.md）。
    /// 命名 P&lt;品番&gt;_&lt;パス番号&gt;.CSV（品番=R[89] / パス番号=R[88]・0=復帰）。出力先は Ros2Info.json csvOutputDir（環境依存＝設定化）。
    /// </summary>
    private void OnExportCsv()
    {
        if (!planner.TryBuildCurrentCsv(out string csv, out string err))
        {
            if (statusText != null) { statusText.text = "CSV出力: " + err; }
            Debug.LogWarning("[ComRos2PlanPanel] CSV出力失敗: " + err);
            return;
        }
        // 命名: P<品番>_<パス番号>.CSV（R[89]/R[88] と一致・整数・省略しない）。
        int prod = 1;
        if (csvProductInput != null) { int.TryParse(csvProductInput.text, out prod); }
        if (prod < 0) { prod = 0; }
        // パス番号は既定でモード連動（復帰=0 / 登録=表番号1オリジン）だが、欄で手動上書きできる。
        int pathNo = CurrentPathNo();
        if (csvPathInput != null && int.TryParse(csvPathInput.text, out int pv) && pv >= 0) { pathNo = pv; }
        string fileName = $"P{prod}_{pathNo}.CSV";
        // 出力先: Ros2Info.json csvOutputDir（ROBOGUIDE=<ワークセル>\Robot_1\UD1\KMX、実機=USB/FTP先）。未設定なら C:\KMX-Path。
        string dir = @"C:\KMX-Path";
        try
        {
            var cfg = GlobalScript.LoadJson<ComRos2.Ros2Setting>("Ros2Info") as ComRos2.Ros2Setting;
            if (cfg != null && !string.IsNullOrEmpty(cfg.csvOutputDir)) { dir = cfg.csvOutputDir; }
        }
        catch { /* 既定フォルダを使う */ }
        try
        {
            System.IO.Directory.CreateDirectory(dir);   // KMX サブフォルダが無ければ作成
            string path = System.IO.Path.Combine(dir, fileName);
            System.IO.File.WriteAllText(path, csv, new System.Text.UTF8Encoding(false));   // BOM無し=ASCII互換
            Debug.Log("[ComRos2PlanPanel] FANUC CSV 出力: " + path);
            // 実機（dcs_host が実IP＝ROBOGUIDE 127.0.0.1 以外）なら、同じ機体IPの UD1:\KMX へ FTP も行う。
            string ip = SelRobotIp();
            bool isReal = NormDcsHost(ip) != "auto";
            if (isReal)
            {
                if (statusText != null) { statusText.text = $"CSV出力: {path}（{ip} へFTP中…）"; }
                string host = ip.Trim(), fn = fileName, body = csv;
                var t = new System.Threading.Thread(() => { try { FtpUploadCsv(host, fn, body); } catch { } }) { IsBackground = true };
                t.Start();   // FTP は同期ブロックするので別スレッド（未接続実機で UI を固めない）
            }
            else if (statusText != null) { statusText.text = "CSV出力: " + path; }
        }
        catch (System.Exception e)
        {
            if (statusText != null) { statusText.text = "CSV出力エラー: " + e.Message; }
            Debug.LogWarning("[ComRos2PlanPanel] CSV書込失敗: " + e.Message);
        }
    }

    /// <summary>
    /// FANUC コントローラ(実機)の UD1:\KMX へ CSV を FTP アップロード（匿名・KMX 自動作成）。
    /// ★実機未検証：ワーカースレッドから呼ぶ（同期FTPで UI を固めない）。認証/パスは実機に合わせ要調整。
    /// </summary>
    private static void FtpUploadCsv(string host, string fileName, string content)
    {
        string dirUri = $"ftp://{host}/UD1/KMX";
        string fileUri = $"ftp://{host}/UD1/KMX/{fileName}";
        var cred = new System.Net.NetworkCredential("anonymous", "anonymous@");   // ひとまず匿名
        // サブフォルダ KMX を作成（既存/未対応はエラーになるが無視）。
        try
        {
            var mk = (System.Net.FtpWebRequest)System.Net.WebRequest.Create(dirUri);
            mk.Method = System.Net.WebRequestMethods.Ftp.MakeDirectory;
            mk.Credentials = cred;
            mk.KeepAlive = false;
            mk.Timeout = 8000;
            using (mk.GetResponse()) { }
        }
        catch { /* 既存 or 未対応は無視 */ }
        // ファイル upload。
        try
        {
            var req = (System.Net.FtpWebRequest)System.Net.WebRequest.Create(fileUri);
            req.Method = System.Net.WebRequestMethods.Ftp.UploadFile;
            req.Credentials = cred;
            req.UseBinary = true;
            req.KeepAlive = false;
            req.Timeout = 15000;
            byte[] data = new System.Text.UTF8Encoding(false).GetBytes(content);
            req.ContentLength = data.Length;
            using (var s = req.GetRequestStream()) { s.Write(data, 0, data.Length); }
            using (var resp = (System.Net.FtpWebResponse)req.GetResponse())
            {
                Debug.Log($"[CSV FTP] {fileUri} 転送OK: {resp.StatusDescription?.Trim()}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[CSV FTP] 失敗 {fileUri}: {e.Message}");
        }
    }

    /// <summary>
    /// 計画のたびに DCS安全ゾーンを必ず受信し直す（受信忘れ防止）。
    /// ROS の latched topic /kmx/safety_zones からキャッシュ値を取り直して全ユニットへ再適用・再描画する。
    /// 機種切替直後や再配信漏れでも、計画時点で表示を最新化する。ParameterLoader が無ければ無視（安全）。
    /// </summary>
    private void ReceiveDcsZones()
    {
        var loaders = FindObjectsByType<Parameters.ParameterLoader>(FindObjectsSortMode.None);
        var loader = loaders.Length > 0 ? loaders[0] : null;
        // clearIfEmpty=false: 受信が取れない瞬間でも既存の DCS表示を消さない（計画のたびに消えるのを防ぐ）。
        if (loader != null) { loader.ReloadSafetyZones(false); }
    }

    /// <summary>
    /// 対象ロボットにカメラを合わせる：正面(FANUC X方向)へ移動し、回転中心をロボットの TCP にする。
    /// 経路計画パネル表示時・機種切替時に呼ぶ。
    /// </summary>
    private void FocusCameraOnRobot()
    {
        EnsureKin();
        if (targetKin == null) { return; }
        // 回転中心 = TCP 世界位置（取れなければロボ原点）。
        if (!targetKin.GetTcpPoseWorld(out Vector3 pivot, out _))
        {
            pivot = targetKin.GetRobotOriginWorldPosition();
        }
        // 視線方向 = 正面(FANUC X) を基準に、やや上(FANUC Z)＝見下ろし・少し横(FANUC Y)へずらす。距離は張り出し量から算出。
        GetBaseAxes(out Vector3 ax, out Vector3 ay, out Vector3 az, out Vector3 origin);
        Vector3 viewDir = ax + az * 0.35f + ay * 0.35f;   // 前＋少し上＋少し横（見下ろし＆横ずらし）
        float reach = Vector3.Distance(origin, pivot);
        float dist = Mathf.Max(3.5f, reach * 2.2f + 1.0f);
        var cams = FindObjectsByType<CameraController>(FindObjectsSortMode.None);
        if (cams != null && cams.Length > 0) { cams[0].MoveToFront(pivot, viewDir, dist); }
    }

    /// <summary>DCS安全ゾーンを SafetyZoneInfo.json から再読込して再描画する（全体F5リロード不要）。</summary>
    private void OnReloadDcs()
    {
        var loaders = FindObjectsByType<Parameters.ParameterLoader>(FindObjectsSortMode.None);
        var loader = loaders.Length > 0 ? loaders[0] : null;
        if (loader == null)
        {
            if (statusText != null) { statusText.text = "DCS再読込: ParameterLoader が見つかりません"; }
            return;
        }
        loader.ReloadSafetyZones();
        if (statusText != null) { statusText.text = "DCS安全ゾーンを再読込しました"; }
        Debug.Log("[ComRos2PlanPanel] DCS安全ゾーン 再読込");
    }

    private void OnStartRos2()
    {
        // 起動は選択機体の robot_model/robotIp で（同一modelなら kmx_start.sh 側で再起動されない）。
        if (launcher != null) { launcher.StartRos2(SelRobotModel(), SelRobotIp()); }
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
        if (launcher != null) { launcher.RestartRos2(SelRobotModel(), SelRobotIp()); }
    }

    /// <summary>選択機体の robot_model（=ModelKey）。ロボット切替の robot_model に使う。</summary>
    private string SelRobotModel()
    {
        var t = registry != null && registry.Selected != null ? registry.Selected.Target : null;
        return (t != null && !string.IsNullOrEmpty(t.ModelKey)) ? t.ModelKey : "";
    }

    /// <summary>選択機体のコントローラIP（RobotInfo.json robotIp）。dcs_host($6)/CSV FTP 先に使う。</summary>
    private string SelRobotIp()
    {
        var t = registry != null && registry.Selected != null ? registry.Selected.Target : null;
        return t != null ? t.ControllerIp : "";
    }

    /// <summary>選択機体の dcs_host（正規化済）。</summary>
    private string SelDcsHost() => NormDcsHost(SelRobotIp());

    /// <summary>dcs_host の正規化。空/127.0.0.1/localhost は "auto"（ROS側と同じ規約）。稼働値との比較用。</summary>
    private static string NormDcsHost(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) { return "auto"; }
        ip = ip.Trim();
        return (ip == "127.0.0.1" || ip == "localhost") ? "auto" : ip;
    }

    /// <summary>「この機種でROS起動/切替」：選択機体の robot_model で bringup を(再)起動し、
    /// running_full 待ち→再接続→planning scene 再送 まで一括で行う（ROBOT_SWITCH_UNITY_SPEC.md §3）。</summary>
    private void OnSwitchRobot()
    {
        if (launcher == null || launcher.Busy) { return; }
        string model = SelRobotModel();
        if (string.IsNullOrEmpty(model))
        {
            if (statusText != null) { statusText.text = "機種切替: 対象ロボット未選択"; }
            return;
        }
        // 計画中/プレビューは片付けてから切替（再起動で endpoint が落ちるため）。
        if (planner != null &&
            (planner.State == ComRos2PathPlanner.PlanState.Preview ||
             planner.State == ComRos2PathPlanner.PlanState.Planning ||
             planner.State == ComRos2PathPlanner.PlanState.Playing))
        {
            planner.CancelPlan();
        }
        StartCoroutine(SwitchRobotRoutine(model, SelRobotIp()));
    }

    private System.Collections.IEnumerator SwitchRobotRoutine(string model, string ip)
    {
        if (statusText != null) { statusText.text = $"ROS機種切替中… ({model})"; }
        launcher.StartRos2(model, ip);   // 同一modelなら再起動なし・別modelなら stop→再起動
        yield return null;

        // スクリプト完了＋running_full を待つ（別modelは再起動で ~15-45s）。
        float deadline = Time.unscaledTime + 90f;
        while (Time.unscaledTime < deadline)
        {
            if (!launcher.Busy && launcher.State == ComRos2Launcher.LaunchState.RunningFull) { break; }
            yield return new WaitForSecondsRealtime(0.5f);
        }
        if (launcher.State != ComRos2Launcher.LaunchState.RunningFull)
        {
            if (statusText != null) { statusText.text = "ROS機種切替: running_full にならず（タイムアウト）"; }
            yield break;
        }

        // ROS-TCP 再接続待ち（ROS-TCP-Connector が自動再接続）。
        float linkDeadline = Time.unscaledTime + 20f;
        while (Time.unscaledTime < linkDeadline && (planner == null || !planner.IsLinkUp))
        {
            yield return new WaitForSecondsRealtime(0.3f);
        }

        // 再起動で planning scene は空になるので障害物/ヘッド/床を再送。
        if (obstacles != null) { obstacles.SendObstacles(); }
        // 機種/コントローラ切替後は DCS が別機体のものに変わるので読み直す（base 確定後・ロード後のこのタイミング）。
        ReceiveDcsZones();

        string cur = launcher.CurrentRobotModel;
        if (statusText != null)
        {
            statusText.text = (planner != null && planner.IsLinkUp)
                ? $"ROS機種切替 完了: {(!string.IsNullOrEmpty(cur) ? cur : model)}"
                : $"ROS機種切替: 起動OK・TCP未接続（{model}）";
        }
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

    /// <summary>ステータスの「最良」クリック：探索中バッファの経路時間ベスト10（昇順）をツールチップでトグル表示。</summary>
    private void OnBestTooltipToggle()
    {
        if (bestTooltipGo == null || planner == null) { return; }
        if (bestTooltipGo.activeSelf)
        {
            bestTooltipGo.SetActive(false);
            return;
        }
        var best = planner.OptBestTimes(10);
        if (best == null || best.Count == 0)
        {
            return;   // 未発見（まだ経路なし）は出さない
        }
        // 改行は各項目の「前」に付けて末尾に空行を作らない（末尾 \n があると下マージンが1行分増える）。
        var sb = new System.Text.StringBuilder("経路時間 ベスト10（昇順）");
        for (int i = 0; i < best.Count; i++)
        {
            sb.Append($"\n{i + 1,2}.  {best[i]:F2} s");
        }
        bestTooltipText.text = sb.ToString();
        bestTooltipGo.transform.SetAsLastSibling();   // 手前に出す
        bestTooltipGo.SetActive(true);
        // 上下の余白を同じにする：text-rect は上下 6px インセットなので、box 高さ=内容(preferredHeight)+12。
        var trt = (RectTransform)bestTooltipGo.transform;
        trt.sizeDelta = new Vector2(trt.sizeDelta.x, bestTooltipText.preferredHeight + 12f);
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
        // 計画が完了(Preview)/失敗(Failed)/待機(Idle)など「計画中でない」状態になったら 最良ツールチップを閉じる。
        if (s != ComRos2PathPlanner.PlanState.Planning && bestTooltipGo != null && bestTooltipGo.activeSelf)
        {
            bestTooltipGo.SetActive(false);
        }
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
        if (planBtn != null) { planBtn.interactable = planner.IsLinkUp && s != ComRos2PathPlanner.PlanState.Planning; }
        // 計画中は「ゴール設定」を押せなくする（押すと CancelPlan で計画が止まるため）。
        if (setGoalBtn != null) { setGoalBtn.interactable = s != ComRos2PathPlanner.PlanState.Planning; }
        // 停止ボタンは「登録の探索中」のみ表示（実際の可視制御は Update でも毎フレーム更新）。
        bool searching = s == ComRos2PathPlanner.PlanState.Planning && planner != null && planner.OptSearching;
        if (!searching) { stopSearchLatched = false; }   // 探索終了でラッチ解除（次回は押せる）
        if (stopSearchBtn != null)
        {
            stopSearchBtn.gameObject.SetActive(searching);
            stopSearchBtn.interactable = searching && !stopSearchLatched;
        }
        // 進捗バーは計画中(OptActive)のみ。状態遷移でまず隠し、Update が値を反映。
        if (progressBar != null)
        {
            progressBar.gameObject.SetActive(s == ComRos2PathPlanner.PlanState.Planning && planner != null && planner.OptActive);
        }
    }

    /// <summary>起動/停止/再起動ボタンの活性のみ更新（状態表示は commText に統合）。</summary>
    private void UpdateLaunchUi()
    {
        bool busy = launcher.Busy;
        var st = launcher.State;
        bool needStart = st != ComRos2Launcher.LaunchState.RunningFull;   // 未起動＝起動を促す
        if (startBtn != null)
        {
            startBtn.interactable = !busy && needStart;
            // 起動が必要なときは起動ボタンをオレンジで強調して押下を促す。稼働中は通常色。
            if (startBtn.image != null)
            {
                startBtn.image.color = needStart
                    ? new Color(1f, 0.5f, 0.1f, 0.98f)      // 未起動→オレンジで強調
                    : new Color(0.2f, 0.4f, 0.7f, 0.95f);   // 稼働中→通常
            }
        }
        if (stopBtn != null) { stopBtn.interactable = !busy && st != ComRos2Launcher.LaunchState.Stopped; }
        if (restartBtn != null) { restartBtn.interactable = !busy; }
        if (switchRobotBtn != null)
        {
            // 選択機体が居て処理中でなければ切替可。稼働機種と選択が違えばオレンジで強調。
            bool canSwitch = !busy && registry != null && registry.Selected != null;
            switchRobotBtn.interactable = canSwitch;
            if (switchRobotBtn.image != null)
            {
                string want = SelRobotModel();
                string cur = launcher.CurrentRobotModel;
                bool modelMis = !string.IsNullOrEmpty(want) && !string.IsNullOrEmpty(cur) && cur != want;
                bool dcsMis = !string.IsNullOrEmpty(want) && !string.IsNullOrEmpty(launcher.CurrentDcsHost)
                              && NormDcsHost(launcher.CurrentDcsHost) != SelDcsHost();
                switchRobotBtn.image.color = (modelMis || dcsMis)
                    ? new Color(1f, 0.5f, 0.1f, 0.98f)      // 稼働機体≠選択→切替を促す
                    : new Color(0.2f, 0.4f, 0.7f, 0.95f);
            }
        }
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
            if (sel != null)
            {
                // ロボット名(ロボットタイプ)。タイプ＝ModelKey（robot_id の機種部・ROS2 robot_map 索引）。
                string type = (sel.Target != null && !string.IsNullOrEmpty(sel.Target.ModelKey)) ? sel.Target.ModelKey : "?";
                robotNameText.text = $"{sel.DisplayName}({type})  ({registry.SelectedIndex + 1}/{n})";
            }
            else
            {
                robotNameText.text = "ロボット: -";
            }
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
        SetJointRowsInteractable(!on);   // 登録モードでは J1~J6 を編集不可（ゴールはステップで確定）
        // 計画ボタンのラベルはモードで切替：復帰モード=復帰計画 / 登録モード=経路計画（選択ステップの登録計画）。
        if (planBtn != null)
        {
            var pt = planBtn.GetComponentInChildren<Text>();
            if (pt != null) { pt.text = on ? "経路計画" : "復帰計画"; }
        }
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
        // 登録モードは J1~J6 に「ゴール＝選択ステップの目標姿勢(poseDeg)」を表示（read-only）。
        // ゴール設定ボタンで開始姿勢(前step終了)のプレビューにトグルできる。
        goalSetMode = true;
        SetButtonColor(setGoalBtn, new Color(0.8f, 0.5f, 0.1f, 0.95f));   // ゴール表示=オレンジ
        var steps = SelectedSteps();
        if (steps != null && steps.Count > 0 && stepIdx >= 0 && stepIdx < steps.Count)
        {
            PoseRobotAt(steps[stepIdx] != null ? steps[stepIdx].poseDeg : null);   // ゴール（この step の poseDeg）
            // 「現在:」は開始姿勢（前step終了・循環）を表示する（goalSetMode=true で固定）。
            int prev = (stepIdx - 1 + steps.Count) % steps.Count;
            curDisplayDeg = PoseToDeg(steps[prev] != null ? steps[prev].poseDeg : null);
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
            MakeLabel(row, "no", (i + 1).ToString(), 12, new Vector2(8f, 0f), 18f, 24f);   // パス番号(1オリジン)に合わせる
            MakeButton(row, "nm", nm, new Vector2(26f, 1f), 120f, 22f, () => OnSelectStep(si));   // 名前クリック=選択（ゴール表示）
            var st = MakeLabel(row, "st", "", 12, new Vector2(148f, 0f), 40f, 24f);
            // 登録は「計画」ボタンに統合（選択ステップを計画→OKで保存）。行には 解除/再生 のみ。
            stepButtons.Add(MakeButton(row, "rel", "解除", new Vector2(192f, 1f), 78f, 22f, () => OnDeleteStep(robotIdx, si)));
            stepButtons.Add(MakeButton(row, "play", "再生", new Vector2(276f, 1f), 78f, 22f, () => OnPlayStep(robotIdx, si)));
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

    /// <summary>
    /// ロボット3D表示だけを指定姿勢に更新する（スライダー/goalDeg/現在: は変えない）。
    /// 登録モードの「開始/ゴール」プレビュー用（J1~J6 は常にゴール表示のまま）。
    /// </summary>
    private void PoseRobotOnly(List<float> poseDeg)
    {
        EnsureKin();
        if (poseDeg == null || poseDeg.Count == 0 || targetKin == null) { return; }
        int n = (jointNames != null && jointNames.Length > 0) ? jointNames.Length : 6;
        var arr = new double[n];
        for (int i = 0; i < n; i++) { arr[i] = (i < poseDeg.Count) ? poseDeg[i] : 0d; }
        targetKin.SetManual(true);
        targetKin.SetManualJointsDeg(arr);
    }

    /// <summary>
    /// J1~J6（JOG時は X/Y/Z/RX/RY/RZ）のスライダー/入力/JOGトグルの編集可否を切り替える。
    /// 登録モードではゴールが選択ステップで確定するので触れなくする（read-only）。
    /// PoseRobotAt/ToggleGoalSet はプログラムから SetValueWithoutNotify で更新するので影響しない。
    /// </summary>
    private void SetJointRowsInteractable(bool on)
    {
        if (sliders != null)
        {
            for (int i = 0; i < sliders.Length; i++)
            {
                if (sliders[i] != null) { sliders[i].interactable = on; }
            }
        }
        if (sliderInputs != null)
        {
            for (int i = 0; i < sliderInputs.Length; i++)
            {
                if (sliderInputs[i] != null) { sliderInputs[i].interactable = on; }
            }
        }
        if (jogToggle != null) { jogToggle.interactable = on; }
    }

    /// <summary>ステップの poseDeg(List&lt;float&gt;) を関節数ぶんの double[] に変換（「現在:」表示用）。null は null。</summary>
    private double[] PoseToDeg(List<float> poseDeg)
    {
        if (poseDeg == null) { return null; }
        int n = (jointNames != null && jointNames.Length > 0) ? jointNames.Length : 6;
        var arr = new double[n];
        for (int i = 0; i < n; i++) { arr[i] = (i < poseDeg.Count) ? poseDeg[i] : 0d; }
        return arr;
    }

    /// <summary>登録モードのラベルを "登録モード(ロボ停止・教示)：テーブル名" に更新する。</summary>
    /// <summary>CSV パス番号(R[88])。復帰モード=0 / 登録モード=選択テーブル番号(1オリジン)。</summary>
    private int CurrentPathNo()
    {
        bool reg = registerModeToggle != null && registerModeToggle.isOn;
        return reg ? selectedStep + 1 : 0;
    }

    private void UpdateRegisterLabel()
    {
        // CSV パス番号欄はモード/選択テーブルから自動更新（復帰=0 / 登録=表番号1オリジン）。
        if (csvPathInput != null) { csvPathInput.SetTextWithoutNotify(CurrentPathNo().ToString()); }
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
