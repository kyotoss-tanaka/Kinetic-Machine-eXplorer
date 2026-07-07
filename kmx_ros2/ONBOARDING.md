# KMX ⇄ ROS2 新人スタートアップガイド

新しく参加した人が、**全体像 → 用語 → 起動 → 経路計画 → トラブル対処**まで順に読めば動かせるようにまとめた資料です。
（対象：新規メンバー ／ FANUC CRX-30iA ／ ROS2 humble + MoveIt ／ Windows11 + WSL2 ／ rev. 2026-07-06）

> 📄 **見やすいビジュアル版（図つき）**：`https://claude.ai/code/artifact/de20e7f2-4ee8-4e3f-a3ba-2931c35c05f5`
> 開発者向けの詳細な引き継ぎは `HANDOFF.md`（=`~/ros2_ws/CLAUDE.md`）を参照。

---

## 1. これは何をするシステム？
ひとことで言うと **「画面の中の仮想ロボットを、本物のロボット制御ソフトで動かす」** 仕組み。登場人物は2人：

- **Unity（デジタルツイン）** … 工場や **FANUC CRX-30iA**（協働ロボット）を 3D で再現した「見た目」担当。オペレーターはここを操作する。
- **ROS2（ロボットの頭脳）** … 「どう関節を動かすか」「障害物を避ける通り道はどこか」を計算する「頭脳」担当。

ロボットの動かし方は **2通り**：
- **(a) 直接駆動** … 「J1 を 30度に」と角度を直接指示。
- **(b) 経路生成（MoveIt）** … ゴールだけ渡すと ROS2 が**障害物を避ける通り道を自動計算**。← 本システムの主役。

> **実機の FANUC には接続しない**（`use_mock:=true`＝ロボットを模擬）。安全に段取りを試せるのがデジタルツインの価値。

## 2. 全体の地図（アーキテクチャ）
Unity（Windows）と ROS2（WSL2/Ubuntu）は別々にいて、**ROS-TCP** が橋渡しする。

```
[Windows] Unity/KMX  ⇄  ros_tcp_endpoint  ⇄  [WSL2] kmx_planner + move_group(MoveIt)
  デジタルツイン画面        TCP 橋渡し              要求を受けて計画を発行 / 経路計算・衝突判定
```

流れ：**Unity がゴールを送る → ROS2 が通り道（軌道）を計算して返す → Unity が仮想ロボットで再生**。

> ⚠️ ROS2 が動いていないと Unity は橋につながれない（**橋自体が ROS2 側の一部**）。この「鶏と卵」は第5章の起動の話に効く。

## 3. まず覚える用語（ふわっとでOK）
| 用語 | ざっくり |
|---|---|
| デジタルツイン | 現実の設備を 3D でそっくり再現した「双子」 |
| ROS2 | ロボット用ソフトの共通土台。小さなプログラム（ノード）が連携 |
| ノード / トピック | ノード＝個々のプログラム。トピック＝メッセージの通り道（例 `/kmx/plan_request`） |
| MoveIt / move_group | ROS2 の経路計画フレームワーク。衝突を避ける動きを計算する本体 |
| OMPL / BITstar | MoveIt が使う計画アルゴリズム。既定は `BITstar`（速く・短い経路） |
| planning scene | 計画に使う「世界の状態」。障害物やヘッドが入る |
| 障害物 / attached | 床・機械カバー＝障害物。ロボット先端の道具＝attached（ヘッド） |
| WSL2 | Windows の中で動く Ubuntu。ROS2 はここで動く |
| bringup | 必要なノードをまとめて起動する launch |
| 軌道 (trajectory) | 計画結果＝通過する関節角度の並び。Unity が再生する |

## 4. 環境の地図と「正本」ルール
WSL2 側のワークスペースは3つ：

| ワークスペース | 中身 |
|---|---|
| `~/colcon_ws` | ROS-TCP-Endpoint（Unity⇄ROS2 の橋）。ブランチ `main-ros2` |
| `~/ros2_ws` | 本命。`kmx_planner`・`kmx_msgs`・FANUC 記述・MoveIt 設定 |
| `~/ws_moveit` | MoveIt 本体（ソースビルド）。move_group はここのものが動く |

**正本（せいほん）＝直接いじってはいけない場所がある**：
- コードの正本は **Windows 側の Unity リポ `kmx_ros2/`** の中。
- WSL の `~/ros2_ws/src/kmx_planner` は `sync.sh` でコピーされた**複製**。
- 🚫 複製を直接編集しても**次の sync で上書きされて消える**。必ず「正本を編集 → sync → build」（第6章）。

## 5. 起動・停止・再起動
一発で全部を制御するスクリプトが `~/ros2_ws/` にある：

| コマンド | 動作 |
|---|---|
| `./kmx_start.sh [use_moveit]` | 起動（冪等・既に起動中なら何もしない）。`false` で軽量モード |
| `./kmx_stop.sh` | 安全に停止 |
| `./kmx_restart.sh` | 停止→起動しなおし |
| `./kmx_status.sh` | 状態を1行表示 |

状態は3つ。**計画を送っていいのは `running_full` のときだけ**：
- `stopped` … 止まっている
- `starting` … 起動途中（待つ）
- `running_full` … 準備OK（計画できる）

```bash
# 手で起動する場合
~/ros2_ws/kmx_status.sh          # 状態確認
~/ros2_ws/kmx_start.sh           # 起動（軽量は kmx_start.sh false）
~/ros2_ws/kmx_status.sh          # running_full になればOK
```

