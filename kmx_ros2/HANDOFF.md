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
| `/kmx/plan_request` | kmx_msgs/PlanRequest (names,start,goal,**time_budget,good_ratio**) | Unity→ROS2 | 経路要求(度)。time_budget(秒)/good_ratio は任意＝計画の粘り具合を要求ごとに指定（0/未設定=ノード既定）。※フィールド追加につき Unity は Generate ROS Messages 再生成が必要 |
| `/kmx/trajectory` | trajectory_msgs/JointTrajectory | ROS2→Unity | 生成軌道(度) |
| `/kmx/plan_status` | **std_msgs/String** (reliable) | ROS2→Unity | 計画ステータス通知。`planning` / `succeeded:<points>:<ratio>` / `failed:<reason>`（軌道は載せない・状態専用。Unityの計画中表示/プレビュー用） |
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
- **★attached が world と衝突判定される要点**: MoveGroup goal の `start_state.is_diff = True`。`False` だと MoveIt が `clearAttachedBodies()` で attached(ヘッド)を消してから計画し、ヘッドが障害物をすり抜ける（2026-07-05 真因特定・修正済）。
- **リトライ＋経路最適化＋大回り回避**（狭所対策・2026-07-05）: 1要求＝1計画セッション。時間予算内(`plan_time_budget_sec` or `PlanRequest.time_budget`)・最大 `plan_retries` 回まで計画を繰り返し、**失敗はリトライ／成功は貯めて関節総移動量が最小の経路を採用**。「始点→終点の直線関節距離の `plan_good_ratio`(or `PlanRequest.good_ratio`) 倍以下」の短経路が出たら**早期終了**、出なければ予算まで**より短い通り道を探し続ける**（稀な大迂回ホモトピーで妥協しない）。1試行は `allowed_planning_time`(既定1.0s)で短く打ち切り回数を稼ぐ。
- **経路短縮（発行前・RRT*-Smart の Path Optimization 相当）**: `path_shortcut`=true のとき、採用経路の**非隣接ウェイポイント間を直結できる（直線補間が衝突しない）なら中間点を捨てて**うねりを除去。衝突判定は `/check_state_validity` を経路上だけに使う（attachヘッド＋障害物込み・`is_diff=True`）。`shortcut_step_deg`/`shortcut_output_step_deg` で刻み調整。ログ `cost A→B, 直線=D [N倍]` で効果確認。
- **計画バックエンド `planner_backend`**: `moveit`（既定＝OMPL RRTConnect＋上記retry/shortcut）/ `rrtstar_smart`（**Python実装のRRT*-Smart**・実験的。関節空間RRT*＋近傍リワイヤ＋Intelligent Sampling＋shortcut、時間予算内で最良）。RRT*-Smart は衝突判定が `check_state_validity`(サービス)経由でスループット低め＝狭所発見は弱い。関節可動域は `/robot_description` から自動取得。本番最適性は C++ OMPL プラグイン化が本筋。
- **地面（床）は Unity が `/kmx/obstacles` で送る**（例 `kmx_ground_plane` 4×4×0.1m を base_link下 z≈-0.9 等）。ROS2 側でハードコード地面は持たない（一度実装したが撤去）。※ Unity の巨大床(scale 1000)は `ComRos2Obstacles` の `maxObstacleSize` 安全弁で除外され得るので、適正サイズで送るか安全弁を調整。
- params（`ros2 param set /kmx_planner …` でライブ調整可）: `use_moveit`, `planning_group`(=manipulator), `moveit_joint_names`(=[J1..J6]), `duration_sec`, `num_points`, `allowed_planning_time`(=1.0), `vel_scale`/`acc_scale`(=0.3), `planner_id`(=RRTConnect), **`num_planning_attempts`(=8・OMPL ParallelPlanで並列best-of-N)**, `planning_pipeline`, **`plan_retries`(=20)**, **`plan_time_budget_sec`(=10)**, **`plan_good_ratio`(=2.0)**, **`path_shortcut`**, `shortcut_step_deg`, `shortcut_output_step_deg`, **`planner_backend`**, `rrt_*`（RRT*-Smart用）, `head_calibration_rpy`(=[0,90,90]), `attach_link`(=flange), `attached_touch_links`, **`attached_merge_aabb`(=true)**, **`attached_merge_over`(=12・この数超で union1箱に安全弁統合／以下は個別attach＝間引き)**, `obstacles_topic`, `apply_scene_service`, `get_scene_service`, `planning_scene_topic`, **`plan_status_topic`(=/kmx/plan_status)**。
- メッセージ: `PlanRequest`(names,start,goal,**time_budget,good_ratio**) / `Obstacles` / `ObstaclePrimitive` は `kmx_msgs`（障害物系は `geometry_msgs` 依存）。`JointTrajectory` は **ROS-TCP-Connector 同梱**（Unity側は生成不要）。**PlanRequest にフィールド追加したので Unity は `Generate ROS Messages` 再生成が必要**。
- 堅牢化: `Obstacles` import は try/except で保護（未ビルドでも PlanRequest 経路は起動）。`_convert_result` は関節名不一致時に発行中止（0埋め廃止）。実行モデルは **MultiThreadedExecutor**（shortcut の同期 `check_state_validity` 呼び出しをコールバック内から行うため。sv クライアントは別コールバックグループ）。

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
- **検証済(2026-07-05)**: `/kmx/obstacles` の Unity実送信（`Send Obstacles`）と**座標一致**＝Unity/RViz で確認。世界障害物は `baseCalibrationEuler`=(0,-90,0)、ヘッド `/kmx/attached` は生送り＋`head_calibration_rpy=[0,90,90]` で一致（§4.1）。`Robotics > Generate ROS Messages`（geometry_msgs 含む）済。
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
- **★ヘッドの Collider 数（性能）＝間引き対応済(2026-07-06)**: CADヘッドは実測395個の Collider があり全attachは
  MoveIt が激重。ROS2 は `attached_merge_over`(=12) を**超える** item 数を受けたら **union AABB 1箱に自動統合**（安全弁）、
  **12個以下はそのまま個別 attach**（＝間引きで把持開口を残せる）。→ **形状切替は Unity 側だけで完結**：
  `ComRos2Obstacles.headAsSingleBox=true`→1箱／`false`→間引き数箱（本体＋爪等・**≤12個**）を送る。移行期に旧来の395個を
  送っても安全弁で1箱化＝性能退行なし。ROS2側検証済：**3箱→個別保持／15箱→1箱統合**。
  **注意**：`attached_touch_links` の `J4_link` は旧 union箱（手首後方に膨らむ）前提。間引き形状が J4 に膨らまないなら
  `J4_link` を外す方が実 J4 衝突を隠さず正確（Unity 間引き形状が確定したら調整）。完全に統合断つなら `attached_merge_aabb=false`。
