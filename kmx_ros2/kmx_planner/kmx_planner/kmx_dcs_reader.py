#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
kmx_dcs_reader — FANUC DCS(Dual Check Safety) の CPC 安全ゾーン ($DCSS_CPC[i]) を
Karel 常駐ソケット(A案) 経由で読み、ROS2 に配信するノード。
仕様: DCS_ZONE_ROS2_LIVE_SPEC.md §4/§5（P2-1/P2-2）。

出力:
  - latched topic  /kmx/safety_zones   (kmx_msgs/SafetyZones, transient_local) … 起動時取得用
  - service        /kmx/get_safety_zones(kmx_msgs/GetSafetyZones)              … 「DCS再読込」ボタン

役割分担（A案）:
  - Karel(controller/ROBOGUIDE) = TCP サーバとして常駐。接続ごとに全 CPC ゾーンを
    1行1ゾーンの ASCII(CSV) で吐いてクローズ（or "END")。単位 mm・UF0(World)。
  - このノード = TCP クライアント。サービス呼び/起動時/任意ポーリングで接続→読取り→配信。

★ワイヤプロトコル（Karel → node・ASCII 行・改行終端）:
    (任意) "DCS <n>"                                        … ゾーン件数(検証用・無くても可)
    ゾーン行 "CPC,<idx>,<comment>,<enable>,<mode>,<grp>,<ufrm>,<x1>,<x2>,<y1>,<y2>,<z1>,<z2>"
       例    "CPC,1,KMX_TEST,1,1,1,0,300,900,-300,300,0,600"
    (任意) "END"                                            … 終端（無ければ接続クローズで終端）
  ・数値は mm（実数可）。comment に ',' を含めない（Karel 側で '_' 置換）。
  ・enable/mode/grp/ufrm は整数。ufrm=0=World。
  ・$MODE→inside_allowed: 外側(=$MODE=mode_outside_value・既定1)→内側 keep-out→inside_allowed=false。
    ★内側ゾーンの $MODE 値は未確定（暫定: mode!=outside を inside_allowed=true）。実機で確定したら
      mode_outside_value / mode_inside_value パラメータで調整。
