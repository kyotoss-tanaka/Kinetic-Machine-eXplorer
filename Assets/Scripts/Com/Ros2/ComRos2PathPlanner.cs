using System;
using System.Collections;
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
        // plan要求 publisher を起動時に事前登録（初回 Test Plan で "Not registered" レースを避ける）。
        transport.RegisterPlanRequestPublisher(planRequestTopic);
        started = true;
        Debug.Log($"[ComRos2PathPlanner] start req='{planRequestTopic}' traj='{trajectoryTopic}' joints={jointNames.Length} transport={transport.GetType().Name}");
#endif
    }

    private void OnDestroy()
    {
        destroyed = true;
        // /kmx/trajectory の購読を解除（常駐 ROSConnection にコールバックが残らないよう）。
        try { transport?.Disconnect(); } catch { /* ignore */ }
    }

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
        transport.PublishPlanRequest(planRequestTopic, jointNames, startDeg, goalDeg);
        Debug.Log($"[ComRos2PathPlanner] plan要求 start=[{string.Join(",", startDeg)}] goal=[{string.Join(",", goalDeg)}]");
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

    /// <summary>現在の関節角（度）を ComRos2 経由で読む。読めない軸は 0。</summary>
    private double[] ReadCurrentDeg()
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
        playing = true;
        warnedMappingThisTraj = false;
        double dur = t.timesSec[t.timesSec.Length - 1];
        Debug.Log($"[ComRos2PathPlanner] 軌道受信: {t.positions.Length}点 / {(t.jointNames != null ? t.jointNames.Length : 0)}軸 / 所要 {dur:F2}s");
    }

    private void Update()
    {
        if (!started || !playing || destroyed || traj == null)
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
                playing = false;   // 最終姿勢で停止
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
