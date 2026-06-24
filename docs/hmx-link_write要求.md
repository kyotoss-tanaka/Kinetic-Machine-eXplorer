# hmx-link 拡張要求：KMX からの書込（手動操作デバイスON）対応

宛先：hmx-link / HMX View 開発者
起案：Unity（KMX / デジタルツイン）側
関連：`docs/Unity連携仕様.md`（write/write_ack）／`docs/KMX側実装要求.md` §3(M8)／`docs/hmx-link_readonly_subscribe要求.md`

---

## ✅ 実装状況・確定仕様（hmx-link 側 / 2026-06-22）

本要求の **サーバ側（hmx-link）は実装済み**です（`hmx-link/server.js`・`hmx-link/config.json`）。KMX 側は下記の確定プロトコル／フィールド名で実装してください。

### 確定プロトコル（実装済み）
1. **書込権限の取得**（接続・hello/hello_ack 後）
   ```json
   送信: { "type":"auth", "role":"writer", "token":"<事前共有トークン>" }
   応答: { "type":"auth_ack", "ok":true, "role":"writer", "allow":["Y386","Y387"] }
   ```
   - 失敗時：`{ "type":"auth_ack", "ok":false, "role":"writer", "msg":"token" | "writer-disabled" }`
   - **token 空運用＝認証不要で writer 許可（HMX確定・2026-06）**。`auth{role:writer,token:""}` で `auth_ack ok:true`＋`allow` を返す。将来 token を設定した場合のみ一致必須（不一致＝`msg:"token"`）。`writer-disabled` は writer 機能を明示的に無効化した場合のみ（空 token では発生しない）。
2. **JOG（押下中ON・デッドマン式）**
   ```json
   押下中(100ms周期): { "type":"jog", "dev":"Y386", "val":1, "hold":true, "seq":123 }
   応答:             { "type":"jog_ack", "dev":"Y386", "seq":123, "ok":true }
   解除:             { "type":"jog", "dev":"Y386", "val":0, "hold":false }
   自動OFF通知:       { "type":"jog_timeout", "dev":"Y386" }   ← Tout超過時にサーバから送信
   ```
   - 拒否時：`{ "type":"jog_ack", "dev,seq", "ok":false, "msg":"auth"|"denied"|"mode"|"nodriver" }`

### 確定フィールド（`config.write`／環境変数）
| 設定 | 意味 | 既定 |
|---|---|---|
| `token`（または環境変数 **`HMX_WRITE_TOKEN`** 優先） | writer 認証の事前共有トークン。**空＝認証不要で writer 許可**（将来設定時のみ一致必須） | `""`（認証不要） |
| `allow` | JOG/手動操作で書込許可するデバイス配列（手動操作対象のみ） | `[]` |
| `jogTimeoutMs` | デッドマン Tout。最後の hold から この時間 hold が来なければ自動 0(OFF) | `300` |
| `manualModeDevice` | 設定時のみ：その値が ON(非0) のときだけ JOG 許可（インターロック連動・任意・要購読） | `""`（無効） |
> 秘密の token は **`HMX_WRITE_TOKEN`（環境変数）で渡す**運用（config にコミットしない）。KMX 側も同じ token を使う。

### 監査ログ（W4/W10・全件記録）
`jog_on` / `jog_off`(reason=`release`/`timeout`/`disconnect`) / `jog_denied`(reason=`auth`/`allow`/`mode`/`nodriver`) / `jog_error` / `auth_writer`(ok) を中央監査ログ（JSONL＋ハッシュチェーン）へ記録。

### 実装上の確定事項・要件対応
- **ON は押下開始の立ち上がりで1回だけ書込**。hold毎はウォッチドッグ再武装のみ（再書込せず・PLCポーリング非圧迫＝W5）。
- **解除/Tout超過/切断・異常のいずれでも必ず 0(OFF)**（両側ウォッチドッグ＝W8/W11、`jog_off` 監査）。Tout 既定 300ms。
- write は**既存ドライバ経由**でPLCへ（新規接続・再構成なし＝W3）。read-only/既存 write は無改修（受け入れ条件4）。
- 要件対応：**W1✅ W2✅(allow) W3✅ W4✅ W5✅ W6✅ W7△(manualModeDevice 任意) / W8✅ W9✅ W10✅ W11△ W12✅(config)**。

