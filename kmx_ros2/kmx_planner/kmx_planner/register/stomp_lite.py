#!/usr/bin/env python3
"""STOMP-lite (②): stochastic optimization of a clamped-cubic-B-spline path for the
CRX-30iA register mode. Global homotopy comes from BITstar (upstream); this smooths
+ shortens + clears the given seed path while keeping endpoints fixed.

Design (CONSULT3_ANSWER):
  variables = interior B-spline control points (endpoints pinned = start/goal)
  cost      = w_clear·clearance_deficit² + w_len·path_length + w_smooth·joint_accel²
            + w_grav·(gravity_torque/effort)² + w_tip·tip_cartesian_accel²
  update    = STOMP: K correlated-noise rollouts -> soft-min weighted average -> anneal σ
  feasibility (hard) = PinScene margin-0 boolean (parity-validated); soft clearance =
                       quantized nested-margin buckets (fast) or exact distances (slow).
  anytime: keeps best feasible path; honors time budget + cancel callback.

The returned path is dense (for ③ re-timing); publish-time /check_state_validity is
still the final authority (validate-what-you-publish), with BASE as fallback.
"""
import time
import numpy as np
import pinocchio as pin
try:
    from .bspline import basis_matrix, fit_control_points, resample_path
except ImportError:
    from bspline import basis_matrix, fit_control_points, resample_path

DEF_WEIGHTS = dict(clear=25.0, length=1.0, smooth=3.0, grav=1.0, tip=2.0)


