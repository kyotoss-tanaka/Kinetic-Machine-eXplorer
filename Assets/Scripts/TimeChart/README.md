# TimingChart Runtime — 最終版

## ファイル構成

| ファイル | 役割 | 配置先 |
|---|---|---|
| `TimingChartData.cs` | データモデル・ScriptableObject | `Assets/Scripts/TimingChart/` |
| `WaveformRenderer.cs` | GL波形描画（RenderTexture→RawImage） | 同上 |
| `TimingChartView.cs` | uGUI メインView（Canvas自動生成） | 同上 |
| `TimingChartRecorder.cs` | リアルタイム記録コンポーネント | 同上 |
| `PositionSignalGenerator.cs` | 位置チャンネル自動生成 | 同上 |
| `MachineControllerExample.cs` | 使用例（参考） | 同上 |

---

## セットアップ

### 1. DataAsset 作成
```
Assets > Create > KyotoSS > TimingChart Data
```

### 2. シーンに配置
空の GameObject に以下をアタッチし、全て同じ DataAsset を設定：
- `TimingChartView`
- `TimingChartRecorder`
- `PositionSignalGenerator`

### 3. PositionSignalGenerator の設定（Inspector）
```
m_Pairs の要素を追加：
  ForwardCommandName  = "CYL1_前進指令"   ← Recorder と同じ名前
  ForwardASName       = "AS1_前端"
  BackwardCommandName = "CYL1_後退指令"
  BackwardASName      = "AS1_後端"
  PositionName        = "POS1_位置"
  Color               = (任意)
```

---

## 制御スクリプトからの呼び出し

```csharp
void Update()
{
    // IO 記録
    recorder.SetDigital("CYL1_前進指令", DeviceCategory.Cylinder,   fwdCmd);
    recorder.SetDigital("AS1_前端",      DeviceCategory.AutoSwitch, fwdAS);
    recorder.SetDigital("CYL1_後退指令", DeviceCategory.Cylinder,   bwdCmd);
    recorder.SetDigital("AS1_後端",      DeviceCategory.AutoSwitch, bwdAS);

    // モータ位置（アナログ）
    recorder.SetAnalog("MOT1_位置", motorPos, 0f, 300f);

    // 位置チャンネル更新
    posGen.UpdateSignals("POS1_位置",
        fwdCmd: fwdCmd, fwdAS: fwdAS,
        bwdCmd: bwdCmd, bwdAS: bwdAS);
}

// JSON ロード後
void OnJsonLoaded() => posGen.GenerateFromRecordedData();
```

---

## 位置チャンネルの生成ルール

| イベント | 位置値 |
|---|---|
| 前進指令 ON エッジ | 現在値から 1 へ上昇開始 |
| 前進 AS ON エッジ | 1 に到達・確定 |
| 後退指令 ON エッジ | 現在値から 0 へ下降開始 |
| 後退 AS ON エッジ | 0 に到達・確定 |

各区間は **開始点・終了点の2サンプルのみ** で正確なリニアを表現。

---

## ウィンドウ操作

| 操作 | 内容 |
|---|---|
| マウスホイール | ズームイン/アウト（カーソル位置基点） |
| 左ドラッグ | 横パン |
| マウスオーバー | カーソル線 + 全信号値ツールチップ |
| 縦スクロール | チャンネル縦スクロール |
| AutoScroll | 最新データへ自動追従 |
| 全体表示 | 全データを画面に収める |

---

## 注意事項
- **TextMeshPro** が必要（Package Manager で導入済みであれば OK）
- JSONファイルダイアログはデフォルトで `StreamingAssets/timingchart.json` 固定。
  ダイアログが必要な場合は StandaloneFileBrowser を導入し `USE_SFB` を Define Symbols に追加。
