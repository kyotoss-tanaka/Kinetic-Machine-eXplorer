# 引継ぎ：方式B「ヘッド(attached)が world障害物と衝突しない」問題の調査

**作成 2026-07-05。担当交代（別モデル）向け。目的＝方式Bを維持したまま、attached（ヘッド/ツール）が world障害物と衝突・回避するように直す。**

---

## ✅ 解決済み（2026-07-05）

**根本原因**: MoveIt の仕様。`moveit_msgs/RobotState` は `is_diff=false` のとき「完全状態」扱いとなり、
`moveit_core/robot_state/conversions.cpp` の `_robotStateMsgToRobotStateHelper` が
**`clearAttachedBodies()` で attached body を全消去**してから適用する。
- `plan_only`: `planner_node.py` が `start_state.is_diff = False` を明示していたため、OMPL の
  計画開始状態（`getCurrentStateUpdated(req.start_state)` → `complete_initial_robot_state_`）から
  ヘッドが消え、計画全体でヘッドが無視されていた。
- `check_state_validity`（§8 repro.py 含む本書の全検証）: リクエストの `RobotState` も `is_diff`
  既定 false のため同様に消去。**§5 の全試行は「衝突判定が効かない」のではなく「検証方法自体が
  attached を消していた」**。attach 登録・向き・ACM は最初から正常。

**修正**（1行 + コメント）: 正本 `kmx_planner/planner_node.py` の `start_state.is_diff = False` → `True`。
`is_diff=True` でも `joint_state` の値は絶対値として適用されるため始点指定の挙動は不変。
sync.sh → colcon build 済み。**launch 再起動後に有効**。

**検証（ROS2側・live move_group 2.5.9 で実施済み）**:
- `check_state_validity`（`rs.is_diff=True` に修正した repro）: ツール×world箱 → `valid=False,
  contacts=[('hb','tool')]` ＝ §9 完了定義1 達成。`is_diff=False` だと従来どおり `valid=True`（見逃し）。
- `plan_only` 直接 goal: ツールが箱内の start → `is_diff=False` で成功(バグ)、`is_diff=True` で
  拒否(INVALID_MOTION_PLAN)。
- 回避軌道: start J1=+40°→goal J1=−40°（直進するとツールが箱を通過）で、`is_diff=False` は直進成功
  （貫通）、`is_diff=True` は **J2..J6 最大42°の迂回軌道で成功** ＝ §9 完了定義2 の ROS2側相当を達成。

**残り**: Unity 実機での E2E（Send Obstacles + Send Head → `/kmx/plan_request` → ヘッドごと回避）確認のみ。
§8 repro.py を使う場合は `valid()` 内で `rs.is_diff = True` にすること（さもないと再び偽陰性）。

---

## 0. 一言サマリ
`moveit_msgs/AttachedCollisionObject`（フランジに attach したヘッド/ツール）が **world の CollisionObject（障害物）と衝突判定されない**。腕リンク J1..J6 は world と正しく衝突する。結果、`plan_only` の経路がヘッド形状を無視し、**ヘッドが障害物を貫通**する。登録・向き・増殖対策は正常。**衝突判定だけが効かない。**

## 1. 環境
- ROS2 **humble** / **MoveIt 2.5.9（`~/ws_moveit` のソースビルド**。`move_group` はこれを使用。apt版ではない点に注意）。
- `~/ros2_ws`：`kmx_planner`, `kmx_msgs`, `fanuc_driver`(humble, `fanuc_moveit_config` 同梱), `fanuc_description`。
- `~/colcon_ws`：`ros_tcp_endpoint`(main-ros2)。
- 起動：`ros2 launch kmx_planner kmx_bringup.launch.py use_moveit:=true`
  - endpoint + `move_group`(robot_model=crx30ia, use_mock:=true) + `kmx_planner` を一括起動。
- source（各端末）：
  ```bash
  source /opt/ros/humble/setup.bash
  source ~/colcon_ws/install/setup.bash
  source ~/ros2_ws/install/setup.bash
  ```
