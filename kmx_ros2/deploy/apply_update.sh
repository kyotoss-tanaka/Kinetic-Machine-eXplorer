#!/usr/bin/env bash
# =====================================================================
# KMX 差分アップデート（新PC側で実行）— ROS2 バックエンドのコード更新を反映
#   フルインストール(KMX-Installer.ps1)不要。apt / BITstar MoveIt(/opt/kmx_moveit) /
#   proxy 証明書 / .bashrc は触らない。ros2_src.tgz(＋任意で scripts.tgz)を展開し
#   colcon build → kmx_restart するだけ。
#
#   使い方(WSL): bash apply_update.sh                # このスクリプトと同じ場所の *.tgz を使う
#                bash apply_update.sh /path/to/artifacts
#   ROS-TCP-Endpoint も更新するとき: UPDATE_ENDPOINT=1 bash apply_update.sh
#   ※ Unity(Windows)から: wsl.exe -e bash -lc ".../apply_update.sh" （kmx_start.sh と同じ流儀）
# =====================================================================
set -eo pipefail
KIT="${1:-$(cd "$(dirname "$0")" && pwd)}"
H="$HOME"
echo "== KMX 差分アップデート  kit=$KIT  user=$H =="
[ -f "$KIT/ros2_src.tgz" ] || { echo "ERR: $KIT/ros2_src.tgz が見つかりません（make_update.sh で生成）"; exit 1; }

echo "-- [1/4] ソース展開（~/ros2_ws/src を上書き）"
mkdir -p "$H/ros2_ws/src"
tar xzf "$KIT/ros2_src.tgz" -C "$H/ros2_ws"
[ -f "$KIT/scripts.tgz" ] && tar xzf "$KIT/scripts.tgz" -C "$H/ros2_ws"
# ROS-TCP-Endpoint は通常不変。明示要求時のみ更新。
if [ -f "$KIT/endpoint_src.tgz" ] && [ "${UPDATE_ENDPOINT:-0}" = "1" ]; then
  mkdir -p "$H/colcon_ws/src"; tar xzf "$KIT/endpoint_src.tgz" -C "$H/colcon_ws"; echo "   endpoint も更新"
fi
find "$H/ros2_ws" -name __pycache__ -type d -exec rm -rf {} + 2>/dev/null || true   # 古い/壊れた .pyc 除去(bad marshal 対策)
find "$H/ros2_ws" -name '*.pyc' -delete 2>/dev/null || true
# ビルド機ユーザ(/home/kyotoss)のハードコードを新PCのユーザに置換
grep -rl '/home/kyotoss/' "$H/ros2_ws" 2>/dev/null | xargs -r sed -i "s#/home/kyotoss/#$H/#g"
chmod +x "$H"/ros2_ws/kmx_*.sh 2>/dev/null || true
# RViz を KMX_RVIZ でゲート／slider 削除（既に適用済みなら no-op）
MOVEIT=$(find "$H/ros2_ws/src" -path '*fanuc_moveit_config/launch/fanuc_moveit.launch.py' | head -1 || true)
MOCK=$(find "$H/ros2_ws/src" -path '*fanuc_hardware_interface/launch/fanuc_mock_control.launch.py' | head -1 || true)
if [ -n "$MOCK" ] && ! grep -q 'KMX: slider' "$MOCK"; then
  sed -i 's/^\(\s*\)nodes_to_launch\.append(slider_test_node)/\1pass  # KMX: slider removed/' "$MOCK"
fi
if [ -n "$MOVEIT" ] && ! grep -q 'KMX_RVIZ' "$MOVEIT"; then
  sed -i 's/^\(\s*\)nodes_to_launch\.append(rviz_node)/\1import os as _kmxos\n\1if _kmxos.environ.get("KMX_RVIZ","0") in ("1","true","True"): nodes_to_launch.append(rviz_node)/' "$MOVEIT"
fi

echo "-- [2/4] colcon build（kmx_msgs→kmx_planner ほか。MoveIt は再ビルドしない）"
source /opt/ros/humble/setup.bash
source /opt/kmx_moveit/setup.bash 2>/dev/null || true
source "$H/colcon_ws/install/setup.bash" 2>/dev/null || true
cd "$H/ros2_ws"
# --packages-up-to kmx_planner で依存の kmx_msgs も必要時に再ビルド（msg 追加も自動反映）
colcon build --symlink-install \
  --packages-up-to kmx_planner fanuc_moveit_config fanuc_hardware_interface fanuc_controllers slider_publisher

echo "-- [3/4] 再起動"
source "$H/ros2_ws/install/setup.bash"
"$H/ros2_ws/kmx_restart.sh" 2>/dev/null || "$H/ros2_ws/kmx_start.sh" true 0

echo "-- [4/4] 起動待ち（running_full）"
for i in $(seq 1 40); do
  st=$("$H/ros2_ws/kmx_status.sh" 2>/dev/null || echo)
  if [ "$st" = "running_full" ]; then echo "OK: running_full（アップデート完了）"; exit 0; fi
  sleep 3
done
echo "WARN: running_full 未達 → ~/ros2_ws/kmx_bringup.log を確認"; exit 1
