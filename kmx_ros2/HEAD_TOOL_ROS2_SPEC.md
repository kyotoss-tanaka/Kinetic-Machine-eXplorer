# 【ROS2側 実装要望】ヘッド(ツール/グリッパ)を MoveIt に反映

CRX-30iA の **6軸目フランジ(`J6FLANGE` / URDF の flange 相当リンク)の子オブジェクト＝ヘッド(ツール)** を
MoveIt に「ロボットが持っているツール」として認識させたい。目的は **経路計画がツール形状も含めて障害物を回避**
すること（現状 URDF にツールが無いと、ツールが障害物を貫通する計画が出得る）。

Unity 側は `ComRos2Obstacles` の **`Measure Head`**（右クリックメニュー）で、ツールの
**AABB 寸法(m)** と **フランジからの取付オフセット(m)** をログ出力できる（下記の数値源）。

> **採用方式（2026-07-05 決定・更新）= 方式B（動的 AttachedCollisionObject）。**
> Unity 側は実装済み（`ComRos2Obstacles.SendHead` / ContextMenu「Send Head」）。**新規メッセージは作らず、
> 既存 `kmx_msgs/Obstacles` を別トピック `/kmx/attached` に流用**し、`frame_id` に attach 先リンク名を載せる。
> ROS2 側は §0（リンク名確認）と §方式B（B-1〜B-3）を実施。方式A は固定ツール向けの参考として残置。

---

## 0. まず確認：フランジ(取付先)リンク名
`base_link` と同様、起動中の move_group から SRDF を取って確認する:
```bash
ros2 param get /move_group robot_description_semantic > /tmp/x.srdf
grep -iE 'link name|group name|end_effector|tip' /tmp/x.srdf
```
CRX-30iA は末端が `flange` / `tool0` / `link_6` 等のいずれか。以降これを **`<FLANGE_LINK>`** と表記する。

---

## 参考：方式A（固定ツール）：URDF/xacro にツールを固定リンクで追加 ※今回は不採用

ツールがフランジに常時固定なら URDF に足す手もある（実行時の通信・フレームずれが無い）。今回は付け替え運用の
可能性を考え **方式B を採用**。以下は将来固定運用に切替える場合の参考。`Measure Head`(ContextMenu) の
box size / origin xyz をそのまま使える。

### A-1. リンク＋固定ジョイントを追加
`fanuc_description`（または moveit_config の xacro）で、`<FLANGE_LINK>` の子に固定リンクを足す:
```xml
<link name="kmx_tool">
  <collision>
    <origin xyz="X Y Z" rpy="R P Yw"/>
    <geometry>
      <!-- 単純化: ボックス。複雑なら複数 <collision> か mesh -->
      <box size="SX SY SZ"/>
    </geometry>
  </collision>
  <!-- 見た目が要るなら <visual> も同様に -->
</link>
<joint name="kmx_tool_joint" type="fixed">
  <parent link="<FLANGE_LINK>"/>
  <child link="kmx_tool"/>
  <origin xyz="0 0 0" rpy="0 0 0"/>
</joint>
```
- `SX SY SZ` = Unity `Measure Head` の **「URDF box size(m)」**。
- `X Y Z` = Unity `Measure Head` の **「URDF origin xyz(m)…ROS(FLU)」**（フランジ相対）。
- `R P Yw` = まず 0。実機と向きが合わなければ調整（Unity フランジ軸と URDF リンク軸のずれ分。base_link で
  `baseCalibrationEuler=(0,-90,0)` が要ったのと同様、フランジでも 90°系の補正が要る場合がある）。

### A-2. SRDF で隣接リンクとの自己干渉を無効化
ツールはフランジ等と接するので、SRDF に `disable_collisions` を足す（無いと常時自己衝突で計画不能）:
```xml
<disable_collisions link1="kmx_tool" link2="<FLANGE_LINK>" reason="Adjacent"/>
<!-- 必要なら手首側リンク(link_5 等)とも -->
```

### A-3. 検証
- `ros2 launch ... fanuc_moveit.launch.py robot_model:=crx30ia use_mock:=true` で RViz に**ツール形状が表示**される。
- ツールを障害物に突っ込む start/goal で `/kmx/plan_request` → **ツールが障害物を避ける**軌道になれば成立。
- ツール込みで初期姿勢が衝突（`START_STATE_IN_COLLISION`）するなら A-1 の origin/size か A-2 の disable_collisions を見直す。

---

## 方式B（採用）：Unity から動的に AttachedCollisionObject

Unity から実行時にツール形状を送り、**`moveit_msgs/AttachedCollisionObject`** としてフランジに attach する。
障害物(`/kmx/obstacles`)と同じ枠組みだが、**world ではなくリンクに attach** する点が違う。

