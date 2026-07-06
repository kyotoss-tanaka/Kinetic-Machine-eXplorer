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

    [Header("計画のレビュー（計画中表示／成否／経路プレビュー→OK/Cancel）")]
    [Tooltip("軌道受信後すぐ動かさず、3Dプレビュー表示して OK(ApprovePlan)/Cancel(CancelPlan) を待つ")]
    [SerializeField] private bool requireApproval = true;
    [Tooltip("計画ステータス(std_msgs/String)トピック。計画中/成功/失敗を受ける（ROS2側が publish）")]
    [SerializeField] private string planStatusTopic = "/kmx/plan_status";
    [Tooltip("計画中のまま軌道/失敗通知が来ない場合に失敗とみなす保険の秒数（time_budget より十分大きく）")]
    [SerializeField] private float planTimeoutSec = 20f;
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
    private float planStartTime;                 // Planning に入った時刻（timeout 判定用）
    private LineRenderer previewLine;            // 先端軌跡プレビュー
    private const string PreviewLineName = "Ros2PlanPreviewLine";
    private const string GhostObjectName = "CRX-30iA_Ghost";   // CRX_30iA が生成するゴースト名
    private Kinematics6D kin;                    // FK サンプル用（先端位置）＋ゴースト
    private readonly System.Collections.Generic.List<Vector3> tipBuf = new();
    private double previewT;                     // ゴースト再生の時刻（ループ）
    private bool ghostActive;                    // ゴースト再生中か
    [SerializeField] private float ghostLoopPauseSec = 0.6f;   // ループ末で一瞬止める

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
            if (t != null && t.gameObject.name == GhostObjectName)
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
            if (t != null && t.gameObject.name == GhostObjectName)
            {
                DestroyImmediate(t.gameObject);
            }
        }
    }
#endif

    #region 要求
    /// <summary>始点/終点（度・jointNames と同数）を渡して経路生成を要求する。</summary>
    public void RequestPlan(double[] startDeg, double[] goalDeg)
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
        // 新しい要求。前の再生/プレビュー/ゴーストは破棄して計画中へ。
        playing = false;
        traj = null;
        StopGhostPreview();
        HidePreviewLine();
        transport.PublishPlanRequest(planRequestTopic, jointNames, startDeg, goalDeg, planTimeBudget, planGoodRatio);
        planStartTime = Time.time;
        SetState(PlanState.Planning, "計画中…");
        Debug.Log($"[ComRos2PathPlanner] plan要求 start=[{string.Join(",", startDeg)}] goal=[{string.Join(",", goalDeg)}] "
            + $"time_budget={planTimeBudget} good_ratio={planGoodRatio}");
    }

    /// <summary>現在の関節角を始点にして、終点までの経路生成を要求する。</summary>
    public void RequestPlanFromCurrent(double[] goalDeg)
    {
        RequestPlan(ReadCurrentDeg(), goalDeg);
    }

    /// <summary>
    /// planning scene（障害物＋ヘッド）を先に送ってから経路生成を要求する。
    /// scene 反映は非同期（ROS2側が service で適用）なので、送信→少し待ち→plan の順にする。
    /// sendSceneBeforePlan=false や障害物コンポ非在時は通常の RequestPlan と同じ。
    /// </summary>
    public void RequestPlanWithScene(double[] startDeg, double[] goalDeg)
    {
        if (sendSceneBeforePlan && obstacles != null && isActiveAndEnabled)
        {
            StartCoroutine(SendSceneThenPlan(startDeg, goalDeg));
        }
        else
        {
            RequestPlan(startDeg, goalDeg);
        }
    }

    private IEnumerator SendSceneThenPlan(double[] startDeg, double[] goalDeg)
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
        RequestPlan(startDeg, goalDeg);
    }

    /// <summary>現在の関節角（度）を ComRos2 経由で読む。読めない軸は 0。UI がゴール初期値/start に使う。</summary>
    public double[] ReadCurrentDeg()
    {
        var a = new double[jointNames.Length];
        for (int j = 0; j < jointNames.Length; j++)
        {
            com.TryReadValue(jointNames[j], out a[j]);
        }
        return a;
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
        playT = 0d;
        playing = false;   // すぐ動かさない。承認(OK)まで待つ。
        warnedMappingThisTraj = false;
        double dur = t.timesSec[t.timesSec.Length - 1];
        Debug.Log($"[ComRos2PathPlanner] 軌道受信: {t.positions.Length}点 / {(t.jointNames != null ? t.jointNames.Length : 0)}軸 / 所要 {dur:F2}s");

        BuildPreviewLine(t);   // 先端の軌跡を3D表示
        if (requireApproval)
        {
            SetState(PlanState.Preview, $"成功: {t.positions.Length}点 / {dur:F1}s（OK で実行 / Cancel で破棄）");
            StartGhostPreview();   // 半透明複製が経路をなぞる（ヘッドの当たり確認用）
        }
        else
        {
            // 承認不要モード（従来動作）＝そのまま再生。
            ApprovePlan();
        }
    }

    /// <summary>ROS2 の計画ステータス(std_msgs/String)を受けて状態を更新する。</summary>
    private void OnPlanStatus(string data)
    {
        if (destroyed || string.IsNullOrEmpty(data))
        {
            return;
        }
        // 例: "planning" / "succeeded:74:1.8" / "failed:no_solution"
        if (data.StartsWith("failed", StringComparison.OrdinalIgnoreCase))
        {
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

    private void Update()
    {
        if (!started || destroyed)
        {
            return;
        }
        // 計画中に軌道も失敗通知も来ないまま時間超過 → 失敗扱い（だんまり防止の保険）。
        if (State == PlanState.Planning && Time.time - planStartTime > planTimeoutSec)
        {
            SetState(PlanState.Failed, $"タイムアウト（{planTimeoutSec:F0}s 応答なし）");
        }

        // プレビュー中：ゴースト(半透明複製)を軌道でループ再生（実機モデルは動かさない）。
        if (ghostActive && State == PlanState.Preview && traj != null && kin != null)
        {
            var gt = traj.timesSec;
            double gtotal = gt[gt.Length - 1];
            previewT += Time.deltaTime;
            if (gtotal <= 0d || previewT > gtotal + ghostLoopPauseSec)
            {
                previewT = 0d;   // 先頭へ（末尾で少しポーズしてループ）
            }
            double sampleT = previewT < gtotal ? previewT : gtotal;
            kin.PoseGhostDeg(SamplePoseJ16(sampleT));
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

        playT += Time.deltaTime;

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
        SetState(PlanState.Idle, "キャンセル");
    }

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
        if (kin == null)
        {
            var kins = FindObjectsByType<Kinematics6D>(FindObjectsSortMode.None);
            if (kins != null && kins.Length > 0) { kin = kins[0]; }
        }
        if (kin == null)
        {
            return;
        }
        kin.CreateGhost();
        previewT = 0d;
        ghostActive = true;
    }

    /// <summary>ゴーストを消してプレビュー再生を止める。</summary>
    private void StopGhostPreview()
    {
        ghostActive = false;
        if (kin != null)
        {
            kin.DestroyGhost();
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
        if (kin == null)
        {
            var kins = FindObjectsByType<Kinematics6D>(FindObjectsSortMode.None);
            if (kins != null && kins.Length > 0)
            {
                kin = kins[0];
            }
        }
        if (kin == null)
        {
            Debug.LogWarning("[ComRos2PathPlanner] Kinematics6D が見つからず、先端軌跡プレビューを描けません。");
            return;
        }
        kin.SampleTipWorld(BuildJ16Waypoints(t), tipBuf);
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
