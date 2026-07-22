# 登録最適化：探索中バックグラウンド STOMP（先行後処理＋キャッシュ）ROS2 要求仕様

対象ノード：`kmx_ros2/kmx_planner/kmx_planner/planner_node.py`（登録モード＝`optimize=True` の経路生成）
関連：`REGISTER_OPTIMIZE_ROS2_SPEC.md`（現行の探索→候補→STOMP 再設計）、`PLAN_PROGRESS_UNITY_SPEC.md`（`/kmx/plan_status` 進捗契約）
Unity 側：ほぼ変更不要（進捗表示の文言のみ）。**復帰モード（`optimize=False`）は一切変更しない。**

---

## 1. 目的（なぜ）
登録の経路計画時間を短縮する。現状は **探索がすべて終わってから**、貯めた候補（既定 `register_candidates=10`）を **直列に STOMP 後処理**している（`stomp_budget_sec=8s`×候補数＝終盤にまとめて重い）。
一方、登録探索は **`npa=1`（単発 BITstar・1コア）**（[planner_node.py:810-813](kmx_planner/kmx_planner/planner_node.py#L810-L813)）で、24コア機では**探索中に約23コアが遊んでいる**。この遊休コアで **探索と並行に候補を先行 STOMP しキャッシュ**しておけば、探索終了時にはトップ候補が処理済み＝**終盤のまとめ後処理を消せる**。

体感計画時間（壁時計）は「探索の裏に STOMP を隠す」ことで、先行本数を 3→10 に増やしても大きく変わらない（遊休コア・長い探索予算があるため）。総CPU負荷は本数に比例して増えるが、offline register なので許容。

---

## 2. 現状（変更前の直列パイプライン）
- 探索：`_send_plan_attempt`→`_on_result`（[927](kmx_planner/kmx_planner/planner_node.py#L927)）で成功軌道を `session['candidates']` に cost 昇順・上位 N 本保持（[940-944](kmx_planner/kmx_planner/planner_node.py#L940-L944)）。
- 完了：`_maybe_retry_or_finish`（[956](kmx_planner/kmx_planner/planner_node.py#L956)）→ `_optimize_and_publish`（[1207](kmx_planner/kmx_planner/planner_node.py#L1207)）で
  1. `_dedup_candidates`（ホモトピー重複排除）
  2. **候補を1本ずつ `_stomp_build`**（[1239-1248](kmx_planner/kmx_planner/planner_node.py#L1239-L1248)・各 `stomp_budget_sec`）
  3. 衝突フリーな中で `achieved`（最終実行時間）最小を採用し発行。
- オラクル（pin+coal 衝突）は `_build_stomp_oracle`（[1362](kmx_planner/kmx_planner/planner_node.py#L1362)）で**1回だけ構築**し全候補で使い回す。
- ステータス：`opt phase=search best=X` / `opt phase=stomp candK/N ... prog=` / `opt done ...`。

**課題**：STOMP がすべて探索の後（直列）＝終盤に最大 `8s × 候補数` の待ちが乗る。

---

## 3. 提案（探索中バックグラウンド STOMP プール）
探索を止めずに、**安定したトップ候補を遊休コアで先行 STOMP し、結果を候補 IDでキャッシュ**する。探索終了時は**キャッシュ済みの現トップ候補から最短を発行**（未処理があればその分だけ同期 STOMP）。

### 3.1 候補の一意キー（identity）
候補ベース軌道の**関節位置を量子化してハッシュ**（例：各点 positions を 1e-3 rad で丸めてタプル→hash）。用途：
- キャッシュキー（`stomp_cache[key] = result`）
- 二重投入防止（同一 base を複数回 STOMP しない）
- churn 追跡（top-N に居続けているか）

`_dedup_candidates` の同ホモトピー判定（`stomp_dedup_deg`）とは別レイヤ（こちらは厳密同一性）。

### 3.2 安定化ゲート（無駄仕事の抑制）
探索が進むとトップ候補は入れ替わる（[942-944](kmx_planner/kmx_planner/planner_node.py#L942-L944)）。すぐ圏外に落ちる候補を STOMP すると無駄。よって：
- **`register_bg_stomp_start_after`（既定 6）本の候補が貯まってから**先行 STOMP を開始。
- 各候補は **top-N に `register_bg_stomp_stable_hits`（既定 2）回連続で入っていたら**「安定」とみなし投入対象にする。
- 既にキャッシュ済み or 投入済みの候補はスキップ。

### 3.3 並列度（遊休コア上限）
- ワーカ数 `register_bg_stomp_workers`（既定 0＝自動）。自動時は `max(1, min(register_candidates, cpu_count - 2))`（探索1コア＋余裕を残す）。
- 同時に走る STOMP は最大ワーカ数。キューが空くたび「現トップの未処理・安定候補」を投入。

### 3.4 先行処理する本数
- `register_bg_stomp_topn`（既定 0＝`register_candidates` と同じ＝全キープ候補）。ユーザーの整理どおり **3 に絞ってもよいし、遊休コアが許すなら 10 まで**。0=全部。

### 3.5 キャッシュのライフサイクル
- セッション（1計画）単位。`session['stomp_cache']`（key→result）と `session['stomp_inflight']`（投入済み key 集合）。
- 圏外に落ちた候補のキャッシュは**捨てない**（再浮上時に再利用）。メモリは候補数×1軌道で小。
- 新しい plan 要求／セッション破棄でクリア。

### 3.6 完了時（`_optimize_and_publish` の置き換え）
1. 現候補を dedup。
2. 各候補について：**キャッシュ済みなら即採用、未処理なら同期 `_stomp_build`**（従来ロジックのフォールバック）。
3. 衝突フリーな中で `achieved` 最小を採用し発行（採用ロジックは現行のまま）。
→ 先行処理が効いていれば大半が即ヒットし、終盤の重い直列 STOMP が消える。

### 3.7 中断（cancel）との整合
- `/kmx/plan_cancel`（「探索停止」）は「現在の best を最適化して確定」の意図（[1223-1227](kmx_planner/kmx_planner/planner_node.py#L1223-L1227)）。
- cancel 時：**進行中の先行 STOMP は完走を待つ**（またはキャッシュ済みを使う）→現トップから即発行。cancel が STOMP を 0 反復にする現行の轍を踏まないよう、先行プールは cancel と独立予算で回す。

---

## 4. スレッド安全（★最大の実装リスク）
先行 STOMP をワーカで回すため、**オラクル（pinocchio + coal 衝突）と STOMP-lite がワーカ実行で安全**である必要がある。
- **スレッド方式（ThreadPoolExecutor）**：GIL のため純Python部は並列化されない。STOMP-lite の重い部分（numpy 行列演算・coal 距離）が GIL を解放するなら実効並列。**pin/coal のモデルはスレッド間共有不可の可能性**があるので、**ワーカごとにオラクルを1個ずつ構築（`_build_stomp_oracle` を worker-local に）**するのが安全。構築コストは1回/ワーカ。
- **プロセス方式（ProcessPoolExecutor）**：真の並列だが、オラクル（pin/coal）が **picklable でない**と使えない→ワーカ側で URDF から再構築する起動関数が要る。メモリ増。
- **推奨**：まず **ThreadPool＋worker-local オラクル**で実装し、GIL 解放が不十分（並列が効かない）なら ProcessPool へ。どちらも `register_backend='stomp'` かつ `_build_stomp_oracle` 成功時のみ。失敗/例外は**現行の直列 STOMP へ自動フォールバック**（安全第一）。

rclpy 実行モデル：ノードは async コールバック（既定 single-thread executor）。先行 STOMP は **executor 外の別プール**で回し、結果は**スレッドセーフな dict＋ロック**で受け渡す（executor をブロックしない）。ステータス publish は**メインスレッド側で集約**（rclpy publish をワーカから直接呼ばない）。

---

## 5. 新パラメータ（すべて登録のみ・既定で現行同等 or 安全側）
| パラメータ | 既定 | 意味 |
|---|---|---|
| `register_bg_stomp` | `true` | 探索中の先行 STOMP を有効化（false＝現行の直列のみ） |
| `register_bg_stomp_workers` | `0` | 同時 STOMP 本数。0＝自動（`min(register_candidates, cpu-2)`） |
| `register_bg_stomp_topn` | `0` | 先行処理する上位本数。0＝`register_candidates` と同じ |
| `register_bg_stomp_start_after` | `6` | 候補がこの本数貯まってから先行開始 |
| `register_bg_stomp_stable_hits` | `2` | top-N に連続 in した回数で「安定」＝投入対象 |

`ros2 param set` でライブ調整可。`register_bg_stomp=false` で**完全に現行動作へ戻せる**（回帰時の保険）。

---

## 6. `/kmx/plan_status` 契約（追加・後方互換）
Unity の既存パーサを壊さないよう、既存行はそのまま。先行処理の可視化は任意で追加：
- 追加（任意）：`opt phase=bg_stomp done=K/M`（先行完了 K 本 / 対象 M 本）。Unity 未対応でも無害（else 節でそのまま表示 or 無視）。
- 完了時の `opt phase=stomp candK/N ... prog=` と `opt done ...` は**現行どおり**（先行キャッシュを使っても最終発行の見え方は不変）。

Unity 側実装（任意）：`ComRos2PathPlanner.ParseOptStatus` に `phase=="bg_stomp"` 分岐を足し「先行後処理 K/M」を表示。無くても既存表示で問題なし。

---

## 7. Unity 側の変更
- **必須：なし**（ROS2 内部のパイプライン変更）。
- 任意：`bg_stomp` の進捗文言追加のみ。`PlanRequest` の契約変更は**不要**（ノードのパラメータで制御）。

---

## 8. リスクとフォールバック
| リスク | 対策 |
|---|---|
| pin/coal がスレッド非安全 | worker-local オラクル（各ワーカで1個構築）。ダメなら ProcessPool |
| GIL で並列が効かない | ThreadPool で効果薄→ProcessPool へ切替（param or 実装分岐） |
| churn で無駄 STOMP | 安定化ゲート（start_after / stable_hits） |
| 例外・衝突・import不可 | 現行どおり**直列 STOMP→legacy へ自動フォールバック** |
| CPU/熱の増加 | workers 上限＝コア数-2。offline register なので許容。速さ優先で topn/workers を下げられる |
| cancel との競合 | 先行プールは cancel と独立予算。cancel は「現トップを確定」 |

**復帰モード（optimize=false）・既存の候補採用ロジック・発行前ゲート・validate-what-you-publish は不変。**

---

## 9. 検証
1. `register_bg_stomp=false` で現行と bit-identical な発行（回帰なし）を確認。
2. `true`（workers=auto）で同一シーンを登録：
   - **総計画時間が短縮**（終盤の直列 STOMP バッチが消える）を実測（`opt` ログの phase 別経過を比較）。
   - 発行される軌道の `achieved` が現行と**同等以上**（品質劣化なし）。
3. `register_bg_stomp_topn=3` と `=10` で**壁時計の差が小さい**こと・`10` で品質が最良になることを実測（ユーザー仮説の確認）。
4. 探索中に `/kmx/plan_cancel`：先行キャッシュから即確定、空発行にならないこと。
5. 長時間（10分）安定・SIGSEGV/デッドロックなし（BITstar crash 露出は変えない）。

---

## 10. 段階実装（Phase）
- **Phase 1**：ThreadPool＋worker-local オラクルで先行 STOMP＋キャッシュ。完了時はキャッシュ優先・未処理は同期フォールバック。`register_bg_stomp` で on/off。ステータスは既存のまま。
- **Phase 2**：`opt phase=bg_stomp done=K/M` 進捗を publish＋Unity 表示。安定化ゲートのチューニング（start_after/stable_hits）。
- **Phase 3（必要なら）**：ThreadPool の並列が不足なら ProcessPool 化（オラクルをワーカで URDF から再構築）。

---

### 実装の当たり所（ROS2）
- 候補投入フック：`_on_result`（[940-949](kmx_planner/kmx_planner/planner_node.py#L940-L949)）で top-N 更新後に「安定＆未投入」候補をプールへ submit。
- 先行 STOMP 本体：`_stomp_build`（[1381](kmx_planner/kmx_planner/planner_node.py#L1381)）を worker-local オラクルで呼ぶ薄いラッパ。結果は `session['stomp_cache'][key]` へ（ロック）。
- 完了置換：`_optimize_and_publish`（[1207](kmx_planner/kmx_planner/planner_node.py#L1207)）の候補ループを「キャッシュ hit なら再利用／miss なら同期 `_stomp_build`」に変更。採用・発行は現行のまま。
- セッション破棄・新規要求でプール中断＆キャッシュclear。