class StompLite:
    def __init__(self, scene, *, K=12, M=60, d_safe=0.03,
                 clearance="margin", weights=None,
                 rollouts=20, sigma_deg=2.5, sigma_min_deg=0.4, anneal=0.94, h=10.0,
                 seed=0, alim=None, length_metric="euclid"):
        self.S = scene
        self.model = scene.model
        self.data = scene.model.createData()          # own data (FK/gravity), separate from oracle
        self.K, self.M, self.d_safe = K, M, d_safe
        self.w = dict(DEF_WEIGHTS, **(weights or {}))
        # ★Tier0(CONSULT4)：'length' コストを「時間近似メトリック」に。加速度律速下では区間所要 ∝
        #   max_j √(|Δq_j|/a_j) なので、Euclidean 関節距離でなく "実行時間そのもの" を幾何段で最小化する。
        #   a_j[rad/s²]（高accel関節ほど動かして良い＝手首優遇）。length_metric='euclid' で従来の距離。
        self.length_metric = length_metric
        self.alim = np.asarray(alim if alim is not None else [0.8, 0.8, 0.8, 2.0, 2.0, 2.0], float)
        self.R = rollouts
        self.sigma0 = np.radians(sigma_deg)
        self.sigma_min = np.radians(sigma_min_deg)
        self.anneal = anneal
        self.h = h
        self.rng = np.random.default_rng(seed)
        self.u = np.linspace(0.0, 1.0, M)
        self.B = basis_matrix(self.u, K)              # (M,K)  Q = B @ P
        self.effort = np.asarray(self.model.effortLimit[:6], float)
        self.flange = self.model.getFrameId("flange")
        self.clearance = clearance
        if clearance == "margin":
            scene.setup_clearance(d_safe=d_safe)
        # correlation across control-point index (smooth noise), then B-spline smooths more
        self._corr = self._corr_matrix(K - 2)

    # ---------- geometry ----------
    def _corr_matrix(self, n):
        if n <= 1:
            return np.eye(max(n, 1))
        A = np.zeros((n, n))
        for i in range(n):
            for j in range(n):
                A[i, j] = np.exp(-((i - j) ** 2) / (2 * 1.2 ** 2))
        # normalize rows so variance stays ~sigma^2
        A /= np.linalg.norm(A, axis=1, keepdims=True)
        return A

    def _path(self, Pint, start, goal):
        P = np.empty((self.K, 6))
        P[0], P[-1] = start, goal
        P[1:-1] = Pint
        return self.B @ P                              # (M,6)

    def _tip_pts(self, Q):
        pts = np.empty((len(Q), 3))
        for i, q in enumerate(Q):
            pin.forwardKinematics(self.model, self.data, q)
            pin.updateFramePlacement(self.model, self.data, self.flange)
            pts[i] = self.data.oMf[self.flange].translation
        return pts

    # ---------- cost ----------
    def _clear_feas(self, q):
        if self.clearance == "exact":
            return self.S.clearance_exact(q)
        return self.S.clearance_soft(q)

    def _raw_terms(self, Q):
        clear = 0.0
        nbad = 0
        for q in Q:
            defi, feas = self._clear_feas(q)
            clear += defi * defi
            if not feas:
                nbad += 1
        dq = np.diff(Q, axis=0)
        if self.length_metric == "time":
            # 時間近似メトリック d_T=Σ_i max_j √(|Δq_ij|/a_j)（加速度律速の区間所要∝これ）
            length = float(np.sum(np.max(np.sqrt(np.abs(dq) / self.alim), axis=1)))
        else:
            length = float(np.sum(np.linalg.norm(dq, axis=1)))   # 従来 Euclidean 関節距離
        acc = np.diff(Q, 2, axis=0)
        smooth = float(np.sum(acc ** 2))
        grav = 0.0
        for q in Q:
            g = pin.computeGeneralizedGravity(self.model, self.data, q)
            grav += float(np.sum((g / self.effort) ** 2))
        pts = self._tip_pts(Q)
        tip = float(np.sum(np.diff(pts, 2, axis=0) ** 2))
        return dict(clear=clear, length=length, smooth=smooth, grav=grav, tip=tip), (nbad == 0), nbad

    def _scalarize(self, terms, nbad):
        s = 0.0
        for k, v in terms.items():
            s += self.w[k] * v / self._norm[k]
        s += 1e4 * nbad                                # dominant infeasibility penalty
        return s

    # ---------- optimize ----------
    def optimize(self, start, goal, base_path, *, budget_sec=8.0, max_iter=200,
                 stall_iter=25, should_cancel=None, verbose=True, progress_cb=None):
        start = np.asarray(start, float)
        goal = np.asarray(goal, float)
        base = resample_path(base_path, max(self.M, 40))
        P0 = fit_control_points(base, self.K, start=start, goal=goal)
        Pint = P0[1:-1].copy()

        Q0 = self._path(Pint, start, goal)
        terms0, feas0, nbad0 = self._raw_terms(Q0)
        self._norm = {k: max(v, 1e-6) for k, v in terms0.items()}
        self._norm["clear"] = max(terms0["clear"], self.d_safe ** 2)   # avoid 0-div when seed clear
        s0 = self._scalarize(terms0, nbad0)
        best = dict(Pint=Pint.copy(), score=s0, terms=terms0, feasible=feas0, Q=Q0)
        if verbose:
            print(f"[init] score={s0:.3f} feasible={feas0} nbad={nbad0} "
                  f"len={terms0['length']:.3f} smooth={terms0['smooth']:.2f} "
                  f"grav={terms0['grav']:.3f} tip={terms0['tip']:.4f} clear={terms0['clear']:.5f}")

        sigma = self.sigma0
        cur = Pint.copy()
        cur_score = s0
        t0 = time.time()
        last_improve = 0
        last_cb = 0.0
        it = 0
        while it < max_iter and (time.time() - t0) < budget_sec:
            if should_cancel is not None and should_cancel():
                if verbose: print(f"[cancel] iter={it}")
                break
            eps = np.empty((self.R, self.K - 2, 6))
            scores = np.empty(self.R)
            for r in range(self.R):
                raw = self.rng.normal(0.0, sigma, size=(self.K - 2, 6))
                e = self._corr @ raw                     # smooth-correlated noise
                eps[r] = e
                Qr = self._path(cur + e, start, goal)
                tr, feasr, nbadr = self._raw_terms(Qr)
                scores[r] = self._scalarize(tr, nbadr)
            smin, smax = scores.min(), scores.max()
            denom = (smax - smin) if (smax - smin) > 1e-9 else 1.0
            wts = np.exp(-self.h * (scores - smin) / denom)
            wts /= wts.sum()
            delta = np.tensordot(wts, eps, axes=(0, 0))  # (K-2,6)
            cur = cur + delta
            Qc = self._path(cur, start, goal)
            tc, feasc, nbadc = self._raw_terms(Qc)
            cur_score = self._scalarize(tc, nbadc)
            if feasc and cur_score < best["score"] - 1e-6:
                best = dict(Pint=cur.copy(), score=cur_score, terms=tc, feasible=True, Q=Qc)
                last_improve = it
            sigma = max(self.sigma_min, sigma * self.anneal)
            it += 1
            if progress_cb is not None and (time.time() - last_cb) > 1.5:
                last_cb = time.time()
                try:
                    progress_cb(time.time() - t0, budget_sec, bool(best["feasible"]), it)
                except Exception:
                    pass
            if verbose and it % 10 == 0:
                print(f"  it={it:3d} score={cur_score:.3f} best={best['score']:.3f} "
                      f"σ={np.degrees(sigma):.2f}° len={tc['length']:.3f} "
                      f"smooth={tc['smooth']:.2f} feas={feasc}")
            if it - last_improve >= stall_iter:
                if verbose: print(f"[stall] no improvement for {stall_iter} iters at it={it}")
                break

        dt = time.time() - t0
        bt = best["terms"]
        if verbose:
            print(f"[done] iters={it} time={dt:.1f}s best_score={best['score']:.3f} "
                  f"feasible={best['feasible']}")
            print(f"       len {terms0['length']:.3f}->{bt['length']:.3f} "
                  f"smooth {terms0['smooth']:.2f}->{bt['smooth']:.2f} "
                  f"grav {terms0['grav']:.3f}->{bt['grav']:.3f} "
                  f"tip {terms0['tip']:.4f}->{bt['tip']:.4f} "
                  f"clear {terms0['clear']:.5f}->{bt['clear']:.5f}")
        return best, dict(init_terms=terms0, init_score=s0, iters=it, time=dt)

    def dense_path(self, Pint, start, goal, n=120):
        P = np.empty((self.K, 6)); P[0], P[-1] = np.asarray(start), np.asarray(goal); P[1:-1] = Pint
        return basis_matrix(np.linspace(0, 1, n), self.K) @ P
