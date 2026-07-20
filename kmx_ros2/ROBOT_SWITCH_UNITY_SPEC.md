# ロボット種別 切替 — Unity(KMX)側 仕様書

ROS/FANUC 側担当（a-tanaka）→ **Unity(KMX) 担当** への仕様。
KMX から **ロボット種別（robot_model）を指定して ROS2 側のロボットを切り替える**機能（方式A＝再起動切替）。
**ROS/FANUC 側は実装・実機同等(ROBOGUIDE/WSL)で動作確認済み（2026-07-19）**。Unity 側は「種別を渡して起動／切替」と「切替後の再接続・シーン再送」を担当。

---

## 0. 全体像
```
KMX(Unity) がロボット種別を選択
  → wsl.exe で kmx_start.sh <use_moveit> <rviz> <robot_model> を呼ぶ
    → 同一モデルなら何もしない / 別モデルなら bringup を stop→そのモデルで再起動
  → Unity は kmx_status.sh を running_full までポーリング
  → ROS-TCP 再接続 → (再起動時は空になる) planning scene に obstacles/head を再送
```
- MoveIt(move_group) は**起動時にロボットモデルを読む**ため、切替＝**bringup 再起動**（方式A）。
- **関節名は全ロボット共通 J1..J6**なので、`/kmx/command`・`/kmx/state`・`/kmx/trajectory`・`/kmx/plan_request` 等の**トピック仕様は不変**。変わるのは「ロボットの形状・可動範囲」と「起動時に渡す robot_model」だけ。

---

## 1. インタフェース（Unity → ROS）

