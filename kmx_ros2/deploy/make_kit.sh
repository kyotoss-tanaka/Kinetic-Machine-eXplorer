#!/usr/bin/env bash
# =====================================================================
# KMX Deploy Kit ビルダー — 旧PC(ビルド機)で「1回だけ」実行
#   BITstar 入り MoveIt を /opt/kmx_moveit（ユーザ非依存の固定パス）へ再ビルドし、
#   fanuc/kmx/endpoint と一緒に配布用 artifacts/ にまとめる。
#   新PC側は KMX-Installer.ps1 が「展開するだけ」＝重いビルド不要・スペック低下なし。
#
#   前提: このビルド機に ~/ws_moveit（BITstarバックポート版 MoveIt2 ソース）、
#         ~/ros2_ws（fanuc群＋kmx）、~/colcon_ws（ROS-TCP-Endpoint）があること。
#         kmx_planner/kmx_*.sh は最新（リポジトリと同期済み＝KMX_RVIZ 対応版）にしておく。
#   使い方: bash make_kit.sh [出力先(既定 ~/KMX-Deploy/artifacts)]
# =====================================================================
set -eo pipefail   # nounset(-u) は ROS setup.bash が未定義変数を参照するため付けない
OUT="${1:-$HOME/KMX-Deploy/artifacts}"
WS="$HOME/ros2_ws"; CW="$HOME/colcon_ws"; WM="$HOME/ws_moveit"
mkdir -p "$OUT"
source /opt/ros/humble/setup.bash

echo "== [1/5] BITstar MoveIt を /opt/kmx_moveit へ再ビルド（ユーザ非依存パス）=="
# /opt/kmx_moveit を用意（root で作成済み＝書込可ならスキップ）。sudo が要る環境向けのフォールバック付き。
if [ ! -d /opt/kmx_moveit ] || [ ! -w /opt/kmx_moveit ]; then
  sudo mkdir -p /opt/kmx_moveit && sudo chown "$USER":"$USER" /opt/kmx_moveit
fi
cd "$WM"
# 既存 build/(isolated) を再利用し install 先だけ /opt/kmx_moveit へ。全PC同一パスなので再配置不要で動く。
# 実行時に要る MoveIt 一式のみ（tutorials/task_constructor/visual_tools/py は対象外）。
colcon build --install-base /opt/kmx_moveit \
  --packages-up-to moveit moveit_ros_move_group moveit_planners_ompl
tar czf "$OUT/kmx_moveit.tgz" -C /opt kmx_moveit
echo "   -> $OUT/kmx_moveit.tgz"

echo "== [2/5] fanuc launch を編集（RViz を KMX_RVIZ でゲート／slider 削除）=="
MOVEIT=$(find "$WS/src" -path '*fanuc_moveit_config/launch/fanuc_moveit.launch.py' | head -1 || true)
MOCK=$(find "$WS/src" -path '*fanuc_hardware_interface/launch/fanuc_mock_control.launch.py' | head -1 || true)
if [ -n "${MOCK:-}" ] && ! grep -q 'KMX: slider' "$MOCK"; then
  sed -i 's/^\(\s*\)nodes_to_launch\.append(slider_test_node)/\1pass  # KMX: slider removed/' "$MOCK"
  echo "   slider 削除: $MOCK"
fi
if [ -n "${MOVEIT:-}" ] && ! grep -q 'KMX_RVIZ' "$MOVEIT"; then
  sed -i 's/^\(\s*\)nodes_to_launch\.append(rviz_node)/\1import os as _kmxos\n\1if _kmxos.environ.get("KMX_RVIZ","0") in ("1","true","True"): nodes_to_launch.append(rviz_node)/' "$MOVEIT"
  echo "   RViz ゲート: $MOVEIT"
fi

echo "== [3/5] fanuc + kmx (~/ros2_ws/src) と起動スクリプトを固める =="
tar czf "$OUT/ros2_src.tgz" --exclude='__pycache__' --exclude='*.pyc' -C "$WS" src
( cd "$WS" && ls kmx_*.sh >/dev/null 2>&1 && tar czf "$OUT/scripts.tgz" -C "$WS" $(ls kmx_*.sh) ) || echo "   (kmx_*.sh 無し・スキップ)"

echo "== [4/5] ROS-TCP-Endpoint を固める =="
tar czf "$OUT/endpoint_src.tgz" -C "$CW" src

HERE="$(cd "$(dirname "$0")" && pwd)"
[ -f "$HERE/apply_update.sh" ] && cp -f "$HERE/apply_update.sh" "$OUT/apply_update.sh" && chmod +x "$OUT/apply_update.sh" && echo "   差分適用スクリプト同梱: apply_update.sh"

echo "== [5/5] Ros2Info テンプレ（新PCで wslUser を置換）=="
cat > "$OUT/Ros2Info.json" <<'JSON'
{
  "enabled": true,
  "ip": "127.0.0.1",
  "port": 10000,
  "publishTopic": "/kmx/state",
  "subscribeTopic": "/kmx/command",
  "cycleMs": 50,
  "wslUser": "kmxros",
  "wslDistro": "Ubuntu-22.04",
  "launchUseMoveit": true,
  "launchRviz": false,
  "launchUseMock": true,
  "robotIp": "192.168.1.100",
  "lsSpeedPercent": 100,
  "lsCnt": 100
}
JSON

echo ""
echo "==================== 完了 ===================="
echo "配布フォルダ KMX-Deploy/ を作り、次を入れて丸ごと配布:"
echo "  KMX-Installer.ps1   ui.html"
echo "  artifacts/  ← 下記一式（$OUT）"
du -sh "$OUT"/* 2>/dev/null || true
echo "新PCでは KMX-Installer.ps1 を実行 → 各ステップ［実行］→［確認］で完了。"
