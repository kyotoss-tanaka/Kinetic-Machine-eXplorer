# 新PCへの配布 ＋ ROS2 構築 手順書（SETUP_NEW_PC）

別PCで **KMX（配布ビルド・実行環境）＋ ROS2（頭脳）** を動かすためのチェックリスト。
前提知識・詳細は `ONBOARDING.md`（全体像）／`README.md`（初回A〜E）／`RUN.md`（起動）を併読。

```
[新PC・Windows] KMX配布ビルド(実行)  ⇄  ros_tcp_endpoint  ⇄  [WSL2 or Linux] kmx_planner + move_group(MoveIt)
```

---

## 0. 方式の確認（重要）
| 項目 | 結論 |
|---|---|
| Unity | **配布（Standalone ビルドを実行）**。Editor 不要。 |
| ビルドターゲット | **Windows Standalone x64**（ROS2連携あり）。 |
| **WebGL 配布** | **ROS2連携は不可**（ブラウザは生TCP不可＝`ros_tcp_endpoint` に繋げない。KMX も WebGL は通信全無効）。**表示専用ビューアーとしてのみ**可。ROSを使うなら rosbridge(WebSocket)化が別途必要。 |
| ROS2 実行環境 | **WSL2（Windows内）** or **別Linuxマシン**。どちらも手順ほぼ同じ（差はネットワークだけ・§3）。 |

---

## 1. Unity 配布（Windows・実行環境）
配布は**開発PCで作ったビルド成果物を新PCへコピーするだけ**。

### 1-1. ビルド設定（開発PCで・初回のみ確認）
- Build Target = **Windows / x86_64**
- Scripting Define Symbols に **`KMX_ROS2`**（Standalone）
- **Robotics > ROS Settings**：Protocol=**ROS2** / IP=**接続先**（同一PCのWSL2 なら `127.0.0.1` / 別Linux なら そのIP） / Port=**10000**
- ビルド → `KMX.exe` ＋ `KMX_Data/` が生成される

### 1-2. 配布物（新PCへコピー）
- `KMX.exe` と `KMX_Data/` フォルダ**一式**。
- ⚠ **`StreamingAssets/Datas`（*Info.json 群）は git 管理外だが、ビルドには自動同梱**される（`KMX_Data/StreamingAssets/Datas/`）。**Datas がある開発PCでビルドすれば成果物に入る**ので、そのまま渡せばOK。
- 新PC側に Unity/Editor は不要。ダブルクリックで起動。

> 接続先IPを変えたい場合はビルドし直し（ROS Settings はビルド時に焼き込まれる）。

---

## 2. ROS2 バックエンド（WSL2 or Linux・頭脳）
既存ドキュメントは「環境がある前提」なので、**新PCでは土台の導入から**。Ubuntu 22.04 / ROS2 Humble 前提。

### 2-A. 土台（初回だけ）
1. **（WSL2の場合）** `wsl --install -d Ubuntu-22.04` → Ubuntu 起動
2. **ROS2 Humble** を apt 導入（`ros-humble-desktop`）＋ `ros-dev-tools`
3. **MoveIt**：`sudo apt install ros-humble-moveit`（旧PCが `~/ws_moveit` のソースビルドならそれに合わせる）
4. **ROS-TCP-Endpoint**：`~/colcon_ws/src` に clone → **ブランチ `main-ros2`**（★`v0.7.0` タグは ROS1 なので使わない）→ `cd ~/colcon_ws && colcon build`
5. **FANUC moveit_config（CRX-30iA）**：`fanuc_moveit_config` を `~/ros2_ws/src` へ（旧PCの `~/ros2_ws/src` からコピー or 入手元から）

