#!/usr/bin/env bash
# KMX bringup 起動（Unity/wsl.exe から呼ばれる想定）。
#   使い方: kmx_start.sh [use_moveit(true|false)]  既定 true
#   冪等: 既に起動中なら何もしない。バックグラウンド起動して即 return（Unity をブロックしない）。
#   ログ: ~/ros2_ws/kmx_bringup.log。稼働 PID: ~/ros2_ws/.kmx_bringup.pid
WS="/home/kyotoss/ros2_ws"
PIDFILE="$WS/.kmx_bringup.pid"
LOG="$WS/kmx_bringup.log"
USE_MOVEIT="${1:-true}"

if pgrep -f "ros2 launch kmx_planner kmx_bringup" >/dev/null 2>&1; then
  echo "[kmx] already running（二重起動しません）"
  exit 0
fi

# ROS 環境を source（子プロセスへ継承させる）
source /opt/ros/humble/setup.bash 2>/dev/null
source /home/kyotoss/colcon_ws/install/setup.bash 2>/dev/null
source /home/kyotoss/ws_moveit/install/setup.bash 2>/dev/null
source "$WS/install/setup.bash" 2>/dev/null

# 端末から切り離した新セッションで起動（wsl.exe が抜けても生存）。PID=セッションリーダ。
setsid bash -c "exec ros2 launch kmx_planner kmx_bringup.launch.py use_moveit:=$USE_MOVEIT" \
  >"$LOG" 2>&1 < /dev/null &
echo $! > "$PIDFILE"
echo "[kmx] starting (pid $!, use_moveit=$USE_MOVEIT)  log=$LOG"
echo "[kmx] 完全起動まで ~15-20s。状態は kmx_status.sh で確認可。"
