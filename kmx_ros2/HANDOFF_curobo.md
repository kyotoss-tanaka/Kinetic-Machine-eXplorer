# 引継ぎ：cuRobo（GPU計画）バックエンド統合 — 未着手・後日実施

**作成 2026-07-06。目的＝MoveIt(OMPL/BITstar) とは別に、NVIDIA cuRobo で GPU 並列の高速計画＋軌道最適化を
`kmx_planner` の選択式バックエンドとして統合する。** ユーザー判断で **本日は着手せず後日別途進める**（環境導入が数GB・WSL2 の CUDA ビルドという不確実性があるため）。本書はその再開用メモ。

---

## 0. 位置づけ / なぜやるか（と、急がない理由）
- 現行 **MoveIt + BITstar ＋環境修正**で、実シーン（機械カバー/ヒモ捨て箱/床＋FANUCヘッド）は
  **5/5 成功・cost 1.8倍前後**で実用足りている（[[rrtstar-smart-throughput-limit]] / `HANDOFF_rrtstar_smart.md` ✅節）。
  → cuRobo は「さらに速く・短く」を狙う**任意の上積み**。急ぎではない。
- 2026-07-06 の文献調査結論：狭所発見の本命ボトルネックは「衝突判定スループット」。cuRobo(GPU) と VAMP(CPU SIMD)
  がそれを桁違いに解く。cuRobo は幾何計画 ~20ms＋最小ジャーク軌道最適化を内包（CPU比60倍の報告）。
- 同日に **狭所 ValidStateSampler** を MoveIt に実装済（`narrow-passage-valid-state-sampler` / 下記「関連実装」）。
  ただし現行シーンでは BITstar に及ばず＝発見が既に解決済のため旨味が出ず。cuRobo は別アプローチ（GPU力技）。

## 1. 環境ステータス（2026-07-06 実測）
| 項目 | 状態 | 備考 |
|---|---|---|
| GPU | ✅ NVIDIA RTX A2000 12GB | Ampere = **compute capability sm_86** |
| ドライバ / CUDA runtime | ✅ driver 573.24 / **CUDA 12.8** | `nvidia-smi` で確認。WSL2 passthrough 動作OK |
| CUDA toolkit (`nvcc`) | ❌ **未導入** | cuRobo のカーネルコンパイルに必須。driver 12.8 以下の toolkit（例 12.1〜12.4）を導入 |
| pip3 | ❌ 未導入 | `sudo apt install python3-pip` |
| PyTorch | ❌ 未導入 | cu12x ビルドを入れる |
| cuRobo | ❌ 未導入 | github.com/NVlabs/curobo |
| Python | 3.10.12（システム） | venv 推奨（ROS2 と混ぜない） |
| CRX-30iA URDF | ✅ `fanuc_crx_description/robot/crx30ia.urdf.xacro` | cuRobo robot(球)config の生成元 |

## 2. 導入手順（想定・~6-8GB / 30-60分）
```bash
# 0) pip
sudo apt update && sudo apt install -y python3-pip
# 1) CUDA toolkit（nvcc）。driver=12.8 なので 12.1〜12.4 系でよい。runfile or apt。
#    ※ WSL2 は "CUDA on WSL" の toolkit を使う（ドライバは Windows 側、toolkit のみ Linux）
# 2) venv（ROS2 の site-packages と分離）
python3 -m venv ~/curobo_venv && source ~/curobo_venv/bin/activate
pip install --upgrade pip
# 3) PyTorch（CUDA 12.x ビルド）
pip install torch --index-url https://download.pytorch.org/whl/cu121
# 4) cuRobo（A2000=sm_86 を指定してビルド）
git clone https://github.com/NVlabs/curobo.git ~/curobo
cd ~/curobo
TORCH_CUDA_ARCH_LIST="8.6" pip install -e . --no-build-isolation
python -c "import curobo; from curobo.util_file import get_robot_configs_path; print('OK')"
```
- 詰まりどころ：nvcc バージョン>driver は不可（12.8以下に）。`--no-build-isolation` 必須級。初回 import 時に warp/JIT が走る。

## 3. 統合設計（`kmx_planner/planner_node.py` に `planner_backend=curobo` を追加）
**cuRobo は move_group を通さない別スタック**。既存の topic 契約はそのまま流用する。
- **robot 球config**：`crx30ia.urdf.xacro` を xacro 展開 → cuRobo の robot config(yaml) を作る。関節 `J1..J6`、
  base=`base_link`、tip=`flange`。**衝突球（sphere）モデル**が要る（cuRobo 付属の URDF→sphere 補助 or 手動でリンク毎に球群）。
  関節可動域は URDF から。単位は **cuRobo=ラジアン**（kmx は度）→ deg⇄rad 変換をバックエンド境界で。
