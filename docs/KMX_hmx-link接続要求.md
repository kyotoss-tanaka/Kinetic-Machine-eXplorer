# KMX → hmx-link 接続要求（手動操作 JOG）

KMX（Unity / WebGL）から HMX のバックエンド **hmx-link** へ手動操作（JOG）を送るための通信仕様。
方式は **(A) KMX が自前の WebSocket クライアントで hmx-link に直接接続**する（View 経由の中継はしない）。

プロトコルの正典は `docs/hmx-link_write要求.md §9`。本書はその中で **KMX 側が実装すべき事項**を抜き出したもの。
動作確認済みの最小リファレンス実装：本リポジトリの `jog-test.js`（Node）。**これと同じ手順を踏めば動きます**。

---

## 0. 現状（重要）

- hmx-link 側・HMX 側は**実装済みで動作確認済み**（`jog dev:IB9608 → 実デバイス M2000` の ON/OFF を確認済み）。
- 現在 **KMX は hmx-link へ auth も jog も送っていない**（監査ログに痕跡なし）。そのため KMX 自身のガードが「writer未認証 / allow外」を表示している。
- → **本書の 2〜4 を KMX に実装すれば解決**する。HMX 側の追加実装は不要。

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

## 5. インターロック（任意・HMX 側設定）

- HMX 側でマップ行にインターロック条件を設定した場合、条件成立時のみ JOG 許可（不成立＝`jog_ack ok:false msg:"mode"`）。
- インターロック判定は hmx-link のポーリングキャッシュの値で行う。**そのデバイスを誰か（View/HMI）が購読していないと「不明＝安全側で拒否」**になる。
  - 通常は HMI がその画面でインターロックデバイスを表示・監視していれば自動で巡回される。
  - KMX 側で購読したい場合は読取専用購読 `{"type":"subscribe","readOnly":true,"interval":500,"devices":["<IL>"]}` を送ってもよい（任意）。

## 6. UI ガード（KMX 側の操作可否表示）

「操作可」とする条件：
1. `auth_ack ok:true` を受領（writer 確定）。
2. 対象軸の**内部IOが `allow` に含まれる**こと。
   - 含まれない＝HMX 側で実デバイス未割付。「操作不可（未割付）」表示でよい。
   - **実デバイス名で `allow` 判定しないこと**（allow は内部IOのリスト）。

## 7. 内部IO の取り決め（KMX ⇔ HMX）

- 内部IO は **KMX の `ManualOpInfo.json`** がユニットの動作ごとに定義（KMX が採番）。1動作につき2つ:
  - **JOG (write)**：`IB9600〜9799`（200点）。KMX が `jog.dev` として ON/OFF を書く。
  - **ランプ (read)**：`IB9800〜9999`（= JOG内部IO + 200）。**§7.5 参照**。
- HMX（Studio）はそれを取り込み、各内部IO に**この機械の実デバイスを割付 → 配信（通信→Viewへ送信）**する。
- 割付済みの内部IO のみ `allow` に載る＝操作可能になる（allow は JOG 内部IO の一覧）。
- KMX は **`ManualOpInfo.json` の内部IO をそのまま `jog.dev` / ランプ購読に使う**（HMX と同じキー体系）。

## 7.5 ランプ（PLCのボタン認識返し）— 追加要求

ボタンを **押した瞬間に光らせるのではなく、PLC がボタン操作を認識したことを受け取ってから光らせたい**（実機の動作確認用）。そのための「ランプデバイス」を追加する。

- KMX は各動作に **ランプ用の読取内部IO**（`ManualOpInfo.json` の `lamp` = JOG内部IO + 200、例 `jog.dev=IB9608 → lamp=IB9808`）を持つ。
- **HMX への要求**：
  1. 各ランプ内部IO（`IB9800〜9999`）に、**PLC がボタン操作を認識した時に ON になる実デバイス**（PLC側の受付/ランプ信号）を `manualOpMap` で割り付ける。
  2. KMX が **読取専用購読**（§5 と同形式 `{"type":"subscribe","readOnly":true,"interval":...,"devices":["IB9808", …]}`）でランプ内部IOを購読したら、**その（割付先実デバイスの）値を `vals` で内部IOキーのまま返す**こと（例 `{"type":"vals","vals":{"IB9808":1}}`）。
  3. JOG (write) とは独立。ランプは read のみ（KMX は書かない）。
- **KMX の挙動**：ランプ内部IOを購読し、`vals` の値が ON ならボタンを点灯。押下したがランプがまだ OFF の間は「PLC確認待ち（くすんだ朱）」、ランプ ON で点灯（朱フィル）。`lamp` 未定義の動作は従来どおり押下即点灯。
- インターロック（§5）と同様、ランプ実デバイスは hmx-link のポーリングキャッシュ値で返す。割付が無い／値が来ない場合、KMX 側はボタンが点灯しない（押下中はPLC確認待ち表示のまま）。

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

## 10. 改訂メモ
- 2026-06-23 初版。方式A（KMX 自前 WS 接続）で確定。protocol 正典は `hmx-link_write要求.md §9`。
