# KMX → hmx-link 接続要求（手動操作 JOG）

KMX（Unity / WebGL）から HMX のバックエンド **hmx-link** へ手動操作（JOG）を送るための通信仕様。
方式は **(A) KMX が自前の WebSocket クライアントで hmx-link に直接接続**する（View 経由の中継はしない）。

プロトコルの正典は `docs/hmx-link_write要求.md §9`。本書はその中で **KMX 側が実装すべき事項**を抜き出したもの。
動作確認済みの最小リファレンス実装：本リポジトリの `jog-test.js`（Node）。**これと同じ手順を踏めば動きます**。

---

## 0. 現状（重要）— 2026-06-25 更新

- hmx-link 側・HMX 側は **JOG(write) 実装済みで動作確認済み**（`jog dev:IB9608 → 実デバイス M2000` の ON/OFF を確認済み）。
- **KMX 側も §2〜§7 を実装済み**（`ComHmi`）：`hello_ack` ／ `auth(role:writer, token空)` ／ JOG デッドマン（100ms `hold` ＋ ack 途絶ウォッチドッグ）／ キープアライブ `ping` ＋ 自動再接続・再auth ／ 読取専用 `subscribe`（`clientType:"kmx"` 付与）／ ランプ・インターロック読取。UI ガードは `auth_ack.allow` で判定。
- **残課題＝読取データ（位置・動作）の受信**：従来 `ComPostgres`（Postgres `latestdata` を全取得）が担っていた**機械駆動データ**を、`ComHmi` の `vals` 受信に置き換え中。これが成立するかは **§10 の H1〜H7（HMX側への確認事項）次第**。特に **H1（一般デバイスの `vals` 返信）** が要（これが無いと購読できても位置が動かない）。
- **接続実績の確認**：監査ログ／Link ステータスに **KMX 接続（`clientType:"kmx"`）と auth・購読**が見えるか（§10 H7）。見えなければ接続先（`wsUrl`）／hmx-link 稼働の問題。KMX 側の受信状況は `[ComHmi]` ログ（`subscribe sent` ／ `vals#X: matched/total` ／ `unmatched=…` ／ 購読タグ一覧）で判別できる。
- → JOG(write) は両側実装済み。**残るは読取データ受信（§10）＝HMX 側の確認・対応**が鍵。

## 1. 接続先

- URL: `ws://<hmx-linkのホスト>:8765`
  - 同一 PC（埋め込み WebGL）なら `ws://localhost:8765`。
  - View と同じ接続先（プロジェクト設定の WebSocket URL）に合わせる。
- 注意: WebGL ページが **https** で配信される場合、ブラウザは **ws://（非TLS）をブロック**する。現状は http 配信なので `ws://` で可。https 配信に変える場合は `wss://` ＋証明書が必要。
- 接続は **JOG 操作する画面の表示中だけ**でよい（常時接続でも可）。切断時は hmx-link 側が自動で OFF にする。

## 2. ハンドシェイク（接続直後）

1. 接続すると hmx-link から `{"type":"hello","version":"3.3"}` が届く。
   → KMX は **`{"type":"hello_ack"}`** を返す。
2. writer 認証を要求する：
   **`{"type":"auth","role":"writer","token":""}`**
   - 現状トークンは**空運用（認証不要）**。`token:""` または省略でよい。
   - 将来トークンを設定する場合は一致が必須（環境変数 `HMX_WRITE_TOKEN`）。
3. hmx-link が返す：
   **`{"type":"auth_ack","ok":true,"role":"writer","allow":["IB9608","IB9609", …]}`**
   - `allow` = **操作可能な内部IO一覧**（HMX 側で実デバイスが割付済みのもの）。**KMX はこれを保持**する。
   - `ok:false` のとき `msg`（`"token"` / `"unknown-role"`）に理由。

## 2.5 接続維持・再接続（重要）

hmx-link は「クライアントから一定時間メッセージが無い」と接続を切断する（`heartbeatTimeout`、現在 **30秒**）。**KMX は接続を維持し、切れたら必ず再認証すること。**

