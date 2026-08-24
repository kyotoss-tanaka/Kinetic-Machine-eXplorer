# ComEthernetIP 実装仕様

`Assets/Scripts/Com/` 配下に EtherNet/IP（CIP over TCP）通信クラス `ComEthernetIP` を追加するための実装仕様です。既存の `ComMcProtocol` / `ComMicks` と**同一の設計規約**に揃えることを前提にしています。

---

## 1. 位置づけ

```
ParameterLoader (ファクトリ)
   └ AddComponent<ComEthernetIP>() ─ SetParameter(..., KmxDirectData)
                                          │
KssBaseScript → ComBaseScript → ComProtocolBase → ComEthernetIP  ★新規
                                  (ITagCom)         │
                                                    ├ CreateMessage()    要求電文組立
                                                    ├ AnalysysMessage()  応答解析
                                                    └ Connect()          セッション確立
```

既存プロトコルとの対応関係:

| クラス | プロトコル | トランスポート | セッション |
|---|---|---|---|
| `ComMcProtocol` | MC プロトコル | TCP / UDP | 不要（ステートレス） |
| `ComMicks` | MICKS | TCP | 要（バージョン取得 → バイナリモード切替） |
| `ComOpcUa` | OPC UA | OPC UA SDK | 要（SDK が管理） |
| **`ComEthernetIP`** | **EtherNet/IP (CIP)** | **TCP 44818** | **要（Register Session）** |

`ComEthernetIP` は「**セッションを持つバイナリ電文プロトコル**」であり、実装の手本は **`ComMicks`** が最も近くなります（`Connect()` をオーバーライドしてハンドシェイクを行う点）。

---

## 2. ファイル構成

| ファイル | 内容 | 必須 |
|---|---|---|
| `Assets/Scripts/Com/ComEthernetIP.cs` | 本体（`ComProtocolBase` 継承） | ○ |
| `Assets/Scripts/Com/Datas/EipTags.cs` | CIP 定数・列挙（任意。`OpcUaTags.cs` に倣う） | △ |

`ComMicksApi.cs` / `ComOpcUaApi.cs` のような `*Api.cs` は **HTTP/REST 系の別系統**（`ComBaseScript` 直継承）であり、EtherNet/IP では不要です。

---

## 3. フレームワーク契約（override 一覧）

`ComProtocolBase` の仮想メンバのうち、`ComEthernetIP` が実装すべきものです。

### 3.1 必須オーバーライド

| メンバ | 型 | 実装内容 |
|---|---|---|
| `Start()` | `void` | `base.Start()` 後に `GlobalScript.ethernetips` へ自身を登録 |
| `Connect()` | `bool` | `base.Connect()`（TCP 接続）後に **Register Session** を実行 |
| `Disconnect()` | `void` | **UnRegister Session** 送信後に `base.Disconnect()` |
| `CreateMessage()` | `List<byte>` | 要求電文組立（読み/書きを `values` の有無で分岐） |
| `AnalysysMessage()` | `bool` | 応答解析し `data.values[i]` へ格納（float は §7.3） |
| `Write()` | `bool` | **float 書き込みのため必須**（基底実装は小数部を捨てる → §7.3.3） |

### 3.2 レジスタ種別定義

`RegisterType`（文字列）をデータ幅ごとに分類します。`CreateSortedData()` がこの分類を使ってビット / ワードの扱いを切り替えます。

| プロパティ | 用途 | ComEthernetIP の値 |
|---|---|---|
| `regTypeBit` | ビットデバイス | BOOL 配列タグ名 |
| `regTypeBit16` | 16 進ビット表記 | （未使用 → 空） |
| `regTypeData16` | 16bit レジスタ | INT / UINT 配列タグ名 |
| `regTypeData32` | 32bit レジスタ | DINT / REAL 配列タグ名 |
| `regTypeData64` | 64bit レジスタ | LINT / LREAL 配列タグ名 |
| `regTypeExistPrg` | プログラム番号を持つ | （未使用 → 空） |

