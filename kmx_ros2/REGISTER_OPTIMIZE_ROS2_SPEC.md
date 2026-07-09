# 登録軌道 最適化 ROS2 要求書（REGISTER_OPTIMIZE）

## 目的
登録モード（オフライン教示）で、開始姿勢→終了姿勢の関節軌道を**多目的最適化**して登録キャッシュにする。
Unity は最適化済み軌道を受け取り、ゴーストでプレビュー→OK で `Ros2TrajCache.json` に保存する（既存フロー）。
登録はオフラインなので**重い最適化でも可**。ただし**途中経過を Unity へ送る**こと。

## 前提・関連ドキュメント（ROS2 実装者向け）
この spec は**既存の経路計画連携に “最適化モード” を足す**もの。単体で読めるが、下記が前提コンテキスト：
- **既存 `kmx_planner` ノード**（`README.md` / `RUN.md` / `kmx_planner/`）＝ここに `optimize=true` の分岐を足す。通常計画（MoveIt / 補間）は現状のまま。
- **`PLAN_STATUS_ROS2_SPEC.md`**＝`/kmx/plan_status`(String) の既存形式 `planning` / `succeeded:<pts>:<ratio>` / `failed:<reason>`。本 spec の **`opt …` 行はこれと同じトピックに併存**（Unity は先頭語で区別）。既存の planning/succeeded/failed も従来どおり出すこと。
- **`OBSTACLES_ROS2_SPEC.md` / `HEAD_TOOL_ROS2_SPEC.md`**＝`/kmx/obstacles`・`/kmx/attached` のシーン（**実装済**）。最適化も同じ planning scene で衝突回避する（流用）。
- **`MULTI_ROBOT_ROS2_SPEC.md`**＝`robot_id` ルーティング（現状 `""`＝既定ロボなので当面不要）。
- **`kmx_msgs/msg/PlanRequest.msg`**＝入力の全フィールド（names/start/goal/time_budget/good_ratio/robot_id＋本 spec の optimize/target_time/payload）。
- **`PLAN_BUDGET_UNITY_SPEC.md`**＝`time_budget`/`good_ratio` の意味（通常計画の粘り制御。最適化とは別）。

## 目的関数（優先順位＝辞書式：時間 > ジャーク > トルク）
ユーザー確定：
1. **時間（ハード制約・必須）**：所要時間 ≤ `target_time`（robotStep の `time`, **ms**）。
   - `target_time` が実機の速度/加速度/ジャーク限界で**達成不能**なら、**達成可能な最小時間**を求めて返す（`feasible=false` ＋ `min_time_s`）。Unity はそれを表示。
2. **ジャーク最小**：時間制約を満たす中で、**ヘッド先端(Cartesian)加速度の時間微分＝ジャーク**を最小化（関節ジャークでも可）。
3. **トルク最小**：上記の中で、各関節トルク（逆動力学）のピーク/RMS を最小化。

辞書式なので実装は staged（①時間充足→②ジャーク最適→③トルク最適）または weighted-sum（時間≫ジャーク≫トルクの重み）。

## 手法（例）
- **時間**：TOTG（velocity/acceleration scaling=1）で「限界最短」を算出。これが `target_time` 達成可否の基準（＝現状 Unity が表示している「最短」もこれ）。
- **ジャーク**：**Ruckig**（ジャーク制限付き time-parameterization）または ジャークコスト付き軌道最適化。ヘッド先端 Cartesian ジャークを見るならヤコビアン経由。
- **トルク**：**逆動力学**（Pinocchio または KDL、URDF の慣性＋ペイロード質量/重心）でトルク時系列→ピーク/RMS をコスト化。
- **最適化器**：TrajOpt / CHOMP / STOMP（コスト付き）または「時間スケール＋経由点微調整」の QP。オフラインなので反復可。

## ペイロード（トルク計算に必須・段階2）
- ヘッド(ツール)＋把持ワークの**質量・重心**が要る。当面は既定値/推定、後で実測へ差し替え（Ros2MotorLimits と同じ「暫定値」扱い）。
- 供給元は **PlanRequest の payload_mass / payload_com**（Unity→ROS2）で渡す方式に決定。値の元は：
  - 段階2着手時に `Ros2Info` の robots[] に payload 項目を足す or 既定値。URDF tool link に持たせる案は ROS2 側で完結するが Unity から可変にできないため見送り。

