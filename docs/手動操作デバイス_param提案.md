# 手動操作デバイス パラメータ仕様（提案）

宛先：KMX パラメータ作成者 / 装置仕様担当
起案：Unity（KMX）側
関連：`Assets/StreamingAssets/Datas/UnitInfo.json` / `ActionInfo.json` / `UseDeviceList.json`、`docs/hmx-link_write要求.md`

「ユニットの動作軸をタップ → 専用の手動操作デバイスをON」のために、**どのユニットのどの軸が、どのデバイスを操作するか**をパラメータで定義する。現状の params には動作I/O（`actions[].start/end`）はあるが、**手動操作専用のデバイス定義は無い**ため新設する。

---

## 推奨案：新規 `ManualOpInfo.json`（StreamingAssets/Datas）

ユニット(mechId+name)ごとに、操作ボタン(=軸の向き)の一覧を持つ。

```json
[
  {
    "mechId": "R0230",
    "name": "シート束ストッパ",
    "ops": [
      { "label": "前進", "axis": 1, "dir":  1, "dev": "Y386", "tag": "d_plc_y1[902]", "onValue": 1, "mode": "jog" },
      { "label": "後退", "axis": 1, "dir": -1, "dev": "Y387", "tag": "d_plc_y1[903]", "onValue": 1, "mode": "jog" }
    ]
  }
]
```

### フィールド
| キー | 意味 |
|---|---|
| `mechId` / `name` | 対象ユニット（UnitInfo と一致） |
| `ops[]` | そのユニットの手動操作（＝軸方向の数だけ） |
| `label` | ボタン/方向の表示名（例「前進」「上昇」「正転」） |
| `axis` | どの軸の表示か（0=X/1=Y/2=Z。`ActionInfo.axis` と整合） |
| `dir` | 軸のどちら向きか（+1/-1。矢印ハンドルの位置） |
| `dev` | **hmx-link へ write するデバイスアドレス**（例 `Y386`）。write要求のホワイトリスト対象 |
| `tag` | 参考：対応タグ名（`UseDeviceList` と整合。内部シム/実PLC直結時はこちら経由でも可） |
| `onValue` | ON時に書く値（既定 1） |
| `mode` | `jog`=**押下中ONのデッドマン式**（100msハートビート、応答途絶/タッチ外れ/フォーカス喪失で即OFF。`docs/hmx-link_write要求.md` §8。実機操作の標準）／`alternate`=トグル／`set`=ONのみ。※実機(hmx-link)操作は安全のため原則 `jog` を使う |

### 対応 C#（追加予定）
```csharp
[Serializable] public class ManualOp {
    public int axis; public int dir; public string label;
    public string dev; public string tag; public int onValue = 1; public string mode = "jog";
}
[Serializable] public class ManualOpData {
    public string mechId; public string name; public List<ManualOp> ops = new();
}
```
ParameterLoader で `LoadListJson<List<ManualOpData>>("ManualOpInfo")` 読込 → `GlobalScript` に保持 → `UnitOperationView` が選択ユニットの ops を引いて軸ハンドルに割当 → タップで `dev` を write。

---

## 代替案：`ActionInfo.json` にフィールド追加

`actions[]` の各フェーズ（既に `start`/`dir` を持つ）に手動操作デバイスを足す。
```json
{ "trg":5350, "dir":1, "start":"d_plc_y1[902]", "manualDev":"Y386", "manualMode":"momentary" }
```
- 長所：既存の軸・方向定義と同居。長所/短所で選択。
- 短所：手動操作の有無/ラベルが動作定義に混ざる。1ユニット=1action 制約と相性確認要。

---

## 確認したいこと（パラメータ作成者へ）
1. **対象デバイス**：手動でONしてよいデバイス（`dev`）はどれか。実機の手動操作仕様・インターロックと整合する必要（`docs/hmx-link_write要求.md` §5）。
2. **操作方式**：実機操作は安全のため `jog`（押下中ON・デッドマン）を標準とする想定。トグル(`alternate`)等を併用するか。
3. **粒度**：1軸=2方向（前進/後退）か、1ボタン=1デバイスか。
4. フォーマットは推奨案(新規JSON)／代替案(ActionInfo拡張)のどちらでいくか。

決まり次第、ローダ＋`UnitOperationView`(Phase2)＋ComHmi write を実装します。
