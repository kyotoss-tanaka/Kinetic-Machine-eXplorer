# -*- coding: utf-8 -*-
"""登録最適化：探索中バックグラウンド STOMP プール（REGISTER_BG_STOMP_ROS2_SPEC）。

探索(npa=1・1コア)の裏で、安定したトップ候補を遊休コアで先行 STOMP しキャッシュする。
探索終了時は _optimize_and_publish がキャッシュ済みを即採用（未処理だけ同期）＝終盤の直列後処理待ちを消す。

★スレッド安全の要点：
  - ワーカは rclpy に一切触れない（compute_fn=_stomp_compute は worker-safe・publish/service/get_parameter を呼ばない）。
  - pin+coal オラクルは **worker-local**（スレッドごとに1個構築＝共有しない）。coal(HPP-FCL) 共有 SEGV を回避。
  - 結果は lock 付き dict で受け渡し。rclpy publish はメインスレッド側でのみ行う。
  - 例外は握って result=None（呼び側が同期フォールバック）。SEGV はプロセス層なので防げない→WSL 実走で要検証。
"""
import math
import multiprocessing
import threading
from concurrent.futures import ProcessPoolExecutor, ThreadPoolExecutor


def candidate_key(traj, quant_rad=1e-3):
    """候補ベース軌道の関節位置を量子化してハッシュ（キャッシュキー／二重投入防止／churn 追跡）。
    positions は度で来るので rad 相当の粗さに合わせ deg で量子化（quant_rad rad ≒ その度数）。"""
    pts = getattr(traj, 'points', None)
    if not pts:
        return None
    q = 1.0 / max(math.degrees(quant_rad), 1e-6)   # 度あたりの量子化係数
    vals = []
    for p in pts:
        for v in p.positions:
            vals.append(int(round(float(v) * q)))
    return hash((len(pts), tuple(vals)))


class BgStompPool:
    """探索中バックグラウンド STOMP プール。メインスレッドから submit()/get()/shutdown()。

    oracle_factory : () -> PinScene（★ワーカ内で呼ぶ worker-local ファクトリ。scene snapshot 込みの closure）
    compute_fn     : (S, base, target, mn, params, should_cancel, progress_cb, tag) -> result dict or None
                     （= node._stomp_compute。worker-safe・rclpy 非依存）
    log            : Optional[callable(str)]（メインスレッドから呼ばれる時のみ使用。ワーカからは使わない）
    """

    def __init__(self, workers, oracle_factory, compute_fn, target_time, mn, params, log=None):
        self._workers = max(1, int(workers))
        self._oracle_factory = oracle_factory
        self._compute_fn = compute_fn
        self._target = target_time
        self._mn = list(mn)
        self._params = params
        self._log = log
        self._lock = threading.Lock()
        self._cache = {}        # key -> result dict or None（None=infeasible/例外）
        self._inflight = set()  # 投入済み・未完 key
        self._tls = threading.local()   # ワーカごとの worker-local オラクル
        self._done = 0
        self._submitted = 0
        self._closed = False
        self._ex = ThreadPoolExecutor(max_workers=self._workers, thread_name_prefix='bgstomp')

    # ---- ワーカ本体（rclpy に触れない）----
    def _worker(self, key, base, tag):
        result = None
        try:
            S = getattr(self._tls, 'oracle', None)
            if S is None:
                S = self._oracle_factory()      # worker-local に1個構築（以降 再利用）
                self._tls.oracle = S
            if S is not None:
                result = self._compute_fn(S, base, self._target, self._mn, self._params,
                                          should_cancel=None, progress_cb=None, tag=tag)
        except BaseException as e:   # noqa: BLE001  例外は握る（SEGV は防げない）
            result = None
        with self._lock:
            self._cache[key] = result
            self._inflight.discard(key)
            self._done += 1

    # ---- メインスレッドAPI ----
    def submit(self, key, base, tag=""):
        """未キャッシュ・未投入なら先行 STOMP をキューへ。投入したら True。"""
        if key is None:
            return False
        with self._lock:
            if self._closed or key in self._cache or key in self._inflight:
                return False
            self._inflight.add(key)
            self._submitted += 1
        try:
            self._ex.submit(self._worker, key, base, tag)
        except RuntimeError:   # shutdown 後の submit
            with self._lock:
                self._inflight.discard(key)
            return False
        return True

    def get(self, key):
        """(hit, result) を返す。hit=True かつ result=None は「先行処理したが infeasible/失敗」。"""
        with self._lock:
            if key in self._cache:
                return True, self._cache[key]
        return False, None

    def stats(self):
        with self._lock:
            return self._done, self._submitted, len(self._inflight)

    def shutdown(self, wait=False):
        with self._lock:
            self._closed = True
        try:
            # cancel_futures=True は未開始ジョブを破棄（Python3.9+）。実行中は完走を待たない(wait=False)。
            self._ex.shutdown(wait=wait, cancel_futures=True)
        except TypeError:
            self._ex.shutdown(wait=wait)


