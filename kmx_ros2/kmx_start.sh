#!/usr/bin/env bash
# KMX bringup 起動（Unity/wsl.exe から呼ばれる想定）。
#   使い方: kmx_start.sh [use_moveit(true|false)] [rviz 0/1] [robot_model] [use_mock(true|false)] [robot_ip] [dcs_host]
#     既定: use_moveit=true, rviz=0, robot_model=crx30ia, use_mock=true, robot_ip=127.0.0.1, dcs_host=auto
#     dcs_host: DCS Karel ソケット接続先。auto=ROBOGUIDE(同一PC)/実機=コントローラIP。127.0.0.1/localhost は auto に読替。
#   冪等 / ロボット・接続 切替:
#     - 未起動                                         → 起動
#     - 起動中＋同一(model,use_mock,robot_ip,dcs_host) → 何もしない（★同一条件は再起動しない）
#     - 起動中＋いずれか変化                           → stop → 新条件で再起動（機種/接続 切替＝方式A）
#     - 起動中でも Unity 受け口(ros_tcp_endpoint)が居ない → stop → 再起動
#   ログ: ~/ros2_ws/kmx_bringup.log（前回分は kmx_bringup.<日時>.log・直近10本）  PID: ~/ros2_ws/.kmx_bringup.pid
#   稼働中の条件（Unity 参照用）: .kmx_robot_model / .kmx_use_mock / .kmx_robot_ip / .kmx_dcs_host
WS="/home/kyotoss/ros2_ws"
PIDFILE="$WS/.kmx_bringup.pid"
LOG="$WS/kmx_bringup.log"
MODELFILE="$WS/.kmx_robot_model"
MOCKFILE="$WS/.kmx_use_mock"
IPFILE="$WS/.kmx_robot_ip"
DCSHOSTFILE="$WS/.kmx_dcs_host"
USE_MOVEIT="${1:-true}"
RVIZ="${2:-0}"                  # 第2引数: RViz 表示 0/1（Ros2Info.json launchRviz 由来）
ROBOT_MODEL="${3:-crx30ia}"     # 第3引数: fanuc_moveit_config の robot_model（例 crx30ia / m20_25_18d）
USE_MOCK="${4:-true}"           # 第4引数: true=模擬HW / false=実機接続(Stream Motion。CSV運用では未使用)
ROBOT_IP="${5:-}"              # 第5引数: use_mock=false 時の Stream Motion 接続先IP（CSV運用では未使用）
DCS_HOST="${6:-auto}"          # 第6引数: ★DCS Karel ソケット接続先。auto=WSLゲートウェイ(ROBOGUIDE同一PC)/実機=コントローラIP
export KMX_RVIZ="$RVIZ"
# WSLg の GPU 描画（Mesa d3d12）では RViz の 3D ビューが真っ黒になるため、RViz を出す時は CPU 描画にする。
#   GPU で描きたい環境では KMX_RVIZ_GPU=1 を付けて呼ぶ。
if [ "$RVIZ" = "1" ] && [ "${KMX_RVIZ_GPU:-0}" != "1" ]; then
  export LIBGL_ALWAYS_SOFTWARE=1
fi

# robot_model の妥当性チェック（空・不正・旧引数の boolean 等は crx30ia にフォールバック）。
#   新機種を足したらこの一覧にも追加（fanuc_moveit.launch.py の choices と一致させる）。
case "$ROBOT_MODEL" in
  crx3ia|crx5ia|crx10ia|crx10ia_l|crx20ia_l|crx30ia|m20_25_18d) ;;
  *)
    echo "[kmx] warning: robot_model='$ROBOT_MODEL' は不正 → crx30ia にフォールバック"
    ROBOT_MODEL="crx30ia"
    ;;
esac

# use_mock の妥当性チェック（true/false 以外は模擬=true にフォールバック＝安全側）
case "$USE_MOCK" in
  true|false) ;;
  *)
    echo "[kmx] warning: use_mock='$USE_MOCK' は不正 → true(模擬) にフォールバック"
    USE_MOCK="true"
    ;;
esac

# robot_ip 既定（空なら 127.0.0.1）。use_mock=true では未使用だが記録・比較のため確定させる。
[ -z "$ROBOT_IP" ] && ROBOT_IP="127.0.0.1"

# dcs_host 既定（空なら auto）。
#   ★Unity の robotIp 既定 127.0.0.1/localhost は「このPC(=Windows/ROBOGUIDE)」の意味だが、
#     WSL から見た 127.0.0.1 は WSL 自身で Karel に届かない。→ auto(WSLゲートウェイ=Windowsホスト)へ読み替え。
#     実機は別ホストの実IP（例 192.168.1.20）を渡す＝そのまま使用。
[ -z "$DCS_HOST" ] && DCS_HOST="auto"
case "$DCS_HOST" in
  127.0.0.1|localhost)
    echo "[kmx] note: dcs_host=$DCS_HOST は WSL では自ホスト → auto(ゲートウェイ=Windowsホスト) に読み替え"
    DCS_HOST="auto"
    ;;
esac

