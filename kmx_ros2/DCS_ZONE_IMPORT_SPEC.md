# DCS安全ゾーン取り込み＆可視化 実装仕様（KMX側）

作成: 2026-07-15 / 対象: KMX(Unity製HMI) / 関連: FANUC CRX-30iA の DCS(Dual Check Safety)

> この資料は「**FANUC DCS のカルテシアン安全ゾーンを KMX に取り込み、シーン上にボックスで可視化する**」機能の実装仕様。KMX側チャットが実装する前提でまとめる。
> ROBOGUIDE評価の経緯は [[roboguide-eval]]、障害物パイプラインは `kmx_ros2/OBSTACLES_ROS2_SPEC.md` を参照。

---

## 1. ゴール / スコープ
- **やること**：DCSで定義された安全ゾーン（直交空間の箱）を KMX に読み込み、ロボット周辺に**半透明/ワイヤフレームのボックスとして表示**する。安全域（居てよい）と禁止域を**色で区別**。
- **やらないこと（重要）**：KMXからDCS設定を**書き換えない**。DCSは安全機能で、変更にはペンダントでのパスワード＋適用＋チェックサム＋コールドスタートが要る。**取り込みは一方向（読むだけ）**。設計思想は「DCS＝固定の検証済み安全エンベロープ／KMXの経路計画はその内側で回す」。
- 任意拡張（§6）：取り込んだゾーンを MoveIt の計画制約にも流用（既存の障害物パイプライン流用）。

---

## 2. FANUC DCS 側：ゾーンデータの実体
- DCSの **Cartesian Position Check (CPC)**＝指定フレーム上の**軸平行ボックス**。1ゾーンあたり概ね次を持つ：
  - **有効/無効**（enable）
  - **箱の範囲**：X/Y/Z の下限・上限（**単位 mm**）を、指定フレーム（通常ロボット**World/base フレーム**、または DCS ユーザフレーム）で定義。
  - **inside/outside**：**ロボットが居てよいのが箱の内側か外側か**（安全域の向き）。
  - 反応（停止種別/出力）— 可視化には不要。
- 実体はコントローラのシステム変数 `$DCS_CPC[i]`（サブフィールド）に格納され、**DCSバックアップに含まれる**。ゾーン数上限や回転箱可否は**版・オプション依存**なので実機のDCS設定/マニュアルで要確認（§8）。
- **取り出し方（データ源）**：
  1. **オフライン・エクスポート（推奨・初手）**：ペンダント MENU→SYSTEM→DCS で各CPCの数値を確認、または DCSバックアップ/DCSレポートから **X/Y/Z上下限・フレーム・inside/outside・enable** を書き写し、後述の `SafetyZoneInfo.json` を作る。ゾーンは静的なので**セルごとに一度**でよい。
  2. **将来の自動読み取り（任意・Phase2）**：`$DCS_CPC[...]` をPC Interface/SNPX や ROS 経由で読めれば自動生成可。ただし露出可否は版依存。まずは 1 で十分。

---

## 3. KMX側の既存資産（流用元・調査済み）
| 用途 | 既存物 | 場所 |
|---|---|---|
| 可視化する箱の生成（塑性コピー元） | `ShapeScript.SetParameter()`（Cube生成＋localScale＝size＋Resources URPマテリアル） | `Assets/Scripts/Devices/ShapeScript.cs` |
| 箱のデータモデル/JSONロードの型 | `ShapeSetting` / `UnitShape`、`ShapeInfo.json` | `Assets/Scripts/Common/AppParameter.cs`（class 1118/1139, loader 1177） |
| ロボット基準フレーム | `IRos2PlanTarget.GetBaseTransform()`（CRX: `crx.transform`, localPos=0） | `Assets/Scripts/Com/Ros2/IRos2PlanTarget.cs:51` / `Assets/Scripts/Kinematics/6Aixs/Fanuc/CRX-30iA.cs:209` |
| Unity↔ROS 軸/単位変換の実装と“基準補正” | `ComRos2Obstacles`（`baseCalibrationEuler` 既定 `(0,-90,0)`、`BoxFromWorldAabb`）、`RosTcpConnectorTransport`（`To<FLU>`） | `Assets/Scripts/Com/Ros2/ComRos2Obstacles.cs` / `RosTcpConnectorTransport.cs` |
| ランタイム線描画（ワイヤフレーム用） | `ComRos2PathPlanner.CreatePreviewLine()/BuildPreviewLine()`（LineRenderer, 色/幅, 再コンパイル時cleanup） | `Assets/Scripts/Com/Ros2/ComRos2PathPlanner.cs:1505/1656` |
| JSONロード＆per-unitバインドの流儀 | `ParameterLoader`（`ShapeInfo` を `LoadListJson<T>` で読み `unitSetting.shapeSetting` に結線） | `Assets/Scripts/Common/AppParameter.cs:1177/1212`, 結線 360/955 |