- **正本の場所（重要）**：`kmx_planner/planner_node.py` の正本は Windows Unityリポ
  `/mnt/c/Users/gi-guest/source/repos/Kinetic Machine eXplorer/kmx_ros2/kmx_planner/`。
  **正本を編集 → `bash "/mnt/c/.../kmx_ros2/sync.sh"` → `cd ~/ros2_ws && colcon build --symlink-install --packages-select kmx_planner && source install/setup.bash`**。
  `~/ros2_ws/src/kmx_planner` を直接編集しても sync で上書きされる。

## 2. 方式B の設計（現状の実装）
- Unity `ComRos2Obstacles.SendHead()` が、ヘッド配下の Collider を AABB 化し、**既存 `kmx_msgs/Obstacles` を別トピック `/kmx/attached` に publish**（`frame_id` = attach 先リンク名）。
- `kmx_planner/planner_node.py` の `on_attached(msg)`：
  - `link = msg.frame_id`（既定 `flange`。SRDF `manipulator` の tip_link）。
  - 各 item を `CollisionObject`(SolidPrimitive) 化 → `AttachedCollisionObject{link_name=link, object=co, touch_links=attached_touch_links}`。
  - 更新規約：**前回 attached を全 REMOVE 先行 → 今回分 ADD**（同一diff）。REMOVE時は **world側も同idをREMOVE**（MoveIt が attached REMOVE を world へ detach＝残すため、蓄積防止に必要。ここは修正済み）。
  - `head_calibration_rpy`（度・RPY、既定 `[0,90,90]`）で各 item.pose を attachリンク原点まわりに回転（Unityフランジ軸 vs URDF軸の90°ズレ補正。実機確認済み）。
- 関連パラメータ：`attached_topic`(=/kmx/attached), `attach_link`(=flange), `attached_touch_links`(=['flange','fanuc_flange','end_effector','J6_link','J5_link']), `apply_scene_service`(=/apply_planning_scene), `planning_scene_topic`(=/planning_scene)。
- 反映は `/apply_planning_scene`（ApplyPlanningScene）に `PlanningScene{is_diff=true, robot_state.is_diff=true, robot_state.attached_collision_objects=[...]}` を投げる（未準備時 `/planning_scene` publish の fallback）。

## 3. 症状 → 根本原因（確定済み）
- `get_planning_scene`(component=ROBOT_STATE_ATTACHED_OBJECTS=4) に attached は出る（登録OK）。向きOK、増殖なし。
- **しかし attached は world と衝突しない**。`plan_only` はヘッドを無視した経路を返す＝Unityでヘッドが障害物を貫通。

## 4. 検証で「効かない」ことを確定した根拠
`/check_state_validity` が **world衝突を見ること自体は実証済み**（腕リンクに重なる箱 → `valid=False`、contacts に `J4_link/J5_link/J6_link <-> box`）。その上で：
- ツール(attached)を **明確に重なる** world箱に入れても → `valid=True, contacts=0`（衝突検出されない）。
- `plan_only` でも、ツールが箱内にある start から計画が**成功**してしまう（腕が箱に重なる場合は失敗する）。

## 5. 既に潰した仮説（＝これらは原因では無い。再調査不要）
| 試行 | 結果 |
|---|---|
| attach方法：shapes直接ADD | 衝突せず |
| attach方法：world へ ADD →（同id）attach の2段階 | 衝突せず |
| attach先リンク：`flange`（collision形状なしの空フレーム） | 衝突せず |
| attach先リンク：`J6_link`（collision形状あり） | 衝突せず |
| `touch_links` = 既定 | 衝突せず |
| `touch_links` = 空 `[]` | 衝突せず |
| ACM 確認 | attached id は ACM entry_names に**存在せず**、default_entry も空（＝ACMで許可されているわけではない。本来なら衝突すべき） |