- **world 障害物**：`/kmx/obstacles`（**BOX・base_link相対・メートル**）→ cuRobo `WorldConfig` の cuboid にそのまま写像
  （既に全て軸整列BOXで来る＝相性良。`kmx_ground_plane` も cuboid 1枚）。受信のたび world を作り直す（全置換は既存規約と同じ）。
- **attached ヘッド**：`/kmx/attached`（flange 相対・単一AABB `#headbox`）→ cuRobo の attach（tool 座標に cuboid/sphere）。
  `attached_merge_aabb` で1箱なので写像は容易。
- **計画**：`MotionGen.plan_single(start_joint_state, goal_joint_state, MotionGenPlanConfig)` →
  補間済み関節軌道 → **rad→deg・J1..J6 順**へ変換 → `/kmx/trajectory`(JointTrajectory, 度) を発行。
  始点＝`PlanRequest.start`、終点＝`goal`。`time_budget`/`good_ratio` は cuRobo では別概念＝`MotionGenPlanConfig`
  の反復/timeout にマップ（or 無視して既定）。
- **切替**：`planner_backend`(既定 `moveit`) に `curobo` を追加。moveit 経路は一切変えない（cuRobo は opt-in）。
  cuRobo import は try/except で保護（未導入でも moveit 経路は起動する＝`Obstacles` import と同じ堅牢化方針）。

## 4. 検証観点（導入後）
- 現行と同一シーン・同一 goal `[0,40,-30,0,70,0]` で `planner_backend=curobo` にし、
  `scratchpad/bench_planner.py`（成功率・cost/直線倍率・時間を `/kmx/trajectory` から自己完結計測）で
  BITstar と比較。**期待＝計画時間が桁で短縮、cost は同等〜改善**。
- 衝突の一致（cuRobo 球モデル vs MoveIt メッシュ）を、既知の当たる/当たらない姿勢で突き合わせる
  （球近似で緩く/きつくなり得る＝ヘッドが実際に通れるかの再確認）。
- ヘッド向き＝現行は ROS2 `head_calibration_rpy=[0,90,90]`。cuRobo attach でも同じ姿勢になるよう変換を合わせる。

## 5. リスク / 注意
- WSL2 の CUDA ビルド（nvcc・torch arch）でハマる可能性。sm_86 明示。toolkit ≤ driver(12.8)。
- 球近似モデルの精度：狭所は「通れる/通れない」が数mmで変わる（実測最薄2mm）。球が太いと通れない、細いと貫通。要調整。
- 保守コスト：MoveIt と二重スタック。**既定は BITstar 据え置き**、cuRobo は評価/上積み用に。
- venv と ROS2 Python の混在注意（rclpy は system、cuRobo は venv）。バックエンドをどう同居させるか要検討
  （別プロセス化＋topic 連携 or venv に rclpy も入れる等）。← 設計の肝。着手時に最初に決める。

## 6. 関連実装（本日の成果・cuRobo とは別に済）
- **狭所 ValidStateSampler**（MoveIt OMPL）実装済＝[[narrow-passage-valid-state-sampler]]。
  `ompl_planning.yaml` の `PRMbridge/PRMobstacle/ESTgaussian/SBLbridge`＋`model_based_planning_context.cpp` パッチ
  （ws_moveit・**更新時要再適用**）。ベンチ＝現行シーンでは BITstar(1.84) に及ばず（SBLbridge 2.11 が最速サンプラ）。
- 標準プランナ比較（実シーン単発6回）：BITstar 6/6・1.84 ／ ABITstar 5/6・1.85 ／ AITstar 4/6・2.14(不安定) ／
  RRTConnect 4/6・2.56(0.8s と最速だが長い)。→ **BITstar 据え置きが最良**。
- 文献調査（2026-07-06）：VAMP(CPU SIMD・OMPL2.0統合)、cuRobo(GPU)、狭所サンプラ、STOMP/CHOMP(狭所は弱い)。

## 7. 現在の稼働状態（再開時の前提）
- `kmx_bringup.launch.py`（endpoint+move_group+kmx_planner）稼働中。**運用既定に復帰済**：
  `planner_id=BITstar` / `plan_fallback_planner=RRTConnect` / `plan_retries=20` / `plan_time_budget_sec=10` /
  `plan_good_ratio=2.0` / `allowed_planning_time=3.0` / `path_shortcut=true` / `planner_backend=moveit`。
- planning scene は Unity 送信の実シーンが載っている（機械カバー/ヒモ捨て箱/床＋FANUCヘッド#headbox@flange）。
- ベンチ用スクリプト：`<session scratchpad>/bench_planner.py`（引数: planner_id, N回, [待ち秒]）。
