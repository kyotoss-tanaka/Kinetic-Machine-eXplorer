# 【Unity(KMX)側 実装要望】Unity から ROS2 の起動 / 停止 / 再起動

**方向：ROS2側 → Unity側**（ROS2 側は制御スクリプトを提供済み。Unity がそれを呼ぶ）。
Unity のボタン等から WSL 上の ROS2 bringup を **起動 / 停止 / 再起動**できるようにする。

---

## 0. 方式と前提（重要）
- **方式A＝Unity(Windows) が `wsl.exe` を起動**して、WSL 上の制御スクリプトを実行する（`System.Diagnostics.Process`）。
- なぜ ROS 経由でないか：Unity⇄ROS2 は `ros_tcp_endpoint` 経由だが、その endpoint 自体が bringup の一部。
  **落ちている状態では ROS メッセージの受け手がいない**（鶏と卵）ため、コールドスタートは Windows プロセス起動で行う。
- **ROS2 側は実装・検証済み**：下記4スクリプトを `~/ros2_ws/` に用意（`stop→stopped / start→running_full(~4s) / restart→running_full` 動作確認済）。
  **Unity 側の作業＝これらを `wsl.exe` で呼ぶだけ**。

## 1. ROS2 側が提供するスクリプト（Unity は呼ぶだけ・変更不要）
| スクリプト（WSL 絶対パス） | 動作 | 出力(stdout) |
|---|---|---|
| `/home/kyotoss/ros2_ws/kmx_start.sh [use_moveit]` | bringup 起動（冪等・detach・即 return）。引数省略で `true` | `[kmx] starting ...` |
| `/home/kyotoss/ros2_ws/kmx_stop.sh` | 停止（SIGINT→10s→SIGKILL、子ノードも掃除） | `[kmx] stopped` |
| `/home/kyotoss/ros2_ws/kmx_restart.sh [use_moveit]` | 停止→2s→起動 | 上記2つ |
| `/home/kyotoss/ros2_ws/kmx_status.sh` | 状態を1行返す | **`stopped` / `starting` / `running_full`** |

- `use_moveit`：`true`（MoveIt 込み＝計画可能・既定）／`false`（endpoint＋planner 補間のみの軽量）。
- ※ ユーザー名が `kyotoss` 前提。異なる環境ではパスを合わせる（`/home/<user>/ros2_ws/`）。

## 2. Unity 側 実装（C#・`System.Diagnostics.Process`）
### 2-1. 基本呼び出し
```csharp
using System.Diagnostics;

// 例：起動。wsl.exe に bash -lc でスクリプトを渡す（-l で環境を確実に読む）。
static Process RunWsl(string scriptCmd)
{
    var psi = new ProcessStartInfo
    {
        FileName  = "wsl.exe",
        // 既定ディストロを使用。複数ある場合は "-d Ubuntu " を先頭に付ける。
        Arguments = $"-e bash -lc \"{scriptCmd}\"",
        UseShellExecute        = false,
        CreateNoWindow         = true,
        RedirectStandardOutput = true,
        RedirectStandardError  = true,
    };
    return Process.Start(psi);
}

public void StartRos2(bool useMoveit = true)
    => RunWsl($"/home/kyotoss/ros2_ws/kmx_start.sh {(useMoveit ? "true" : "false")}");
public void StopRos2()    => RunWsl("/home/kyotoss/ros2_ws/kmx_stop.sh");
public void RestartRos2() => RunWsl("/home/kyotoss/ros2_ws/kmx_restart.sh");

// 状態取得（stdout の1行 = stopped/starting/running_full）
public string Ros2Status()
{
    var p = RunWsl("/home/kyotoss/ros2_ws/kmx_status.sh");
    string s = p.StandardOutput.ReadToEnd().Trim();
    p.WaitForExit();
    return s;
}
```
- `wsl.exe` 実行はブロッキングにしない（`kmx_start.sh` は即 return するが、UI スレッドを止めないよう別スレッド/async 推奨）。

### 2-2. 起動シーケンス（推奨フロー）
1. `StartRos2()` を呼ぶ（即 return）。
2. **`Ros2Status()` を 1〜2秒間隔でポーリング**し、`running_full` になるまで待つ（**通常 ~15-20s**、実測 ~数秒〜）。
3. `running_full` を確認してから **ROS-TCP 接続を確立**（`RosConnector`/`ComRos2` の接続開始）。`starting`/`stopped` の間は接続やプラン要求を送らない。

### 2-3. 停止 / 再起動時の注意
- **停止・再起動では endpoint も落ちる**＝ **Unity⇄ROS の TCP 接続が切れる**。Unity 側は：
  - `StopRos2()`/`RestartRos2()` を呼ぶ前に、ROS-TCP 接続を明示的に切断（例外抑制）。
  - `RestartRos2()` 後は 2-2 と同様に `running_full` を待ってから再接続。
- 再接続後は **planning scene が空**になっている（move_group 再起動）。障害物/ヘッドを再送すること
  （[[OBSTACLES_ROS2_SPEC]] / [[HEAD_TOOL_ROS2_SPEC]]。※ 未送信＝ROS2 は前回シーンを保持しないので明示送信が必要）。

## 3. UI 例
- ボタン3つ：**起動 / 停止 / 再起動**。加えて状態ランプ（`Ros2Status()` を定期ポーリングして stopped=灰 / starting=黄 / running_full=緑）。
- 起動/再起動ボタン押下 → ステータスが `running_full` になったら自動で ROS-TCP 再接続＋（必要なら）obstacles/head 再送。

## 4. 動作確認（ROS2 側は済・Unity 側の受け入れ）
- Unity の起動ボタン → 数十秒後 `kmx_status.sh` が `running_full` → RViz/move_group 起動、`/kmx/*` トピックが見える。
- 停止ボタン → `stopped`。再起動ボタン → 一度切れて再び `running_full`。
- ログは WSL 側 `~/ros2_ws/kmx_bringup.log`（起動失敗時の調査用）。

## 5. 備考（ROS2 側・触らない）
- スクリプトは `~/ros2_ws/kmx_{start,stop,restart,status}.sh`（正本は `kmx_ros2/` にも複製・[[docs-mirror-not-synced]]）。
- `kmx_start.sh` は `setsid` で端末から切り離して起動するので、`wsl.exe` が抜けても bringup は生存。PID は `~/ros2_ws/.kmx_bringup.pid`。
- WSL2 自体が停止していると `wsl.exe` 実行で自動起動する（初回は数秒余計にかかる）。**WSL の停止（`wsl --shutdown`）まではこの仕組みでは制御しない**（必要なら別途 Windows 側で）。
