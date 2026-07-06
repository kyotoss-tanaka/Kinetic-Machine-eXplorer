# 【Unity(KMX)側 実装要望】PlanRequest に計画予算(time_budget / good_ratio)を詰める

**方向が逆の要望書**：これは **ROS2側 → Unity側** への依頼（Obstacles/HEAD_TOOL は Unity→ROS2 の依頼だった）。
ROS2 側は受け皿を実装済み。**Unity が値を送れば、経路計画の「粘り具合（時間・大回り許容度）」を動作ごとに制御できる**。

---

## 0. 背景 / 現状
- 狭所（ヘッドがトート脇を通る）では、MoveIt(RRTConnect)＋リトライで計画するが、**難しい姿勢は時間をかけて粘りたい／簡単な姿勢は速く返したい**というニーズがある。
- そこで `kmx_msgs/PlanRequest` に **`time_budget`（秒）** と **`good_ratio`（直線距離比）** を追加済み（ROS2側で実装・CLI検証済み）。要求ごとに指定でき、**0/未設定なら ROS2 ノード既定にフォールバック**（後方互換）。
- **現状**：Unity は `Generate ROS Messages` 再生成済み（通信は成立）だが、**`PlanRequestMsg` にこの2値をセットしていない**ため 0 送信 → ROS2 は既定(budget=10s / good_ratio=2.0)を使用。＝**まだ Unity から制御できていない**。

## 1. メッセージ定義（既存・再生成済み）
`kmx_msgs/PlanRequest`：
```
string[] names
float64[] start
float64[] goal
float64 time_budget   # 計画の総時間予算(秒)。難しい姿勢は大きく/簡単なら小さく。0以下=ROS2既定(plan_time_budget_sec)
float64 good_ratio    # 大回り回避の許容倍率(始点→終点の直線関節距離比)。小さいほど短経路を要求。0以下=ROS2既定(plan_good_ratio)
```
※ フィールド追加済み。Unity は再生成済み。**メッセージ側の追加作業は不要**、値を詰めるだけ。

## 2. 変更箇所（2ファイル）
### 2-1. `Assets/Scripts/Com/Ros2/RosTcpConnectorTransport.cs`
現状 `PublishPlanRequest`（〜L112）が2値を設定していない：
```csharp
// 現状
public void PublishPlanRequest(string topic, string[] names, double[] startDeg, double[] goalDeg)
{
    ...
    ros.Publish(topic, new PlanRequestMsg { names = names, start = startDeg, goal = goalDeg });
}
```
→ 引数を足して設定：
```csharp
public void PublishPlanRequest(string topic, string[] names, double[] startDeg, double[] goalDeg,
                               double timeBudget = 0.0, double goodRatio = 0.0)
{
    ...
    ros.Publish(topic, new PlanRequestMsg {
        names = names, start = startDeg, goal = goalDeg,
        time_budget = timeBudget, good_ratio = goodRatio });
}
```

### 2-2. `Assets/Scripts/Com/Ros2/ComRos2PathPlanner.cs`
- Inspector で調整できるよう SerializeField を追加（or 動作/ゴールごとに値を持つ）：
```csharp
[SerializeField] private double planTimeBudget = 0.0;  // 0=ROS2既定
[SerializeField] private double planGoodRatio  = 0.0;  // 0=ROS2既定
```
- `RequestPlan(startDeg, goalDeg)`（〜L88-100）内の発行呼び出しを差し替え：
```csharp
transport.PublishPlanRequest(planRequestTopic, jointNames, startDeg, goalDeg,
                             planTimeBudget, planGoodRatio);
```
- `RequestPlanWithScene` 等も同経路（`RequestPlan` を通るなら自動反映）。

## 3. 値の指針
- **難しい姿勢（トート近傍・ヘッドが壁際を通る等）**：`time_budget` 大きめ（例 8〜15秒）＋ `good_ratio` 小さめ（例 1.5＝より短い経路を要求）。
- **簡単な動作**：`time_budget` 小さめ（速い）。`good_ratio` 既定(2.0)でよい。
- **0 のまま**：ROS2 既定（10s / 2.0）。まず 0 で問題なければ据え置き可。
- 動作ごとに変えたいなら、Test Plan 系メソッドや動作定義側で planTimeBudget/planGoodRatio をセットしてから発行。

## 4. 検証
- Unity で Test Plan → **ROS2(kmx_planner)ログに `plan request: … time_budget=X good_ratio=Y` が出れば成功**（Unity が送れている証拠）。今は出ていない＝未送信。
- ログ末尾 `published best trajectory: … (moveit, cost A→B, 直線=D [N倍], S/A 成功)` の **[N倍]** と反復数で、予算/比率の効き具合を確認できる（good_ratio を下げると倍率が下がり時間がかかる、等）。

## 5. 備考（ROS2側・触らない）
- 受信・適用は実装済み：`kmx_planner/planner_node.py` の `on_request` が `msg.time_budget`/`msg.good_ratio` を読み、>0 の時だけ node 既定を上書き（`plan_with_moveit` → セッション）。
- ROS2 既定パラメータ：`plan_time_budget_sec`(10) / `plan_good_ratio`(2.0)（`ros2 param set /kmx_planner …` でも変更可）。
- 全体設計は `HANDOFF.md` §6、計画の粘り/最適化の詳細は同 §6 の「リトライ＋経路最適化＋大回り回避」参照。