### 既知の限界
- **W11（hmx-link 再起動時）**：再起動直後は「直前ONだったデバイス」を知り得ず自動OFFできません（プロセス消失のため）。KMX側ウォッチドッグ（jog_ack途絶で停止）＋PLCインターロックで担保。必要なら「起動時に allow 全デバイスへ0書込」を任意オプションで追加可能（既定OFF：Y出力を起動毎に0化する副作用回避のため）。

### KMX 側のTODO（本実装後）
- ComHmi で `auth(role:writer, token)` 送信 → `auth_ack.allow` 保持。
- 軸押下中 100ms 周期で `jog(hold:true)`、離す/タッチ外れ/フォーカス喪失/`jog_ack`途絶 で即 `jog(hold:false)` ＋ハートビート停止（§8.3）。
- `allow` 外・未認証・`GlobalScript.isSystemRecorder` 中は UI で操作不可表示。

---

## 1. 背景・目的

KMX(タッチパネル)で 3Dユニットの「動作軸」をタップ／押下して、対応する**手動操作デバイスをONにしたい**（手動操作＝**JOG**機能）。
現状 KMX は **read-only クライアント**で、`write` は hmx-link に拒否される（KMX側実装要求 **M8**：`write_ack {ok:false, msg:"readonly"}`）。
M8 に明記の通り「書込権限つきクライアント仕様（認証・監査が必要）」を別途協議する、の具体化が本要求。

**JOG が主用途**：ボタンを**押している間だけ**デバイスをON、離すとOFF。実機が動くため、**デッドマン式ハートビート**（§8）で通信途絶時に必ずOFFへ倒すことが安全上の最重要要件。

**最優先要件**：書込機能を足しても、(a) HMI⇔PLC の通信・ドライバを揺らさない、(b) 任意デバイスへの無制限書込を許さない、(c) **通信途絶・KMX異常時はデバイスを必ずOFF（フェイルセーフ）**。

## 2. 方針

- KMX は **read-only subscribe は従来どおり**（値の購読）。
- それとは別に、**限定された「手動操作デバイス」だけ**に書ける権限を、認証付きで KMX に与える。
- 書込先は **ホワイトリスト（手動操作デバイスの集合）に限定**。それ以外の write は拒否。
- 接続(connection)は引き続き **KMX からは送らない**（ドライバは HMI / hmx-link config が所有。write は既存ドライバ経由で PLC へ）。

## 3. プロトコル拡張（案）

### 3.1 権限取得
KMX は接続後、書込権限を要求する。
```json
{ "type":"auth", "role":"writer", "token":"<事前共有トークン or 鍵>" }
```
応答:
```json
{ "type":"auth_ack", "ok":true, "role":"writer", "allow":["Y386","Y387","M1200"] }
```
- `allow`：書込を許可するデバイスのホワイトリスト（hmx-link の config で定義）。

### 3.2 書込
```json
{ "type":"write", "writes":{ "Y386":1 }, "reqId":"<任意>" }
```
応答:
```json
{ "type":"write_ack", "ok":true, "reqId":"...", "results":{ "Y386":"ok" } }
```
- 認証されていない／allow 外のデバイスは `ok:false, results:{dev:"denied"}`。

## 4. hmx-link 側の動作要件

| # | 要件 |
|---|---|
| W1 | **認証**：`role:"writer"` は token 検証に成功した接続のみ許可。失敗は read-only に留める。 |
| W2 | **ホワイトリスト限定**：write は config で許可した手動操作デバイス（`allow`）のみ。範囲外は拒否＋監査。 |
| W3 | **ドライバ非干渉**：write は**既存ドライバ経由**で PLC に書く。新規接続・再構成・切替をしない（HMIを揺らさない）。 |
| W4 | **監査ログ**：全 write を（接続ID/デバイス/値/時刻/結果）で記録。拒否も記録。 |
| W5 | **レート制限/安全**：手動操作の write に最小間隔・連打制限を設け、PLCポーリングを阻害しない。 |
| W6 | **read 併用**：writer クライアントも read-only subscribe を併用可（購読でON反映を3Dへ確認）。 |
| W7 | **PLC/HMIの手動モード前提**：実機が手動操作を受け付ける状態（HMI/PLCのモード・インターロック）でない場合の扱い（拒否/状態通知）を定義。 |

## 5. 安全上の注意（実機を動かすため必須協議）

