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
import os
import random
import re
import threading

import yaml   # 登録最適化(段階1.5)：joint_limits.yaml から vel/acc/jerk 上限を読む

# 動的ショートカット(段階1.5+)の steering に使う。★単一ターゲット state-to-state のみ＝ローカル動作
# （intermediate_positions は OSS だとクラウド送信＝使わない）。未導入でも動的SCを無効化して起動できる。
try:
    from ruckig import Ruckig as _Ruckig, InputParameter as _RkInput, \
        Trajectory as _RkTraj, Result as _RkResult
    _RUCKIG_OK = True
except Exception:   # noqa: BLE001
    _RUCKIG_OK = False

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
        # 登録最適化の長時間探索を Unity から中断するトピック（String data="cancel"）。REGISTER_OPTIMIZE §追加要望。
        self.declare_parameter('plan_cancel_topic', '/kmx/plan_cancel')
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
        # ★復帰モードの速度倍率(2026-07-11)：復帰は距離比例再タイム(_densify_retime)で加速度/ジャークを
        #   守っていなかった→登録と同じ per-joint double-S(_jerk_retime)で厳守化。この倍率で v/a/j 上限を
        #   一律スケールして計時＝「N% の速さ」で動く（スケール後の上限＝フル上限内なので厳守は保証）。
        #   既定0.25（25%・ゆっくり安全）。1.0 で最速(全上限使用)。ros2 param set で live 調整可。
        self.declare_parameter('return_speed_scale', 0.25)
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
        # ★登録最適化(optimize)の再タイム方式（REGISTER_OPTIMIZE 段階1/1.5）：
        #   'jerk'  （段階1.5・既定）＝純Python double-S(7区間)で per-joint 速度/加速度/ジャーク上限を厳守した最短再タイム。
        #   'scurve'（段階1）      ＝node内 S字(smootherstep)固定形（簡易ジャーク低減・上限は厳守しない）。
        #   'scale'               ＝MoveIt Ruckig 済 base_traj の一様時間スケール（要 MoveIt 側 Ruckig アダプタ）。
        self.declare_parameter('optimize_retime', 'jerk')
        # ★段階1.5 角分割：障害物を縫う折れ線経路の“角”(関節速度の向きが急変する点)は単一 double-S でも
        #   ジャークがスパイク＋一様スケールで経路全体が律速される。_jerk_retime は経路をこの角で分割し、
        #   各サブ経路を rest-to-rest double-S で個別に計時（角ごとに局所減速）。閾値＝隣接方向の変化角。
        self.declare_parameter('jerk_corner_min_deg', 5.0)   # この角度超で分割点＝角（未満は同一直線区間に統合）
        # ★角丸め：発行前に折れ線の角を制約付き Laplacian で丸める試み。ただし「角で停止する区間double-S」とは
        #   相性が悪い（丸めると1つの鋭角が複数の緩いキンク>min_deg に広がり分割＝停止が増える）ため**既定オフ**。
        #   角を速度維持で通すには曲率対応の時間割り当て（Ruckig/3次TOPP）が必要＝今後の改良方針。コードは温存。
        self.declare_parameter('jerk_corner_round', 0)       # 角丸め反復上限(0=無効・既定)。
        self.declare_parameter('jerk_corner_lambda', 0.5)    # 角丸めの強さ(0..1)
        # ★経路短縮（RRT*-Smart の Path Optimization 相当）：発行前に、非隣接ウェイポイント間を
        #   直結できる（間の直線補間が衝突しない）なら中間点を捨てて経路を短くする。衝突判定は
        #   /check_state_validity を経路上だけに使う（attachヘッド＋障害物込み）。冗長な迂回を除去。
        self.declare_parameter('path_shortcut', True)
        # 直線補間の衝突チェック刻み(度)。★安全：粗いとトンネリング／ハグ経路のグレージング（隣接サンプル間で
        #   薄い障害物・爪をすり抜け）。ショートカットが導入する straight をこの刻みで検証＝発行前ゲート解像度に
        #   合わせて細かくしないと擦る straight を採ってしまう。0.6°(≈2cm掃引)＋obstacle_margin_m=2cm で
        #   タイト箱でも発行軌道 衝突0（301点中点込）を実測。offline register 前提の細刻み。
        self.declare_parameter('shortcut_step_deg', 0.6)
        self.declare_parameter('shortcut_output_step_deg', 5.0)   # 出力の再サンプル刻み(度)
        # ★動的ショートカット（Hauser 2010・軌道空間）：min-time 軌道上でランダム2時刻の状態 (q,v,a) を
        #   非ゼロ境界速度の jerk 制限 state-to-state(ローカル Ruckig)で直結→衝突検証して短ければ置換。
        #   クリアランスのある角は自動で丸まり停止が消える／無い角は棄却で停止のまま（安全）。offline register 用。
        #   ★既定オフ(0)：現状は (q,v,a) を有限差分で得ており加速度が不正確→繋ぎ目でジャーク違反(実測1.56)。
        #   軌道が解析的 (q,v,a) を保持する実装に直してから有効化する（次段）。
        self.declare_parameter('dynamic_shortcut_iters', 0)
        self.declare_parameter('state_validity_service', '/check_state_validity')
        # 安全マージン：world 障害物を各面 obstacle_margin_m 膨張。★既定0＝無効。
        #   当初は擦り防止に 2cm 入れたが、cluttered 実シーンでは start/goal 姿勢がヘッドごと障害物の 2cm 以内
        #   にあり、膨張で端点が衝突→GOAL_STATE_INVALID(-27) で計画不能になった（実測）。安全は
        #   「細ショートカット(shortcut_step_deg=0.6°)＋発行前ゲート(_traj_collision_free)」で担保できるため
        #   マージンは既定0。狭所でないシーンで余裕が欲しい時だけ小さく（<最小端点クリアランス）設定する。
        self.declare_parameter('obstacle_margin_m', 0.0)
        # 発行直前に最終軌道の全点＋隣接中点を /check_state_validity で一括検証（validate-what-you-publish）。
        #   衝突が残れば発行中止（failed:collision）＝擦る軌道を Unity に流さない。offline register の安全ゲート。
        self.declare_parameter('final_collision_check', True)
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
        # ★登録(optimize)バックエンド：'legacy'(既定・現行 shortcut/raw + _opt_retime)／
        #   'stomp'(②③ 再設計＝pin+coal オラクル上で STOMP-lite で C² 経路最適化→単一 double-S ジャーク制限 retime)。
        #   stomp は失敗/衝突/例外/import不可なら legacy へ自動フォールバック＝安全。復帰(optimize=false)は不変。
        #   詳細は register_redesign/HANDOFF_register_redesign.md。pin/coal/numpy2 は ~/.local（node と共存確認済）。
        self.declare_parameter('register_backend', 'stomp')
        # ★stomp_K＝clamped cubic B-spline 制御点数。小さいほど構造的に滑らか＝低曲率＝retime が速く一貫。
        #   実測(実 clutter・同一base×3seed)：K=6→19s / K=8→21s / K=10→25s / K=12→30s（全て衝突フリー）。
        #   K を上げると狭所を縫う自由度は増すが曲率が増え遅くなる。既定8＝速度と threading の均衡。
        self.declare_parameter('stomp_K', 8)
        self.declare_parameter('stomp_M', 60)             # コスト評価サンプル数
        self.declare_parameter('stomp_d_safe', 0.03)      # clearance ソフト帯(m)
        self.declare_parameter('stomp_rollouts', 20)      # STOMP rollout 数/反復
        self.declare_parameter('stomp_budget_sec', 8.0)   # 最適化の時間予算(anytime・cancel対応)
        self.declare_parameter('stomp_dense_n', 1500)     # retime へ渡す密サンプル数(≥600＝頂点artifact回避)
        self.declare_parameter('stomp_clearance', 'margin')   # 'margin'(高速量子化)/'exact'(厳密・低速)
        self.declare_parameter('stomp_w_clear', 25.0)     # コスト重み: clearance 不足²
        self.declare_parameter('stomp_w_length', 1.0)     #             経路長
        self.declare_parameter('stomp_w_smooth', 3.0)     #             関節加速度²
        self.declare_parameter('stomp_w_grav', 1.0)       #             重力トルク(g/effort)²
        self.declare_parameter('stomp_w_tip', 2.0)        #             先端Cartesian加速度²
        # ★登録の候補ベース数：BITstar は run 毎に別ホモトピー/長さの経路を返す。最短1本に賭けず
        #   上位 N 本を保持し、register 発行時に「短い順に STOMP 最適化→発行前ゲート」を試し、最初に
        #   衝突フリーで通った経路を発行する（＝最短でなく“衝突しない中で最短”）。1本が縫えなくても
        #   別ホモトピーで通る＝成功率が上がる。オラクル(pin+coal)は1回だけ構築して全候補で使い回す。
        # ★CONSULT4 Tier0(2026-07-12)：候補数 5→10（探索余剰予算を使い "衝突フリーな中で achieved 最小" を
        #   引く確度↑。実測 単一base比 -25%・現行5比 期待-6%＋"遅い外れ"を7倍減〔全候補12s超 13%→2%〕。
        #   計画時間は候補数に比例〔~2分〕。offline register なので許容。速さ優先なら下げる）。
        self.declare_parameter('register_candidates', 10)
        # ホモトピー重複排除しきい（度）：候補経路の arc-length 対応点間 最大距離がこの値未満なら同じ通り道と
        #   みなし重複を捨てる（distinct な通り道だけ STOMP＝無駄削減）。0以下で dedup 無効。
        self.declare_parameter('stomp_dedup_deg', 20.0)
        # STOMP 経路長コストのメトリック：'euclid'(関節距離・既定)/'time'(d_T時間近似)。
        #   ※実測で joint-to-joint 登録では d_T は achieved にほぼ不感（正味変位が端点固定）＝既定 euclid。
        self.declare_parameter('stomp_length_metric', 'euclid')
        # ★登録探索の試行回数上限：optimize は従来「予算(time_budget=600s)いっぱい BITstar を数百回」
        #   呼んでいたが、OMPL BITstar は稀に solve/publishSolution で SIGSEGV し move_group が落ちる
        #   （OMPL 既知バグ・回数に比例して踏む）。候補方式では上位数本あれば十分なので、この回数で
        #   探索を打ち切り crash 露出を下げる（＋register も速くなる）。time_budget/cancel より先に効く。
        #   もっと短い base を粘りたいなら増やす。0以下で無制限（従来動作）。
        #   ★既定0＝無制限（BITstar pruning 無効化で crash 根治済みのため回数制限は不要・ユーザー要望2026-07-11）。
        #   BITstar が再びクラッシュするようなら >0 にして露出を抑える保険として残す。
        self.declare_parameter('register_search_attempts', 0)
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
        # 登録最適化の長時間探索の中断（Unity「探索停止」→ 現在の best で確定）。
        self._cancel_requested = False
        self.create_subscription(String, self.get_parameter('plan_cancel_topic').value,
                                 self.on_plan_cancel, 10)
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
        self._last_world_msg = None      # ★最後に受けた障害物/ヘッド（scene 乖離時の再適用用・安全）
        self._last_attached_msg = None
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
                GetPlanningScene, self.get_parameter('get_scene_service').value,
                callback_group=MutuallyExclusiveCallbackGroup())   # sync .call() を別スレッドで安全に
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

    def on_plan_cancel(self, msg):
        """登録最適化の長時間探索を中断（現在の best_traj で確定させる）。Unity「探索停止」ボタン。"""
        self._cancel_requested = True
        self.get_logger().info(f"探索中断要求を受信（data='{getattr(msg, 'data', '')}'）→ 現在の最良経路で確定します。")

    def on_request(self, msg: PlanRequest):
        names = list(msg.names) if msg.names else list(self.kmx_joints)
        start = list(msg.start)
        goal = list(msg.goal)
        # Unity から任意で計画の粘り具合を指定（>0 のときだけ有効。未設定/0 は node 既定）。
        req_budget = float(getattr(msg, 'time_budget', 0.0) or 0.0)
        req_ratio = float(getattr(msg, 'good_ratio', 0.0) or 0.0)
        optimize = bool(getattr(msg, 'optimize', False))
        target_time = float(getattr(msg, 'target_time', 0.0) or 0.0)
        req_speed = float(getattr(msg, 'speed_scale', 0.0) or 0.0)   # 復帰の速度倍率。0以下=node既定
        self.get_logger().info(
            f"plan request: start={start} goal={goal} (deg), joints={names}"
            + (f" time_budget={req_budget}s" if req_budget > 0 else "")
            + (f" good_ratio={req_ratio}" if req_ratio > 0 else "")
            + (f" speed_scale={req_speed}" if (req_speed > 0 and not optimize) else "")
            + (f" ★optimize target_time={target_time if target_time > 0 else '成り行き'}s" if optimize else ""))

        if len(start) != len(names) or len(goal) != len(names):
            self.get_logger().error("names / start / goal の長さが一致しません。")
            self._publish_status("failed:bad_request")
            return

        self._publish_status("planning")   # 計画開始（moveit / 補間 共通）

        # ★登録軌道の多目的最適化（REGISTER_OPTIMIZE_ROS2_SPEC 段階1＋長時間探索/中断）。
        if optimize:
            self._publish_status("opt phase=search iter=0 best=0.00")
            if self.use_moveit:
                # ★安全：move_group respawn で scene が空になっていたらキャッシュ再適用（無障害での誤成功を防ぐ）。
                self._resync_scene_if_diverged()
                # 衝突回避経路を scaling=1 で計画→完了時に _optimize_and_publish で再タイム付け発行。
                self.plan_with_moveit(names, start, goal, req_budget, req_ratio,
                                      optimize=True, target_time=target_time)
            else:
                base = self.plan_interpolate(names, start, goal)
                self._optimize_and_publish(base, target_time, names)
            return

        if self.use_moveit:
            self.plan_with_moveit(names, start, goal, req_budget, req_ratio,
                                  req_speed_scale=req_speed)   # 非同期。完了時に発行＋status。
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
        self._last_world_msg = msg   # scene 乖離時の再適用用にキャッシュ

        scene = PlanningScene()
        scene.is_diff = True

        mrg = float(self.get_parameter('obstacle_margin_m').value)
        new_ids = set()
        for item in msg.items:
            new_ids.add(item.id)
            co = CollisionObject()
            co.header.frame_id = frame
            co.id = item.id
            sp = SolidPrimitive()
            sp.type = int(item.type)            # 1=BOX,2=SPHERE,3=CYLINDER
            dims = [float(d) for d in item.dimensions]
            if mrg > 0.0:                        # ★安全マージン：各面 mrg 膨張（ハグ経路の擦り防止）
                if sp.type == SolidPrimitive.BOX:
                    dims = [d + 2.0 * mrg for d in dims]                      # [x,y,z]
                elif sp.type == SolidPrimitive.SPHERE and dims:
                    dims[0] += mrg                                            # radius
                elif sp.type == SolidPrimitive.CYLINDER and len(dims) >= 2:
                    dims[0] += 2.0 * mrg; dims[1] += mrg                      # [height, radius]
            sp.dimensions = dims
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
        self._last_attached_msg = msg   # scene 乖離時の再適用用にキャッシュ（縮退ガード通過後）

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

    def _resync_scene_if_diverged(self):
        """★安全（Fable 指摘）：move_group が SEGV→respawn すると planning scene が空になり、以降
        「障害物なし」で計画が平然と成功する（無人の登録モードでは致命的）。optimize セッション開始時に
        world 個数を照合し、期待(_obstacle_ids)より少なければ scene 消失とみなし、キャッシュした
        obstacles/attached を再適用する。get_scene 未準備/未送信なら何もしない（非致命）。"""
        if self._get_scene_cli is None or not self._get_scene_cli.service_is_ready():
            return
        expected = len(self._obstacle_ids)
        if expected == 0 and self._last_world_msg is None and self._last_attached_msg is None:
            return   # そもそも何も送られていない
        try:
            req = GetPlanningScene.Request()
            req.components.components = PlanningSceneComponents.WORLD_OBJECT_NAMES
            res = self._get_scene_cli.call(req)
            actual = len({co.id for co in res.scene.world.collision_objects})
        except Exception as e:   # noqa: BLE001
            self.get_logger().warn(f"scene 照合失敗（再適用スキップ）: {e}")
            return
        if actual < expected:
            self.get_logger().warn(
                f"★planning scene 乖離検知: world {actual}個 < 期待 {expected}個"
                "（move_group respawn で scene 消失の疑い）→ キャッシュした障害物/ヘッドを再適用。")
            self._obstacle_ids = set()      # 全 ADD させるため id をリセット
            self._attached_ids = set()
            if self._last_world_msg is not None:
                self.on_obstacles(self._last_world_msg)
            if self._last_attached_msg is not None:
                self.on_attached(self._last_attached_msg)

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
    def plan_with_moveit(self, kmx_names, start_deg, goal_deg, req_budget=0.0, req_ratio=0.0,
                         optimize=False, target_time=0.0, req_speed_scale=0.0):
        """move_group に MoveGroup アクション(plan_only)で joint 目標を投げる（非同期）。
        req_budget/req_ratio が >0 ならその要求だけ node 既定を上書き（Unity から粘り具合を指定）。
        optimize=True（登録最適化）: scaling=1 で衝突回避経路を計画し、完了時に _optimize_and_publish で
        時間充足＋ジャーク低減の再タイム付けをして発行する（REGISTER_OPTIMIZE_ROS2_SPEC 段階1）。"""
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
        # ★optimize は npa=1（ParallelPlan 不使用）。OMPL BITstar×ParallelPlan は use-after-free で SEGV
        #   （ompl#779/#1146）＝optimize の多数回リプランで踏む。逐次リトライループ(_maybe_retry_or_finish)で
        #   best-of-N は担保＝オフラインなら並列8と逐次8は品質等価・クラッシュ0（Fable 助言）。通常計画は既定のまま。
        req.num_planning_attempts = 1 if optimize else int(self.get_parameter('num_planning_attempts').value)
        req.allowed_planning_time = float(self.get_parameter('allowed_planning_time').value)
        # 登録最適化(optimize)は TOTG 限界最短(t_min)を得るため scaling=1。通常は node 既定 scaling。
        req.max_velocity_scaling_factor = 1.0 if optimize else float(self.get_parameter('vel_scale').value)
        req.max_acceleration_scaling_factor = 1.0 if optimize else float(self.get_parameter('acc_scale').value)

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
        if optimize:
            self._cancel_requested = False   # 新しい探索セッション開始時に中断フラグをクリア
        # Unity 指定(req_*)が >0 ならそれを、無ければ node 既定を使う。
        budget = req_budget if req_budget > 0 else float(self.get_parameter('plan_time_budget_sec').value)
        good_ratio = req_ratio if req_ratio > 0 else float(self.get_parameter('plan_good_ratio').value)
        session = {
            'id': self._plan_session,
            'goal': goal,
            'out_names': list(kmx_names[:n]),
            'moveit_names': list(mj),
            'attempts': 0,
            # 登録最適化：回数上限 register_search_attempts で打ち切り（BITstar SIGSEGV 露出を抑制）。
            #   0以下なら従来どおり無制限（time_budget/cancel で確定）。
            'max_attempts': ((int(self.get_parameter('register_search_attempts').value)
                              if int(self.get_parameter('register_search_attempts').value) > 0 else 10**9)
                             if optimize else max(1, int(self.get_parameter('plan_retries').value))),
            'deadline_ns': (self.get_clock().now().nanoseconds + int(budget * 1e9)) if budget > 0 else None,
            'good_ratio': good_ratio,
            'best_traj': None,
            'best_cost': None,
            'candidates': [],   # 登録用: 上位N本 [(cost, traj)]（最短1本に賭けず衝突フリーを探す）
            'best_time': 0.0,   # 現在の最良経路の総時間[秒]（opt phase=search の best= 表示用）
            'last_prog_ns': 0,      # 探索進捗を最後に publish した時刻（スロットル用）
            'last_prog_best': -1.0,  # 最後に publish した best（更新検知用）
            'successes': 0,
            'last_error': 0,   # 直近試行の MoveItErrorCodes.val（最終失敗時の理由に使う）
            'optimize': optimize,       # 登録最適化モード（完了時に再タイム付けして発行）
            'target_time': target_time, # 目標所要時間[秒]（0以下=成り行き）
            'speed_scale': float(req_speed_scale or 0.0),  # 復帰の速度倍率（0以下=node既定 return_speed_scale）

            # 始点→終点の直線関節距離（度）。経路長の下限。大回り判定の基準に使う。
            'direct_cost': math.sqrt(sum((g - s) ** 2 for s, g in zip(start_deg[:n], goal_deg[:n]))),
        }
        self.get_logger().info(
            f"plan session #{session['id']} 開始: planner={req.planner_id} "
            f"max_attempts={session['max_attempts']} budget={budget}s")
        self._send_plan_attempt(session)

    def _send_plan_attempt(self, session):
        session['attempts'] += 1
        # ★登録探索：各試行を送る“前”にハートビート publish（試行中は move_group アクション待ちで
        #   ブロックし publish できない＝1試行が長いと opt 行が途絶え Unity の無進捗 watchdog が誤発火する）。
        #   best 更新 or 2秒経過でスロットル（易しいシーンの氾濫を防ぐ）。長い試行はその直前の1行でカバー。
        if session.get('optimize'):
            now_ns = self.get_clock().now().nanoseconds
            bt = session.get('best_time', 0.0)
            if (now_ns - session.get('last_prog_ns', 0) >= int(2e9)
                    or abs(bt - session.get('last_prog_best', -1.0)) > 1e-6):
                self._publish_status(f"opt phase=search iter={session['attempts']} best={bt:.2f}")
                session['last_prog_ns'] = now_ns
                session['last_prog_best'] = bt
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
                # 上位N候補を cost 昇順で保持（登録：最短1本でなく“衝突しない中で最短”を選ぶため）
                cands = session['candidates']
                cands.append((cost, traj))
                cands.sort(key=lambda c: c[0])
                ncand = max(1, int(self.get_parameter('register_candidates').value))
                del cands[ncand:]
                if session['best_cost'] is None or cost < session['best_cost']:
                    session['best_traj'] = traj
                    session['best_cost'] = cost
                    last = traj.points[-1].time_from_start   # 最良経路の総時間[秒]（search 進捗の best=）
                    session['best_time'] = last.sec + last.nanosec * 1e-9
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
        # ★登録最適化(optimize)は「予算いっぱい探索し続ける」ので good_enough では止めない（中断/予算/回数で確定）。
        ratio = session['good_ratio']
        good_enough = (not session.get('optimize')
                       and session['best_cost'] is not None and ratio > 0.0
                       and session['direct_cost'] > 1e-6
                       and session['best_cost'] <= ratio * session['direct_cost'])
        cancelled = bool(session.get('optimize')) and bool(self._cancel_requested)
        if not good_enough and within_attempts and within_time and not cancelled:
            # 進捗 publish は _send_plan_attempt 冒頭のハートビート（試行の直前）に集約した。
            self._send_plan_attempt(session)   # 失敗はリトライ／大回りしか無いならより短い通り道を探し続ける
            return
        # 予算/回数を使い切った → 最良経路を発行
        if session['best_traj'] is not None:
            raw_cost = session['best_cost']
            # 登録最適化：生の best_traj を渡す（ショートカット＋生経路フォールバック＋発行前ゲートは
            #   _optimize_and_publish 内で行う＝擦る軌道を出さず、擦ったら生の move_group 経路で発行）。
            if session.get('optimize'):
                cand_trajs = [t for _, t in session.get('candidates', [])] or [session['best_traj']]
                self._optimize_and_publish(cand_trajs,
                                           float(session.get('target_time', 0.0)),
                                           session['moveit_names'])
                return
            # 復帰計画：経路を短縮→ per-joint double-S(_jerk_retime) で速度倍率 return_speed_scale で計時。
            #   ★速度/加速度/ジャークを厳守（旧 _densify_retime の距離比例＝一定速で角/端点の加速度が上限
            #   超過し得た問題を解消）。角では一旦停止＝復帰の「角停止OK」方針とも整合。geometry は短縮経路のまま
            #   （jerk_corner_round=0 既定で角丸め無し＝衝突安全）。scale=1.0 で最速、既定0.25で25%速。
            traj0 = session['best_traj']
            mn = session['moveit_names']
            keep = self._shortcut_keep(traj0, mn) if bool(self.get_parameter('path_shortcut').value) else None
            path = keep if keep is not None else [list(p.positions) for p in traj0.points]
            # 速度倍率：Unity 要求(session['speed_scale'])>0 ならそれ、無ければ node 既定 return_speed_scale。
            rscale = float(session.get('speed_scale', 0.0) or 0.0)
            if rscale <= 0.0:
                rscale = float(self.get_parameter('return_speed_scale').value)
            traj, _tmin, _ach, _feas = self._jerk_retime(
                path, traj0.joint_names, 0.0, moveit_names=mn, scale=rscale)
            post = self._traj_cost(traj)
            direct = max(session['direct_cost'], 1e-6)
            self.pub.publish(traj)
            self._publish_status(f"succeeded:{len(traj.points)}:{post / direct:.2f}")
            self.get_logger().info(
                f"published best trajectory (復帰・jerk厳守 scale={rscale}): {len(traj.points)}点 "
                f"総時間={_tmin:.2f}s cost {raw_cost:.1f}→{post:.1f} 直線={session['direct_cost']:.1f}"
                f"[{post / direct:.1f}倍] {session['successes']}/{session['attempts']} 成功")
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
    def _shortcut_keep(self, traj, moveit_names):
        """発行前の経路短縮の“節点列(keep, deg)”を返す。非隣接点を直結できるなら中間を捨てる（貪欲）。
        衝突判定は /check_state_validity を経路上の補間点だけに使う（attachヘッド＋障害物込み）。
        sv 未準備 or 点少なら None（呼び側は元経路を使う）。"""
        if self._sv_cli is None or not self._sv_cli.service_is_ready():
            return None
        pts = [list(p.positions) for p in traj.points]   # deg, out_names(J1..J6)順
        if len(pts) < 3:
            return None
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
        self.get_logger().info(f"  経路短縮: {len(pts)}点→{len(keep)}節 (衝突チェック{nchk[0]}回)")
        return keep

    def _shortcut_traj(self, traj, moveit_names):
        """短縮＋距離比例再タイム（旧経路。登録legacy の候補生成等で使用）。keep 無しなら元経路。"""
        keep = self._shortcut_keep(traj, moveit_names)
        if keep is None:
            return traj
        return self._densify_retime(keep, traj)

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

    def _traj_collision_free(self, traj, moveit_names):
        """★発行直前ゲート（validate-what-you-publish）：発行する離散軌道そのものの全点＋隣接中点を
        /check_state_validity で検証。全点衝突無しなら True。無効点があれば False（＝擦る軌道は発行しない）。
        sv 未準備 or final_collision_check=false なら検証せず True（従来動作を壊さない）。"""
        if not bool(self.get_parameter('final_collision_check').value):
            return True
        if self._sv_cli is None or not self._sv_cli.service_is_ready():
            return True
        pts = [list(p.positions) for p in traj.points]
        n = 0
        for i, q in enumerate(pts):
            n += 1
            if not self._state_valid(q, moveit_names):
                self.get_logger().error(f"★発行前検証：点{i}/{len(pts)} が衝突。")
                return False
            if i < len(pts) - 1:
                mid = [(a + b) * 0.5 for a, b in zip(q, pts[i + 1])]
                n += 1
                if not self._state_valid(mid, moveit_names):
                    self.get_logger().error(f"★発行前検証：点{i}-{i + 1} 中点が衝突。")
                    return False
        self.get_logger().info(f"  発行前検証: 全{n}点(中点込) 衝突なし ✓")
        return True

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

    def _densify_traj(self, traj, max_step_deg):
        """★発行前密化（validate-what-you-publish 完成）：隣接点の最大関節差が max_step_deg 以下になるよう
        点を挿入（位置/時刻は線形補間）。Unity は点間を線形再生するので、粗いと弦が経路の角を切って障害物に
        食い込む（＝生 move_group 経路でも擦る主因）。密化で「発行する離散点列そのもの」を経路に沿わせる。"""
        pts = traj.points
        if len(pts) < 2 or max_step_deg <= 0:
            return traj
        def tf(p):
            return p.time_from_start.sec + p.time_from_start.nanosec * 1e-9
        out = JointTrajectory()
        out.joint_names = list(traj.joint_names)
        first = JointTrajectoryPoint()
        first.positions = list(pts[0].positions)
        first.time_from_start = pts[0].time_from_start
        out.points.append(first)
        for a, b in zip(pts, pts[1:]):
            qa, qb = a.positions, b.positions
            ta, tb = tf(a), tf(b)
            dmax = max((abs(y - x) for x, y in zip(qa, qb)), default=0.0)
            n = max(1, int(math.ceil(dmax / max_step_deg)))
            for k in range(1, n + 1):
                w = k / n
                q = [x + (y - x) * w for x, y in zip(qa, qb)]
                t = ta + (tb - ta) * w
                p = JointTrajectoryPoint()
                p.positions = [float(v) for v in q]
                p.time_from_start = Duration(sec=int(t), nanosec=int(round((t - int(t)) * 1e9)))
                out.points.append(p)
        return out

    # ============================ 登録軌道の多目的最適化（REGISTER_OPTIMIZE_ROS2_SPEC 段階1）
    def _opt_retime(self, base_traj, target_time, mn):
        """1本の衝突回避経路 base_traj を再タイム付けして (opt, t_min, achieved, feasible) を返す。
        mode='jerk'＝区間double-S＋動的SC（既定）／'scurve','scale'＝段階1。発行/ゲートは呼び側。"""
        pts = base_traj.points
        path = [list(p.positions) for p in pts]
        mode = str(self.get_parameter('optimize_retime').value)
        if mode == 'jerk':
            last = pts[-1].time_from_start
            t_totg = last.sec + last.nanosec * 1e-9
            opt, t_min, _, _ = self._jerk_retime(
                path, base_traj.joint_names, 0.0, floor_time=t_totg, moveit_names=mn)
            opt, t_min = self._dynamic_shortcut(opt, mn)
            feasible = (target_time <= 0.0) or (target_time >= t_min - 1e-6)
            achieved = t_min if (target_time <= 0.0 or not feasible) else float(target_time)
            if abs(achieved - t_min) > 1e-6 and t_min > 1e-9:
                opt = self._time_scale_traj(opt, achieved / t_min)
        else:
            last = pts[-1].time_from_start
            t_min = last.sec + last.nanosec * 1e-9
            if t_min <= 1e-6:
                t_min = float(self.get_parameter('duration_sec').value)
            feasible = (target_time <= 0.0) or (target_time >= t_min)
            achieved = t_min if (target_time <= 0.0 or not feasible) else target_time
            opt = (self._scale_retime(base_traj, achieved) if mode == 'scale'
                   else self._smooth_retime(path, base_traj.joint_names, achieved))
            # 段階1(scurve/scale)は出力が粗いことがあるので密化（jerk は _jerk_retime 内で経路上密サンプル済）。
            opt = self._densify_traj(opt, float(self.get_parameter('shortcut_step_deg').value))
        return opt, t_min, achieved, feasible

    def _optimize_and_publish(self, candidates, target_time, moveit_names=None):
        """登録軌道を発行。candidates＝候補ベース列（短い順・単一 traj も可）。
        register_backend='stomp'：pin+coal オラクルを1回だけ構築し、候補を短い順に
        STOMP→③double-S retime→発行前ゲートに掛け、**最初に衝突フリーで通った経路を発行**
        （＝最短でなく“衝突しない中で最短”）。全候補が未発行なら legacy へフォールバック。
        validate-what-you-publish（発行前に離散軌道を service で検証）は各候補で維持。"""
        if not isinstance(candidates, (list, tuple)):
            candidates = [candidates]
        cands = [c for c in candidates if c is not None and c.points]
        if not cands:
            self._publish_status("failed:no_solution")
            return
        mn = (moveit_names or list(cands[0].joint_names))
        # ★Tier0(CONSULT4): ホモトピー重複排除＝distinct な通り道だけ残す（d_T 昇順）。BITstar は同じ通り道を
        #   何本も返し得るので、重複を STOMP する無駄を省く（多様性も確保）。失敗時は元のまま。
        cands = self._dedup_candidates(cands, mn)
        # ★cancel は「探索停止」用。ここ（最適化/発行フェーズ）に入った時点で探索は終わっているので
        #   フラグを clear し、候補の STOMP 最適化を必ず走らせる。旧: cancel が STOMP まで残ると
        #   should_cancel が即 True→0 反復→infeasible シードのまま全候補「解なし」→failed:collision だった。
        #   「探索停止＝現在の best を最適化して確定」の意図に合わせる（最適化中の新規 cancel は別途有効）。
        self._cancel_requested = False
        if str(self.get_parameter('register_backend').value) == 'stomp':
            S = None
            try:
                S = self._build_stomp_oracle()
            except Exception as e:   # noqa: BLE001
                self.get_logger().warn(f"★登録[stomp] オラクル構築例外({e})→legacy へ。")
            if S is not None:
                # ★全候補を STOMP＋③retime まで処理し、衝突フリーな中から「最終実行時間(achieved)が最小」を採用。
                #   関節距離が最短でも、角が多いと減速/曲率律速で最終時間が伸びる＝距離でなく“実行が速い”で選ぶ。
                #   achieved 同値(target_time クランプ)なら min_time→jerk をタイブレーク。
                results = []
                for ci, base in enumerate(cands):
                    tag = f"cand{ci + 1}/{len(cands)}"
                    try:
                        r = self._stomp_build(S, base, target_time, mn, tag,
                                              cand_i=ci, n_cands=len(cands))
                    except Exception as e:   # noqa: BLE001
                        self.get_logger().warn(f"★登録[stomp] {tag} 例外({e})→次候補。")
                        r = None
                    if r is not None:
                        results.append(r)
                if results:
                    best_r = min(results, key=lambda r: (round(r['achieved'], 3),
                                                         round(r['min_time'], 3), r['jerk']))
                    self.get_logger().info(
                        f"★登録[stomp] 衝突フリー {len(results)}/{len(cands)} 候補→最終時間 最小の "
                        f"[{best_r['tag']}] 採用 (achieved={best_r['achieved']:.2f}s・"
                        f"各候補={[round(r['achieved'], 1) for r in results]})")
                    self._publish_stomp_result(best_r)
                    return
                self.get_logger().warn(
                    f"★登録[stomp] 全{len(cands)}候補で未発行→legacy へフォールバック。")
        self._optimize_and_publish_legacy(cands[0], target_time, mn)

    def _publish_stomp_result(self, r):
        """勝者候補（最終時間 最小）の軌道を発行＋ plan_status。"""
        out = r['out']
        self._publish_status(f"opt phase=stomp {r['tag']} time={r['achieved']:.3f} prog=90")
        self.pub.publish(out)
        self._publish_status(f"succeeded:{len(out.points)}:{r['ratio']:.2f}")
        self._publish_status(f"opt done time={r['achieved']:.3f} feasible={1 if r['feasible'] else 0} "
                             f"min_time={r['min_time']:.3f} jerk={r['jerk']:.2f}")
        self.get_logger().info(
            f"★登録[stomp] 発行({r['tag']}): {len(out.points)}点 achieved={r['achieved']:.3f}s "
            f"feasible={r['feasible']}(min_time={r['min_time']:.3f}s) jerk_max={r['jerk']:.1f}deg/s^3 "
            f"len {r['it0']:.3f}->{r['itb']:.3f}rad")

    def _optimize_and_publish_legacy(self, base_traj_raw, target_time, mn):
        """legacy 登録：ショートカット/生 move_group 経路を区間double-S で再タイム→発行前ゲート→発行。
        （register_backend='legacy'、または stomp 全候補が未発行のときのフォールバック）。"""
        use_sc = bool(self.get_parameter('path_shortcut').value) and \
            self._sv_cli is not None and self._sv_cli.service_is_ready()
        cands = []
        if use_sc:
            cands.append(('shortcut', self._shortcut_traj(base_traj_raw, mn)))
        cands.append(('raw', base_traj_raw))   # フォールバック：move_group 経路そのもの
        for label, cand in cands:
            if not cand.points:
                continue
            opt, t_min, achieved, feasible = self._opt_retime(cand, target_time, mn)
            if not self._traj_collision_free(opt, mn):
                self.get_logger().warn(
                    f"★登録[legacy]：候補[{label}]の発行軌道が衝突→次候補へ。")
                continue
            jerk = self._jerk_metric(opt)
            self._publish_status(f"opt phase=jerk iter=1 time={achieved:.3f} prog=80")
            self.pub.publish(opt)
            path0 = [list(cand.points[0].positions), list(cand.points[-1].positions)]
            direct = math.sqrt(sum((b - a) ** 2 for a, b in zip(path0[0], path0[1])))
            ratio = (self._traj_cost(opt) / direct) if direct > 1e-6 else 1.0
            self._publish_status(f"succeeded:{len(opt.points)}:{ratio:.2f}")
            self._publish_status(
                f"opt done time={achieved:.3f} feasible={1 if feasible else 0} "
                f"min_time={t_min:.3f} jerk={jerk:.2f}")
            self.get_logger().info(
                f"★登録[legacy] 発行([{label}]): {len(opt.points)}点 achieved={achieved:.3f}s "
                f"feasible={feasible}(min_time={t_min:.3f}s) jerk_max={jerk:.1f}deg/s^3"
                + ("" if feasible else " ※target_time 達成不能→最短で出力")
                + ("" if label == 'shortcut' else " ※ショートカット版が擦ったので生経路で発行"))
            return
        self.get_logger().error("★登録：全候補の発行軌道が衝突。発行中止（failed:collision）。")
        self._publish_status("failed:collision")

    # ================= 登録バックエンド stomp（②③ 再設計・register_backend='stomp'）=================
    def _register_paths(self):
        """pin+coal オラクル用の (urdf, srdf, pkg) を解決（キャッシュ）。"""
        if getattr(self, '_reg_paths', None) is not None:
            return self._reg_paths
        from ament_index_python.packages import get_package_share_directory
        urdf = os.path.join(get_package_share_directory('kmx_planner'),
                            'register', 'crx30ia_clean.urdf')
        srdf = os.path.join(get_package_share_directory('fanuc_moveit_config'),
                            'srdf', 'crx30ia.srdf')
        pkg = [os.path.dirname(get_package_share_directory('fanuc_crx_description'))]
        self._reg_paths = (urdf, srdf, pkg)
        return self._reg_paths

    def _get_full_scene(self):
        """GetPlanningScene(world geometry + attached objects) を同期取得。scene or None。"""
        if self._get_scene_cli is None or not self._get_scene_cli.service_is_ready():
            return None
        try:
            req = GetPlanningScene.Request()
            req.components.components = (PlanningSceneComponents.WORLD_OBJECT_GEOMETRY |
                                         PlanningSceneComponents.ROBOT_STATE_ATTACHED_OBJECTS)
            res = self._get_scene_cli.call(req)
            return res.scene
        except Exception as e:   # noqa: BLE001
            self.get_logger().warn(f"★登録[stomp] GetPlanningScene 失敗: {e}")
            return None

    def _dedup_candidates(self, cands, mn):
        """候補(JointTrajectory・度)をホモトピー重複排除し distinct な代表を d_T 昇順で返す（Tier0）。
        しきい stomp_dedup_deg≤0 or 例外時は元のまま。"""
        thresh = float(self.get_parameter('stomp_dedup_deg').value)
        if thresh <= 0.0 or len(cands) < 2:
            return cands
        try:
            import numpy as np
            from .register.candidates import dedup_homotopies
            lim = self._load_kine_limits()   # name -> (v,a,j) 度
            big = (600.0, 600.0, 6000.0)
            alim_deg = [lim.get(j, big)[1] for j in mn]   # deg/s²（base は度）
            paths = [np.array([[p.positions[k] for k in range(len(mn))] for p in c.points]) for c in cands]
            reps = dedup_homotopies(paths, alim_deg, thresh=thresh)
            out = [cands[i] for i in reps]
            if len(out) < len(cands):
                self.get_logger().info(
                    f"★登録[stomp] ホモトピー重複排除: {len(cands)}→{len(out)}本(distinct・d_T昇順)")
            return out or cands
        except Exception as e:   # noqa: BLE001
            self.get_logger().warn(f"★登録[stomp] dedup 失敗({e})→元候補で続行。")
            return cands

    def _build_stomp_oracle(self):
        """現在の planning scene から pin+coal 衝突/距離オラクル PinScene を構築（候補間で使い回す）。
        None=構築不可（呼び側が legacy へ）。"""
        from .register.pin_scene import PinScene
        scene = self._get_full_scene()
        if scene is None:
            self.get_logger().warn("★登録[stomp] planning scene 取得不可→フォールバック。")
            return None
        urdf, srdf, pkg = self._register_paths()
        try:
            S = PinScene(urdf=urdf, srdf=srdf, pkg=pkg)
            S.sync_from_scene(scene)
            S.finalize()
        except Exception as e:   # noqa: BLE001
            self.get_logger().warn(f"★登録[stomp] PinScene 構築失敗({e})→フォールバック。")
            return None
        self.get_logger().info(f"★登録[stomp] {S.summary()}")
        return S

    def _stomp_build(self, S, base_traj_raw, target_time, mn, tag="", cand_i=0, n_cands=1):
        """1本の候補ベースを STOMP-lite(オラクル S)で C² 最適化→③double-S retime→発行前ゲート。
        発行はせず、結果 dict(out, achieved, min_time, feasible, jerk, ratio, tag, it0, itb) を返す。
        feasible解なし/ゲート衝突は None（呼び側が除外）。呼び側が全候補から最終時間 最小を選び発行する。
        base は度・mn 順。pin は rad・JOINTS(J1..J6) 順で扱い、出力は度・mn 順へ戻す。"""
        import numpy as np
        from .register.pin_scene import JOINTS
        from .register.stomp_lite import StompLite
        from .register.retime import retime_double_s
        pts = base_traj_raw.points
        if len(pts) < 2:
            return None
        d2r = math.pi / 180.0
        # base 幾何(度・mn 順) → rad・JOINTS 順
        try:
            idx = [mn.index(j) for j in JOINTS]
        except ValueError:
            self.get_logger().warn(f"★登録[stomp] 関節名不一致 mn={mn}→スキップ。")
            return None
        base_rad = np.array([[p.positions[i] for i in idx] for p in pts]) * d2r
        start_r, goal_r = base_rad[0].copy(), base_rad[-1].copy()
        w = dict(clear=float(self.get_parameter('stomp_w_clear').value),
                 length=float(self.get_parameter('stomp_w_length').value),
                 smooth=float(self.get_parameter('stomp_w_smooth').value),
                 grav=float(self.get_parameter('stomp_w_grav').value),
                 tip=float(self.get_parameter('stomp_w_tip').value))
        lim = self._load_kine_limits()   # name -> (v,a,j) 度
        big = (600.0, 600.0, 6000.0)
        alim_rad = np.array([lim.get(j, big)[1] for j in JOINTS]) * d2r   # rad/s²（STOMP は rad）
        opt = StompLite(S,
                        K=int(self.get_parameter('stomp_K').value),
                        M=int(self.get_parameter('stomp_M').value),
                        d_safe=float(self.get_parameter('stomp_d_safe').value),
                        clearance=str(self.get_parameter('stomp_clearance').value),
                        weights=w,
                        rollouts=int(self.get_parameter('stomp_rollouts').value),
                        alim=alim_rad,
                        length_metric=str(self.get_parameter('stomp_length_metric').value))
        budget = float(self.get_parameter('stomp_budget_sec').value)
        # ★進捗（RETURN/REGISTER_PROGRESS）：候補 i/N と各 STOMP の経過で全体% を出す（10→95% を候補で按分）。
        #   StompLite が ~1.5s ごとに progress_cb を呼ぶ→ここで throttle 済み plan_status を publish。
        def _prog(pct):
            return int(max(10, min(95, 10 + 85 * pct / max(n_cands, 1))))
        self._publish_status(f"opt phase=stomp {tag} iter=0 prog={_prog(cand_i)}")

        def _pcb(elapsed, bud, feas, it):
            frac = min(1.0, elapsed / max(bud, 1e-3))
            self._publish_status(
                f"opt phase=stomp {tag} iter={it} feasible={1 if feas else 0} prog={_prog(cand_i + frac)}")
        best, info = opt.optimize(start_r, goal_r, base_rad, budget_sec=budget,
                                  should_cancel=lambda: self._cancel_requested, verbose=False,
                                  progress_cb=_pcb)
        if not best.get('feasible'):
            self.get_logger().warn(f"★登録[stomp] {tag} feasible 解なし→除外。")
            return None
        # 密 C² 経路(rad・JOINTS 順) → double-S retime（rad で計時＝standalone 検証と同一・単位一貫）
        n_dense = int(self.get_parameter('stomp_dense_n').value)
        c2 = opt.dense_path(best['Pint'], start_r, goal_r, n=n_dense)   # (n,6) rad
        lim = self._load_kine_limits()   # name -> (v,a,j) deg
        big = (600.0, 600.0, 6000.0)
        vlim = np.array([lim.get(j, big)[0] for j in JOINTS]) * d2r
        alim = np.array([lim.get(j, big)[1] for j in JOINTS]) * d2r
        jlim = np.array([lim.get(j, big)[2] for j in JOINTS]) * d2r
        rt = retime_double_s(c2, vlim, alim, jlim, target_time=float(target_time),
                             step=math.radians(float(self.get_parameter('shortcut_step_deg').value)))
        # JointTrajectory(度・mn 順)を組み立て（rad→deg）
        r2d = 180.0 / math.pi
        out = JointTrajectory()
        out.joint_names = list(mn)
        back = [JOINTS.index(j) for j in mn]
        P, V, A, T = rt['pos'], rt['vel'], rt['acc'], rt['times']
        for k in range(len(P)):
            q = JointTrajectoryPoint()
            q.positions = [float(P[k][back[c]]) * r2d for c in range(len(mn))]
            q.velocities = [float(V[k][back[c]]) * r2d for c in range(len(mn))]
            q.accelerations = [float(A[k][back[c]]) * r2d for c in range(len(mn))]
            tt = float(T[k])
            q.time_from_start = Duration(sec=int(tt), nanosec=int(round((tt - int(tt)) * 1e9)))
            out.points.append(q)
        # validate-what-you-publish（service 最終ゲート）
        if not self._traj_collision_free(out, mn):
            self.get_logger().warn(f"★登録[stomp] {tag} 発行軌道が衝突→除外。")
            return None
        achieved, t_min = rt['achieved'], rt['min_time']
        feasible = (float(target_time) <= 0.0) or (float(target_time) >= t_min - 1e-6)
        jerk = self._jerk_metric(out)
        direct = math.sqrt(sum((b - a) ** 2 for a, b in zip(pts[0].positions, pts[-1].positions)))
        ratio = (self._traj_cost(out) / direct) if direct > 1e-6 else 1.0
        self.get_logger().info(
            f"★登録[stomp] {tag} 候補OK: {len(out.points)}点 achieved={achieved:.3f}s "
            f"min_time={t_min:.3f}s jerk={jerk:.1f}deg/s^3")
        return dict(out=out, achieved=achieved, min_time=t_min, feasible=feasible,
                    jerk=jerk, ratio=ratio, tag=tag,
                    it0=info['init_terms']['length'], itb=best['terms']['length'])

    def _scale_retime(self, base_traj, duration_s):
        """段階1.5：MoveIt が返した base_traj（★Ruckig 平滑化アダプタ有効前提でジャーク制限済）を
        duration_s へ一様時間スケールして返す。位置列はそのまま、time_from_start を k=duration_s/t0 倍。
        jerk は 1/k^3 で増減するので achieved≥t_min（k≥1）なら base_traj のジャーク上限内を維持する。
        base_traj が退化（総時間0/点不足）なら S字にフォールバック。"""
        pts = base_traj.points
        if len(pts) < 2:
            return self._smooth_retime([list(p.positions) for p in pts], base_traj.joint_names, duration_s)
        last = pts[-1].time_from_start
        t0 = last.sec + last.nanosec * 1e-9
        if t0 <= 1e-6:
            return self._smooth_retime([list(p.positions) for p in pts], base_traj.joint_names, duration_s)
        k = max(float(duration_s), 1e-3) / t0
        out = JointTrajectory()
        out.joint_names = list(base_traj.joint_names)
        for p in pts:
            q = JointTrajectoryPoint()
            q.positions = [float(v) for v in p.positions]
            tp = p.time_from_start.sec + p.time_from_start.nanosec * 1e-9
            tt = tp * k
            q.time_from_start = Duration(sec=int(tt), nanosec=int(round((tt - int(tt)) * 1e9)))
            out.points.append(q)
        return out

    def _smooth_retime(self, path, joint_names, duration_s, samples=120):
        """節点列 path(度) を arc-length に沿って S字時間法(smootherstep)で duration_s に再タイム付け。
        端点で速度/加速度/ジャーク=0 の滑らかな単一動作＝関節ジャークを低減する（段階1の簡易ジャーク最適化）。"""
        if len(path) < 2:
            out = JointTrajectory()
            out.joint_names = list(joint_names)
            q = JointTrajectoryPoint()
            q.positions = [float(v) for v in (path[0] if path else [])]
            q.time_from_start = Duration(sec=0, nanosec=0)
            out.points.append(q)
            return out
        # 密化（3度刻み）＋累積関節距離
        dense = [list(path[0])]
        for a, b in zip(path, path[1:]):
            dmax = max((abs(y - x) for x, y in zip(a, b)), default=0.0)
            n = max(1, int(math.ceil(dmax / 3.0)))
            for k in range(1, n + 1):
                t = k / n
                dense.append([x + (y - x) * t for x, y in zip(a, b)])
        cum = [0.0]
        for a, b in zip(dense, dense[1:]):
            cum.append(cum[-1] + math.sqrt(sum((y - x) ** 2 for x, y in zip(a, b))))
        length = cum[-1] if cum[-1] > 1e-9 else 1.0
        dur = max(float(duration_s), 1e-3)

        def interp_at(s):     # arc-length s → config（dense 上を線形補間）
            if s <= 0.0:
                return dense[0]
            if s >= length:
                return dense[-1]
            for i in range(1, len(cum)):
                if cum[i] >= s:
                    w = (s - cum[i - 1]) / max(cum[i] - cum[i - 1], 1e-9)
                    return [x + (y - x) * w for x, y in zip(dense[i - 1], dense[i])]
            return dense[-1]

        out = JointTrajectory()
        out.joint_names = list(joint_names)
        for k in range(samples + 1):
            u = k / samples
            s_law = u * u * u * (u * (u * 6.0 - 15.0) + 10.0)   # smootherstep: 6u^5-15u^4+10u^3
            cfg = interp_at(s_law * length)
            q = JointTrajectoryPoint()
            q.positions = [float(v) for v in cfg]
            tt = dur * u
            q.time_from_start = Duration(sec=int(tt), nanosec=int(round((tt - int(tt)) * 1e9)))
            out.points.append(q)
        return out

    @staticmethod
    def _jerk_metric(traj):
        """関節ジャーク最大値(度/s^3)の概算（有限差分・報告用）。等時間サンプル前提。"""
        pts = traj.points
        if len(pts) < 4:
            return 0.0

        def tf(p):
            return p.time_from_start.sec + p.time_from_start.nanosec * 1e-9
        jmax = 0.0
        ndof = len(pts[0].positions)
        for i in range(len(pts) - 3):
            dt = (tf(pts[i + 3]) - tf(pts[i])) / 3.0
            if dt < 1e-6:
                continue
            inv = 1.0 / (dt ** 3)
            for j in range(ndof):
                p0 = pts[i].positions[j]
                p1 = pts[i + 1].positions[j]
                p2 = pts[i + 2].positions[j]
                p3 = pts[i + 3].positions[j]
                jerk = (p3 - 3.0 * p2 + 3.0 * p1 - p0) * inv
                if abs(jerk) > jmax:
                    jmax = abs(jerk)
        return jmax

    def _load_kine_limits(self):
        """joint_limits.yaml から各関節の (vmax, amax, jmax) を「度」単位で読む（段階1.5・キャッシュ）。
        move_group と同じ config を使い整合させる。読めなければ空dict（呼び側で大きめ既定にフォールバック）。"""
        if getattr(self, '_kine_limits', None) is not None:
            return self._kine_limits
        lim = {}
        try:
            from ament_index_python.packages import get_package_share_directory
            p = os.path.join(get_package_share_directory('fanuc_moveit_config'), 'config', 'joint_limits.yaml')
            y = yaml.safe_load(open(p)) or {}
            r2d = 180.0 / math.pi
            for name, e in (y.get('joint_limits', {}) or {}).items():
                v = float(e.get('max_velocity', 3.14))
                a = float(e.get('max_acceleration', 1.0))
                jk = float(e.get('max_jerk', 0.0)) or (a * 10.0)   # jerk 未定義なら accel×10[rad/s^3]
                lim[name] = (v * r2d, a * r2d, jk * r2d)
            self.get_logger().info(f"段階1.5: joint_limits から vel/acc/jerk 読込 {len(lim)}軸（{p}）")
        except Exception as e:   # noqa: BLE001
            self.get_logger().warn(f"joint_limits.yaml 読込失敗({e}) → 大きめ既定でジャーク制限を代用。")
        self._kine_limits = lim
        return lim

    @staticmethod
    def _jerk_limited_time_law(L, vmax, amax, jmax):
        """rest-to-rest 0→L の double-S（ジャーク制限）時間法。(T, phases) を返す。
        phases: [(t0, dur, s0, v0, a0, jerk), ...]（各区間の開始時刻/長さ/開始位置速度加速度と一定ジャーク）。"""
        L = float(L)
        if L <= 1e-9 or vmax <= 0 or amax <= 0 or jmax <= 0:
            return 0.0, []

        def th_of_v(v):     # 0→v の加速ハーフ所要時間（対称double-Sなので距離=v/2*th）
            if v <= amax * amax / jmax:
                return 2.0 * math.sqrt(v / jmax)
            return amax / jmax + v / amax

        if L >= vmax * th_of_v(vmax):          # vmax に到達（巡航あり）
            vpk = vmax
            tv = (L - vmax * th_of_v(vmax)) / vmax
        else:                                   # vmax 未達
            vtri = (L * math.sqrt(jmax) / 2.0) ** (2.0 / 3.0)   # 三角加速（amax 未到達）仮定
            if vtri <= amax * amax / jmax:
                vpk = vtri
            else:                               # 台形加速（amax 到達・vmax 未達）：v^2/amax + (amax/jmax)v - L = 0
                A = 1.0 / amax
                B = amax / jmax
                vpk = (-B + math.sqrt(B * B + 4.0 * A * L)) / (2.0 * A)
            tv = 0.0
        if vpk <= amax * amax / jmax + 1e-12:
            tj = math.sqrt(vpk / jmax)
            tc = 0.0
        else:
            tj = amax / jmax
            tc = vpk / amax - tj
        segs = [(tj, jmax), (tc, 0.0), (tj, -jmax), (tv, 0.0),
                (tj, -jmax), (tc, 0.0), (tj, jmax)]
        phases = []
        t0 = s0 = v0 = a0 = 0.0
        for dur, jk in segs:
            if dur <= 1e-12:
                continue
            phases.append((t0, dur, s0, v0, a0, jk))
            s0 = s0 + v0 * dur + a0 * dur * dur / 2.0 + jk * dur ** 3 / 6.0
            v0 = v0 + a0 * dur + jk * dur * dur / 2.0
            a0 = a0 + jk * dur
            t0 = t0 + dur
        return t0, phases

    @staticmethod
    def _eval_s(phases, t):
        """time law の位置 s(t)（区間ごとの3次式で評価）。"""
        if not phases:
            return 0.0
        for i, (t0, dur, s0, v0, a0, jk) in enumerate(phases):
            if t < t0 + dur or i == len(phases) - 1:
                tau = max(0.0, min(t - t0, dur))
                return s0 + v0 * tau + a0 * tau * tau / 2.0 + jk * tau ** 3 / 6.0
        return phases[-1][2]

    @staticmethod
    def _eval_sva(phases, t):
        """time law の (s, ṡ, s̈)（弧長・弧長速度・弧長加速度）を返す。解析的 (q,v,a) 算出用。"""
        if not phases:
            return 0.0, 0.0, 0.0
        for i, (t0, dur, s0, v0, a0, jk) in enumerate(phases):
            if t < t0 + dur or i == len(phases) - 1:
                tau = max(0.0, min(t - t0, dur))
                s = s0 + v0 * tau + a0 * tau * tau / 2.0 + jk * tau ** 3 / 6.0
                sd = v0 + a0 * tau + jk * tau * tau / 2.0
                sdd = a0 + jk * tau
                return s, sd, sdd
        return phases[-1][2], 0.0, 0.0

    @staticmethod
    def _max_turn_deg(pts):
        """折れ線の隣接区間ベクトルのなす最大角(度)＝最も鋭い角。丸め要否/収束判定用。"""
        worst = 0.0
        for i in range(1, len(pts) - 1):
            u = [pts[i][d] - pts[i - 1][d] for d in range(len(pts[i]))]
            v = [pts[i + 1][d] - pts[i][d] for d in range(len(pts[i]))]
            nu = math.sqrt(sum(x * x for x in u))
            nv = math.sqrt(sum(x * x for x in v))
            if nu < 1e-9 or nv < 1e-9:
                continue
            c = sum(a * b for a, b in zip(u, v)) / (nu * nv)
            worst = max(worst, math.degrees(math.acos(max(-1.0, min(1.0, c)))))
        return worst

    def _round_corners(self, pts, moveit_names):
        """発行前の角丸め：折れ線の角を制約付き Laplacian（端点固定・`p_i += λ(0.5(p_{i-1}+p_{i+1})−p_i)`）で
        丸め、区間分割＝角での一旦停止を減らす。**各点を個別に衝突検証**（attachヘッド＋障害物）し、
        丸めた位置が valid な点だけ採用＝**余裕のある角は丸め、狭所で丸めきれない角は残す**（＝そこは停止のまま安全）。
        最大角が jerk_corner_min_deg 未満になれば終了。sv 未準備/緩い角なら丸めず原経路。返り値 (pts, 適用反復数)。"""
        iters = int(self.get_parameter('jerk_corner_round').value)
        if (iters <= 0 or len(pts) < 3 or moveit_names is None
                or self._sv_cli is None or not self._sv_cli.service_is_ready()):
            return pts, 0
        lam = float(self.get_parameter('jerk_corner_lambda').value)
        min_deg = float(self.get_parameter('jerk_corner_min_deg').value)
        ndof = len(pts[0])
        cur = [list(p) for p in pts]
        before = self._max_turn_deg(cur)
        applied = 0
        nchk = 0
        for _ in range(iters):
            prev_turn = self._max_turn_deg(cur)
            if prev_turn < min_deg:
                break   # もう鋭い角なし＝分割されない
            trial = [list(p) for p in cur]
            for i in range(1, len(cur) - 1):   # 端点(start/goal)は固定
                cand = [cur[i][d] + lam * (0.5 * (cur[i - 1][d] + cur[i + 1][d]) - cur[i][d])
                        for d in range(ndof)]
                nchk += 1
                if self._state_valid(cand, moveit_names):   # 個別検証＝衝突しない点だけ丸める
                    trial[i] = cand
            # 最も鋭い角が緩まないなら（狭所で丸めきれない）採用しない＝緩い部分を均して曲率を足すのを防ぐ。
            if self._max_turn_deg(trial) > prev_turn - 0.5:
                break
            cur = trial
            applied += 1
        if applied:
            self.get_logger().info(
                f"  段階1.5 角丸め: {applied}反復適用（最大角 {before:.0f}°→{self._max_turn_deg(cur):.0f}°"
                f"・衝突チェック{nchk}回）")
        return cur, applied

    def _ruckig_steer(self, q1, v1, a1, q2, v2, a2, vmax, amax, jmax, pmin, pmax):
        """ローカル Ruckig 単一ターゲット state-to-state（intermediate 不使用＝クラウド行かない）。
        入出力は度。(q,v,a) 境界を満たす jerk 制限時間最適セグメントを返す（Trajectory・rad）。不能は None。"""
        d2r = math.pi / 180.0
        dof = len(q1)
        inp = _RkInput(dof)
        inp.current_position = [x * d2r for x in q1]
        inp.current_velocity = [x * d2r for x in v1]
        inp.current_acceleration = [x * d2r for x in a1]
        inp.target_position = [x * d2r for x in q2]
        inp.target_velocity = [x * d2r for x in v2]
        inp.target_acceleration = [x * d2r for x in a2]
        inp.max_velocity = [x * d2r for x in vmax]
        inp.max_acceleration = [x * d2r for x in amax]
        inp.max_jerk = [x * d2r for x in jmax]
        try:                                   # 関節可動域も渡す（非ゼロ境界はオーバーシュートし得る＝Fable 追2）
            inp.min_position = [x * d2r for x in pmin]
            inp.max_position = [x * d2r for x in pmax]
        except Exception:   # noqa: BLE001
            pass
        try:
            otg = _Ruckig(dof)
            tr = _RkTraj(dof)
            res = otg.calculate(inp, tr)
        except Exception:   # noqa: BLE001
            return None
        if res not in (_RkResult.Working, _RkResult.Finished):
            return None
        return tr

    def _resample_uniform(self, traj, n, total=None):
        """traj を等時間 n+1 点へ再サンプル（線形補間）。total 指定で総時間をそこへ一様スケール。"""
        pts = traj.points
        if len(pts) < 2:
            return traj
        def tf(p):
            return p.time_from_start.sec + p.time_from_start.nanosec * 1e-9
        T = [tf(p) for p in pts]
        Q = [list(p.positions) for p in pts]
        dur = T[-1]
        if dur <= 1e-9:
            return traj
        sc = (float(total) / dur) if total else 1.0
        out = JointTrajectory()
        out.joint_names = list(traj.joint_names)
        j = 0
        for k in range(n + 1):
            t = dur * k / n
            while j < len(T) - 2 and T[j + 1] < t:
                j += 1
            w = min(max((t - T[j]) / max(T[j + 1] - T[j], 1e-9), 0.0), 1.0)
            q = [a + (b - a) * w for a, b in zip(Q[j], Q[j + 1])]
            tt = t * sc
            p = JointTrajectoryPoint()
            p.positions = [float(x) for x in q]
            p.time_from_start = Duration(sec=int(tt), nanosec=int(round((tt - int(tt)) * 1e9)))
            out.points.append(p)
        return out

    def _time_scale_traj(self, traj, k):
        """時間 ×k 一様スケール（位置そのまま・v×1/k・a×1/k²・time×k）。k≥1 で低ジャーク延伸。"""
        out = JointTrajectory()
        out.joint_names = list(traj.joint_names)
        for p in traj.points:
            q = JointTrajectoryPoint()
            q.positions = list(p.positions)
            if len(p.velocities):
                q.velocities = [v / k for v in p.velocities]
            if len(p.accelerations):
                q.accelerations = [a / (k * k) for a in p.accelerations]
            tp = p.time_from_start.sec + p.time_from_start.nanosec * 1e-9
            tt = tp * k
            q.time_from_start = Duration(sec=int(tt), nanosec=int(round((tt - int(tt)) * 1e9)))
            out.points.append(q)
        return out

    def _dynamic_shortcut(self, traj, moveit_names):
        """★動的ショートカット（Hauser 2010・軌道空間）。min-time 軌道 traj(度) を短縮して返す (traj, 総時間s)。
        ランダム2時刻の状態 (q,v,a) をローカル Ruckig で直結→実経路を衝突検証し、短ければ置換（単調短縮）。
        クリアランスのある角は自動で丸まり停止が消える／無い角は棄却で停止のまま（安全側）。"""
        def total_of(t):
            last = t.points[-1].time_from_start
            return last.sec + last.nanosec * 1e-9
        iters = int(self.get_parameter('dynamic_shortcut_iters').value)
        if (not _RUCKIG_OK or iters <= 0 or moveit_names is None
                or self._sv_cli is None or not self._sv_cli.service_is_ready()
                or len(traj.points) < 4):
            return traj, total_of(traj)

        def tf(p):
            return p.time_from_start.sec + p.time_from_start.nanosec * 1e-9
        T = [tf(p) for p in traj.points]
        Q = [list(p.positions) for p in traj.points]      # deg
        dof = len(Q[0])
        # ★点が持つ解析的 v,a を使う（有限差分の加速度はゴミ＝以前の違反原因）。無ければ 0。
        V = [list(p.velocities) if len(p.velocities) == dof else [0.0] * dof for p in traj.points]
        A = [list(p.accelerations) if len(p.accelerations) == dof else [0.0] * dof for p in traj.points]
        names = list(traj.joint_names)
        lim = self._load_kine_limits()
        big = (1e6, 1e6, 1e6)
        vmax = [lim.get(names[j], big)[0] for j in range(dof)]
        amax = [lim.get(names[j], big)[1] for j in range(dof)]
        jmax = [lim.get(names[j], big)[2] for j in range(dof)]
        jl = self._jl(names)                             # (lo,hi) deg（URDF 由来・無ければ±180）
        pmin = [jl[j][0] for j in range(dof)]
        pmax = [jl[j][1] for j in range(dof)]
        maxv = max(vmax) if vmax else 60.0
        step = float(self.get_parameter('shortcut_step_deg').value)
        d2r = math.pi / 180.0

        def idx_at(t):                                   # T[lo] <= t < T[lo+1] の lo（二分）
            if t <= T[0]:
                return 0
            if t >= T[-1]:
                return len(T) - 2
            lo, hi = 0, len(T) - 1
            while hi - lo > 1:
                mid = (lo + hi) // 2
                if T[mid] <= t:
                    lo = mid
                else:
                    hi = mid
            return lo

        def sample(t):                                   # (q,v,a) 度 を線形補間（v,a は解析値の補間＝正確）
            i = idx_at(t)
            w = min(max((t - T[i]) / max(T[i + 1] - T[i], 1e-9), 0.0), 1.0)
            q = [a + (b - a) * w for a, b in zip(Q[i], Q[i + 1])]
            v = [a + (b - a) * w for a, b in zip(V[i], V[i + 1])]
            ac = [a + (b - a) * w for a, b in zip(A[i], A[i + 1])]
            return q, v, ac

        eps = 0.02
        accepted = 0
        nchk = [0]
        for _ in range(iters):
            L = T[-1]
            if L < 0.1 or len(T) < 4:
                break
            t1 = random.uniform(0.0, L)
            t2 = random.uniform(0.0, L)
            if t1 > t2:
                t1, t2 = t2, t1
            if t2 - t1 < max(0.05, 0.03 * L):
                continue
            q1, v1, a1 = sample(t1)
            q2, v2, a2 = sample(t2)
            tr = self._ruckig_steer(q1, v1, a1, q2, v2, a2, vmax, amax, jmax, pmin, pmax)
            if tr is None:
                continue
            d = tr.duration
            if d >= (t2 - t1) - eps * (t2 - t1):
                continue                                 # 十分短くない
            nseg = max(3, min(400, int(math.ceil(d * maxv / max(step, 0.5)))))
            seg = []                                     # (tau, qdeg, vdeg, adeg)
            ok = True
            for k in range(nseg + 1):
                tau = d * k / nseg
                pp, vv, aa = tr.at_time(tau)
                pdeg = [x / d2r for x in pp]
                seg.append((tau, pdeg, [x / d2r for x in vv], [x / d2r for x in aa]))
                nchk[0] += 1
                if not self._state_valid(pdeg, moveit_names):
                    ok = False
                    break
            if not ok:
                continue
            shift = d - (t2 - t1)                         # <0（短縮）
            nT, nQ, nV, nA = [], [], [], []
            for tt, qq, vv, aa in zip(T, Q, V, A):
                if tt < t1 - 1e-6:
                    nT.append(tt); nQ.append(qq); nV.append(vv); nA.append(aa)
            for tau, qq, vv, aa in seg:
                nT.append(t1 + tau); nQ.append(qq); nV.append(vv); nA.append(aa)
            for tt, qq, vv, aa in zip(T, Q, V, A):
                if tt > t2 + 1e-6:
                    nT.append(tt + shift); nQ.append(qq); nV.append(vv); nA.append(aa)
            T2, Q2, V2, A2 = [nT[0]], [nQ[0]], [nV[0]], [nA[0]]     # 時刻単調化
            for tt, qq, vv, aa in zip(nT[1:], nQ[1:], nV[1:], nA[1:]):
                if tt > T2[-1] + 1e-6:
                    T2.append(tt); Q2.append(qq); V2.append(vv); A2.append(aa)
            T, Q, V, A = T2, Q2, V2, A2
            accepted += 1
        out = JointTrajectory()
        out.joint_names = names
        for tt, qq, vv, aa in zip(T, Q, V, A):
            p = JointTrajectoryPoint()
            p.positions = [float(x) for x in qq]
            p.velocities = [float(x) for x in vv]
            p.accelerations = [float(x) for x in aa]
            p.time_from_start = Duration(sec=int(tt), nanosec=int(round((tt - int(tt)) * 1e9)))
            out.points.append(p)
        self.get_logger().info(
            f"  動的ショートカット: {iters}試行/{accepted}採用 "
            f"{total_of(traj):.2f}s→{T[-1]:.2f}s（衝突チェック{nchk[0]}回）")
        return out, T[-1]

    def _jerk_retime(self, path, joint_names, target_time, samples=150, floor_time=0.0,
                     moveit_names=None, scale=1.0):
        """段階1.5：経路 path(度) を joint_limits(vel/acc/jerk) 準拠に再タイム付け。返り値 (traj, min_time, achieved, feasible)。
        ★区間 double-S 方式：経路を“角”(隣接方向変化 > jerk_corner_min_deg)で分割し、各サブ経路を rest-to-rest の
        double-S(7区間ジャーク制限)で計時。角では一旦減速(v=0,a=0)して通過＝**角ごとに局所減速**するので、以前の
        「単一 double-S＋一様スケール」で1つの角が経路全体を律速していた過剰減速を解消。各区間は per-joint ジャーク
        上限を厳守し、境界も a=0 で滑らか。直線1本なら分割されず単一 double-S＝従来挙動(無回帰)。
        min_time=max(Σ区間時間, floor_time)（floor_time=TOTG時間＝物理下限）。target_time≥min_time なら achieved へ
        一様延伸（更に低ジャーク）、未満なら feasible=0＋min_time。geometry は不変＝衝突安全（角丸めは不要に）。"""
        out = JointTrajectory()
        out.joint_names = list(joint_names)
        if len(path) < 2:
            q = JointTrajectoryPoint()
            q.positions = [float(v) for v in (path[0] if path else [])]
            q.time_from_start = Duration(sec=0, nanosec=0)
            out.points.append(q)
            return out, 0.0, max(float(target_time), 0.0), True
        lim = self._load_kine_limits()
        big = (1e6, 1e6, 1e6)
        ndof = len(path[0])
        sc = max(1e-3, float(scale))   # 速度倍率：v/a/j 上限を一律スケール（<1 で遅く・厳守は保たれる）
        limits = [tuple(x * sc for x in (lim.get(joint_names[jdx], big) if jdx < len(joint_names) else big))
                  for jdx in range(ndof)]
        min_turn = float(self.get_parameter('jerk_corner_min_deg').value)

        # ★角丸め（発行前）：折れ線の角を衝突検証つき Laplacian で丸め、角度を緩める。緩んで min_turn 未満に
        #   なった角は下の分割で「角」とみなされず＝一旦停止せず滑らかに通過＝速くなる。狭所で丸めきれない角は
        #   残り従来どおり停止（安全）。geometry を動かすので `/check_state_validity` で必ず衝突再検証する。
        path, _ = self._round_corners(path, moveit_names)

        # 経路を“角”（隣接方向の変化 > min_turn）で分割。角の間はほぼ直線＝1本の rest-to-rest double-S。
        # 角では一旦減速(v=0,a=0)して通過＝各区間が per-joint ジャーク上限を厳守し、区間境界も a=0 で滑らか。
        # 直線1本なら分割されず単一 double-S＝従来挙動（無回帰）。角ごとの局所減速で一様スロー化を回避。
        splits = [0]
        for i in range(1, len(path) - 1):
            u = [path[i][j] - path[i - 1][j] for j in range(ndof)]
            v = [path[i + 1][j] - path[i][j] for j in range(ndof)]
            nu = math.sqrt(sum(x * x for x in u))
            nv = math.sqrt(sum(x * x for x in v))
            if nu < 1e-9 or nv < 1e-9:
                continue
            c = sum(a * b for a, b in zip(u, v)) / (nu * nv)
            if math.degrees(math.acos(max(-1.0, min(1.0, c)))) > min_turn:
                splits.append(i)
        splits.append(len(path) - 1)

        segs = []   # (pts, cum, L, phases, t_seg)
        for a_idx, b_idx in zip(splits, splits[1:]):
            pts = [list(p) for p in path[a_idx:b_idx + 1]]
            cum = [0.0]
            for a, b in zip(pts, pts[1:]):
                cum.append(cum[-1] + math.sqrt(sum((y - x) ** 2 for x, y in zip(a, b))))
            Lk = cum[-1]
            if Lk <= 1e-9:
                continue
            g = [1e-9] * ndof
            for a, b in zip(pts, pts[1:]):
                ds = math.sqrt(sum((y - x) ** 2 for x, y in zip(a, b)))
                if ds <= 1e-9:
                    continue
                for j in range(ndof):
                    g[j] = max(g[j], abs(b[j] - a[j]) / ds)
            vcap = amaxcap = jcap = float('inf')
            for j in range(ndof):
                gj = max(g[j], 1e-9)
                vcap = min(vcap, limits[j][0] / gj)
                amaxcap = min(amaxcap, limits[j][1] / gj)
                jcap = min(jcap, limits[j][2] / gj)
            tk, phases = self._jerk_limited_time_law(Lk, vcap, amaxcap, jcap)
            if tk <= 1e-6 or not phases:
                continue
            segs.append((pts, cum, Lk, phases, tk))
        if not segs:
            q = JointTrajectoryPoint()
            q.positions = [float(v) for v in path[0]]
            q.time_from_start = Duration(sec=0, nanosec=0)
            out.points.append(q)
            return out, 0.0, max(float(target_time), 0.0), True

        t_phys = sum(s[4] for s in segs)

        def sub_interp(pts, cum, Lk, s):
            if s <= 0.0:
                return pts[0]
            if s >= Lk:
                return pts[-1]
            for i in range(1, len(cum)):
                if cum[i] >= s:
                    w = (s - cum[i - 1]) / max(cum[i] - cum[i - 1], 1e-9)
                    return [x + (y - x) * w for x, y in zip(pts[i - 1], pts[i])]
            return pts[-1]

        def sub_interp_tangent(pts, cum, Lk, s):   # 位置＋局所単位接線 dq/ds（弧長で正規化）
            if len(cum) < 2:
                return list(pts[0]), [0.0] * ndof
            s = min(max(s, 0.0), Lk)
            for i in range(1, len(cum)):
                if cum[i] >= s or i == len(cum) - 1:
                    dseg = max(cum[i] - cum[i - 1], 1e-9)
                    w = (s - cum[i - 1]) / dseg
                    pos = [x + (y - x) * w for x, y in zip(pts[i - 1], pts[i])]
                    tan = [(y - x) / dseg for x, y in zip(pts[i - 1], pts[i])]   # |tan|=1（cum=関節弧長）
                    return pos, tan
            return list(pts[-1]), [0.0] * ndof

        def positions_at(total):   # 総時間 total（t_phys を一様スケール）で等時間サンプルした位置列
            sc = total / t_phys if t_phys > 1e-9 else 1.0
            bnds = [0.0]
            for s in segs:
                bnds.append(bnds[-1] + s[4] * sc)
            pos = []
            mm = 0
            for k in range(samples + 1):
                tt = total * k / samples
                while mm < len(segs) - 1 and tt > bnds[mm + 1]:
                    mm += 1
                pts, cum, Lk, phases, tk = segs[mm]
                s = self._eval_s(phases, (tt - bnds[mm]) / sc)   # phases は未スケール tk 基準
                pos.append(sub_interp(pts, cum, Lk, min(max(s, 0.0), Lk)))
            return pos

        def states_at(total, ns):   # 等時間 ns+1 サンプルの (pos, vel, acc)。各点は sub_interp＝polyline 上。
            sc = total / t_phys if t_phys > 1e-9 else 1.0
            bnds = [0.0]
            for s in segs:
                bnds.append(bnds[-1] + s[4] * sc)
            states = []
            mm = 0
            for k in range(ns + 1):
                tt = total * k / ns
                while mm < len(segs) - 1 and tt > bnds[mm + 1]:
                    mm += 1
                pts, cum, Lk, phases, tk = segs[mm]
                s, sd, sdd = self._eval_sva(phases, (tt - bnds[mm]) / sc)
                pos, tan = sub_interp_tangent(pts, cum, Lk, s)
                # 時間スケール sc：実 ṡ=sd/sc, s̈=sdd/sc²。速度/加速度=接線×(ṡ/s̈)。
                vel = [d * sd / sc for d in tan]
                acc = [d * sdd / (sc * sc) for d in tan]
                states.append((pos, vel, acc))
            return states

        # 安全弁：区間 double-S は各区間の g_j(接線)で計時するが、区間内の緩い曲率で実 a/jerk が僅かに超え得る。
        # 出力を実測し超過なら一様に時間延伸（通常は無補正 k≈1）。局所計時が主で、これは最終保証のみ。
        bind_desc = "なし"
        for _ in range(6):
            posv = positions_at(t_phys)
            dt = t_phys / samples
            rv = ra = rj = 0.0
            bv = ba = bj = 0
            for jdx in range(ndof):
                vj, aj, jj = limits[jdx]
                for i in range(len(posv) - 1):
                    val = abs(posv[i + 1][jdx] - posv[i][jdx]) / dt / max(vj, 1e-9)
                    if val > rv:
                        rv, bv = val, jdx
                for i in range(len(posv) - 2):
                    val = abs(posv[i + 2][jdx] - 2.0 * posv[i + 1][jdx] + posv[i][jdx]) \
                        / (dt * dt) / max(aj, 1e-9)
                    if val > ra:
                        ra, ba = val, jdx
                for i in range(len(posv) - 3):
                    val = abs(posv[i + 3][jdx] - 3.0 * posv[i + 2][jdx]
                              + 3.0 * posv[i + 1][jdx] - posv[i][jdx]) / (dt ** 3) / max(jj, 1e-9)
                    if val > rj:
                        rj, bj = val, jdx
            typ, sc_r, bjoint = max((('速度', rv, bv), ('加速度', ra ** 0.5, ba),
                                     ('ジャーク', rj ** (1.0 / 3.0), bj)), key=lambda c: c[1])
            bind_desc = f"{typ} J{bjoint + 1}"
            # 停止判定は「生の超過比」で（時間スケール指数で正規化した比で見ると生で数%超過を見逃す）。
            if max(rv, ra, rj) <= 1.005:
                break
            t_phys = t_phys * sc_r   # 律速拘束を上限へ合わせる時間スケール（v:1, a:½, jerk:⅓ 乗）
        # min_time＝区間 double-S（＋安全弁）が保証する物理最短 t_phys。
        # ※ floor_time（＝ショートカット“前”の base_traj の TOTG 時間）でのクランプは撤去（Fable 助言）。
        #    ショートカットで幾何が短くなった後は floor は過保守な下限になり不要（実測 t_phys<floor で無駄に遅化）。
        #    参考値としてログ表示のみ。同一幾何なら TOTG(jerk無視)≤double-S のはずで、逆転はリタイマのバグ検知に使える。
        t_min = t_phys
        feasible = (target_time <= 0.0) or (target_time >= t_min - 1e-6)
        achieved = t_min if (target_time <= 0.0 or not feasible) else float(target_time)

        # ★発行点列は「経路上に」密サンプル：各点は sub_interp＝polyline 上なので、点数を弧長/ステップから
        #   十分多く取れば、点間の線形補間(Unity 再生)が経路の角を切って障害物に食い込むのを防げる
        #   （＝一様時間150点では fast 区間で頂点をスキップし弦が擦る主因を解消）。×3 は一様時間の粗密ムラ吸収。
        step = float(self.get_parameter('shortcut_step_deg').value)
        L_total = sum(s[2] for s in segs)
        ns_out = max(samples, min(4000, int(math.ceil(L_total / max(step, 0.1) * 3.0))))
        states = states_at(achieved, ns_out)
        for k, (cfg, vel, acc) in enumerate(states):
            t_out = achieved * k / ns_out
            q = JointTrajectoryPoint()
            q.positions = [float(v) for v in cfg]
            q.velocities = [float(v) for v in vel]
            q.accelerations = [float(v) for v in acc]
            q.time_from_start = Duration(sec=int(t_out), nanosec=int(round((t_out - int(t_out)) * 1e9)))
            out.points.append(q)
        self.get_logger().info(
            f"  段階1.5 retime(区間double-S): {len(segs)}区間(角{len(segs) - 1}) "
            f"t_phys={t_phys:.2f}s(律速={bind_desc}) [参考 SC前TOTG={float(floor_time):.2f}s] "
            f"→ min_time={t_min:.2f}s achieved={achieved:.2f}s 出力{len(out.points)}点")
        return out, t_min, achieved, feasible

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