class ProcessBgStompPool:
    """Phase3：真の並列（別プロセス）版。BgStompPool と同一の main-thread API（submit/get/stats/shutdown）。

    ★なぜ：pin.computeCollisions は GIL を解放しない（実測 8スレッド 1.1x＝並列ゼロ）＝ThreadPool 版は
      実質1コア。ProcessPoolExecutor(spawn) で複数プロセスに分ければ真の並列＋pin/coal 完全分離（SEGV 無縁）。
    ★pickle 境界：ワーカ関数はモジュール関数 stomp_worker.process_worker。オラクルはワーカ内で URDF から
      再構築（scene は bytes で1回だけ渡す・プロセス内キャッシュ）。base/out も bytes（rclpy serialize）。
    ★spawn：fork は rclpy/DDS 状態を子に持ち込み危険なので spawn（クリーン子）を明示。

    static payload（全プロセス共通・picklable）: urdf, srdf, pkg, scene_bytes, target_time, mn, params。
    """

    def __init__(self, workers, static, log=None):
        self._workers = max(1, int(workers))
        self._static = static      # dict(urdf, srdf, pkg, scene_bytes, target_time, mn, params)
        self._log = log
        self._lock = threading.Lock()
        self._cache = {}           # key -> result dict(out=JointTrajectory,...) or None
        self._inflight = set()
        self._done = 0
        self._submitted = 0
        self._closed = False
        self._broken = False       # 子プロセス崩壊(BrokenProcessPool)→以降 submit 停止＝同期フォールバック
        ctx = multiprocessing.get_context('spawn')
        self._ex = ProcessPoolExecutor(max_workers=self._workers, mp_context=ctx)

    def _on_done(self, key, fut):
        result = None
        try:
            r = fut.result()
            if r is not None and 'out_bytes' in r:
                from rclpy.serialization import deserialize_message
                from trajectory_msgs.msg import JointTrajectory
                out = deserialize_message(r['out_bytes'], JointTrajectory)
                r = dict(r)
                r.pop('out_bytes')
                r['out'] = out
                result = r
        except BaseException:   # noqa: BLE001  子プロセス崩壊/pickle 失敗など
            result = None
            with self._lock:
                self._broken = True   # 以降の submit を止める（残りは完了時 同期フォールバック）
        with self._lock:
            self._cache[key] = result
            self._inflight.discard(key)
            self._done += 1

    def submit(self, key, base_traj, tag=""):
        if key is None:
            return False
        with self._lock:
            if self._closed or self._broken or key in self._cache or key in self._inflight:
                return False
            self._inflight.add(key)
            self._submitted += 1
        try:
            from rclpy.serialization import serialize_message
            payload = dict(self._static)
            payload['base_bytes'] = serialize_message(base_traj)
            payload['key'] = key
            payload['tag'] = tag
            from .stomp_worker import process_worker
            fut = self._ex.submit(process_worker, payload)
            fut.add_done_callback(lambda f, k=key: self._on_done(k, f))
        except BaseException:   # noqa: BLE001  submit 失敗（shutdown 後/pickle 不可等）
            with self._lock:
                self._inflight.discard(key)
                self._broken = True
            return False
        return True

    def get(self, key):
        with self._lock:
            if key in self._cache:
                return True, self._cache[key]
        return False, None

    def stats(self):
        with self._lock:
            return self._done, self._submitted, len(self._inflight)

    def shutdown(self, wait=False):
        with self._lock:
            self._closed = True
        try:
            self._ex.shutdown(wait=wait, cancel_futures=True)
        except TypeError:
            self._ex.shutdown(wait=wait)
