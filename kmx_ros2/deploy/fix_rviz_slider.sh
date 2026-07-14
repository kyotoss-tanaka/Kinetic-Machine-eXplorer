#!/usr/bin/env bash
# 既存 ~/ros2_ws の fanuc launch に「slider削除 ＋ RVizを KMX_RVIZ でゲート」を適用し、
# install が symlink か(=再ビルド要否)を報告する。冪等（既に適用済みならスキップ）。
set -eo pipefail
WS="$HOME/ros2_ws"
MOVEIT=$(find "$WS/src" -path '*fanuc_moveit_config/launch/fanuc_moveit.launch.py' | head -1)
MOCK=$(find "$WS/src" -path '*fanuc_hardware_interface/launch/fanuc_mock_control.launch.py' | head -1)
echo "MOVEIT_SRC=$MOVEIT"
echo "MOCK_SRC=$MOCK"

if [ -n "$MOCK" ] && ! grep -q 'KMX: slider' "$MOCK"; then
  sed -i 's/^\(\s*\)nodes_to_launch\.append(slider_test_node)/\1pass  # KMX: slider removed/' "$MOCK"
  echo "[applied] slider removed"
else
  echo "[skip] slider (already or not found)"
fi

if [ -n "$MOVEIT" ] && ! grep -q 'KMX_RVIZ' "$MOVEIT"; then
  sed -i 's/^\(\s*\)nodes_to_launch\.append(rviz_node)/\1import os as _kmxos\n\1if _kmxos.environ.get("KMX_RVIZ","0") in ("1","true","True"): nodes_to_launch.append(rviz_node)/' "$MOVEIT"
  echo "[applied] rviz gated by KMX_RVIZ"
else
  echo "[skip] rviz gate (already or not found)"
fi

echo "--- KMX marks (src) ---"
grep -n KMX "$MOVEIT" "$MOCK" || true

echo "--- install が symlink か（COPY なら再ビルド要）---"
INS_M=$(find "$WS/install" -path '*fanuc_moveit_config/launch/fanuc_moveit.launch.py' | head -1)
INS_K=$(find "$WS/install" -path '*fanuc_hardware_interface/launch/fanuc_mock_control.launch.py' | head -1)
NEED_REBUILD=0
for f in "$INS_M" "$INS_K"; do
  if [ -z "$f" ]; then echo "MISSING install"; NEED_REBUILD=1;
  elif [ -L "$f" ]; then echo "symlink OK: $f";
  else echo "COPY(要再ビルド): $f"; NEED_REBUILD=1; fi
done
echo "NEED_REBUILD=$NEED_REBUILD"
