# 【ROS2側 実装要望】経路計画のステータス通知（計画中 / 成功 / 失敗）

> **✅ ROS2側 実装・検証済（2026-07-06）**：`/kmx/plan_status`(std_msgs/String, reliable) を新設。
> `planning` / `succeeded:<points>:<ratio>` / `failed:<reason>`（`no_solution`/`GOAL_IN_COLLISION`/`bad_request`等）を publish。
> 補間モードも planning→succeeded。実測確認 `planning`→`succeeded:15:1.00`／`failed:bad_request`。新param `plan_status_topic`。
> **残＝Unity側**（購読・プレビュー→OK/Cancel・timeout保険）。seq は未実装（spec通り）。詳細 HANDOFF.md §4表/§9。

**方向：ROS2側 → Unity側**（受け皿は Unity が作る）。Unity で「計画中の表示」「成功/失敗の表示」「経路プレビュー→OK/Cancel」を
出したい。現状は **成功時だけ `/kmx/trajectory` が飛び、失敗は無言**（ノードはログのみ）＝ Unity が失敗を検知できず「だんまり」に見える。

## 要望：`/kmx/plan_status` を publish
- **型：`std_msgs/String`**（新規 kmx_msgs 不要＝再生成の手間なし。std_msgs は Unity 側で Generate 済み or すぐ生成可）。
- **QoS**：reliable（取りこぼし防止）。
- **publish するタイミングとペイロード（`:` 区切りの簡易文字列で十分）**：
  | 段階 | 例文字列 |
  |---|---|
  | 計画開始（`on_request` で MoveIt セッション開始時） | `planning` |
  | 成功（best trajectory を `/kmx/trajectory` に publish する直前） | `succeeded:<points>:<ratio>` 例 `succeeded:74:1.8` |
  | 失敗（時間予算内に解なし / MoveIt エラーで発行中止） | `failed:<理由>` 例 `failed:no_solution` / `failed:START_STATE_IN_COLLISION` |
- **補間モード(`use_moveit:=false`)** でも同様に `planning`→`succeeded` を出してくれると Unity 側が分岐不要で楽。
- （任意）複数要求の混線防止に、`plan_request` に付けた seq を status にも載せられると理想。まずは無しでOK。

## 受信側（Unity・こちらで実装）
- `/kmx/plan_status`(std_msgs/String) を購読 → 状態機械（Planning/Preview/Failed）を駆動。
- `succeeded` を受けても**すぐ再生しない**：`/kmx/trajectory` の軌道を 3D プレビュー表示 → ユーザーが OK なら再生 / Cancel なら破棄。
- 保険：status が来ない環境でも、`plan_request` 送信後 **一定時間(=time_budget+α) 軌道が来なければ Failed** とする timeout を持つ。

## 補足（従来どおり・変えない）
- 軌道本体は従来どおり成功時に `/kmx/trajectory`。`plan_status` は**状態通知専用**（軌道は載せない）。
- 失敗理由が取りやすいなら `failed:<reason>` に入れてくれると UI に出せて助かる（無ければ `failed` だけでも可）。
</content>
</invoke>