## 6. 手がかり・環境事実
- `flange` は URDF で **collision ジオメトリ無し**の空フレーム（`world`,`J1_link`..`J6_link` のみ collision あり）。ただし `J6_link`(collisionあり)に付けても同症状。
- `world→base_link` は identity。ホーム `[0,0,0,0,0,0]` で `flange` は base_link `(0.930,-0.185,1.320)`・回転 identity。
- collision detector は MoveIt2 既定（FCL）想定。`move_group` は **~/ws_moveit の 2.5.9 ソースビルド**。
- SRDF：`manipulator` の chain tip=`flange`、`fanuc_flange` は `flange` から rpy(180,-90) 回転の標準ツール座標。

## 7. 調査の方向性（推奨する切り分け）
1. **監視シーンの反映**：`/apply_planning_scene`(diff) で attach したとき、`move_group` が **collision robot に attached body の衝突形状を組み込んでいるか**。PlanningSceneMonitor が保持するシーンと、`move_action`（プランナ）が使うシーンが同一か。2.5.9 特有の挙動が無いか。
2. **attach 経路の違い**：raw の PlanningScene diff ではなく、**MoveGroupInterface / PlanningSceneInterface（C++/py）の `attachObject`** で attach した場合に衝突するかを比較（差が出れば、diff適用時の collision再構築漏れが濃厚）。
3. **collision_detector / padding**：`move_group` の collision plugin、`link_padding`/`link_scale`、`robot_description_planning` の collision 設定を確認。
4. **最小再現の切り分け**：チュートリアル config（`moveit_resources_fanuc_moveit_config demo.launch.py`）で同じ attach→world衝突を試し、**config依存か MoveIt本体(2.5.9)依存か**を分離。
5. **apt版 MoveIt との差**：`~/ws_moveit` ソースビルドを疑う。apt の move_group で再現するか。

## 8. 最短の再現手順（自己完結スクリプト）
起動後（`ros2 launch kmx_planner kmx_bringup.launch.py use_moveit:=true` → `move_action` が出るまで待つ）、以下を実行。**ツール(attached)と world箱を重ねて `check_state_validity` が衝突を見逃すこと**、および**対照として腕リンク箱は検出すること**を示す。

```python
# repro.py  :  python3 repro.py
import rclpy
from moveit_msgs.srv import ApplyPlanningScene, GetStateValidity
from moveit_msgs.msg import PlanningScene, AttachedCollisionObject, CollisionObject, RobotState
from shape_msgs.msg import SolidPrimitive
from geometry_msgs.msg import Pose
from sensor_msgs.msg import JointState
rclpy.init(); n = rclpy.create_node('repro')
app = n.create_client(ApplyPlanningScene, '/apply_planning_scene')
cv  = n.create_client(GetStateValidity, '/check_state_validity')
app.wait_for_service(timeout_sec=10); cv.wait_for_service(timeout_sec=10)
ADD, REM = CollisionObject.ADD, CollisionObject.REMOVE
def apply(s):
    r = ApplyPlanningScene.Request(); r.scene = s
    f = app.call_async(r); rclpy.spin_until_future_complete(n, f); return f.result().success
def wbox(id_, dims, pos):
    co = CollisionObject(); co.id = id_; co.header.frame_id = 'base_link'
    sp = SolidPrimitive(); sp.type = 1; sp.dimensions = dims
    po = Pose(); po.position.x, po.position.y, po.position.z = pos; po.orientation.w = 1.0
    co.primitives.append(sp); co.primitive_poses.append(po); co.operation = ADD; return co
def valid():
    rs = RobotState(); js = JointState(); js.name=['J1','J2','J3','J4','J5','J6']; js.position=[0.0]*6; rs.joint_state=js
    r = GetStateValidity.Request(); r.robot_state = rs
    f = cv.call_async(r); rclpy.spin_until_future_complete(n, f)
    return f.result().valid, [(c.contact_body_1, c.contact_body_2) for c in f.result().contacts[:6]]

# --- A: ツールを flange+X0.2m に attach、そこに重なる world箱 → 衝突するはず（実際は valid=True になる＝バグ）
s=PlanningScene(); s.is_diff=True
s.world.collision_objects.append(wbox('hb',[0.15,0.15,0.15],(1.13,-0.185,1.32))); apply(s)
s=PlanningScene(); s.is_diff=True; s.robot_state.is_diff=True
co=CollisionObject(); co.id='tool'; co.header.frame_id='flange'
sp=SolidPrimitive(); sp.type=1; sp.dimensions=[0.12,0.08,0.08]
po=Pose(); po.position.x=0.2; po.orientation.w=1.0
co.primitives.append(sp); co.primitive_poses.append(po); co.operation=ADD
a=AttachedCollisionObject(); a.link_name='flange'; a.object=co; a.touch_links=['flange','J6_link']
s.robot_state.attached_collision_objects.append(a); apply(s)
print('A) attached tool vs world box  ->', valid(), ' # 期待: valid=False, 実際: valid=True (バグ)')

# --- B: 対照。腕リンクに重なる箱 → 正しく valid=False
s=PlanningScene(); s.is_diff=True
s.world.collision_objects.append(wbox('armbox',[0.35,0.35,0.35],(0.75,-0.185,1.32))); apply(s)
print('B) arm link vs world box       ->', valid(), ' # 期待通り valid=False, contacts に J*_link<->armbox')

# cleanup
s=PlanningScene(); s.is_diff=True; s.robot_state.is_diff=True
a=AttachedCollisionObject(); a.link_name='flange'; a.object.id='tool'; a.object.operation=REM
s.robot_state.attached_collision_objects.append(a)
for i in ['hb','armbox','tool']:
    w=CollisionObject(); w.id=i; w.operation=REM; s.world.collision_objects.append(w)
apply(s)
rclpy.shutdown()
```
※ ホーム時 `flange` が base_link `(0.930,-0.185,1.320)`・identity なので、`flange+X0.2` のツール中心は `(1.13,-0.185,1.32)`＝world箱`hb`と重なる。`armbox` は腕(J6_link~0.75)に重なる。