> これらは `ComMcProtocol` では `{"M","X","Y",…}` のように**機種固定**で定義されています。EtherNet/IP は接続先ごとにタグ名が変わるため、**固定リストではなく `directData.tags` から動的に構築する**必要があります（→ §7.2）。

### 3.3 定数

| プロパティ | 既定 | ComEthernetIP 推奨値 | 根拠 |
|---|---|---|---|
| `BULK_RCV_COUNT` | 900 | **240** | UCMM（非接続メッセージ）の実用上限が約 504 バイト。INT 240 個 = 480 バイト |
| `BIT_COUNT` | 16 | **32** | CIP の BOOL 配列は 32bit（DWORD）単位でパックされる |
| `LAN_BUFF_MAX` | 4096 | **4096（オーバーライド不要）** | 上記より応答は 600 バイト未満に収まる |

---

## 4. 通信仕様（EtherNet/IP / CIP）

### 4.1 トランスポート

| 項目 | 値 |
|---|---|
| プロトコル | TCP（Explicit Messaging / UCMM） |
| ポート | **44818** |
| バイトオーダ | **リトルエンディアン**（`BitConverter` がそのまま使用可） |
| 受信タイムアウト | `_RCVTIMEOUT` = 3000ms（基底の定数を流用） |

> UDP 2222 の Implicit（I/O）Messaging は**対象外**。周期 I/O 通信であり、本フレームワークの「要求 → 応答」モデルに合致しないため。

### 4.2 カプセル化ヘッダ（24 バイト固定）

全電文の先頭に付与します。

| オフセット | サイズ | フィールド | 値 |
|---|---|---|---|
| 0 | 2 | Command | §4.3 参照 |
| 2 | 2 | Length | ヘッダ以降のデータ長 |
| 4 | 4 | Session Handle | Register Session で取得した値 |
| 8 | 4 | Status | 要求時 0。応答が 0 以外はエラー |
| 12 | 8 | Sender Context | 任意。要求と応答の照合に使用可 |
| 20 | 4 | Options | 0 固定 |

### 4.3 カプセル化コマンド

| コマンド | 値 | 用途 |
|---|---|---|
| NOP | `0x0000` | 死活確認 |
| ListIdentity | `0x0063` | 機器情報取得（接続確認に利用可） |
| **RegisterSession** | `0x0065` | セッション確立 |
| **UnRegisterSession** | `0x0066` | セッション解放 |
| **SendRRData** | `0x006F` | CIP 要求 / 応答（本仕様の主役） |
| SendUnitData | `0x0070` | Connected 通信用（対象外） |

### 4.4 セッション確立（Connect）

```
[要求] Command=0x0065, Length=4, SessionHandle=0
       データ: ProtocolVersion(2)=0x0001, OptionsFlags(2)=0x0000

[応答] Status=0 なら成功 → ヘッダ offset 4 の SessionHandle を保持
```

以降の全 SendRRData でこの Session Handle を使用します。`Disconnect()` では Command=`0x0066` を送ってから TCP を閉じます。

### 4.5 CIP 要求（SendRRData のデータ部）

| サイズ | フィールド | 値 |
|---|---|---|
| 4 | Interface Handle | 0（CIP） |
| 2 | Timeout | 0（カプセル化層でのタイムアウト無効） |
| 2 | Item Count | 2 |
| 2 | Item1 Type ID | `0x0000`（Null Address Item） |
| 2 | Item1 Length | 0 |
| 2 | Item2 Type ID | `0x00B2`（Unconnected Data Item） |
| 2 | Item2 Length | 後続 CIP 要求のバイト数 |
| n | CIP 要求 | §4.6 |

### 4.6 CIP サービスコード

| サービス | 値 | 用途 |
|---|---|---|
| **Read Tag** | `0x4C` | シンボリックタグ読み出し（主用途） |
| **Write Tag** | `0x4D` | シンボリックタグ書き込み（主用途） |
| Read Tag Fragmented | `0x52` | 応答が 1 パケットに収まらない場合 |
| Write Tag Fragmented | `0x53` | 同（書き込み） |
| Multiple Service Packet | `0x0A` | 複数タグ一括（将来拡張） |
| Get_Attribute_Single | `0x0E` | Assembly オブジェクト読み出し（汎用アダプタ用） |
| Set_Attribute_Single | `0x10` | 同（書き込み） |