### 2-B. KMX パッケージ（このリポジトリから反映）
6. 新PCに**このリポジトリを clone**（`kmx_ros2/` が正本）
7. `bash "<repo>/kmx_ros2/sync.sh"` で `~/ros2_ws/src` へ `kmx_msgs` / `kmx_planner` を反映（rsync）
8. `cd ~/ros2_ws && colcon build --symlink-install && source install/setup.bash`
9. 起動スクリプト `kmx_start.sh` / `kmx_stop.sh` / `kmx_restart.sh` / `kmx_status.sh` と `kmx_bringup.launch.py` を旧PCの `~/ros2_ws/` から持ってくる
10. `~/.bashrc` 末尾に source を追加（新端末で即使えるように）:
    ```bash
    source /opt/ros/humble/setup.bash
    source ~/colcon_ws/install/setup.bash   # ros_tcp_endpoint
    source ~/ros2_ws/install/setup.bash     # kmx_msgs / kmx_planner
    ```

> ⚠ **`/mnt/c`（Windows側）で `colcon build` しない**（遅い・不安定）。必ずネイティブの `~/ros2_ws`。
> ⚠ コードの正本は `kmx_ros2/`。WSL の複製を直接編集しても次の sync で消える（`ONBOARDING.md` 第6章）。

---

## 3. ネットワーク（Unity ⇄ ROS2）
endpoint は **`ROS_IP:=0.0.0.0`** で起動（`kmx_bringup` が実施）。Unity 側 ROS Settings の接続先IPを合わせる。

| ROS2 の場所 | Unity(ROS Settings)のIP | 備考 |
|---|---|---|
| **同一PCのWSL2** | `127.0.0.1` | WSL2 の localhost 転送で届く。届かないときは Windows FW で 10000/TCP 許可、or `wsl hostname -I` のIP を使う |
| **別 Linux マシン** | その Linux の LAN IP | 両者同一LAN・FW で 10000/TCP 開放 |

Port は両側とも **10000**。

---

## 4. 起動 & 疎通確認（チェックリスト）
```bash
# ① ROS2（WSL2/Linux）— 推奨1コマンド
ros2 launch kmx_planner kmx_bringup.launch.py        # endpoint + move_group(CRX) + planner
#   軽量（MoveIt/RViz無し・補間）: ... kmx_bringup.launch.py use_moveit:=false
~/ros2_ws/kmx_status.sh                               # ← running_full を確認（計画OKの合図）
```
- [ ] `kmx_status.sh` が **running_full**
- [ ] `ros2 node list` に `/UnityEndpoint` `/kmx_planner`
- [ ] **KMX.exe を起動**（ROS2連携ON）
- [ ] KMX 起動後 Console/ログに `resolve tag='d_robo_a1..a6'` が6本 → 駆動準備OK
- [ ] `ros2 topic echo /kmx/state` に現在角度（度）が流れる
- [ ] KMX で計画/登録 → 軌道（ゴースト）が返る

CLI だけで疎通確認する例は `RUN.md`「確認・デバッグ用コマンド」。

---

## 5. ハマりどころ（`RUN.md` / `ONBOARDING.md` / memory）
- **版固定**：ROS-TCP-Connector `#v0.7.0`（Unity UPM）／ endpoint `main-ros2` ブランチ。不一致は握手が `JSONDecodeError` で切断ループ。
- **`KMX_ROS2` define** が無いビルドは「稼働中でも未接続」。
- **`/mnt/c` でビルドしない**。symlink 衝突は `rm -rf build install log`。
- **`Datas` は git 管理外** → ビルド同梱 or 手動コピー。
- 障害物/ヘッドは**全置換**（空送信で消える／送らねば前回のまま）。
- endpoint→Unity の順で起動が綺麗（逆だと `Not registered` が一時的に出るが無害）。

---

## 6. 参考（このフォルダ内）
- `ONBOARDING.md` … 全体像・用語・起動・トラブル
- `README.md` … 初回セットアップ A〜E（msg/endpoint/planner/検証）
- `RUN.md` … 起動手順・確認コマンド・版固定
- `HANDOFF.md` … 開発引き継ぎ全体像
- `LAUNCH_CONTROL_UNITY_SPEC.md` … Unity ボタンからの起動制御（`wsl.exe` 経由）
