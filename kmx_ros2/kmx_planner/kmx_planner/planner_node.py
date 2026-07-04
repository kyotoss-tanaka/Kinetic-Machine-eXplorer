#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
KMX 経路生成ノード。

役割:
  - /kmx/plan_request (kmx_msgs/PlanRequest, 度) を購読
  - 始点→終点の関節空間経路を生成
      * use_moveit:=false … 関節空間の線形補間（MoveIt不要。まず往復検証用）
      * use_moveit:=true  … MoveIt の move_group に MoveGroup アクション(plan_only)で計画依頼
  - trajectory_msgs/JointTrajectory (度) を /kmx/trajectory へ発行

Unity 側（ComRos2PathPlanner）は /kmx/trajectory を時間補間しながら d_robo_a1..a6 に再生する。
単位は /kmx/* 全体で「度」。MoveIt へは deg->rad、結果は rad->deg に変換する。

MoveIt モードの前提:
  別プロセスで move_group を起動しておくこと（例: fanuc チュートリアル config）。
    ros2 launch moveit_resources_fanuc_moveit_config demo.launch.py
  planning_group / moveit_joint_names は使う config に合わせる（既定は fanuc: manipulator / joint_1..6）。
  Unity 側の関節名 J1..J6 と MoveIt 側 joint_1..6 は「インデックス対応」で紐づける。
"""
import math

import rclpy
from rclpy.node import Node
from rclpy.action import ActionClient

from builtin_interfaces.msg import Duration
from trajectory_msgs.msg import JointTrajectory, JointTrajectoryPoint
from sensor_msgs.msg import JointState
from moveit_msgs.action import MoveGroup
from moveit_msgs.msg import MotionPlanRequest, Constraints, JointConstraint, RobotState

from kmx_msgs.msg import PlanRequest


class KmxPlannerNode(Node):
    def __init__(self):
        super().__init__('kmx_planner')

        self.declare_parameter('use_moveit', False)
        self.declare_parameter('planning_group', 'manipulator')
        # Unity 側の関節名（/kmx 上の名前）。start/goal・出力軌道の並び順。
        self.declare_parameter('joint_names', ['J1', 'J2', 'J3', 'J4', 'J5', 'J6'])
        # MoveIt(URDF) 側の関節名。joint_names とインデックス対応させる。
        self.declare_parameter('moveit_joint_names',
                               ['joint_1', 'joint_2', 'joint_3', 'joint_4', 'joint_5', 'joint_6'])
        self.declare_parameter('request_topic', '/kmx/plan_request')
        self.declare_parameter('trajectory_topic', '/kmx/trajectory')
        self.declare_parameter('move_action', '/move_action')
        self.declare_parameter('allowed_planning_time', 5.0)
        self.declare_parameter('vel_scale', 0.1)
        self.declare_parameter('acc_scale', 0.1)
        # 補間モード用
        self.declare_parameter('duration_sec', 3.0)
        self.declare_parameter('num_points', 30)

        self.use_moveit = bool(self.get_parameter('use_moveit').value)
        self.kmx_joints = list(self.get_parameter('joint_names').value)
        self.moveit_joints = list(self.get_parameter('moveit_joint_names').value)
        req_topic = self.get_parameter('request_topic').value
        traj_topic = self.get_parameter('trajectory_topic').value

        self.sub = self.create_subscription(PlanRequest, req_topic, self.on_request, 10)
        self.pub = self.create_publisher(JointTrajectory, traj_topic, 10)

        self._ac = None
        self._pending_out_names = []
        self._pending_moveit_names = []
        if self.use_moveit:
            self._ac = ActionClient(self, MoveGroup, self.get_parameter('move_action').value)

        self.get_logger().info(
            f"kmx_planner ready: sub='{req_topic}' pub='{traj_topic}' use_moveit={self.use_moveit}")

    # ---------------------------------------------------------------- 要求受信
    def on_request(self, msg: PlanRequest):
        names = list(msg.names) if msg.names else list(self.kmx_joints)
        start = list(msg.start)
        goal = list(msg.goal)
        self.get_logger().info(f"plan request: start={start} goal={goal} (deg), joints={names}")

        if len(start) != len(names) or len(goal) != len(names):
            self.get_logger().error("names / start / goal の長さが一致しません。")
            return

        if self.use_moveit:
            self.plan_with_moveit(names, start, goal)   # 非同期。完了時に発行。
        else:
            traj = self.plan_interpolate(names, start, goal)
            self.pub.publish(traj)
            self.get_logger().info(f"published trajectory: {len(traj.points)} points (interpolate)")

    # -------------------------------------------------- 補間モード（MoveIt不要）
    def plan_interpolate(self, names, start_deg, goal_deg):
        """関節空間の線形補間（度のまま）。smoothstep で加減速っぽく。障害物回避なし。"""
        n = max(1, int(self.get_parameter('num_points').value))
        dur = float(self.get_parameter('duration_sec').value)

        traj = JointTrajectory()
        traj.joint_names = names
        for k in range(n + 1):
            a = k / n
            s = a * a * (3.0 - 2.0 * a)   # smoothstep
            pt = JointTrajectoryPoint()
            pt.positions = [start_deg[j] + (goal_deg[j] - start_deg[j]) * s
                            for j in range(len(names))]
            t = dur * a
            pt.time_from_start = Duration(sec=int(t), nanosec=int(round((t - int(t)) * 1e9)))
            traj.points.append(pt)
        return traj

    # ---------------------------------------------------- MoveIt モード（本命）
    def plan_with_moveit(self, kmx_names, start_deg, goal_deg):
        """move_group に MoveGroup アクション(plan_only)で joint 目標を投げる（非同期）。"""
        if self._ac is None:
            self.get_logger().error("ActionClient 未初期化。")
            return
        if not self._ac.wait_for_server(timeout_sec=3.0):
            self.get_logger().error(
                f"move_group アクション '{self.get_parameter('move_action').value}' に接続できません。"
                "move_group を起動していますか？"
                "（例: ros2 launch moveit_resources_fanuc_moveit_config demo.launch.py）")
            return

        # J1..J6(度) → moveit joint(rad)。インデックス対応。
        n = min(len(kmx_names), len(self.moveit_joints))
        mj = self.moveit_joints[:n]
        start_rad = [math.radians(v) for v in start_deg[:n]]
        goal_rad = [math.radians(v) for v in goal_deg[:n]]

        req = MotionPlanRequest()
        req.group_name = self.get_parameter('planning_group').value
        req.num_planning_attempts = 5
        req.allowed_planning_time = float(self.get_parameter('allowed_planning_time').value)
        req.max_velocity_scaling_factor = float(self.get_parameter('vel_scale').value)
        req.max_acceleration_scaling_factor = float(self.get_parameter('acc_scale').value)

        # 始点（絶対指定）
        start_state = RobotState()
        js = JointState()
        js.name = mj
        js.position = start_rad
        start_state.joint_state = js
        start_state.is_diff = False
        req.start_state = start_state

        # 終点（各 joint の関節制約）
        c = Constraints()
        for name, pos in zip(mj, goal_rad):
            jc = JointConstraint()
            jc.joint_name = name
            jc.position = pos
            jc.tolerance_above = 0.001
            jc.tolerance_below = 0.001
            jc.weight = 1.0
            c.joint_constraints.append(jc)
        req.goal_constraints.append(c)

        goal = MoveGroup.Goal()
        goal.request = req
        goal.planning_options.plan_only = True   # 計画のみ（実行しない。Unityが再生する）

        # 出力を J1..J6 順へ戻すための対応を保持
        self._pending_out_names = list(kmx_names[:n])
        self._pending_moveit_names = list(mj)

        self.get_logger().info("move_group へ plan_only 要求を送信…")
        self._ac.send_goal_async(goal).add_done_callback(self._on_goal_response)

    def _on_goal_response(self, future):
        goal_handle = future.result()
        if not goal_handle.accepted:
            self.get_logger().error("move_group がゴールを拒否しました。")
            return
        goal_handle.get_result_async().add_done_callback(self._on_result)

    def _on_result(self, future):
        result = future.result().result
        # moveit_msgs/MoveItErrorCodes.SUCCESS == 1
        if result.error_code.val != 1:
            self.get_logger().error(f"MoveIt 計画失敗: error_code={result.error_code.val}")
            return
        jt_rad = result.planned_trajectory.joint_trajectory   # rad, URDF順
        traj = self._convert_result(jt_rad, self._pending_out_names, self._pending_moveit_names)
        self.pub.publish(traj)
        self.get_logger().info(f"published trajectory: {len(traj.points)} points (moveit)")

    def _convert_result(self, jt_rad: JointTrajectory, out_names, moveit_names) -> JointTrajectory:
        """MoveIt の JointTrajectory(rad) を、度＋out_names(J1..J6)順 に変換する。"""
        idx = {nm: i for i, nm in enumerate(jt_rad.joint_names)}
        out = JointTrajectory()
        out.joint_names = list(out_names)
        for p in jt_rad.points:
            q = JointTrajectoryPoint()
            q.positions = [
                math.degrees(p.positions[idx[mn]]) if (mn in idx and idx[mn] < len(p.positions)) else 0.0
                for mn in moveit_names
            ]
            q.time_from_start = p.time_from_start
            out.points.append(q)
        return out


def main(args=None):
    rclpy.init(args=args)
    node = KmxPlannerNode()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.destroy_node()
        rclpy.shutdown()


if __name__ == '__main__':
    main()