### 4.7 EPATH セグメント

| セグメント | 値 | 内容 |
|---|---|---|
| ANSI 拡張シンボル | `0x91` | `0x91, 名前長, 名前バイト列, (奇数長なら 0x00 パディング)` |
| Class (8bit) | `0x20` | `0x20, クラス ID` |
| Instance (8bit) | `0x24` | `0x24, インスタンス ID` |
| Attribute (8bit) | `0x30` | `0x30, 属性 ID` |

配列要素の指定は、シンボルセグメントの後に添字を続けます（`0x28, idx` = 8bit、`0x29,0x00, idx(2)` = 16bit、`0x2A,0x00, idx(4)` = 32bit）。

### 4.8 CIP 応答フォーマット

| サイズ | フィールド | 内容 |
|---|---|---|
| 1 | Service | 要求サービス &#124; `0x80`（例: 読み出しなら `0xCC`） |
| 1 | Reserved | 0 |
| 1 | **General Status** | **`0x00` = 成功**。それ以外はエラー |
| 1 | Additional Status Size | ワード数 |
| n | データ | 先頭 2 バイトが CIP データ型、以降が値 |

### 4.9 CIP データ型コード

| 型 | 値 | バイト数 | `eDeviceSize` 対応 |
|---|---|---|---|
| BOOL | `0xC1` | 1 | `Bit` / `Bool` |
| SINT | `0xC2` | 1 | `Byte` |
| INT | `0xC3` | 2 | `W` |
| DINT | `0xC4` | 4 | `DW` |
| LINT | `0xC5` | 8 | `QW` / `LW` |
| USINT | `0xC6` | 1 | `Byte` |
| UINT | `0xC7` | 2 | `W`（`IsUnsigned`） |
| UDINT | `0xC8` | 4 | `DW`（`IsUnsigned`） |
| REAL | `0xCA` | 4 | `DW`（実数） |
| LREAL | `0xCB` | 8 | `QW`（実数） |
| STRING | `0xD0` | 可変 | `String` |

---

## 5. アドレス設計

既存フレームワークは `RegisterType`（種別文字列）＋ `RegisterNo`（番号）でアドレスを表し、`CreateSorted()` が **`RegisterNo` 昇順にソートして連続領域を `BULK_RCV_COUNT` 単位でチャンク分割**します。この仕組みをそのまま活かすため、次の方式を採ります。

### 5.1 主方式: 配列タグ ＋ 添字（推奨）

| 既存フィールド | EtherNet/IP での意味 | 例 |
|---|---|---|
| `RegisterType` | **配列タグ名** | `"DataBlock"` |
| `RegisterNo` | **配列の添字** | `100` |
| `DataCount` | 読み出し要素数 | `50` |
| `DataType` | 要素のデータ幅 | `W` / `DW` |

→ `DataBlock[100]` から 50 要素を Read Tag Service（`0x4C`）で一括読み出し。

配列要素は PLC 内で連続配置されるため、既存のチャンク分割ロジックが**無改造で機能**します。これが `ComMcProtocol` の「デバイス種別＋先頭番号＋点数」と構造的に等価になります。

### 5.2 副方式: Assembly オブジェクト（汎用アダプタ用・第 2 フェーズ）

シンボリックタグを持たない汎用 EtherNet/IP アダプタ向け。`RegisterType` に予約名（例 `"ASM"`）を割り当て、`RegisterNo` を Assembly インスタンス番号として `Get_Attribute_Single`（Class `0x04` / Attribute `3`）で読み出します。**初版では対象外**とします。

---

## 6. 電文組立（CreateMessage）

`ComMcProtocol.CreateMessage()` と同じシグネチャ・同じ分岐構造で実装します。

```csharp
protected override List<byte> CreateMessage(KMXDBSetting data, ref int commandId, List<ulong> values = null)
```