### 起動/切替コマンド
```
kmx_start.sh [use_moveit=true] [rviz=0] [robot_model=crx30ia] [use_mock=true] [robot_ip=127.0.0.1] [dcs_host=auto]
```
| 位置 | 引数 | 意味 | 既定 |
|---|---|---|---|
| $1 | use_moveit | MoveIt 込みか | true |
| $2 | rviz | RViz 0/1 | 0 |
| $3 | robot_model | 機種（§2 の値） | crx30ia |
| **$4** | **use_mock** | true=模擬HW / false=実機接続(Stream Motion)。**CSV運用では未使用**＝常に true でOK | true |
| **$5** | **robot_ip** | use_mock=false 時の Stream Motion 接続先IP。**CSV運用では未使用** | 127.0.0.1 |
| **$6** | **dcs_host** | **★DCS Karel ソケットの接続先**。auto=ROBOGUIDE(同一PC・WSLゲートウェイ)/**実機=コントローラIP**。`127.0.0.1`/`localhost` は auto に自動読替 | auto |

wsl.exe 例：
```
# ROBOGUIDE（同一PC・既定）：$4-$6 省略可
wsl.exe -e bash -lc "/home/kyotoss/ros2_ws/kmx_start.sh true 0 m20_25_18d"
# 実機コントローラ（CSV運用）：dcs_host に実機IP。use_mock は true のままでOK
wsl.exe -e bash -lc "/home/kyotoss/ros2_ws/kmx_start.sh true 0 m20_25_18d true 192.168.1.20 192.168.1.20"
```
- **挙動（★ここが今回の肝）**：`robot_model`／`use_mock`／`robot_ip`／`dcs_host` の**4条件で同一判定**：
  | 状態 | 動作 |
  |---|---|
  | 未起動 | その条件で起動 |
  | 起動中＋**同一(model,use_mock,robot_ip,dcs_host)** | **何もしない（再起動しない）** |
  | 起動中＋**いずれか変化**（機種 or 模擬↔実機 or IP or DCS接続先） | stop → 新条件で再起動（切替） |
- **後方互換**：`$4-$6` 省略＝模擬・auto＝従来動作。`$4` 不正値は `true`(模擬)へ、`dcs_host=127.0.0.1/localhost` は `auto` へフォールバック。
- **★実機コントローラの接続先＝`dcs_host`（$6）**：CSV運用で実機に繋ぐ鍵はここ（Karel DCS ソケット）。Unity は **RobotInfo.json の `robotIp`（機体ごと）を `dcs_host`($6) に渡す**とよい（ROBOGUIDE=127.0.0.1→auto、実機=コントローラIP）。CSV の FTP 転送先も同じコントローラIP。`use_mock`/`robot_ip`(Stream Motion) は CSV運用では未使用（§1 末尾の注記参照）。
- **記録ファイル**（Unity 参照可）：`~/ros2_ws/.kmx_robot_model` / `.kmx_use_mock` / `.kmx_robot_ip` / `.kmx_dcs_host`。

> **★CSV 運用では `use_mock=true`（模擬）固定でOK・Stream Motion は不使用（重要）**
> 本システムの実行経路は **CSV 再生（Karel/TP が PR[] を再生）** であり、**ROS の Stream Motion / 実機リアルタイム駆動は使いません**。
> 計画の始点も **Unity の `PlanRequest.start`** から取るため、ROS は実機のライブ状態を必要としません。
> したがって **Unity は `use_mock=false` を送る必要はありません（常に `true`＝模擬）**。`use_mock`/`robot_ip`($4/$5) は
> **将来の実機 ROS 直結や telemetry のための保険**として引数だけ残してあります（現運用では未使用）。
> ※将来デジタルツインを実機の実姿勢へリアルタイム同期したい場合は、**Stream Motion ではなく Karel ソケットの
> 状態リーダ（DCS リーダと同流儀）** で行うのが本アーキテクチャに一貫します。

### 明示再起動（同一でも必ず再起動したい場合）
```
kmx_restart.sh [use_moveit] [rviz] [robot_model=crx30ia]
```

### 状態問い合わせ（Unity ポーリング用）
- `kmx_status.sh` → `stopped` / `starting` / `running_full`（従来どおり・仕様不変）
- **稼働中のモデル** = ファイル **`~/ros2_ws/.kmx_robot_model`** を読む（1行・例 `m20_25_18d`）

### ★★ robot_id と robot_model の関係（最重要・誤解注意）
**現状の設計（方式A＝起動時切替）では、どのロボットで計画するかは「bringup の robot_model」だけで決まります。`PlanRequest.robot_id` は計画のルーティングに使われません（ROS2 planner は無視）。**

- **`robot_map` による robot_id → 機種ルーティングは未実装**です（`MULTI_ROBOT_ROS2_SPEC.md` は将来設計であって現状は動きません）。
- したがって **「robot_map に m20 が無いとルーティング失敗」は現状には当てはまりません**。代わりに **「bringup を robot_model=m20_25_18d で起動していれば m20 で、crx30ia で起動していれば crx で計画」** されます。
- **`robot_id` は送っても構いません（メタデータ）が、機種選択には効きません。** 機種選択は必ず **`robot_model`（kmx_start.sh 第3引数）** で行ってください。`Ros2Info.json` の robotId 明示指定も Unity 側の管理としては有効ですが、現状 ROS2 の計画振り分けには影響しません（将来 robot_map 実装時に有効化）。

#### ★サイレント不一致に注意（Unity 側の責務）
`robot_id` を m20 にして plan_request を送っても、**bringup が crx のままなら crx で計画されてしまい、エラーになりません**。防止するため Unity 側で必ず：
1. 目的機種の robot_model で **bringup を切替**（`kmx_start.sh true 0 m20_25_18d`。同一なら再起動されない）
2. `~/ros2_ws/.kmx_robot_model` を読み、**目的機種になっているか確認してから** plan_request を送る
3. 切替（別機種）時は bringup 再起動＝endpoint 断 → **Unity 再接続＋scene 再送**（本仕様 §3・§4）

> 複数ロボットを **同時に** 扱い robot_id で振り分けたい場合は方式が異なります（`MULTI_ROBOT_ROS2_SPEC.md` の robot_map 実装が必要・未着手）。必要になれば ROS/FANUC 側へ依頼を。

---

## 2. ROS 側が対応している robot_model（★これ以外は渡さない）
`kmx_start.sh` の第3引数 `robot_model` に渡せる値。**下表の「使用可」以外を渡しても ROS 側で `crx30ia` にフォールバック**（保険）されるので、必ず使用可の値を渡してください。

| robot_model | ロボット | 状態 |
|---|---|---|
| **`crx30ia`** | CRX-30iA（協働・25kg） | **使用可・検証済（既定）** |
| **`m20_25_18d`** | **M-20iD/25**（産業用・25kg） | **使用可・検証済（2026-07-19 追加）** |
| `crx3ia` | CRX-3iA | 使用可（SRDF有・KMX未検証） |
| `crx5ia` | CRX-5iA | 使用可（SRDF有・KMX未検証） |
| `crx10ia` | CRX-10iA | 使用可（SRDF有・KMX未検証） |
| `crx10ia_l` | CRX-10iA/L | 使用可（SRDF有・KMX未検証） |
| `crx20ia_l` | CRX-20iA/L | 使用可（SRDF有・KMX未検証） |
| `m20_12_23d` | M-20iD/12 | **未対応**（URDFはあるが HW-URDF/SRDF/launch選択肢 未追加） |
| `m20_35_18d` | M-20iD/35 | **未対応**（同上） |

- **使用可＝そのまま `robot_model` に渡せる**（起動時に move_group がその機種で立ち上がる）。「検証済」は KMX 経路で実起動確認済み、「KMX未検証」は fanuc_driver 由来で SRDF は揃っているが KMX での実起動は都度確認。
- **未対応**の機種を使いたい時は ROS 側で追加が必要（M-20iD/25 と同手順：HW-URDF＋SRDF＋launch選択肢）。依頼してください。
- 最新の使用可一覧は ROS 側 `fanuc_moveit.launch.py` の `robot_model` choices と一致。

---

## 3. Unity 実装フロー（切替時）

1. **UI/設定でロボット種別を選択** → robot_model 文字列を決定（例 `m20_25_18d`）。
2. 現在のモデルを確認（任意）：`~/ros2_ws/.kmx_robot_model` を読む。
   - 同じなら切替不要（ROS 側でも同一なら再起動しないが、Unity 側で無駄呼び出しを省ける）。
3. **切替前に ROS-TCP を切断**（再起動で endpoint が落ちるため）。
4. `wsl.exe … kmx_start.sh true 0 <robot_model>` を**別スレッドで**呼ぶ（UI をブロックしない）。
5. `kmx_status.sh` を 1–2s 間隔でポーリングし **`running_full`** を待つ。
6. **ROS-TCP 再接続**。
7. **planning scene を再送**：再起動後は scene が空になるので、`/kmx/obstacles`・`/kmx/attached`（床/障害物/ヘッド）を再送（[[LAUNCH_CONTROL_UNITY_SPEC.md]] と同じ規約）。
8. **Unity のデジタルツイン表示も選択機種に切替**（M-20iD/25 の 3D モデル表示等）。これは Unity 側の担当。

---

## 4. 注意点
- **切替＝bringup 再起動**：endpoint(ROS-TCP)・move_group が落ちる → Unity は切断→ running_full 後に再接続。**scene 再送必須**。同一モデルなら再起動されないので再接続・再送も不要。
- **トピック/メッセージ仕様は不変**（J1..J6 共通）。ロボットが変わっても `/kmx/*` の使い方は同じ。
- **可動範囲・速度はロボットで異なる**：M-20iD/25 の joint_limits は現状 CRX 値で暫定（保守的）。実値差替は ROS 側で対応予定。到達可否（可動範囲）は各ロボットの URDF に従う。
- **実機接続**：CRX と M-20iD は実機ドライバ/通信が異なる。デジタルツイン（Unity＋計画）は共通で動くが、実機直結は別途。CSV再生（方式A）は関節ベースでロボット非依存。
- **DCS**：DCS常駐(KMX_DCS_SRV)・safety_zones は bringup と独立。切替（再起動）中は kmx_dcs_reader も一旦落ちて再接続する。

---

## 5. ★Unity 側 実装要望（TODO チェックリスト）
**基本方針＝「Unity で機種を決めて、その robot_model で ROS(bringup) を(再)起動する」**（＝方式A・現状の正解）。
robot_id によるルーティングは使わない（§1「robot_id と robot_model の関係」参照）。

- [ ] **ロボット種別の選択**（UI/設定）→ `robot_model` を決定（**§2 の使用可の値**：`crx30ia` / `m20_25_18d` など）。
- [ ] **切替は robot_model で ROS 再起動**：`wsl.exe -e bash -lc ".../kmx_start.sh true 0 <robot_model> <use_mock> <robot_ip> <dcs_host>"` を**別スレッド**で呼ぶ。
- [ ] **★実機コントローラの接続先＝`dcs_host`($6) に RobotInfo.json の `robotIp`（機体ごと）を渡す**：
      ROBOGUIDE は `127.0.0.1`（→ROS側で auto 読替）／実機は**コントローラの実IP**（例 192.168.1.20）。これで DCS Karel が正しい機体に繋がる。
      `use_mock`($4)/`robot_ip`($5) は **CSV運用では未使用**＝`use_mock=true` 固定でOK（省略可）。
      例（実機）：`kmx_start.sh true 0 m20_25_18d true 192.168.1.20 192.168.1.20`
- [ ] **CSV の FTP 転送先も同じコントローラIP**（`UD1:\KMX\P<品番>_<パス>.CSV`）。dcs_host と同じ機体IPを使う。
- [ ] **切替要否の判定**：`~/ros2_ws/.kmx_robot_model`（＋任意で `.kmx_dcs_host` 等）を読み、**同一条件なら呼ばない/再起動されない**（無駄な再接続回避）／**別条件なら切替**。
- [ ] **別機種切替のシーケンス**：ROS-TCP 切断 → `kmx_start.sh` 呼出 → `kmx_status.sh`=`running_full` 待ち → ROS-TCP 再接続 → **planning scene（障害物/ヘッド/床）再送**。
- [ ] **★計画前チェック（サイレント不一致防止）**：`plan_request` 送信前に `~/ros2_ws/.kmx_robot_model` が**目的機種になっているか確認**（違う機種のまま投げても他機種で計画されエラーにならない）。
- [ ] **`robot_id` は計画選択に使わない**：送ってよいがルーティング非対応。機種選択は必ず `robot_model` で。
- [ ] **robot_model は §2 の使用可の文字列のみ渡す**（不正値は ROS 側で `crx30ia` にフォールバック＝保険）。
- [ ] **Unity のデジタルツイン 3D 表示も選択機種へ切替**。
- [ ] **RViz 不要**（可視化は Unity。RViz は ROS デバッグ用）。
- [ ] （将来）複数ロボ同時＋robot_id 振り分けが要るなら ROS へ `MULTI_ROBOT_ROS2_SPEC.md`(robot_map) 実装依頼。**今は設計しない**。

## 6. 分担
| 側 | 担当 |
|---|---|
| **Unity(KMX)** | ロボット種別 UI/選択／wsl.exe で kmx_start.sh に **robot_model＋dcs_host(=機体のrobotIp)** 送信／RobotInfo.json robotIp 管理／`.kmx_robot_model` 確認→計画／running_full 待ち→再接続→scene再送／CSV を機体IPへ FTP／機種の 3D 表示切替 |
| **ROS/FANUC** | kmx_start.sh の robot_model／use_mock／robot_ip／**dcs_host** 対応・妥当性チェック（127.0.0.1→auto 等・実装済）／対応機種の MoveIt 設定（crx30ia・m20_25_18d 済・§2）／bringup 伝搬（済）／（将来）robot_map ルーティング |

---

## 7. 参考（ROS側の実体）
- 切替スクリプト：`~/ros2_ws/kmx_start.sh`（正本 `kmx_ros2/kmx_start.sh`）第3引数 robot_model・`.kmx_robot_model` 記録。
- bringup：`kmx_bringup.launch.py`（robot_model → fanuc_moveit.launch.py へ）。
- M-20iD/25 追加：`fanuc_hardware_interface/robot/m20_25_18d.urdf.xacro`＋`fanuc_moveit_config/srdf/m20_25_18d.srdf`＋launch選択肢。
- 既存の起動制御仕様：`LAUNCH_CONTROL_UNITY_SPEC.md`（本仕様はその robot_model 拡張版）。

以上。ご不明点は ROS/FANUC 側（a-tanaka）まで。