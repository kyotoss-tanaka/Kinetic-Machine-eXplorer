# 【ROS/FANUC 側 実装要望】ロボット切替時の コントローラ接続（use_mock / robot_ip）

**方向：Unity(KMX) 側 → ROS/FANUC 側**（a-tanaka）。
機種切替（方式A＝`robot_model` で bringup 再起動）は実装済み（[[ROBOT_SWITCH_UNITY_SPEC.md]]）。本書は、その切替で **選択機体のコントローラへ接続**するために ROS 側へ依頼する追加事項（`kmx_start.sh` の引数拡張と bringup 反映）をまとめる。

作成: 2026-07-20 / 対象: `kmx_start.sh` ＋ `kmx_bringup.launch.py`（+ hardware interface）

---

## 0. 概要（何を足してほしいか）
`kmx_start.sh` に **`use_mock` と `robot_ip` の2引数を追加**し、bringup の **ros2_control ハードウェアIF**へ反映してほしい。
- `use_mock=true` … 模擬HW（現状の既定・安全）
- `use_mock=false` … **実機/ROBOGUIDE の FANUC コントローラへ接続**（接続先＝`robot_ip`）
- `robot_ip` … 接続先コントローラの IP（**ロボットごとに異なる**。Unity が選択機体の IP を渡す）

> トピック仕様（`/kmx/*`）・`robot_model`・`.kmx_robot_model`・`kmx_status.sh` は**変更なし**。追加は「選択機体のコントローラへ繋ぐための use_mock / robot_ip」だけ。

---

## 1. 現状と課題
- 現 `kmx_start.sh` は **`$1 use_moveit / $2 rviz / $3 robot_model`** の3引数のみ（`use_mock`/`robot_ip` を受け取らない）。
- Unity は「模擬 or 実機」「コントローラIP」を持っているが、**現状 bringup へ渡す口が無い**（旧 Unity は誤って第3引数に mock値を渡して `robot_model` フォールバックしていた＝機種切替も接続切替も効いていなかった）。
- 今回 **コントローラIP は RobotInfo.json（機体ごと）で管理**する方針に確定。切替時に **選択機体の IP** を bringup へ渡したい。

---

## 2. 要求①：`kmx_start.sh` シグネチャ拡張（最重要）
```
kmx_start.sh [use_moveit=true] [rviz=0] [robot_model=crx30ia] [use_mock=true] [robot_ip=]
```
| 位置 | 引数 | 意味 | 既定 |
|---|---|---|---|
| $1 | use_moveit | MoveIt 込みか | true |
| $2 | rviz | RViz 0/1 | 0 |
| $3 | robot_model | 機種（[[ROBOT_SWITCH_UNITY_SPEC.md]] §2 の値） | crx30ia |
| **$4** | **use_mock** | **true=模擬HW / false=実機接続** | **true** |
| **$5** | **robot_ip** | **use_mock=false 時の接続先コントローラIP** | 空 |

- wsl.exe 例（Unity が送る形）:
  ```
  wsl.exe -e bash -lc "/home/kyotoss/ros2_ws/kmx_start.sh true 0 m20_25_18d false 192.168.1.20"
  ```
- **後方互換**：`$4/$5` 省略時は `use_mock=true`（模擬）＝従来動作。`$4` が不正値なら `true`（模擬）にフォールバック（`robot_model` と同様の保険）。
- `kmx_restart.sh` も同じ引数順で拡張してほしい。
- **同一 model かつ同一 (use_mock, robot_ip) なら再起動しない**、が理想（現状の「同一modelなら再起動しない」を接続条件込みに拡張）。接続先が変わる場合は再起動（切替）でよい。

---

## 3. 要求②：bringup への反映
- `kmx_start.sh` → `kmx_bringup.launch.py`（→ `fanuc_moveit.launch.py` / hardware interface）へ **`use_mock` / `robot_ip` を伝搬**。
  - `use_mock=true` → ros2_control を **mock_components**（模擬）で起動（現状相当）。
  - `use_mock=false` → **実機/ROBOGUIDE の FANUC ドライバ**で `robot_ip` のコントローラへ接続（Stream Motion 等、既存の実機接続方式に合わせて）。
- `robot_model`（機種）と `use_mock/robot_ip`（接続）は**直交**：どの機種でも 模擬/実機 を選べること。
- 実機接続の具体（ドライバ・ポート・Stream Motion 設定）は ROS/FANUC 側の既存方式に委ねる。Unity は **use_mock と robot_ip を渡すだけ**。

---

## 4. robot_ip の供給元（Unity 側の管理・参考）
- コントローラIP は **RobotInfo.json の `RobotSetting.robotIp`（機体ごと）** に保持（既定 `127.0.0.1`）。
- **use_mock はグローバル**（Ros2Info.json `launchUseMock`）。当面は全体スイッチ（模擬 or 実機）。
- 切替時、Unity は **選択機体の robot_model と robot_ip、グローバルの use_mock** を `kmx_start.sh` へ渡す。

---

## 5. 変更しない部分（確認）
- `kmx_status.sh`（`stopped`/`starting`/`running_full`）… 不変。
- `~/ros2_ws/.kmx_robot_model`（稼働中モデル1行）… 不変。Unity はこれで機種一致を確認する。
  - （任意）`use_mock`/`robot_ip` も記録ファイルに残してくれると Unity が接続条件も確認できて助かる（必須ではない）。
- トピック/メッセージ（`/kmx/command`・`/kmx/state`・`/kmx/trajectory`・`/kmx/plan_request` 等）… J1..J6 共通で不変。
- `PlanRequest.robot_id` … 計画ルーティングには使わない（現状のまま。robot_map は将来）。

---

## 6. 単機前提・将来（重要）
- 本要求は **一度に1機体（方式A＝再起動切替）** 前提。**同時に複数コントローラへは繋がない**（bringup は1 robot_model・1 robot_ip）。
- 将来 **複数ロボットを同時にライブ**にしたい場合は方式が異なる（`MULTI_ROBOT_ROS2_SPEC.md` の robot_map ＋ 複数 move_group／複数 hardware IF 並行起動が必要・未着手）。必要になれば別途依頼する。

---

## 7. 受け入れ確認（ROS 側完了の目安）
1. `kmx_start.sh true 0 crx30ia false 192.168.1.10` → CRX が **実機モード**で `192.168.1.10` へ接続して `running_full`。
2. `kmx_start.sh true 0 m20_25_18d false 192.168.1.20` → 切替（stop→再起動）で M-20iD/25 が `192.168.1.20` へ接続。
3. `kmx_start.sh true 0 crx30ia`（$4/$5 省略）→ 従来どおり **模擬HW** で起動（後方互換）。
4. `$4` に不正値 → `use_mock=true`（模擬）へフォールバックし bringup は壊れない。

---

## 8. 分担
| 側 | 担当 |
|---|---|
| **ROS/FANUC** | `kmx_start.sh`/`kmx_restart.sh` に `use_mock`($4)/`robot_ip`($5) 追加・妥当性チェック・後方互換／bringup(hardware IF) へ伝搬（mock ⇔ 実機 robot_ip 接続）／（任意）稼働中の use_mock・robot_ip の記録 |
| **Unity(KMX)** | RobotInfo.json `robotIp`（機体ごと）管理／切替時に 選択機体の `robot_model`＋`robot_ip`＋グローバル `use_mock` を `kmx_start.sh` へ送信／`.kmx_robot_model` で機種一致確認→計画／running_full 待ち→再接続→scene 再送／3D 表示 |

以上。ご不明点・実機接続方式の詳細は擦り合わせをお願いします。
