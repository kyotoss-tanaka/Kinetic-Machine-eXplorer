# ROS2連携 コードレビュー 残項目トラッカー

`c9f40d0`（🔴上位8件＝A相当）は対応済み。以下は中位以下の残項目。
**この一覧は前回会話（clear済）から復元して永続化したもの。着手のたびに更新する。**

- 正本 = このリポの `kmx_ros2/`（gitはここ）。WSL `~/ros2_ws` へは `sync.sh` で反映。
- Unity(C#)側の検証（コンパイル＋実機Play）と ROS2側の検証（colcon build＋実行）は
  a-tanaka / WSL側で行う。Claude はこの環境で Unity/ROS2 を実行できないため、
  コード修正の「動作確認済」判定はユーザー検証をもって確定とする（verify-first）。

---

## A. 検証待ち（動かす前に・最優先）
- [ ] A1 Unityコンパイル確認（`KMX_ROS2` のみで `ObstaclesMsg` 参照。赤エラー無いか）
- [ ] A2 レビュー修正の実機再確認（直接駆動／経路生成が従来通りか。購読解除・接続を触ったため）
- [ ] A3 障害物のUnity実送信（`Send Obstacles`→scene反映→迂回。まず箱1個で座標一致）

## B. 未修正のバグ（中位）→ 全件コード修正済（要 Unity/ROS2 検証）
- [x] #9  `WriteTag`/`ReadTag` を **ROSマップ宣言型を一貫適用**する形へ（一方向true化を廃止）。
      ※調査結果: ローダーは isFloat を設定せず `d_robo_a` は既定 false。「既存尊重」だと CRX が
        整数度に劣化して回帰するため、マップ(`isFloat:true`)を正として float 保持する方式にした。
- [x] #10 `OnTrajectory` で関節名/点数を検証、`ApplyPositions` を **名前で厳密マッピング**（名前が有る軌道は
      index フォールバックしない＝軸取り違え防止。失敗は軌道毎1回警告）
- [x] #11 障害物 基部解決を堅牢化: 大小無視の部分一致＋null名ガード、`SendObstacles()` が bool を返し、
      autoSend は **成功時のみ確定**（未解決は 0.5s 間隔で再試行、上限 20 回で打ち切り警告）
- [x] #12 `maxObstacleSize`(既定2.0) を追加し **巨大AABB/基部包含コライダーを除外**（`START_STATE_IN_COLLISION` 回避）。
      `layerMask=~0` の Tooltip に実運用はレイヤーで絞る旨を明記
- [x] #14 planner: マッピングを **コールバック閉包で持ち回り**（`_pending_*` 廃止＝並行安全）／
      `wait_for_server(3s)` → `server_is_ready()` の **非ブロック確認**へ
- [x] #15 `ResolveTargets` の tagDatas 走査を **序数ソートで決定化**／inbox に上限(4096)／
      再生の区間比率を **[0,1] クランプ**（負の外挿を防止）

（#13 は「障害物 position にも unitScale 適用」＝ `c9f40d0` で対応済みのため欠番）

## C. クリーンアップ（バグではないが品質）
- [ ] C1 リロード掃除の5ブロック×2経路の重複 → ヘルパー化
- [ ] C2 `LoadConfig`／`ReadTag`・`WriteTag` が既存 `GlobalScript.LoadJson`／`GetTagInfo`＋タグ生成を再実装 → 共通化
- [ ] C3 微小: プラットフォームガード3重複／`Name` 毎回連結／per-frame アロケーション／`OverlapSphere` alloc／`IsConnected` 実質常 true／登録フラグ3重複 等

## D. 機能の残タスク
- [x] D1 障害物キャリブレーション（**実機Unity＋RViz で確認完了 2026-07-05**：bin が正面・やや上・2箱接合／
      R0230(1mパネル)も正配置／巨大床(1000m)は意図除外。床は送らない方針をユーザー確定）
  - [x] **位置collapse 修正**: 基部は `unitSetting.moveObject` 配下でユニット lossyScale を継承。旧
        `baseT.InverseTransformPoint()*unitScale` は基部 lossyScale で割り、寸法(world サイズ)と単位が
        食い違い位置が縮む。→ `Inverse(baseT.rotation)*(worldCenter-baseT.position)*unitScale`（除算なし）へ。
  - [x] **基部の取り違え修正**: 部分一致がメッシュ名 `J1BASE…CRX-30IA…`(J1で回る腕・euler90°X)に先ヒット
        していた。`ResolveBase` を**完全一致(大小無視)優先**にし、コード生成の固定ルート `CRX-30iA`
        (euler=0/lossyScale=1) を掴むように。→ 箱の倒れ(縦長化)が解消。
  - [x] **ヨー補正**: 基部は世界軸(Y-up)。To<FLU> のみだと水平が90°ずれる（Unity X=前 が ROS -Y=右 に化ける）。
        `baseCalibrationEuler` を追加し既定 **(0,-90,0)**（＝ROS-Z回り+90°）で base_link(X=前,Y=左,Z=上)へ整合。
        検算: BRep `(1.255,0.534,0)`→ROS`(1.255,0,0.534)`=前・上。位置と向きに一貫適用。
  - [x] **姿勢方式を AABB へ変更**: CAD B-rep コライダーのローカル軸が傾いており、姿勢(To<FLU>)＋寸法
        並べ替えだと箱が倒れ・隣接コライダーが分離・上下反転。→ **向きを持たせず「基部フレーム軸整列の
        世界AABB(BOX)」**で送る（球のみ中心＋半径）。寸法は `newSize_i=Σ|R_ij|·size_j` ＋FLU並べ替え。
        分離/反転が解消する見込み（障害物回避用途は軸整列AABBで十分・安全）。
  - [ ] 再Playで最終確認（正面一致＋2箱が接触＋平たい方が上）。背後/反転なら cal の Y を 90/180 に
  - [x] 診断ログ `debugPose`(既定on): base pos/euler/lossyScale と 各障害物 base相対Unity→ROS(x,y,z)→dims
  - [x] Capsule は AABB 化で吸収（旧 direction=Y のみ問題は解消。厳密円柱が要る場合のみ別途）
- [x] D2 planner 起動時 `GetPlanningScene`(WORLD_OBJECT_NAMES) で既存 id を取り込み、再起動時に前プロセス
      残置の障害物を初回受信で REMOVE できるように（サービス未準備は 2s×5 回リトライ→諦め、非ブロック）
- [ ] D3 WSL側 `planner_node.py` は正本の修正を `sync.sh` で取り込み（別編集あればマージ）※今回 #14/D2 で変更

---

## 検証中に判明した追加対応（2026-07-05）
- [~] **CRX-30iA arm3 の実機↔ROS 規約差**: 実機(OPC UA)の3軸目は J2連成値(`arm3=y+z`)だが ROS は純粋関節角
      (`arm3=z`)。`GlobalScript.useRos2`（ParameterLoader が判定時に設定）で場合分け。放置すると ROS で
      `y+z` が J2 と打ち消し合い arm3 が動かない。**未コミット**（GlobalScript/ParameterLoader/CRX-30iA.cs）→ 要ROS実機で -30°確認。
- [~] **ヘッド(ツール)を MoveIt に反映（方式B）**: Unity 送信＋ROS2 受信とも実装済（未コミット/要sync）。
      - Unity: `ComRos2Obstacles.SendHead()`／ContextMenu「Send Head」。既存 `Obstacles` を `/kmx/attached` に流用
        (`frame_id`=attach先リンク)。`Kinematics6D.HeadObject` 配下 Collider を isTrigger 問わず全て AABB 化。**生送り**。
      - ROS2: `on_attached`→`AttachedCollisionObject`(全置換 `_attached_ids`, touch_links)。**補正は ROS2 `head_calibration_rpy`**。
      - **認識合わせ決定**: ヘッド補正は ROS2 一本（Unity headCalibrationEuler 撤去）。base 補正は Unity のまま。二重補正禁止（HANDOFF §4.1）。
      - **確定(2026-07-05)**: attach_link=`flange`(SRDF tip)・touch_links 既定は実在名でOK。`head_calibration_rpy=[0,90,90]` 実機確認→param既定へ焼込。安定id＋Clear Scene で累積対策。
      - **残(性能)**: ヘッドが 150+ Collider で重い→ `headAsSingleBox`(既定false) で1個AABB化可（把持開口不要なら推奨）。要検証。

- [~] **地面(ground plane)を障害物に**: Unity `ComRos2Obstacles.sendGroundPlane`(既定true)で、**基部の真下・床の高さ**に
      **可動範囲サイズの薄板(既定4×4×0.1m, id=kmx_ground_plane)** を `/kmx/obstacles` で送る。床の高さは `groundNameContains`
      (="Floor")の Collider 上面から取得。実床(1000m)は送らず軽量。ROS2は既存 on_obstacles で処理＝**新規実装不要**。
      汎用の `extraObstacleNames`(明示障害物・除外無視) も別途あり(既定 空)。**未コミット**（ComRos2Obstacles.cs）。

- [~] **計画予算 time_budget/good_ratio を Unity から送る**（ROS2→Unity 要望書 `PLAN_BUDGET_UNITY_SPEC.md`・ROS2側受け皿実装済）:
      Unity 実装済（未コミット）: `IRos2Transport.PublishPlanRequest` に timeBudget/goodRatio 追加（既定0=ROS2既定）／
      RosTcpConnectorTransport で `PlanRequestMsg.time_budget/good_ratio` セット／`ComRos2PathPlanner` に
      `planTimeBudget`/`planGoodRatio` SerializeField 追加し RequestPlan で送信。生成 PlanRequestMsg にフィールド有り(再生成済)。
      検証: Test Plan → ROS2 ログ `plan request: … time_budget=X good_ratio=Y` が出れば Unity 送信成功。

- [~] **経路計画のレビューUX（計画中表示/成否/経路プレビュー→OK/Cancel）**（要望書 `PLAN_STATUS_ROS2_SPEC.md`）:
      Unity 中核 実装済（未コミット・要検証）: `ComRos2PathPlanner` を状態機械化（Idle/Planning/Preview/Playing/Failed）。
      軌道受信で即再生せず **先端軌跡ライン(LineRenderer, ロボは動かさずFKサンプル)** を表示→`ApprovePlan`(OK)/`CancelPlan`(Cancel)。
      `/kmx/plan_status`(std_msgs/String)購読で計画中/成功/失敗、+timeout保険。`State`/`StatusMessage`/`StateChanged` 公開。
      Kinematics6D/CRX-30iA に `SampleTipWorld`（先端位置サンプル）追加。
      **残**: ①ROS2側 `/kmx/plan_status` publish（`PLAN_STATUS_ROS2_SPEC.md`）②Unity std_msgs 生成（未生成ならコンパイル要対応）
      ③**専用パネル(uGUI)**＝中核検証後に載せる（ボタン→Approve/Cancel、`StateChanged`で表示更新）。

## 次バッチ候補（C クリーンアップ）
バグではないので B/D と分離。着手時は別コミット推奨（検証済みコードへの広い差分を混ぜない）。
- C1 リロード掃除（`Ros2TransportFactory.Create`＋`destroyed`＋`OnDestroy`→`Disconnect`）が 3 コンポで重複 → 基底 or ヘルパー
- C2 `LoadConfig`/`ReadTag`/`WriteTag` が既存 `GlobalScript.LoadJson`/`GetTagInfo`＋タグ生成を再実装 → 共通化
- C3 微小: プラットフォームガード3重複／`Name` 毎回連結（キャッシュ）／`IsConnected` 実質常 true／登録フラグ3重複
      （※ `loopPlayback` は Update で使用中＝デッドではない）
</content>
</invoke>
<invoke name="Skill">
<parameter name="skill">TodoWrite