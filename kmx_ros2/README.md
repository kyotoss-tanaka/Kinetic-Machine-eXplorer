# KMX ⇄ ROS2 経路生成（scaffold）

Unity で **始点/終点（関節角 J1..J6・度）** を渡し、ROS2 で経路生成して、生成軌道を Unity の CRX-30iA で再生する連携の ROS2 側一式。

```
Unity(ComRos2PathPlanner)
  ── /kmx/plan_request (kmx_msgs/PlanRequest, deg) ──▶ kmx_planner ノード
                                                          │  MoveIt or 補間
  ◀── /kmx/trajectory (trajectory_msgs/JointTrajectory, deg) ──┘
Unity: 軌道を時間補間しながら d_robo_a1..a6 に再生（既存 ComRos2 のマッピング/解決を再利用）
```

- **単位は /kmx/* 全体で「度」**（既存 /kmx/command と一貫）。MoveIt を使う場合はノード内で deg↔rad 変換。
- リアルタイム tag 同期（/kmx/command, /kmx/state, ComRos2）はそのまま並行動作。

このフォルダは Unity リポジトリに置いた**参照用**。実体は WSL2 の ROS2 ワークスペースへコピーして使う。

---

## 前提（既存のROS2連携が動いていること）
- `~/ros2_ws/src/kmx_msgs`（kmx_msgs/TagArray がある）、`~/colcon_ws/src/ROS-TCP-Endpoint`（`main-ros2` ブランチ）。
- Unity: ROS-TCP-Connector `#v0.7.0`、`Kyotoss/ROS2連携を有効化` ON（`KMX_ROS2`）。
- endpoint 起動: 両ws source 後 `ros2 run ros_tcp_endpoint default_server_endpoint --ros-args -p ROS_IP:=0.0.0.0`

## A. kmx_msgs に PlanRequest を追加
1. `kmx_ros2/kmx_msgs/msg/PlanRequest.msg` を `~/ros2_ws/src/kmx_msgs/msg/PlanRequest.msg` へコピー。
2. `kmx_msgs/CMakeLists.txt` の `rosidl_generate_interfaces(...)` に追加:
   ```cmake
   rosidl_generate_interfaces(${PROJECT_NAME}
     "msg/TagArray.msg"
     "msg/PlanRequest.msg"      # ← 追加
     DEPENDENCIES builtin_interfaces
   )
   ```
3. ビルド:
   ```bash
   cd ~/ros2_ws && colcon build --packages-select kmx_msgs && source install/setup.bash
   ```

## B. Unity 側メッセージ再生成
`Robotics > Generate ROS Messages...` で以下を生成（`Assets/RosMessages` に出力）:
- `kmx_msgs`（PlanRequest が増える → `RosMessageTypes.Kmx.PlanRequestMsg`）
- `trajectory_msgs`（`RosMessageTypes.Trajectory.JointTrajectoryMsg`）※MoveItや標準に含まれる
- 依存の `builtin_interfaces`（Duration）も生成されること
→ これで `RosTcpConnectorTransport.cs` の未解決（PlanRequestMsg / JointTrajectoryMsg）が解消する。

## C. kmx_planner ノードをビルド
```bash
cp -r kmx_ros2/kmx_planner ~/ros2_ws/src/     # このフォルダをws/srcへ
cd ~/ros2_ws && colcon build --packages-select kmx_planner && source install/setup.bash
```

## D. まず補間モードで往復検証（MoveIt不要）
```bash
ros2 run kmx_planner kmx_planner --ros-args -p use_moveit:=false -p duration_sec:=3.0 -p num_points:=30
```
- Unity を Play → `GlobalSetting` の **ComRos2PathPlanner** を右クリック → `Test Plan (current→goal)`。
  - `testGoalDeg` に終点（度・6軸）を入れておく。始点は現在角度。
- ノードが `/kmx/trajectory` を発行 → Unity Console に `軌道受信: N点 …` → **CRXが終点までスムーズに動けば往復成立**。
- 手動確認: `ros2 topic echo /kmx/trajectory --once`

## E. MoveIt モード（本命）
`plan_with_moveit` は実装済み（move_group の MoveGroup アクションに plan_only で joint 目標を投げ、
返り値 rad→deg・J1..J6順へ変換して発行）。ノード側にconfigを読ませないので setup が軽い。

### E-1. まず代役 config（moveit_resources_fanuc）で MoveIt 往復を実証
CRX 専用 config が無くても、humble 同梱の fanuc チュートリアル config（6軸・group=`manipulator`・
joint=`joint_1..6`）で「Unity→計画→軌道→再生」を今すぐ検証できる（寸法/リミットは M-10iA 相当）。
```bash
# 端末X: move_group 起動（RVizも上がる。WSLg でGUI表示）
ros2 launch moveit_resources_fanuc_moveit_config demo.launch.py

# 端末B: ノードを MoveIt モードで（既定 group=manipulator / joint_1..6 が fanuc に一致）
ros2 run kmx_planner kmx_planner --ros-args -p use_moveit:=true

# Unity: Test Plan (start→goal)。ノードに "published trajectory: N points (moveit)" が出て
#        Unity で CRX が計画経路を再生すれば MoveIt 往復成立。
```

### E-2. 実 CRX-30iA へ差し替え
1. CRX-30iA の **moveit_config**（URDF/SRDF）を用意（FANUC CRX description の入手 or MoveIt Setup Assistant で自作）。
2. その config で move_group を launch。
3. ノード起動時に group 名・joint 名を合わせる:
   ```bash
   ros2 run kmx_planner kmx_planner --ros-args -p use_moveit:=true \
     -p planning_group:=<CRXのgroup名> \
     -p moveit_joint_names:="[<j1>,<j2>,<j3>,<j4>,<j5>,<j6>]"
   ```
   `moveit_joint_names` は Unity の J1..J6 と**インデックス対応**。符号/ゼロ点が違えば `_convert_result` で補正。

---

## トピック / 型 / 単位
| 方向 | topic | 型 | 単位 |
|---|---|---|---|
| Unity→ROS2 | `/kmx/plan_request` | `kmx_msgs/PlanRequest` (names, start, goal) | 度 |
| ROS2→Unity | `/kmx/trajectory` | `trajectory_msgs/JointTrajectory` | 度 |

## 関節対応（キャリブレーション注意）
- Unity は `J1..J6 → d_robo_a1..a6` を**度そのまま**で駆動（rates=1）。単軸JOG（J1=30で基部回転）で検証済みの並び。
- MoveIt(URDF) の関節名/符号/ゼロ点が Unity と違う場合、`plan_with_moveit` の並べ替えや符号補正で吸収する。まず補間モードで並び・向きを確認してから MoveIt を繋ぐと安全。