### シーン確認コマンド
```bash
# attached 一覧（個数）
ros2 service call /get_planning_scene moveit_msgs/srv/GetPlanningScene '{components: {components: 4}}' \
 | python3 -c "import sys,re;print('attached=',len(re.split(r'AttachedCollisionObject\\(',sys.stdin.read()))-1)"
# world 一覧（個数）
ros2 service call /get_planning_scene moveit_msgs/srv/GetPlanningScene '{components: {components: 8}}' \
 | python3 -c "import sys,re;print('world=',len(re.split(r'moveit_msgs.msg.CollisionObject\\(',sys.stdin.read()))-1)"
# 実データで試すなら Unity で Send Obstacles / Send Head、または CLI:
ros2 topic pub --once /kmx/attached kmx_msgs/msg/Obstacles \
 '{frame_id: flange, items: [{id: t1, type: 1, dimensions: [0.1,0.1,0.2], pose: {position: {x: 0.2,y: 0,z: 0}, orientation: {w: 1.0}}}]}'
```

## 9. 完了の定義（このタスクのゴール）
- 上記 repro の A) が **`valid=False`（contacts に tool<->hb）** になる ＝ attached↔world 衝突が有効化される。
- Unity で Send Obstacles + Send Head 後、ヘッドが障害物に干渉する start/goal で `/kmx/plan_request` → **ヘッドごと回避する軌道**が返る（貫通しない）。

## 10. 補足（触ると事故る点）
- ヘッド向き補正は **ROS2 `head_calibration_rpy` の一本**。Unity は生送り（Unity側補正は撤去済）。**二重補正禁止**。obstacles には補正を掛けない。
- `on_attached` の removal は「attached REMOVE ＋ world 同id REMOVE」をセットで（world detach 蓄積を防ぐ）。
- ドキュメントは複数コピーが手動ミラー（`~/ros2_ws/CLAUDE.md` ＝ 正本 `kmx_ros2/HANDOFF.md`、`OBSTACLES_ROS2_SPEC.md`/`HEAD_TOOL_ROS2_SPEC.md` は正本と ~/ros2_ws の2コピー）。`sync.sh` はコードと .msg のみ同期し .md は同期しない。
