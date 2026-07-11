#!/usr/bin/env python3
"""Clamped cubic B-spline in joint space, self-implemented (no scipy — system scipy
is ABI-incompatible with pin's numpy 2.x). Used by STOMP-lite (②).

A clamped cubic B-spline with K control points guarantees C² continuity by
construction, and q(0)=P[0], q(1)=P[K-1] (endpoints = first/last control point).
Evaluation is LINEAR in the control points: Q = B @ P, where B (M×K) is the basis
matrix at M sample parameters. That linearity keeps STOMP noise/updates clean.
"""
import numpy as np

DEG = 3


def clamped_knots(K, p=DEG):
    """Knot vector for a clamped B-spline: p+1 zeros, K-p-1 interior, p+1 ones."""
    assert K >= p + 1, f"need K>={p+1} control points"
    n_interior = K - p - 1
    interior = (np.arange(1, n_interior + 1) / (n_interior + 1)) if n_interior > 0 else np.array([])
    return np.concatenate([np.zeros(p + 1), interior, np.ones(p + 1)])


def _basis_row(u, T, K, p=DEG):
    """Cox–de Boor basis values [N_{0,p}(u)..N_{K-1,p}(u)] at scalar u."""
    N = np.zeros(K)
    # degree 0
    N0 = np.zeros(K)
    if u >= 1.0:                     # clamp right end onto last control point
        N0[K - 1] = 1.0
    else:
        for i in range(K):
            if T[i] <= u < T[i + 1]:
                N0[i] = 1.0
                break
    N = N0
    for d in range(1, p + 1):
        Nd = np.zeros(K)
        for i in range(K):
            a = 0.0
            den1 = T[i + d] - T[i]
            if den1 > 1e-12 and N[i] != 0.0:
                a += (u - T[i]) / den1 * N[i]
            if i + 1 < K:
                den2 = T[i + d + 1] - T[i + 1]
                if den2 > 1e-12 and N[i + 1] != 0.0:
                    a += (T[i + d + 1] - u) / den2 * N[i + 1]
            Nd[i] = a
        N = Nd
    return N


def basis_matrix(u_samples, K, p=DEG):
    """M×K basis matrix so that Q(M,dof) = B @ P(K,dof)."""
    T = clamped_knots(K, p)
    return np.array([_basis_row(u, T, K, p) for u in u_samples])


def fit_control_points(Qbase, K, start=None, goal=None, p=DEG):
    """Least-squares fit K control points so the spline approximates Qbase (N,dof).
    If start/goal given, P[0]=start and P[-1]=goal are pinned and only interior
    control points are solved."""
    Qbase = np.asarray(Qbase, float)
    Npts, dof = Qbase.shape
    u = np.linspace(0.0, 1.0, Npts)
    B = basis_matrix(u, K, p)                       # (N, K)
    if start is None:
        start = Qbase[0]
    if goal is None:
        goal = Qbase[-1]
    # solve interior control points P[1..K-2]
    Bfix = B[:, [0, K - 1]]
    fixed = np.array([start, goal])                 # (2, dof)
    rhs = Qbase - Bfix @ fixed
    Bint = B[:, 1:K - 1]                            # (N, K-2)
    Pint, *_ = np.linalg.lstsq(Bint, rhs, rcond=None)
    P = np.zeros((K, dof))
    P[0] = start; P[K - 1] = goal; P[1:K - 1] = Pint
    return P


def resample_path(path, N):
    """Resample a polyline (list of dof-vectors) to N points by arc length in joint space."""
    P = np.asarray(path, float)
    seg = np.linalg.norm(np.diff(P, axis=0), axis=1)
    s = np.concatenate([[0.0], np.cumsum(seg)])
    if s[-1] < 1e-9:
        return np.repeat(P[:1], N, axis=0)
    su = np.linspace(0.0, s[-1], N)
    return np.array([np.interp(su, s, P[:, j]) for j in range(P.shape[1])]).T


if __name__ == "__main__":
    # self-test: endpoints exact, C² (bounded 3rd diff), fit reproduces a curve
    K = 10
    u = np.linspace(0, 1, 60)
    B = basis_matrix(u, K)
    assert abs(B[0, 0] - 1.0) < 1e-9 and abs(B[-1, K - 1] - 1.0) < 1e-9, "not clamped"
    assert np.allclose(B.sum(axis=1), 1.0, atol=1e-9), "partition of unity failed"
    # fit a wiggly path
    t = np.linspace(0, 1, 40)
    Qb = np.stack([np.sin(3 * t), 0.5 * t, np.cos(2 * t) - 1,
                   0.2 * t, t ** 2, 0 * t], axis=1)
    P = fit_control_points(Qb, K, start=Qb[0], goal=Qb[-1])
    Q = basis_matrix(t, K) @ P
    print("endpoint err start:", np.max(np.abs(Q[0] - Qb[0])),
          " goal:", np.max(np.abs(Q[-1] - Qb[-1])))
    print("fit RMS:", np.sqrt(np.mean((Q - Qb) ** 2)))
    acc = np.diff(basis_matrix(np.linspace(0, 1, 200), K) @ P, 2, axis=0)
    print("max |accel| (smooth if small/continuous):", np.max(np.abs(acc)))
    print("partition-of-unity OK, clamped OK — bspline self-test passed")