- **★地面(ground plane)対応＝ROS2側 新規実装 不要(2026-07-05)**: Unity が **基部の真下・床の高さ**に
  **可動範囲サイズの薄い板(既定 4×4×0.1m)** を `id="kmx_ground_plane"` で **`/kmx/obstacles` の世界障害物**として送る
  （`ComRos2Obstacles.sendGroundPlane`＝既定 true。床の高さは `groundNameContains="Floor"` の Collider 上面から取得）。
  base_link で床高さ(例 Z≈-0.9m)の薄板になり基部を内包しない。**実床(1000m級)は送らない**（軽量）。既存 `on_obstacles`
  でそのまま処理＝**新規実装不要・性能懸念なし**。板サイズ/厚みは Unity 側で調整可。
- **認識合わせ 確認事項（要ROS2側回答）**: ①attach 先=`flange`(SRDF tip 確定)・`attached_touch_links` は実在名でOK
  ②scene 反映レイテンシは Unity 待ち 0.4s で足りるか ③world 障害物は現状**全て軸整列BOX(向き無し)** で来る前提でよいか
  ④(任意) attached の起動時同期: `GetPlanningScene(ROBOT_STATE_ATTACHED_OBJECTS)` で `_attached_ids` を seed すると
  planner 単体再起動でも古い attached を掃除でき、move_group 再起動不要になる。
- **★狭所プランナ調査＋狭所サンプラ実装＝済(2026-07-06)**: 標準プランナ比較（実シーン単発6回）で
  **BITstar が最良（6/6・cost1.84倍）**＝据え置き確定（ABITstar 5/6・1.85／AITstar 4/6・2.14不安定／
  RRTConnect 4/6・2.56倍・0.8sと最速だが長い）。運用リトライ有でも BITstar 1.77 > SBLbridge 2.15。
  文献調査で VAMP(CPU SIMD・OMPL2.0統合)/cuRobo(GPU)/狭所サンプラ/STOMP等を整理。
  **狭所 ValidStateSampler を MoveIt OMPL に実装済**：`ompl_planning.yaml` に `PRMbridge/PRMobstacle/
  ESTgaussian/SBLbridge`、`model_based_planning_context.cpp` に `valid_state_sampler` キー処理
  （`bridge_test|gaussian|obstacle_based|max_clearance|uniform`＋任意 `valid_state_sampler_stddev`[rad]）。
  **PRM/EST/SBL/BKPIECE/STRIDE のみ効く**（RRTConnect/KPIECE/BIT*系は StateSampler で無効）。
  **ws_moveit パッチ＝更新時要再適用**（BITstar backport と同様）。ただし現行シーンでは BITstar に及ばず
  ＝発見は環境修正で既に解決済で旨味なし → **より狭い所/速度優先時の予備**として温存。詳細 `HANDOFF_curobo.md` §6。
