# 引継ぎ：RRT*-Smart バックエンドの問題点と解決依頼

**作成 2026-07-06。目的＝狭所（ヘッドがトート脇を通る）で「短く・毎回成功する」経路を出せる最適化プランナを完成させる。**
現状の Python 版 RRT*-Smart は実装済みだが**狭所で実用にならない**。原因と解決方針を以下にまとめる。

---

## ✅ 解決済み（2026-07-06）

**結果（実シーン=Unity送信の機械カバー/ヒモ捨て箱/床＋FANUCヘッド395AABB、goal `[0,40,-30,0,70,0]`）**:
`/kmx/plan_request` E2E で **5/5 成功・cost 1.53〜1.76×直線（131〜151° / 86°）・各1発成功・約4s応答**。
§6 完了定義（毎回成功・~1.5〜2倍以下）を ROS2 側で達成。採用構成＝**OMPL BITstar ＋ 環境修正**（C++ RRT*-Smart 自作は不要になった。BITstar の informed batch 探索＋kmx 側 shortcut が RRT*-Smart の Intelligent Sampling + Path Optimization を実質カバー）。

**判明した真因は「プランナ」ではなく環境側に2つ**:
1. **離散衝突チェックが粗すぎ**: `longest_valid_segment_fraction: 0.01` ＝関節空間対角の1%≈**8.8°刻み**。
   最薄2mmの薄板障害物を経路がすり抜け、TOTG後の再検証で `INVALID_MOTION_PLAN(-2)` 却下。
   RRTConnect ですら 6/10 失敗（→現行 moveit backend が20回リトライを要していた真因）。**0.002 に変更**。
2. **TOTG のコーナー丸め**: 時間パラメータ化アダプタの `path_tolerance` 既定 **0.1rad(≈5.7°)** が角を丸め、
   障害物に接する最適化経路（BITstar/shortcut系）が丸めで食い込み -2 化。**0.01rad に変更**。
   ※最適化プランナは経路が障害物境界に接するため、この2つの影響を最も強く受ける（APS は 9/10 -2 で不採用）。

**実施した変更**:
- `~/ws_moveit` **ローカルパッチ**: `moveit_planners/ompl/ompl_interface/src/planning_context_manager.cpp` に
  **BITstar/ABITstar/AITstar を登録**（上流の後継版から backport。OMPL 1.7 に実装は同梱済み）。
  `colcon build --packages-select moveit_planners_ompl` 済み。**ws_moveit を更新/再取得したら要再適用**。
- `fanuc_moveit_config/config/ompl_planning.yaml`（fanuc_driver 内・sync対象外）:
  `longest_valid_segment_fraction: 0.002`／トップレベル `path_tolerance: 0.01`／BITstar系 planner_configs。
- `kmx_planner/planner_node.py`（正本→sync済）:
  - **attached_merge_aabb（既定on）**: /kmx/attached の item 群（実測395個/ベアリング球単位）を
    attachリンク座標系の **union AABB 1箱** に統合して attach（実測 0.55×0.22×0.354m）。衝突チェックが
    体数比例で軽くなり計画が高速化。`false` で従来の個別 attach。
  - `attached_touch_links` に **J4_link** 追加（union箱が手首後方に膨らみ J4 と常時接触するため。
    実ヘッドは J4 非接触設計・J3以下は引き続き検出）。touch_links は live 読み化。
  - 既定値: `planner_id=BITstar`／`allowed_planning_time=3.0`（anytime なので1試行の質に直結）／
    `num_planning_attempts=1`（リトライは kmx ループ）／**`plan_fallback_planner=RRTConnect`**
    （全リトライ失敗時に1回だけ保険試行。BITstar 単発成功率 7〜8/10 → リトライ＋保険で実質毎回成功）。
- 実測比較（同一シーン・10回、成功率と cost/直線比）:
  | planner | 成功 | cost比 |
  |---|---|---|
  | RRTConnect@1s | 10/10 | 2.6〜5.0 |
  | AnytimePathShortening | 0〜1/10 | — |
  | **BITstar@3s** | **7〜8/10** | **1.5〜2.0** |
  | （E2E: BITstar+retry+fallback+shortcut） | **5/5** | **1.53〜1.76** |

**運用メモ**: Python 版 `planner_backend=rrtstar_smart` は実験用に残置（既定 `moveit` のまま使わない）。
シーン再現用に `scene.json`＋`replay_kmx.py`（セッション scratchpad）あり。§3〜9 は経緯として保存。
Unity 実機評価（Send Obstacles/Send Head → Test Plan）は未実施＝次の確認事項。

---

## 0. 一言サマリ
`planner_backend=rrtstar_smart`（Python 実装）は、**見つかれば品質は良い（moveit より短い）が、狭所では時間内に経路を発見できず失敗しやすい**。根因は **衝突判定を `check_state_validity`（ROS2サービス）経由で行うためスループットが低く**、時間予算内の反復数が 6軸の狭所探索に**桁違いに不足**すること。**解決の本命は C++ OMPL カスタムプランナ化**（MoveIt の in-process 高速衝突判定を使う）。

