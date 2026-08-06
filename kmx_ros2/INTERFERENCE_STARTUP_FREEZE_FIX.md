# 干渉チェッカ 起動時フリーズ 修正仕様（KMX側）

作成: 2026-07-23 / 対象: KMX(Unity製HMI) の機械干渉チェッカ / 関連メモリ: [[collider-cookable-fallback]]

> 症状: **ユニットのモデルに `collision`(＝`unitSetting.isCollision`) を true にすると、起動(ロード)中に固まる/止まってしまう。**
> 結論: `MachineInterferenceChecker` の**ベースライン採取が起動時に無条件で走る**のが原因。**実行時トグルがONの時だけ走らせる**よう遅延化する。
> ※FANUC DCS とは無関係。KMX内部の当たり判定の話。

---

## 1. 症状
- あるユニットの `isCollision=true`（かつ `actionSetting`あり）にすると、**起動中に長時間フリーズ**する（重いモデル/囲まれ配置ほど顕著）。
- 実行時の衝突チェック（トグル）をONにしていなくても発生する。

## 2. 原因（コードで確定）
1. **配線**: [ParameterLoader.cs:800-817](Assets/Scripts/Common/ParameterLoader.cs#L800-L817)。`isCollision && actionSetting!=null` のユニットを **a側チェック対象(`checkedRoots`)** として `MachineInterferenceChecker.Setup(movingRoots, checkedRoots, prefabObj)` を**ロード中に**呼ぶ（相手b側＝機械全体 `prefabObj`）。
2. **起動時ベースライン**: [MachineInterferenceChecker.cs:162-182](Assets/Scripts/Collision/MachineInterferenceChecker.cs#L162-L182)。`FixedUpdate` が `!baselineReady` の間、**`GlobalScript.isCollision`(実行時トグル)がOFFでも・`intervalFrames`間引きも無視して、完了まで毎フレーム `CheckCore(true)`** を走らせる（設計上の常時接触ペアの採取）。
3. **中身が重い**: `CheckCore` は **チェック対象ユニット × 機械全体** のメッシュ三角形突き合わせ。`BuildWorld`（[:412](Assets/Scripts/Collision/MachineInterferenceChecker.cs#L412)）が `mesh.vertices/triangles` を読んでワールド頂点配列を確保（**予算外**）＋SAT交差判定。1フレーム上限 `triTestBudget=120000` はtri-testのみに効く。

→ 対象が**細かい多数サブメッシュ**／**機械に囲まれてbounds重なりが多い**と、ベースラインが多数フレーム分＝**ロード直後に長時間フリーズ**。**核心は「トグルOFFでも `isCollision` を付けただけで起動時にこのコストを払う」こと**。

補足（無関係と確認済）:
- 旧・重いMeshCollider生成 `GlobalScript.CreateCollider`/SAColliderBuilder は**廃止済**（[AxisMotionBase.cs:518](Assets/Scripts/Kinematics/AxisMotion/AxisMotionBase.cs#L518)）。
- `maxTrianglesPerMesh=6000` 超の巨大単一メッシュは判定スキップ。犯人は中サイズ多数メッシュ／囲まれ配置。

## 3. 切り分け（ログで確認）
- `[Interference] 部品 N(チェック対象 M) … 準備 ready=true` の後、
- `[Interference] 基準接触 K ペア…登録` が**出るまでの間で固まっていれば**ベースラインが犯人。
- `三角形テスト予算(120000)超過。次フレームに継続` の連発＝量過多の裏付け。

## 4. 修正（本命：ベースラインをトグルON後に遅延）
`FixedUpdate` の**ベースライン採取ブロックを、実行時トグル `GlobalScript.isCollision` のガードの後ろに移動**する。＝トグルOFF中はベースラインも走らせない。

**現状** [MachineInterferenceChecker.cs:162-182](Assets/Scripts/Collision/MachineInterferenceChecker.cs#L162-L182):
```csharp
private void FixedUpdate()
{
    if (!ready) { return; }
    if (!baselineReady)                       // ← トグルOFFでも起動時に走る(フリーズ元)
    {
        if (CheckCore(true)) { baselineReady = true; Debug.Log(...); }
        return;
    }
    if (!GlobalScript.isCollision)
    {
        if (curRed.Count > 0 || prevRed.Count > 0 || scanOffset != 0) { RevertAll(); }
        return;
    }
    if (intervalFrames > 1 && (frameCtr++ % intervalFrames) != 0) { return; }
    CheckCore(false);
}
```

**修正後**:
```csharp
private void FixedUpdate()
{
    if (!ready) { return; }

    // トグルOFF中は「ベースラインも判定も」走らせない（起動時フリーズ回避）。
    if (!GlobalScript.isCollision)
    {
        if (curRed.Count > 0 || prevRed.Count > 0 || scanOffset != 0) { RevertAll(); }
        return;
    }

    // 初めてONになった時に設計上の接触を採取（完了まで間引き無視で毎フレーム）。
    if (!baselineReady)
    {
        if (CheckCore(true)) { baselineReady = true; Debug.Log($"[Interference] 基準接触 {baseline.Count} ペア…登録"); }
        return;
    }

    if (intervalFrames > 1 && (frameCtr++ % intervalFrames) != 0) { return; }
    CheckCore(false);
}
```

- 効果: `isCollision` を付けても**起動は止まらない**。干渉チェックを**実際にONにした時だけ**（間引きしつつ）ベースラインを採る。ROS2障害物検知用途の `isCollision`（非トリガBoxCollider実体化）とも両立。
- 注意: トグルをベースライン採取中にOFF→再ONすると `RevertAll` が `scanOffset/resumeJ` を戻すため**ベースラインは最初から採り直し**（`baseline` はHashSetで冪等なので正しく完了する。効率だけの話）。

## 5. 補助策（必要なら併用）
- **ロード完了までベースライン開始を待つ**（ロードのフレーム予算を食わない）。ロード完了フラグを見て `ready` を立てる等。
- **初回採取中は `triTestBudget` を絞る**（1フレーム負荷を下げてカクつきを分散）。
- **チェック対象が巨大なユニットは簡易プロキシメッシュ**にする（`maxTrianglesPerMesh` で弾かれない中サイズ多数メッシュ対策）。

## 6. すぐの回避（コード変更なし）
- 該当ユニットの **`isCollision` を false に戻す**（＝チェッカが配線されず起動は元通り）。
- ただし `isCollision` は **ROS2障害物検知(非トリガBoxCollider)・`WorkCollisionScript`** も兼ねる（[AxisMotionBase.cs:520-539](Assets/Scripts/Kinematics/AxisMotion/AxisMotionBase.cs#L520-L539)）。それらが要るなら §4 の恒久対策で。

## 7. 検証（完了条件）
1. `isCollision=true` のユニットありで**起動が止まらない**（ベースラインが起動時に走らない）。
2. 実行時トグルを**ONにした時に初めて**ベースライン採取ログが出て、以後干渉が赤表示される。
3. トグルOFFで赤が戻り、判定が止まる。
4. ROS2障害物検知(`isCollision`のBoxCollider)は従来どおり動く（起動時から）。

## 8. 参考ポインタ
- 配線: `Assets/Scripts/Common/ParameterLoader.cs`（`checkedRoots`/`checker.Setup`）。
- チェッカ本体: `Assets/Scripts/Collision/MachineInterferenceChecker.cs`（`FixedUpdate`/`CheckCore`/`BuildWorld`/tunables `intervalFrames`/`maxTrianglesPerMesh`/`triTestBudget`）。
- isCollisionの多重役割: `Assets/Scripts/Kinematics/AxisMotion/AxisMotionBase.cs`（`SetCollision`）、`Assets/Scripts/Collision/WorkCollisionScript.cs`、`GlobalScript.isCollision`。
- 設計背景（コライダー安定化の経緯）: [[collider-cookable-fallback]]。