- **キープアライブ ping**：押していない間も **`{"type":"ping"}` を 2〜5秒ごと**に送る（hmx-link は `{"type":"pong"}` を返す）。これを送らないと無通信で切断される。
- **自動再接続**：WebSocket が閉じたら自動で再接続する。
- **再接続後は §2 のハンドシェイクをやり直す**：`hello_ack` → **`auth{role:writer}`（再auth必須）** → 必要なら購読を再登録。
  - ⚠ **再auth を省くと writer 権限を失い、JOG が常に灰色（操作不可）になる**（実際に発生した不具合）。`auth_ack` の `allow` も毎回受け直して保持する。

## 3. JOG（押下中 ON のデッドマン式）

### 押下開始〜押下中（ハートビート）
**`{"type":"jog","dev":"IB9608","val":1,"hold":true,"seq":<連番>}`** を **100ms ごとに送り続ける**。

- `dev` = **内部IO（IB9600〜）**。KMX の `ManualOpInfo.json` で定義したキー。
  **実デバイス（例 `R0230`）ではない。** 必ず内部IOを送ること。
- `val` = ON させる値（通常 `1`）。
- `hold` = `true`（押下中）。
- `seq` = 単調増加の連番（任意。`jog_ack` 照合用）。

### 押下解除
**`{"type":"jog","dev":"IB9608","val":0,"hold":false,"seq":<連番>}`** を 1 回送る。

### hmx-link の応答
- `{"type":"jog_ack","dev,"seq","ok":true}` … 受理（割付先の実デバイスへ反映）。
- `{"type":"jog_ack","dev","seq","ok":false,"msg":"…"}` … 拒否。`msg`：
  | msg | 意味 | 対処 |
  |---|---|---|
  | `auth` | （トークン設定時）writer 未認証 | §2 の auth を実施 |
  | `denied` | その内部IOに実デバイスが未割付 | HMX で割付→配信 |
  | `nodriver` | PLC ドライバ未接続 | PLC/接続先を確認 |
  | `mode` | インターロック不成立 | §5 参照 |
- `{"type":"jog_timeout","dev"}` … デッドマン Tout（hold が途切れて自動 OFF した通知）。

## 4. 安全（デッドマン）— KMX 側の責務

- hold を **100ms 間隔で送り続ける間だけ** ON を維持する。
- **300ms** hold が途切れると hmx-link が**自動で OFF** にする（KMX クラッシュ／LAN 断／タブ非アクティブでも確実に倒れる）。
- ボタンを離したら必ず `hold:false` を送る（即 OFF）。送り損ねても 300ms で自動 OFF。
- WebGL ではタブが非アクティブになると `setInterval` が間引かれる点に注意（その場合も 300ms で自動 OFF し、安全側に倒れる）。

## 5. インターロック（HMX 側設定 ＋ KMX 側で事前グレー表示）

- HMX 側でマップ行にインターロック条件を設定した場合、条件成立時のみ JOG 許可（不成立＝`jog_ack ok:false msg:"mode"`）。
- インターロック判定は hmx-link のポーリングキャッシュの値で行う。**そのデバイスを誰か（View/HMI）が購読していないと「不明＝安全側で拒否」**になる。
  - 通常は HMI がその画面でインターロックデバイスを表示・監視していれば自動で巡回される。
- **KMX 側の事前グレー表示（実装済 2026-06）**：押す前から操作可否を見せるため、各動作に**インターロック読取内部IO**（`ManualOpInfo.json` の `interlock` = JOG内部IO + 400、例 `jog.dev=IB9608 → interlock=IB10008`、§7.6 参照）を持ち、読取専用購読する。`vals` の値が **ON のときだけ操作可**、**OFF / 値未受信(不明)はボタンを灰色（操作不可）**にする（安全側）。これは §6 の認証/allow 判定と AND 条件。
  - HMX への要求は §7.6 を参照。`interlock` 未割付（空）の動作はインターロック制約なし（従来どおり）。

## 6. UI ガード（KMX 側の操作可否表示）

「操作可」とする条件：
1. `auth_ack ok:true` を受領（writer 確定）。
2. 対象軸の**内部IOが `allow` に含まれる**こと。
   - 含まれない＝HMX 側で実デバイス未割付。「操作不可（未割付）」表示でよい。
   - **実デバイス名で `allow` 判定しないこと**（allow は内部IOのリスト）。

