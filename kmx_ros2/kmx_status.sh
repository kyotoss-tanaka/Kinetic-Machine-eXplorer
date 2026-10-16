#!/usr/bin/env bash
# KMX bringup の稼働状態を1行で返す（Unity のポーリング用）。
#   running_full : launch＋move_group＋kmx_planner＋Unity 受け口(endpoint)が揃って稼働（計画可能）
#   starting     : launch はいるが上記がまだ揃っていない（endpoint の自動再起動待ちも含む）
#   stopped      : launch なし
if ! pgrep -f "ros2 launch kmx_planner kmx_bringup" >/dev/null 2>&1; then
  echo "stopped"; exit 0
fi
if pgrep -f "lib/kmx_planner/kmx_planner" >/dev/null 2>&1 \
   && pgrep -f "moveit_ros_move_group/move_group" >/dev/null 2>&1 \
   && pgrep -f "ros_tcp_endpoint/default_server_endpoint" >/dev/null 2>&1; then
  echo "running_full"; exit 0
fi
echo "starting"; exit 0
