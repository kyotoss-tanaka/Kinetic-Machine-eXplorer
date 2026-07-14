# 経路計画 進捗ステータス仕様（復帰計画のフェーズ通知 & 無応答タイムアウト）

作成: 2026-07-14 / 対象: KMX(Unity) ⇄ kmx_planner(ROS2)

## 1. 目的
1. **誤タイムアウト解消**：Unity の固定タイムアウトが、ROS 側の実計画時間（BITstar リトライ ＋ RRTConnect フォールバック ＋ 後処理）を超えて**誤って「失敗」表示**する問題を、ROS からの**ハートビート**で解消する。
2. **詳細ステータス表示**：いま「どのフェーズ（BITstar / RRTConnect / 後処理）を何回目 実行中か」を Unity に表示する。

## 2. 対象・非対象
- **対象**：復帰計画（通常の「計画」ボタン、`optimize=false`）。
- **非対象**：登録最適化（`optimize=true`）は既存の `opt ...` 進捗通知を継続（**本仕様で変更しない**）。

## 3. 背景（現状の処理と時間）
復帰計画の流れ（既定パラメータ）:

| フェーズ | 使うパラメータ（ros2 param 既定） | 時間の目安 |
|---|---|---|
| ① BITstar 計画（8並列・各試行 `allowed_planning_time`・予算内でリトライ） | `plan_time_budget_sec`=10 / `allowed_planning_time`=3 / `num_planning_attempts`=8 / `plan_retries`=20 | **予算まで（既定〜10s、最後の試行が跨ぐと +最大3s）＝概ね 3〜4 回** |
| ② BITstar が**1本も見つからない**とき → **RRTConnect** に切替えて**1回だけ**（予算超過でも実行） | `plan_fallback_planner`=RRTConnect / `allowed_planning_time`=3 | **〜3s** |
| ③ 後処理（経路短縮＋ジャーク再タイム） | — | 1〜数s |

- BITstar は**1本でも経路が見つかれば** RRTConnect に行かず、予算いっぱい短い経路を探して最良を返す。
- RRTConnect に落ちるのは**BITstar が全滅（best_traj=None）**のときだけ。
- 合計は概ね「予算 ＋ 3s ＋ 後処理」。実測は各試行/後処理/遅延の上乗せで **20s を超えることがある**。

現状の問題：Unity は「総経過 > 固定秒(既定20〜30s)」で失敗判定していたため、予算をいくつにしても固定秒で**誤タイムアウト**していた。

## 4. トピック
- `/kmx/plan_status` （`std_msgs/String`）… **既存**。ROS→Unity。本仕様はこのトピックにメッセージを追加するだけ（新規トピックなし）。

## 5. メッセージ仕様（ROS が publish するもの）
復帰計画セッション中、以下を `/kmx/plan_status` に publish する。フォーマットは**既存の `opt ...` と同じ key=value 形式**。

| タイミング | メッセージ | 意味 |
|---|---|---|
| 各 **BITstar** 試行の**直前** | `planning phase=bitstar attempt=<n>/<m>` | n=現在の試行回数（1始まり）、m=想定最大回数 |
| **RRTConnect** フォールバック試行の**直前** | `planning phase=rrtconnect` | BITstar 全滅→RRTConnect を1回実行 |
| **後処理**（短縮＋再タイム）の**直前** | `planning phase=postprocess` | 経路整形中 |
| 成功時（**既存・変更なし**） | `succeeded:<点数>:<倍率>` ＋ 軌道を `/kmx/trajectory` へ | — |
| 失敗時（**既存・変更なし**） | `failed:<理由>` | 例 `failed:no_solution` |

- `<m>`（想定最大回数）＝ `ceil(budget / allowed_planning_time)`（`budget>0` のとき）、`budget<=0` なら `plan_retries`。あくまで**表示用の目安**（実回数は前後し得る。n が m を超えたら Unity 側で m にクランプ表示）。
- `budget` は Unity の PlanRequest.time_budget（>0）優先、無ければ `plan_time_budget_sec`。