**注意点（既存資産の“罠”）**：
- `ShapeScript` が借りる Resources の Cube マテリアルは**不透明**。半透明ゾーンには**新規の透明URPマテリアル**が要る（Surface=Transparent, 低α）。
- 単位は **1 Unity単位＝1 m**。**DCSは mm** なので取り込み時に **×0.001**。
- Unity基準軸 ≠ URDF/コントローラ base 軸。障害物送信では `baseCalibrationEuler`（CRXは `(0,-90,0)`）で補正済み。**ゾーン配置でも同じ補正が要る**（§4.4）。

---

## 4. 実装仕様

### 4.1 データモデル ＋ `SafetyZoneInfo.json`
`ShapeSetting`/`ShapeInfo.json` を踏襲し、**ロボットunitに紐づく**新規型を追加（`Assets/Scripts/Common/AppParameter.cs` に）：

```csharp
public class SafetyZoneSetting {
    public string mechId;      // どのロボットunitか（shapeSetting と同じ結線キー）
    public string name;        // 表示名
    public string frame;       // "world"（ロボットWorld/base）等。DCS定義フレーム
    public string unit;        // "mm"（既定）。KMX側で ×0.001
    public List<SafetyZone> zones;
}
public class SafetyZone {
    public string id;          // 例 "CPC1"
    public bool enabled;
    public bool insideAllowed; // true=箱の内側が安全域(居てよい)/false=箱の内側が進入禁止
    public List<float> min;    // [xmin,ymin,zmin] （frame・unit準拠）
    public List<float> max;    // [xmax,ymax,zmax]
}
```

配置：`Assets/StreamingAssets/Datas/SafetyZoneInfo.json`。例：
```json
[
  { "mechId": "CRX30_1", "name": "CRX30_DCS", "frame": "world", "unit": "mm",
    "zones": [
      { "id":"CPC1", "enabled":true, "insideAllowed":true,
        "min":[-1200,-1200,0], "max":[1200,1200,2200] },
      { "id":"CPC2", "enabled":true, "insideAllowed":false,
        "min":[300,-200,0], "max":[900,400,600] }
    ] }
]
```
（値は例。実データはDCSエクスポート§2から。）※ゾーンをRos2Info.jsonに同居も可だが、per-unitバインドが素直な `ShapeInfo` 流儀を推奨。

### 4.2 ロード（`ParameterLoader`）
- `ShapeInfo` と同じく `LoadListJson<SafetyZoneSetting>("SafetyZoneInfo")` で読み込み（`GlobalScript.LoadListJson<T>` 使用）。
- per-unit 結線も `shapeSetting` と同じ箇所（`unitSetting.safetyZoneSetting = list.Find(x=>x.mechId==...)`）。
- 生成トリガも `ShapeScript` と同様、ロボットunitの生成後に付与（§4.3）。ROS2無効時でも表示したいなら `KMX_ROS2` gate の外に置く。