## 1. 環境
- ROS2 **humble** / **MoveIt 2.5.9（`~/ws_moveit` ソースビルド**、move_group はこれ）。
- `~/ros2_ws`：`kmx_planner`(ノード), `kmx_msgs`, `fanuc_driver`(内 `fanuc_moveit_config`, robot_model=crx30ia), `fanuc_description`。
- 起動：`ros2 launch kmx_planner kmx_bringup.launch.py use_moveit:=true`（endpoint+move_group+kmx_planner）。
- source：`source /opt/ros/humble/setup.bash && source ~/colcon_ws/install/setup.bash && source ~/ros2_ws/install/setup.bash`
- **正本＝Windows Unityリポ `/mnt/c/Users/gi-guest/source/repos/Kinetic Machine eXplorer/kmx_ros2/kmx_planner/`**。編集は正本→`bash kmx_ros2/sync.sh`→`cd ~/ros2_ws && colcon build --symlink-install --packages-select kmx_planner && source install/setup.bash`。`~/ros2_ws/src` 直接編集は sync で上書き。
- 全体設計は `HANDOFF.md`（=`~/ros2_ws/CLAUDE.md`）§6 参照。

## 2. 現状の実装（`kmx_planner/kmx_planner/planner_node.py`）
2つの計画バックエンドを `planner_backend` パラメータで切替（既定 `moveit`）。
- **`moveit`（既定・実用）**：OMPL(RRTConnect) を MoveGroup アクション(plan_only)で呼ぶ。**時間予算内でリトライ＋成功分から最短採用＋大回り回避（直線距離の good_ratio 倍以下まで粘る）＋発行前ショートカット**。狭所でも**安定**して発行でき、cost も shortcut で抑制。これが現状のベスト。
- **`rrtstar_smart`（本課題・Python実装）**：関節空間 RRT*（近傍リワイヤ）＋ Intelligent Sampling（見つけた経路の中継点近傍を集中サンプル）＋ shortcut。時間予算内で最良を返し、直線距離の good_ratio 倍以下で早期終了。
  - 主メソッド：`_rrtstar_smart()`（本体ループ）, `_run_rrtstar_smart()`（別スレッド実行→発行）, `_on_robot_description()`（URDFから関節可動域取得）, `_jl()`, `_extract_path()`, `_path_to_traj()`。
  - **衝突判定**：`_state_valid(cfg_deg, moveit_names)` が `/check_state_validity`（`GetStateValidity`, `RobotState.is_diff=True`）を**同期 call**。`_segment_free(a,b,…)` が辺を刻んで各点 `_state_valid`。**attach ヘッド＋障害物＋床込みで判定される**（`is_diff=True` 必須。False だと attached が消える＝別issue、解決済）。
  - 実行モデル：ノードは **MultiThreadedExecutor**、`_sv_cli` は別 CallbackGroup（コールバック内から同期 call してもデッドロックしない）。RRT*-Smart 本体は `threading.Thread` で回す。
- 関連パラメータ（`ros2 param set /kmx_planner …` でライブ調整）：
  - `planner_backend`（moveit|rrtstar_smart）
  - `rrt_step_deg`(20), `rrt_goal_bias`(0.1), `rrt_goal_tol_deg`(6), `rrt_rewire_radius_deg`(45), `rrt_beacon_bias`(0.35), `rrt_beacon_radius_deg`(25)
  - 予算：`plan_time_budget_sec`(10) or `PlanRequest.time_budget`、`plan_good_ratio`(2.0) or `PlanRequest.good_ratio`
  - shortcut：`path_shortcut`(true), `shortcut_step_deg`(4), `shortcut_output_step_deg`(5)

## 3. 問題（実測データ・2026-07-06）
同一 goal `[0,40,-30,0,70,0]`（start=home、直線関節距離=86°、シーン＝トート/機械/床、ヘッドはattach有無で試行）：

| backend | 結果 | cost[°] / 直線比 | 反復 |
|---|---|---|---|
| moveit | 成功（安定） | 239.5 / **2.8倍** | (retry 3/6) |
| rrtstar_smart ①(既定) | 成功 | 208.3 / **2.4倍** | 395 |
| rrtstar_smart ②(既定) | **失敗（10s発見不可）** | — | 589 |
| rrtstar_smart（goal_bias0.2/step30/予算15s）×4 | **全失敗（0/4）** | — | 1053〜1337 |

- **見つかれば moveit より短い（2.4<2.8倍）**＝アルゴリズムの最適化自体は正しく効いている。
- しかし**狭所で発見に失敗**。予算/反復を増やしても改善せず、`rrt_step_deg` 拡大はむしろ悪化（狭所を粗いステップでは通れない）。

