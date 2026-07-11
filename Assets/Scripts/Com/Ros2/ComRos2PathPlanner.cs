using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ROS2 経路生成連携（Unity→ROS2 で始点/終点を渡し、ROS2→Unity で軌道を受け取って再生）。
///
/// 流れ：
///   1) RequestPlan(startDeg, goalDeg) で始点/終点(度・6軸)を /kmx/plan_request(kmx_msgs/PlanRequest) へ発行。
///   2) ROS2 側ノードが MoveIt で関節空間プラン → trajectory_msgs/JointTrajectory(度) を /kmx/trajectory へ発行。
///   3) 本コンポーネントが軌道を受信し、時間補間しながら各関節角を再生する。
///      実際のタグ書き込みは同一 GameObject の ComRos2 に委譲（ComRos2.ApplyValue）。
///      → 単位・可搬性（unit名解決）・タグ生成の扱いがリアルタイム受信と完全に一致する。
///
/// 前提：ComRos2 と同じ GameObject（GlobalSetting）に付く。ParameterLoader が自動アタッチする。
/// プラットフォーム：Standalone のみ（WebGL/Android/iPhone は無効化）。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ComRos2))]
public class ComRos2PathPlanner : MonoBehaviour
{
    [SerializeField] private string planRequestTopic = "/kmx/plan_request";
    [SerializeField] private string trajectoryTopic = "/kmx/trajectory";
    /// <summary>関節名（ComRos2 の Ros2Info.json 側 name と一致させる）。start/goal・軌道の並び順もこれ。</summary>
    [SerializeField] private string[] jointNames = { "J1", "J2", "J3", "J4", "J5", "J6" };
    [SerializeField] private bool loopPlayback = false;

    [Header("テスト用（Play中に本コンポーネントを右クリック→メニュー）")]
    [SerializeField] private double[] testStartDeg = new double[6];
    [SerializeField] private double[] testGoalDeg = new double[6];

    [Header("plan前に planning scene を更新")]
    [Tooltip("plan要求の前に障害物とヘッドを送って planning scene を更新する")]
    [SerializeField] private bool sendSceneBeforePlan = true;
    [Tooltip("障害物/ヘッド送信から plan要求までの待ち(秒)。scene反映が非同期なので少し待つ")]
    [SerializeField] private float sceneSettleSec = 0.4f;

    [Header("計画の粘り具合（0=ROS2ノード既定にフォールバック）")]
    [Tooltip("計画の総時間予算(秒)。難所は大きく(例8〜15)/簡単なら小さく。0=ROS2既定(plan_time_budget_sec)")]
    [SerializeField] private double planTimeBudget = 0.0;
    [Tooltip("大回り回避の許容倍率(始点→終点の直線関節距離比)。小さいほど短経路を要求(例1.5)。0=ROS2既定(plan_good_ratio)")]
    [SerializeField] private double planGoodRatio = 0.0;
    [Tooltip("復帰(通常計画)の速度倍率(0.05〜1.0)。v/a/j 上限を一律スケール。登録(optimize)には送らない。実行中も UI で調整可")]
    [SerializeField, Range(0.05f, 1.0f)] private float returnSpeedScale = 0.25f;

    [Header("計画のレビュー（計画中表示／成否／経路プレビュー→OK/Cancel）")]
    [Tooltip("軌道受信後すぐ動かさず、3Dプレビュー表示して OK(ApprovePlan)/Cancel(CancelPlan) を待つ")]
    [SerializeField] private bool requireApproval = true;
    [Tooltip("計画ステータス(std_msgs/String)トピック。計画中/成功/失敗を受ける（ROS2側が publish）")]
    [SerializeField] private string planStatusTopic = "/kmx/plan_status";
    [Tooltip("計画中のまま軌道/失敗通知が来ない場合に失敗とみなす保険の秒数（time_budget より十分大きく）")]
    [SerializeField] private float planTimeoutSec = 20f;
    [Tooltip("登録モードの探索予算(秒)。この間 ROS2 がリトライし続け、停止/予算到達でその間の最良(最短)を採用")]
    [SerializeField] private float registerSearchBudgetSec = 600f;
    [Tooltip("探索中の『無進捗』失敗判定(秒)。計画試行1回が長い場合 opt行が間遠になるため通常より大きく。手動停止あり")]
    [SerializeField] private float searchTimeoutSec = 90f;
    [Tooltip("探索中断の通知トピック（std_msgs/String）。押下で ROS2 が現在の最良で確定")]
    [SerializeField] private string planCancelTopic = "/kmx/plan_cancel";
    [Tooltip("経路プレビューの先端軌跡ライン色/幅")]
    [SerializeField] private Color previewLineColor = new Color(0.1f, 0.8f, 1f, 1f);
    [SerializeField] private float previewLineWidth = 0.01f;

    /// <summary>計画の状態。UI(専用パネル)や外部がこれを見て表示を切替える。</summary>
    public enum PlanState { Idle, Planning, Preview, Playing, Failed }
    /// <summary>現在の計画状態。</summary>
    public PlanState State { get; private set; } = PlanState.Idle;
    /// <summary>状態の付随メッセージ（成功詳細/失敗理由など。UI表示用）。</summary>
    public string StatusMessage { get; private set; } = "";
    /// <summary>状態が変わったとき (state, message) を通知する。UI パネルが購読する。</summary>
    public event Action<PlanState, string> StateChanged;

    /// <summary>計画の時間予算(秒)。0=ROS2既定。UIから設定でき、残り時間表示にも使う。</summary>
    public double PlanTimeBudget { get => planTimeBudget; set => planTimeBudget = value; }
    /// <summary>大回り回避の許容倍率。0=ROS2既定。UIから設定。</summary>
    public double PlanGoodRatio { get => planGoodRatio; set => planGoodRatio = value; }
    /// <summary>計画中の経過秒（Planning 以外は 0）。UI の残り時間表示用。</summary>
    public float PlanElapsedSec => (State == PlanState.Planning) ? Time.time - planStartTime : 0f;
    /// <summary>だんまり保険の timeout 秒（UI の残り時間上限にも使える）。</summary>
    public float PlanTimeoutSec => planTimeoutSec;
    /// <summary>ROS(endpoint) と実接続できているか（通信状態表示用）。</summary>
    public bool IsLinkUp => transport != null && transport.IsLinkUp;

    private IRos2Transport transport;
    private ComRos2 com;
    private ComRos2Obstacles obstacles;   // 同一GameObject。plan前の scene 更新に使う
    private bool started;
    private bool destroyed;

    // 再生状態
    private Ros2Trajectory traj;
    private double playT;
    private bool playing;
    private bool warnedMappingThisTraj;   // 軌道1本につきマップ失敗警告は1回だけ（毎フレーム spam 防止）

    // レビュー/プレビュー
    private float planStartTime;                 // Planning/探索 に入った時刻（経過表示・timeout 判定用。探索中はリセットしない）
    private bool optSearching;                    // 登録の最適化探索中か（停止ボタン表示・経過表示・timeout猶予に使う）
    private float lastOptMsgTime;                 // 直近の opt 行受信時刻（探索中の「無進捗」タイムアウト判定用）
    public bool OptSearching => optSearching;    // UI（停止ボタン表示）用
    /// <summary>登録の保留中か（登録ボタン押下→OK保存/NGキャンセルで解除）。UIでステップ操作を無効化するのに使う。</summary>
    public bool RegisterPending => registerPending != null;
    /// <summary>復帰(通常計画)の速度倍率(0.05〜1.0)。UI/Inspectorから設定。復帰プランに speed_scale で送る。</summary>
    public float ReturnSpeedScale { get => returnSpeedScale; set => returnSpeedScale = Mathf.Clamp(value, 0.05f, 1f); }
    private LineRenderer previewLine;            // 先端軌跡プレビュー
    private const string PreviewLineName = "Ros2PlanPreviewLine";
    private const string GhostNameSuffix = "_Ghost";   // ゴースト複製名の接尾辞（機種非依存 "<model>_Ghost"）
    private IRos2PlanTarget target;              // 計画対象ロボット（FKサンプル/ゴースト/現在角度）
    private Ros2PlanTargetRegistry registry;     // 対象ロボットの解決（選択）に使う
    private string robotId = "";                 // 計画要求に載せる robot_id（Phase1 は ""）
    private readonly System.Collections.Generic.List<Vector3> tipBuf = new();
    private double previewT;                     // ゴースト再生の時刻（ループ）
    private bool ghostActive;                    // ゴースト再生中か
    private bool ghostSeek;                      // シークバーで手動スクラブ中（自動送り停止）
    [SerializeField] private float ghostLoopPauseSec = 0.6f;   // ループ末で一瞬止める

    // ===== robotSteps シーケンス駆動（自動再生／登録モード） =====
    public enum SeqMode { Auto, Register }
    /// <summary>自動再生(既定)／登録 モード。登録モード中はロボの自動再生(タグ駆動)を止める。</summary>
    public SeqMode Mode { get; private set; } = SeqMode.Auto;
    private const float SeqPoseTolDeg = 1.0f;    // 開始点一致の許容(±度)