## 7. 内部IO の取り決め（KMX ⇔ HMX）

- 内部IO は **KMX の `ManualOpInfo.json`** がユニットの動作ごとに定義（KMX が採番）。1動作につき3つ:
  - **JOG (write)**：`IB9600〜9799`（200点）。KMX が `jog.dev` として ON/OFF を書く。
  - **ランプ (read)**：`IB9800〜9999`（= JOG内部IO + 200）。**§7.5 参照**。
  - **インターロック (read)**：`IB10000〜10199`（= JOG内部IO + 400）。**§7.6 参照**。
- HMX（Studio）はそれを取り込み、各内部IO に**この機械の実デバイスを割付 → 配信（通信→Viewへ送信）**する。
- 割付済みの内部IO のみ `allow` に載る＝操作可能になる（allow は JOG 内部IO の一覧）。
- KMX は **`ManualOpInfo.json` の内部IO をそのまま `jog.dev` / ランプ・インターロック購読に使う**（HMX と同じキー体系）。
- ⚠ `IB9500+` は HMX 認可領域と衝突するため不可。手動操作専用は `IB9600〜10199`（JOG=9600-9799 / ランプ=9800-9999 / インターロック=10000-10199）。

## 7.5 ランプ（PLCのボタン認識返し）— 追加要求

ボタンを **押した瞬間に光らせるのではなく、PLC がボタン操作を認識したことを受け取ってから光らせたい**（実機の動作確認用）。そのための「ランプデバイス」を追加する。

- KMX は各動作に **ランプ用の読取内部IO**（`ManualOpInfo.json` の `lamp` = JOG内部IO + 200、例 `jog.dev=IB9608 → lamp=IB9808`）を持つ。
- **HMX への要求**：
  1. 各ランプ内部IO（`IB9800〜9999`）に、**PLC がボタン操作を認識した時に ON になる実デバイス**（PLC側の受付/ランプ信号）を `manualOpMap` で割り付ける。
  2. KMX が **読取専用購読**（§5 と同形式 `{"type":"subscribe","readOnly":true,"interval":...,"devices":["IB9808", …]}`）でランプ内部IOを購読したら、**その（割付先実デバイスの）値を `vals` で内部IOキーのまま返す**こと（例 `{"type":"vals","vals":{"IB9808":1}}`）。
  3. JOG (write) とは独立。ランプは read のみ（KMX は書かない）。
- **KMX の挙動**：ランプ内部IOを購読し、`vals` の値が ON ならボタンを点灯。押下したがランプがまだ OFF の間は「PLC確認待ち（くすんだ朱）」、ランプ ON で点灯（朱フィル）。`lamp` 未定義の動作は従来どおり押下即点灯。
- インターロック（§5）と同様、ランプ実デバイスは hmx-link のポーリングキャッシュ値で返す。割付が無い／値が来ない場合、KMX 側はボタンが点灯しない（押下中はPLC確認待ち表示のまま）。

## 7.6 インターロック読取（操作可否の事前グレー表示）— 追加要求

JOG ボタンを **押す前から操作可否を見せたい**（インターロック不成立なら灰色で押せない）。そのための「インターロック読取デバイス」を追加する（§5 の HMX 側拒否 `jog_ack msg:"mode"` に加え、KMX 側でも事前に灰色化）。

- KMX は各動作に **インターロック用の読取内部IO**（`ManualOpInfo.json` の `interlock` = JOG内部IO + 400、例 `jog.dev=IB9608 → interlock=IB10008`）を持つ。
- **HMX への要求**：
  1. 各インターロック内部IO（`IB10000〜10199`）に、**その動作を許可してよい時に ON になる実デバイス**（運転モード・安全条件・インターロック成立信号）を `manualOpMap` で割り付ける。
  2. KMX が **読取専用購読**（§7.5 と同形式 `{"type":"subscribe","readOnly":true,"interval":...,"devices":["IB10008", …]}`）でインターロック内部IOを購読したら、**その（割付先実デバイスの）値を `vals` で内部IOキーのまま返す**こと（例 `{"type":"vals","vals":{"IB10008":1}}`）。
  3. JOG (write) とは独立。インターロックは read のみ（KMX は書かない）。
