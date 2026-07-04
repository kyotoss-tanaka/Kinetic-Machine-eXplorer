# KMX ⇄ ROS2 起動手順（次回用クイックスタート）

WSL2（例: `kyotoss@LEP3-014`）での毎回の起動コマンド集。
- endpoint = `~/colcon_ws`（ROS-TCP-Endpoint, `main-ros2` ブランチ）
- kmx_msgs / kmx_planner = `~/ros2_ws`
- Unity リポジトリ（WSLから）= `/mnt/c/Users/gi-guest/source/repos/Kinetic Machine eXplorer`

---

## 0. 各ターミナルで source（新しい端末を開くたび）
```bash
# ROS2 本体が .bashrc で source されていなければ最初に（distro は環境依存）:
# source /opt/ros/<distro>/setup.bash
source ~/colcon_ws/install/setup.bash   # ros_tcp_endpoint
source ~/ros2_ws/install/setup.bash     # kmx_msgs / kmx_planner / trajectory_msgs
```

## 1. 端末A：ROS-TCP-Endpoint 起動
```bash
ros2 run ros_tcp_endpoint default_server_endpoint --ros-args -p ROS_IP:=0.0.0.0
```
- `Starting server on 0.0.0.0:10000` が出ればOK。
- ※ **endpoint を先に起動 → その後 Unity を Play** の順が綺麗（逆だと `Not registered to publish '/kmx/state'` が一時的に出るが無害）。

## 2. 端末B：経路生成ノード 起動
```bash
# 補間モード（MoveIt不要・まずこれで往復確認）
ros2 run kmx_planner kmx_planner --ros-args -p use_moveit:=false -p duration_sec:=3.0

# MoveIt モード（実CRX-30iA・FANUC公式 config）→ 先に move_group を別端末で起動:
#   端末X: ros2 launch fanuc_moveit_config fanuc_moveit.launch.py robot_model:=crx30ia use_mock:=true
#          （use_mock:=true=モックHWで実機不要。RVizは触れなくてOK＝可視化はUnity）
# それからノードを MoveIt モードで（CRX-30iA: group=manipulator / joint=J1..J6）:
ros2 run kmx_planner kmx_planner --ros-args -p use_moveit:=true -p planning_group:=manipulator -p moveit_joint_names:="[J1,J2,J3,J4,J5,J6]"
# 代役の tutorial config を使う場合: ros2 launch moveit_resources_fanuc_moveit_config demo.launch.py
#   （そちらは joint_1..6 なので -p moveit_joint_names は既定のままでよい）
```
- `kmx_planner ready: sub='/kmx/plan_request' pub='/kmx/trajectory' use_moveit=False`（or True）が出ればOK。

## 3. Unity 側
- `Kyotoss/ROS2連携を有効化` が **ON**（`KMX_ROS2`）／`Robotics > ROS Settings` Protocol=ROS2, IP=127.0.0.1, Port=10000。
- **Play** → 起動時 Console に ComRos2 の `resolve tag='d_robo_a1..a6' → …` が6本出れば駆動準備OK。
- 直接駆動テスト（JOG的）: 下の `topic pub /kmx/command`。
- 経路生成テスト: `GlobalSetting` の **ComRos2PathPlanner** を右クリック → `Test Plan (start→goal)` or `(current→goal)`（`testStartDeg`/`testGoalDeg` に度で設定。**goalを非0に**しないと動かない）。

---

## 確認・デバッグ用コマンド
```bash
ros2 topic list                          # トピック一覧
ros2 topic echo /kmx/state               # Unityが発行する現在の関節角(度)を監視
ros2 topic echo /kmx/trajectory --once   # 生成軌道を1回表示
ros2 node list                           # /UnityEndpoint, /kmx_planner が見えるか

# 直接1軸駆動（度）。基部J1を30度へ:
ros2 topic pub --once /kmx/command kmx_msgs/msg/TagArray \
  "{names: ['J1','J2','J3','J4','J5','J6'], values: [30.0, 0.0, 0.0, 0.0, 0.0, 0.0]}"

# 経路生成をCLIから要求（Unityの代わりに手動トリガ）:
ros2 topic pub --once /kmx/plan_request kmx_msgs/msg/PlanRequest \
  "{names: ['J1','J2','J3','J4','J5','J6'], start: [0,0,0,0,0,0], goal: [45,20,0,0,0,0]}"
```

## ビルドが要るとき（コード / .msg を変更したときだけ）
```bash
# kmx_msgs を変更（PlanRequest.msg 追加/変更など）
cd ~/ros2_ws && colcon build --packages-select kmx_msgs && source install/setup.bash
#   → Unity 側も Robotics > Generate ROS Messages で kmx_msgs 再生成

# kmx_planner のコードを変更（planner_node.py など）
cd ~/ros2_ws && colcon build --packages-select kmx_planner && source install/setup.bash

# リポジトリの最新ノードを WSL へ再コピー（スペース含むパスは要クォート）
cp -r "/mnt/c/Users/gi-guest/source/repos/Kinetic Machine eXplorer/ROS2/kmx_planner" ~/ros2_ws/src/
```

## トピック / 型 / 単位（早見）
| 方向 | topic | 型 | 単位 |
|---|---|---|---|
| Unity→ROS2 | `/kmx/command` | `kmx_msgs/TagArray` (names, values) | 度（関節駆動）|
| Unity→ROS2 | `/kmx/plan_request` | `kmx_msgs/PlanRequest` (names, start, goal) | 度 |
| ROS2→Unity | `/kmx/state` | `kmx_msgs/TagArray` | 度（現在角度）|
| ROS2→Unity | `/kmx/trajectory` | `trajectory_msgs/JointTrajectory` | 度 |

## バージョン固定（ハマりどころ・変更しない）
- ROS-TCP-Connector（Unity側 UPM）= **`#v0.7.0`**
- ROS-TCP-Endpoint（`~/colcon_ws`）= **`main-ros2` ブランチ**（`v0.7.0` タグは ROS1 なので使わない）
- 不一致だと握手が `JSONDecodeError` で切断ループになる。