if pgrep -f "ros2 launch kmx_planner kmx_bringup" >/dev/null 2>&1; then
  CUR_MODEL="$(cat "$MODELFILE" 2>/dev/null)"; [ -z "$CUR_MODEL" ] && CUR_MODEL="crx30ia"
  CUR_MOCK="$(cat "$MOCKFILE" 2>/dev/null)";  [ -z "$CUR_MOCK" ]  && CUR_MOCK="true"
  CUR_IP="$(cat "$IPFILE" 2>/dev/null)";      [ -z "$CUR_IP" ]    && CUR_IP="127.0.0.1"
  CUR_DCS="$(cat "$DCSHOSTFILE" 2>/dev/null)"; [ -z "$CUR_DCS" ]  && CUR_DCS="auto"
  # Unity 受け口(endpoint)だけ落ちて launch が残っていると、同一条件でも Unity は繋がらない。
  #   respawn の待ち時間(2s)を越えて居ない場合は同一条件でも再起動する。
  EP_ALIVE=0
  for i in 1 2 3 4 5 6; do
    pgrep -f "ros_tcp_endpoint/default_server_endpoint" >/dev/null 2>&1 && { EP_ALIVE=1; break; }
    sleep 0.5
  done
  if [ "$CUR_MODEL" = "$ROBOT_MODEL" ] && [ "$CUR_MOCK" = "$USE_MOCK" ] && [ "$CUR_IP" = "$ROBOT_IP" ] && [ "$CUR_DCS" = "$DCS_HOST" ] && [ $EP_ALIVE -eq 1 ]; then
    echo "[kmx] already running（同一条件 model=$ROBOT_MODEL use_mock=$USE_MOCK robot_ip=$ROBOT_IP dcs_host=$DCS_HOST・再起動しません）"
    exit 0
  fi
  if [ $EP_ALIVE -eq 0 ]; then
    echo "[kmx] Unity 受け口(ros_tcp_endpoint)が居ない → stop → 再起動"
  fi
  echo "[kmx] 切替 ($CUR_MODEL,$CUR_MOCK,$CUR_IP,$CUR_DCS) → ($ROBOT_MODEL,$USE_MOCK,$ROBOT_IP,$DCS_HOST)（stop → 再起動）"
  "$WS/kmx_stop.sh"
  # 完全停止を待つ（次の起動が『二重起動』検知に引っかからないように）
  for i in $(seq 1 30); do
    pgrep -f "ros2 launch kmx_planner kmx_bringup" >/dev/null 2>&1 || break
    sleep 0.5
  done
fi

# ROS 環境を source（子プロセスへ継承させる）
source /opt/ros/humble/setup.bash 2>/dev/null
source /home/kyotoss/colcon_ws/install/setup.bash 2>/dev/null
source /home/kyotoss/ws_moveit/install/setup.bash 2>/dev/null
source /opt/kmx_moveit/setup.bash 2>/dev/null      # 配布先: BITstar MoveIt（固定パス /opt/kmx_moveit）
source "$WS/install/setup.bash" 2>/dev/null

# 稼働条件を記録（同一判定・Unity 問い合わせ用）
echo "$ROBOT_MODEL" > "$MODELFILE"
echo "$USE_MOCK"    > "$MOCKFILE"
echo "$ROBOT_IP"    > "$IPFILE"
echo "$DCS_HOST"    > "$DCSHOSTFILE"

# 前回のログを日時付きで残す（直近10本）。落ちたノードのエラー出力を後から追えるように。
if [ -s "$LOG" ]; then
  mv -f "$LOG" "$WS/kmx_bringup.$(date +%Y%m%d-%H%M%S).log"
  ls -1t "$WS"/kmx_bringup.*.log 2>/dev/null | tail -n +11 | xargs -r rm -f
fi

# 端末から切り離した新セッションで起動（wsl.exe が抜けても生存）。PID=セッションリーダ。
setsid bash -c "exec ros2 launch kmx_planner kmx_bringup.launch.py use_moveit:=$USE_MOVEIT robot_model:=$ROBOT_MODEL use_mock:=$USE_MOCK robot_ip:=$ROBOT_IP dcs_host:=$DCS_HOST" \
  >"$LOG" 2>&1 < /dev/null &
echo $! > "$PIDFILE"
echo "[kmx] starting (pid $!, use_moveit=$USE_MOVEIT, robot_model=$ROBOT_MODEL, use_mock=$USE_MOCK, robot_ip=$ROBOT_IP, dcs_host=$DCS_HOST)  log=$LOG"

# ★重要（Unity/wsl.exe から呼ぶ場合の必須処理）:
#   ros2 launch は多プロセス(12+ノード)のため、呼び出し元(wsl.exe)のセッションが「確立前」に
#   抜けると WSL2 が子プロセスごと刈り取り、ログ空のまま即死する。そこで running_full（＝安定）に
#   達するまでここで待ってから return し、起動ウィンドウを跨ぐ。到達後は wsl.exe が抜けても生存。
STABLE=0
DEADLINE=$((SECONDS + 45))
while [ $SECONDS -lt $DEADLINE ]; do
  sleep 0.5
  st="$("$WS/kmx_status.sh")"
  if [ "$USE_MOVEIT" = "true" ]; then
    if [ "$st" = "running_full" ]; then
      echo "[kmx] running_full（安定・robot_model=$ROBOT_MODEL use_mock=$USE_MOCK robot_ip=$ROBOT_IP）"
      exit 0
    fi
  else
    if pgrep -f "lib/kmx_planner/kmx_planner" >/dev/null 2>&1; then
      STABLE=$((STABLE + 1))
      if [ $STABLE -ge 4 ]; then
        echo "[kmx] planner up（軽量・安定・robot_model=$ROBOT_MODEL use_mock=$USE_MOCK）"
        exit 0
      fi
    fi
  fi
done
echo "[kmx] warning: 起動確立を時間内に確認できず（継続中の可能性。kmx_status.sh で確認）"