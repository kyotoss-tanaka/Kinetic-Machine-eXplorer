#!/usr/bin/env bash
# =====================================================================
# KMX Deploy 各ステップの実体（KMX-Installer.ps1 が wsl 経由で source して呼ぶ）
#   環境変数: KIT_WSL = artifacts の WSL パス（/mnt/c/.../artifacts）
#             WSL_USER = 対象 WSL ユーザー名
#   各ステップ sN_run / sN_verify。verify は成功時に "VERIFY_OK" を出力する。
# =====================================================================

# ---- 1) 社内プロキシ / Zscaler SSL 証明書を信頼ストアへ（root） ----
s1_run(){
  openssl s_client -connect github.com:443 -servername github.com -showcerts </dev/null 2>/dev/null \
    | sed -n '/-----BEGIN CERTIFICATE-----/,/-----END CERTIFICATE-----/p' > /tmp/zs.pem
  csplit -z -f /usr/local/share/ca-certificates/zscaler- -b '%02d.crt' /tmp/zs.pem '/-----BEGIN CERTIFICATE-----/' '{*}' 2>/dev/null || true
  update-ca-certificates
}
s1_verify(){ curl -sS https://raw.githubusercontent.com/ros/rosdistro/master/ros.key -o /dev/null && echo VERIFY_OK; }

# ---- 2) ROS2 Humble + 依存 apt（root） ----
s2_run(){
  set -e
  export DEBIAN_FRONTEND=noninteractive
  apt-get update
  apt-get install -y locales curl gnupg lsb-release
  locale-gen en_US.UTF-8
  install -m0755 -d /usr/share/keyrings
  curl -sSL https://raw.githubusercontent.com/ros/rosdistro/master/ros.key -o /usr/share/keyrings/ros-archive-keyring.gpg
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/ros-archive-keyring.gpg] http://packages.ros.org/ros2/ubuntu jammy main" \
    > /etc/apt/sources.list.d/ros2.list
  apt-get update
  apt-get install -y ros-humble-desktop ros-dev-tools ros-humble-moveit ros-humble-pinocchio ros-humble-coal \
    python3-numpy python3-yaml ros-humble-moveit-ros-control-interface ros-humble-ros2-control ros-humble-ros2-controllers
}
s2_verify(){ source /opt/ros/humble/setup.bash && ros2 pkg list | grep -q moveit_ros_move_group && echo VERIFY_OK; }

# ---- 3) BITstar 入り MoveIt を /opt/kmx_moveit へ展開（root） ----
s3_run(){
  mkdir -p /opt
  tar xzf "$KIT_WSL/kmx_moveit.tgz" -C /opt
  # ldd で move_group の依存を正しく辿るには ROS 環境を source する必要がある。未source だと直接依存
  # (rclcpp 等) すら解決できず、geometric_shapes のような“推移的依存”が ldd 出力に現れないため、
  # 下の symlink 対象検出が空振りする（＝版ズレを吸収し損ねて move_group が起動できない）。
  source /opt/ros/humble/setup.bash 2>/dev/null
  source /opt/kmx_moveit/setup.bash 2>/dev/null
  # apt の geometric_shapes 版ズレ吸収: move_group が要求する版を、対象に実在する版へ symlink（root 実行前提）
  mg=/opt/kmx_moveit/moveit_ros_move_group/lib/moveit_ros_move_group/move_group
  real=$(find /opt/ros/humble/lib -maxdepth 1 -type f -name 'libgeometric_shapes.so.*' 2>/dev/null | head -1)
  if [ -n "$real" ] && [ -x "$mg" ]; then
    for want in $(ldd "$mg" 2>/dev/null | grep -oE 'libgeometric_shapes\.so\.[0-9.]+' | sort -u); do
      [ -e "/opt/ros/humble/lib/$want" ] || ln -sf "$real" "/opt/ros/humble/lib/$want"
    done
  fi
  echo done
}
s3_verify(){
  test -f /opt/kmx_moveit/setup.bash || { echo NO_SETUP; return; }
  # move_group は RPATH を持たず、実行時に setup.bash(LD_LIBRARY_PATH) でライブラリを解決する。
  # ldd も同じ環境で行わないと /opt/ros/humble・/opt/kmx_moveit 配下が全部 not found に見える（誤検知）。
  source /opt/ros/humble/setup.bash 2>/dev/null
  source /opt/kmx_moveit/setup.bash 2>/dev/null
  mg=/opt/kmx_moveit/moveit_ros_move_group/lib/moveit_ros_move_group/move_group
  if ldd "$mg" 2>/dev/null | grep -q "not found"; then echo "missing libs:"; ldd "$mg" | grep "not found"; else echo VERIFY_OK; fi
}

