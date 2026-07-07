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
import random
import re
import threading

import rclpy
from rclpy.node import Node
from rclpy.action import ActionClient
from rclpy.qos import QoSProfile, ReliabilityPolicy, DurabilityPolicy
from rclpy.callback_groups import MutuallyExclusiveCallbackGroup
from rclpy.executors import MultiThreadedExecutor

from builtin_interfaces.msg import Duration
from std_msgs.msg import String
from trajectory_msgs.msg import JointTrajectory, JointTrajectoryPoint
from sensor_msgs.msg import JointState
from shape_msgs.msg import SolidPrimitive
from geometry_msgs.msg import Pose
from moveit_msgs.action import MoveGroup
from moveit_msgs.msg import (MotionPlanRequest, Constraints, JointConstraint, RobotState,
                             PlanningScene, PlanningSceneComponents, CollisionObject,
                             AttachedCollisionObject)
from moveit_msgs.srv import ApplyPlanningScene, GetPlanningScene, GetStateValidity

from kmx_msgs.msg import PlanRequest
# Obstacles は kmx_msgs に後から追加したメッセージ。CMakeLists 登録＋ビルドが済むまでは
# import できないことがある。ここで失敗してもノード全体（PlanRequest→trajectory）は動かす。
try:
    from kmx_msgs.msg import Obstacles
    _OBSTACLES_MSG_AVAILABLE = True
except ImportError:
    Obstacles = None
    _OBSTACLES_MSG_AVAILABLE = False


# --- クォータニオン小道具（ヘッド向き補正用。外部依存を避け純Python実装） ---
def _quat_from_rpy(roll, pitch, yaw):
    """RPY(rad, ZYX順) → quaternion (x,y,z,w)。"""
    cr, sr = math.cos(roll / 2), math.sin(roll / 2)
    cp, sp = math.cos(pitch / 2), math.sin(pitch / 2)
    cy, sy = math.cos(yaw / 2), math.sin(yaw / 2)
    return (sr * cp * cy - cr * sp * sy,
            cr * sp * cy + sr * cp * sy,
            cr * cp * sy - sr * sp * cy,
            cr * cp * cy + sr * sp * sy)


def _quat_mul(a, b):
    """quaternion 積 a⊗b（各 (x,y,z,w)）。"""
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


