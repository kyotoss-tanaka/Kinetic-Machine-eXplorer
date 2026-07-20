#!/usr/bin/env bash
# KMX bringup 再起動（stop → start）。
#   引数: [use_moveit(既定true)] [rviz 0/1(既定0)] [robot_model(既定crx30ia)] [use_mock(既定true)] [robot_ip(既定127.0.0.1)] [dcs_host(既定auto)]
#   ※明示的な再起動なので、同一条件でも必ず stop→start する
#     （「同一なら再起動しない」を効かせたい場合は kmx_start.sh を使う）。
WS="/home/kyotoss/ros2_ws"
"$WS/kmx_stop.sh"
sleep 2
"$WS/kmx_start.sh" "${1:-true}" "${2:-0}" "${3:-crx30ia}" "${4:-true}" "${5:-}" "${6:-auto}"