| 引数 | 意味 |
|---|---|
| `data` | 対象タグ（`RegisterType` / `RegisterNo` / `AllDataCount` / `DataType`） |
| `commandId` | 要求識別子。Sender Context に載せて応答照合に使う |
| `values` | **`null` = 読み出し** / 非 null = 書き込み |

組立手順:

1. `values == null` → サービス `0x4C`、非 null → `0x4D`
2. EPATH を組む: `0x91` + タグ名（`RegisterType`）+ 添字（`RegisterNo`）
3. 読み出しなら要素数（2 バイト）、書き込みならデータ型（2 バイト）＋要素数（2 バイト）＋値列
4. §4.5 の CPF で包む
5. §4.2 のカプセル化ヘッダ（Command=`0x006F`、Session Handle 設定）を先頭に付与

要素数の算出は `data.AllDataCount` を基点とし、ビット種別なら `BIT_COUNT`(=32) で切り上げ除算します（`ComMcProtocol` の該当ロジックと同じ考え方）。

---

## 7. 受信解析（AnalysysMessage）

### 7.1 手順

1. General Status（§4.8）が `0x00` 以外なら `false` を返す
2. データ型コード（2 バイト）を読み、§4.9 に従い要素サイズを決定
3. 要素を順に `data.values[index].Value` へ格納（`index` が `data.values.Count` に達したら打ち切り）
4. `DataType == UnitTag` の場合は `data.values[index].Size` から要素幅を都度決定（`ComMcProtocol` と同じ扱い）

### 7.2 regType* の動的構築

§3.2 のとおり、EtherNet/IP ではタグ名が接続先ごとに変わります。`SetParameter()` のオーバーライドで `directData.tags` を走査し、`DataType` に応じて各リストへ振り分けてから `base.SetParameter()` を呼びます。

> `base.SetParameter()` は内部で `CreateSortedData()` を呼ぶため、**リスト構築は base 呼び出しより前**に行う必要があります。

### 7.3 float（REAL / LREAL）の扱い

`TagInfo` は**既に float に対応済み**です。新規フィールドの追加は不要で、`ComOpcUa` が先行実装になっています。

| `TagInfo` フィールド | 型 | 役割 |
|---|---|---|
| `Value` | `int` | 整数値。float タグでも**切り捨て値を必ず入れる**（フォールバック） |
| `fValue` | `float` | 浮動小数点値の本体 |
| `isFloat` | `bool` | どちらを正とするかの判別フラグ |

#### 7.3.1 受信時（AnalysysMessage）

`ComOpcUa` と同じく **3 つすべてを設定**します。

```csharp
// CIP データ型が REAL(0xCA) の場合
var f = BitConverter.ToSingle(buff, i);
data.values[index].Value   = (int)f;   // 整数フォールバック
data.values[index].fValue  = f;        // 本体
data.values[index].isFloat = true;     // フラグ
```

⚠️ **`isFloat` は「true のときだけ設定」ではなく、毎回 true / false を確定させてください。** 整数型の要素では明示的に `isFloat = false` を代入します。

```csharp
// CIP データ型が INT/DINT 等の場合
data.values[index].Value   = BitConverter.ToInt32(buff, i);
data.values[index].isFloat = false;    // ★必ず false を代入する
```

片方向（true のみ設定）にすると、同一タグが以前 float として読まれた場合に `isFloat` が残り、**書き込んだ `Value` ではなく古い `fValue` が読まれる**非対称バグになります。これは `ComRos2` で実際に発生し修正された事例です（`ComRos2.cs` の該当コメント参照）。

#### 7.3.2 型の判定方法

CIP 応答にはデータ型コード（§4.9）が含まれるため、**受信時に型が自動判別できます**。設定ファイル側に float 種別を追加する必要はありません。

| 判定タイミング | 方法 |
|---|---|
| 読み出しタグ | 応答の CIP データ型コードで判定（`0xCA` / `0xCB` なら float） |
| 書き込みタグ | 基底の `Recieve()` が `isFirst` 時に `dctReadSortedTags2`（書き込みタグ）を**先に読む**ため、初回読み出しで型が確定する |