    private sealed class SeqEntry
    {
        public Ros2PlanTargetRegistry.RegisteredRobot robot;
        public int index;
        public Parameters.Ros2RobotStep step;
        public bool prevOn;                       // 前フレームの start タグ状態（立ち上がり検出）
        public string db = "";                    // タグ読み書き用 database（解決済み）
        public string mech = "";                  // タグ読み書き用 mechId（解決済み）
    }
    private readonly List<SeqEntry> seqEntries = new();
    private bool seqBuilt;
    private readonly Queue<SeqEntry> seqQueue = new();
    private SeqEntry activeSeq;                   // 実行中（完了で end タグ）。null=空き
    private bool seqAwaitingPlan;                 // ズレ時の自動計画の軌道待ち（受信で自動再生）
    private float seqPlaySpeed = 1f;              // step.time 再スケール用（playT の進行倍率）
    private SeqEntry registerPending;             // 登録モード：教示中の保留（承認で保存）
    private float[] registerStartDeg;             // 教示の開始姿勢（＝前step終了）
    private float[] registerEndDeg;               // 教示の終了姿勢（＝step.poseDeg）
    private bool ghostReviewOnly;                 // 「再生」でゴーストレビュー中（OK で実機再生しない）

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
        com = GetComponent<ComRos2>();
        if (com == null)
        {
            Debug.LogWarning("[ComRos2PathPlanner] 同一 GameObject に ComRos2 が必要です。無効化します。");
            enabled = false;
            return;
        }
        obstacles = GetComponent<ComRos2Obstacles>();   // plan前の scene 更新用（無くても可）
        registry = GetComponent<Ros2PlanTargetRegistry>();   // 計画対象ロボットの解決（無くても既定で動く）
        // ROSConnection はシングルトンなので ComRos2 と同じ接続を共有する（Connect は ComRos2 が実施済み）。
        transport = Ros2TransportFactory.Create();
        transport.SubscribeTrajectory(trajectoryTopic, OnTrajectory);
        transport.SubscribePlanStatus(planStatusTopic, OnPlanStatus);   // 計画中/成功/失敗
        // plan要求 publisher を起動時に事前登録（初回 Test Plan で "Not registered" レースを避ける）。
        transport.RegisterPlanRequestPublisher(planRequestTopic);
        CreatePreviewLine();
        started = true;
        Debug.Log($"[ComRos2PathPlanner] start req='{planRequestTopic}' traj='{trajectoryTopic}' joints={jointNames.Length} transport={transport.GetType().Name}");
#endif
    }

    private void OnDestroy()
    {
        destroyed = true;
        StopGhostPreview();   // ゴースト複製を残さない
        if (previewLine != null)
        {
            Destroy(previewLine.gameObject);   // 先端軌跡ラインを残さない
            previewLine = null;
        }
        // /kmx/trajectory の購読を解除（常駐 ROSConnection にコールバックが残らないよう）。
        try { transport?.Disconnect(); } catch { /* ignore */ }
    }

    /// <summary>再コンパイル/リロードで残った先端軌跡ライン・ゴーストを破棄する。</summary>
    private static void DestroyStalePreview()
    {
        foreach (var lr in FindObjectsByType<LineRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (lr != null && lr.gameObject.name == PreviewLineName)
            {
                Destroy(lr.gameObject);
            }
        }
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t != null && t.gameObject.name.EndsWith(GhostNameSuffix, StringComparison.OrdinalIgnoreCase))
            {
                Destroy(t.gameObject);
            }
        }
    }

#if UNITY_EDITOR
    // ★再コンパイル(ドメインリロード)直前に、先端軌跡ライン・ゴーストを確実に破棄する。
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditorReloadCleanup()
    {
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= CleanupOnAssemblyReload;
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += CleanupOnAssemblyReload;
    }

    private static void CleanupOnAssemblyReload()
    {
        foreach (var lr in FindObjectsByType<LineRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (lr != null && lr.gameObject.name == PreviewLineName)
            {
                DestroyImmediate(lr.gameObject);
            }
        }
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t != null && t.gameObject.name.EndsWith(GhostNameSuffix, StringComparison.OrdinalIgnoreCase))
            {
                DestroyImmediate(t.gameObject);
            }
        }
    }