## 途中経過（必須）※既存 `/kmx/plan_status` を流用（新トピック不要）
既存の `/kmx/plan_status`（`std_msgs/String`, ROS2→Unity）は Unity 側が購読済み＆パネル表示済み。ここに**進捗行**を流す。
- 形式（key=value・スペース区切り。Unity は前方一致で判別＆パース）：
  - `opt phase=<time|jerk|torque> iter=<n> time=<s> jerk=<v> torque=<v> prog=<0..100>`
  - 例: `opt phase=jerk iter=42 time=1.85 jerk=12.3 prog=60`
- 完了・結果も同トピックで（下記「応答」参照）。
- Unity：`opt ` 始まりを進捗として認識し、status テキスト＋`prog` から簡易進捗バー表示。

## 要求（Unity→ROS2）※既存 `/kmx/plan_request`（PlanRequest.msg）を拡張
PlanRequest.msg の「0/空＝既定＝後方互換」方式に合わせ、**任意フィールドを追加**（robot_id と同じ再生成に相乗り）：
```
bool    optimize        # true=登録最適化モード。false/未設定=通常計画（後方互換）
float64 target_time     # 目標所要時間[秒]。0以下=成り行き（時間制約なし＝ジャーク/トルクのみ最適化）
# --- 段階2(トルク)用・当面は既定値。0以下=既定 ---
float64 payload_mass    # 把持ペイロード質量[kg]（ツール込み）
float64[] payload_com   # ペイロード重心[m]（フランジ相対 x,y,z。空=既定）
```
- 優先順位は固定＝time>jerk>torque（将来 weights 化可）。
- 単位注意：robotStep.time は **ms**、PlanRequest.target_time は **秒** → Unity 側で /1000 して送る（既存 time_budget と単位統一）。

## 応答（ROS2→Unity）
- 最適化済み軌道 → 既存 `/kmx/trajectory`（trajectory_msgs/JointTrajectory, 度）。
- 結果サマリ → 既存 `/kmx/plan_status`（String）に完了行：
  - `opt done time=<achieved_s> feasible=<0|1> min_time=<s> jerk=<v> torque=<v>`
  - `feasible=0`（target_time 未達）なら `min_time` に達成可能な最小時間。Unity が「target_time 未達／最小 X.XXs」を警告表示。
- 構造化した結果 msg が要るほど項目が増えたら専用 msg 化を検討（当面は String で十分）。

## ROS2 へ渡す情報（データの所在）★重要
最適化に必要な情報の「誰が持つか」を明確化する。**メッセージ契約はこのままで段階1＋点質量の段階2まで十分**（新フィールド追加は不要）。

### ① Unity が送る（per-request・動的）
- **経路の始点/終点**：PlanRequest `start[]` / `goal[]`（度）
- **関節名・数**：PlanRequest `names`
- **対象ロボット**：PlanRequest `robot_id`（現状 ""＝受信側既定）
- **最適化指示**：`optimize`(bool) / `target_time`(秒・0=成り行き)
- **ペイロード**：`payload_mass`(kg) / `payload_com`(m・フランジ相対) ※段階2トルク用。0/空=既定
- **シーン（衝突回避に必須）**：`/kmx/obstacles`（周辺障害物）＋ `/kmx/attached`（ツール/ヘッド。把持中は把持ワークを含む形状）を**計画前に送信済み**（`RequestPlanWithScene`＝SendObstacles+SendHead→plan）

### ② ROS2 が自前で持つ（送らない・URDF/MoveIt config）
- ロボットの運動学（リンク/関節）
- **リンク慣性**（トルク計算・段階2）
- **関節の 速度/加速度/ジャーク 上限**（joint_limits.yaml）
- **ヘッド先端(tip)フレーム**（Cartesian ジャークの対象点）＝ツール/フランジ

### ③ 整合が必須（両側で同じ値にする）
- **定格 関節速度/加減速/ジャーク**：ROS2 の joint_limits と Unity の `Ros2MotorLimits` を**一致**させる。食い違うと Unity 表示の「最短時間/軸速%」と ROS2 の最適化結果がズレる。**CRX-30iA は現在暫定値、要データシート差し替え（両側とも）**。
- **単位**：角度=度、時間=秒（`target_time` / `trajectory.time_from_start` とも秒）。

### 追記候補（今は不要・将来）
- `payload_inertia`（慣性テンソル Ixx..Izz）：大型/偏心ペイロードでトルク精度が要るとき。段ボール把持は質量+重心の**点質量近似で十分**。
- `tip_link` / `tip_offset`：Cartesian ジャーク対象点を明示したいとき。**段階1は関節ジャークで代替可**なので不要。
- 段階2でトルクを出すには「その step が把持中か（payload 有無）」を Unity 側 data で持つ必要（②pick&place ワーク連動 と統合）。現状 `payload_mass=0`（無負荷）固定。

