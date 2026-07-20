# Cartesian JOG ヘッドオフセットTCP 実装仕様（KMX側）

作成: 2026-07-19 / 対象: KMX(Unity製HMI) の Cartesian JOG（数値IK） / 関連メモリ: [[cartesian-jog-numerical-ik]]

> 目的: **Cartesian JOG時に、ヘッド(ツール)オフセットで TCP をずらし、吸盤などの作業点を基準に X/Y/Z/RX/RY/RZ を制御する**。
> 例: RobotInfo.json の `RobotSetting.offset` に吸盤位置を入れると、その点基準で位置・姿勢を調整できる。
> KMX側チャットが実装する前提の要求仕様。

---

## 1. ゴール / スコープ
- **やること**: JOG（`ComRos2PlanPanel` の Cartesian JOG）で、TCPを**ヘッドオフセット点**（例=吸盤）に置く。並進・回転・現在値読取りが全てその点基準になる。
- **やらないこと**: JOG UI の改修（不要）。関節空間の通常運転（タグ/ROSで J1..J6 を送る経路）への変更（後述のとおり関係なし）。
- **再利用**: オフセット値は既存 `RobotSetting.offset`（現状6Dでは休眠）。可視化/IK/JOG導線は現行のまま。

---

## 2. 現状の把握（コード確定事項）
### 2.1 TCPの定義とJOG/IKの流れ
- **TCP = `TipTf`**（`HeadObject`があればその transform、無ければ `arm6` フランジ）: [CRX-30iA.cs:105](Assets/Scripts/Kinematics/6Aixs/Fanuc/CRX-30iA.cs#L105)。
- `GetTcpPoseWorld`（JOG開始/現在値の基準）と `FkPoseNoSave`（数値IKが使うFK）が、いずれも **`tip.position` / `tip.rotation` を素で使用**: [CRX-30iA.cs:108-121](Assets/Scripts/Kinematics/6Aixs/Fanuc/CRX-30iA.cs#L108-L121)。
- JOG本体（[ComRos2PlanPanel.cs:953-998](Assets/Scripts/Com/Ros2/ComRos2PlanPanel.cs#L953-L998)）:
  - `ReadCartFromCurrent` → `GetTcpPoseWorld` でTCP姿勢を読み `cartVals[X/Y/Z/RX/RY/RZ]` に変換。
  - `ApplyCartTarget` → `cartVals` から world 目標姿勢を組み `TrySolveIkWorld(pos, rot, …)` で関節を解く。**位置は cartVals の点、回転はその点姿勢**（＝位置を保ったまま向きだけ変える）。
- ⇒ **`GetTcpPoseWorld` と `FkPoseNoSave` が返すTCPをオフセット点にすれば、読取り・並進・回転すべてがその点基準になる**（回転もその点回りにピボット）。**JOG側の改修は不要**。

### 2.2 オフセットの現状
- `RobotSetting.offset`（`List<float>` [X,Y,Z]）は [Kinematics3D.SetParameter](Assets/Scripts/Kinematics/Kinematics3D.cs#L214-L223) で `offsetX/offsetY/offsetZ`（protected・CRXも継承）に読込み済み。
- **3D経路**（`Kinematics3D.setTarget(Vector3)`）はこれを `target` から素引き＝ガントリ等のカルテシアン目標のツールオフセット。
- **6D経路**（`Kinematics6D.setTarget(Vector3,Vector3)` → `SetTarget(x,y,z,rx,ry,rz)`）は **offsetを使っていない**（[Kinematics6D.cs:213-216](Assets/Scripts/Kinematics/Kinematics6D.cs#L213-L216)）。

### 2.3 ★重要な前提: 6Dの `SetTarget` 引数は「関節角」
`CRX_30iA.SetTarget(x,y,z,rx,ry,rz)` は各 arm の localEulerAngles に**直接 J1..J6 を代入するFK**（[CRX-30iA.cs:58-69](Assets/Scripts/Kinematics/6Aixs/Fanuc/CRX-30iA.cs#L58-L69)）。つまり6Dの通常運転は**関節空間**（タグ/ROSが J1..J6 を供給）。
→ **ツールオフセット（カルテシアン量）は `SetTarget`/関節経路に足してはいけない**（意味が壊れる）。オフセットが効くべきは **Cartesian JOG/IK の TCP定義（`GetTcpPoseWorld`/`FkPoseNoSave`）だけ**。6Dで offset が休眠なのは正しい設計で、本仕様はそのカルテシアン層にだけ効かせる。

---

## 3. 実装（最小・局所変更 / CRX-30iA.cs）
TCP位置を「tip をヘッドオフセット分ずらした点」にするヘルパーを追加し、TCPを返す2箇所で使う。

```csharp
// ヘッド(ツール)オフセットを tip の向きで適用した TCP world 位置。
// offset は既存 robo.offset 由来(offsetX/Y/Z)。TransformPoint ではなく position + rotation*offset で
// スケールの影響を避ける（offset は長さ量）。
private Vector3 TcpWorldPos(Transform tip)
    => tip.position + tip.rotation * new Vector3(offsetX, offsetY, offsetZ);   // 単位は §4-1 で確定

public override bool GetTcpPoseWorld(out Vector3 pos, out Quaternion rot)
{
    var tip = TipTf;
    if (tip == null) { pos = Vector3.zero; rot = Quaternion.identity; return false; }
    pos = TcpWorldPos(tip); rot = tip.rotation; return true;   // ← オフセット点
}

private void FkPoseNoSave(double[] q, out Vector3 pos, out Quaternion rot)
{
    SetTarget((float)q[0], (float)q[1], (float)q[2], (float)q[3], (float)q[4], (float)q[5]);
    var tip = TipTf;
    pos = TcpWorldPos(tip); rot = tip.rotation;                // ← オフセット点
}
```

**これだけで得られる挙動**:
- **X/Y/Z JOG** = 吸盤(オフセット点)を並進。
- **RX/RY/RZ JOG** = 吸盤回りに回転（位置保持）。
- **Cartesian現在値** = 吸盤基準で表示。
- `offset=(0,0,0)` なら従来と完全一致（後方互換）。

`SetTarget` / 関節経路 / `setTarget(Vector3,Vector3)` は**変更しない**（§2.3）。

---

## 4. 実装前に確定すべき設計判断
1. **offsetの単位**: 既存3D経路は `target`(m) から素引き＝**メートル規約**。ただし**6Dでは休眠中なので独立に決められる**。吸盤位置はmm入力が自然なので、**6Dは mm 採用＋`TcpWorldPos` で `/1000f`** を推奨（`new Vector3(offsetX,offsetY,offsetZ)/1000f`）。3Dには影響しない。RobotInfo.json の既存値の単位と必ず整合させる。
2. **軸の向き**: offset(X,Y,Z) は **tip(HeadObject)ローカル軸**。「吸盤はツールのどの軸方向へ何mm」の対応は、**1軸に既知値を入れてJOGのCartesian読み値/TCPマーカーが想定方向に動くか目視確認**（「1件入れて確認」流儀）。
3. **他6D機種への展開**: `M_20iD25` 等も同様。`Kinematics6D` に `protected Vector3 TcpWorldPos(Transform tip)` を1つ置き、各機種の `GetTcpPoseWorld/FkPoseNoSave` から呼ぶ形にすると共通化できる。
4. **経路プレビューの整合（任意）**: `SampleTipWorld`（[CRX-30iA.cs:75](Assets/Scripts/Kinematics/6Aixs/Fanuc/CRX-30iA.cs#L75)）も `tip.position` 使用。プレビュー軌跡も吸盤基準に揃えるなら同じ `TcpWorldPos` を適用。JOGだけで良ければ不要。

---

## 5. 検証（完了条件）
1. RobotInfo.json の `offset` に既知値 → JOG開始で **Cartesian現在値が吸盤点**を指す。
2. **X/Y/Z JOG** で吸盤が並進する。
3. **RX/RY/RZ JOG** で吸盤回りに回転（吸盤位置は不動、向きだけ変わる）。
4. `offset=(0,0,0)` で従来挙動と一致（回帰なし）。
5. 軸方向・単位が想定どおり（§4-1,2 を1軸ずつ目視）。
6. 関節空間の通常運転（タグ/ROSでJ1..J6）が**不変**であること（`SetTarget`未変更の確認）。

---

## 6. 参考ポインタ
- TCP/IK: `Assets/Scripts/Kinematics/6Aixs/Fanuc/CRX-30iA.cs`（`TipTf`/`GetTcpPoseWorld`/`FkPoseNoSave`/`TrySolveIkWorld`/`SetTarget`）。
- JOG本体: `Assets/Scripts/Com/Ros2/ComRos2PlanPanel.cs`（`ReadCartFromCurrent`/`ApplyCartTarget`/`GetBaseAxes`）。
- offsetロード: `Assets/Scripts/Kinematics/Kinematics3D.cs`（`SetParameter` の `offsetX/Y/Z`）、`setTarget(Vector3)`。
- 6D共通層: `Assets/Scripts/Kinematics/Kinematics6D.cs`。
- データ型: `Assets/Scripts/Common/AppParameter.cs`（`RobotSetting.offset`）。
- 設計背景: [[cartesian-jog-numerical-ik]]（Cartesian JOG＋数値IK・ヤコビアンper-radian）。