def _quat_rotate_vec(q, v):
    """ベクトル v=(x,y,z) を quaternion q=(x,y,z,w) で回転。"""
    x, y, z, w = q
    vx, vy, vz = v
    tx = 2.0 * (y * vz - z * vy)
    ty = 2.0 * (z * vx - x * vz)
    tz = 2.0 * (x * vy - y * vx)
    return (vx + w * tx + (y * tz - z * ty),
            vy + w * ty + (z * tx - x * tz),
            vz + w * tz + (x * ty - y * tx))


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
        # ヘッド(ツール) → AttachedCollisionObject 反映用（方式B）
        self.declare_parameter('attached_topic', '/kmx/attached')
        # 計画ステータス通知トピック（ROS2→Unity）。planning / succeeded:.. / failed:.. を流す。
        self.declare_parameter('plan_status_topic', '/kmx/plan_status')
        # attach 先リンク（msg.frame_id 未指定時の既定）。CRX-30iA は manipulator の tip_link=flange。
        self.declare_parameter('attach_link', 'flange')
        # ツールが接触して当然のリンク（self-collision 許可）。無いと即自己衝突で計画不能。
        # J4_link: attached_merge_aabb の union 箱はヘッド後方（手首側）に膨らみ J4 と常時接触する
        # ため許可（実ヘッドは J4 に当たらない設計。J3 以下との干渉は引き続き検出される）。
        self.declare_parameter('attached_touch_links',
                               ['flange', 'fanuc_flange', 'end_effector',
                                'J6_link', 'J5_link', 'J4_link'])
        # ヘッド向き補正（度・RPY）。Unityフランジ軸とURDF attachリンク軸のズレを ROS2 側で吸収。
        # attach リンク原点まわりに各 item.pose を回転。Unity 側は生送り（headCalibrationEuler 撤去済＝二重補正なし）。
        # ros2 param set /kmx_planner head_calibration_rpy "[r,p,y]" で live 調整可（次の Send Head で反映）。
        # ★CRX-30iA の FANUCヘッド は [0,90,90] で実機確認済（2026-07-05）。別ツール/構成なら再調整。
        self.declare_parameter('head_calibration_rpy', [0.0, 90.0, 90.0])
        # ★attached の統合（既定 on）: 受信 item 数が attached_merge_over を超えたら、attach リンク座標系の
        #   union AABB 1箱に統合して attach する（安全弁）。Unity の Send Head は Collider 毎の AABB を
        #   数百個送り得て（実測395個）、attached 体数に比例して move_group の衝突チェック＝計画が激重になるため。
        #   ★間引き運用（把持開口を残す）: Unity が headAsSingleBox=false で「本体＋爪」など数箱を送れば、
        #   attached_merge_over 以下なので ROS2 は統合せず「送られた箱そのまま」attach＝開口が保たれる。
        #   headAsSingleBox=true なら1箱。＝ヘッド形状の切替は Unity 側だけで完結（ROS2 は閾値超のみ安全弁で統合）。
        #   完全に統合を切りたいなら attached_merge_aabb=false（次の Send Head から反映）。
        self.declare_parameter('attached_merge_aabb', True)
        # 統合を発動する item 数の閾値。この数「超」で union 1箱化（安全弁）。間引き想定の数箱は保持される。
        self.declare_parameter('attached_merge_over', 12)
        # 1試行あたりの計画時間。BITstar は anytime（この時間を使い切って最適化し続ける）なので
        # 「1試行の質」に直結する。3s で cost≈直線1.5〜2.0倍が実測（2026-07-06、実シーン）。
        self.declare_parameter('allowed_planning_time', 3.0)
        self.declare_parameter('vel_scale', 0.3)
        self.declare_parameter('acc_scale', 0.3)
        # 軌跡最適化（設計1・ROS2側固定。Unity/PlanRequest は無変更）。ros2 param set で live 調整可。
        # planner_id: OMPL プランナ。既定 BITstar（informed 最適化。狭所の実測で cost 1.5〜2.0倍・
        #   成功率 7〜8/10。失敗分は下のリトライ＋plan_fallback_planner で吸収し実質毎回成功）。
        #   ※BITstar/ABITstar/AITstar は ~/ws_moveit(2.5.9) にローカルバックポート登録済み。
        #   速さ最優先なら RRTConnect（発見は速いが cost 2.6〜5倍）。
        self.declare_parameter('planner_id', 'BITstar')
        # 全リトライ失敗時に1回だけ試す保険プランナ（空文字で無効）。BITstar が稀に全滅しても
        # RRTConnect（実測10/10成功）で「経路が返らない」事態を防ぐ。cost は劣るが不成立よりよい。
        self.declare_parameter('plan_fallback_planner', 'RRTConnect')
        # move_group 内の並列試行数 = OMPL ParallelPlan で N 本を並列スレッド生成し最短を返す（in-process）。
        # ★8 に設定（2026-07-07 実測で単発 npa=1 に全項目で勝利：実シーン単発比較で
        #   成功 5/5 vs 4/5・倍率中央 1.64 vs 1.88・レイテンシ 9.8s vs 13.1s）。24コアで余裕。
        #   狭所は best-of-8 が単発の取りこぼしを救い、経路も短く・速い。単発に戻すなら 1（revert_baseline.sh）。
        self.declare_parameter('num_planning_attempts', 8)
        self.declare_parameter('planning_pipeline', 'ompl')
        # ★リトライ＋経路最適化（狭所対策）：時間予算内・最大 plan_retries 回まで計画を繰り返し、
        #   失敗はリトライ／成功は貯めて「関節空間の総移動量が最小の経路」を採用して発行する。
        #   plan_time_budget_sec<=0 なら回数のみで制御。ros2 param set で live 調整可。
        self.declare_parameter('plan_retries', 20)
        self.declare_parameter('plan_time_budget_sec', 10.0)
        # 大回り回避＆早期終了：最良経路の cost が「始点→終点の直線関節距離(=下限)」の
        # plan_good_ratio 倍以下なら十分短いとみなし、予算を待たず即採用。超えていれば予算/回数まで
        # 「より短い通り道」を探し続ける（稀な大迂回ホモトピーで妥協しない）。0以下で無効（常に予算使用）。
        self.declare_parameter('plan_good_ratio', 2.0)
        # ★経路短縮（RRT*-Smart の Path Optimization 相当）：発行前に、非隣接ウェイポイント間を
        #   直結できる（間の直線補間が衝突しない）なら中間点を捨てて経路を短くする。衝突判定は
        #   /check_state_validity を経路上だけに使う（attachヘッド＋障害物込み）。冗長な迂回を除去。
        self.declare_parameter('path_shortcut', True)
        self.declare_parameter('shortcut_step_deg', 4.0)          # 直線補間の衝突チェック刻み(度)
        self.declare_parameter('shortcut_output_step_deg', 5.0)   # 出力の再サンプル刻み(度)
        self.declare_parameter('state_validity_service', '/check_state_validity')
        # ★経路生成バックエンド。'moveit'=現行(OMPL RRTConnect+retry+shortcut, 既定)。
        #   'rrtstar_smart'=Python実装のRRT*-Smart（実験的）。衝突判定は check_state_validity 経由の
        #   ため速度は控えめ。時間予算(plan_time_budget_sec / PlanRequest.time_budget)内で最良を返す。
        self.declare_parameter('planner_backend', 'moveit')
        self.declare_parameter('rrt_step_deg', 20.0)          # 木の1ステップ伸長量(度)
        self.declare_parameter('rrt_goal_bias', 0.1)          # goal 方向サンプル確率
        self.declare_parameter('rrt_goal_tol_deg', 6.0)       # goal 到達とみなす関節距離(度)
        self.declare_parameter('rrt_rewire_radius_deg', 45.0) # RRT* 近傍リワイヤ半径(度)
        self.declare_parameter('rrt_beacon_bias', 0.35)       # RRT*-Smart: 経路近傍サンプル確率
        self.declare_parameter('rrt_beacon_radius_deg', 25.0) # ビーコン近傍サンプル半径(度)
        self.declare_parameter('robot_description_topic', '/robot_description')
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
        # 計画ステータス通知（状態文字列のみ・軌道は載せない）。reliable で取りこぼし防止。
        status_qos = QoSProfile(depth=10, reliability=ReliabilityPolicy.RELIABLE)
        self.status_pub = self.create_publisher(
            String, self.get_parameter('plan_status_topic').value, status_qos)

        self._ac = None
        self._sv_cli = None
        if self.use_moveit:
            self._ac = ActionClient(self, MoveGroup, self.get_parameter('move_action').value)
            # 経路短縮の衝突チェック用。別コールバックグループにして、計画コールバック内から
            # 同期 call() してもデッドロックしないようにする（MultiThreadedExecutor と併用）。
            self._sv_cli = self.create_client(
                GetStateValidity, self.get_parameter('state_validity_service').value,
                callback_group=MutuallyExclusiveCallbackGroup())
            # RRT*-Smart 用に URDF の関節可動域を取得（/robot_description を1回購読・latched）。
            self._joint_limits_deg = {}   # name -> (lo_deg, hi_deg)
            rd_qos = QoSProfile(depth=1, reliability=ReliabilityPolicy.RELIABLE,
                                durability=DurabilityPolicy.TRANSIENT_LOCAL)
            self.create_subscription(String, self.get_parameter('robot_description_topic').value,
                                     self._on_robot_description, rd_qos)
        self._plan_session = 0   # リトライセッションID（新要求で++し、古いセッションのコールバックを無効化）

        # ---- 障害物 → planning scene ----
        # /kmx/obstacles を購読し、CollisionObject 化して move_group の planning scene に反映する。
        # 反映後は plan_only（/kmx/plan_request）が障害物を回避した軌道を返す。
        self._obstacle_ids = set()   # 前回反映した world 障害物 id 集合（消し込み用）
        self._attached_ids = set()   # 前回反映した attached(ツール) id 集合
        self.attach_link = self.get_parameter('attach_link').value
        self.attached_touch_links = list(self.get_parameter('attached_touch_links').value)
        self._scene_cli = None
        self._get_scene_cli = None
        obs_topic = self.get_parameter('obstacles_topic').value
        att_topic = self.get_parameter('attached_topic').value
        if _OBSTACLES_MSG_AVAILABLE:
            self.obs_sub = self.create_subscription(Obstacles, obs_topic, self.on_obstacles, 10)
            # ヘッド(ツール) 用。型は障害物と同じ Obstacles、トピックだけ別（frame_id=attachリンク）。
            self.att_sub = self.create_subscription(Obstacles, att_topic, self.on_attached, 10)
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
            f"obstacles={'on' if _OBSTACLES_MSG_AVAILABLE else 'off'} "
            f"attached={'on(' + att_topic + ')' if _OBSTACLES_MSG_AVAILABLE else 'off'} "
            f"use_moveit={self.use_moveit}")

    # ---------------------------------------------------------------- 要求受信
    # MoveItErrorCodes.val → Unity 向け失敗理由文字列。取れない/未設定は no_solution。
    _ERROR_NAMES = {
        -2: 'invalid_motion_plan', -3: 'env_changed',
        -10: 'START_STATE_IN_COLLISION', -11: 'start_violates_constraints',
        -12: 'GOAL_IN_COLLISION', -13: 'goal_violates_constraints',
        -14: 'goal_constraints_violated', -15: 'invalid_group_name',
        -17: 'invalid_robot_state',
    }

    def _error_reason(self, val):
        if val in (0, -1, -6):   # 未設定 / PLANNING_FAILED / TIMED_OUT ＝「解なし」に集約
            return 'no_solution'
        return self._ERROR_NAMES.get(val, f'code_{val}')

    def _publish_status(self, text):
        """計画ステータスを /kmx/plan_status に publish（planning / succeeded:.. / failed:..）。"""
        try:
            self.status_pub.publish(String(data=text))
        except Exception:   # noqa: BLE001  通知失敗で計画本体を止めない
            pass

    def on_request(self, msg: PlanRequest):
        names = list(msg.names) if msg.names else list(self.kmx_joints)
        start = list(msg.start)
        goal = list(msg.goal)
        # Unity から任意で計画の粘り具合を指定（>0 のときだけ有効。未設定/0 は node 既定）。
        req_budget = float(getattr(msg, 'time_budget', 0.0) or 0.0)
        req_ratio = float(getattr(msg, 'good_ratio', 0.0) or 0.0)
        self.get_logger().info(
            f"plan request: start={start} goal={goal} (deg), joints={names}"
            + (f" time_budget={req_budget}s" if req_budget > 0 else "")
            + (f" good_ratio={req_ratio}" if req_ratio > 0 else ""))

        if len(start) != len(names) or len(goal) != len(names):
            self.get_logger().error("names / start / goal の長さが一致しません。")
            self._publish_status("failed:bad_request")
            return

        self._publish_status("planning")   # 計画開始（moveit / 補間 共通）
        if self.use_moveit:
            self.plan_with_moveit(names, start, goal, req_budget, req_ratio)   # 非同期。完了時に発行＋status。
        else:
            traj = self.plan_interpolate(names, start, goal)
            direct = math.sqrt(sum((g - s) ** 2 for s, g in zip(start[:len(names)], goal[:len(names)])))
            ratio = (self._traj_cost(traj) / direct) if direct > 1e-6 else 1.0
            self.pub.publish(traj)
            self._publish_status(f"succeeded:{len(traj.points)}:{ratio:.2f}")
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

        # id 集合は「反映に成功したら」更新する（失敗時に据え置くことで、
        # 次回の REMOVE 差分を最後に成功した状態基準で正しく計算できる）。
        self._apply_scene(scene, '_obstacle_ids', new_ids, '障害物')

    # ------------------------------------- ヘッド(ツール)受信 → AttachedCollisionObject
    def on_attached(self, msg: Obstacles):
        """/kmx/attached をロボットに付いたツール(AttachedCollisionObject)として反映する（方式B）。

        障害物(/kmx/obstacles)と型は同じ Obstacles だが、world ではなく attach リンクに付ける点が違う。
        - frame_id = attach 先リンク名（例 flange）。空なら attach_link パラメータの既定。
        - items[] の pose は attach リンク相対。
        - touch_links(=attached_touch_links) でツールと接触して当然のリンクの自己干渉を許可。
        - 受信のたび全置換（今回分 ADD／前回あって今回無い id は REMOVE）。world 障害物とは id 集合を分離。
        反映後は plan_only がツール形状も含めて障害物を回避する。
        """
        link = msg.frame_id if msg.frame_id else self.attach_link
        self.get_logger().info(
            f"attached(head) received: {len(msg.items)} items, link='{link}'")

        # ★縮退ガード（HEAD_POSE_ZERO_UNITY_SPEC・2026-07-07）: 間引きヘッドで Unity が時々「全箱 pose=(0,0,0)」で
        #   送る不具合があり、ヘッドが flange 原点に潰れる。複数 item が全て原点なら明らかに不正なので、この更新は
        #   破棄して前回の正常なヘッドを維持する。空配列(=全消し)は正当なので除外（len>=2 のときだけ判定）。
        if len(msg.items) >= 2 and all(
                abs(it.pose.position.x) < 1e-4 and abs(it.pose.position.y) < 1e-4
                and abs(it.pose.position.z) < 1e-4 for it in msg.items):
            self.get_logger().warn(
                f"attached(head): {len(msg.items)}個が全て原点(pose≒0)＝縮退。Unity 送信不良の疑いにより"
                "この更新を破棄し前回のヘッドを維持します（HEAD_POSE_ZERO_UNITY_SPEC）。")
            return

        scene = PlanningScene()
        scene.is_diff = True
        scene.robot_state.is_diff = True

        # ヘッド向き補正（attachリンク原点まわりの回転）。live 読み（param set で次回反映）。
        rpy = list(self.get_parameter('head_calibration_rpy').value)
        cal_active = any(abs(float(a)) > 1e-9 for a in rpy)
        qc = _quat_from_rpy(math.radians(rpy[0]), math.radians(rpy[1]),
                            math.radians(rpy[2])) if cal_active else None
        if cal_active:
            self.get_logger().info(f"  head_calibration_rpy(deg)={rpy} を適用")

        # ★attached は「前回分を全部 REMOVE してから今回分を ADD」する（REMOVE 先行）。
        #   world 障害物は同一id ADD で置換されるが、attached は同一id 再ADDでも綺麗に置換されず
        #   毎回積み増さる（プランのたびにヘッドが増える）挙動があるため、差分ではなく全消し先行にする。
        new_ids = set(item.id for item in msg.items)

        # 1) 前回 attached を全て外す（同一idを含め全部）。REMOVE を先に積む。
        #    ★重要: MoveIt は attached を REMOVE すると「削除」せず world へ detach（戻す）ため、
        #      放置すると full-replace の度に古いヘッドが world collision object として蓄積する
        #      （RViz に残り重なって見える）。よって同 id を world からも REMOVE する。
        #      PlanningScene diff は robot_state(attached) → world の順に処理されるので、
        #      同一 diff 内で「detach→world REMOVE」が正しく消える。
        for old_id in self._attached_ids:
            co = CollisionObject()
            co.header.frame_id = link
            co.id = old_id
            co.operation = CollisionObject.REMOVE
            aco = AttachedCollisionObject()
            aco.link_name = link
            aco.object = co
            scene.robot_state.attached_collision_objects.append(aco)
            # detach 先の world コピーも消す
            wco = CollisionObject()
            wco.id = old_id
            wco.operation = CollisionObject.REMOVE
            scene.world.collision_objects.append(wco)

        # 2) 今回分を付ける（ADD）。
        # touch_links は live 読み（ros2 param set → 次の Send Head から反映）。
        touch_links = list(self.get_parameter('attached_touch_links').value)
        merge_over = max(1, int(self.get_parameter('attached_merge_over').value))
        merge = bool(self.get_parameter('attached_merge_aabb').value) and len(msg.items) > merge_over
        if merge:
            # 全 item（補正回転適用後）を包む union AABB を attach リンク座標系で計算し、1箱で attach。
            lo = [float('inf')] * 3
            hi = [float('-inf')] * 3
            for item in msg.items:
                pp = item.pose.position
                px, py, pz = float(pp.x), float(pp.y), float(pp.z)
                po = item.pose.orientation
                q = (float(po.x), float(po.y), float(po.z), float(po.w))
                if cal_active:
                    px, py, pz = _quat_rotate_vec(qc, (px, py, pz))
                    q = _quat_mul(qc, q)
                d = [float(x) for x in item.dimensions]
                t = int(item.type)
                if t == 2:      # SPHERE: dimensions=[radius]。回転不変なので中心±r。
                    r = d[0]
                    for i, c in enumerate((px, py, pz)):
                        lo[i] = min(lo[i], c - r)
                        hi[i] = max(hi[i], c + r)
                    continue
                if t == 3:      # CYLINDER: dimensions=[height, radius]。外接箱で保守的に。
                    ext = (d[1], d[1], d[0] * 0.5)
                else:           # BOX: dimensions=[x,y,z]
                    ext = (d[0] * 0.5, d[1] * 0.5, d[2] * 0.5)
                for sx in (-1.0, 1.0):
                    for sy in (-1.0, 1.0):
                        for sz in (-1.0, 1.0):
                            cx, cy, cz = _quat_rotate_vec(q, (sx * ext[0], sy * ext[1], sz * ext[2]))
                            for i, v in enumerate((px + cx, py + cy, pz + cz)):
                                lo[i] = min(lo[i], v)
                                hi[i] = max(hi[i], v)
            co = CollisionObject()
            co.header.frame_id = link
            co.id = 'kmx_head_merged'
            sp = SolidPrimitive()
            sp.type = SolidPrimitive.BOX
            sp.dimensions = [max(hi[i] - lo[i], 1e-3) for i in range(3)]
            co.primitives.append(sp)
            pose = Pose()
            pose.position.x, pose.position.y, pose.position.z = [(hi[i] + lo[i]) * 0.5 for i in range(3)]
            pose.orientation.w = 1.0
            co.primitive_poses.append(pose)
            co.operation = CollisionObject.ADD
            aco = AttachedCollisionObject()
            aco.link_name = link
            aco.object = co
            aco.touch_links = touch_links
            scene.robot_state.attached_collision_objects.append(aco)
            new_ids = {co.id}
            self.get_logger().info(
                f"  attached_merge_aabb: {len(msg.items)}個 → 1箱 "
                f"size={[round(v, 3) for v in sp.dimensions]} "
                f"center={[round((hi[i] + lo[i]) * 0.5, 3) for i in range(3)]}")
        else:
            for item in msg.items:
                co = CollisionObject()
                co.header.frame_id = link
                co.id = item.id
                sp = SolidPrimitive()
                sp.type = int(item.type)
                sp.dimensions = [float(d) for d in item.dimensions]
                co.primitives.append(sp)
                pose = item.pose
                if cal_active:
                    p = pose.position
                    p.x, p.y, p.z = _quat_rotate_vec(qc, (p.x, p.y, p.z))
                    o = pose.orientation
                    o.x, o.y, o.z, o.w = _quat_mul(qc, (o.x, o.y, o.z, o.w))
                co.primitive_poses.append(pose)
                co.operation = CollisionObject.ADD
                aco = AttachedCollisionObject()
                aco.link_name = link
                aco.object = co
                aco.touch_links = touch_links
                scene.robot_state.attached_collision_objects.append(aco)
            self.get_logger().info(
                f"  attached: {len(msg.items)}箱を個別 attach（統合せず・間引き想定・link={link}・"
                f"閾値 attached_merge_over={merge_over}）")

        self._apply_scene(scene, '_attached_ids', new_ids, 'ツール(attached)')

    def _apply_scene(self, scene: PlanningScene, target_attr, new_ids, label):
        """planning scene diff を反映。service 優先、未準備なら topic publish で fallback。

        target_attr: 反映確定時に new_ids を書き込む属性名（'_obstacle_ids' or '_attached_ids'）。
        label: ログ用ラベル。world障害物と attached(ツール) で共用する。
        """
        if self._scene_cli is not None and self._scene_cli.service_is_ready():
            req = ApplyPlanningScene.Request()
            req.scene = scene
            self._scene_cli.call_async(req).add_done_callback(
                lambda fut: self._on_scene_applied(fut, target_attr, new_ids, label))
        else:
            # publish は成否不明のためベストエフォートで更新（service が使えない環境向け）。
            self._scene_pub.publish(scene)
            setattr(self, target_attr, new_ids)
            self.get_logger().warn(
                f"apply_planning_scene 未準備 → /planning_scene へ publish で反映（{label}・fallback・成否不明）。"
                "move_group は起動していますか？")

    def _on_scene_applied(self, future, target_attr, new_ids, label):
        try:
            res = future.result()
            ok = getattr(res, 'success', True)
            # ★id は success に関わらず確定する。attached の ADD は MoveIt(2.5.9) が success=False を
            #   返しても diff 自体は適用される（実測：箱は scene に載る）。ここで確定しないと _attached_ids が
            #   空のままになり、全置換の REMOVE が対象を持てず「空を送ってもヘッドが消えない」stale 残留に陥る。
            #   呼び出しが例外で完全失敗した場合のみ据え置き（下の except）。
            setattr(self, target_attr, new_ids)
            if ok:
                self.get_logger().info(
                    f"planning scene 更新({label}): success=True active_ids={sorted(new_ids)}")
            else:
                self.get_logger().warn(
                    f"planning scene 更新({label}): success=False だが diff は適用済み前提で "
                    f"{target_attr} を確定（stale 残留回避）。active_ids={sorted(new_ids)}")
        except Exception as e:   # noqa: BLE001
            self.get_logger().error(
                f"apply_planning_scene 呼び出し失敗({label}): {e}（{target_attr} は据え置き）")

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
    def plan_with_moveit(self, kmx_names, start_deg, goal_deg, req_budget=0.0, req_ratio=0.0):
        """move_group に MoveGroup アクション(plan_only)で joint 目標を投げる（非同期）。
        req_budget/req_ratio が >0 ならその要求だけ node 既定を上書き（Unity から粘り具合を指定）。"""
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

        # バックエンド分岐：'rrtstar_smart' なら Python 実装で計画（別スレッド・時間予算内で最良）。
        if self.get_parameter('planner_backend').value == 'rrtstar_smart':
            self._plan_session += 1
            sid = self._plan_session
            budget = req_budget if req_budget > 0 else float(self.get_parameter('plan_time_budget_sec').value)
            ratio = req_ratio if req_ratio > 0 else float(self.get_parameter('plan_good_ratio').value)
            threading.Thread(
                target=self._run_rrtstar_smart,
                args=(list(start_deg[:n]), list(goal_deg[:n]), list(mj),
                      list(kmx_names[:n]), max(0.5, budget), ratio, sid),
                daemon=True).start()
            return

        req = MotionPlanRequest()
        req.group_name = self.get_parameter('planning_group').value
        req.pipeline_id = self.get_parameter('planning_pipeline').value
        req.planner_id = self.get_parameter('planner_id').value      # OMPL 最適化プランナ（軌跡最適化）
        req.num_planning_attempts = int(self.get_parameter('num_planning_attempts').value)
        req.allowed_planning_time = float(self.get_parameter('allowed_planning_time').value)
        req.max_velocity_scaling_factor = float(self.get_parameter('vel_scale').value)
        req.max_acceleration_scaling_factor = float(self.get_parameter('acc_scale').value)

        # 始点（関節は絶対指定。is_diff=True でも joint_state の値は絶対値として適用される）
        # is_diff=False だと MoveIt がシーン現在状態の attached body を全消去した状態で計画し、
        # attached（ヘッド/ツール）が world 障害物と衝突判定されなくなる
        # （moveit_core/robot_state/conversions.cpp: !is_diff → clearAttachedBodies()）。
        start_state = RobotState()
        js = JointState()
        js.name = mj
        js.position = start_rad
        start_state.joint_state = js
        start_state.is_diff = True
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

        # リトライ＋経路最適化セッションを開始。時間予算内・最大 plan_retries 回、
        # 失敗はリトライ／成功は貯めて最短経路を採用。マッピング(out/moveit名)は session に閉じて持ち回る
        # （self._pending_* だと並行要求で上書きされ誤変換するため）。
        self._plan_session += 1
        # Unity 指定(req_*)が >0 ならそれを、無ければ node 既定を使う。
        budget = req_budget if req_budget > 0 else float(self.get_parameter('plan_time_budget_sec').value)
        good_ratio = req_ratio if req_ratio > 0 else float(self.get_parameter('plan_good_ratio').value)
        session = {
            'id': self._plan_session,
            'goal': goal,
            'out_names': list(kmx_names[:n]),
            'moveit_names': list(mj),
            'attempts': 0,
            'max_attempts': max(1, int(self.get_parameter('plan_retries').value)),
            'deadline_ns': (self.get_clock().now().nanoseconds + int(budget * 1e9)) if budget > 0 else None,
            'good_ratio': good_ratio,
            'best_traj': None,
            'best_cost': None,
            'successes': 0,
            'last_error': 0,   # 直近試行の MoveItErrorCodes.val（最終失敗時の理由に使う）
            # 始点→終点の直線関節距離（度）。経路長の下限。大回り判定の基準に使う。
            'direct_cost': math.sqrt(sum((g - s) ** 2 for s, g in zip(start_deg[:n], goal_deg[:n]))),
        }
        self.get_logger().info(
            f"plan session #{session['id']} 開始: planner={req.planner_id} "
            f"max_attempts={session['max_attempts']} budget={budget}s")
        self._send_plan_attempt(session)

    def _send_plan_attempt(self, session):
        session['attempts'] += 1
        self._ac.send_goal_async(session['goal']).add_done_callback(
            lambda fut: self._on_goal_response(fut, session))

    def _on_goal_response(self, future, session):
        if session['id'] != self._plan_session:
            return   # 新しい plan 要求に置き換わった（このセッションは破棄）
        goal_handle = future.result()
        if not goal_handle.accepted:
            self.get_logger().warn("move_group がゴールを拒否（この試行をスキップ）。")
            self._maybe_retry_or_finish(session)
            return
        goal_handle.get_result_async().add_done_callback(
            lambda fut: self._on_result(fut, session))

    def _on_result(self, future, session):
        if session['id'] != self._plan_session:
            return   # 破棄されたセッション
        result = future.result().result
        # moveit_msgs/MoveItErrorCodes.SUCCESS == 1
        if result.error_code.val == 1:
            traj = self._convert_result(
                result.planned_trajectory.joint_trajectory,
                session['out_names'], session['moveit_names'])
            if traj is not None and len(traj.points) > 0:
                cost = self._traj_cost(traj)
                session['successes'] += 1
                if session['best_cost'] is None or cost < session['best_cost']:
                    session['best_traj'] = traj
                    session['best_cost'] = cost
            else:
                session['last_error'] = -2   # 計画成功だが変換/検証で無効（INVALID_MOTION_PLAN 相当）
        else:
            session['last_error'] = result.error_code.val
        self._maybe_retry_or_finish(session)

    def _maybe_retry_or_finish(self, session):
        within_time = (session['deadline_ns'] is None
                       or self.get_clock().now().nanoseconds < session['deadline_ns'])
        within_attempts = session['attempts'] < session['max_attempts']
        # 十分短い経路が既に得られたか（直線距離の good_ratio 倍以下）。得られていれば粘らず終了。
        ratio = session['good_ratio']
        good_enough = (session['best_cost'] is not None and ratio > 0.0
                       and session['direct_cost'] > 1e-6
                       and session['best_cost'] <= ratio * session['direct_cost'])
        if not good_enough and within_attempts and within_time:
            self._send_plan_attempt(session)   # 失敗はリトライ／大回りしか無いならより短い通り道を探し続ける
            return
        # 予算/回数を使い切った → 最良経路を（必要なら短縮して）発行
        if session['best_traj'] is not None:
            traj = session['best_traj']
            raw_cost = session['best_cost']
            if bool(self.get_parameter('path_shortcut').value):
                traj = self._shortcut_traj(traj, session['moveit_names'])
            post = self._traj_cost(traj)
            direct = max(session['direct_cost'], 1e-6)
            self.pub.publish(traj)
            self._publish_status(f"succeeded:{len(traj.points)}:{post / direct:.2f}")
            self.get_logger().info(
                f"published best trajectory: {len(traj.points)} points "
                f"(moveit, cost {raw_cost:.1f}→{post:.1f}, 直線={session['direct_cost']:.1f} "
                f"[{post / direct:.1f}倍], {session['successes']}/{session['attempts']} 成功)")
        else:
            # ★保険: 主プランナ（既定 BITstar）が全滅なら、fallback プランナで1回だけ最終試行。
            #   予算超過でも実行する（「経路が返らない」のが最悪のため）。以降のリトライ判定は
            #   通常ループに戻る（成功すれば発行、失敗すればここに再度落ちて下の error）。
            fb = str(self.get_parameter('plan_fallback_planner').value or '').strip()
            cur = session['goal'].request.planner_id
            if fb and fb != cur and not session.get('fallback_used'):
                session['fallback_used'] = True
                session['goal'].request.planner_id = fb
                self.get_logger().warn(
                    f"plan session #{session['id']}: {cur} {session['attempts']}回失敗 → "
                    f"fallback '{fb}' で最終試行。")
                self._send_plan_attempt(session)
                return
            reason = self._error_reason(session.get('last_error', 0))
            self._publish_status(f"failed:{reason}")
            self.get_logger().error(
                f"MoveIt 計画失敗: {session['attempts']} 回試行しても有効経路なし。(reason={reason})")

    @staticmethod
    def _traj_cost(traj):
        """経路コスト＝関節空間の総移動量（度）。小さいほど短く素直。最良選択に使う。"""
        total = 0.0
        pts = traj.points
        for a, b in zip(pts, pts[1:]):
            total += math.sqrt(sum((y - x) ** 2 for x, y in zip(a.positions, b.positions)))
        return total

    # ------------------------------------------- 経路短縮（RRT*-Smart の Path Optimization 相当）
    def _shortcut_traj(self, traj, moveit_names):
        """発行前の経路短縮：非隣接ウェイポイント間を直結できるなら中間を捨てる（貪欲）。

        衝突判定は /check_state_validity を経路上の補間点だけに使う（attachヘッド＋障害物込み）。
        MoveIt 側の簡易ショートカットで残った冗長な迂回を、より積極的に除去する。
        """
        if self._sv_cli is None or not self._sv_cli.service_is_ready():
            return traj
        pts = [list(p.positions) for p in traj.points]   # deg, out_names(J1..J6)順
        if len(pts) < 3:
            return traj
        step = float(self.get_parameter('shortcut_step_deg').value)
        nchk = [0]
        keep = [pts[0]]
        i = 0
        while i < len(pts) - 1:
            j = len(pts) - 1
            while j > i + 1 and not self._segment_free(pts[i], pts[j], moveit_names, step, nchk):
                j -= 1
            keep.append(pts[j])
            i = j
        out = self._densify_retime(keep, traj)
        self.get_logger().info(
            f"  経路短縮: {len(pts)}点→{len(keep)}節→{len(out.points)}点 "
            f"(衝突チェック{nchk[0]}回)")
        return out

    def _segment_free(self, a, b, moveit_names, step, nchk):
        """a→b の関節空間直線を step(度)刻みで補間し、各点が衝突しないか検証。"""
        dmax = max((abs(y - x) for x, y in zip(a, b)), default=0.0)
        n = max(1, int(math.ceil(dmax / max(0.5, step))))
        for k in range(1, n + 1):
            t = k / n
            cfg = [x + (y - x) * t for x, y in zip(a, b)]
            nchk[0] += 1
            if not self._state_valid(cfg, moveit_names):
                return False
        return True

    def _state_valid(self, pos_deg, moveit_names):
        """関節姿勢(度)がシーン(attachヘッド＋障害物)に対し衝突しないか。同期 call。"""
        req = GetStateValidity.Request()
        rs = RobotState()
        js = JointState()
        js.name = list(moveit_names)
        js.position = [math.radians(v) for v in pos_deg]
        rs.joint_state = js
        rs.is_diff = True   # scene の attached body を保持して判定（is_diff=Falseだと消える）
        req.robot_state = rs
        req.group_name = self.get_parameter('planning_group').value
        try:
            res = self._sv_cli.call(req)
            return bool(res.valid)
        except Exception as e:   # noqa: BLE001
            self.get_logger().warn(f"check_state_validity 失敗（短縮を保守的に中止）: {e}")
            return False   # 検証不能 → 直結しない（元の経路を保つ＝安全側）

    def _densify_retime(self, keep, orig_traj):
        """短縮後の節点列を再サンプル(度刻み)し、累積関節距離に比例して再タイミングする。"""
        out_step = float(self.get_parameter('shortcut_output_step_deg').value)
        dense = [keep[0]]
        for a, b in zip(keep, keep[1:]):
            dmax = max((abs(y - x) for x, y in zip(a, b)), default=0.0)
            n = max(1, int(math.ceil(dmax / max(0.5, out_step))))
            for k in range(1, n + 1):
                t = k / n
                dense.append([x + (y - x) * t for x, y in zip(a, b)])
        # 総時間は元軌道を踏襲（速度感を保つ）。累積距離に比例して time_from_start を割り当て。
        last = orig_traj.points[-1].time_from_start
        total_time = last.sec + last.nanosec * 1e-9
        if total_time <= 0.0:
            total_time = float(self.get_parameter('duration_sec').value)
        cum = [0.0]
        for a, b in zip(dense, dense[1:]):
            cum.append(cum[-1] + math.sqrt(sum((y - x) ** 2 for x, y in zip(a, b))))
        length = cum[-1] if cum[-1] > 1e-9 else 1.0
        out = JointTrajectory()
        out.joint_names = list(orig_traj.joint_names)
        for cfg, c in zip(dense, cum):
            q = JointTrajectoryPoint()
            q.positions = [float(v) for v in cfg]
            t = total_time * (c / length)
            q.time_from_start = Duration(sec=int(t), nanosec=int(round((t - int(t)) * 1e9)))
            out.points.append(q)
        return out

    # ------------------------------------------------- RRT*-Smart（Python実装・実験的バックエンド）
    def _on_robot_description(self, msg):
        """URDF から各関節の可動域(度)を取得（RRT*-Smart のサンプリング範囲用）。"""
        found = {}
        for m in re.finditer(r'<joint\b[^>]*name="([^"]+)"[^>]*>(.*?)</joint>', msg.data, re.S):
            name, body = m.group(1), m.group(2)
            lm = re.search(r'<limit\b[^>]*>', body)
            if not lm:
                continue
            lo = re.search(r'lower="([-\d.eE]+)"', lm.group(0))
            hi = re.search(r'upper="([-\d.eE]+)"', lm.group(0))
            if lo and hi:
                found[name] = (math.degrees(float(lo.group(1))), math.degrees(float(hi.group(1))))
        if found:
            self._joint_limits_deg = found
            self.get_logger().info(f"RRT*-Smart: 関節可動域(度)取得 {len(found)}軸")

    def _jl(self, moveit_names):
        """moveit_names に対応する (lo_deg, hi_deg) のリスト。未取得なら ±180 で代用。"""
        return [self._joint_limits_deg.get(nm, (-180.0, 180.0)) for nm in moveit_names]

    @staticmethod
    def _dist(a, b):
        return math.sqrt(sum((y - x) ** 2 for x, y in zip(a, b)))

    def _run_rrtstar_smart(self, start_deg, goal_deg, moveit_names, out_names, budget, ratio, sid):
        """別スレッド：RRT*-Smart で計画→（短縮して）発行。sid が古くなれば破棄。"""
        try:
            path, gcost, iters = self._rrtstar_smart(start_deg, goal_deg, moveit_names, budget, ratio, sid)
        except Exception as e:   # noqa: BLE001
            self.get_logger().error(f"RRT*-Smart 例外: {e}")
            self._publish_status("failed:exception")
            return
        if sid != self._plan_session:
            return
        if path is None:
            self.get_logger().error(
                f"RRT*-Smart: 時間内({budget:.1f}s)に経路が見つかりませんでした（{iters}反復）。")
            self._publish_status("failed:no_solution")
            return
        traj = self._path_to_traj(path, out_names)
        raw = self._traj_cost(traj)
        if bool(self.get_parameter('path_shortcut').value):
            traj = self._shortcut_traj(traj, moveit_names)
        direct = max(self._dist(start_deg, goal_deg), 1e-6)
        post = self._traj_cost(traj)
        self.pub.publish(traj)
        self._publish_status(f"succeeded:{len(traj.points)}:{post / direct:.2f}")
        self.get_logger().info(
            f"published best trajectory: {len(traj.points)} points "
            f"(RRT*-Smart, cost {raw:.1f}→{post:.1f}, 直線={direct:.1f} [{post / direct:.1f}倍], {iters}反復)")

    def _rrtstar_smart(self, start, goal, moveit_names, budget, ratio, sid):
        """RRT*-Smart 本体（関節空間・度）。(config列 start→goal, コスト, 反復数) を返す。"""
        limits = self._jl(moveit_names)
        step = float(self.get_parameter('rrt_step_deg').value)
        goal_bias = float(self.get_parameter('rrt_goal_bias').value)
        goal_tol = float(self.get_parameter('rrt_goal_tol_deg').value)
        radius = float(self.get_parameter('rrt_rewire_radius_deg').value)
        beacon_bias = float(self.get_parameter('rrt_beacon_bias').value)
        beacon_r = float(self.get_parameter('rrt_beacon_radius_deg').value)
        chk_step = float(self.get_parameter('shortcut_step_deg').value)
        dim = len(start)
        deadline = self.get_clock().now().nanoseconds + int(budget * 1e9)

        if not self._state_valid(start, moveit_names):
            return None, None, 0

        def edge_free(a, b):
            return self._segment_free(a, b, moveit_names, chk_step, [0])

        Q = [list(start)]
        parent = [-1]
        cost = [0.0]
        best_goal, best_goal_cost = -1, float('inf')
        beacons = []
        iters = 0
        direct = max(self._dist(start, goal), 1e-6)
        while self.get_clock().now().nanoseconds < deadline and sid == self._plan_session:
            iters += 1
            r = random.random()
            if beacons and r < beacon_bias:
                base = random.choice(beacons)
                q_rand = [min(hi, max(lo, base[j] + random.uniform(-beacon_r, beacon_r)))
                          for j, (lo, hi) in enumerate(limits)]
            elif r < beacon_bias + goal_bias:
                q_rand = list(goal)
            else:
                q_rand = [random.uniform(lo, hi) for (lo, hi) in limits]
            i_near = min(range(len(Q)), key=lambda i: self._dist(Q[i], q_rand))
            d = self._dist(Q[i_near], q_rand)
            if d < 1e-6:
                continue
            t = min(1.0, step / d)
            q_new = [Q[i_near][j] + (q_rand[j] - Q[i_near][j]) * t for j in range(dim)]
            if not self._state_valid(q_new, moveit_names) or not edge_free(Q[i_near], q_new):
                continue
            near = [i for i in range(len(Q)) if self._dist(Q[i], q_new) <= radius]
            bp, bc = i_near, cost[i_near] + self._dist(Q[i_near], q_new)
            for i in near:
                c = cost[i] + self._dist(Q[i], q_new)
                if c < bc and edge_free(Q[i], q_new):
                    bp, bc = i, c
            Q.append(q_new)
            parent.append(bp)
            cost.append(bc)
            new_i = len(Q) - 1
            for i in near:
                c = bc + self._dist(q_new, Q[i])
                if c < cost[i] - 1e-9 and edge_free(q_new, Q[i]):
                    parent[i] = new_i
                    cost[i] = c
            dg = self._dist(q_new, goal)
            if dg <= goal_tol and edge_free(q_new, goal):
                gc = bc + dg
                if gc < best_goal_cost:
                    Q.append(list(goal))
                    parent.append(new_i)
                    cost.append(gc)
                    best_goal, best_goal_cost = len(Q) - 1, gc
                    beacons = self._extract_path(Q, parent, best_goal)
                    if best_goal_cost <= ratio * direct:
                        break   # 十分短い → 早期終了
        if best_goal < 0:
            return None, None, iters
        return self._extract_path(Q, parent, best_goal), best_goal_cost, iters

    @staticmethod
    def _extract_path(Q, parent, idx):
        path = []
        while idx != -1:
            path.append(list(Q[idx]))
            idx = parent[idx]
        path.reverse()
        return path

    def _path_to_traj(self, path, out_names):
        """config列(度) → 出力刻みで再サンプル＋距離比例タイミングの JointTrajectory（度, out_names順）。"""
        out_step = float(self.get_parameter('shortcut_output_step_deg').value)
        dense = [list(path[0])]
        for a, b in zip(path, path[1:]):
            dmax = max((abs(y - x) for x, y in zip(a, b)), default=0.0)
            m = max(1, int(math.ceil(dmax / max(0.5, out_step))))
            for k in range(1, m + 1):
                t = k / m
                dense.append([x + (y - x) * t for x, y in zip(a, b)])
        total_time = float(self.get_parameter('duration_sec').value)
        cum = [0.0]
        for a, b in zip(dense, dense[1:]):
            cum.append(cum[-1] + self._dist(a, b))
        length = cum[-1] if cum[-1] > 1e-9 else 1.0
        traj = JointTrajectory()
        traj.joint_names = list(out_names)
        for cfg, c in zip(dense, cum):
            q = JointTrajectoryPoint()
            q.positions = [float(v) for v in cfg]
            tt = total_time * (c / length)
            q.time_from_start = Duration(sec=int(tt), nanosec=int(round((tt - int(tt)) * 1e9)))
            traj.points.append(q)
        return traj

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
    # 経路短縮の同期 check_state_validity 呼び出しをコールバック内から行うため MultiThreaded に。
    # （check_state_validity クライアントは別コールバックグループ＝別スレッドで応答処理される）
    executor = MultiThreadedExecutor()
    executor.add_node(node)
    try:
        executor.spin()
    except KeyboardInterrupt:
        pass
    finally:
        node.destroy_node()
        rclpy.shutdown()


if __name__ == '__main__':
    main()
