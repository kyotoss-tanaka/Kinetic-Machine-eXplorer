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

/// <summary>ROS-TCP-Connector 経由のトランスポート（kmx_msgs/TagArray で名前＋値を送受信）。</summary>
public sealed class RosTcpConnectorTransport : IRos2Transport
{
    private ROSConnection ros;
    private bool publisherRegistered;

    public bool IsConnected => ros != null;

    public void Connect(string ip, int port)
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RosIPAddress = ip;
        ros.RosPort = port;
        ros.Connect();
    }

    public void Disconnect()
    {
        // ROSConnection は常駐インスタンス。必要なら購読解除やスレッド停止をここで。
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

    public void PublishPlanRequest(string topic, string[] names, double[] startDeg, double[] goalDeg)
    {
        if (ros == null)
        {
            ros = ROSConnection.GetOrCreateInstance();
        }
        if (!planReqRegistered)
        {
            RegisterPlanRequestPublisher(topic);   // 事前登録漏れ時のフォールバック（レースの可能性あり）
        }
        ros.Publish(topic, new PlanRequestMsg { names = names, start = startDeg, goal = goalDeg });
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
    }

#if KMX_ROS2_OBSTACLES
    private bool obstaclesRegistered;
#endif

    public void RegisterObstaclesPublisher(string topic)
    {
#if KMX_ROS2_OBSTACLES
        if (ros == null)
        {
            ros = ROSConnection.GetOrCreateInstance();
        }
        ros.RegisterPublisher<ObstaclesMsg>(topic);
        obstaclesRegistered = true;
#endif
    }

    public void PublishObstacles(string topic, string frameId, List<Ros2Obstacle> obstacles)
    {
#if KMX_ROS2_OBSTACLES
        if (ros == null)
        {
            ros = ROSConnection.GetOrCreateInstance();
        }
        if (!obstaclesRegistered)
        {
            RegisterObstaclesPublisher(topic);
        }
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
#else
        // kmx_msgs/Obstacles 未生成のため無効。ROS2側でメッセージ追加→Unityで再生成→
        // Scripting Define(Standalone) に KMX_ROS2_OBSTACLES を追加すると有効化される。
#endif
    }
}
#endif
