#!/usr/bin/env python3
"""③ jerk-limited re-timing for the register mode's C² B-spline path.

Two time laws over the geometric path (radians, (N,dof)); geometry is NOT changed
(collision-safe). Both honor per-joint (v, a, jerk) limits.

  retime_double_s : single continuous double-S over the whole arc length. Because a
    C² spline has no sharp corners, there is NO corner-splitting and NO interior stop
    (unlike the node's return-mode _jerk_retime which is rest-to-rest per sub-path and
    stops at every >min_turn vertex). Jerk-limited by construction. Conservative: one
    high-|q'| point throttles the whole path (global g_j).
  retime_topp : variable-ṡ acceleration-limited forward-backward TOPP (proper
    a_j = q'_j·s̈ + q''_j·ṡ² coupling). Time-optimal for accel limits; jerk is bounded
    by a post-filter on the arc-accel profile. Faster than double-S where the dominant
    joint changes along the path.

Limits are in the SAME units as the path (pass radians). The node uses degrees at the
interface; convert there. joint_limits.yaml (rad): J1-3 v=2.094/2.094/3.14 a=0.4 j=4,
J4-6 v=3.14 a=1.0 j=10.
"""
import math
import numpy as np


# ---------------- double-S (ported from planner_node._jerk_limited_time_law) ----------------
def double_s_law(L, vmax, amax, jmax):
    """rest-to-rest 0->L double-S. Returns (T, phases) with phases=[(t0,dur,s0,v0,a0,jerk)...]."""
    L = float(L)
    if L <= 1e-9 or vmax <= 0 or amax <= 0 or jmax <= 0:
        return 0.0, []

    def th_of_v(v):
        if v <= amax * amax / jmax:
            return 2.0 * math.sqrt(v / jmax)
        return amax / jmax + v / amax

    if L >= vmax * th_of_v(vmax):
        vpk = vmax
        tv = (L - vmax * th_of_v(vmax)) / vmax
    else:
        vtri = (L * math.sqrt(jmax) / 2.0) ** (2.0 / 3.0)
        if vtri <= amax * amax / jmax:
            vpk = vtri
        else:
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
    segs = [(tj, jmax), (tc, 0.0), (tj, -jmax), (tv, 0.0), (tj, -jmax), (tc, 0.0), (tj, jmax)]
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


def eval_sva(phases, t):
    if not phases:
        return 0.0, 0.0, 0.0
    for i, (t0, dur, s0, v0, a0, jk) in enumerate(phases):
        if t < t0 + dur or i == len(phases) - 1:
            tau = max(0.0, min(t - t0, dur))
            return (s0 + v0 * tau + a0 * tau * tau / 2.0 + jk * tau ** 3 / 6.0,
                    v0 + a0 * tau + jk * tau * tau / 2.0,
                    a0 + jk * tau)
    return phases[-1][2], 0.0, 0.0


# ---------------- shared: arc length + interp ----------------
def _arclen(path):
    P = np.asarray(path, float)
    seg = np.linalg.norm(np.diff(P, axis=0), axis=1)
    cum = np.concatenate([[0.0], np.cumsum(seg)])
    return P, cum


def _interp_pos_tan(P, cum, s):
    L = cum[-1]
    s = min(max(s, 0.0), L)
    i = int(np.searchsorted(cum, s, side="right"))
    i = max(1, min(i, len(cum) - 1))
    dseg = max(cum[i] - cum[i - 1], 1e-12)
    w = (s - cum[i - 1]) / dseg
    pos = P[i - 1] + (P[i] - P[i - 1]) * w
    tan = (P[i] - P[i - 1]) / dseg
    return pos, tan


def sample_states(P, cum, phases, total, ns, dof):
    """Sample ns+1 (pos,vel,acc) at uniform time over [0,total]; time scaled from phases' T."""
    T = phases[-1][0] + phases[-1][1] if phases else 0.0
    sc = total / T if T > 1e-9 else 1.0
    out = []
    for k in range(ns + 1):
        tt = total * k / ns
        s, sd, sdd = eval_sva(phases, tt / sc)
        pos, tan = _interp_pos_tan(P, cum, s)
        out.append((pos, tan * sd / sc, tan * sdd / (sc * sc)))
    return out