### 4.3 可視化 `SafetyZoneScript`（`ShapeScript` を塑性コピー）
- 新規 MonoBehaviour `Assets/Scripts/Devices/SafetyZoneScript.cs`。
- ゾーンの表示位置は **`IRos2PlanTarget.GetBaseTransform()` を親にしたコンテナ**の下に置く（ロボットと一緒に動く/整列）。
- **塗り（半透明ボックス）**：`ShapeScript` と同じく `GameObject.CreatePrimitive(Cube)` → `localScale = size_m` → **新規透明URPマテリアル**を割当。`insideAllowed` で色分け（安全域=緑α0.15 / 禁止域=赤α0.2 など）。
- **枠（ワイヤフレーム）**：`ComRos2PathPlanner.CreatePreviewLine()` を参考に LineRenderer で箱の12辺（または閉ループ）を描く。塗り＋枠併用が見やすい。再コンパイル/シーン再読込時の **cleanup を必ず実装**（`ComRos2PathPlanner` の `DestroyStalePreview` を踏襲）。
- enable=false のゾーンは非表示 or グレー表示。

### 4.4 座標・単位・フレーム整合（★最重要）
1. **mm→m**：`center_m = (min+max)/2 * 0.001`、`size_m = (max-min) * 0.001`。
2. **フレーム変換**：DCSゾーンは**ロボットWorld/baseフレーム（URDF系）**。KMXの `GetBaseTransform()` は Unity 系。障害物送信の逆変換が要る。
   - 障害物送信の実効写像（`ComRos2Obstacles`）：Unity `p=(x,y,z)` → ROS `(z,-x,y)`。**逆**は ROS/URDF `(rx,ry,rz)` → Unity `(-ry, rz, rx)`。
   - さらにロボット基準の向き補正 `baseCalibrationEuler`（CRX `(0,-90,0)`）を**コンテナの回転**に反映（障害物で使っている値をそのまま流用）。
3. **実装の進め方（OBSTACLES_ROS2_SPEC §4 と同じ流儀・強く推奨）**：
   - まず**1ゾーンだけ**表示し、ROBOGUIDE/実機のDCS表示と**目視で突合**して座標が合うことを確認してから全ゾーンへ。
   - 最初は「コンテナを `GetBaseTransform()` の子にし、`baseCalibrationEuler` を回転に入れ、上記軸写像で localPosition を出す」→ズレたら**コンテナ側に調整オフセット/オイラーを1つ持たせて実測合わせ**。合ったら固定。
   - frame が DCSユーザフレームの場合は、そのフレームのオフセット/回転も加味（§8で要確認）。

### 4.5 表示の意味づけ
- `insideAllowed=true`（内側が安全）→ 箱＝**動いてよい範囲**（緑）。
- `insideAllowed=false`（内側が禁止）→ 箱＝**進入禁止**（赤）。
- 凡例/ラベルにゾーン `id` と enable 状態を出すと現場で分かりやすい。

---

## 5. データ源フロー（まとめ）
```
DCS(コントローラ) の CPC 設定
  → §2-1 でエクスポート/転記（X/Y/Z上下限・frame・inside/outside・enable, 単位mm）
  → SafetyZoneInfo.json（§4.1）
  → ParameterLoader ロード（§4.2）
  → SafetyZoneScript が base フレーム下にボックス描画（§4.3/4.4）
```
※将来 §2-2（`$DCS_CPC` 自動読み取り）ができれば、JSON生成を自動化できる。

---

## 6. 任意拡張：ゾーンを MoveIt の計画制約にも使う
- 既存の障害物パイプライン（`Ros2Obstacle` + `ComRos2Obstacles.BoxFromWorldAabb` + `PublishObstacles` → `kmx_msgs/Obstacles` → planner_node）を流用可能。
- **注意（inside/outs’ideの違い）**：障害物は「keep-out（その箱に入るな）」。
  - `insideAllowed=false`（進入禁止箱）→ そのまま **type=1 BOX の障害物**として送れる。
  - `insideAllowed=true`（内側が安全＝外側に出るな）→ 障害物1個では表現できない（補集合）。MoveIt のワークスペース境界、または安全域を囲う複数の keep-out 箱で近似が必要。まずは**可視化のみ**にとどめ、計画制約化は別途検討。

---

