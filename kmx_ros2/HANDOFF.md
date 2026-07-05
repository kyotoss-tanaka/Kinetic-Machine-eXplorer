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
- `kmx_msgs` は WSL側が正本（CMakeLists/package.xml）。`.msg` 実体（`PlanRequest.msg` / `Obstacles.msg` / `ObstaclePrimitive.msg`）は正本リポから sync される。新規 .msg を足したら CMakeLists 登録＋依存追加は WSL側で手動（例: 障害物系は `geometry_msgs` 依存を追加済み）。
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
| `/kmx/obstacles` | kmx_msgs/Obstacles (frame_id,items[]) | Unity→ROS2 | 障害物→planning scene（**メートル**・base_link相対・**世界CollisionObject**） |
| `/kmx/attached` | **kmx_msgs/Obstacles（同型を流用）** | Unity→ROS2 | ヘッド(ツール)→**AttachedCollisionObject**（**メートル**・**frame_id=attach先リンク**相対） |

- CRX-30iA: MoveIt **group=`manipulator`**, **joint=`J1..J6`**（Unity /kmx と同名）。ノードが MoveItへ deg→rad、戻り rad→deg 変換。
- ⚠ **単位の例外**: 関節系(`command`/`state`/`plan_request`/`trajectory`)は「度」だが、`/kmx/obstacles`・`/kmx/attached` は **メートル**（幾何プリミティブ）。

### 4.1 障害物 / ヘッド の契約（★ROS2側と認識合わせ・2026-07-05）
- **`/kmx/obstacles`（世界障害物）**: `Obstacles{frame_id, items[]}`。`frame_id`=`base_link`。各 item は
  `ObstaclePrimitive{id, type, dimensions[], pose}`。**現状 Unity は全て type=1(BOX)・軸整列AABB・pose.orientation=単位**で送る
  （CAD由来コライダーの向き問題を避けるため向きは持たせない）。id は**階層パスの安定ID**（GetInstanceID廃止＝Play跨ぎで残留しない）。
- **`/kmx/attached`（ヘッド=ツール）**: 型は `Obstacles` を流用。**`frame_id` に attach 先の URDF リンク名**（例 `flange`/`tool0`）を入れる。
  ROS2 側は各 item を **`AttachedCollisionObject{ link_name=frame_id, object=CollisionObject(items), touch_links=<param> }`** 化して
  `robot_state.attached_collision_objects` に反映。**touch_links は最低 attach リンク自身が必須**（無いと自己衝突で計画不能）。
- **更新規約（両トピック共通）＝全置換**: 受信のたびに、今回 id を ADD（同一 id 置換）／前回あって今回無い id を REMOVE。
  **world と attached は別集合で管理**すること（`_obstacle_ids` と別に `_attached_ids`）。空配列受信＝全消し。
- **タイミング**: Unity は TestPlan 前に obstacles/attached を送り **約0.4s 待ってから** `/kmx/plan_request` を出す
  （`ComRos2PathPlanner.sceneSettleSec`）。ROS2 側の scene 反映（service）がこの猶予で間に合う前提。間に合わなければ猶予を延ばす。
- **座標補正の所在（★認識合わせの肝・二重補正禁止）**: Unity は「基準リンク相対・ROS(FLU)・メートル」で送る。
  基準リンクの Unity 軸 ↔ URDF リンク軸のズレは **ストリームごとに1か所だけ**で吸収する:
  - **`/kmx/obstacles`（base_link）→ Unity 側 `baseCalibrationEuler`=(0,-90,0)**（検証済）。ROS2 は obstacles を補正しない。
  - **`/kmx/attached`（flange）→ ROS2 側 `head_calibration_rpy`（ros2 param・ライブ調整可）**。Unity は**生(raw)で送る**
    （`ComRos2Obstacles` の Unity 側ヘッド補正は撤去済）。**両方を同時に掛けない**こと。
    **★CRX-30iA FANUCヘッドの確定値 = `[0,90,90]`（実機確認 2026-07-05）→ planner_node.py の param 既定に焼込済。**