- **KMX の挙動**：インターロック内部IOを購読し、`vals` の値が **ON のときだけ操作可**（朱）。**OFF / 値未受信(不明)はボタン灰色（操作不可）**で押下も受け付けない（安全側＝§5 の「不明＝拒否」と整合）。`interlock` 未定義（空）の動作はインターロック制約なし（従来どおり）。
- 注意：割付が無い／値が来ないと**常に灰色**になる。HMX 側で必ず実デバイス（または常時ONダミー）を割り付けること。

## 7.7 接続元の識別（clientType）— 追加要求（2026-06-25）

- `subscribe` に **`"clientType":"kmx"`** を付与すること。
  - 例：`{"type":"subscribe","clientType":"kmx","readOnly":true,"interval":200,"devices":["IB9808","IB10008", …]}`
  - Studio の「Linkステータス」モニタで接続元が **KMX** と明示される（デバッグ用）。
  - 未指定だと推定表示になり、`fastActive` 等を送る実装では **View と誤判定**される（実際に発生）。

## 8. メッセージ例（接続〜1回JOG）

```text
S→C  {"type":"hello","version":"3.3"}
C→S  {"type":"hello_ack"}
C→S  {"type":"auth","role":"writer","token":""}
S→C  {"type":"auth_ack","ok":true,"role":"writer","allow":["IB9608","IB9609"]}
（押下開始：100ms ごとに繰り返し）
C→S  {"type":"jog","dev":"IB9608","val":1,"hold":true,"seq":1}
S→C  {"type":"jog_ack","dev":"IB9608","seq":1,"ok":true}
C→S  {"type":"jog","dev":"IB9608","val":1,"hold":true,"seq":2}
…
（押下解除）
C→S  {"type":"jog","dev":"IB9608","val":0,"hold":false,"seq":N}
S→C  {"type":"jog_ack","dev":"IB9608","seq":N,"ok":true}
```

## 9. JavaScript リファレンス（WebGL の JS 相互運用にそのまま流用可）

```js
let ws, allow = [], writer = false, hb = null, seq = 0;

function connect(url) {            // url = "ws://localhost:8765"
  ws = new WebSocket(url);
  ws.onmessage = (e) => {
    const m = JSON.parse(e.data);
    if (m.type === 'hello') {
      ws.send(JSON.stringify({ type: 'hello_ack' }));
      ws.send(JSON.stringify({ type: 'auth', role: 'writer', token: '' }));
    } else if (m.type === 'auth_ack') {
      writer = !!m.ok; allow = m.allow || [];   // 操作可否はこの allow で判定
    } else if (m.type === 'jog_ack' && !m.ok) {
      console.warn('jog denied:', m.dev, m.msg);
    }
  };
}

function canOperate(io) { return writer && allow.includes(io); }

function jogStart(io, val = 1) {    // 押下開始
  if (!canOperate(io)) return false;
  const send = () => ws.send(JSON.stringify({ type: 'jog', dev: io, val, hold: true, seq: ++seq }));
  send();
  hb = setInterval(send, 100);     // 100ms ハートビート
  return true;
}

function jogStop(io) {             // 押下解除
  if (hb) { clearInterval(hb); hb = null; }
  ws.send(JSON.stringify({ type: 'jog', dev: io, val: 0, hold: false, seq: ++seq }));
}
```

## 10. HMX側への確認事項（読取データ＝位置・動作の受信）

KMX は従来 **ComPostgres**（Postgres `latestdata` テーブルを `SELECT *` で**全タグ取得**）で機械を駆動していた。これを **ComHmi（hmx-link 経由）** に置き換える。
JOG(write)・ランプ/インターロック(read) は §3〜§7.6 で確定済みだが、**機械の動作を駆動する一般読取データ（位置・IO）を hmx-link から受け取れるか**が未確認。以下を HMX 側に確認したい。

