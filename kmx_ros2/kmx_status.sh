#!/usr/bin/env bash
# KMX bringup の稼働状態を1行で返す（Unity のポーリング用）。
#   running_full : launch＋move_group＋kmx_planner が揃って稼働（計画可能）
#   starting     : launch はいるが move_group/kmx_planner がまだ揃っていない
#   stopped      : launch なし
if ! pgrep -f "ros2 launch kmx_planner kmx_bringup" >/dev/null 2>&1; then
  echo "stopped"; exit 0
fi
if pgrep -f "lib/kmx_planner/kmx_planner" >/dev/null 2>&1 \
   && pgrep -f "moveit_ros_move_group/move_group" >/dev/null 2>&1; then
  echo "running_full"; exit 0
fi
echo "starting"; exit 0
