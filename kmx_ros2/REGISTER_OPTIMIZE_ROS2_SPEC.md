# 登録軌道 最適化 ROS2 要求書（REGISTER_OPTIMIZE）

## 目的
登録モード（オフライン教示）で、開始姿勢→終了姿勢の関節軌道を**多目的最適化**して登録キャッシュにする。
Unity は最適化済み軌道を受け取り、ゴーストでプレビュー→OK で `Ros2TrajCache.json` に保存する（既存フロー）。
登録はオフラインなので**重い最適化でも可**。ただし**途中経過を Unity へ送る**こと。

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

## Unity 側（実装予定・段階）
- **前提**：PlanRequest.msg 拡張＋`Generate ROS Messages` 再生成が必要。**robot_id が未再生成のまま**なので、robot_id＋optimize＋target_time＋payload を**まとめて1回再生成**する（`RosTcpConnectorTransport.PublishPlanRequest` の robot_id 有効化と同時）。
- 登録モードの計画リクエストに `optimize=true` / `target_time`(=step.time/1000) を載せる（`ComRos2PathPlanner` の登録計画経路）。
- **`/kmx/plan_status` の `opt ...` 行を進捗として解釈**し、パネルに途中経過表示（既存 `OnPlanStatus`→statusText に相乗り。`prog=` で簡易進捗バー）。
- 完了で最適化軌道を**ゴースト＋シークバーでプレビュー**→OK で登録（既存フロー流用）。
- `opt done ... feasible=0` なら「target_time 未達／達成最小 X.XXs」を警告表示。

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

## 関連
- 現状の Unity 側 robotSteps / 登録・キャッシュ / Step A(速度・時間解析) は実装済（`ComRos2PathPlanner` / `ComRos2PlanPanel` / `Ros2TrajCacheStore` / `Ros2MotorLimits`）。
- 「最短時間」の基準＝MoveIt が返す軌道の総時間（TOTG＋vel/accel scaling）。トルク推定(Step B)は未実装で本 spec に統合。