#endif

    #region 要求
    /// <summary>始点/終点（度・jointNames と同数）を渡して経路生成を要求する。
    /// optimize=true は登録軌道の多目的最適化（targetTimeSec=目標所要秒・0=成り行き。REGISTER_OPTIMIZE_ROS2_SPEC.md）。</summary>
    public void RequestPlan(double[] startDeg, double[] goalDeg, bool optimize = false, double targetTimeSec = 0.0,
                            double budgetSec = -1.0)
    {
        if (!started || transport == null)
        {
            return;
        }
        if (startDeg == null || goalDeg == null
            || startDeg.Length != jointNames.Length || goalDeg.Length != jointNames.Length)
        {
            Debug.LogWarning($"[ComRos2PathPlanner] start/goal は {jointNames.Length} 要素（度）で渡してください。");
            return;
        }
        // 新しい要求。前の再生/プレビュー/ゴースト・最適化途中経過は破棄して計画中へ。
        playing = false;
        traj = null;
        ghostReviewOnly = false;
        StopGhostPreview();
        HidePreviewLine();
        ResetOptProgress();
        double budget = (budgetSec >= 0.0) ? budgetSec : planTimeBudget;   // 登録探索は大予算(600s等)を渡す
        optSearching = optimize;                                           // 探索中フラグ（中断ボタン・経過表示・timeout猶予に使う）
        // 復帰(通常計画)のみ速度倍率を送る。登録(optimize)は target_time で時間制御するため送らない(0)。
        double speedScale = optimize ? 0.0 : returnSpeedScale;
        transport.PublishPlanRequest(planRequestTopic, jointNames, startDeg, goalDeg, budget, planGoodRatio, robotId,
            optimize, targetTimeSec, speedScale: speedScale);
        planStartTime = Time.time;
        lastOptMsgTime = Time.time;
        SetState(PlanState.Planning, optimize ? "最適化探索中…" : "計画中…");
        Debug.Log($"[ComRos2PathPlanner] plan要求 start=[{string.Join(",", startDeg)}] goal=[{string.Join(",", goalDeg)}] "
            + $"time_budget={budget} good_ratio={planGoodRatio} optimize={optimize} target_time={targetTimeSec:F3}");
    }

    /// <summary>現在の関節角を始点にして、終点までの経路生成を要求する。</summary>
    public void RequestPlanFromCurrent(double[] goalDeg)
    {
        RequestPlan(ReadCurrentDeg(), goalDeg);
    }

    /// <summary>登録の最適化探索を停止し、ROS2 に現在の最良(最短)で確定させる（/kmx/plan_cancel）。</summary>
    public void RequestStopSearch()
    {
        if (transport == null || !optSearching)
        {
            return;
        }
        transport.PublishPlanCancel(planCancelTopic);
        SetState(PlanState.Planning, "確定処理中…（最良を採用）");
        Debug.Log("[ComRos2PathPlanner] 探索停止要求 → ROS2 が現在の最良で確定");
    }

    /// <summary>
    /// planning scene（障害物＋ヘッド）を先に送ってから経路生成を要求する。
    /// scene 反映は非同期（ROS2側が service で適用）なので、送信→少し待ち→plan の順にする。
    /// sendSceneBeforePlan=false や障害物コンポ非在時は通常の RequestPlan と同じ。
    /// </summary>
    public void RequestPlanWithScene(double[] startDeg, double[] goalDeg, bool optimize = false, double targetTimeSec = 0.0,
                                     double budgetSec = -1.0)
    {
        if (sendSceneBeforePlan && obstacles != null && isActiveAndEnabled)
        {
            StartCoroutine(SendSceneThenPlan(startDeg, goalDeg, optimize, targetTimeSec, budgetSec));
        }
        else
        {
            RequestPlan(startDeg, goalDeg, optimize, targetTimeSec, budgetSec);
        }
    }

    private IEnumerator SendSceneThenPlan(double[] startDeg, double[] goalDeg, bool optimize, double targetTimeSec,
                                          double budgetSec)
    {
        // 障害物とヘッド(ツール)を送って planning scene を更新。
        obstacles.SendObstacles();
        obstacles.SendHead();
        Debug.Log($"[ComRos2PathPlanner] scene(障害物+ヘッド)送信 → {sceneSettleSec:F2}s 待って plan要求");
        yield return new WaitForSeconds(sceneSettleSec);
        if (destroyed)
        {
            yield break;
        }
        RequestPlan(startDeg, goalDeg, optimize, targetTimeSec, budgetSec);
    }

    /// <summary>現在の関節角（度）を読む。タグにマップ済みなら ComRos2 経由（CRX後方互換）、
    /// 無ければ計画対象ロボットの kinematics から直接読む。UI がゴール初期値/start に使う。</summary>
    public double[] ReadCurrentDeg()
    {
        // 複数ロボ時はタグ名(J1..J6)衝突で com.TryReadValue が別ロボ(robot2)の値を返すため、
        // 対象ロボの Kinematics から直接読む。単一ロボは従来どおりタグ優先（/kmx/state と一致）。
        if (registry == null) { registry = GetComponent<Ros2PlanTargetRegistry>(); }
        if (registry != null && registry.Robots != null && registry.Robots.Count > 1)
        {
            var tt = EnsureTarget();
            return (tt != null) ? tt.GetCurrentJointsDeg() : new double[jointNames.Length];
        }
        var a = new double[jointNames.Length];
        bool allTagged = jointNames.Length > 0;
        for (int j = 0; j < jointNames.Length; j++)
        {
            if (!com.TryReadValue(jointNames[j], out a[j]))
            {
                allTagged = false;
                break;
            }
        }
        if (allTagged)
        {
            return a;
        }
        var t = EnsureTarget();
        return (t != null) ? t.GetCurrentJointsDeg() : new double[jointNames.Length];
    }

    /// <summary>計画対象ロボットを設定する（パネルの選択から呼ぶ）。関節名/robot_id を切替える。</summary>
    public void SetTarget(Ros2PlanTargetRegistry.RegisteredRobot r)
    {
        if (r == null || r.Target == null)
        {
            return;
        }
        // 対象切替時は前のゴースト/プレビュー/再生を片付ける（別機体の残骸を残さない）。
        playing = false;
        traj = null;
        StopGhostPreview();
        HidePreviewLine();
        target = r.Target;
        robotId = r.RobotId;
        var jn = r.JointNames;
        jointNames = (jn != null && jn.Length >= 1) ? jn : target.JointNames;
    }

    /// <summary>計画対象を解決（未設定ならレジストリの選択→無ければシーンの最初の Kinematics6D）。</summary>
    private IRos2PlanTarget EnsureTarget()
    {
        if (target != null)
        {
            return target;
        }
        if (registry == null)
        {
            registry = GetComponent<Ros2PlanTargetRegistry>();
        }
        if (registry != null && registry.Selected != null)
        {
            SetTarget(registry.Selected);
            return target;
        }
        // 後方互換フォールバック：シーンの最初の Kinematics6D（＝従来の単一ロボット挙動）。
        var kins = FindObjectsByType<Kinematics6D>(FindObjectsSortMode.None);
        if (kins != null && kins.Length > 0)
        {
            target = kins[0];
        }
        return target;
    }
    #endregion 要求

    #region 受信 / 再生
    private void OnTrajectory(Ros2Trajectory t)
    {
        if (destroyed)
        {
            return;   // リロードで破棄済み（購読コールバックの残留対策）
        }
        if (t == null || t.positions == null || t.positions.Length == 0 || t.timesSec == null
            || t.timesSec.Length != t.positions.Length)
        {
            Debug.LogWarning("[ComRos2PathPlanner] 空/不正な軌道を受信しました（点数と時刻数の不一致含む）。");
            return;
        }
        // 軌道の関節名を検証しておく（無音で軸を取り違えないように）。名前が有る場合は
        // 1点あたりの位置数と本数が一致しているべき。無い場合は設定 jointNames の順で index 対応する。
        int firstLen = t.positions[0] != null ? t.positions[0].Length : 0;
        if (t.jointNames != null && t.jointNames.Length > 0)
        {
            if (t.jointNames.Length != firstLen)
            {
                Debug.LogWarning($"[ComRos2PathPlanner] 軌道の関節名数({t.jointNames.Length})と位置数({firstLen})が不一致。"
                    + $"名前対応でずれる可能性があります: [{string.Join(",", t.jointNames)}]");
            }
        }
        else
        {
            Debug.Log("[ComRos2PathPlanner] 軌道に関節名が無いため、設定 jointNames の順で index 対応します。");
        }
        traj = t;
        optSearching = false;   // 軌道が届いた＝探索終了（停止ボタンを隠す）
        playT = 0d;
        playing = false;   // すぐ動かさない。承認(OK)まで待つ。
        warnedMappingThisTraj = false;
        double dur = t.timesSec[t.timesSec.Length - 1];
        Debug.Log($"[ComRos2PathPlanner] 軌道受信: {t.positions.Length}点 / {(t.jointNames != null ? t.jointNames.Length : 0)}軸 / 所要 {dur:F2}s");

        // シーケンスの自動計画（開始点ズレ/キャッシュ無効時）＝承認スキップで即再生（キャッシュ保存はしない）。
        if (seqAwaitingPlan && activeSeq != null)
        {
            seqAwaitingPlan = false;
            SetSeqPlaySpeed(activeSeq.step, dur);
            string rep = AnalyzeTraj(t, activeSeq.step.time, activeSeq.robot.Target != null ? activeSeq.robot.Target.ModelKey : "", out bool warn);
            playT = 0d;
            playing = true;
            SetState(PlanState.Playing, $"再生(自動計画): {activeSeq.step.name} {rep}");
            if (warn) { Debug.LogWarning($"[ComRos2PathPlanner] {activeSeq.step.name}: {rep}"); }
            return;
        }

        BuildPreviewLine(t);   // 先端の軌跡を3D表示
        if (requireApproval)
        {
            SetState(PlanState.Preview, $"成功: {t.positions.Length}点 / {dur:F1}s");
            RefreshOptPreview();   // 最適化結果(opt done)が既に揃っていれば所要/最短表示へ上書き（順序非依存）
            StartGhostPreview();   // 半透明複製が経路をなぞる（ヘッドの当たり確認用）
        }
        else
        {
            // 承認不要モード（従来動作）＝そのまま再生。
            ApprovePlan();
        }
    }

    // --- 登録軌道 多目的最適化の途中経過（/kmx/plan_status の "opt ..." 行。REGISTER_OPTIMIZE_ROS2_SPEC.md） ---
    private bool optActive;                 // 最適化の進捗行を受信中か（UI が進捗表示に使う）
    private string optProgress = "";        // 表示用の途中経過テキスト
    private float optProgress01;            // 進捗 0..1（prog= から。バー用）
    private string optResultWarn = "";      // 完了時の警告（目標時間未達など）。無ければ空
    private bool optHasResult;              // opt done を受信済み（最短などの結果あり）
    private double optAchieved;             // 最適化後の所要秒（achieved＝実際の再生時間）
    private double optMinTime;              // 達成可能な最短秒（t_min）。target を守れた時も表示する
    private bool optFeasible = true;        // target_time を満たせたか
    /// <summary>最適化の途中経過/結果を UI へ公開。</summary>
    public bool OptActive => optActive;
    public string OptProgress => optProgress;
    public float OptProgress01 => optProgress01;
    public string OptResultWarn => optResultWarn;
    public bool OptHasResult => optHasResult;
    public double OptMinTime => optMinTime;
    private void ResetOptProgress()
    {
        optActive = false;
        optProgress = "";
        optProgress01 = 0f;
        optResultWarn = "";
        optHasResult = false;
        optAchieved = 0d;
        optMinTime = 0d;
        optFeasible = true;
    }

    /// <summary>登録最適化のプレビュー表示を更新（軌道と opt done が両方揃ったら 所要/最短/軸速% を表示）。順序非依存。</summary>
    private void RefreshOptPreview()
    {
        if (!optHasResult || traj == null || State != PlanState.Preview || registerPending == null)
        {
            return;
        }
        string mk = registerPending.robot != null && registerPending.robot.Target != null
            ? registerPending.robot.Target.ModelKey
            : (target != null ? target.ModelKey : "");
        // 所要・最短とも ROS2 の opt done 値(achieved / min_time)で一貫表示（軌道実時間だと丸めで逆転するため achieved を使う）。
        float achMs = (float)((optAchieved > 0d ? optAchieved : optMinTime) * 1000.0);
        double setSec = registerPending.step != null ? registerPending.step.time / 1000.0 : 0.0;
        // 所要 > 設定（達成不能）＝ 最短(=所要)は冗長なので「設定◯s」を表示。守れた/成り行きは「最短◯s」。
        bool infeasible = !optFeasible && setSec > 0d;
        bool warn;
        string rep = infeasible
            ? AnalyzeTraj(traj, achMs, mk, out warn, optMinTime, "設定", setSec)
            : AnalyzeTraj(traj, achMs, mk, out warn, optMinTime);
        string wm = infeasible ? " ⚠目標未達" : "";
        SetState(PlanState.Preview, $"最適化完了: {rep}{wm}");   // OK/NG はボタンにあるので繰り返さない
        if (warn || infeasible) { Debug.LogWarning($"[ComRos2PathPlanner] 登録最適化: {rep}{wm}"); }
    }

    /// <summary>ROS2 の計画ステータス(std_msgs/String)を受けて状態を更新する。</summary>
    private void OnPlanStatus(string data)
    {
        if (destroyed || string.IsNullOrEmpty(data))
        {
            return;
        }
        // 最適化の途中経過/結果： "opt phase=jerk iter=42 time=1.85 prog=60" / "opt done time=.. feasible=0 min_time=.."
        if (data.StartsWith("opt", StringComparison.OrdinalIgnoreCase))
        {
            ParseOptStatus(data);
            return;
        }
        // 例: "planning" / "succeeded:74:1.8" / "failed:no_solution"
        if (data.StartsWith("failed", StringComparison.OrdinalIgnoreCase))
        {
            optSearching = false;
            string reason = data.Length > 6 ? data.Substring(6).TrimStart(':', ' ') : "";
            SetState(PlanState.Failed, string.IsNullOrEmpty(reason) ? "計画失敗" : $"計画失敗: {reason}");
        }
        else if (data.StartsWith("planning", StringComparison.OrdinalIgnoreCase))
        {
            if (State != PlanState.Preview && State != PlanState.Playing)
            {
                SetState(PlanState.Planning, "計画中…");
            }
        }
        // "succeeded" は軌道(OnTrajectory)受信でプレビュー遷移するので、ここでは状態を変えない
        // （メッセージとしてログのみ）。
        else if (data.StartsWith("succeeded", StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"[ComRos2PathPlanner] plan_status: {data}");
        }
    }

    /// <summary>"opt ..." 進捗/結果行をパースして UI 公開値を更新する。</summary>
    private void ParseOptStatus(string data)
    {
        Debug.Log($"[ComRos2PathPlanner] {data}");
        lastOptMsgTime = Time.time;   // 進捗が来ている＝生存。無進捗 watchdog をリセット
        bool done = data.IndexOf("done", StringComparison.OrdinalIgnoreCase) >= 0;
        string phase = OptKv(data, "phase");
        double t = OptKvD(data, "time", double.NaN);
        double prog = OptKvD(data, "prog", double.NaN);
        int iter = (int)OptKvD(data, "iter", 0);
        if (done)
        {
            optActive = false;
            optSearching = false;   // 探索終了
            optProgress01 = 1f;
            optFeasible = OptKvD(data, "feasible", 1.0) != 0.0;
            optMinTime = OptKvD(data, "min_time", 0.0);
            optAchieved = double.IsNaN(t) ? 0.0 : t;
            optHasResult = true;
            // 時間を守れた場合も「最短」を併記（例: 所要3.00s（最短2.64s））。守れない時は警告。
            string at = optAchieved > 0.0 ? $"所要{optAchieved:F2}s" : "";
            string minS = optMinTime > 0.0 ? $"（最短{optMinTime:F2}s）" : "";
            optResultWarn = (!optFeasible && optMinTime > 0.0) ? "⚠ 目標時間未達" : "";
            optProgress = "最適化完了 " + at + minS + (optResultWarn.Length > 0 ? "  " + optResultWarn : "");
            RefreshOptPreview();   // 軌道が先に届いていた場合（順序非依存）はここでプレビュー表示を更新
        }
        else if (phase == "search")
        {
            // 長時間探索フェーズ：現在の最良(最短)を表示。経過時間は Panel が毎フレーム付加。
            optActive = true;
            double best = OptKvD(data, "best", double.NaN);
            string bestStr = double.IsNaN(best) ? "" : $" 最良{best:F2}s";
            optProgress = $"探索中{bestStr} ({iter}回)";
        }
        else
        {
            optActive = true;
            if (!double.IsNaN(prog)) { optProgress01 = Mathf.Clamp01((float)(prog / 100.0)); }
            string phaseJa = phase == "time" ? "時間" : phase == "jerk" ? "ジャーク" : phase == "torque" ? "トルク" : phase;
            string progStr = double.IsNaN(prog) ? "" : $"{prog:F0}% ";
            string tStr = double.IsNaN(t) ? "" : $" 所要{t:F2}s";
            optProgress = $"最適化中[{phaseJa}] {progStr}iter{iter}{tStr}";
        }
    }

    /// <summary>"key=value" を空白区切り文字列から取り出す（無ければ空）。</summary>
    private static string OptKv(string data, string key)
    {
        var tokens = data.Split(' ');
        foreach (var tk in tokens)
        {
            int eq = tk.IndexOf('=');
            if (eq > 0 && string.Equals(tk.Substring(0, eq), key, StringComparison.OrdinalIgnoreCase))
            {
                return tk.Substring(eq + 1);
            }
        }
        return "";
    }
    private static double OptKvD(string data, string key, double fallback)
    {
        string v = OptKv(data, key);
        return double.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : fallback;
    }

    private void Update()
    {
        if (!started || destroyed)
        {
            return;
        }
        // 計画中に軌道も失敗通知も来ないまま時間超過 → 失敗扱い（だんまり防止の保険）。
        // 登録の長時間探索中は総経過でなく「無進捗(opt行が来ない)」で判定（10分探索でも誤タイムアウトしない）。
        if (State == PlanState.Planning)
        {
            // 探索中は「無進捗(opt行が来ない)」で判定＋大きめ猶予(searchTimeoutSec)。通常計画は総経過(planTimeoutSec)。
            float limit = optSearching ? searchTimeoutSec : planTimeoutSec;
            float since = optSearching ? (Time.time - lastOptMsgTime) : (Time.time - planStartTime);
            if (since > limit)
            {
                string tmsg = optSearching
                    ? $"探索応答なし（{limit:F0}s 進捗なし）"
                    : $"タイムアウト（{limit:F0}s 応答なし）";
                optSearching = false;
                SetState(PlanState.Failed, tmsg);
            }
        }

        // robotSteps シーケンス：自動再生モードのみ、start タグの立ち上がりを監視して順次実行。
        if (Mode == SeqMode.Auto)
        {
            PollSequence();
        }

        // プレビュー中：ゴースト(半透明複製)を軌道でループ再生（実機モデルは動かさない）。
        if (ghostActive && State == PlanState.Preview && traj != null && target != null)
        {
            var gt = traj.timesSec;
            double gtotal = gt[gt.Length - 1];
            if (!ghostSeek)   // シーク中は自動送りを止め、シーク位置で固定表示（スクラブ確認）。
            {
                previewT += Time.deltaTime;
                if (gtotal <= 0d || previewT > gtotal + ghostLoopPauseSec)
                {
                    previewT = 0d;   // 先頭へ（末尾で少しポーズしてループ）
                }
            }
            double sampleT = previewT < 0d ? 0d : (previewT < gtotal ? previewT : gtotal);
            target.PoseGhostDeg(SamplePoseJ16(sampleT));
        }

        if (!playing || traj == null)
        {
            return;
        }
        if (com == null || !com.IsReady)
        {
            return;   // 解決前は待つ
        }

        var times = traj.timesSec;
        int last = times.Length - 1;
        double total = times[last];

        // シーケンス実行中で step.time>0 のときは、軌道を step.time 秒に再スケール（進行倍率）。
        playT += Time.deltaTime * ((activeSeq != null && seqPlaySpeed > 0f) ? seqPlaySpeed : 1f);

        double[] pos;
        if (playT >= total || last == 0)
        {
            pos = traj.positions[last];
            if (loopPlayback)
            {
                playT = 0d;   // 先頭へ
            }
            else
            {
                playing = false;       // 最終姿勢で停止
                SetState(PlanState.Idle, "再生完了");
                OnSeqPlaybackDone();   // シーケンス実行中なら end タグに 1 を書いて完了・次へ
            }
        }
        else
        {
            // playT を含む区間 [i, i+1] を探す（times は単調増加前提）
            int i = 0;
            while (i < last && times[i + 1] < playT)
            {
                i++;
            }
            double t0 = times[i];
            double t1 = times[i + 1];
            double a = (t1 > t0) ? (playT - t0) / (t1 - t0) : 0d;
            // playT が times[0] より前（開始時刻が非0の軌道など）だと a が負になり、
            // 始点より手前へ外挿してしまう。区間内比率として [0,1] にクランプする。
            a = a < 0d ? 0d : (a > 1d ? 1d : a);
            double[] p0 = traj.positions[i];
            double[] p1 = traj.positions[i + 1];
            pos = new double[p0.Length];
            for (int j = 0; j < p0.Length; j++)
            {
                pos[j] = p0[j] + (p1[j] - p0[j]) * a;
            }
        }

        ApplyPositions(pos);
    }

    /// <summary>1点分の関節角（度）を ComRos2 のマッピング経由でタグへ書く。</summary>
    private void ApplyPositions(double[] pos)
    {
        if (pos == null)
        {
            return;
        }
        // 複数ロボ時は /kmx state の関節名(J1..J6)がタグで衝突し、com.ApplyValue が別ロボ(最後に登録された
        // robot2)へ漏れる。その場合は対象ロボの Kinematics を直接駆動して漏れを防ぐ（軌道の関節順→対象ロボ順に
        // 並べ替え。SetManual(true) でタグ駆動の上書きも防止）。単一ロボは従来どおりタグ経由（/kmx/state 同期を維持）。
        if (registry == null) { registry = GetComponent<Ros2PlanTargetRegistry>(); }
        bool multiRobot = registry != null && registry.Robots != null && registry.Robots.Count > 1;
        if (multiRobot && target != null)
        {
            var tn = traj != null ? traj.jointNames : null;
            var ordered = new double[jointNames.Length];
            for (int k = 0; k < jointNames.Length; k++)
            {
                int idx = k;
                if (tn != null && tn.Length > 0)
                {
                    idx = Array.IndexOf(tn, jointNames[k]);
                    if (idx < 0) { idx = k; }
                }
                ordered[k] = (idx >= 0 && idx < pos.Length) ? pos[idx] : 0d;
            }
            target.SetManual(true);
            target.SetManualJointsDeg(ordered);
            return;
        }
        var names = traj.jointNames;
        bool haveNames = names != null && names.Length > 0;
        for (int j = 0; j < pos.Length; j++)
        {
            if (haveNames)
            {
                // 軌道に関節名が有るなら「名前で厳密に」対応させる。名前が範囲外/未マップでも
                // 設定 jointNames[j] への index フォールバックはしない（軌道の並びが設定と違うと
                // 別の軸へ pos[j] を誤適用＝軸取り違えになるため）。失敗は警告して読み飛ばす。
                string name = j < names.Length ? names[j] : null;
                if (string.IsNullOrEmpty(name) || !com.ApplyValue(name, pos[j]))
                {
                    if (!warnedMappingThisTraj)
                    {
                        Debug.LogWarning($"[ComRos2PathPlanner] 軌道の関節 '{name}' をタグへマップできません"
                            + "（Ros2Info.json の name と不一致）。この軸は適用しません。");
                        warnedMappingThisTraj = true;
                    }
                }
            }
            else
            {
                // 軌道に名前が無い場合のみ、設定 jointNames[j] へ index 対応で適用する。
                if (j < jointNames.Length)
                {
                    com.ApplyValue(jointNames[j], pos[j]);
                }
            }
        }
    }
    #endregion 受信 / 再生

    #region 承認 / プレビュー
    /// <summary>プレビュー中の経路を承認して再生する（OK）。</summary>
    [ContextMenu("Approve Plan (OK・実行)")]
    public void ApprovePlan()
    {
        if (traj == null)
        {
            Debug.LogWarning("[ComRos2PathPlanner] 承認する軌道がありません。");
            return;
        }
        if (ghostReviewOnly)
        {
            return;   // 「再生」ゴーストレビュー中の OK は無効（実機は動かさない）
        }
        // 登録モードの教示承認 → 軌道をキャッシュへ保存（実機は動かさない）。
        if (registerPending != null)
        {
            SaveRegisteredCache();
            return;
        }
        StopGhostPreview();   // ゴーストを消して実機モデルで再生する
        HidePreviewLine();
        playT = 0d;
        playing = true;
        SetState(PlanState.Playing, "実行中…");
    }

    /// <summary>プレビュー中の経路を破棄する（Cancel）。ロボットは動かさない。</summary>
    [ContextMenu("Cancel Plan (破棄)")]
    public void CancelPlan()
    {
        playing = false;
        traj = null;
        StopGhostPreview();
        HidePreviewLine();
        // シーケンス/登録の保留も破棄（end タグは書かない＝完了扱いにしない）。
        registerPending = null;
        seqAwaitingPlan = false;
        activeSeq = null;
        ghostReviewOnly = false;
        SetState(PlanState.Idle, "キャンセル");
    }

    #region robotSteps シーケンス駆動（自動再生／登録）
    /// <summary>モード切替（UI から）。登録モードに入るとロボの自動再生(タグ駆動)だけ止める（他ユニットは動く）。</summary>
    public void SetMode(SeqMode m)
    {
        if (Mode == m)
        {
            return;
        }
        Mode = m;
        // どちらへ切り替えても進行中の再生/計画/ゴースト/レビューは片付ける
        //（登録解除でゴーストが残らないように）。
        playing = false;
        traj = null;
        seqAwaitingPlan = false;
        activeSeq = null;
        ghostReviewOnly = false;
        seqQueue.Clear();
        StopGhostPreview();
        HidePreviewLine();
        SetState(PlanState.Idle, m == SeqMode.Register ? "登録モード" : "自動再生モード");
    }

    /// <summary>監視対象（レジストリ各ロボの robotSteps）を構築する（ロード後1回）。</summary>
    private void BuildSequence()
    {
        seqEntries.Clear();
        if (registry == null)
        {
            registry = GetComponent<Ros2PlanTargetRegistry>();
        }
        if (registry == null)
        {
            return;
        }
        var robots = registry.Robots;
        foreach (var r in robots)
        {
            var steps = r != null && r.Target != null ? r.Target.PlanSteps : null;
            if (steps == null)
            {
                continue;
            }
            GlobalScript.TryResolveUnitDb(r.Target.UnitName, out var db, out var mech);
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i] == null || string.IsNullOrEmpty(steps[i].start))
                {
                    continue;
                }
                seqEntries.Add(new SeqEntry { robot = r, index = i, step = steps[i], db = db ?? "", mech = mech ?? "" });
            }
        }
        seqBuilt = true;
        if (seqEntries.Count > 0)
        {
            Debug.Log($"[ComRos2PathPlanner] robotSteps 監視 {seqEntries.Count}件");
        }
    }

    /// <summary>start タグの立ち上がりを監視し、空いていれば順次実行（1台1計画＝キュー）。</summary>
    private void PollSequence()
    {
        if (com == null || !com.IsReady || !GlobalScript.isLoaded)
        {
            return;
        }
        if (!seqBuilt)
        {
            if (registry == null)
            {
                registry = GetComponent<Ros2PlanTargetRegistry>();
            }
            if (registry == null || !registry.IsBuilt)
            {
                return;
            }
            BuildSequence();
        }
        foreach (var s in seqEntries)
        {
            bool on = ReadTagOn(s, s.step.start);
            if (on && !s.prevOn && s != activeSeq && !seqQueue.Contains(s))
            {
                seqQueue.Enqueue(s);   // 立ち上がり → キュー投入
            }
            s.prevOn = on;
        }
        if (activeSeq == null && !playing && State != PlanState.Planning && seqQueue.Count > 0)
        {
            StartSeqStep(seqQueue.Dequeue());
        }
    }

    /// <summary>1ステップ開始：キャッシュ再生／開始点ズレ・poseDeg変更時は自動計画／未登録はスキップ。</summary>
    private void StartSeqStep(SeqEntry s)
    {
        activeSeq = s;
        WriteTag(s, s.step.end, 0);   // 実行開始＝end を落とす

        SetTarget(s.robot);            // 対象ロボへ切替（jointNames/robot_id）
        var goal = ToDoubleArray(s.step.poseDeg);
        if (goal == null || goal.Length == 0)
        {
            Debug.LogWarning($"[ComRos2PathPlanner] step '{s.step.name}'(#{s.index}) poseDeg 空 → スキップ。");
            SkipStep(s);
            return;
        }
        var cache = Ros2TrajCacheStore.Get(s.robot.RobotId, s.index);
        if (cache == null)
        {
            Debug.LogWarning($"[ComRos2PathPlanner] step '{s.step.name}'(#{s.index}) キャッシュ未登録 → スキップ。");
            SkipStep(s);
            return;
        }
        var cur = ReadCurrentDeg();
        if (!ApproxEqual(cache.endDeg, goal, SeqPoseTolDeg))
        {
            Debug.Log($"[ComRos2PathPlanner] step '{s.step.name}' poseDeg 変更でキャッシュ無効 → 自動計画。");
            AutoPlanStep(cur, goal);
            return;
        }
        if (!ApproxEqual(cache.startDeg, cur, SeqPoseTolDeg))
        {
            Debug.Log($"[ComRos2PathPlanner] step '{s.step.name}' 開始点ズレ → 自動計画。");
            AutoPlanStep(cur, goal);
            return;
        }
        // キャッシュ再生（承認スキップ・ゴースト無し）
        traj = BuildTrajFromCache(cache);
        SetSeqPlaySpeed(s.step, traj.timesSec[traj.timesSec.Length - 1]);
        string rep = AnalyzeStepDisplay(traj, s.step.time / 1000.0, cache.minTimeSec, s.robot.Target != null ? s.robot.Target.ModelKey : "", out bool warn);
        playT = 0d;
        playing = true;
        SetState(PlanState.Playing, $"再生(ｷｬｯｼｭ): {s.step.name} {rep}");
        if (warn) { Debug.LogWarning($"[ComRos2PathPlanner] {s.step.name}: {rep}"); }
    }

    private void AutoPlanStep(double[] cur, double[] goal)
    {
        seqAwaitingPlan = true;                 // 受信で OnTrajectory→承認スキップ自動再生
        RequestPlanWithScene(cur, goal);
    }

    private void SkipStep(SeqEntry s)
    {
        WriteTag(s, s.step.end, 1);             // サイクルを止めない（未登録/空はスキップ扱い）
        activeSeq = null;
    }

    /// <summary>シーケンス再生の完了処理（Update から）。end タグに 1 を書いて次へ。</summary>
    private void OnSeqPlaybackDone()
    {
        if (activeSeq == null)
        {
            return;
        }
        WriteTag(activeSeq, activeSeq.step.end, 1);
        activeSeq = null;
        seqPlaySpeed = 1f;
    }

    private void SetSeqPlaySpeed(Parameters.Ros2RobotStep step, double nativeDur)
    {
        double timeSec = (step != null) ? step.time / 1000d : 0d;   // time は ms
        seqPlaySpeed = (timeSec > 0d && nativeDur > 0d) ? (float)(nativeDur / timeSec) : 1f;
    }

    // --- 登録モード（教示） ---
    /// <summary>指定 step を教示登録：開始点(前step終了・循環)→poseDeg を計画しゴーストプレビュー。OK で保存。</summary>
    public void RegisterStep(int robotIndex, int stepIndex)
    {
        if (registry == null)
        {
            registry = GetComponent<Ros2PlanTargetRegistry>();
        }
        if (registry == null)
        {
            return;
        }
        var robots = registry.Robots;
        if (robotIndex < 0 || robotIndex >= robots.Count)
        {
            return;
        }
        var r = robots[robotIndex];
        var steps = r != null && r.Target != null ? r.Target.PlanSteps : null;
        if (steps == null || stepIndex < 0 || stepIndex >= steps.Count)
        {
            return;
        }
        SetTarget(r);
        var endDeg = ToDoubleArray(steps[stepIndex].poseDeg);
        if (endDeg == null || endDeg.Length == 0)
        {
            Debug.LogWarning("[ComRos2PathPlanner] poseDeg 空で登録不可。");
            return;
        }
        var startDeg = ToDoubleArray(PrevPoseDeg(steps, stepIndex));   // 前stepの終了(循環)
        if (startDeg == null || startDeg.Length != endDeg.Length)
        {
            startDeg = ReadCurrentDeg();
        }
        registerPending = new SeqEntry { robot = r, index = stepIndex, step = steps[stepIndex] };
        registerStartDeg = ToFloat(startDeg);
        registerEndDeg = ToFloat(endDeg);
        // ロボを開始姿勢へ置いてから計画（教示の始点を揃える）。
        // ★対象ロボの Kinematics を直接 manual で置く。com.ApplyValue は subByName が関節名(J1..J6)キーで
        //   複数ロボを区別できず、別ロボ(非manual側)へ書込みが漏れるため使わない。SetManual(true) で
        //   タグ駆動の上書きも防ぐ（パネルの PoseRobotAt と同じ方式）。
        if (target != null)
        {
            target.SetManual(true);
            target.SetManualJointsDeg(startDeg);
        }
        // 登録は多目的最適化を要求（優先度 時間>ジャーク>トルク）。time は ms→秒（0=成り行き）。
        // 大きな探索予算(registerSearchBudgetSec・既定10分)で回し続け、ユーザーが「停止」するか予算到達で
        // その間の最良(最短)を採用する（ROS2側 optimize は good_enough 早期終了せず継続）。
        double targetTimeSec = steps[stepIndex].time / 1000.0;
        RequestPlanWithScene(startDeg, endDeg, optimize: true, targetTimeSec: targetTimeSec,
            budgetSec: registerSearchBudgetSec);   // 受信→プレビュー（OK で SaveRegisteredCache）
    }

    /// <summary>登録キャッシュを削除（再登録可能に）。</summary>
    public void DeleteStepCache(int robotIndex, int stepIndex)
    {
        if (registry == null)
        {
            registry = GetComponent<Ros2PlanTargetRegistry>();
        }
        if (registry == null || robotIndex < 0 || robotIndex >= registry.Robots.Count)
        {
            return;
        }
        Ros2TrajCacheStore.Delete(registry.Robots[robotIndex].RobotId, stepIndex);
        Debug.Log($"[ComRos2PathPlanner] キャッシュ削除: robot#{robotIndex} step#{stepIndex}");
    }

    /// <summary>ステップにキャッシュ登録済みか（UI 表示用）。</summary>
    public bool HasStepCache(int robotIndex, int stepIndex)
    {
        if (registry == null)
        {
            registry = GetComponent<Ros2PlanTargetRegistry>();
        }
        if (registry == null || robotIndex < 0 || robotIndex >= registry.Robots.Count)
        {
            return false;
        }
        return Ros2TrajCacheStore.Get(registry.Robots[robotIndex].RobotId, stepIndex) != null;
    }

    /// <summary>登録済みステップの軌道を「ゴースト(半透明複製)」でループ再生（実機は動かさない・レビュー用）。</summary>
    public void PlayStepGhost(int robotIndex, int stepIndex)
    {
        if (registry == null)
        {
            registry = GetComponent<Ros2PlanTargetRegistry>();
        }
        if (registry == null || robotIndex < 0 || robotIndex >= registry.Robots.Count)
        {
            return;
        }
        var r = registry.Robots[robotIndex];
        var cache = Ros2TrajCacheStore.Get(r.RobotId, stepIndex);
        if (cache == null)
        {
            Debug.LogWarning($"[ComRos2PathPlanner] step#{stepIndex} 未登録のためゴースト再生できません。");
            return;
        }
        SetTarget(r);                  // 対象ロボへ（ゴーストもこのロボで作る）。playing/traj/ghost はリセット
        playing = false;               // 実機は動かさない
        traj = BuildTrajFromCache(cache);
        ghostReviewOnly = true;        // レビュー中：OK は無効化
        BuildPreviewLine(traj);        // 先端の軌跡も表示
        float tSec = 0f;
        var psteps = r.Target != null ? r.Target.PlanSteps : null;
        if (psteps != null && stepIndex < psteps.Count && psteps[stepIndex] != null)
        {
            tSec = psteps[stepIndex].time;
        }
        string rep = AnalyzeStepDisplay(traj, tSec / 1000.0, cache.minTimeSec, r.Target != null ? r.Target.ModelKey : "", out bool warn);
        SetState(PlanState.Preview, $"ｺﾞｰｽﾄ再生 step#{stepIndex}: {rep}（NGで停止）");
        if (warn) { Debug.LogWarning($"[ComRos2PathPlanner] step#{stepIndex}: {rep}"); }
        StartGhostPreview();           // Update がゴーストを軌道でループ再生
    }

    /// <summary>ApprovePlan から：教示中の軌道をキャッシュ保存し、ロボを終了姿勢へ置く。</summary>
    private void SaveRegisteredCache()
    {
        if (registerPending == null || traj == null)
        {
            registerPending = null;
            return;
        }
        var e = new Ros2TrajCacheStore.Entry
        {
            robotId = registerPending.robot.RobotId,
            stepIndex = registerPending.index,
            name = registerPending.step != null ? registerPending.step.name : "",
            startDeg = new List<float>(registerStartDeg ?? new float[0]),
            endDeg = new List<float>(registerEndDeg ?? new float[0]),
            jointNames = new List<string>(traj.jointNames ?? jointNames),
            minTimeSec = (float)optMinTime,   // ROS2 最適化の達成可能最短（再生/再登録レビュー表示用）
        };
        for (int p = 0; p < traj.positions.Length; p++)
        {
            e.timesSec.Add((float)traj.timesSec[p]);
            var row = new List<float>();
            var pp = traj.positions[p];
            for (int j = 0; j < pp.Length; j++)
            {
                row.Add((float)pp[j]);
            }
            e.positions.Add(row);
        }
        Ros2TrajCacheStore.Put(e);
        // ロボを終了姿勢へ（次stepの開始点＝この終了点を揃える）。対象ロボ固有に置く（別ロボへ漏らさない）。
        var regTarget = registerPending.robot != null ? registerPending.robot.Target : target;
        if (registerEndDeg != null && regTarget != null)
        {
            var endD = new double[registerEndDeg.Length];
            for (int j = 0; j < endD.Length; j++) { endD[j] = registerEndDeg[j]; }
            regTarget.SetManual(true);
            regTarget.SetManualJointsDeg(endD);
        }
        string mk = registerPending.robot.Target != null ? registerPending.robot.Target.ModelKey : "";
        float tSec = registerPending.step != null ? registerPending.step.time : 0f;
        string rep = AnalyzeStepDisplay(traj, tSec / 1000.0, optMinTime, mk, out bool warn);
        Debug.Log($"[ComRos2PathPlanner] 登録: {e.robotId} step#{e.stepIndex} ({e.positions.Count}点) {rep}");
        if (warn) { Debug.LogWarning($"[ComRos2PathPlanner] 登録 {e.name}: {rep}"); }
        StopGhostPreview();
        HidePreviewLine();
        registerPending = null;
        SetState(PlanState.Idle, "登録完了");
    }

    // --- ヘルパ ---
    private Ros2Trajectory BuildTrajFromCache(Ros2TrajCacheStore.Entry e)
    {
        var t = new Ros2Trajectory
        {
            jointNames = (e.jointNames != null && e.jointNames.Count > 0) ? e.jointNames.ToArray() : jointNames,
            timesSec = new double[e.timesSec.Count],
            positions = new double[e.positions.Count][],
        };
        for (int i = 0; i < e.timesSec.Count; i++)
        {
            t.timesSec[i] = e.timesSec[i];
        }
        for (int i = 0; i < e.positions.Count; i++)
        {
            var row = e.positions[i];
            var d = new double[row.Count];
            for (int j = 0; j < row.Count; j++)
            {
                d[j] = row[j];
            }
            t.positions[i] = d;
        }
        return t;
    }

    /// <summary>登録軌道の再生/登録用の解析文字列を「動作時間ルール」で作る（設定と最短の大小で所要/括弧を切替）。
    /// 規約：最短&gt;設定→所要=最短(設定:XX) ／ 設定≥最短→所要=設定(最短:XX)。所要=大きい方＝実動作時間(achieved)。
    /// 所要は軌道の実時間(timeMs=0→effDur=nativeDur=achieved)を使う。</summary>
    private string AnalyzeStepDisplay(Ros2Trajectory tr, double setSec, double minSec, string modelKey, out bool warn)
    {
        bool minGtSet = minSec > 0d && setSec > 0d && minSec > setSec + 0.05d;   // 最短 > 設定（＝目標未達）
        return minGtSet
            ? AnalyzeTraj(tr, 0f, modelKey, out warn, minSec, "設定", setSec)   // 所要=最短, 括弧=設定
            : AnalyzeTraj(tr, 0f, modelKey, out warn, minSec);                  // 所要=設定(or最短), 括弧=最短
    }

    /// <summary>軌道の所要/軸速%を解析（Step A）。warn=超過。
    /// minTimeDisplay&gt;0 で局所の時間超過判定を無効化（最適化軌道は ROS2 が feasible 判定済）。
    /// 括弧内は既定「最短(=minTimeDisplay or 軌道時間)」。parenValue≥0 なら parenLabel＋parenValue に差し替え
    /// （例：達成不能時は「設定◯s」）。速度計算・所要は従来どおり。</summary>
    private string AnalyzeTraj(Ros2Trajectory tr, float timeMs, string modelKey, out bool warn,
                               double minTimeDisplay = 0d, string parenLabel = "最短", double parenValue = -1d)
    {
        warn = false;
        if (tr == null || tr.timesSec == null || tr.timesSec.Length < 2
            || tr.positions == null || tr.positions.Length < 2 || tr.positions[0] == null)
        {
            return "";
        }
        double timeSec = timeMs / 1000d;   // time は ms
        int last = tr.timesSec.Length - 1;
        double nativeDur = tr.timesSec[last];
        double effDur = (timeSec > 0d) ? timeSec : nativeDur;
        if (effDur <= 0d)
        {
            effDur = (nativeDur > 0d) ? nativeDur : 1d;
        }
        double scale = (nativeDur > 0d && effDur > 0d) ? nativeDur / effDur : 1d;   // 再生速度倍率
        int nj = tr.positions[0].Length;
        var limits = Ros2MotorLimits.MaxJointSpeedDeg(modelKey, nj);
        var peak = new double[nj];
        for (int p = 1; p <= last; p++)
        {
            double dt = tr.timesSec[p] - tr.timesSec[p - 1];
            if (dt <= 1e-6 || tr.positions[p] == null || tr.positions[p - 1] == null)
            {
                continue;
            }
            var a = tr.positions[p];
            var b = tr.positions[p - 1];
            for (int j = 0; j < nj && j < a.Length && j < b.Length; j++)
            {
                double w = System.Math.Abs(a[j] - b[j]) / dt * scale;   // 実効角速度(°/s)
                if (w > peak[j]) { peak[j] = w; }
            }
        }
        double maxRatio = 0d;
        int worst = -1;
        for (int j = 0; j < nj; j++)
        {
            double lim = (limits != null && j < limits.Length && limits[j] > 0f) ? limits[j] : 0d;
            double ratio = (lim > 0d) ? peak[j] / lim : 0d;
            if (ratio > maxRatio) { maxRatio = ratio; worst = j; }
        }
        double showMin = (minTimeDisplay > 0d) ? minTimeDisplay : nativeDur;   // 既定の「最短」（最適化時は ROS2 の t_min）
        double parenShown = (parenValue >= 0d) ? parenValue : showMin;         // 括弧内の値（設定◯s に差し替え可）
        bool overSpeed = maxRatio > 1.0d;
        // 最適化軌道(minTimeDisplay>0)は ROS2 が feasible 判定済なので局所の時間超過判定はしない。
        bool overTime = (minTimeDisplay <= 0d) && timeSec > 0f && nativeDur > 0d && timeSec < nativeDur - 1e-3;
        warn = overSpeed || overTime;
        string s = $"所要{effDur:F2}s({parenLabel}{parenShown:F2}s) 軸速{maxRatio * 100d:F0}%";
        if (worst >= 0)
        {
            s += $"(J{worst + 1})";
        }
        double g = PeakTipAccelG(tr, scale, out bool hasG);   // ヘッド先端のピーク加速度(G)
        if (hasG) { s += $" 加速{g:F2}G"; }
        if (overTime) { s += " ⚠時間<最短"; }
        if (overSpeed) { s += " ⚠速度超過"; }
        return s;
    }

    private readonly List<double[]> accelJ16Buf = new();
    private readonly List<Vector3> accelTipBuf = new();
    /// <summary>ヘッド先端の world 座標を FK で出し（全点・間引きなし）、2階差分でピーク加速度を求め G(=÷9.80665)で返す。
    /// scale(=nativeDur/effDur) で再生タイミングに合わせて a∝scale² を掛ける。1 Unity単位=1m 前提。target 必須。</summary>
    private double PeakTipAccelG(Ros2Trajectory tr, double scale, out bool ok)
    {
        ok = false;
        if (target == null || tr == null || tr.timesSec == null || tr.positions == null || tr.timesSec.Length < 3)
        {
            return 0d;
        }
        // 全点を jointNames 順へ写像（プレビュー線と違い間引きしない＝加速度の精度確保）。
        accelJ16Buf.Clear();
        var names = tr.jointNames;
        for (int p = 0; p < tr.positions.Length; p++)
        {
            var src = tr.positions[p];
            var w = new double[jointNames.Length];
            if (src != null)
            {
                for (int k = 0; k < jointNames.Length; k++)
                {
                    int idx = k;
                    if (names != null && names.Length > 0)
                    {
                        idx = Array.IndexOf(names, jointNames[k]);
                        if (idx < 0) { idx = k; }
                    }
                    w[k] = (idx >= 0 && idx < src.Length) ? src[idx] : 0d;
                }
            }
            accelJ16Buf.Add(w);
        }
        accelTipBuf.Clear();
        target.SampleTipWorld(accelJ16Buf, accelTipBuf);
        if (accelTipBuf.Count != tr.timesSec.Length || accelTipBuf.Count < 3)
        {
            return 0d;
        }
        double scaleAcc = scale * scale;
        double u = (obstacles != null && obstacles.UnitScale > 0f) ? obstacles.UnitScale : 1.0d;   // Unity単位→m
        double peak = 0d;
        int last = tr.timesSec.Length - 1;
        for (int p = 1; p < last; p++)
        {
            double dtA = tr.timesSec[p] - tr.timesSec[p - 1];
            double dtB = tr.timesSec[p + 1] - tr.timesSec[p];
            if (dtA <= 1e-6 || dtB <= 1e-6)
            {
                continue;
            }
            Vector3 vA = (accelTipBuf[p] - accelTipBuf[p - 1]) / (float)dtA;
            Vector3 vB = (accelTipBuf[p + 1] - accelTipBuf[p]) / (float)dtB;
            Vector3 acc = (vB - vA) / (float)((dtA + dtB) * 0.5d);
            double m = acc.magnitude * scaleAcc * u;   // Unity単位/s² → m/s²
            if (m > peak) { peak = m; }
        }
        ok = true;
        return peak / 9.80665d;   // m/s² → G
    }

    private bool ReadTagOn(SeqEntry s, string tag)
    {
        if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(s.db) || string.IsNullOrEmpty(s.mech))
        {
            return false;
        }
        return GlobalScript.GetTagData(s.db, s.mech, tag) >= 1;
    }

    private void WriteTag(SeqEntry s, string tag, int value)
    {
        if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(s.db) || string.IsNullOrEmpty(s.mech))
        {
            return;
        }
        var info = GlobalScript.GetTagInfo(s.db, s.mech, tag);
        if (info != null)
        {
            GlobalScript.SetTagData(info, value);
        }
    }

    private static double[] ToDoubleArray(List<float> l)
    {
        if (l == null)
        {
            return null;
        }
        var d = new double[l.Count];
        for (int i = 0; i < l.Count; i++)
        {
            d[i] = l[i];
        }
        return d;
    }

    private static float[] ToFloat(double[] d)
    {
        if (d == null)
        {
            return null;
        }
        var f = new float[d.Length];
        for (int i = 0; i < d.Length; i++)
        {
            f[i] = (float)d[i];
        }
        return f;
    }

    private static List<float> PrevPoseDeg(IReadOnlyList<Parameters.Ros2RobotStep> steps, int index)
    {
        if (steps == null || steps.Count == 0)
        {
            return null;
        }
        int prev = (index - 1 + steps.Count) % steps.Count;   // 循環：先頭の前は最終step
        return steps[prev] != null ? steps[prev].poseDeg : null;
    }

    private static bool ApproxEqual(List<float> a, double[] b, float tolDeg)
    {
        if (a == null || b == null || a.Count != b.Length)
        {
            return false;
        }
        for (int i = 0; i < b.Length; i++)
        {
            if (Math.Abs(a[i] - b[i]) > tolDeg)
            {
                return false;
            }
        }
        return true;
    }
    #endregion robotSteps シーケンス駆動

    private void SetState(PlanState s, string msg)
    {
        State = s;
        StatusMessage = msg;
        Debug.Log($"[ComRos2PathPlanner] state={s} : {msg}");
        try { StateChanged?.Invoke(s, msg); }
        catch (Exception e) { Debug.LogException(e); }
    }

    private void CreatePreviewLine()
    {
        DestroyStalePreview();   // 再コンパイル/リロードで残った線・ゴーストを先に掃除
        var go = new GameObject(PreviewLineName);
        go.transform.SetParent(transform, false);
        previewLine = go.AddComponent<LineRenderer>();
        previewLine.useWorldSpace = true;
        previewLine.widthMultiplier = previewLineWidth;
        previewLine.numCornerVertices = 2;
        previewLine.numCapVertices = 2;
        previewLine.material = new Material(Shader.Find("Sprites/Default"));
        previewLine.startColor = previewLine.endColor = previewLineColor;
        previewLine.positionCount = 0;
    }

    private void HidePreviewLine()
    {
        if (previewLine != null)
        {
            previewLine.positionCount = 0;
        }
    }

    /// <summary>ゴースト(半透明複製)のプレビュー再生を開始（Update でループ）。</summary>
    private void StartGhostPreview()
    {
        var t = EnsureTarget();
        if (t == null)
        {
            return;
        }
        t.CreateGhost();
        previewT = 0d;
        ghostSeek = false;   // 再生開始時は自動ループから
        ghostActive = true;
    }

    /// <summary>ゴースト再生の進捗(0..1)。シークバー表示用。</summary>
    public float GhostPreviewT01
    {
        get
        {
            if (traj == null || traj.timesSec == null || traj.timesSec.Length == 0)
            {
                return 0f;
            }
            double gtotal = traj.timesSec[traj.timesSec.Length - 1];
            return gtotal > 0d ? Mathf.Clamp01((float)(previewT / gtotal)) : 0f;
        }
    }

    /// <summary>ゴースト再生の現在時刻[秒]（軌道タイムライン上・0..総時間）。UIの再生時間表示用。</summary>
    public float GhostTimeSec
    {
        get
        {
            float dur = GhostDurationSec;
            return dur > 0f ? Mathf.Clamp((float)previewT, 0f, dur) : 0f;
        }
    }
    /// <summary>ゴースト軌道の総時間[秒]（＝最終点の time_from_start＝動作時間 achieved）。</summary>
    public float GhostDurationSec => (traj != null && traj.timesSec != null && traj.timesSec.Length > 0)
        ? (float)traj.timesSec[traj.timesSec.Length - 1] : 0f;


    /// <summary>ゴースト再生中か（シークバーの表示可否に使う）。</summary>
    public bool GhostActive => ghostActive;

    /// <summary>シークバーからゴーストを 0..1 の位置へ手動スクラブする（自動送りは停止）。</summary>
    public void SetGhostSeek(float t01)
    {
        if (!ghostActive || traj == null || traj.timesSec == null || traj.timesSec.Length == 0 || target == null)
        {
            return;
        }
        ghostSeek = true;   // シーク＝一時停止（自動送り停止）
        double gtotal = traj.timesSec[traj.timesSec.Length - 1];
        previewT = Mathf.Clamp01(t01) * gtotal;
        target.PoseGhostDeg(SamplePoseJ16(previewT));
    }

    /// <summary>ゴースト再生中か（再生ボタンのラベル判定用）。true=自動送り中。</summary>
    public bool GhostPlaying => ghostActive && !ghostSeek;

    /// <summary>ゴースト再生の 再生/一時停止。再生=現在位置から自動送り再開／一時停止=現在位置で保持。</summary>
    public void SetGhostPlaying(bool play)
    {
        if (!ghostActive)
        {
            return;
        }
        ghostSeek = !play;   // 再生=自動送り(false) / 一時停止=保持(true)
    }

    /// <summary>ゴーストを消してプレビュー再生を止める。</summary>
    private void StopGhostPreview()
    {
        ghostActive = false;
        ghostSeek = false;
        if (target != null)
        {
            target.DestroyGhost();
        }
    }

    /// <summary>軌道の時刻 t(秒) での姿勢を J1..J6(度) で返す（区間線形補間＋J1..J6並べ替え）。</summary>
    private double[] SamplePoseJ16(double t)
    {
        var res = new double[jointNames.Length];
        if (traj == null || traj.positions == null || traj.positions.Length == 0)
        {
            return res;
        }
        var times = traj.timesSec;
        int last = times.Length - 1;
        double[] pos;
        if (t <= times[0] || last == 0)
        {
            pos = traj.positions[0];
        }
        else if (t >= times[last])
        {
            pos = traj.positions[last];
        }
        else
        {
            int i = 0;
            while (i < last && times[i + 1] < t) { i++; }
            double t0 = times[i], t1 = times[i + 1];
            double a = (t1 > t0) ? (t - t0) / (t1 - t0) : 0d;
            a = a < 0d ? 0d : (a > 1d ? 1d : a);
            double[] p0 = traj.positions[i], p1 = traj.positions[i + 1];
            pos = new double[p0.Length];
            for (int k = 0; k < p0.Length; k++) { pos[k] = p0[k] + (p1[k] - p0[k]) * a; }
        }
        // J1..J6 へ並べ替え（軌道の関節名順が違う場合に対応）。
        var names = traj.jointNames;
        for (int k = 0; k < jointNames.Length; k++)
        {
            int idx = k;
            if (names != null && names.Length > 0)
            {
                idx = Array.IndexOf(names, jointNames[k]);
                if (idx < 0) { idx = k; }
            }
            res[k] = (idx >= 0 && idx < pos.Length) ? pos[idx] : 0d;
        }
        return res;
    }

    /// <summary>受信軌道の先端(ツール/フランジ)の通り道を LineRenderer で描く（ロボは動かさない）。</summary>
    private void BuildPreviewLine(Ros2Trajectory t)
    {
        if (previewLine == null)
        {
            return;
        }
        var tgt = EnsureTarget();
        if (tgt == null)
        {
            Debug.LogWarning("[ComRos2PathPlanner] 計画対象ロボットが見つからず、先端軌跡プレビューを描けません。");
            return;
        }
        tgt.SampleTipWorld(BuildJ16Waypoints(t), tipBuf);
        previewLine.positionCount = tipBuf.Count;
        previewLine.SetPositions(tipBuf.ToArray());
    }

    /// <summary>軌道を J1..J6 順の関節角(度)列にそろえる（FK サンプル用。過密なら間引く）。</summary>
    private List<double[]> BuildJ16Waypoints(Ros2Trajectory t)
    {
        var names = t.jointNames;
        double[] ToJ16(double[] src)
        {
            var w = new double[jointNames.Length];
            for (int k = 0; k < jointNames.Length; k++)
            {
                int idx = k;
                if (names != null && names.Length > 0)
                {
                    idx = Array.IndexOf(names, jointNames[k]);
                    if (idx < 0) { idx = k; }
                }
                w[k] = (idx >= 0 && idx < src.Length) ? src[idx] : 0d;
            }
            return w;
        }
        var res = new List<double[]>();
        int n = t.positions.Length;
        int stride = Mathf.Max(1, n / 80);   // 線が過密/重くならないよう最大~80点
        for (int p = 0; p < n; p += stride)
        {
            if (t.positions[p] != null) { res.Add(ToJ16(t.positions[p])); }
        }
        if (n > 0 && (n - 1) % stride != 0 && t.positions[n - 1] != null)
        {
            res.Add(ToJ16(t.positions[n - 1]));   // 最終点は必ず含める
        }
        return res;
    }
    #endregion 承認 / プレビュー

    #region テスト用
    // 既定でシーン(障害物+ヘッド)を送ってから plan する（sendSceneBeforePlan で切替）。
    [ContextMenu("Test Plan (start→goal)")]
    private void TestPlanStartGoal()
    {
        RequestPlanWithScene(testStartDeg, testGoalDeg);
    }

    [ContextMenu("Test Plan (current→goal)")]
    private void TestPlanCurrentGoal()
    {
        RequestPlanWithScene(ReadCurrentDeg(), testGoalDeg);
    }

    // シーンを送らず plan だけ投げたい時用（従来動作）。
    [ContextMenu("Test Plan (start→goal, plan only)")]
    private void TestPlanStartGoalOnly()
    {
        RequestPlan(testStartDeg, testGoalDeg);
    }
    #endregion テスト用
}