| # | 確認事項 | KMX 側の前提 |
|---|---|---|
| **H1** | **一般デバイスの `vals` 返信**：`subscribe readOnly` した**全デバイス**（位置 `d_mech_pos`、IO `d_plc_x/y` 等の**実PLCデバイス**を含む。ランプ/IL の内部IO だけではない）の現在値を `vals` で周期返信するか？ **← これが無いと model が動かない（最重要・ComPostgres置換の核心）** | UseDeviceList.json の全 `dev` を購読し、`vals` を `tagDatas` に反映してモーション計算する |
| **H2** | **`vals` キーの完全一致**：KMX が送る `dev`（UseDeviceList の `dev` 値）と hmx-link が返す `vals` のキーが**書式まで完全一致**するか（配列添字 `[..]`・大小文字・ゼロ埋め等）。不一致＝反映不可 | `devToBinding[dev]` で dev→tag 解決。一致しないと unmatched 扱いで無視 |
| **H3** | **アドレス体系の混在**：読取（位置/IO）は**実PLCデバイス**（`Y5F` / MICKS 等）、JOG/ランプ/IL は**内部IO**（`IB….`）。両方を **1つの `subscribe.devices` に混ぜて**送るが、hmx-link は両体系を同じ `vals` で返せるか？ | 1接続・1購読に内部IO＋実デバイスが混在 |
| **H4** | **32bit 値**：位置 `d_mech_pos`(x/y/z) 等の 32bit は、KMX は size:2 で「下位 `dev` ＋ 上位 `dev`(アドレス+1)」を購読し合成する。hmx-link の返し方（1キーで32bit / 2ワード分割）と整合するか？ | size:2 は下位/上位2デバイスを購読・合成 |
| **H5** | **更新周期**：一般読取データは主ポール周期での更新という理解で正しいか？ その周期は何 ms？（§11 改訂メモの「高速ポール」はランプ/IL 即応用） | `subscribe.interval`（既定 200ms）で要求 |
| **H6** | **購読数の上限/負荷**：UseDeviceList は数十〜数百デバイス。大量購読でも `vals` を周期返信できるか（上限・負荷・絞り込みの要否） | 読まれたタグ(wasRead)のみ動的購読（KMX側実装要求 S2） |
| **H7** | **§0 接続の現状**：監査ログに KMX の auth/jog 痕跡が無い件は今も継続か？ `clientType:"kmx"`（§7.7）付与後、Link ステータスに **KMX 接続**が見えるか？ 見えなければ接続先(`wsUrl`)/hmx-link 稼働を要確認 | ComHmi が WS 接続・auth(writer)・購読を実施 |

> 補足：ComPostgres は「タグ名(`event_id`)」で値を受けていたが、ComHmi は「デバイスアドレス(`dev`)→タグ」の対応（UseDeviceList）で受ける。**H1〜H3 が成立しないと、購読はできても位置が反映されない**（KMX 側ログ `[ComHmi] vals#X: matched/total`・`unmatched=…` で判別可能）。

### HMX回答（2026-06-25）

- **H1：返します（最重要OK）。** `subscribe`（read-only 含む）した**全デバイスの現在値を `vals` で周期返信**します。実PLCデバイス（位置・IO）も主ポールの読出プランに含めて巡回し、**変化分**を配信。
  - 前提：**主クライアント（View/HMI）が接続して接続先設定でPLCドライバを確立**していること。read-only 単独（KMX だけ）だとドライバ未確立で読みません（R7）。KMX は View と併用なのでOK。
  - 例外：**未割付の内部IO(IB/IW)** はPLC非デバイスのため読みません（ランプ/IL別名は割付があれば実デバイス値を返す）。実PLCデバイスは対象。
