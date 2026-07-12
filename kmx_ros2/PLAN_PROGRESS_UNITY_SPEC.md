# 【Unity(KMX)側 実装要望】登録最適化の進捗を /kmx/plan_status で表示する

**方向**：**ROS2側 → Unity側** への依頼。ROS2 側は**進捗 publish を実装・検証済み**。
**Unity は既存の `/kmx/plan_status`（std_msgs/String・reliable）を購読しているので、文字列を解釈して進捗バー/フェーズ表示を出すだけ**。

---

## 0. 背景 / 現状（2026-07-12）
- 登録最適化（optimize）は候補ベースを増やした（`register_candidates=10`）ため **計画に ~1〜2分**かかる。その間 Unity に進捗が見えないと固まったように見える。
- そこで ROS2 は `/kmx/plan_status` に **探索フェーズ＋候補ごとの最適化フェーズの進捗**を細かく流すようにした（実装・実測済）。
- **Unity は既に `/kmx/plan_status` を購読済**（計画中表示・プレビュー→OK/Cancel 用）。**メッセージ追加作業は不要、文字列を解釈して表示するだけ**。全て後方互換（未対応でも従来の `planning`/`succeeded`/`failed` は不変）。

## 1. メッセージ仕様（`/kmx/plan_status`・std_msgs/String・reliable）
発行順の例（登録・候補5本の場合）：
```
planning
opt phase=search iter=0 best=0.00           ← BITstar 探索(候補収集)。best=現在の最良経路の総時間[s]（0=未発見）
opt phase=search iter=6 best=6.93            ← 2秒 or best更新ごとに throttle
opt phase=stomp cand1/5 iter=0 prog=10       ← 候補1/5 の STOMP 最適化 開始（prog=全体進捗% 10〜95）
opt phase=stomp cand1/5 iter=5 feasible=1 prog=16   ← ~1.5秒ごとに tick。feasible=1で衝突フリー解あり
opt phase=stomp cand2/5 iter=0 prog=27
...
opt phase=stomp cand5/5 iter=20 feasible=1 prog=95
succeeded:1260:1.03                          ← 発行完了。<点数>:<cost/直線距離 比>
opt done time=11.12 feasible=1 min_time=11.12 jerk=828.4   ← 最終メトリクス（time=実行時間[s], jerk=deg/s³概算）
```
失敗時：`failed:<reason>`（`no_solution`/`collision`/`bad_request`/`code_-N` 等）。

**フィールドの意味**：
- `phase=search`：候補収集中。`iter`=試行回数、`best`=現時点の最良経路の総時間[s]（小さいほど良い・0=未発見）。**この間は prog を持たない**（不定＝スピナー表示 or 0-10% 固定）。
- `phase=stomp candK/N`：候補 K/N を最適化中。`iter`=STOMP反復数、`feasible`=1/0（衝突フリー解の有無）、**`prog`=全体進捗%（10→95 で候補間を按分）**。
- `succeeded:P:R` / `opt done …`：完了（prog=100 とみなす）。
- `failed:…`：失敗（prog リセット・理由表示）。

## 2. Unity 側の表示実装（推奨）
`Assets/Scripts/Com/Ros2/ComRos2PathPlanner.cs`（or plan_status 購読ハンドラ）で文字列を分岐：
```csharp
void OnPlanStatus(string s) {
    if (s == "planning") { ShowProgress(0, "計画開始…"); }
    else if (s.StartsWith("opt phase=search")) {
        double best = ParseField(s, "best");   // 0=未発見
        ShowProgressIndeterminate(best > 0 ? $"経路探索中… 最良 {best:F1}s" : "経路探索中…");
    }
    else if (s.StartsWith("opt phase=stomp")) {
        int prog = (int)ParseField(s, "prog");         // 10〜95
        string cand = ParseToken(s, "cand");           // "K/N"
        bool feas = ParseField(s, "feasible") > 0.5;   // 無い最初のtickは false 扱いで可
        ShowProgress(prog, $"最適化中 候補 {cand}{(feas ? " ✓" : "")}");
    }
    else if (s.StartsWith("succeeded")) { ShowProgress(100, "完了"); /* 既存のプレビュー→OK/Cancel へ */ }
    else if (s.StartsWith("opt done")) { /* time/jerk を詳細表示（任意） */ }
    else if (s.StartsWith("failed")) { ShowFailed(s.Substring(7)); }
}
```
- **進捗バー**：`prog`（10〜95）をそのまま使う。search 中は `prog` が無いので**不定（スピナー）** or 0-10% 固定。`succeeded`/`opt done` で 100%。
- **フェーズ表示**：「経路探索中（best=Xs）」→「最適化中 候補 K/N」→「完了」。`feasible` で「衝突フリー解あり ✓」を出すと安心感◎。
- **既存の「探索停止」ボタン**（`/kmx/plan_cancel` へ "cancel"）はそのまま有効：探索中に押すと**現在の最良候補で確定**（cancel 後も STOMP は走って発行される＝空にならない）。
- **watchdog（任意）**：tick は search で ~2秒、stomp で ~1.5秒ごとに来る。**5秒以上 tick が途切れたら異常**とみなすと固まり検知になる。

## 3. 検証
- Unity で登録要求 → 進捗バーが **探索(スピナー)→最適化 candK/N で 10→95%→100%** と動けば成功。
- ROS2 ログ/`ros2 topic echo /kmx/plan_status` で同じ文字列列が出る（実測確認済）。

## 4. 備考（ROS2側・触らない）
- 実装：`planner_node.py` の `_stomp_build` が候補ごとに開始 prog＋StompLite の `progress_cb`（~1.5秒ごと）で tick を publish。全体% は「候補 i/N ＋ 各 STOMP の経過/budget」で 10→95 に按分。
- 進捗の細かさは `stomp_budget_sec`（1候補の最適化秒数）と `register_candidates`（候補数）で決まる。計画を速くしたいなら `register_candidates` を下げる（進捗も短くなる）。
- 文字列フォーマットは既存の `planning`/`succeeded:P:R`/`opt done …`/`failed:…` を維持＋`opt phase=stomp` に `iter`/`feasible`/`prog` を追加した後方互換。
- 関連：`PLAN_STATUS_ROS2_SPEC.md`（plan_status の原設計）、`REGISTER_OPTIMIZE_ROS2_SPEC.md`（登録最適化）、`RETURN_SPEED_UNITY_SPEC.md`（復帰速度倍率）。
