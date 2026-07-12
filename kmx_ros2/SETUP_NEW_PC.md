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
**Ubuntu 22.04 + ROS2 Humble 固定**（24.04 は Jazzy になり移行作業が要る）。新PCでは土台の導入から。以下は**そのまま貼れるコマンド**。
> `<REPO>` は新PCでの本リポジトリのパス。WSL2 例：`/mnt/c/Users/<user>/source/repos/Kinetic Machine eXplorer`（スペースを含むので必ずダブルクォート）。

### 2-A. 土台（初回だけ）

**1) （WSL2の場合）Ubuntu 22.04 を導入**（Windows の PowerShell/cmd 側）
```powershell
wsl --install -d Ubuntu-22.04
```
以降は **Ubuntu(WSL2) のターミナル**で実行。

**2) ROS2 Humble**
```bash
# ロケール(UTF-8)
sudo apt update && sudo apt install -y locales
sudo locale-gen en_US en_US.UTF-8
sudo update-locale LC_ALL=en_US.UTF-8 LANG=en_US.UTF-8; export LANG=en_US.UTF-8
# リポジトリ登録
sudo apt install -y software-properties-common curl
sudo add-apt-repository -y universe
sudo curl -sSL https://raw.githubusercontent.com/ros/rosdistro/master/ros.key \
  -o /usr/share/keyrings/ros-archive-keyring.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/ros-archive-keyring.gpg] http://packages.ros.org/ros2/ubuntu $(. /etc/os-release && echo $UBUNTU_CODENAME) main" \
  | sudo tee /etc/apt/sources.list.d/ros2.list > /dev/null
# インストール
sudo apt update && sudo apt upgrade -y
sudo apt install -y ros-humble-desktop ros-dev-tools
sudo rosdep init && rosdep update
# 確認
printenv ROS_DISTRO 2>/dev/null; source /opt/ros/humble/setup.bash; printenv ROS_DISTRO   # humble
```

**3) MoveIt ＋ register バックエンドの依存（Pinocchio / coal / numpy / yaml）**
```bash
sudo apt install -y ros-humble-moveit
sudo apt install -y ros-humble-pinocchio python3-numpy python3-yaml
# coal（衝突オラクル hpp-fcl の後継）：Pinocchio3 に同梱のことが多い。無ければ pip:
python3 -c "import coal" 2>/dev/null && echo "coal OK" || pip install coal
# 依存確認（register/ が import する）
python3 -c "import pinocchio, coal, numpy, yaml; print('deps OK')"
```
> ※ `coal` は**旧PCと同じ導入方法**に合わせるのが確実（`pip freeze | grep -iE 'coal|pin'` で確認）。

**4) ROS-TCP-Endpoint（Unity⇄ROS2 の橋・ブランチ `main-ros2`）**
```bash
mkdir -p ~/colcon_ws/src
git clone -b main-ros2 https://github.com/Unity-Technologies/ROS-TCP-Endpoint.git \
  ~/colcon_ws/src/ROS-TCP-Endpoint
cd ~/colcon_ws && colcon build && source install/setup.bash
```
> ★`v0.7.0` タグは ROS1。必ず `main-ros2`（不一致は握手が JSONDecodeError で切断ループ）。

**5) FANUC moveit_config（CRX-30iA）**
本リポジトリには**含まれない**（WSL側で保持）。**旧PCからコピーが最短**：
```bash
mkdir -p ~/ros2_ws/src
cp -r <旧PC>/ros2_ws/src/fanuc_moveit_config ~/ros2_ws/src/
```
> 無ければ MoveIt Setup Assistant で CRX-30iA の config を作成。`config/joint_limits.yaml`（v/a/j 上限）が**最適化・復帰速度の基準**になる（`RETURN_SPEED_UNITY_SPEC.md` / 段階1.5）。

### 2-B. KMX パッケージ（このリポジトリから反映）

