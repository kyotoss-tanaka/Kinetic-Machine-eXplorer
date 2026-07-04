using System;
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

    private IRos2Transport transport;
    private ComRos2 com;
    private bool started;
    private bool destroyed;

    // 再生状態
    private Ros2Trajectory traj;
    private double playT;
    private bool playing;

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
        if (t == null || t.positions == null || t.positions.Length == 0 || t.timesSec == null)
        {
            Debug.LogWarning("[ComRos2PathPlanner] 空/不正な軌道を受信しました。");
            return;
        }
        traj = t;
        playT = 0d;
        playing = true;
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
        for (int j = 0; j < pos.Length; j++)
        {
            // 受信軌道の関節名を優先。無ければ設定の jointNames[j]（インデックス対応）。
            string name = (traj.jointNames != null && j < traj.jointNames.Length) ? traj.jointNames[j] : null;
            if (string.IsNullOrEmpty(name) || !com.ApplyValue(name, pos[j]))
            {
                if (j < jointNames.Length)
                {
                    com.ApplyValue(jointNames[j], pos[j]);
                }
            }
        }
    }
    #endregion 受信 / 再生

    #region テスト用
    [ContextMenu("Test Plan (start→goal)")]
    private void TestPlanStartGoal()
    {
        RequestPlan(testStartDeg, testGoalDeg);
    }

    [ContextMenu("Test Plan (current→goal)")]
    private void TestPlanCurrentGoal()
    {
        RequestPlanFromCurrent(testGoalDeg);
    }
    #endregion テスト用
}