## 4. 根本原因
- **衝突判定スループットが低い**：`check_state_validity` はサービス往復（1回あたり ms オーダ＋シリアライズ）。RRT* は1反復で「新ノード検証＋近傍への複数 edge 検証（各 edge は刻んで多数点）」＝**1反復で数〜数十回の衝突チェック**。
- 結果、**15秒で ~1000〜1300 反復**しか回らない。6自由度＋狭所（ナローパッセージ）を確実に発見するには**桁違い（数万〜）の反復**が要る。
- ＝ **アルゴリズムではなく I/O（衝突判定の呼び出し方）がボトルネック**。

## 5. 解決の方向性（fable への依頼）
**本命：RRT*-Smart を OMPL カスタムプランナ(C++)として実装し MoveIt に組み込む。**
- MoveIt/OMPL の **in-process 衝突判定（`planning_scene->isStateValid` / OMPL `StateValidityChecker`）** を使えば、衝突チェックがメモリ内で完結し**桁違いに高速**＝反復数が確保でき、狭所発見＋最適化を両立できる。
- 実装場所の案：`fanuc_moveit_config`（or 新規 pkg）に OMPL プランナプラグインを追加し、`ompl_planning.yaml` の `planner_configs` に `RRTstarSmart`（`type: geometric::～`）を登録 → `planner_id=RRTstarSmart` で選べるように。RRT*-Smart 論文（J.Nasir 2013）の Intelligent Sampling + Path Optimization を RRTstar 派生で実装。
  - 参考記事（Python独自実装・アルゴリズム解説）: https://qiita.com/haruhiro1020/items/c04e9231424b50db00ed
- こうすれば **kmx_planner 側は `planner_id` を渡すだけ**で使える（今の `moveit` バックエンド経路のまま。Pythonバックエンドは廃止 or 実験用に残置）。

**代替/併用案（軽い順）**：
- (a) **現状維持＝`moveit` バックエンドで運用**。狭所発見は RRTConnect（双方向で発見が得意）に任せ、**shortcut で最適化**（＝RRT*-Smart の Path Optimization 相当は既に実装済）。実測で**最も安定**。「使えるレベル」には到達済み。
- (b) **ハイブリッド**：RRTConnect で発見→そのホモトピー内で RRT*/最適化（局所最適化）。C++ か、found path 周辺だけ Python 最適化なら軽い。
- (c) Python 版の衝突スループット改善：`check_state_validity` をやめ、**MoveIt の C++ 衝突を Python から使える経路**（moveit_py 等）や、シーンをローカル FCL に複製してバッチ判定。いずれも大改修。

## 6. 完了の定義
- 狭所 goal（例 `[0,40,-30,0,70,0]`、トート/床/ヘッドあり）で、**毎回（5/5）成功**し、経路 cost が**直線の ~1.5〜2倍以下**で安定。
- Unity 実機 Test Plan で、ヘッドがトートを避けつつ**素直な（大回りでない）経路**を再生。

## 7. 再現・比較手順
```bash
# 起動後（move_action が出るまで待つ）。シーンは Unity で Send Obstacles(+Send Head) するか、CLIで箱を置く。
ros2 param set /kmx_planner planner_backend rrtstar_smart   # / moveit で戻す
# CLI で計画（Unityの代わり）：
ros2 topic pub -t 3 /kmx/plan_request kmx_msgs/msg/PlanRequest \
  '{names: [J1,J2,J3,J4,J5,J6], start: [0,0,0,0,0,0], goal: [0,40,-30,0,70,0], time_budget: 15.0, good_ratio: 1.5}'
# ログ（kmx_planner）で結果確認：published best trajectory: … (RRT*-Smart, cost A→B, 直線=D [N倍], K反復) / 見つかりませんでした（K反復）
```
- 現在のログ：`~/... /scratchpad/bringup_v12.log`（セッション依存）。通常は launch を起動した端末の標準出力。
- シーン確認：`ros2 service call /get_planning_scene moveit_msgs/srv/GetPlanningScene '{components:{components: 4}}'`（attached）/ `8`（world）。

## 8. 注意（触ると事故る点）
- ヘッド向き補正は **ROS2 `head_calibration_rpy=[0,90,90]` 一本**、Unity は生送り。obstacles は Unity `baseCalibrationEuler=(0,-90,0)`。**二重補正禁止**。
- `start_state.is_diff=True` を維持（False だと attached が消える）。
- ドキュメントは複数コピーが手動ミラー（`~/ros2_ws/CLAUDE.md`＝正本`HANDOFF.md`、SPEC類は正本と~/ros2_wsの2コピー）。sync.sh はコードと.msgのみ同期し.mdは同期しない。
- PlanRequest に `time_budget`/`good_ratio` を追加済み。Unity は `Generate ROS Messages` 再生成済みが前提。

## 9. まとめ
- **アルゴリズムは正しく動く（見つかれば moveit より短い）**。詰まっているのは **Python×サービス衝突判定のスループット**の一点。
- **推奨解＝ C++ OMPL プラグイン化**（in-process 衝突で反復数を確保）。それが重ければ **moveit バックエンドで運用**（既に実用レベル・最安定）。
