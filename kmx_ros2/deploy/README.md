# KMX Deploy Kit — 新PCへの ROS2 環境 自動インストール

新PC（Windows + WSL2）へ KMX の ROS2 バックエンド一式を**各ステップ［実行］→［確認］**で入れるローカルWebウィザード。重いビルド（MoveIt/BITstar）は**旧PCで1回**だけ行い、新PCは**展開するだけ**。

## 構成
```
KMX-Deploy/
  KMX-Installer.ps1      ← 新PCで実行（PowerShellローカルサーバ＋ブラウザUI）。ヘッダで「新規インストール」/「アップデート」タブ切替
  ui.html                ← ウィザードUI（Installer が配信）
  steps.json / steps.sh  ← 各ステップのメタ(mode=install|update) と実体(s1..s8)
  make_kit.sh            ← 旧PCで1回実行してフルキットを作る（BITstar MoveIt 込み）
  make_update.sh         ← 旧PCでコード変更後に差分キットを作る（MoveIt 再ビルドなし・~8MB）
  Build-Kit.ps1 / Build-Update.ps1 ← 上記2つの Windows ランチャ（WSL 実行＋配布フォルダ組立）
  artifacts/             ← make_kit / make_update が生成（配布物）
    kmx_moveit.tgz         BITstar入り MoveIt（/opt/kmx_moveit へ展開）※フルのみ
    ros2_src.tgz           fanuc群＋kmx（~/ros2_ws/src）※差分はメッシュ/.git除外で軽量
    endpoint_src.tgz       ROS-TCP-Endpoint（~/colcon_ws/src）※フルのみ
    scripts.tgz            kmx_start/stop/restart/status.sh
    apply_update.sh        差分適用スクリプト（新PCの「アップデート」タブ／CLI が呼ぶ）
    Ros2Info.json          テンプレ（wslUser を置換）
  KMX-Unity/             ← Unity ビルド（任意・同梱すると配布が1つで済む）
```

## 手順A：旧PC（ビルド機）で1回だけ
```bash
bash make_kit.sh                 # ~/KMX-Deploy/artifacts/ に一式生成
# 生成物を KMX-Deploy/artifacts/ に配置し、KMX-Installer.ps1 と ui.html を同じ階層へ
```
※前提：旧PCに `~/ws_moveit`（BITstarバックポート MoveIt2 ソース）・`~/ros2_ws`・`~/colcon_ws`。
　kmx_planner と kmx_*.sh は**リポジトリ最新**（`KMX_RVIZ` 対応版）に同期しておくこと。

## 手順B：新PC（配布先）で
1. **WSL2＋Ubuntu-22.04** を導入（未なら管理者 PowerShell で `wsl --install -d Ubuntu-22.04`）。
2. `KMX-Installer.ps1` の先頭 `$WslUser` を新PCの WSL ユーザー名に。
3. `KMX-Installer.ps1` を右クリック→「PowerShell で実行」→ ブラウザが開く。
4. 上から各ステップを **［実行］→［確認］**（緑になったら次へ）。
   1. `.wslconfig`(mirrored)　2. 社内SSL/Zscaler CA　3. ROS2＋依存 apt
   4. **BITstar を /opt/kmx_moveit 展開**　5. fanuc/kmx/endpoint 展開（RViz非表示・slider削除込）
   6. colcon build　7. 設定（source/Ros2Info）　8. 起動＋BITstar疎通
5. Unity 側 `Ros2Info.json` の `wslUser`/`launchRviz` を確認（`launchRviz:false`=RViz非表示）。

## 手順C：既に配布済みの新PCへ「差分アップデート」（コードだけ更新）
ROS2 のコード（`kmx_planner` / `register` / config yaml / `kmx_msgs`）を直したときは、**フルインストーラを回さず**軽量更新できる。BITstar MoveIt(`/opt/kmx_moveit`)・apt・証明書・`.bashrc` は触らない（数十秒）。**インストールと同じ Web ウィザードの「アップデート」タブ**で完結する。

### C-1. 旧PC(ビルド機)で差分キットを作る
```powershell
# Windows: リポの変更を ~/ros2_ws に sync + colcon build 済みの状態で
powershell -ExecutionPolicy Bypass -File Build-Update.ps1   # -> C:\KMX-Update（deploy 一式 + slim artifacts。MoveIt 再ビルドなし）
```
（WSL 内で直接作るなら `bash make_update.sh` → `~/KMX-Deploy/artifacts`。`FULL=1` でメッシュ込み）

### C-2. 新PC(配布先)で Web ウィザードから適用
1. `C:\KMX-Update` を新PCへコピー（**既存の KMX-Deploy を置き換え**＝更新版ウィザードも入る）。
2. `KMX-Installer.ps1` を実行 → ブラウザで**ヘッダの「アップデート」タブ**を選択。
3. ステップ **「コード アップデート（展開→ビルド→再起動）」** を **［実行］→［確認］**（緑になれば完了）。
   - 中身＝`artifacts/apply_update.sh`：展開→ユーザ名置換→`colcon build`(kmx_msgs→kmx_planner ほか)→`kmx_restart`→`running_full` 待ち。
4. **`msg` を変えた場合のみ** Unity で `Robotics > Generate ROS Messages` を再生成（ROS↔Unity は wire 互換＝両側ロックステップ）。

CLI で済ませたいとき（Web を使わない）: 新PC WSL で `bash artifacts/apply_update.sh`（`UPDATE_ENDPOINT=1` で endpoint も更新）。Unity から `wsl.exe -e bash -lc ".../apply_update.sh"` でも可。

| 変えたもの | 使う手段 | Unity 再生成 |
|---|---|---|
| Python コード（`kmx_planner`/`register`/config yaml） | 手順C（Build-Update →「アップデート」タブ） | 不要 |
| `kmx_msgs`（PlanRequest 等のフィールド追加） | 手順C（apply_update が kmx_msgs も再ビルド） | **要** |
| MoveIt / BITstar | 手順A/B（Build-Kit フル → 「新規インストール」手順4-6） | 不要 |

## ポイント
- WSL 側は **`wsl -u root`** で実行するため **sudo パスワード不要**。
- **BITstar はスペック維持**（旧PCでビルド済みを固定パス `/opt/kmx_moveit` に展開するだけ）。
- **RViz** は `Ros2Info.json launchRviz` → `KMX_RVIZ` で表示/非表示、**slider は常時削除**。
- 詰まりどころ（Zscaler / fanuc依存 / ユーザ名 / ポート10000）は `../SETUP_NEW_PC.md` §5 も参照。
