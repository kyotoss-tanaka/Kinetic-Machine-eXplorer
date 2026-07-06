#!/usr/bin/env bash
# KMX bringup 再起動（stop → start）。引数で use_moveit を渡せる（既定 true）。
WS="/home/kyotoss/ros2_ws"
"$WS/kmx_stop.sh"
sleep 2
"$WS/kmx_start.sh" "${1:-true}"