- 実PLCのデバイスを外部から ON する＝**実機が動く**。インターロック・非常停止・運転モード（自動/手動）との整合を HMI/PLC 仕様と必ず擦り合わせる。
- 「どのデバイスを手動操作対象（allow）にしてよいか」は装置仕様側の判断。KMX 側は allow されたものだけ操作する。
- 押下中ON/トグル等の操作方式（momentary/alternate）は装置の安全要件に合わせる。

## 6. 受け入れ条件

1. 認証された writer のみが、allow リストのデバイスに write でき、それ以外は拒否される。
2. write/auth/切断のいずれでも **HMI⇔PLC 通信が途切れない**。
3. 全 write が監査ログに残る。
4. read-only クライアント（従来）は無改修で動作。

## 7. KMX 側の対応（本拡張後）

- ComHmi に `auth(role:writer, token)` 送信 → `auth_ack.allow` を保持。
- 軸の押下中、対象の手動操作デバイス（`docs/手動操作デバイス_param提案.md` で定義）を §8 の JOG 手順でON/OFF。
- `GlobalScript.isSystemRecorder` 中・allow 外・未認証では操作不可（UIで無効表示）。

---

## 8. JOG（押下中ON・デッドマン式ハートビート）★安全要件【追加要求】

ボタンを**押している間だけ**デバイスをONにし、離すとOFF。通信途絶やKMX異常時には**必ずOFF**へ倒す。安全のため **KMX・hmx-link の両側にウォッチドッグ**を持つ。

### 8.1 プロトコル
- 押下開始〜押下中：KMX は **100ms 周期**で hold（ハートビート）を送る。
  ```json
  { "type":"jog", "dev":"Y386", "val":1, "hold":true, "seq":123 }
  ```
- hmx-link は1通ごとに応答（ハンドシェイク）。
  ```json
  { "type":"jog_ack", "dev":"Y386", "seq":123, "ok":true }
  ```
- 押下解除：KMX は即OFFを送り、ハートビートを停止。
  ```json
  { "type":"jog", "dev":"Y386", "val":0, "hold":false }
  ```

### 8.2 ウォッチドッグ（両側フェイルセーフ）
- **hmx-link 側（必須・安全の要）**：デバイスごとに、最後の `hold:true` から **Tout（既定 300ms）** 以内に次の jog が来なければ、hmx-link が**自動でそのデバイスを 0(OFF) に書く**。
  → KMX のクラッシュ／フリーズ／ネットワーク断でも、デバイスは必ずOFFになる（デッドマン）。OFF時 `{"type":"jog_timeout","dev":"Y386"}` を通知。
- **KMX 側**：`jog_ack` が **Tout** 以内に返らなければ、KMX は即座に JOG を中止（ハートビート停止＝hmx-link側ウォッチドッグも作動しOFF）し、UIをOFF表示にする。
- 既定：**ハートビート間隔 100ms / Tout 300ms（約3回欠落で作動）**、config 可。短すぎ＝ジッタ誤作動、長すぎ＝安全応答遅延のトレードオフ。

### 8.3 KMX が OFF にする条件（いずれも即OFF）
- ボタンから指を離した
- 押下中にタッチが**ウィンドウ外へ出た／タッチが cancel された**
- アプリが**フォーカスを失った**（※ KMX は `Application.runInBackground=true` で非フォーカスでも動き続けるため、JOG 中のフォーカス喪失では明示的にOFFする）
- `jog_ack` 途絶（§8.2 KMX側ウォッチドッグ）
- `GlobalScript.isSystemRecorder` 中はそもそも JOG 不可

### 8.4 hmx-link 追加要件
| # | 要件 |
|---|---|
| W8 | **JOGウォッチドッグ**：dev ごとに hold を監視し、Tout 内に hold が来なければ自動で 0(OFF) を書く＋`jog_timeout` 通知。 |
| W9 | JOG 対象も **allow ホワイトリスト限定**（W2 と同じ）。範囲外の jog は拒否。 |
| W10 | jog の ON/OFF・auto-OFF をすべて**監査ログ**（W4）に記録。 |
| W11 | **hmx-link の再起動・KMX切断・PLC断のいずれでも、JOG中デバイスは安全側(OFF)へ**倒す。 |
| W12 | Tout・ハートビート間隔は config 可とし、KMX とサーバで**同一値**を使う。 |

### 8.5 受け入れ条件（JOG）
1. ボタン押下中のみデバイスがON、離すとOFF。
2. KMX を強制終了／LAN ケーブルを抜く等で**ハートビートを止めると、Tout 内にデバイスが自動OFF**になる（hmx-link 側ウォッチドッグ）。
3. 押下中にタッチがウィンドウ外へ出る／フォーカスを失うと OFF になる。
4. allow 外デバイスへの jog は拒否され、全操作が監査に残る。

