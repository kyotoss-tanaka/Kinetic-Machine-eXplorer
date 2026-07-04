# KMX ⇄ ROS2 ハンドオフ（WSL側 Claude Code 向け）

あなた（WSLの `~/ros2_ws` で動く Claude Code）への引き継ぎ資料。担当は **ROS2側のビルド/実行/デバッグ/ノード拡張**。Unity(C#)側は別のWindows VSCodeの担当。

---

## 1. これは何
Unity製デジタルツイン **KMX (Kinetic Machine eXplorer)** と **ROS2** を連携させ、Unity内の **FANUC CRX-30iA** を
(a) ROS2から直接関節駆動、(b) MoveItで経路生成して動かす、システム。すべて実機Unityで**動作確認済み**（〜2026-07-04）。

## 2. 環境
- Windows11 + WSL2(Ubuntu) / **ROS2 humble** / MoveItフル導入済。
- ワークスペース:
  - `~/colcon_ws` … ROS-TCP-Endpoint（**`main-ros2` ブランチ**。Unity⇄ROS2のTCP橋渡し）
  - `~/ros2_ws` … `kmx_msgs`, `kmx_planner`, `fanuc_description`(main), `fanuc_driver`(**humble**), `fanuc_moveit_config` ほか
  - `~/ws_moveit` … MoveItソースビルド
- source（新端末ごと。`~/.bashrc` に入れると楽）:
  ```bash
  source ~/colcon_ws/install/setup.bash
  source ~/ros2_ws/install/setup.bash
  ```

## 3. ★ソースの正本と同期（最重要・誤編集注意）
- **正本 = Windows側 Unityリポの `kmx_ros2/`**（gitはそこで管理）。WSLからのパス:
  `/mnt/c/Users/gi-guest/source/repos/Kinetic Machine eXplorer/kmx_ros2`
- **`~/ros2_ws/src/kmx_planner` は sync.sh で正本からコピーされたビルド用**。**直接編集しない**（次の sync で上書きされる）。
- ノードを直すとき: **正本(`/mnt/c/.../kmx_ros2/kmx_planner`)を編集 → `sync.sh` → colcon build**。
- `kmx_msgs` は WSL側が正本（CMakeLists等）。`PlanRequest.msg` だけ正本リポから sync される。
- 同期コマンド: `bash "/mnt/c/Users/gi-guest/source/repos/Kinetic Machine eXplorer/kmx_ros2/sync.sh"`

## 4. アーキテクチャ / トピック / 単位（すべて「度」）
```
Unity ⇄ ros_tcp_endpoint ⇄ (DDS) ⇄ kmx_planner / move_group
```
| topic | 型 | 向き | 用途 |
|---|---|---|---|
| `/kmx/command` | kmx_msgs/TagArray (names,values) | Unity→ROS2 | 直接関節駆動(度) |
| `/kmx/state` | kmx_msgs/TagArray | ROS2→Unity | 現在角度(度) |
| `/kmx/plan_request` | kmx_msgs/PlanRequest (names,start,goal) | Unity→ROS2 | 経路要求(度) |
| `/kmx/trajectory` | trajectory_msgs/JointTrajectory | ROS2→Unity | 生成軌道(度) |

- CRX-30iA: MoveIt **group=`manipulator`**, **joint=`J1..J6`**（Unity /kmx と同名）。ノードが MoveItへ deg→rad、戻り rad→deg 変換。

## 5. ビルド/実行（統合launch＝1コマンド）
```bash
bash "/mnt/c/Users/gi-guest/source/repos/Kinetic Machine eXplorer/kmx_ros2/sync.sh"
cd ~/ros2_ws && colcon build --symlink-install --packages-select kmx_planner && source install/setup.bash

ros2 launch kmx_planner kmx_bringup.launch.py                    # endpoint+move_group(crx30ia)+planner（MoveIt）
ros2 launch kmx_planner kmx_bringup.launch.py use_moveit:=false  # 軽量: endpoint+planner(補間)のみ
```
個別起動が要るとき:
```bash
ros2 run ros_tcp_endpoint default_server_endpoint --ros-args -p ROS_IP:=0.0.0.0
ros2 launch fanuc_moveit_config fanuc_moveit.launch.py robot_model:=crx30ia use_mock:=true
ros2 run kmx_planner kmx_planner --ros-args -p use_moveit:=true -p planning_group:=manipulator -p moveit_joint_names:="[J1,J2,J3,J4,J5,J6]"
```
詳細は `kmx_ros2/RUN.md`（起動手順）と `kmx_ros2/README.md`（初回セットアップA〜E）。

## 6. ノード実装（kmx_planner/kmx_planner/planner_node.py）
- `use_moveit:=false` → `plan_interpolate`（関節空間 smoothstep 補間、MoveIt不要）。
- `use_moveit:=true` → **MoveGroupアクション `plan_only` を `send_goal_async`（非同期）**。始点=`start_state.joint_state`(rad絶対)、終点=`JointConstraint`。結果は `_convert_result` で rad→deg＋`J1..J6`順へ。
- params: `use_moveit`, `planning_group`(=manipulator), `moveit_joint_names`(=[J1..J6]), `duration_sec`, `num_points`, `allowed_planning_time`, `vel_scale`, `acc_scale`。
- メッセージ: `PlanRequest` は `kmx_msgs`。`JointTrajectory` は **ROS-TCP-Connector 同梱**（Unity側は生成不要）。

## 7. 版固定（変えない・過去に長時間ハマった）
- ROS-TCP-Connector（Unity UPM）= **`#v0.7.0`**
- ROS-TCP-Endpoint（`~/colcon_ws`）= **`main-ros2` ブランチ**（`v0.7.0` タグは ROS1 で不可）
- `fanuc_driver` = **humble** ブランチ、`fanuc_description` = main。**git-lfs 必須**、**submodule(sockpp) の `git submodule update --init --recursive` 必須**。

## 8. gotcha
- **`/mnt/c` から colcon build は遅い/不安定** → 必ず `~/ros2_ws`(ネイティブ)でビルド。
- `--symlink-install` と通常 `colcon build` を混在させると symlink 衝突 → `rm -rf build install log` で解決。
- **plan_only なので move_group/RViz 上ではロボットは動かない（正常）**。可視化は Unity。
- endpoint を Unity 起動後に再起動すると `/kmx/state` の "Not registered" が一時噴出→回復（無害）。順序は endpoint→Unity が綺麗。
- 実機FANUCには繋がない → `use_mock:=true`。

## 9. 状況・残タスク
- **検証済**: 直接駆動 / 補間経路 / MoveIt(実CRX-30iA config) すべて実機Unityで動作。
- **未検証（あなたが最初に確認）**: 統合launch `kmx_bringup.launch.py` と `sync.sh`（今回追加分）。まず `use_moveit:=false` で起動確認 → 次に MoveIt。
- 任意: FANUC URDF のゼロ点/符号が Unity(d_robo_a) と食い違えば `_convert_result` に補正。/kmx/state の `ros2 topic echo` 確認。
- **コードレビュー修正(2026-07-04)を planner_node.py に反映済**（詳細は `OBSTACLES_ROS2_SPEC.md` §6）: Obstacles import を try/except 保護／`_convert_result` は関節名不一致で発行中止（0埋め廃止）／既定 `moveit_joint_names`=J1..J6／`_obstacle_ids` は apply 成功後に確定。WSLで別途編集していれば `sync.sh` 取り込み時に要マージ。
- 既知の残(任意対応): `wait_for_server(3s)` が単一executorをブロック／planner再起動時の planning scene 乖離。

## 10. Unity側（参考・別VSCode担当。ここは触らない）
- C#: `Assets/Scripts/Com/Ros2/`（ComRos2 / RosTcpConnectorTransport / ComRos2PathPlanner）。`GlobalScript`, `ParameterLoader`, `BuildAndRun` にも変更。
- `KMX_ROS2` define がONのときだけ実通信。Unityメニュー **`Kyotoss/ROS2連携を有効化`** でトグル。
- コミット: `refine-URP` の `26cfc94`（ROS2連携一式）。`kmx_ros2` 改称＋統合launch/sync は未コミット（別途）。