## 7. 検証（完了条件）
1. `SafetyZoneInfo.json` に1ゾーン → 起動 → ロボット周辺に箱が出る。
2. その箱の位置/寸法が **ROBOGUIDE/実機のDCS表示と一致**（§4.4の実測合わせ）。
3. 複数ゾーン＋inside/outside色分けが正しい。
4. enable反映、シーン再読込/再コンパイルでゾーンが**残留/多重生成しない**（cleanup）。
5. ROS2無効ビルドでも表示が壊れない（gate方針を明記）。

---

## 8. 未確定 / 確認事項
1. 実機CRX-30iAの **DCS CPC ゾーン数・回転箱可否**。→ ペンダント/DCSマニュアルで確認。（**定義フレームは §8' で World/UF0 と確定**）
2. `baseCalibrationEuler` の逆適用でゾーンがピタリ合うか（§4.4は要実測。障害物送信が正しく効いている前提の逆算）。
3. `mechId` の結線キー（`shapeSetting` と同じ値でよいか）。
4. 表示ON/OFFのUI（メニューにトグルを置くか）。
5. §2-2（`$DCS_CPC` 自動読み取り）の可否＝PC Interface/SNPX/ROS で該当変数が読めるか（版・オプション依存）。

---

## 8'. ROBOGUIDE実測で確定した事項（2026-07-15）
ROBOGUIDEのグラフィカルDCS「直交位置チェック(CPC)」エディタで CPC1 を定義・可視化して確認：
- **定義フレーム＝World（ユーザ座標 0 / UF0）**（§8-1 確定）。→ KMX側は base フレーム＋`baseCalibrationEuler` の逆適用で合わせる。
- **単位＝mm**、**箱＝対角2点** `(X1,Y1,Z1)-(X2,Y2,Z2)`（頂点8の軸平行ボックス）。→ `min`/`max` に1:1対応。
- **モード `対角(内側)/(外側)`** が inside/outside 切替＝`insideAllowed`。**`外側`＝箱の内側が進入禁止(keep-out)＝`insideAllowed=false`**（赤表示で確認）。
- グループ＝`GP:1 - CRX-30iA`（→ `mechId`）。停止方法＝パワーオフストップ（可視化には不要）。
- ROBOGUIDE側UIは 表示/ワイヤフレーム/透明度スライダ/色（内側=緑・外側=赤・危険=黒・無効=黄）を持つ＝**KMXの色分け・透明度設計の参考**。

**実測サンプル（このCPC1をそのまま `SafetyZoneInfo.json` に）**：
```json
[
  { "mechId": "<実際のロボットunit id>", "name": "CRX30_DCS", "frame": "world", "unit": "mm",
    "zones": [
      { "id": "CPC1", "enabled": true, "insideAllowed": false,
        "min": [300, -300, 0], "max": [900, 300, 800] }
    ] }
]
```
（keep-out小箱：ロボット前方 X300-900 / Y±300 / Z0-800。`insideAllowed=false`＝内側が進入禁止。）

---

## 9. 参考ポインタ
- 可視化コピー元：`Assets/Scripts/Devices/ShapeScript.cs`（`SetParameter`）。
- データ型/ロード流儀：`Assets/Scripts/Common/AppParameter.cs`（`ShapeSetting`/`UnitShape`/`ParameterLoader`）。
- 基準フレーム：`Assets/Scripts/Com/Ros2/IRos2PlanTarget.cs:51`、`Assets/Scripts/Kinematics/6Aixs/Fanuc/CRX-30iA.cs:209`。
- 座標変換/基準補正：`Assets/Scripts/Com/Ros2/ComRos2Obstacles.cs`（`baseCalibrationEuler`, `BoxFromWorldAabb`）、`RosTcpConnectorTransport.cs`（`To<FLU>`）。
- ワイヤフレーム線：`Assets/Scripts/Com/Ros2/ComRos2PathPlanner.cs:1505/1656`。
- 障害物仕様（流儀・検証手順の参考）：`kmx_ros2/OBSTACLES_ROS2_SPEC.md`。
- DCSの位置づけ/安全設計方針：[[roboguide-eval]] 及び本チャットの議論。
