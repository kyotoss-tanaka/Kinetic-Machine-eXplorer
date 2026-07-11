# 【Unity(KMX)側 実装要望】復帰モードの速度倍率 speed_scale を PlanRequest に詰める

**方向**：**ROS2側 → Unity側** への依頼（PLAN_BUDGET_UNITY_SPEC と同型）。
ROS2 側は**受け皿を実装・検証済み**。**Unity が `PlanRequest.speed_scale` を送れば、復帰(通常計画)の動作速度を要求ごとに制御できる**。

---

## 0. 背景 / 現状（2026-07-11）
- **復帰モード(optimize=false)のタイミングを「加速度/ジャーク厳守」に修正**した。
  - 旧実装は距離比例の再タイム(`_densify_retime`)で**一定速**になり、始点/終点/角で速度が不連続→**加速度が上限を超過(実測1.09×)・ジャークは非強制**だった。
  - 新実装は登録と同じ **per-joint double-S(`_jerk_retime`)** で計時＝**速度・加速度・ジャークを全軸で厳守**（発行軌道に velocity/acceleration も付与）。角では一旦停止（復帰＝角停止OK の方針と整合）。
- **速度は「倍率(speed_scale)」で制御**：v/a/j 上限を一律 `speed_scale` 倍して計時する。
  - 例）`speed_scale=0.25` ＝各軸上限の25%で動く（ゆっくり安全）。スケール後の値 ≤ フル上限なので**厳守は常に保証**。
  - 実測：`0.25`→総時間3.84s(最大比0.25)／`0.5`→2.74s(0.50)／`1.0`→1.97s(1.00)＝倍率どおり・全上限内。
- **現状**：ROS2 は `PlanRequest.speed_scale` を受信し、`>0` なら採用／`0以下・未設定`なら node 既定 `return_speed_scale`(=0.25) を使用（CLI 検証済み）。
  **Unity はまだこの値を送っていない**（＝常に node 既定 0.25 で動く）。Unity から動作ごとに指定したい。

## 1. メッセージ定義（ROS2側で追加済み・**Unity は再生成が必要**）
`kmx_msgs/PlanRequest` に末尾追加：
```
...
float64 payload_mass
float64[] payload_com
float64 speed_scale    # 復帰モードの速度倍率(0<scale≤1.0)。小さいほど遅く安全。0以下=ROS2既定 return_speed_scale(0.25)
```
- ⚠ **フィールドを増やしたので Unity で `Robotics > Generate ROS Messages` の再生成が必要**（`PlanRequestMsg` に `speed_scale` を出す）。
- 後方互換：未設定＝0 → ROS2 既定。既存の送信コードは 0 のままでも動く（挙動は node 既定 0.25）。

## 2. 変更箇所（2ファイル）
### 2-1. `Assets/Scripts/Com/Ros2/RosTcpConnectorTransport.cs`
`PublishPlanRequest` に引数を追加して設定（time_budget/good_ratio と同じ要領）：
```csharp
public void PublishPlanRequest(string topic, string[] names, double[] startDeg, double[] goalDeg,
                               double timeBudget = 0.0, double goodRatio = 0.0,
                               double speedScale = 0.0)   // ← 追加
{
    ...
    ros.Publish(topic, new PlanRequestMsg {
        names = names, start = startDeg, goal = goalDeg,
        time_budget = timeBudget, good_ratio = goodRatio,
        speed_scale = speedScale });   // ← 追加
}
```
### 2-2. `Assets/Scripts/Com/Ros2/ComRos2PathPlanner.cs`
- Inspector で調整できる SerializeField を追加：
```csharp
[SerializeField, Range(0.05f, 1.0f)] private double returnSpeedScale = 0.25;  // 復帰の速度倍率(0=ROS2既定)
```
- 復帰(通常計画)の発行呼び出しに渡す：
```csharp
transport.PublishPlanRequest(planRequestTopic, jointNames, startDeg, goalDeg,
                             planTimeBudget, planGoodRatio, returnSpeedScale);
```
- **登録(optimize=true)の発行では渡さない（or 0）**：登録は `target_time` で時間制御するため `speed_scale` は無視される。

## 3. 値の指針
- **既定 0.25（25%）**：ゆっくり・安全。実機投入初期はこれを推奨。
- **0.5〜1.0**：速くしたいとき。`1.0` で joint_limits の上限をフルに使う（最速）。
- **動作ごとに変える**：ワークとの距離や安全要件で、退避は遅め・大きな移動は速め等。
- **0 のまま**：ROS2 既定(0.25)。まず 0（=0.25）で確認して問題なければ据え置き可。
- ※ `speed_scale` は **v/a/j 上限すべてに掛かる**（速度だけでなく加速度/ジャークも同率）。値を小さくすると滑らかさも増す（ジャークも下がる）。

## 4. 検証
- Unity で復帰プランを要求 → **ROS2(kmx_planner)ログに `plan request: … speed_scale=X`** が出れば送れている。今は出ていない＝未送信。
- 発行ログ `published best trajectory (復帰・jerk厳守 scale=X): …点 総時間=T` の **scale=X** が Unity 指定値になっていること。X を小さくすると総時間 T が伸びる。
- Unity 再生でロボットの復帰動作がゆっくり・滑らかになる（scale 小）／機敏になる（scale 大）ことを確認。

## 4.5 Unity 実装状況（2026-07-11・実装済）
- **生成物 `PlanRequestMsg.cs` に `speed_scale` を手当て**（Generate ROS Messages が拾えないため `.msg` 準拠で追記＝CDR順一致で wire互換。再生成する場合は更新済 `kmx_msgs/msg/PlanRequest.msg` を指すこと）。
- `RosTcpConnectorTransport.PublishPlanRequest` に `speedScale` 引数＋`speed_scale` セット。
- `ComRos2PathPlanner`：`[SerializeField, Range(0.05,1.0)] returnSpeedScale=0.25f` ＋ `public ReturnSpeedScale`。復帰(optimize=false)発行時のみ送る（登録=true は 0）。
- **UI：計画エリアに「復帰速度 XX%」スライダー**（0.05〜1.0）を追加＝**実行中に倍率を調整可**（次の計画発行から反映）。
- 要 in-engine コンパイル/動作確認（ROS2ログの `speed_scale=X` と発行 `scale=X`）。

## 5. 備考（ROS2側・触らない）
- 受信・適用は実装済み：`planner_node.py` の `on_request` が `msg.speed_scale` を読み、`plan_with_moveit(req_speed_scale=…)`→session→復帰発行部で `_jerk_retime(scale=…)` に渡す（`>0` の時だけ node 既定を上書き）。
- ROS2 既定パラメータ：`return_speed_scale`(=0.25)（`ros2 param set /kmx_planner return_speed_scale 0.4` 等でも全体既定を変更可）。
- **加速度/ジャーク上限そのもの**は `fanuc_moveit_config/config/joint_limits.yaml`（現行 v:80/120/180°/s・a:0.8/2.0 rad/s²・j:8/20 rad/s³。a/j は出荷値の2倍＝**実機許容は要検証の暫定値**）。`speed_scale` はこの上限に対する倍率。
- 補間モード(`use_moveit:=false`)は別タイミング（`duration_sec`）で `speed_scale` 非対象。
- 全体設計は `HANDOFF.md`、登録側の時間制御は `REGISTER_OPTIMIZE_ROS2_SPEC.md` 参照。
