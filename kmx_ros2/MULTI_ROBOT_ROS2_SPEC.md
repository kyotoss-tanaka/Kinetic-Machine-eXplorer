# 【ROS2側 実装要望】複数ロボット対応（別機種混在・1台ずつ計画）

**方向：Unity側 → ROS2側**。プロジェクト内の複数ロボット（**別機種が混在**しうる）を、
**1台ずつ選んで**経路計画できるようにする。計画中の1台以外は**障害物として回避**する。
既存の単一ロボット構成（[[HANDOFF]] §4）を土台に、差分だけを定義する。

---

## 0. 決定事項（ユーザー確認済・2026-07-07）
- **別機種が混在**する（同一機種だけとは限らない）。
- **計画は常に1台ずつ**（同時計画はしない）。UIでロボットを選んで計画する。
- 計画対象以外のロボットは**障害物として避ける**（Unityが現在姿勢のAABBで送る）。
- 各ロボの現在関節値は**台数分のタグ（unit/機番）で個別取得可**（Unity側で用意）。

## 1. 契約変更（ワイヤ上の差分）
### 1.1 `/kmx/plan_request` に `robot_id` を追加
```
PlanRequest{ names[], start[], goal[], time_budget, good_ratio, string robot_id }   # robot_id を新規追加
```
- `robot_id`：どのロボット（＝どの機種/planning group）を計画するかの識別子（例 `"crx30ia_1"`, `"robotB_2"`）。
- **後方互換**：`robot_id` 空文字なら従来どおり既定ロボ(=既定group)で計画。
- ⚠ フィールド追加につき **Unity は `Generate ROS Messages` 再生成**が必要。
- `names/start/goal` は**そのロボのローカル関節**（機種により本数/名前が違ってよい。例 6軸=J1..J6）。

### 1.2 ルーティング（robot_id → 機種モデル）
ROS2 は `robot_id` を受けて、対応する**機種の MoveIt 構成**へ振り分ける。実装方式は任せます（推奨2案）：
- **方式A：機種ごとに move_group を別プロセスで起動**し、planner が robot_id→対象 move_group を選ぶ。
  モジュール的だがRAM/プロセス増。別機種が数種なら現実的。
- **方式B：統合SRDFに機種別 planning group** を定義（1つの move_group で複数group）。
  実行は軽いが統合URDF/SRDFの構築が要る。
- どちらでも Unity 側の契約は同じ（robot_id を送るだけ）。`robot_map` パラメータ等で robot_id→group/model を設定する想定。

### 1.3 障害物 / ヘッド（既存トピック流用・frame をロボ基準に）
- `/kmx/obstacles`：**計画対象ロボの base_link 相対**で送る。`frame_id` に**そのロボの base_link 名**を入れる
  （機種で base_link 名が違う場合に備え、Unityは選択ロボの基準名を入れる）。
- **他ロボットも `/kmx/obstacles` に含める**（現在姿勢の各リンクAABB・対象ロボ基準に変換済）。
  ＝計画対象以外は「動かない障害物」として扱う。ROS2は従来どおり全置換で planning scene へ。
- `/kmx/attached`：**計画対象ロボのヘッド**を、そのロボの attach リンク（例 flange）に付ける。
  `frame_id`＝対象ロボの attach リンク名。ヘッド補正 `head_calibration_rpy` は機種別に持てるようにする。

### 1.4 結果トピック（1台ずつ運用のため最小変更）
- `/kmx/trajectory`：従来どおり `JointTrajectory`（joint_names付き）。**1台ずつ**なので Unity は要求中のロボに紐づける。
  可能なら安全のため、対象 robot_id を何らかの形で載せられると堅い（任意）。
- `/kmx/plan_status`：従来どおり（1台ずつ）。

## 2. 座標・キャリブレーション（機種別に持てるように）
- world障害物のUnity→base補正（現状 `baseCalibrationEuler`=(0,-90,0)）は**機種ごとに異なりうる**。
  Unity側は「対象ロボの基準リンク相対・ROS(FLU)・m」で送るので、ROS2の補正は robot_id/機種別パラメータで。
- `head_calibration_rpy` も**機種別**（現状 CRX-30iA=[0,90,90]）。robot_map 内に機種別で持つ想定。

## 3. パラメータ（例・robot_map）
```yaml
robot_map:
  crx30ia_1: { model: crx30ia, group: manipulator, base_link: base_link, attach_link: flange,
               head_calibration_rpy: [0,90,90] }
  robotB_2:  { model: robotB,  group: arm,         base_link: rb_base,    attach_link: rb_tool,
               head_calibration_rpy: [..] }
```
- robot_id が map に無ければエラー（`plan_status: failed:unknown_robot`）。

## 4. 段階リリース案
1. **単一→複数の土台**：`robot_id` 追加＋ルーティング（既存CRX-30iAを `robot_id="crx30ia_1"` として1台で疎通確認）。
2. **2機種目**を robot_map に追加（方式A/Bどちらか）。Unityから選択→計画→他方を障害物回避で往復確認。
3. 機種別キャリブレーション（base/head）を robot_map で調整。

## 5. Unity側（先方＝このリポの担当。ROS2側は参考）
- ロボット登録（シーンの各ロボ＝Kinematics6D＋base＋head＋関節タグ＋robot_id）とセレクタUI。
- 計画/ゴースト/ゴール/障害物/ヘッドを**選択ロボ**へ切替。
- **他ロボを現在姿勢の障害物として `/kmx/obstacles` に合成**（選択ロボ基準）。
- `Ros2Info.json` を台数分（unit/機番/robot_id/関節タグ）に拡張。
- `plan_request` に robot_id を付与（メッセージ再生成）。

## 6. 未確定（要すり合わせ）
- robot_id の命名規約と、Unityのロボ(unit/機番) ↔ robot_id ↔ ROS2 group/model の対応表を誰が持つか
  （案：`Ros2Info.json` に robots 配列で一元管理し、robot_id を ROS2 robot_map と一致させる）。
- 別機種は具体的に何機種・URDF/MoveIt config は用意済みか。
