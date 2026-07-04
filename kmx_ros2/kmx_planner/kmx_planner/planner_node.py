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
  planning_group / moveit_joint_names は使う config に合わせる（既定は 実CRX: manipulator / J1..J6）。
  fanucチュートリアル代役を使う時は -p moveit_joint_names:="[joint_1,...,joint_6]" で上書き。
  Unity 側の関節名（J1..J6）と MoveIt 側の関節名は moveit_joint_names の「インデックス対応」で紐づける。
"""
import math

import rclpy
from rclpy.node import Node
from rclpy.action import ActionClient
from rclpy.qos import QoSProfile, ReliabilityPolicy, DurabilityPolicy

from builtin_interfaces.msg import Duration
from trajectory_msgs.msg import JointTrajectory, JointTrajectoryPoint
from sensor_msgs.msg import JointState
from shape_msgs.msg import SolidPrimitive
from moveit_msgs.action import MoveGroup
from moveit_msgs.msg import (MotionPlanRequest, Constraints, JointConstraint, RobotState,
                             PlanningScene, PlanningSceneComponents, CollisionObject)
from moveit_msgs.srv import ApplyPlanningScene, GetPlanningScene

from kmx_msgs.msg import PlanRequest
# Obstacles は kmx_msgs に後から追加したメッセージ。CMakeLists 登録＋ビルドが済むまでは
# import できないことがある。ここで失敗してもノード全体（PlanRequest→trajectory）は動かす。
try:
    from kmx_msgs.msg import Obstacles
    _OBSTACLES_MSG_AVAILABLE = True
except ImportError:
    Obstacles = None
    _OBSTACLES_MSG_AVAILABLE = False


class KmxPlannerNode(Node):
    def __init__(self):
        super().__init__('kmx_planner')

        self.declare_parameter('use_moveit', False)
        self.declare_parameter('planning_group', 'manipulator')
        # Unity 側の関節名（/kmx 上の名前）。start/goal・出力軌道の並び順。
        self.declare_parameter('joint_names', ['J1', 'J2', 'J3', 'J4', 'J5', 'J6'])
        # MoveIt(URDF) 側の関節名。joint_names とインデックス対応させる。
        # 既定は実CRX-30iA(FANUC公式 fanuc_moveit_config)の J1..J6。fanucチュートリアル代役
        # (moveit_resources_fanuc_moveit_config, joint_1..6)を使う時は -p で上書きすること。
        self.declare_parameter('moveit_joint_names',
                               ['J1', 'J2', 'J3', 'J4', 'J5', 'J6'])
        self.declare_parameter('request_topic', '/kmx/plan_request')
        self.declare_parameter('trajectory_topic', '/kmx/trajectory')
        self.declare_parameter('move_action', '/move_action')
        # 障害物 → planning scene 反映用
        self.declare_parameter('obstacles_topic', '/kmx/obstacles')
        self.declare_parameter('apply_scene_service', '/apply_planning_scene')
        self.declare_parameter('get_scene_service', '/get_planning_scene')
        self.declare_parameter('planning_scene_topic', '/planning_scene')
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
        if self.use_moveit:
            self._ac = ActionClient(self, MoveGroup, self.get_parameter('move_action').value)

        # ---- 障害物 → planning scene ----
        # /kmx/obstacles を購読し、CollisionObject 化して move_group の planning scene に反映する。
        # 反映後は plan_only（/kmx/plan_request）が障害物を回避した軌道を返す。
        self._obstacle_ids = set()   # 前回反映した id 集合（消し込み用）
        self._scene_cli = None
        self._get_scene_cli = None
        obs_topic = self.get_parameter('obstacles_topic').value
        if _OBSTACLES_MSG_AVAILABLE:
            self.obs_sub = self.create_subscription(Obstacles, obs_topic, self.on_obstacles, 10)
            # service 優先で反映（確実）。未準備時は /planning_scene への publish で fallback。
            scene_qos = QoSProfile(depth=1,
                                   reliability=ReliabilityPolicy.RELIABLE,
                                   durability=DurabilityPolicy.TRANSIENT_LOCAL)
            self._scene_pub = self.create_publisher(
                PlanningScene, self.get_parameter('planning_scene_topic').value, scene_qos)
            self._scene_cli = self.create_client(
                ApplyPlanningScene, self.get_parameter('apply_scene_service').value)
            # D2: 起動時に既存 planning scene の collision object id を取り込む。プロセス再起動時、
            # 前インスタンスが move_group に残した障害物を初回受信で正しく REMOVE できるようにする
            # （_obstacle_ids が空のままだと消し込み差分が出ず、古い箱が残る）。move_group 未起動なら
            # サービス未準備なので、タイマーで数回リトライしてから諦める（非ブロック）。
            self._get_scene_cli = self.create_client(
                GetPlanningScene, self.get_parameter('get_scene_service').value)
            self._synced_existing = False
            self._sync_tries = 0
            self._sync_timer = self.create_timer(2.0, self._sync_existing_ids)
        else:
            self.get_logger().warn(
                "kmx_msgs/Obstacles 未登録のため障害物連携は無効。"
                "kmx_msgs に Obstacles/ObstaclePrimitive を追加し colcon build してください。")

        self.get_logger().info(
            f"kmx_planner ready: sub='{req_topic}' pub='{traj_topic}' "
            f"obstacles={'on' if _OBSTACLES_MSG_AVAILABLE else 'off'} use_moveit={self.use_moveit}")

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

    # ---------------------------------------------- 障害物受信 → planning scene
    def on_obstacles(self, msg: Obstacles):
        """/kmx/obstacles を CollisionObject 化し、move_group の planning scene に反映する。

        更新規約（静的運用）: 受信のたびに全置換。今回分は id で ADD（同一 id は置換）、
        前回あって今回無い id は REMOVE。frame_id は Unity が送る base_link 相対。
        """
        frame = msg.frame_id if msg.frame_id else 'base_link'
        self.get_logger().info(
            f"obstacles received: {len(msg.items)} items, frame_id='{frame}'")

        scene = PlanningScene()
        scene.is_diff = True

        new_ids = set()
        for item in msg.items:
            new_ids.add(item.id)
            co = CollisionObject()
            co.header.frame_id = frame
            co.id = item.id
            sp = SolidPrimitive()
            sp.type = int(item.type)            # 1=BOX,2=SPHERE,3=CYLINDER
            sp.dimensions = [float(d) for d in item.dimensions]
            co.primitives.append(sp)
            co.primitive_poses.append(item.pose)
            co.operation = CollisionObject.ADD
            scene.world.collision_objects.append(co)

        # 前回あって今回無い障害物は消す
        for old_id in (self._obstacle_ids - new_ids):
            co = CollisionObject()
            co.header.frame_id = frame
            co.id = old_id
            co.operation = CollisionObject.REMOVE
            scene.world.collision_objects.append(co)

        # _obstacle_ids は「反映に成功したら」更新する（失敗時に据え置くことで、
        # 次回の REMOVE 差分を最後に成功した状態基準で正しく計算できる）。
        self._apply_scene(scene, new_ids)

    def _apply_scene(self, scene: PlanningScene, new_ids):
        """planning scene diff を反映。service 優先、未準備なら topic publish で fallback。"""
        if self._scene_cli is not None and self._scene_cli.service_is_ready():
            req = ApplyPlanningScene.Request()
            req.scene = scene
            self._scene_cli.call_async(req).add_done_callback(
                lambda fut: self._on_scene_applied(fut, new_ids))
        else:
            # publish は成否不明のためベストエフォートで更新（service が使えない環境向け）。
            self._scene_pub.publish(scene)
            self._obstacle_ids = new_ids
            self.get_logger().warn(
                "apply_planning_scene 未準備 → /planning_scene へ publish で反映（fallback・成否不明）。"
                "move_group は起動していますか？")

    def _on_scene_applied(self, future, new_ids):
        try:
            res = future.result()
            ok = getattr(res, 'success', True)
            if ok:
                self._obstacle_ids = new_ids   # 成功時のみ確定
            self.get_logger().info(
                f"planning scene 更新: success={ok} active_ids={sorted(self._obstacle_ids)}")
        except Exception as e:   # noqa: BLE001
            self.get_logger().error(
                f"apply_planning_scene 呼び出し失敗: {e}（_obstacle_ids は据え置き）")

    # ------------------------------------------- 起動時 planning scene 同期（D2）
    def _sync_existing_ids(self):
        """起動時に既存 scene の collision object id を _obstacle_ids へ取り込む（1回だけ）。

        move_group 未起動だとサービス未準備なので、準備できるまでタイマーで再試行し、
        上限回数で諦める（単一 executor をブロックしないよう非ブロックで確認・async 発行）。
        """
        if self._synced_existing:
            return
        self._sync_tries += 1
        if self._get_scene_cli is None or not self._get_scene_cli.service_is_ready():
            if self._sync_tries >= 5:
                self.get_logger().warn(
                    "get_planning_scene 未準備のため起動時 scene 同期を諦めます"
                    "（move_group 未起動？）。再起動直後は古い障害物が残る場合があります。")
                self._sync_timer.cancel()
                self._synced_existing = True
            return
        req = GetPlanningScene.Request()
        req.components.components = PlanningSceneComponents.WORLD_OBJECT_NAMES
        self._get_scene_cli.call_async(req).add_done_callback(self._on_scene_fetched)
        self._synced_existing = True   # 発行は1回だけ
        self._sync_timer.cancel()

    def _on_scene_fetched(self, future):
        try:
            res = future.result()
            ids = {co.id for co in res.scene.world.collision_objects}
            # 取得までに Send Obstacles で追加された分を消さないよう、まだ空のときだけ取り込む。
            if not self._obstacle_ids and ids:
                self._obstacle_ids = ids
            self.get_logger().info(
                f"起動時 planning scene 同期: existing ids={sorted(ids)} "
                f"→ active_ids={sorted(self._obstacle_ids)}")
        except Exception as e:   # noqa: BLE001
            self.get_logger().warn(f"GetPlanningScene 失敗（scene 同期スキップ）: {e}")

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
        # 単一 executor をブロックしないよう、可用性は非ブロックで確認する。
        # wait_for_server(3s) はこのコールバック実行中に他のコールバック（障害物受信・別の
        # plan 要求）まで最大3秒止めてしまうため使わない。move_group は endpoint と同時起動され
        # Unity 接続時には準備できている前提。
        if not self._ac.server_is_ready():
            self.get_logger().error(
                f"move_group アクション '{self.get_parameter('move_action').value}' が未準備です。"
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

        # 出力を J1..J6 順へ戻すための対応は「この要求のコールバック」に閉じて持ち回る。
        # インスタンス変数(self._pending_*)だと、並行要求が来たとき後発の要求で上書きされ、
        # 先発の結果を誤ったマッピングで変換してしまう（並行安全化）。
        out_names = list(kmx_names[:n])
        moveit_names = list(mj)

        self.get_logger().info("move_group へ plan_only 要求を送信…")
        self._ac.send_goal_async(goal).add_done_callback(
            lambda fut: self._on_goal_response(fut, out_names, moveit_names))

    def _on_goal_response(self, future, out_names, moveit_names):
        goal_handle = future.result()
        if not goal_handle.accepted:
            self.get_logger().error("move_group がゴールを拒否しました。")
            return
        goal_handle.get_result_async().add_done_callback(
            lambda fut: self._on_result(fut, out_names, moveit_names))

    def _on_result(self, future, out_names, moveit_names):
        result = future.result().result
        # moveit_msgs/MoveItErrorCodes.SUCCESS == 1
        if result.error_code.val != 1:
            self.get_logger().error(f"MoveIt 計画失敗: error_code={result.error_code.val}")
            return
        jt_rad = result.planned_trajectory.joint_trajectory   # rad, URDF順
        traj = self._convert_result(jt_rad, out_names, moveit_names)
        if traj is None:
            return   # 関節名不一致 → 発行しない（全零軌道でロボを飛ばさない）
        self.pub.publish(traj)
        self.get_logger().info(f"published trajectory: {len(traj.points)} points (moveit)")

    def _convert_result(self, jt_rad: JointTrajectory, out_names, moveit_names):
        """MoveIt の JointTrajectory(rad) を、度＋out_names(J1..J6)順 に変換する。
        moveit_names が受信軌道に無い場合は 0埋めせず None を返す（発行中止）。"""
        idx = {nm: i for i, nm in enumerate(jt_rad.joint_names)}
        missing = [mn for mn in moveit_names if mn not in idx]
        if missing:
            self.get_logger().error(
                f"軌道の関節名が一致しません: 未対応={missing} / 受信={list(jt_rad.joint_names)} / "
                f"期待(moveit_joint_names)={list(moveit_names)}。"
                "moveit_joint_names パラメータを config に合わせてください。発行を中止します。")
            return None
        out = JointTrajectory()
        out.joint_names = list(out_names)
        for p in jt_rad.points:
            q = JointTrajectoryPoint()
            q.positions = [math.degrees(p.positions[idx[mn]]) for mn in moveit_names]
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
