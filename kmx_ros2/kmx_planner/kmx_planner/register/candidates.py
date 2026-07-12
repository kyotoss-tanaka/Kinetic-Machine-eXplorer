#!/usr/bin/env python3
"""Tier0(CONSULT4) 候補ベースのホモトピー重複排除＋時間近似コスト。

BITstar は同じ通り道(homotopy)を少しずつ違う形で何本も返す。それを全部 STOMP するのは無駄なので、
「異なる通り道」だけに絞る。判定は arc-length 再サンプル後の対応点間 最大距離（近ければ同ホモトピー）。
各クラスタの代表は d_T(時間近似コスト)最小のものを残す＝distinct な通り道×最速シードだけ STOMP へ。

単位は paths と alim を一致させること（node は度なので alim も度/s² を渡す）。
"""
import numpy as np
try:
    from .bspline import resample_path
except ImportError:
    from bspline import resample_path


def d_t_cost(path, alim):
    """時間近似コスト d_T = Σ_i max_j √(|Δq_ij|/a_j)（加速度律速の総所要∝これ）。"""
    dq = np.abs(np.diff(np.asarray(path, float), axis=0))
    return float(np.sum(np.max(np.sqrt(dq / np.asarray(alim, float)), axis=1)))


def dedup_homotopies(paths, alim, thresh, n=30):
    """distinct な通り道の代表インデックスを d_T 昇順で返す。
    paths: list[(Ni,dof)]（同単位）。thresh: 同ホモトピー判定の対応点間 最大距離しきい（paths と同単位）。"""
    if not paths:
        return []
    R = [resample_path(np.asarray(p, float), n) for p in paths]
    costs = [d_t_cost(p, alim) for p in paths]
    order = list(np.argsort(costs))          # d_T 最小(=最速)から
    kept = []
    for i in order:
        if all(np.max(np.linalg.norm(R[i] - R[j], axis=1)) >= thresh for j in kept):
            kept.append(i)
    return kept                              # distinct homotopy・d_T 昇順


if __name__ == "__main__":
    # self-test: 同じ通り道の微差2本＋別の通り道1本 → distinct=2
    a = np.linspace([0, 0, 0, 0, 0, 0], [90, 30, -20, 0, 40, 0], 20)
    b = a + np.random.default_rng(0).normal(0, 1.0, a.shape)   # 微差(同homotopy)
    c = np.linspace([0, 0, 0, 0, 0, 0], [90, -30, 20, 0, 40, 0], 20)  # J2/J3逆(別homotopy)
    alim = [22.9, 22.9, 22.9, 57.3, 57.3, 57.3]   # deg/s²
    reps = dedup_homotopies([a, b, c], alim, thresh=15.0)
    print("distinct reps:", reps, " (期待: 2本＝aかb と c)")
    print("d_T:", [round(d_t_cost(p, alim), 2) for p in [a, b, c]])