## Unity 側（実装済み・段階）
- **PlanRequest.msg 拡張＋`Generate ROS Messages` 再生成 済**。`RosTcpConnectorTransport.PublishPlanRequest` で robot_id＋optimize＋target_time＋payload を**送信有効化済**。
- 登録モードの計画リクエストに `optimize=true` / `target_time`(=step.time/1000) を載せて送る（`ComRos2PathPlanner` の登録計画経路）。
- **`/kmx/plan_status` の `opt ...` 行を進捗として解釈**し、パネルに途中経過表示（既存 `OnPlanStatus`→statusText に相乗り。`prog=` で簡易進捗バー）。
- 完了で最適化軌道を**ゴースト＋シークバーでプレビュー**→OK で登録（既存フロー流用）。
- `opt done ... feasible=0` なら「target_time 未達／達成最小 X.XXs」を警告表示。
- ※実際に最適化が効くのは **ROS2 ノードが `optimize` 分岐＋`opt` 行 publish を実装してから**（それまでは通常計画で後方互換）。

## 実装段階
1. **時間最小＋ジャーク制限（Ruckig）＋進捗表示** … まずここから（軽め・payload 不要）。
2. **逆動力学トルク最小（Pinocchio）** を追加（重い・研究寄り。payload 供給が要る）。

## 契約サマリ（既存資産の流用・新規追加）
| 項目 | 使うもの | 追加/変更 |
|---|---|---|
| 要求 | `/kmx/plan_request`（PlanRequest.msg） | 任意フィールド `optimize` / `target_time` / `payload_mass` / `payload_com` を追加（要再生成） |
| 進捗 | `/kmx/plan_status`（String, 既存購読） | `opt ...` 行フォーマットを追加（新トピック無し） |
| 結果 | `/kmx/trajectory`＋`/kmx/plan_status` | `opt done ...` 行を追加 |
| 障害物 | `/kmx/obstacles`,`/kmx/attached` | 変更なし |

## ROS2 実装チェックリスト（TODO）
段階1（まず）：
- [ ] `kmx_planner` で PlanRequest の `optimize` を読み、true なら最適化経路へ分岐（false は現状の通常計画のまま）。
- [ ] `target_time`(秒) を読む。0以下＝時間制約なし。
- [ ] TOTG(scaling=1) で**限界最短**を算出 → `target_time` 達成可否を判定（不能なら `feasible=0`＋`min_time`）。
- [ ] 時間制約内で **ジャーク最小**の time-parameterization（Ruckig 等）で軌道生成。まずは**関節ジャーク**でOK。
- [ ] 既存の planning scene（`/kmx/obstacles`＋`/kmx/attached`）で**衝突回避**したまま最適化。
- [ ] `/kmx/plan_status`(String) に **進捗行** `opt phase=<time|jerk> iter=<n> time=<秒> prog=<0..100>` を定期 publish（0.2〜0.5s or iter毎）。
- [ ] 完了で最適化軌道を `/kmx/trajectory`(度) に publish＋`opt done time=<秒> feasible=<0|1> min_time=<秒>` を publish。
- [ ] 既存の `planning`/`succeeded:..`/`failed:..` も従来どおり出す（`opt` 行と併存）。

段階2（後）：
- [ ] `payload_mass`/`payload_com` を読み、逆動力学（Pinocchio/KDL・URDF慣性＋payload）で**関節トルク**算出。
- [ ] 時間・ジャークを満たす中で**トルクのピーク/RMS 最小**を追加（辞書式 第3優先）。
- [ ] 進捗/完了行に `torque=<v>` を載せる。

整合（必ず）：
- [ ] joint_limits.yaml の 速度/加減速/ジャーク上限を Unity `Ros2MotorLimits` と一致（**CRX-30iA は暫定値・要差し替え**）。
- [ ] 角度=度、時間=秒 を厳守。

## 関連
- 現状の Unity 側 robotSteps / 登録・キャッシュ / Step A(速度・時間解析) は実装済（`ComRos2PathPlanner` / `ComRos2PlanPanel` / `Ros2TrajCacheStore` / `Ros2MotorLimits`）。
- 「最短時間」の基準＝MoveIt が返す軌道の総時間（TOTG＋vel/accel scaling）。トルク推定(Step B)は未実装で本 spec に統合。
