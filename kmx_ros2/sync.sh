#!/usr/bin/env bash
# Unityリポジトリの ROS2 正本 → WSL の ~/ros2_ws/src へ同期する（WSLで実行）。
#   使い方: bash /mnt/c/Users/gi-guest/source/repos/Kinetic\ Machine\ eXplorer/kmx_ros2/sync.sh
#   （or 実行権限付与: chmod +x sync.sh → ./sync.sh）
#
# 正本 = Unityリポの kmx_ros2/（git管理はこちら）。WSLの ~/ros2_ws/src はビルド/実行用のコピー。
set -euo pipefail

REPO="/mnt/c/Users/gi-guest/source/repos/Kinetic Machine eXplorer/kmx_ros2"
WS="$HOME/ros2_ws/src"

if [ ! -d "$REPO" ]; then
  echo "[sync] 正本が見つかりません: $REPO" >&2
  exit 1
fi

# kmx_planner を丸ごと同期（build成果物や __pycache__ は除外。srcにdest固有ファイルがあれば削除して一致させる）
echo "[sync] kmx_planner -> $WS/kmx_planner"
mkdir -p "$WS/kmx_planner"
rsync -a --delete --exclude '__pycache__/' --exclude '*.pyc' \
  "$REPO/kmx_planner/" "$WS/kmx_planner/"

# kmx_msgs は WSL側が正本（CMakeLists等はWSLで管理）。Unity側で定義する .msg だけ反映する。
# （新規 .msg を足したら CMakeLists.txt / package.xml への登録は WSL側で手動。ここはコピーのみ）
if [ -d "$WS/kmx_msgs/msg" ]; then
  for m in PlanRequest.msg ObstaclePrimitive.msg Obstacles.msg; do
    if [ -f "$REPO/kmx_msgs/msg/$m" ]; then
      echo "[sync] $m -> $WS/kmx_msgs/msg/"
      cp "$REPO/kmx_msgs/msg/$m" "$WS/kmx_msgs/msg/$m"
    fi
  done
fi

echo "[sync] 完了。次:"
echo "  cd ~/ros2_ws && colcon build --symlink-install --packages-select kmx_planner && source install/setup.bash"
echo "  ros2 launch kmx_planner kmx_bringup.launch.py            # MoveItまで全部"
echo "  ros2 launch kmx_planner kmx_bringup.launch.py use_moveit:=false  # 軽量"
