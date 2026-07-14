#!/usr/bin/env bash
# KMX bringup 再起動（stop → start）。引数: [use_moveit(既定true)] [rviz 0/1(既定0)]。
WS="/home/kyotoss/ros2_ws"
"$WS/kmx_stop.sh"
sleep 2
"$WS/kmx_start.sh" "${1:-true}" "${2:-0}"