---

## 9. 手動操作デバイス = HMX内部IO(IB9600-9799) 方式【確定】

### 9.1 方針（KMX側決定）
- KMX は手動操作(JOG)で **実PLCデバイスを直接書かず、HMX の手動操作用内部IO(IB9600〜IB9799)** を ON/OFF する。
- **内部IO ↔ 実デバイス/動作の紐づけは HMX 側**で行う（インターロック・安全はHMX側で担保）。
- KMX は「動作するユニット」（ActionInfo の直線/回転 mode0-3 ＋ RobotInfo の各軸）を Editor で走査し、各ユニットの軸方向に内部IO(IBxxxx)を連番割当した `ManualOpInfo.json` を **WebGLビルド時に自動生成**する（`ManualOpInfoGenerator`）。これが **「ユニット/軸 → 内部IO」対応表**となるので HMX と共有する。
  - 例: `{ "mechId":"R0230", "name":"…", "ops":[ {"axis":1,"dir":1,"label":"正転","dev":"IB9600","mode":"jog"}, {"axis":1,"dir":-1,"label":"逆転","dev":"IB9601","mode":"jog"} ] }`
- JOG のハートビート/デッドマン(§8)は内部IO書込に対しても同様に適用したい。

### 9.2 HMX への確認事項（不明点・要仕様調整）
1. **内部IOへの書込可否**: `docs/KMX側実装要求.md` M11 で「内部デバイス(IW9000/IB9500〜)は hmx-link 経由で取得不可（クライアントローカル計算）」とある。**KMX から内部IO(IB9500+)へ write でき、その値を HMX 本体(HMI)が手動操作トリガとして扱えるか**。不可なら別の受け渡し方法を要相談。
2. **アドレス範囲・書式**: 手動操作に使ってよい内部IO の範囲（IB9500〜どこまで）と表記（`IB9500` 形式でよいか）。
3. **HMX 側のマッピング/消費**: 内部IO → 実デバイス/動作 への割付方法（KMX生成の対応表をどう取り込むか）、手動モード・インターロック条件。
4. **デッドマン整合**: 内部IO書込でも §8 の jog/jog_ack/jog_timeout・auto-OFF・ホワイトリスト(allow=内部IO群)・監査が同様に効くか。
5. **値の意味**: 押下中=1(ON)/離す=0(OFF)（momentary）でよいか。

### 9.3 KMX 側の現状実装（§9.4 反映済）
- `Assets/Scripts/Editor/ManualOpInfoGenerator.cs` が `ManualOpInfo.json` を生成（**内部IO IB9600-9799（200点）**、対象=ActionInfo mode0-3＋RobotInfo各軸）。WebGLビルド前処理(`IPreprocessBuildWithReport`)で自動実行。メニュー「Kyotoss/Generate ManualOpInfo」でも実行可。
- ComHmi の JOG(§8) は dev に内部IO(IBxxxx)を渡して write（プロトコルは §8 のまま）。momentary(押下中1/離0)。

### 9.4 ✅ HMX 回答（確定 / 2026-06-22）
**方式（確定）**：手動操作マップ（**内部IO → 実デバイス ＋ インターロック**）は **HMIプロジェクトで機械別に定義**（機械ごとにデバイスが変わるため）→ deploy で hmx-link へ配布 → **実行（jog→実デバイス書込＋デッドマン）は hmx-link**。KMX は「内部IO」をシンボリックキーとして jog 送信するだけで、実デバイス割付は HMX 側。

| # | KMX確認事項(9.2) | HMX回答 |
|---|---|---|
| 1 | 内部IOへ write→HMXがトリガ扱い可否 | 生の内部デバイス値の中継は不可（M11どおり）。ただし **`jog(dev=内部IO)` を hmx-link がキーとして受け、マップした実デバイスを駆動**する方式で実現。M11抵触なし。 |
| 2 | アドレス範囲・書式 | ⚠ **IB9500+ は不可**（認可と衝突：IB9500=ログイン中/IB9501=画面ロック中/IW9500=ロール/IB9510〜9534=Feature許可25個）。KMX通信(手動操作)専用＝**`IB9600〜IB9799`（200点・HMX予約・新規）**。表記 `IBxxxx`(10進)。 |
| 3 | HMX側のマッピング/消費 | `内部IO→実デバイス＋インターロック` は **HMIプロジェクト**(`projectSettings.manualOpMap`)で機械別に保持。KMX は ManualOpInfo（ユニット/軸→内部IO）を提供するだけ。 |
| 4 | デッドマン整合 | §8 をそのまま適用。`jog/jog_ack/jog_timeout`・Tout自動OFF・allow(=手動操作内部IO群)・監査すべて有効。ウォッチドッグは**マップ後の実デバイス**に作動。 |
| 5 | 値の意味 | 押下中=1 / 離す=0（momentary）で OK（§8 hold と一致）。 |