**6-7) リポジトリを clone → `kmx_msgs` / `kmx_planner` を反映**
```bash
# 新PCに本リポジトリを clone 済みとする（正本は kmx_ros2/）
mkdir -p ~/ros2_ws/src
bash "<REPO>/kmx_ros2/sync.sh"        # 正本を rsync（kmx_msgs, kmx_planner → ~/ros2_ws/src）
# ↑が使えなければ手動コピー:
# cp -r "<REPO>/kmx_ros2/kmx_msgs" "<REPO>/kmx_ros2/kmx_planner" ~/ros2_ws/src/
```

**8) 依存解決 → ビルド**
```bash
cd ~/ros2_ws
rosdep install --from-paths src --ignore-src -r -y   # 解決できる依存を自動導入
colcon build --symlink-install && source install/setup.bash
```

**9) 起動スクリプト**（旧PCの `~/ros2_ws/` から持ってくる）
```bash
cp <旧PC>/ros2_ws/kmx_start.sh <旧PC>/ros2_ws/kmx_stop.sh \
   <旧PC>/ros2_ws/kmx_restart.sh <旧PC>/ros2_ws/kmx_status.sh ~/ros2_ws/
chmod +x ~/ros2_ws/kmx_*.sh
```
> `kmx_bringup.launch.py` は `kmx_planner` に同梱（sync 済）。

**10) `~/.bashrc` に source（新端末で自動）**
```bash
cat >> ~/.bashrc <<'EOS'
source /opt/ros/humble/setup.bash
source ~/colcon_ws/install/setup.bash   # ros_tcp_endpoint
source ~/ros2_ws/install/setup.bash     # kmx_msgs / kmx_planner
EOS
source ~/.bashrc
```

> ⚠ **`/mnt/c`（Windows側）で `colcon build` しない**（遅い・不安定）。必ずネイティブの `~/ros2_ws`。symlink 衝突は `rm -rf build install log` 後に再ビルド。
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
- **【社内プロキシ / SSL インスペクション】** `curl: (60) SSL certificate problem: unable to get local issuer certificate` → ROS リポジトリが `NO_PUBKEY`（未署名）→ `ros-humble-desktop` が **Unable to locate package**。
  - 原因：会社プロキシが HTTPS を**会社の証明書で再署名**しており、`curl`/`git`/`pip` がその証明書を検証できない（apt 本体は HTTP なので通る）。
  - **恒久対策＝会社ルートCAを WSL に入れる**（この後の `git clone`/`pip` も HTTPS なので必須）：
    ```bash
    # 会社ルートCA(.crt/PEM)を IT から入手 or Windows の certmgr.msc → 信頼されたルート → 会社CA → Base-64(.cer) でエクスポート
    sudo cp /mnt/c/Users/<user>/Downloads/corp-root-ca.crt /usr/local/share/ca-certificates/corp-root-ca.crt
    sudo update-ca-certificates
    git config --global http.sslCAInfo /etc/ssl/certs/ca-certificates.crt
    export PIP_CERT=/etc/ssl/certs/ca-certificates.crt   # 必要なら ~/.bashrc へ
    ```
  - **暫定（キーだけ）**：Windows のブラウザで `https://raw.githubusercontent.com/ros/rosdistro/master/ros.key` を保存 →
    `sudo cp /mnt/c/Users/<user>/Downloads/ros.key /usr/share/keyrings/ros-archive-keyring.gpg` → `sudo apt update`。
    （急ぎは `sudo curl -sSLk … -o …` の `-k`=検証スキップも可・社内前提の自己責任）
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
- `REGISTER_OPTIMIZE_ROS2_SPEC.md` … 登録軌道の多目的最適化（register バックエンド）
- `RETURN_SPEED_UNITY_SPEC.md` … 復帰(通常計画)の速度倍率 speed_scale
- `PLAN_PROGRESS_UNITY_SPEC.md` … 登録最適化の進捗表示（opt phase=search/stomp）