- **★計画ステータス通知 `/kmx/plan_status`＝実装・検証済(2026-07-06)**（要望書 `PLAN_STATUS_ROS2_SPEC.md`）:
  `std_msgs/String`(reliable) を新設。`on_request` で `planning`、成功発行直後に `succeeded:<points>:<ratio>`、
  失敗（時間予算内に解なし/MoveItエラー/不正要求）で `failed:<reason>`（`no_solution`/`GOAL_IN_COLLISION`/
  `bad_request` 等・`_error_reason` で MoveItErrorCodes を文字列化）。**補間モード(`use_moveit:=false`)も
  planning→succeeded を出す**。軌道は従来どおり `/kmx/trajectory`（status は状態専用）。新param `plan_status_topic`
  (=`/kmx/plan_status`)。実測確認：`planning`→`succeeded:15:1.00`／`failed:bad_request`。**Unity 側も実装済(2026-07-06)**：
  `ComRos2PathPlanner` が購読→`ComRos2PlanPanel` で計画中/残り時間/成否表示＋プレビュー(青線＋半透明ゴースト)→OK/NG、timeout保険。
  ※kmx_planner のみ変更＝ノード再起動で反映（move_group 不要）。
- **★並列 best-of-N 計画＝採用・既定化(2026-07-07)**: `num_planning_attempts` 既定 1→**8**（OMPL ParallelPlan が
  8本を並列スレッド生成し最短を返す・in-process）。単一クリーン起動＋同一シーンのクリーン比較で **単発npa=1 に全項目勝利**：
  成功 5/5 vs 4/5・倍率中央 1.64 vs 1.88・レイテンシ 9.8s vs 13.1s（good_ratio=1.4・各5回）。24コアで余裕。planner_node.py 既定へ焼込済。
  単発へ戻すなら `~/ros2_ws/revert_baseline.sh`。**★計測時の罠＝bringup 二重起動（KMX起動+直接launch）で `/kmx_planner` 名前衝突→全無効**。
  起動は `kmx_start.sh`（冪等）に一元化し `ros2 launch` 直叩き禁止。プロセス確認は `ps|grep -v bash`（`pgrep` はシェル自己マッチ）。
- **★EIT*/Ruckig 追加評価＝BITstar 据え置き(2026-07-07)**: 「さらなる高速化/高精度化」検討で **EIT\*(Effort Informed Trees)** を
  ws_moveit `planning_context_manager.cpp` に登録（BITstar backport と同手順・**更新時要再適用**）＋yaml。**N=15 比較で BITstar が優**
  （中央 1.77 vs EIT\* 1.90／BITstar は締まり外れ値無し・EIT\* は 2.95 の外れ値。N=5 の EIT\* 優位はノイズ）→ **既定 BITstar 継続**。
  EIT\* は「衝突判定が重いほど有利」＝実ヘッド（潰れ修正後）で再確認用に残置。**Ruckig** ジャーク平滑化は adapter 追加したが
  `error -100`＋**kmx が shortcut 後に `_densify_retime` で再タイム付け→move_group段Ruckigは上書き無効**なので撤去（jerk 推定値は joint_limits に残置）。
- **★ヘッド潰れバグ(Unity側・2026-07-07)**: 間引きヘッド運用で**時々**全箱 pose=(0,0,0)＝flange 原点に潰れる。ROS2 は回転補正のみで
  非ゼロをゼロにできない＝**Unity が pose=0 送信**確定（11箱は dims バラバラ＝分割は成立・pose だけ全0）。要望書 `HEAD_POSE_ZERO_UNITY_SPEC.md`。
  **ROS2 保険ガード＝実装・検証済(2026-07-07)**: `on_attached` 冒頭「複数 item が全て pose≒原点(<0.1mm)なら更新破棄＝前回ヘッド維持」（空配列=全消しは除外）。Unity側も縮退ガード＋リトライ実装済＝両側ガード。