- **H2：完全一致します。** `vals` のキーは **KMX が `subscribe.devices` で送った `dev` 文字列そのまま**を返します（hmx-link 側で正規化・改変せずエコー）。ただし `dev` はドライバが解釈できる正しい書式である必要（読めない書式は値が返らない）。配列添字 `[..]` 等の特殊書式は実機で1点だけ疎通確認を推奨。
- **H3：混在OK。** 1接続・1購読に**内部IO(別名)と実PLCデバイスを混在**させて可。各アドレスを個別解決し、別名→実デバイス値（キーは内部IOのまま）／実デバイス→自身の値 を**同じ `vals`** で返します。
- **H4：16bitワード単位で返します。** hmx-link は 1アドレス=1ワード(16bit) で返すので、KMX が **下位 `dev` ＋ 上位 `dev`(addr+1) を size:2 で購読して合成**する方式と整合（符号/エンディアン合成はKMX側）。1キーで32bitを返す機能は使いません（2ワード分割でOK）。
- **H5：主ポール周期です。** 一般読取は fast/slow 以外＝**主ポール（readPlan）**で読みます。周期は `subscribe.interval`（全クライアントの最小・既定200ms・接続先設定のmin/maxにクランプ）。`fast(≈100ms)` はランプ/IL別名専用、`slow(≈3000ms)` はAIコンテキスト専用。
- **H6：数百点OK。** ブロック結合＋0403ランダム読出で最適化。実測（Linkステータス）で**通常405点・処理平均40ms・周期200msで余裕**（超過0）。ハード上限なし、負荷はLinkモニタで可視（点数/ブロック/処理min·avg·max/超過）。KMX の wasRead 動的購読でさらに削減推奨。共有デバイスは最速tier側へ寄せます。
- **H7：clientType 実装済なので、hmx-link 再起動後は Linkステータスに「KMX」と明示されます。** §0更新のとおり auth/JOG も実装済みの認識。JOGがまだ灰色の場合は **再接続時の再auth（§2.5）** と `auth_ack.allow` 保持を確認してください。接続が見えない場合は `wsUrl`／hmx-link 稼働を確認。
  - 補助：Linkステータスの**接続フロントエンド行をクリック**すると、そのフロント（KMX）が**実際に購読しているデバイス一覧**を表示します。`vals` が来ない時はここで購読アドレスの書式（H2）を突き合わせて切り分けできます。
- 関連：**ブラウザ/タブレットの読み上げ**も hmx-link 経由（`POST /tts`）でAzure/OpenAI音声に対応（キーはサーバ側env、クライアントへ出さない）。

## 11. 改訂メモ
- 2026-06-23 初版。方式A（KMX 自前 WS 接続）で確定。protocol 正典は `hmx-link_write要求.md §9`。
- 2026-06-25 追記：
  - §7.7 `clientType:"kmx"` を追加（Linkステータスモニタの接続元識別。未指定だと View と誤判定される）。KMX 実装済（subscribe に付与）。
  - §10 **HMX側への確認事項**（読取データ＝位置・動作の受信／ComPostgres→ComHmi 置換の核心）を追加。H1〜H7 を HMX に確認依頼。
  - §10 に **HMX回答**を追記：H1〜H6 はいずれも対応可（一般実PLCデバイスの `vals` 周期返信／キー完全一致／内部IO・実デバイス混在／16bitワード×2合成／主ポール周期＝interval既定200ms／数百点OK）。H7 は `clientType` 実装済＝hmx-link再起動で「KMX」表示。Linkステータスのフロント別購読デバイス表示で書式突合が可能。
  - §0 を現状に更新：KMX 側は §2〜§7 実装済み（auth/JOG/購読/clientType/ランプ/IL）。残課題は読取データ（位置・動作）の `vals` 受信＝§10 H1〜H7。旧記述「KMX は auth/jog を送っていない」は実装前のもので解消。
  - §7.6 インターロック読取：`interlock` 実デバイス**未割付の行は hmx-link が常に 1(許可) を返す**よう変更（未設定＝無条件許可。JOG書込ゲート `_ilOk` と整合）。KMX は従来どおり `vals=1` を「操作可」として扱えばよい（未割付行は灰色にならない）。
  - 高速ポール（JOG/安全＝ランプ/IL別名の短周期読み）の起動条件＝**writer接続** ／ **ランプ・IL別名(IB9800+/IB10000+)の読取専用購読** ／ View側の高速tierパーツ表示、のいずれか。KMX が auth(writer) も別名購読もしない間は高速ポールが起動せず、ランプ/IL応答は主ポール周期側でのみ更新される（Linkステータスで「高速＝停止」と表示）。