**Unity のボタンから**は `wsl.exe` 経由で上のスクリプトを呼ぶ（実装は Unity 担当・`LAUNCH_CONTROL_UNITY_SPEC.md`）。

> ⚠️ 停止・再起動では**橋（endpoint）も落ちる**＝Unity 接続が一旦切れる。move_group が入れ替わるので **planning scene は空**になる → 再接続後に**障害物とヘッドを送り直す**。

## 6. コードを直すときの鉄則
```bash
# 1) 正本を編集（Windows 側 kmx_ros2/kmx_planner）
# 2) sync
bash "/mnt/c/Users/.../kmx_ros2/sync.sh"
# 3) build（必ずネイティブの ~/ros2_ws で）
cd ~/ros2_ws && colcon build --symlink-install --packages-select kmx_planner
# 4) 反映（再起動）
~/ros2_ws/kmx_restart.sh
```
- 🚫 **`/mnt/c`（Windows 側）で `colcon build` しない**（遅い・不安定）。
- ドキュメントは sync 対象外。`CLAUDE.md` と `HANDOFF.md` は同内容の手動ミラー＝片方直したら両方直す。

## 7. 通信の中身（トピック）
| トピック | 向き | 用途 |
|---|---|---|
| `/kmx/command` | Unity→ROS2 | 直接関節駆動（度） |
| `/kmx/state` | ROS2→Unity | 現在の関節角度（度） |
| `/kmx/plan_request` | Unity→ROS2 | 経路の要求（ゴール姿勢・度） |
| `/kmx/trajectory` | ROS2→Unity | 計画結果の軌道（度） |
| `/kmx/plan_status` | ROS2→Unity | 計画中/成功/失敗の通知 |
| `/kmx/obstacles` | Unity→ROS2 | 障害物（床・機械カバー等・メートル） |
| `/kmx/attached` | Unity→ROS2 | ロボットに付く道具＝ヘッド（メートル） |

- ⚠️ **単位**：関節系（command/state/plan_request/trajectory）は**度**、障害物・ヘッドは**メートル**。
- **全置換ルール**：障害物・ヘッドは受け取るたび総入れ替え。**空を送れば全消し／送らなければ前回のまま**（Unity で消しただけでは ROS2 に残る）。

## 8. 経路計画のしくみ
1. Unity が**障害物とヘッドを送る**→少し待って**ゴール姿勢**（`/kmx/plan_request`）を送る。
2. ROS2 が `planning` を通知し、**BITstar** で**衝突しない通り道**を探す。
3. **時間内に何度もトライ**し、成功した中から**いちばん短い経路**を採用（大回り回避）。
4. 採用経路を**ショートカット**で滑らかに整える。
5. `/kmx/trajectory` で軌道を返し `succeeded` 通知 → Unity がプレビュー → OK なら再生。

- 難所（ヘッドが箱のすき間を通る＝**narrow passage**）は通り道が見つかりにくいので、リトライ＋最短採用で粘る。要求ごとに `time_budget`（粘る時間）を増やせる。
- ✅ 計画は **plan-only**。**RViz / move_group 上ではロボットは動かない（正常）**。動くのは Unity 側だけ。

## 9. 困ったとき
| 症状 | 原因と対処 |
|---|---|
| 計画がずっと失敗（`failed:GOAL_STATE_INVALID` / `-27`） | ゴール姿勢が**何かと衝突**。多くは**古い障害物・ヘッドの残留**。Unity から空を送って作り直すか `clear_scene.py` で強制クリア |
| Unity で消したのにオブジェクトが消えない | ROS2 は**空メッセージで初めて消す**。空の obstacles / attached を明示送信 |
| ヘッドが1個の箱になる | 仕様。コライダーが多い(実測395個)と重いので `attached_merge_over`(=12) 超で自動1箱化。少数（間引き）で送れば形状は保たれる |
| RViz でロボットが動かない | **正常**。plan-only なので RViz では動かない。可視化は Unity |
| `/kmx/state` に "Not registered" | Unity 起動後に endpoint 再起動で一時的に出る→自然回復。順序は endpoint→Unity が綺麗 |
| ビルドが遅い/失敗 | `/mnt/c` でビルドしていないか確認。必ず `~/ros2_ws`。symlink 衝突は `rm -rf build install log` |
| 起動したか不明 | `kmx_status.sh` で確認。ログは `~/ros2_ws/kmx_bringup.log` |

## 10. 担当分担と参考資料
- **ROS2 側（WSL）**：ビルド/起動/デバッグ、`kmx_planner` 拡張、経路計画・衝突判定・障害物反映。
- **Unity 側（Windows）**：デジタルツイン表示と操作 UI、障害物/ヘッド/ゴールの送信、軌道の再生・プレビュー。

**リポジトリ内の資料**：
- `README.md` … 初回セットアップ（A〜E）
- `RUN.md` … 起動手順の詳細
- `HANDOFF.md` / `CLAUDE.md` … 開発の引き継ぎ全体像（同内容）
- `OBSTACLES_ROS2_SPEC.md` / `HEAD_TOOL_ROS2_SPEC.md` … 障害物・ヘッドの仕様
- `PLAN_STATUS_ / PLAN_BUDGET_ / LAUNCH_CONTROL_UNITY_SPEC.md` … Unity 連携の各仕様
- `HANDOFF_curobo.md` / `HANDOFF_rrtstar_smart.md` … 高度な計画バックエンドの経緯

> **最初の一歩**：第5章の `kmx_start.sh` → `kmx_status.sh` で `running_full` を出す。次に Unity をつないでゴールを1つ送り、軌道が返るのを見る。ここまでできれば全体像がつかめます。
