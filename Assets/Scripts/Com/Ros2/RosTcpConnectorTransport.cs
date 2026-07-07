// ROS-TCP-Connector を使ったトランスポート実装。
//
// 有効化手順：
//   1) Unity Package Manager で ROS-TCP-Connector を導入（git URL）
//        https://github.com/Unity-Technologies/ROS-TCP-Connector.git?path=/com.unity.robotics.ros-tcp-connector
//   2) ROS2 側で ros_tcp_endpoint を起動（kmx_msgs を source した状態で）
//        ros2 run ros_tcp_endpoint default_server_endpoint --ros-args -p ROS_IP:=0.0.0.0
//   3) Unity: Robotics > Generate ROS Messages で kmx_msgs/TagArray を生成
//        → RosMessageTypes.Kmx.TagArrayMsg（stamp / names / values）
//   4) Robotics > ROS Settings で Protocol = ROS2 に設定
//   5) Player Settings > Scripting Define Symbols に "KMX_ROS2" を追加
//
// この#if内は KMX_ROS2 定義時のみコンパイルされる（未定義なら丸ごと無効＝プロジェクトは常にビルド可）。
#if KMX_ROS2
using System;
using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;   // Unity→ROS(FLU) 変換
using RosMessageTypes.Kmx;            // 生成した kmx_msgs/TagArray, PlanRequest, Obstacles
using RosMessageTypes.Trajectory;     // 標準 trajectory_msgs/JointTrajectory
using RosMessageTypes.Geometry;       // geometry_msgs/Pose
using RosMessageTypes.Std;            // std_msgs/String（計画ステータス）

/// <summary>ROS-TCP-Connector 経由のトランスポート（kmx_msgs/TagArray で名前＋値を送受信）。</summary>
public sealed class RosTcpConnectorTransport : IRos2Transport
{
    private ROSConnection ros;
    private bool publisherRegistered;
    // このトランスポートが購読したトピック。Disconnect で解除する（常駐シングルトン ROSConnection に
    // コールバックが残留し、リロード毎に多重購読＋破棄済みインスタンスへの配送が起きるのを防ぐ）。
    private readonly List<string> subscribedTopics = new();

    public bool IsConnected => ros != null;
    // 実接続状態：接続スレッドが生きていてエラーが無い（ROS-TCP-Connector の HUD と同じ判定）。
    public bool IsLinkUp => ros != null && ros.HasConnectionThread && !ros.HasConnectionError;

