# -*- coding: utf-8 -*-
"""登録 BG-STOMP：worker-safe な純 STOMP 計算＋ProcessPool ワーカ（rclpy ノード非依存）。

なぜ別モジュール：pin.computeCollisions は GIL を解放しない（実測 8スレッドで 1.1x＝並列ゼロ）。
真の並列には別プロセスが要る＝ProcessPoolExecutor(spawn)。別プロセスは pickle 境界なので
compute はモジュール関数、オラクル(pin/coal)はワーカ内で URDF から再構築、scene/base/out は bytes で受け渡す。

- stomp_compute : in-process 純計算（node._stomp_compute が委譲・ThreadPool/同期でも共有）。rclpy 副作用なし。
- process_worker: 別プロセスで oracle 再構築→stomp_compute→out を bytes 化して返す（全て picklable）。
- 別プロセスの pin/coal は完全分離＝スレッド共有 SEGV も原理的に起きない（ThreadPool より安全）。
"""
import math
import numpy as np
from trajectory_msgs.msg import JointTrajectory, JointTrajectoryPoint
from builtin_interfaces.msg import Duration


def traj_cost(traj):
    """経路コスト＝関節空間の総移動量（度）。node._traj_cost と同一（純粋）。"""
    total = 0.0
    pts = traj.points
    for a, b in zip(pts, pts[1:]):
        total += math.sqrt(sum((y - x) ** 2 for x, y in zip(a.positions, b.positions)))
    return total


def jerk_metric(traj):
    """関節ジャーク最大値(度/s^3)の概算。node._jerk_metric と同一（純粋）。"""
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


def stomp_compute(S, base_traj_raw, target_time, mn, params,
                  should_cancel=None, progress_cb=None, tag=""):
    """★worker-safe：StompLite(オラクル S)で C² 最適化→③double-S retime→出力軌道を生成し
    結果 dict(out, achieved, min_time, feasible, jerk, ratio, tag, it0, itb) を返す。
    ★rclpy 副作用なし（get_parameter/publish/service を呼ばない）。base は度・mn 順。
    pin は rad・JOINTS(J1..J6) 順、出力は度・mn 順へ戻す。infeasible は None。"""
    from .pin_scene import JOINTS
    from .stomp_lite import StompLite
    from .retime import retime_double_s
    pts = base_traj_raw.points
    if len(pts) < 2:
        return None
    d2r = math.pi / 180.0
    try:
        idx = [mn.index(j) for j in JOINTS]
    except ValueError:
        return None   # 関節名不一致
    base_rad = np.array([[p.positions[i] for i in idx] for p in pts]) * d2r
    start_r, goal_r = base_rad[0].copy(), base_rad[-1].copy()
    lim = params['lim']
    big = (600.0, 600.0, 6000.0)
    alim_rad = np.array([lim.get(j, big)[1] for j in JOINTS]) * d2r   # rad/s²
    opt = StompLite(S, K=params['K'], M=params['M'], d_safe=params['d_safe'],
                    clearance=params['clearance'], weights=params['w'],
                    rollouts=params['rollouts'], alim=alim_rad,
                    length_metric=params['length_metric'])
    sc = should_cancel if should_cancel is not None else (lambda: False)
    pcb = progress_cb if progress_cb is not None else (lambda *a, **k: None)
    best, info = opt.optimize(start_r, goal_r, base_rad, budget_sec=params['budget'],
                              should_cancel=sc, verbose=False, progress_cb=pcb)
    if not best.get('feasible'):
        return None
    c2 = opt.dense_path(best['Pint'], start_r, goal_r, n=params['dense_n'])   # (n,6) rad
    vlim = np.array([lim.get(j, big)[0] for j in JOINTS]) * d2r
    alim = np.array([lim.get(j, big)[1] for j in JOINTS]) * d2r
    jlim = np.array([lim.get(j, big)[2] for j in JOINTS]) * d2r
    rt = retime_double_s(c2, vlim, alim, jlim, target_time=float(target_time),
                         step=math.radians(params['step_deg']))
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
    achieved, t_min = rt['achieved'], rt['min_time']
    feasible = (float(target_time) <= 0.0) or (float(target_time) >= t_min - 1e-6)
    jerk = jerk_metric(out)
    direct = math.sqrt(sum((b - a) ** 2 for a, b in zip(pts[0].positions, pts[-1].positions)))
    ratio = (traj_cost(out) / direct) if direct > 1e-6 else 1.0
    return dict(out=out, achieved=achieved, min_time=t_min, feasible=feasible,
                jerk=jerk, ratio=ratio, tag=tag,
                it0=info['init_terms']['length'], itb=best['terms']['length'])


# ============================ ProcessPool ワーカ（別プロセス・spawn）============================
_G = {}   # プロセス内グローバル（oracle キャッシュ。1プロセス1 scene）


def _get_oracle(urdf, srdf, pkg, scene_bytes):
    """プロセス内で pin+coal オラクルを1回だけ構築（scene_bytes ごとにキャッシュ・再利用）。
    setup_clearance は StompLite.__init__ が呼ぶのでここでは不要（ThreadPool factory と同条件）。"""
    key = ('oracle', hash(scene_bytes))
    S = _G.get(key)
    if S is not None:
        return S
    from .pin_scene import PinScene
    S = PinScene(urdf=urdf, srdf=srdf, pkg=pkg)
    if scene_bytes:
        from rclpy.serialization import deserialize_message
        from moveit_msgs.msg import PlanningScene
        S.sync_from_scene(deserialize_message(scene_bytes, PlanningScene))
    S.finalize()
    _G.clear()          # 旧 scene の oracle は破棄（メモリ節約）
    _G[key] = S
    return S


def process_worker(payload):
    """★別プロセスで STOMP を計算。payload/戻り値は全て picklable（scene/base/out は bytes）。
    結果 dict(out_bytes, achieved, ...) or None（infeasible/例外）。例外は握る（プロセスを落とさない）。"""
    try:
        from rclpy.serialization import deserialize_message, serialize_message
        S = _get_oracle(payload['urdf'], payload['srdf'], payload['pkg'], payload['scene_bytes'])
        base = deserialize_message(payload['base_bytes'], JointTrajectory)
        r = stomp_compute(S, base, payload['target_time'], payload['mn'],
                          payload['params'], tag=payload['tag'])
        if r is None:
            return None
        out_bytes = serialize_message(r['out'])
        r = dict(r)
        r.pop('out')
        r['out_bytes'] = out_bytes
        return r
    except BaseException:   # noqa: BLE001  例外は握る（SEGV はプロセス層＝この worker だけ壊れる）
        return None