**KMX への依頼事項**
- ManualOpInfo の `dev` は **`IB9600〜` の範囲**で採番（IB9500+不可）。
- `dev` 採番は**安定させる**（同一ユニット/同一軸方向＝常に同じ内部IO）。機種更新で番号がズレると HMX の実デバイス割付がズレる。
- 3D反映が必要なら、**マップ先の実デバイス**を通常 subscribe で購読（対応表は HMX が共有）。`jog_ack` への状態同梱が要るなら別途相談。

**HMX 側予約（内部デバイス）**：IB9000-9006 通信 / IW9000-9099 システム / IW9100-9119 メニュー / IB9500・IW9500・IB9510-9534 認可 / **IB9600-9799 KMX通信(手動操作)・200点（新規）**。

**HMX 実装メモ**：HMI に手動操作マップ編集UI（内部IO→実デバイス＋インターロック・ManualOpInfo取込）＋ `projectSettings.manualOpMap`。hmx-link は deploy 済みプロジェクトから本マップを読み、`jog(内部IO)` を実デバイスへ変換して §8 デッドマンで駆動。

### 9.5 ✅ KMX 対応・整合確認（2026-06-23）
- 生成器の内部IO範囲 **IB9600-9799（200点）** ＝ HMX 予約(§9.4)と一致。超過時はエラーログ＋範囲拡張を協議。
- **安定採番**：既存 `ManualOpInfo.json` の IB割当を引き継ぎ、新規キー `mechId|name|axis|dir` のみ未使用最小IBを割当 ＝ 同一ユニット/軸方向は常に同じ内部IO（HMX依頼事項を満たす）。
- momentary（押下中1/離0）＝§8 hold 一致。3D反映は実デバイスを通常 subscribe（ComHmi 既存購読のまま）。
- **結論：最新仕様(§9.4)に対し KMX 側のコード変更は不要（前回対応で一致済）。**

### 9.6 ランプ（PLCのボタン認識返し）追加要求（2026-06-24）

ボタンを**押した瞬間に光らせるのではなく、PLC がボタン操作を認識したことを受け取ってから点灯**させたい（実機動作確認用）。JOG(write)・インターロックに加え **ランプ(read)デバイス**を追加する。

- **内部IO範囲（追加）**：ランプ用 **`IB9800〜9999`（= JOG内部IO + 200。例 `jog=IB9608 → lamp=IB9808`）**。`ManualOpInfo.json` の各 op に `lamp` を採番（`ManualOpInfoGenerator` が自動付与）。
- **HMX への要求**：
  1. `manualOpMap` にランプ列を追加し、各ランプ内部IO に **PLC がボタン操作を認識した時 ON になる実デバイス**（PLC側の受付/ランプ信号）を機械別に割り付ける。
  2. KMX が**読取専用購読**（`subscribe readOnly` に `IB9800+` を含める）したら、その割付先実デバイス値を **`vals` で内部IOキーのまま返す**（例 `{"type":"vals","vals":{"IB9808":1}}`）。インターロック購読(§5)と同方式。
  3. ランプは read のみ（KMX は write しない・JOG とは独立）。allow には不要（点灯判定は vals の値のみ）。
- **KMX 側実装（対応済 2026-06-24）**：`ManualOp.lamp` 追加、`ComHmi.RegisterLamp/IsLampOn`（ランプ内部IOを購読し vals で状態保持）、`UnitOperationView` のボタン点灯を **ランプ読み戻し**で決定（押下中ランプOFF=「PLC確認待ち（くすんだ朱）」/ランプON=点灯）。`lamp` 未定義は従来の押下即点灯。生成器が `lamp=JOG+200` を自動採番。
- **HMX 側予約（更新）**：`IB9600-9799` JOG(write) ＋ **`IB9800-9999` ランプ(read)・200点（新規）**。