"""
import socket
import time

import rclpy
from rclpy.node import Node
from rclpy.qos import QoSProfile, QoSDurabilityPolicy, QoSReliabilityPolicy, QoSHistoryPolicy

from kmx_msgs.msg import SafetyZone, SafetyZones
from kmx_msgs.srv import GetSafetyZones


class KmxDcsReader(Node):
    def __init__(self):
        super().__init__('kmx_dcs_reader')
        # --- パラメータ ---
        self.declare_parameter('dcs_host', '127.0.0.1')      # ROBOGUIDE/実機コントローラ IP
        self.declare_parameter('dcs_port', 60011)            # Karel 常駐サーバの待受ポート
        self.declare_parameter('robot_id', '')               # 対象ロボ（""=既定/単機）
        self.declare_parameter('frame', 'world')             # UF0=World 確定
        self.declare_parameter('unit', 'mm')                 # KMX 側で ×0.001
        self.declare_parameter('mode_outside_value', 1)      # $MODE の「外側」値（確定=1）
        self.declare_parameter('include_disabled', False)    # enable=0 のゾーンも配信するか
        self.declare_parameter('id_source', 'comment')       # 'comment'（$COMMENT）or 'index'（"CPC<idx>"）
        self.declare_parameter('read_timeout_sec', 3.0)      # TCP connect/recv タイムアウト
        self.declare_parameter('read_retries', 6)            # 接続失敗時の再試行（Karel の listen フラップ吸収）
        self.declare_parameter('poll_sec', 0.0)              # >0 で定期再読込（DCSは静的＝既定 off）
        self.declare_parameter('publish_on_start', True)     # 起動時に1回読んで latched publish
        self.declare_parameter('zones_topic', '/kmx/safety_zones')
        self.declare_parameter('get_service', '/kmx/get_safety_zones')

        topic = str(self.get_parameter('zones_topic').value)
        srv = str(self.get_parameter('get_service').value)

        # latched（transient_local）QoS: 起動後に購読しても最新1件を受け取れる
        latched = QoSProfile(
            depth=1,
            reliability=QoSReliabilityPolicy.RELIABLE,
            durability=QoSDurabilityPolicy.TRANSIENT_LOCAL,
            history=QoSHistoryPolicy.KEEP_LAST,
        )
        self._pub = self.create_publisher(SafetyZones, topic, latched)
        self._srv = self.create_service(GetSafetyZones, srv, self._on_get)
        self._last = None  # 直近成功した SafetyZones（サービスの fallback 用）

        host = str(self.get_parameter('dcs_host').value)
        port = int(self.get_parameter('dcs_port').value)
        self.get_logger().info(
            f"kmx_dcs_reader 起動: Karel {host}:{port} → topic '{topic}'(latched) / service '{srv}'")

        if bool(self.get_parameter('publish_on_start').value):
            # spin 開始後に一度だけ読む（__init__ をブロックしない）
            self._start_timer = self.create_timer(0.5, self._start_once)

        poll = float(self.get_parameter('poll_sec').value)
        if poll > 0.0:
            self.create_timer(poll, self._poll)

    # ---- 起動時 1回 ----
    def _start_once(self):
        self._start_timer.cancel()
        ok, msg, zones = self._read_zones('')
        if ok:
            self._pub.publish(zones)
            self.get_logger().info(f"起動時 DCS 取得 → publish（{len(zones.zones)} ゾーン）")
        else:
            self.get_logger().warn(f"起動時 DCS 取得 失敗: {msg}（サービス/ボタンで再取得可）")

    def _poll(self):
        ok, _msg, zones = self._read_zones('')
        if not ok:
            return
        self._pub.publish(zones)          # 毎周期 latched を再配信（Unity 自動更新）
        # 値が変わった時だけログ（poll が生きている＆ライブ更新を可視化。無変化時は無言）
        sig = [(z.id, list(z.min_mm), list(z.max_mm), z.enabled, z.inside_allowed) for z in zones.zones]
        if sig != getattr(self, '_last_sig', None):
            self._last_sig = sig
            self.get_logger().info(f"DCS 変化検知 → publish（{len(zones.zones)} ゾーン）")

    # ---- サービス（DCS再読込ボタン）----
    def _on_get(self, req, resp):
        ok, msg, zones = self._read_zones(req.robot_id or '')
        resp.ok = ok
        resp.message = msg
        if ok:
            resp.zones = zones
            self._pub.publish(zones)  # latched も更新
        elif self._last is not None:
            resp.zones = self._last   # 失敗時は直近値を返す（KMX が壊れない）
        return resp

    def _resolve_host(self, host):
        """dcs_host='auto'（or ''/gateway/windows_host）なら WSL のデフォルトゲートウェイ
        ＝Windows ホストIP に解決（NAT で ROBOGUIDE(同一PC)に届く。gw は再起動で変わるので毎回解決）。
        それ以外はそのまま（実機は robot IP を明示指定）。"""
        h = (host or '').strip()
        if h and h.lower() not in ('auto', 'gateway', 'windows_host', 'host'):
            return h
        try:
            import subprocess
            out = subprocess.run(['ip', 'route'], capture_output=True, text=True, timeout=2).stdout
            for line in out.splitlines():
                if line.startswith('default') and ' via ' in line:
                    return line.split(' via ')[1].split()[0]
        except Exception:  # noqa: BLE001
            pass
        return '127.0.0.1'   # 解決不可時は loopback（mirrored 環境向けフォールバック）

    # ---- Karel ソケットから読取り → SafetyZones ----
    def _read_zones(self, robot_id):
        host = self._resolve_host(str(self.get_parameter('dcs_host').value))
        port = int(self.get_parameter('dcs_port').value)
        timeout = float(self.get_parameter('read_timeout_sec').value)
        retries = max(1, int(self.get_parameter('read_retries').value))
        # Karel サーバは1接続ごとに MSG_DISCO+DELAY で一瞬 listen を閉じる（フラップ）。
        # その窓に当たると接続が弾かれるので、少し待って数回リトライし取りこぼしを防ぐ。
        raw = ''
        last = ''
        for attempt in range(retries):
            if attempt > 0:
                time.sleep(0.25)
            try:
                raw = self._tcp_fetch(host, port, timeout)
            except OSError as e:
                last = str(e); raw = ''
            if raw and 'CPC' in raw:
                break
        if not (raw and 'CPC' in raw):
            return False, f"Karel 接続/読取り失敗 {host}:{port}（{retries}回試行: {last}）", None

        try:
            zones = self._parse(raw, robot_id)
        except Exception as e:  # noqa: BLE001  パース失敗も KMX を壊さず理由返し
            return False, f"DCS 応答パース失敗: {e}", None

        self._last = zones
        return True, f"{len(zones.zones)} zone(s)", zones

    def _tcp_fetch(self, host, port, timeout):
        """Karel サーバへ接続し、クローズ or 'END' まで全バイト受信して返す。"""
        chunks = []
        with socket.create_connection((host, port), timeout=timeout) as s:
            s.settimeout(timeout)
            while True:
                try:
                    b = s.recv(4096)
                except socket.timeout:
                    break
                if not b:
                    break
                chunks.append(b)
                if b'END' in b:  # 明示終端があれば即抜け（クローズ待ちしない）
                    break
        return b''.join(chunks).decode('ascii', errors='replace')

    def _parse(self, raw, robot_id):
        mode_out = int(self.get_parameter('mode_outside_value').value)
        include_disabled = bool(self.get_parameter('include_disabled').value)
        id_source = str(self.get_parameter('id_source').value)

        zones = SafetyZones()
        zones.robot_id = robot_id
        zones.frame = str(self.get_parameter('frame').value)
        zones.unit = str(self.get_parameter('unit').value)
        zones.zones = []

        for line in raw.splitlines():
            line = line.strip()
            if not line or not line.upper().startswith('CPC'):
                continue
            parts = [p.strip() for p in line.split(',')]
            # "CPC,idx,comment,enable,mode,grp,ufrm,x1,x2,y1,y2,z1,z2" = 13 要素
            if len(parts) < 13:
                self.get_logger().warn(f"ゾーン行の要素不足({len(parts)}<13)・スキップ: {line}")
                continue
            try:
                idx = int(parts[1])
                comment = parts[2]
                enable = int(parts[3]) != 0
                mode = int(parts[4])
                # parts[5]=grp, parts[6]=ufrm は現状 msg に載せない（World 前提）
                x1, x2, y1, y2, z1, z2 = (float(parts[k]) for k in range(7, 13))
            except ValueError as e:
                self.get_logger().warn(f"ゾーン行の数値パース失敗({e})・スキップ: {line}")
                continue

            if not enable and not include_disabled:
                continue

            z = SafetyZone()
            z.id = comment if (id_source == 'comment' and comment) else f"CPC{idx}"
            z.enabled = enable
            z.inside_allowed = (mode != mode_out)   # 外側=keep-out=false（暫定・§3'）
            # DCS は [1]=下限/[2]=上限だが念のため min/max を正規化
            z.min_mm = [min(x1, x2), min(y1, y2), min(z1, z2)]
            z.max_mm = [max(x1, x2), max(y1, y2), max(z1, z2)]
            zones.zones.append(z)
        return zones


def main(args=None):
    rclpy.init(args=args)
    node = KmxDcsReader()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == '__main__':
    main()