# ---- 4) fanuc / kmx / endpoint を展開＋ユーザ名置換＋RViz/slider（user） ----
s4_run(){
  set -e
  local H="$HOME"
  mkdir -p "$H/ros2_ws/src" "$H/colcon_ws/src"
  tar xzf "$KIT_WSL/ros2_src.tgz" -C "$H/ros2_ws"
  tar xzf "$KIT_WSL/endpoint_src.tgz" -C "$H/colcon_ws"
  [ -f "$KIT_WSL/scripts.tgz" ] && tar xzf "$KIT_WSL/scripts.tgz" -C "$H/ros2_ws"
  find "$H/ros2_ws" -name __pycache__ -type d -exec rm -rf {} + 2>/dev/null || true   # 壊れた/古い .pyc を除去（bad marshal 対策）
  find "$H/ros2_ws" -name '*.pyc' -delete 2>/dev/null || true
  grep -rl '/home/kyotoss/' "$H/ros2_ws" 2>/dev/null | xargs -r sed -i "s#/home/kyotoss/#$H/#g"
  chmod +x "$H"/ros2_ws/kmx_*.sh 2>/dev/null || true
  local MOVEIT MOCK
  MOVEIT=$(find "$H/ros2_ws/src" -path '*fanuc_moveit_config/launch/fanuc_moveit.launch.py' | head -1)
  MOCK=$(find "$H/ros2_ws/src" -path '*fanuc_hardware_interface/launch/fanuc_mock_control.launch.py' | head -1)
  if [ -n "$MOCK" ] && ! grep -q 'KMX: slider' "$MOCK"; then
    sed -i 's/^\(\s*\)nodes_to_launch\.append(slider_test_node)/\1pass  # KMX: slider removed/' "$MOCK"
  fi
  if [ -n "$MOVEIT" ] && ! grep -q 'KMX_RVIZ' "$MOVEIT"; then
    sed -i 's/^\(\s*\)nodes_to_launch\.append(rviz_node)/\1import os as _kmxos\n\1if _kmxos.environ.get("KMX_RVIZ","0") in ("1","true","True"): nodes_to_launch.append(rviz_node)/' "$MOVEIT"
  fi
  echo done
}
s4_verify(){ test -d "$HOME/ros2_ws/src" && test -d "$HOME/colcon_ws/src/ROS-TCP-Endpoint" && echo VERIFY_OK; }

# ---- 5) colcon build（必要分のみ・BITstar MoveIt に対して）（user） ----
s5_run(){
  set -e
  source /opt/ros/humble/setup.bash
  source /opt/kmx_moveit/setup.bash
  cd "$HOME/colcon_ws" && colcon build --symlink-install
  source "$HOME/colcon_ws/install/setup.bash"
  cd "$HOME/ros2_ws" && colcon build --symlink-install \
    --packages-up-to kmx_planner fanuc_moveit_config fanuc_hardware_interface fanuc_controllers slider_publisher
  echo done
}
s5_verify(){ test -f "$HOME/ros2_ws/install/setup.bash" && test -f "$HOME/colcon_ws/install/setup.bash" && echo VERIFY_OK; }

# ---- 6) 設定（.bashrc source / KMX_RVIZ）（user） ----
s6_run(){
  local B="$HOME/.bashrc"
  _add(){ grep -qF "$1" "$B" || echo "$1" >> "$B"; }
  _add 'source /opt/ros/humble/setup.bash'
  _add 'source /opt/kmx_moveit/setup.bash 2>/dev/null'
  _add 'source ~/colcon_ws/install/setup.bash 2>/dev/null'
  _add 'source ~/ros2_ws/install/setup.bash 2>/dev/null'
  _add 'export KMX_RVIZ=0'
  echo done
}
s6_verify(){ source /opt/ros/humble/setup.bash && source /opt/kmx_moveit/setup.bash && python3 -c 'import coal,pinocchio' && echo VERIFY_OK; }

# ---- 7) 起動 + BITstar 疎通（user） ----
s7_run(){ "$HOME/ros2_ws/kmx_start.sh" true 0; sleep 2; "$HOME/ros2_ws/kmx_status.sh"; }
s7_verify(){ st=""; for i in $(seq 1 40); do st=$("$HOME/ros2_ws/kmx_status.sh"); if [ "$st" = running_full ]; then echo "status=$st"; echo VERIFY_OK; return; fi; sleep 3; done; echo "status=$st （running_full に到達せず・kmx_bringup.log 確認）"; }

# =====================================================================
# アップデート専用ステップ（mode=update）。新規インストール(1-7)は不要で、
# 既存環境にコード更新(kmx_planner/register/config/msg)だけ反映する。
# BITstar MoveIt(/opt/kmx_moveit)・apt・証明書・.bashrc は触らない。
# 実体は artifacts に同梱の apply_update.sh（CLI からも同じものが使える＝DRY）。
# =====================================================================
# ---- 8) コード更新（展開→colcon build→再起動）（user） ----
s8_run(){
  [ -f "$KIT_WSL/apply_update.sh" ] || { echo "ERR: $KIT_WSL/apply_update.sh が無い（make_update.sh で生成した artifacts か確認）"; return 1; }
  bash "$KIT_WSL/apply_update.sh" "$KIT_WSL"   # 展開→ユーザ名置換→build(kmx_msgs→kmx_planner)→kmx_restart→running_full 待ち
}
s8_verify(){
  st=""; for i in $(seq 1 20); do st=$("$HOME/ros2_ws/kmx_status.sh" 2>/dev/null || echo); if [ "$st" = running_full ]; then echo "status=$st"; echo VERIFY_OK; return; fi; sleep 3; done
  echo "status=$st （running_full に到達せず・~/ros2_ws/kmx_bringup.log 確認）"
}