- **arm3 の注意（Unity表示側の話）**: 実機(OPC UA)は3軸目が J2連成値だが、**ROS/`/kmx` は純粋な関節角(度)で統一**。ノードは従来どおり純粋角で扱えばよい。

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
- **障害物 → planning scene**: `/kmx/obstacles`(kmx_msgs/Obstacles) を購読 → `moveit_msgs/CollisionObject` 化 → **`/apply_planning_scene` サービス**で move_group の planning scene に反映（未準備時は `/planning_scene` publish で fallback）。反映後は `plan_only` が自動で障害物を回避。受信のたび**全置換**（同一id ADD で置換／消えたidは REMOVE、`_obstacle_ids` は apply 成功後に確定）。詳細・検証結果は `OBSTACLES_ROS2_SPEC.md` §6。
- params: `use_moveit`, `planning_group`(=manipulator), `moveit_joint_names`(=[J1..J6]), `duration_sec`, `num_points`, `allowed_planning_time`, `vel_scale`, `acc_scale`, `obstacles_topic`, `apply_scene_service`, `planning_scene_topic`。
- メッセージ: `PlanRequest` / `Obstacles` / `ObstaclePrimitive` は `kmx_msgs`（障害物系は `geometry_msgs` 依存）。`JointTrajectory` は **ROS-TCP-Connector 同梱**（Unity側は生成不要）。
- 堅牢化: `Obstacles` import は try/except で保護（未ビルドでも PlanRequest 経路は起動）。`_convert_result` は関節名不一致時に発行中止（0埋め廃止）。

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
- **検証済**: 直接駆動 / 補間経路 / MoveIt(実CRX-30iA config) は実機Unityで動作。統合launch `kmx_bringup.launch.py`(both modes) と `sync.sh` も検証済(2026-07-04)＝ROS2起動〜実機Unityでの MoveIt 往復駆動まで確認。障害物→planning scene は ROS2側(CLI模擬)で検証済（scene反映 success=True / 計画ゲート / 全置換）。
- **未検証（次に確認）**: `/kmx/obstacles` の Unity実送信（`Send Obstacles`）と座標一致。Unity で `Robotics > Generate ROS Messages`（geometry_msgs 要）後、まず1個の箱で位置/向きを確認（`OBSTACLES_ROS2_SPEC.md` §4 / §6.4）。
- 任意: FANUC URDF のゼロ点/符号が Unity(d_robo_a) と食い違えば `_convert_result` に補正。/kmx/state の `ros2 topic echo` 確認。
- **コードレビュー修正(2026-07-04)を planner_node.py に反映済**（詳細は `OBSTACLES_ROS2_SPEC.md` §6）: Obstacles import を try/except 保護／`_convert_result` は関節名不一致で発行中止（0埋め廃止）／既定 `moveit_joint_names`=J1..J6／`_obstacle_ids` は apply 成功後に確定。WSLで別途編集していれば `sync.sh` 取り込み時に要マージ。
- 既知の残(任意対応): `wait_for_server(3s)` が単一executorをブロック／planner再起動時の planning scene 乖離。
- **レビュー第2弾(2026-07-05)を planner_node.py に反映済（要 sync＋別編集あればマージ）**:
  - #14 plan結果のマッピング(out_names/moveit_names)を **コールバック閉包で持ち回り**（`_pending_*` 廃止＝並行要求で上書きされない）。`wait_for_server(3s)` → `server_is_ready()` の**非ブロック確認**へ（executorを止めない）。
  - D2 起動時に `GetPlanningScene`(WORLD_OBJECT_NAMES) で既存 collision object id を `_obstacle_ids` に取り込み（再起動時に前プロセス残置を初回受信で REMOVE 可能に）。新パラメータ `get_scene_service`(=/get_planning_scene)。サービス未準備は 2s×5 回リトライ→諦め（非ブロック）。
  - 残項目の全体像は正本 `kmx_ros2/REVIEW_TODO.md`（A検証待ち/B中位=対応済/Cクリーンアップ/D機能）を参照。
- **★ヘッド=ツール attach（方式B）＝ROS2側 実装済(2026-07-05・WSL)**（詳細 `HEAD_TOOL_ROS2_SPEC.md`・契約 §4.1）:
  `/kmx/attached`(型=既存 Obstacles) 購読→`AttachedCollisionObject`(link=frame_id, touch_links=`attached_touch_links`)→
  全置換(`_attached_ids` 別集合)→`robot_state.attached_collision_objects` に反映。**ヘッド向き補正は ROS2 の
  `head_calibration_rpy`(param・ライブ調整)** で行う（Unity は生送り）。実装済・`py_compile` OK。
- **★ヘッド位置キャリブレーション＝確定(2026-07-05)**: `head_calibration_rpy=[0,90,90]`（実機確認）→ param 既定へ焼込済。
- **★残＝ヘッドの Collider 数（性能）**: CADヘッドは 150個超の Collider を attach するため MoveIt が重い懸念。
  Unity 側 `ComRos2Obstacles.headAsSingleBox`(既定false) を true にすると**全体を1個のAABB**で送れる（把持開口が要らなければ推奨）。
  把持開口が要る運用なら個別のまま or 数個へ間引きを別途検討。
- **認識合わせ 未確定（要ROS2側回答）**: ①attach 先リンク名（SRDF: `flange`/`tool0`/`J6_link`? 既定は `flange`／
  `attached_touch_links` の手首側リンク名も実在名に）②scene 反映レイテンシは Unity 待ち 0.4s で足りるか
  ③world 障害物は現状**全て軸整列BOX(向き無し)** で来る前提でよいか。

## 10. Unity側（参考・別VSCode担当。ここは触らない）
- C#: `Assets/Scripts/Com/Ros2/`（ComRos2 / RosTcpConnectorTransport / ComRos2PathPlanner）。`GlobalScript`, `ParameterLoader`, `BuildAndRun` にも変更。
- `KMX_ROS2` define がONのときだけ実通信。Unityメニュー **`Kyotoss/ROS2連携を有効化`** でトグル。
- コミット: `refine-URP` の `26cfc94`（ROS2連携一式）。`kmx_ros2` 改称＋統合launch/sync は未コミット（別途）。