# ---------------- primary: single continuous double-S ----------------
def retime_double_s(path, vlim, alim, jlim, target_time=0.0, ns=None, step=None):
    """Single double-S over whole arc. Returns dict(T, times, pos, vel, acc, achieved,
    min_time, bind, gj)."""
    P, cum = _arclen(path)
    L = cum[-1]
    dof = P.shape[1]
    vlim = np.asarray(vlim, float); alim = np.asarray(alim, float); jlim = np.asarray(jlim, float)
    if L <= 1e-9:
        return dict(T=0.0, times=np.array([0.0]), pos=P[:1], vel=P[:1] * 0, acc=P[:1] * 0,
                    achieved=0.0, min_time=0.0, bind="none", gj=np.zeros(dof))
    dq = np.abs(np.diff(P, axis=0))
    ds = np.linalg.norm(np.diff(P, axis=0), axis=1)[:, None]
    g = np.max(dq / np.maximum(ds, 1e-12), axis=0)           # global tangent contribution per joint
    g = np.maximum(g, 1e-9)
    vcap = float(np.min(vlim / g)); acap = float(np.min(alim / g)); jcap = float(np.min(jlim / g))
    T, phases = double_s_law(L, vcap, acap, jcap)
    if step is None:
        step = math.radians(0.6)
    if ns is None:
        ns = int(max(150, min(4000, math.ceil(L / max(step, 1e-3) * 3.0))))

    # safety valve: measure actual v/a/jerk, uniformly stretch if slightly over
    bind = "none"
    for _ in range(6):
        st = sample_states(P, cum, phases, T, ns, dof)
        pos = np.array([s[0] for s in st])
        rv, ra, rj, bind = _worst_ratios(pos, T / ns, vlim, alim, jlim)
        worst = max(rv, ra, rj)
        if worst <= 1.005:
            break
        T *= max(rv, ra ** 0.5, rj ** (1.0 / 3.0))
    min_time = T
    feasible = (target_time <= 0.0) or (target_time >= min_time - 1e-6)
    achieved = min_time if (target_time <= 0.0 or not feasible) else float(target_time)
    st = sample_states(P, cum, phases, achieved, ns, dof)
    times = np.array([achieved * k / ns for k in range(ns + 1)])
    return dict(T=T, times=times, pos=np.array([s[0] for s in st]),
                vel=np.array([s[1] for s in st]), acc=np.array([s[2] for s in st]),
                achieved=achieved, min_time=min_time, feasible=feasible, bind=bind, gj=g)


# NOTE: a variable-ṡ (time-optimal) accel-limited forward-backward TOPP was prototyped
# here but is deferred — it needs jerk post-filtering (research-grade) and the C²
# single double-S is jerk-limited by construction (CONSULT (ii), "確実"). Revisit only
# if measurements show the global double-S is materially slower than return-mode.


# ---------------- measurement ----------------
def _worst_ratios(pos, dt, vlim, alim, jlim):
    dof = pos.shape[1]
    rv = ra = rj = 0.0; bind = "none"
    for j in range(dof):
        v = np.abs(np.diff(pos[:, j])) / dt
        a = np.abs(np.diff(pos[:, j], 2)) / dt ** 2
        jk = np.abs(np.diff(pos[:, j], 3)) / dt ** 3
        if v.size and v.max() / vlim[j] > rv: rv = v.max() / vlim[j];
        if a.size and a.max() / alim[j] > ra: ra = a.max() / alim[j]
        if jk.size and jk.max() / jlim[j] > rj: rj = jk.max() / jlim[j]
    return rv, ra, rj, bind


def measure(times, pos, vlim, alim, jlim):
    """Achieved per-joint peak v/a/jerk (resampled to uniform time) + min interior speed."""
    times = np.asarray(times); pos = np.asarray(pos)
    tu = np.linspace(times[0], times[-1], max(len(times), 200))
    Pu = np.array([np.interp(tu, times, pos[:, j]) for j in range(pos.shape[1])]).T
    dt = tu[1] - tu[0]
    dof = pos.shape[1]
    per = []
    for j in range(dof):
        v = np.abs(np.diff(Pu[:, j])) / dt
        a = np.abs(np.diff(Pu[:, j], 2)) / dt ** 2
        jk = np.abs(np.diff(Pu[:, j], 3)) / dt ** 3
        per.append((v.max(), a.max(), jk.max()))
    # min interior path speed (stop detection): |dq/dt| summed
    speed = np.linalg.norm(np.diff(Pu, axis=0), axis=1) / dt
    interior = speed[len(speed) // 20: -len(speed) // 20] if len(speed) > 40 else speed
    return per, float(np.min(interior)) if len(interior) else 0.0, float(np.max(speed))
