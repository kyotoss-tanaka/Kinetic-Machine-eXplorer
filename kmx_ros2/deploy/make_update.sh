#!/usr/bin/env bash
# =====================================================================
# KMX 差分アップデート キット生成（旧PC=ビルド機で実行）
#   コード(kmx_planner / register / config / msg)を変えて colcon build 済みの状態で実行。
#   BITstar MoveIt は再ビルドしない＝速い（make_kit.sh のフル版と違い数秒）。
#   生成: ros2_src.tgz / scripts.tgz / apply_update.sh を artifacts へ。
#
#   使い方: bash make_update.sh [出力先(既定 ~/KMX-Deploy/artifacts)]
#   → 出力先を新PCへコピー → 新PC(WSL)で bash apply_update.sh
# =====================================================================
set -eo pipefail
OUT="${1:-$HOME/KMX-Deploy/artifacts}"
WS="$HOME/ros2_ws"
HERE="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$OUT"
echo "== 差分キット生成 → $OUT （MoveIt は再ビルドしない）=="

echo "-- fanuc / kmx (~/ros2_ws/src) を固める（メッシュ除外＝軽量。FULL=1 で全部）"
if [ "${FULL:-0}" = "1" ]; then
  # メッシュ込みフル（初回 or メッシュ/URDF大変更時）
  tar czf "$OUT/ros2_src.tgz" --exclude='__pycache__' --exclude='*.pyc' -C "$WS" src
else
  # 差分の既定：コード/config/urdf は全パッケージ拾い、不変の重いメッシュだけ除外（~1MB）。
  # 新PCの既存メッシュは tar 上書きされず残る（初回インストール以降 fanuc_description は不変）。
  tar czf "$OUT/ros2_src.tgz" --exclude='.git' --exclude='__pycache__' --exclude='*.pyc' \
    --exclude='*.stl' --exclude='*.STL' --exclude='*.dae' --exclude='*.DAE' \
    --exclude='*.obj' --exclude='*.OBJ' --exclude='*.ply' --exclude='*.PLY' \
    --exclude='*.png' --exclude='*.jpg' --exclude='*.jpeg' \
    -C "$WS" src
fi

echo "-- 起動スクリプト(kmx_*.sh) を固める"
( cd "$WS" && ls kmx_*.sh >/dev/null 2>&1 && tar czf "$OUT/scripts.tgz" -C "$WS" $(ls kmx_*.sh) ) || echo "  (kmx_*.sh 無し・スキップ)"

echo "-- 新PC側 適用スクリプトを同梱"
cp -f "$HERE/apply_update.sh" "$OUT/apply_update.sh" && chmod +x "$OUT/apply_update.sh"

echo ""
echo "==================== 完了 ===================="
du -sh "$OUT/ros2_src.tgz" "$OUT/scripts.tgz" "$OUT/apply_update.sh" 2>/dev/null || true
cat <<EOF

新PCでの適用:
  1) $OUT を新PCの任意フォルダにコピー（Windows経由でも可）
  2) WSL で:  bash <コピー先>/apply_update.sh
       ROS-TCP-Endpoint も更新するなら:  UPDATE_ENDPOINT=1 bash <コピー先>/apply_update.sh
  3) msg(PlanRequest 等)を変えた場合のみ Unity で Robotics > Generate ROS Messages を再生成
  ※ MoveIt/BITstar を変えた時は make_update ではなく make_kit.sh（フル）を使う
EOF