## 6. ★ ROS 側への要求（kmx_planner の実装）
`kmx_planner/kmx_planner/planner_node.py` に以下を実装する：

1. **セッション開始時**（`plan_with_moveit` のセッション組立）で、想定最大回数を算出して session に保持：
   ```python
   _allow = max(0.1, float(self.get_parameter('allowed_planning_time').value))
   _est_max = (max(1, int(math.ceil(budget / _allow))) if budget > 0
               else max(1, int(self.get_parameter('plan_retries').value)))
   # session['est_max'] = _est_max
   ```
2. **`_send_plan_attempt`**：`optimize` でない場合、試行を送る**直前**にハートビートを publish：
   ```python
   phase = 'rrtconnect' if session.get('fallback_used') else 'bitstar'
   self._publish_status(f"planning phase={phase} attempt={session['attempts']}/{session.get('est_max', session['max_attempts'])}")
   ```
   （`fallback_used` は RRTConnect 切替時に True 済みなので、フォールバック試行では自動的に `phase=rrtconnect` になる）
3. **後処理の直前**（`_maybe_retry_or_finish` の復帰成功パス＝ optimize でない `best_traj` 確定後、経路短縮/`_jerk_retime` の直前）で：
   ```python
   self._publish_status("planning phase=postprocess")
   ```
4. 既存の `opt ...` / `succeeded:...` / `failed:...` は**変更しない**。

**再ビルド/再起動**：`~/ros2_ws` へ sync → `colcon build --packages-select kmx_planner` → bringup 再起動（配布キットを使う場合は make_kit も更新）。

## 7. Unity 側の挙動（実装済み方針）
`ComRos2PathPlanner` / `ComRos2PlanPanel`：

1. `OnPlanStatus` で `planning phase=...` を受信したら **ウォッチドッグ（`lastPlanMsgTime`）をリセット**し、フェーズ表示文字列を更新：
   - `phase=bitstar`     → **「経路計画1 実行中 (n/m)」**
   - `phase=rrtconnect`  → **「経路計画2 実行中」**
   - `phase=postprocess` → **「後処理中」**
2. **タイムアウト判定を「無応答」方式に変更**：
   `(現在時刻 − lastPlanMsgTime) > planTimeoutSec` で失敗とみなす（＝進捗が `planTimeoutSec` 秒 途絶えたらハング扱い）。ハートビートが来る限りリセットされるので、**予算やフォールバックの長さに依存せず誤タイムアウトしない**。
3. 成功（軌道受信）・失敗（`failed:...`）は**従来どおり即反映**。ウォッチドッグはハング検出専用のバックストップ。
4. パネルの状態行は、計画中に `PlanPhaseText` があればそれ＋経過秒を表示（無ければ従来の「計画中… 経過X秒」）。

## 8. 効果
- 予算をいくつにしても、**ROS が生きていれば誤タイムアウトしない**（ハートビートで都度リセット）。
- ユーザーが「今どのフェーズを何回目 実行中か」を確認でき、「失敗表示のあとに動く」混乱が解消。

## 9. 互換性・注意
- **ROS(kmx_planner) と Unity の両方**の更新が必要。**ROS を先に（または同時に）**更新するのが安全。
- **ROS 未更新（ハートビート無し）の場合**：Unity は最初の `planning` から `planTimeoutSec` 秒 無応答で失敗＝概ね従来同等。予算が `planTimeoutSec` より大きいと誤発火し得るので、**ROS 更新までは大きい予算に注意**。
- 既存の登録最適化（`opt ...`）とはメッセージ接頭辞が違う（`opt` vs `planning`）ので衝突しない。

## 10. 関連
- 登録最適化の進捗仕様：`REGISTER_OPTIMIZE_ROS2_SPEC.md`
- 起動制御：`LAUNCH_CONTROL_UNITY_SPEC.md`
- 計画メッセージ：`PlanRequest`(names,start,goal,time_budget,good_ratio)
