#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
KMX ⇄ ROS2 統合起動。3プロセス（endpoint / move_group / kmx_planner）を1コマンドで。

  ros2 launch kmx_planner kmx_bringup.launch.py                     # MoveItまで全部（move_group込み）
  ros2 launch kmx_planner kmx_bringup.launch.py use_moveit:=false   # 軽量: endpoint + planner(補間モード)のみ

引数:
  use_moveit (true)      : true=MoveIt(move_group起動) / false=補間モード（move_group/RVizを起動しない）
  robot_model (crx30ia)  : fanuc_moveit_config の対象モデル
  ros_ip (0.0.0.0)       : ros_tcp_endpoint の待受IP
  planning_group (manipulator) : MoveIt planning group 名
  use_dcs_reader (true)  : true=DCS安全ゾーン読取りノード(kmx_dcs_reader)を起動（use_moveit と独立）
  dcs_host (127.0.0.1)   : Karel 常駐ソケットの接続先（ROBOGUIDE/実機コントローラ IP）
  dcs_port (60011)       : Karel 常駐ソケットの待受ポート
  use_mock (true)        : true=模擬HW / false=実機・ROBOGUIDE(Stream Motion で robot_ip へ接続)
  robot_ip (192.168.1.100): use_mock=false 時の接続先IP

前提: 端末で colcon_ws(endpoint) / ros2_ws(kmx_*, fanuc_*) を source 済みであること（.bashrc 推奨）。
"""
from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument, IncludeLaunchDescription
from launch.conditions import IfCondition
from launch.launch_description_sources import PythonLaunchDescriptionSource
from launch.substitutions import LaunchConfiguration, PathJoinSubstitution
from launch_ros.actions import Node
from launch_ros.substitutions import FindPackageShare
from launch_ros.parameter_descriptions import ParameterValue


def generate_launch_description():
    use_moveit = LaunchConfiguration('use_moveit')
    robot_model = LaunchConfiguration('robot_model')
    ros_ip = LaunchConfiguration('ros_ip')
    planning_group = LaunchConfiguration('planning_group')
    use_dcs_reader = LaunchConfiguration('use_dcs_reader')
    dcs_host = LaunchConfiguration('dcs_host')
    dcs_port = LaunchConfiguration('dcs_port')
    dcs_poll_sec = LaunchConfiguration('dcs_poll_sec')
    use_mock = LaunchConfiguration('use_mock')      # true=模擬HW / false=実機・ROBOGUIDE(Stream Motion)
    robot_ip = LaunchConfiguration('robot_ip')      # use_mock=false 時の Stream Motion 接続先IP

    # CRX-30iA の関節名（Unity /kmx と同名）。別ロボに替えるならここを書き換え。
    moveit_joint_names = ['J1', 'J2', 'J3', 'J4', 'J5', 'J6']

    args = [
        DeclareLaunchArgument('use_moveit', default_value='true',
                              description='true=MoveIt(move_group込) / false=補間モード(endpoint+plannerのみ)'),
        DeclareLaunchArgument('robot_model', default_value='crx30ia',
                              description='fanuc_moveit_config の robot_model'),
        DeclareLaunchArgument('ros_ip', default_value='0.0.0.0',
                              description='ros_tcp_endpoint の待受IP'),
        DeclareLaunchArgument('planning_group', default_value='manipulator',
                              description='MoveIt planning group 名'),
        DeclareLaunchArgument('use_mock', default_value='true',
                              description='true=模擬HW / false=実機・ROBOGUIDE(Stream Motion で robot_ip へ接続)'),
        DeclareLaunchArgument('robot_ip', default_value='192.168.1.100',
                              description='use_mock=false 時の Stream Motion 接続先IP'),
        DeclareLaunchArgument('use_dcs_reader', default_value='true',
                              description='true=DCS安全ゾーン読取りノードを起動（use_moveit と独立）'),
        DeclareLaunchArgument('dcs_host', default_value='auto',
                              description='Karel 接続先。auto=WSLのdefault gw(=Windowsホスト,NATでROBOGUIDE同一PC向け)/実機はrobot IP'),
        DeclareLaunchArgument('dcs_port', default_value='60011',
                              description='Karel 常駐ソケットの待受ポート'),
        DeclareLaunchArgument('dcs_poll_sec', default_value='2.0',
                              description='DCS 再読込周期[s]。>0=topic 定期再配信(Unity 自動更新・R2)/0=起動時+service のみ'),
    ]

    # ① Unity 橋渡し（TCP endpoint）
    endpoint = Node(
        package='ros_tcp_endpoint',
        executable='default_server_endpoint',
        name='UnityEndpoint',
        parameters=[{'ROS_IP': ros_ip, 'ROS_TCP_PORT': 10000}],
        output='screen',
    )

    # ② MoveIt move_group（use_moveit:=true のときだけ。RVizも付随して開く）
    move_group = IncludeLaunchDescription(
        PythonLaunchDescriptionSource(PathJoinSubstitution([
            FindPackageShare('fanuc_moveit_config'), 'launch', 'fanuc_moveit.launch.py'
        ])),
        launch_arguments={'robot_model': robot_model, 'use_mock': use_mock, 'robot_ip': robot_ip}.items(),
        condition=IfCondition(use_moveit),
    )

    # ③ 経路生成ノード（use_moveit を bool へコアース）
    planner = Node(
        package='kmx_planner',
        executable='kmx_planner',
        name='kmx_planner',
        parameters=[{
            'use_moveit': ParameterValue(use_moveit, value_type=bool),
            'planning_group': planning_group,
            'moveit_joint_names': moveit_joint_names,
        }],
        output='screen',
    )

    # ④ DCS 安全ゾーン読取り（Karel 常駐ソケット→ /kmx/safety_zones ＋ /kmx/get_safety_zones）
    #    use_moveit とは独立（use_dcs_reader:=false で無効化可）。
    dcs_reader = Node(
        package='kmx_planner',
        executable='kmx_dcs_reader',
        name='kmx_dcs_reader',
        parameters=[{
            'dcs_host': dcs_host,
            'dcs_port': ParameterValue(dcs_port, value_type=int),
            'poll_sec': ParameterValue(dcs_poll_sec, value_type=float),
        }],
        output='screen',
        condition=IfCondition(use_dcs_reader),
    )

    return LaunchDescription(args + [endpoint, move_group, planner, dcs_reader])