- **★attached の stale 残留バグ＝修正済(2026-07-06)**: `/apply_planning_scene` は **attached diff で `success=False` を返す**
  （world障害物は True）が **diff は実際に適用される**。旧実装は success=True 時のみ `_attached_ids` を確定→未確定のまま→
  全置換 REMOVE が効かず**空 `/kmx/attached` を送ってもヘッドが消えない**→ stale 蓄積→ goal がそれと衝突し
  **`GOAL_STATE_INVALID`(-27)** で計画失敗（"Unable to sample any valid states for goal tree"）。修正＝`_on_scene_applied` で
  **success に関わらず id 確定**（例外時のみ据え置き）。検証：3箱→空送信で確実クリア。**運用注意**：Unity でオブジェクトを消しても
  ROS2 は自動で消えない＝**空の obstacles/attached を明示送信**して全消し（全置換＝空送信で全消し/未送信で据え置き）。手動強制クリアは
  scratchpad の `clear_scene.py`（id 明示 REMOVE。空id="" は効かない）。
- **★Unity からの ROS2 起動/停止/再起動＝ROS2側 実装・検証済(2026-07-06)**（要望書 `LAUNCH_CONTROL_UNITY_SPEC.md`）:
  方式A＝Unity(Windows) が `wsl.exe` で WSL スクリプトを実行（ROS 経由はコールドスタート不可＝endpoint が bringup 内のため）。
  提供スクリプト（`~/ros2_ws/`・正本 `kmx_ros2/` にも複製）: **`kmx_start.sh [use_moveit]`**（冪等・`setsid` で detach・即return）／
  **`kmx_stop.sh`**（SIGINT→10s→SIGKILL＋子ノード掃除）／**`kmx_restart.sh`**／**`kmx_status.sh`**（`stopped`/`starting`/`running_full`）。
  検証済：stop→stopped／start→running_full(~4s・node2/2)／restart→running_full。PID=`~/ros2_ws/.kmx_bringup.pid`、log=`~/ros2_ws/kmx_bringup.log`。
  **Unity 側（先方）**＝`System.Diagnostics.Process` で `wsl.exe -e bash -lc "…/kmx_start.sh"` 等を呼び、`kmx_status.sh` を
  ポーリングして `running_full` を待ってから ROS-TCP 再接続＋（再起動時は空になる scene に）obstacles/head 再送。
- **★cuRobo(GPU計画)統合＝未着手・後日別途(2026-07-06 ユーザー判断)**: 環境= RTX A2000/CUDA12.8 は可だが
  **nvcc/pip/torch/curobo 未導入**（~6-8GB 導入が要る）。再開用の環境ステータス・導入手順・統合設計
  （`planner_backend=curobo` 追加＝move_group を通さず `/kmx/obstacles`(BOX)→world・`/kmx/attached`→attach・
  CRX-30iA 球モデルで GPU 計画→`/kmx/trajectory`）は **`HANDOFF_curobo.md`** に集約。既定は BITstar 据え置き。

## 10. Unity側（参考・別VSCode担当。WSL側はここを触らない）
- C#: `Assets/Scripts/Com/Ros2/`（**ComRos2 / RosTcpConnectorTransport / ComRos2Obstacles / ComRos2PathPlanner / ComRos2PlanPanel**）。
  `GlobalScript`(useRos2)・`ParameterLoader`(生成/破棄ヘルパー)・`Kinematics6D`＋`CRX-30iA`(手動姿勢/ゴースト)・`BuildAndRun` にも変更。
- `KMX_ROS2` define がONのときだけ実通信。Unityメニュー **`Kyotoss/ROS2連携を有効化`** でトグル。
- **実行時UI＝経路計画パネル**(`ComRos2PlanPanel`)：ゴール角(J1-J6 スライダー/数値直入力)・時間予算/大回り許容比・
  計画ボタン/計画中・残り時間/成否・プレビュー(青線＋半透明ゴースト)→OK/NG・**ROS通信状態表示**・ヘッド形状トグル(1箱/間引き)。
  再コンパイル/リロードで UI・青線・ゴーストは自動掃除。
- 座標契約は §4.1 準拠（obstacles=Unity `baseCalibrationEuler`=(0,-90,0)／attached=生送り＋ROS `head_calibration_rpy`）。
- コミット(`refine-URP`)：`26cfc94`(連携一式)→`a15e419`(kmx_ros2 改称＋統合launch/sync)→`c9f40d0`(レビュー修正)→
  `1ba724e`(計画パネル Stage1)→`85eff95`(パネル拡充＋ゴースト＋ヘッド間引き)。以降、ROS通信状態表示／破棄ヘルパー(C1)／
  青線・ゴーストのコンパイル掃除 は作業中（未コミット）。
