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
        launch_arguments={'robot_model': robot_model, 'use_mock': 'true'}.items(),
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

    return LaunchDescription(args + [endpoint, move_group, planner])
