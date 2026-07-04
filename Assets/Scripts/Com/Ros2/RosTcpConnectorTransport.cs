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
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Kmx;            // 生成した kmx_msgs/TagArray, kmx_msgs/PlanRequest
using RosMessageTypes.Trajectory;     // 標準 trajectory_msgs/JointTrajectory

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
}
#endif