`DataType`（`eDeviceSize`）は**バイト幅の指定として** `DW`（4 バイト = REAL）/ `QW`（8 バイト = LREAL）を使います。`eDeviceSize` に `Real` を追加する必要はありません。

> 追加する場合も、§8.2 と同じ理由（数値シリアライズ）で**必ず末尾に追加**してください。

#### 7.3.3 送信時（Write のオーバーライド）★必須

基底の `ComProtocolBase.Write()` は値を次のように取り出します。

```csharp
values.Add((ulong)tag.Value);   // ← Value(int) のみ。fValue を見ない
```

このため **float タグを基底実装のまま書き込むと小数部が失われます**。`Write()` をオーバーライドし、`isFloat` の場合は IEEE754 のビット列を `ulong` に詰めてください。

```csharp
protected override bool Write(KMXDBSetting data, ref int commandId)
{
    var values = new List<ulong>();
    foreach (var tag in data.values)
    {
        if (tag == null)
        {
            values.Add(0);
        }
        else if (tag.isFloat)
        {
            // IEEE754 のビットパターンをそのまま転送（値変換ではない）
            values.Add(BitConverter.ToUInt32(BitConverter.GetBytes(tag.fValue), 0));
        }
        else
        {
            values.Add((ulong)tag.Value);
        }
    }
    // 以降は基底と同じ（CreateMessage → SendCommand → AnalysysMessage）
}
```

`CreateMessage()` 側では、対象が float なら CIP データ型コードに `0xCA`（REAL）を指定し、受け取った `ulong` の下位 4 バイトをそのまま電文へ載せます。**`(float)` へのキャストを挟まないこと**（ビットパターンが壊れます）。

#### 7.3.4 値の取り出し側（重要）

消費側の API によって float の見え方が変わります。

| API | float タグでの戻り値 |
|---|---|
| `GetTagValue()` | **`(int)(fValue * 1000000f)`** ← ×100万の固定小数点 |
| `GetTagValueF()` | `fValue`（素の float） |

⚠️ **float タグは `GetTagValueF()` で読んでください。** `GetTagValue()`（int 版）は ×1,000,000 したうえで int 化する既存仕様のため、意図しない値になります。

また、ビット指定付きの取得（`タグ名:3` 形式）は `tagInfo.Value` のビットシフトで実装されているため、**float タグへのビット指定は無意味**です。

---

## 8. 既存コードへの改修点 ★重要

`ComEthernetIP.cs` を追加するだけでは動作しません。以下 5 箇所の改修が必要です。

### 8.1 `ComProtocolBase.SendCommand()` — 最重要

現在、応答からペイロードを切り出す処理が**クラス名でハードコード分岐**しています。

```csharp
// Assets/Scripts/Com/ComProtocolBase.cs（現状）
if (this is ComMcProtocol) { /* 0x00D0 チェック、offset 9 以降を取得 */ }
else if (this is ComMicks) { /* size 分そのまま取得 */ }
return lstTmp;   // ← どちらにも該当しないと空リストが返る
```

`ComEthernetIP` は**どちらにも該当せず空リストが返る**ため、`Read()` / `Write()` の `buff.Count > 2` 判定を通らず必ず失敗します。

**対応方針（推奨）**: 切り出し処理を仮想メソッドへ抽出する。

```csharp
// ComProtocolBase に追加
protected virtual List<byte> ExtractPayload(byte[] buff, int size)
{
    return buff.Take(size).ToList();
}
```

`SendCommand()` からは `return ExtractPayload(readBuff, size);` を呼び、既存の MC / MICKS 分岐はそれぞれのクラスへ移動します。`ComEthernetIP` ではカプセル化ヘッダ（24 バイト）＋ CPF を読み飛ばして CIP 応答部を返す実装とします。

**対応方針（最小改修）**: 既存規約に倣い `else if (this is ComEthernetIP)` を追記する。改修量は小さいが、基底クラスが派生クラスを知り続ける構造が残ります。

