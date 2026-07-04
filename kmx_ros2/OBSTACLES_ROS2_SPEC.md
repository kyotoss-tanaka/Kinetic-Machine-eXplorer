# 【ROS2側 実装要望】障害物 → MoveIt planning scene

Unity(`ComRos2Obstacles`)が **`/kmx/obstacles` (kmx_msgs/Obstacles)** でロボット周辺の障害物プリミティブ群を送る。
ROS2側でこれを **moveit_msgs/CollisionObject** 化して **move_group の planning scene** に反映してほしい。
反映後は既存の `/kmx/plan_request`（plan_only）が **障害物を回避した軌道**を返すようになる。

Unity側は実装済み（`Assets/Scripts/Com/Ros2/ComRos2Obstacles.cs`、`RosTcpConnectorTransport.PublishObstacles`）。**ROS2側はこの要望に沿って実装をお願いします。**

---

## 1. kmx_msgs にメッセージ追加
`~/ros2_ws/src/kmx_msgs/msg/` に2ファイル（正本ミラーは Unityリポ `kmx_ros2/kmx_msgs/msg/` にあり）:

`ObstaclePrimitive.msg`
```
string id
uint8 type                # 1=BOX, 2=SPHERE, 3=CYLINDER（shape_msgs/SolidPrimitive 準拠）
float64[] dimensions      # BOX:[x,y,z] / SPHERE:[radius] / CYLINDER:[height,radius]
geometry_msgs/Pose pose   # base_link 相対
```
`Obstacles.msg`
```
string frame_id
ObstaclePrimitive[] items
```
- `CMakeLists.txt`: `rosidl_generate_interfaces(...)` に `"msg/ObstaclePrimitive.msg"` `"msg/Obstacles.msg"` を追加。`DEPENDENCIES` に **`geometry_msgs`** を追加。
- `package.xml`: `<depend>geometry_msgs</depend>` を追加（無ければ）。
- ビルド: `colcon build --packages-select kmx_msgs && source install/setup.bash`
- **Unity側も** `Robotics > Generate ROS Messages` で kmx_msgs 再生成（geometry_msgs も要）。
- **Unity有効化**: 生成済みメッセージ(`Assets/RosMessages/Kmx/msg/ObstaclesMsg.cs` 等)をコミット済み。障害物送信は **`KMX_ROS2` のみで有効**（旧 `KMX_ROS2_OBSTACLES` の二段defineは廃止した）。

## 2. ノード実装（`kmx_planner` に追記でOK）
- **購読**: `/kmx/obstacles` (kmx_msgs/Obstacles)
- **変換**: 各 `item` → `moveit_msgs/CollisionObject`
  - `header.frame_id = msg.frame_id`（通常 `base_link`）
  - `id = item.id`
  - `primitives = [shape_msgs/SolidPrimitive{ type=item.type, dimensions=item.dimensions }]`
  - `primitive_poses = [item.pose]`
  - `operation = CollisionObject.ADD`
- **planning scene 反映**（どちらでも）:
  - `moveit_msgs/PlanningScene{ is_diff=true, world.collision_objects=[...] }` を **`/planning_scene`** へ publish（QoS: reliable / transient_local 推奨）、または
  - **`/apply_planning_scene`** サービス（moveit_msgs/ApplyPlanningScene）で同期反映（確実）。
- **更新規約**（静的運用）: 受信のたびに「**前回分を消して新規追加**」が安全。
  - 同一 `id` で ADD すれば置換。全消しは各 id を `operation=REMOVE`、または PlanningScene の `world.collision_objects` を空にして is_diff=false で置換。
- **フレーム**: `frame_id`(=base_link) が move_group の planning frame と一致すること（fanuc_moveit_config の base_link 名を確認）。

## 3. 検証
1. Unity Play → `GlobalSetting` の **ComRos2Obstacles** を右クリック → **`Send Obstacles`**。
2. ノードに受信ログ、planning scene 更新。`ros2 topic echo /planning_scene` に `collision_objects` が出るか、RViz の PlanningScene/MotionPlanning 表示に箱が見えるか。
3. 障害物を挟む start/goal で `/kmx/plan_request` → **生成軌道が障害物を迂回**すれば成立。貫通するなら frame/座標ズレ（下記）。

## 4. 座標・単位（Unity側で変換済み。ズレたら調整）
- Unity側は「**ロボット基部(base_link)相対・ROS(FLU)右手Z-up・メートル**」で送る（ROSGeometry `To<FLU>()` 使用、`frame_id=base_link`）。
- ただし **Unityモデル基部の向き/スケール ↔ URDF base_link** が食い違うと箱の位置/向きがズレる。**まず1個の箱で位置確認**し、ズレたら Unity側 `unitScale`/基部Transform、または本ノードで補正。
- BOX の `dimensions` は Unity(x,y,z)→ROS(FLU)軸順で `[z,x,y]` に並べ替えて送っている。回転と合わせて要検証。
- `unitScale` は寸法だけでなく**位置にも適用**するよう修正済み（≠1 の非メートルシーンで位置がズレないように）。
- ⚠ Unity側の既定 `layerMask=~0` は床/機械フレームの MeshCollider まで拾い、巨大AABBが base_link を包含して **START_STATE_IN_COLLISION(-10)** を招き得る。まずは対象を絞る（レイヤー/明示リスト）こと。CapsuleCollider は direction=X/Z 未対応（Yのみ正）。

## 5. 参考（既存）
- `/kmx/plan_request`(PlanRequest) → MoveGroupアクション `plan_only` は実装済み。障害物は同じ move_group の planning scene に効くので、**このノードで planning scene を更新しておけば plan 時に自動で回避**される。
- Unity送信側: `ComRos2Obstacles`（半径内Collider収集→primitive化→送信、ContextMenu「Send Obstacles」）。

## 6. レビュー修正（2026-07-04・planner_node.py 済／WSLへ要sync）
リポジトリ正本 `kmx_ros2/kmx_planner/kmx_planner/planner_node.py` に以下を反映済み。WSL側は `sync.sh`→colcon で取り込むこと（WSLで別途編集していれば要マージ）:
- `from kmx_msgs.msg import Obstacles` を **try/except で保護**（未登録でもノード全体は起動＝PlanRequest経路を巻き添えにしない）。障害物購読は登録時のみ有効化。
- `_convert_result`: 受信軌道に無い関節名があれば **0埋めせず None を返し発行中止**（全零軌道でロボを飛ばさない）＋エラーログ。
- 既定 `moveit_joint_names` を **`J1..J6`**（実CRX）へ。fanuc代役は `-p` で `joint_1..6` 上書き。
- `_obstacle_ids` は **apply 成功後に確定**（service成功時のみ更新、失敗時は据え置き＝次回REMOVE差分が正しく出る）。publish fallback はベストエフォート更新。
- 既知の残: `wait_for_server(3s)` が単一executorをブロック（move_group不在時に要求毎3s停止）／プロセス再起動時の scene 乖離（起動時 GetPlanningScene で同期が理想）。必要なら対応。
