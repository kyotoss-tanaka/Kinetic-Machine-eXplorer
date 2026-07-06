#!/usr/bin/env bash
# KMX bringup 停止（Unity/wsl.exe から呼ばれる想定）。
#   launch に SIGINT で graceful 停止 → 最大10s 待ち → 残れば SIGKILL（既知の子プロセスも掃除）。
WS="/home/kyotoss/ros2_ws"
PIDFILE="$WS/.kmx_bringup.pid"

# 停止対象（launch とその子ノード群）。単一インスタンス運用前提。
PATS=(
  "ros2 launch kmx_planner kmx_bringup"
  "moveit_ros_move_group/move_group"
  "lib/kmx_planner/kmx_planner"
  "default_server_endpoint"
  "controller_manager/ros2_control_node"
  "rviz2"
  "robot_state_publisher"
)

alive() { for p in "${PATS[@]}"; do pgrep -f "$p" >/dev/null 2>&1 && return 0; done; return 1; }

# 1) graceful: launch のプロセスグループ＋パターンに SIGINT（ros2 launch が子を順に落とす）
if [ -f "$PIDFILE" ]; then
  PID="$(cat "$PIDFILE" 2>/dev/null)"
  [ -n "$PID" ] && { kill -INT "$PID" 2>/dev/null; kill -INT -- -"$PID" 2>/dev/null; }
fi
pkill -INT -f "ros2 launch kmx_planner kmx_bringup" 2>/dev/null

# 2) 最大10s 待つ
for i in $(seq 1 10); do alive || break; sleep 1; done

# 3) 残っていれば SIGKILL（子ノードも個別に）
if alive; then
  echo "[kmx] graceful 停止しきれず → SIGKILL"
  for p in "${PATS[@]}"; do pkill -9 -f "$p" 2>/dev/null; done
  sleep 1
fi

rm -f "$PIDFILE"
if alive; then
  echo "[kmx] WARN: 一部プロセスが残存しています"; exit 1
else
  echo "[kmx] stopped"; exit 0
fi