> 既存 2 プロトコルの分岐をそのまま残すか整理するかは、影響範囲（`ComMcProtocol` / `ComMicks` の回帰確認）とのトレードオフです。**新規追加のみを目的とするなら最小改修、今後プロトコルを増やす予定があるなら仮想メソッド化**を推奨します。

### 8.2 `DBSetting.eProtocolType` に列挙子追加

```csharp
// Assets/Scripts/Com/Datas/DBSetting.cs
OPC_UA,         // = 10（既存）
EtherNetIP,     // = 11  ★OPC_UAとNoneの間に追加（KMXTool/MMS側と同じ位置）
None            // = 12
```

⚠️ 設定は `System.Text.Json` で読み込まれ（`GlobalScript.LoadListJson`）、`JsonStringEnumConverter` が未登録のため **enum は数値として永続化**されます。Postgres.json を生成する **KMXTool 側の `eProtocolType`（`EtherNetIP` = 11, `None` = 12）と数値を一致させることが最優先**です。KMXTool 側の enum 位置を変えた場合は Unity 側も同じ位置に揃え、Postgres.json を再出力してください。

### 8.3 `KmxDirectData` に判定プロパティ追加

```csharp
// Assets/Scripts/Common/AppParameter.cs
public bool isEtherNetIP
{
    get { return protocol == eProtocolType.EtherNetIP; }
}
```

### 8.4 `ParameterLoader` のファクトリに分岐追加

```csharp
// Assets/Scripts/Common/ParameterLoader.cs（isDirectMode ブロック内、isOpcUa の後）
else if (direct.isEtherNetIP)
{
    var db = (ComEthernetIP)globalSetting.AddComponent<ComEthernetIP>();
    db.SetParameter(p.No, p.Cycle, p.Server, p.Port, p.Database, p.User, p.Password, p.isClientMode, ex, direct);
}
```

### 8.5 `GlobalScript` にレジストリ追加

```csharp
// 宣言（mickses / opcuas と並べる）
public static Dictionary<string, ITagCom> ethernetips = new Dictionary<string, ITagCom>();

// 初期化ブロックにも追加
ethernetips = new Dictionary<string, ITagCom>();

// SetTagDatas の振り分けチェーンにも追加
else if (ethernetips.ContainsKey(tag.Key))
{
    ethernetips[tag.Key].SetDatas(tag.Value);
}
```

⚠️ **振り分けチェーンへの追加を忘れないこと。** 現状 `opcuas` は登録用辞書が存在するのに `SetTagDatas` の振り分けチェーンに含まれておらず、書き込みが届かない状態になっています。同じ漏れを繰り返さないよう注意してください。

---

## 9. 動作フロー

基底クラスが制御するため `ComEthernetIP` 側での実装は不要ですが、前提として把握しておく必要があります。

```
Start()
 └ StartCoroutine(DataUpdate())     ← Cycle ミリ秒周期
      └ RenewData()
           ├ IsConnected == false → Task.Run(Connect())        ★セッション確立
           └ IsConnected == true  → Task.Run(Recieve())        読み出し
                                  └ Task.Run(Send())           書き込み（初回以降）
```

| 注意点 | 内容 |
|---|---|
| **別スレッド実行** | `Recieve()` / `Send()` / `Connect()` は `Task.Run` で動くため、Unity API を直接呼べません |
| **排他** | `SendCommand()` は `m_ComLock` で保護済み。読み書きが同一ソケットを共有するため、独自の送受信を追加する場合も同じロックを使うこと |
| **例外** | 基底側で捕捉され `CommonFunction.DebugLog()` に出力されます |
| **切断検出** | `StreamRead` / `StreamWrite` の失敗で `Disconnect()` が呼ばれ、次周期で再接続されます |

---

## 10. 設定ファイル

`Assets/StreamingAssets/Datas/Postgres.json` の `directDatas` に記述します。

