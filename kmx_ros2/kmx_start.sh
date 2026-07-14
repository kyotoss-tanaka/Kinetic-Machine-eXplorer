#!/usr/bin/env bash
# KMX bringup 起動（Unity/wsl.exe から呼ばれる想定）。
#   使い方: kmx_start.sh [use_moveit(true|false)]  既定 true
#   冪等: 既に起動中なら何もしない。bringup を起動し、running_full(安定)まで待ってから return
#         （呼び出し元 wsl.exe が確立前に抜けると WSL2 に刈られるため。Unity は別スレッドで呼ぶ想定）。
#   ログ: ~/ros2_ws/kmx_bringup.log。稼働 PID: ~/ros2_ws/.kmx_bringup.pid
WS="/home/kyotoss/ros2_ws"
PIDFILE="$WS/.kmx_bringup.pid"
LOG="$WS/kmx_bringup.log"
USE_MOVEIT="${1:-true}"
RVIZ="${2:-0}"                  # 第2引数: RViz 表示 0/1（Ros2Info.json launchRviz 由来）。fanuc_moveit.launch.py が KMX_RVIZ を参照
export KMX_RVIZ="$RVIZ"

if pgrep -f "ros2 launch kmx_planner kmx_bringup" >/dev/null 2>&1; then
  echo "[kmx] already running（二重起動しません）"
  exit 0
fi

# ROS 環境を source（子プロセスへ継承させる）
source /opt/ros/humble/setup.bash 2>/dev/null
source /home/kyotoss/colcon_ws/install/setup.bash 2>/dev/null
source /home/kyotoss/ws_moveit/install/setup.bash 2>/dev/null
source /opt/kmx_moveit/setup.bash 2>/dev/null      # 配布先: BITstar MoveIt（固定パス /opt/kmx_moveit）
source "$WS/install/setup.bash" 2>/dev/null

# 端末から切り離した新セッションで起動（wsl.exe が抜けても生存）。PID=セッションリーダ。
setsid bash -c "exec ros2 launch kmx_planner kmx_bringup.launch.py use_moveit:=$USE_MOVEIT" \
  >"$LOG" 2>&1 < /dev/null &
echo $! > "$PIDFILE"
echo "[kmx] starting (pid $!, use_moveit=$USE_MOVEIT)  log=$LOG"

# ★重要（Unity/wsl.exe から呼ぶ場合の必須処理）:
#   ros2 launch は多プロセス(12+ノード)のため、呼び出し元(wsl.exe)のセッションが「確立前」に
#   抜けると WSL2 が子プロセスごと刈り取り、ログ空のまま即死する（単発の sleep 等は生き残るが
#   launch は起動途中で間に合わない）。そこで running_full（＝安定）に達するまでここで待ってから
#   return し、起動ウィンドウを跨ぐ。到達後は wsl.exe が抜けても bringup は生存する（実測確認済）。
#   Unity は本スクリプトを別スレッドで呼ぶ想定なので、この待ちで UI はブロックしない。
STABLE=0
DEADLINE=$((SECONDS + 45))
while [ $SECONDS -lt $DEADLINE ]; do
  sleep 0.5
  st="$("$WS/kmx_status.sh")"
  if [ "$USE_MOVEIT" = "true" ]; then
    if [ "$st" = "running_full" ]; then
      echo "[kmx] running_full（安定）"
      exit 0
    fi
  else
    # 軽量モード(use_moveit=false)は move_group を上げない。planner が数回連続で確認できたら準備完了。
    if pgrep -f "lib/kmx_planner/kmx_planner" >/dev/null 2>&1; then
      STABLE=$((STABLE + 1))
      if [ $STABLE -ge 4 ]; then
        echo "[kmx] planner up（軽量・安定）"
        exit 0
      fi
    fi
  fi
done
echo "[kmx] warning: 起動確立を時間内に確認できず（継続中の可能性。kmx_status.sh で確認）"