### B-1. メッセージ（★新規メッセージ不要・既存 Obstacles を流用）
Unity は **既存 `kmx_msgs/Obstacles` を別トピック `/kmx/attached` に publish** する。意味付け:
- `frame_id` = **attach 先の URDF リンク名**（例 `flange` / `tool0` / `link_6`）。※ 障害物では base_link だが、ここは attach リンク。
- `items[]` = `ObstaclePrimitive`（id/type/dimensions/pose）。pose は attach リンク相対。
- Unity 実装済み: `ComRos2Obstacles.SendHead()`（ContextMenu「Send Head」）。ヘッド(`Kinematics6D.HeadObject`)配下の
  Collider を **isTrigger の有無を問わず全て** AABB 化して送る。`attachLinkName` が frame_id になる。
- **kmx_msgs の追加は不要**（Obstacles/ObstaclePrimitive は障害物対応で導入済み）。新トピックだけ。

#### B-1.1 ★ヘッド形状の粒度＝間引き対応（2026-07-06・Unity側だけで切替可）
- ROS2 は受信 item 数が `attached_merge_over`(既定 **12**) を**超えたら union AABB 1箱に自動統合**（安全弁）、
  **12個以下はそのまま個別 attach**（把持開口を残せる）。＝**形状の粒度は Unity が送る箱数だけで決まる**。
- **Unity 側の想定運用**（`ComRos2Obstacles.headAsSingleBox`）:
  - `true` → **1箱**（全体を1個の AABB）。開口不要・最軽量。
  - `false` → **間引き数箱**（例：本体1箱＋爪2箱で**把持開口を残す**・**合計 ≤12 個**）。
    ※ 旧実装の「全 Collider を AABB 化して数百個(実測395)送る」は、そのまま送っても ROS2 安全弁で1箱化されるだけ
    （＝開口は出ない）。開口を活かすには **数個へ間引いて送る**こと。
- ROS2側 検証済（2026-07-06）: 3箱→個別保持／15箱→1箱統合。閾値は `ros2 param set /kmx_planner attached_merge_over N`。
- **touch_links 注意**: 現状 `attached_touch_links` に `J4_link` を含む（旧 union箱が手首後方へ膨らみ J4 と常時接触するため）。
  間引き形状が J4 に膨らまない設計なら `J4_link` は外す方が実 J4 衝突を隠さず正確。**Unity の間引き箱の配置が決まったら ROS2 側で調整**。

### B-2. ノード実装（`kmx_planner` に追記）
- **購読**: `/kmx/attached` (kmx_msgs/Obstacles) ← 型は障害物と同じ、トピックだけ別。
- **変換**: 各 `item` → `moveit_msgs/CollisionObject`（`header.frame_id = msg.frame_id`(=attachリンク)、`SolidPrimitive` +
  `primitive_poses=[item.pose]`, `operation=ADD`）をまとめ、
  `AttachedCollisionObject{ link_name = msg.frame_id, object = <上記CollisionObject>, touch_links = <下記> }`。
- **touch_links**: 自己干渉を許可するリンク。パラメータ `attached_touch_links`(既定 `[<attachリンク>, 手首側リンク…]`)で
  与える。最低限 attach リンク自身は必須（無いと即自己衝突で計画不能）。CRX は flange と link_5/6 あたり。
- **反映**: `PlanningScene{ is_diff=true, robot_state.attached_collision_objects=[...] }` を
  `/apply_planning_scene` サービス（未準備は `/planning_scene` publish）で反映。障害物の `_apply_scene` を流用可。
- **更新規約**: 障害物と同様「受信のたびに全置換」。前回 attached id は REMOVE→今回分 ADD（attached 用に別集合で管理）。
- **注意**: 同一 id を world 障害物(`/kmx/obstacles`)と attached の両方に入れない。ツールは基部半径収集では
  ロボット自身として除外されるので通常は衝突しないが、id 重複は避ける。

### B-3. 座標・単位・検証
- Unity は「**フランジ(参照)相対・ROS(FLU)・メートル**」で送る（障害物と同じ AABB 変換系。向きは持たせず軸整列）。
- フランジの Unity 軸と URDF リンク軸のずれは、障害物の `baseCalibrationEuler` と同様に Unity 側
  `headCalibrationEuler`(既定0) で補正。まず1形状で位置確認 → ずれたら 90°系で調整。
- 検証: `ros2 topic echo /kmx/attached --once` で pose/dims 確認 → RViz でツールがフランジに付いて表示 →
  ツールを障害物に突っ込む start/goal で**ツールごと回避**すれば成立。初期姿勢が自己衝突するなら touch_links を追加。

### B-4. 検証チェックリスト（ROS2側）
1. `/kmx/attached` 購読を追加（型=既存 Obstacles、トピックのみ別）。
2. `frame_id` を attach リンクとして `AttachedCollisionObject` 化、`touch_links` パラメータ対応。
3. `attached_collision_objects` を planning scene diff で反映（apply 優先・publish fallback）。
4. RViz 表示＋回避確認、自己衝突なら touch_links 調整。