```json
{
  "DirectMode": 1,
  "directDatas": [
    {
      "mechId": "MECH01",
      "protocol": 11,
      "IpAddress": "192.168.1.10",
      "PortNo": 44818,
      "tags": [
        {
          "Name": "運転状態",
          "RegisterType": "DataBlock",
          "RegisterNo": 0,
          "DataCount": 50,
          "DataType": 1,
          "IsWrite": false
        }
      ]
    }
  ]
}
```

| フィールド | 意味 |
|---|---|
| `protocol` | `eProtocolType.EtherNetIP` の**数値**（= 11。§8.2 の追加位置により決まる） |
| `PortNo` | 44818 |
| `NetAddress` / `PcNo` | **未使用**（MC プロトコル専用フィールド） |
| `RegisterType` | 配列タグ名（§5.1） |
| `DataType` | `eDeviceSize` の数値。`W`=1 / `DW`=2 / `QW`=3 / `Bit`=4 |

---

## 11. 実装チェックリスト

- [ ] `ComEthernetIP.cs` を作成（`ComProtocolBase` 継承。BOM 付き UTF-8・XML ドキュメントコメント付きの既存スタイルに合わせる）
- [ ] `Start()` で `GlobalScript.ethernetips` へ登録
- [ ] `Connect()` で Register Session、`Disconnect()` で UnRegister Session
- [ ] `CreateMessage()` の読み / 書き分岐（`values` の有無）
- [ ] `AnalysysMessage()` の General Status 判定とデータ型別格納
- [ ] **float: `Value` / `fValue` / `isFloat` の 3 点セットで格納（`isFloat` は false も明示代入）→ §7.3.1**
- [ ] **float: `Write()` をオーバーライドし IEEE754 ビット列を転送 → §7.3.3**
- [ ] **float: 消費側が `GetTagValueF()` を使っているか確認（`GetTagValue()` は ×100万）→ §7.3.4**
- [ ] `SetParameter()` オーバーライドで `regType*` を動的構築（`base` 呼び出し**前**）
- [ ] `BULK_RCV_COUNT` = 240、`BIT_COUNT` = 32 をオーバーライド
- [ ] **§8.1 `SendCommand()` のペイロード切り出し対応**（未対応だと必ず失敗）
- [ ] §8.2 `eProtocolType` に**末尾**追加
- [ ] §8.3 `isEtherNetIP` プロパティ追加
- [ ] §8.4 `ParameterLoader` に生成分岐追加
- [ ] §8.5 `GlobalScript` の宣言・初期化・**振り分けチェーン**の 3 箇所追加
- [ ] 実機または EtherNet/IP シミュレータで読み出し / 書き込みを確認

---

## 12. 制約と今後の拡張

| 項目 | 初版 | 拡張案 |
|---|---|---|
| Implicit (I/O) Messaging | 非対応 | UDP 2222 の周期通信。フレームワークの要求応答モデルと合わないため別系統が必要 |
| Assembly オブジェクト | 非対応 | §5.2。汎用アダプタ対応時に追加 |
| Read Tag Fragmented (`0x52`) | 非対応 | `BULK_RCV_COUNT` 超過時に必要。まずはチャンク分割で回避 |
| Multiple Service Packet (`0x0A`) | 非対応 | 非連続タグの一括読み出しによる高速化 |
| 構造体タグ / UDT | 非対応 | Template オブジェクト経由での型情報取得が必要 |
| **REAL (float)** | **対応**（§7.3） | `TagInfo.fValue` / `isFloat` を使用。既存タグモデルの変更は不要 |
| LREAL (double) | 要検討 | `TagInfo.fValue` が `float` のため倍精度は丸められる。必要なら別途方針決定 |
| WebGL | **動作不可** | `ParameterLoader` の `#if UNITY_WEBGL` により生成対象外（ソケット非対応） |

> **LREAL について**: `TagInfo.fValue` は単精度 `float` です。`ComOpcUa` も double 受信時に `(float)dv` で丸めています（`ComOpcUA.cs`）。LREAL を厳密に扱う必要がある場合のみ、タグモデル側の拡張を検討してください。REAL（単精度）であれば §7.3 の方法で精度欠落なく扱えます。