    public void Connect(string ip, int port)
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RosIPAddress = ip;
        ros.RosPort = port;
        ros.Connect();
    }

    public void Disconnect()
    {
        // ROSConnection は常駐シングルトン。購読を明示解除しないとコールバックが残り続ける。
        if (ros != null)
        {
            foreach (var topic in subscribedTopics)
            {
                try { ros.Unsubscribe(topic); } catch { /* ignore */ }
            }
        }
        subscribedTopics.Clear();
    }

    public void RegisterPublisher(string topic)
    {
        if (ros == null)
        {
            ros = ROSConnection.GetOrCreateInstance();
        }
        ros.RegisterPublisher<TagArrayMsg>(topic);
        publisherRegistered = true;
    }

    public void Publish(string topic, string[] names, double[] values)
    {
        if (ros == null)
        {
            ros = ROSConnection.GetOrCreateInstance();
        }
        if (!publisherRegistered)
        {
            RegisterPublisher(topic);
        }
        ros.Publish(topic, new TagArrayMsg { names = names, values = values });
    }

    public void Subscribe(string topic, Action<string[], double[]> onMessage)
    {
        // ※ComRos2 は Connect より先に Subscribe を呼ぶため、ここでもインスタンスを確保する。
        //   ROS-TCP-Connector のコールバックはメインスレッドで呼ばれる。
        if (ros == null)
        {
            ros = ROSConnection.GetOrCreateInstance();
        }
        ros.Subscribe<TagArrayMsg>(topic, m => onMessage(m.names, m.values));
        subscribedTopics.Add(topic);
    }

    private bool planReqRegistered;

    public void RegisterPlanRequestPublisher(string topic)
    {
        if (ros == null)
        {
            ros = ROSConnection.GetOrCreateInstance();
        }
        ros.RegisterPublisher<PlanRequestMsg>(topic);
        planReqRegistered = true;
    }

    public void PublishPlanRequest(string topic, string[] names, double[] startDeg, double[] goalDeg,
                                   double timeBudget = 0.0, double goodRatio = 0.0, string robotId = "")
    {
        if (ros == null)
        {
            ros = ROSConnection.GetOrCreateInstance();
        }
        if (!planReqRegistered)
        {
            RegisterPlanRequestPublisher(topic);   // 事前登録漏れ時のフォールバック（レースの可能性あり）
        }
        // time_budget/good_ratio は 0以下 なら ROS2 ノード既定にフォールバック（後方互換）。
        // ★robotId: PlanRequest.msg に robot_id を足して Generate ROS Messages 再生成したら
        //   下の初期化子に `robot_id = robotId,` を追加する（Phase3）。現状は受け取るだけ（後方互換）。
        ros.Publish(topic, new PlanRequestMsg
        {
            names = names, start = startDeg, goal = goalDeg,
            time_budget = timeBudget, good_ratio = goodRatio,
        });
    }

    public void SubscribeTrajectory(string topic, Action<Ros2Trajectory> onTrajectory)
    {
        if (ros == null)
        {
            ros = ROSConnection.GetOrCreateInstance();
        }
        ros.Subscribe<JointTrajectoryMsg>(topic, m =>
        {
            int pointCount = m.points != null ? m.points.Length : 0;
            var traj = new Ros2Trajectory
            {
                jointNames = m.joint_names,
                timesSec = new double[pointCount],
                positions = new double[pointCount][],
            };
            for (int p = 0; p < pointCount; p++)
            {
                var pt = m.points[p];
                // time_from_start は builtin_interfaces/Duration (sec + nanosec)
                traj.timesSec[p] = pt.time_from_start.sec + pt.time_from_start.nanosec * 1e-9;
                traj.positions[p] = pt.positions;   // 度（ノード側で rad→deg 済み）
            }
            onTrajectory(traj);
        });
        subscribedTopics.Add(topic);
    }

    public void SubscribePlanStatus(string topic, Action<string> onStatus)
    {
        if (ros == null)
        {
            ros = ROSConnection.GetOrCreateInstance();
        }
        ros.Subscribe<StringMsg>(topic, m => onStatus(m.data));
        subscribedTopics.Add(topic);
    }

    // ObstaclesMsg を登録済みのトピック集合（障害物 /kmx/obstacles と ヘッド /kmx/attached など複数）。
    // 単一フラグだと2つ目のトピックが未登録のまま publish され "Not registered" になるため集合で管理。
    private readonly HashSet<string> registeredObstacleTopics = new();

    public void RegisterObstaclesPublisher(string topic)
    {
        if (ros == null)
        {
            ros = ROSConnection.GetOrCreateInstance();
        }
        if (registeredObstacleTopics.Add(topic))
        {
            ros.RegisterPublisher<ObstaclesMsg>(topic);
        }
    }

    public void PublishObstacles(string topic, string frameId, List<Ros2Obstacle> obstacles)
    {
        if (ros == null)
        {
            ros = ROSConnection.GetOrCreateInstance();
        }
        RegisterObstaclesPublisher(topic);   // トピック単位で冪等に登録
        var msg = new ObstaclesMsg
        {
            frame_id = frameId,
            items = new ObstaclePrimitiveMsg[obstacles.Count],
        };
        for (int i = 0; i < obstacles.Count; i++)
        {
            var o = obstacles[i];
            var dims = new double[o.dimensions.Length];
            for (int d = 0; d < dims.Length; d++)
            {
                dims[d] = o.dimensions[d];
            }
            msg.items[i] = new ObstaclePrimitiveMsg
            {
                id = o.id,
                type = (byte)o.type,
                dimensions = dims,
                // Unity(左手Y-up) → ROS(FLU, 右手Z-up) へ変換。ROSGeometry のテスト済み変換を使用。
                pose = new PoseMsg
                {
                    position = o.position.To<FLU>(),
                    orientation = o.rotation.To<FLU>(),
                },
            };
        }
        ros.Publish(topic, msg);
    }
}
#endif
