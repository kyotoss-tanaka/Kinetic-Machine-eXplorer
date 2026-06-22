# Unity 連携仕様（デジタルツイン向け）

Unity で作成したモデルを HMX に表示し、将来的に PLC のリアルタイムデータと接続してデジタルツイン化するための、**Unity 側開発者向け**インターフェース仕様です。

---

## 1. 全体構成

```
   PLC ──(MX Component / MC-TCP)── hmx-link ──┬── WebSocket ── HMX View（HMI画面）
   実機/GX Simulator3            （配信ハブ・8765） └── WebSocket ── Unity（デジタルツイン）★これから
                                                  └── HTTP ────── タブレット等へ画面/Unityビルド配信
```

- **hmx-link** が PLC デバイス値の **WebSocket ハブ**です。複数クライアントが同時接続でき、各クライアントは「自分が購読したデバイス」の値（変化分）を周期受信します。
- Unity は **「もう一台の WS クライアント」** として hmx-link に接続するのが本命方針（表示方法から独立し堅牢）。
- 連携には2つの面があります:
  1. **表示**：HMX の「WebGL (Unity)」パーツで Unity WebGL ビルドの `index.html` を埋め込み表示（実装済み）。
  2. **データ**：hmx-link の WebSocket で PLC デバイス値を購読／書込（本書の主題）。

---

## 2. 表示（WebGL ビルドの埋め込み）

- HMX 側のパーツ「WebGL (Unity)」に、配信した **`index.html` の URL** を設定すると埋め込み表示されます。
- Unity 側でやること:
  - **WebGL ビルド**を作成（`index.html` / `Build/` / `TemplateData/`）。
  - 出力を **HTTP(S) で配信**（`file:` 不可）。タブレット運用ではタブレットから到達できるアドレス（PC の IP 等）で配信。
- 表示は実機(HMX View)＝webview、ブラウザ/タブレット＝iframe（自動）。全画面・ゲームパッド等の入力は許可済み。

> 注：表示（埋め込み）とデータ連携（WS）は独立です。デジタルツインでは「表示＝WebGLパーツ／データ＝下記 WS」を別々に扱えます。

---

## 3. WebSocket 接続

| 項目 | 値 |
|---|---|
| URL | `ws://<hmx-linkを動かすPCのIP>:<port>` |
| ポート | 既定 **8765**（hmx-link の `config.json` の `port`） |
| ホスト | 既定 `0.0.0.0`（LAN 公開）。Unity からは PC の IP を指定 |
| プロトコル | テキスト（**JSON 1メッセージ＝1フレーム**） |
| 文字コード | UTF-8 |

### ハンドシェイク
1. 接続すると hmx-link が `{"type":"hello","version":<n>}` を送ってくる。
2. クライアントは `{"type":"hello_ack"}` を返す（任意だが推奨）。
3. 続けて **`subscribe`** を送る（→ §5）。これを受けて hmx-link が PLC 周期読み出しを開始し、値配信が始まる。

### 再接続
- HMX View は切断時に指数バックオフ（2s→×1.5、最大30s）で自動再接続。Unity も同様の再接続を推奨。

---

## 4. メッセージ一覧

方向：S→C = サーバー(hmx-link)→クライアント、C→S = クライアント→サーバー。

| 方向 | メッセージ | 用途 |
|---|---|---|
| S→C | `{type:"hello", version}` | 接続時ハンドシェイク |
| C→S | `{type:"hello_ack"}` | hello への応答 |
| C→S | `{type:"subscribe", devices:[...], interval, connection?}` | 購読登録（監視デバイス・周期） |
| S→C | `{type:"subscribed", count}` | 購読受理 |
| S→C | `{type:"vals", vals:{addr:val,...}}` | **周期値配信（変化分のみ）** |
| C→S | `{type:"ping"}` / S→C `{type:"pong"}` | 死活監視・往復時間計測 |
| C→S | `{type:"write", id?, addr, val, user?, role?}` | 書き込み（単一） |
| C→S | `{type:"write", id?, writes:[{addr,val}], user?, role?}` | 書き込み（一括） |
| S→C | `{type:"write_ack", id, ok, msg?}` | 書き込み結果 |
| S→C | `{type:"status", plc, detail}` | PLC接続状態（任意で利用） |
| S→C | `{type:"stats", ...}` | 周期/処理時間の統計（任意で利用） |
| C→S | `{type:"audit", event}` | 監査イベント送信（HMI専用・Unityは通常不要） |

> `project` / `deploy` メッセージは HMI 画面配信用で、Unity は無視してよい。

---

## 5. 購読（subscribe）— ★最重要の注意点

```json
{
  "type": "subscribe",
  "devices": ["D100", "D101", "M0", "M1", "X10"],
  "interval": 200,
  "connection": { ... HMIプロジェクトの接続先設定 ... }
}
```

- `devices`：監視したいデバイスのアドレス配列（§8 のアドレス表記）。
- `interval`：希望配信周期(ms)。hmx-link 側で `[minInterval, maxInterval]` にクランプ。
- hmx-link は **全クライアントの devices を union（和集合）して PLC を読みます**。よって HMX View と Unity が同時接続しても、双方が必要なデバイスを購読できます。各クライアントには **自分が購読したデバイスの変化分だけ**が配信されます。

### ⚠️ `connection` の扱い（PLCドライバ設定）
- `subscribe` の `connection` を受けると hmx-link は **PLC ドライバを構成**します。`connection` の内容が**それまでと変わると、ドライバを切り替え（再接続）します**。
- このため、**Unity が HMI と異なる（または空の）`connection` を送ると、HMI が確立している PLC 接続を巻き込んで切り替えてしまう**おそれがあります。
- **現状の回避策（必須）**：Unity の `subscribe` には **HMI プロジェクトと同一の `connection` 設定**を入れて送ってください（内容が同じなら切替は起きません）。`connection` の元データは HMI プロジェクトの `projectSettings.connections[0]`（host/port/protocol/transport 等）です。これを Unity 側にも共有します。
- **推奨する近い将来の拡張（HMX側で対応予定）**：副クライアント向けに **「読み取り専用 subscribe（ドライバ再構成しない）」** を追加します。これが入れば Unity は `connection` を省略して安全に購読できます。デジタルツイン着手時に最初に実装する項目です。

---

## 6. 値配信（vals）と値の意味

```json
{ "type": "vals", "vals": { "D100": 44.2, "M0": 1, "M1": 0 } }
```

- **変化のあったデバイスのみ**を、クライアントの購読周期で配信（初回は購読分のスナップショットを一括送信）。
- 値は **数値**。ビットデバイスは `0`/`1`、ワードは数値。
- 値は **「表示値」**（hmx-link がデバイス定義に従って decode した後の値。小数桁・スケーリングが定義されている場合はそれが反映されます）。生のPLCレジスタ値そのものではない点に注意。デジタルツインでスケーリングを厳密に扱う場合は、HMI側のデバイス定義（小数桁等）と Unity 側の解釈を合わせてください。

---

## 7. 書き込み（write）

```json
{ "type": "write", "id": 123, "addr": "M0", "val": 1, "user": "admin", "role": "Administrator" }
```
または一括:
```json
{ "type": "write", "id": 124, "writes": [ {"addr":"D100","val":150}, {"addr":"M5","val":1} ], "user": "admin", "role": "Administrator" }
```

- 応答：`{ "type":"write_ack", "id":123, "ok":true }` または `{ "ok":false, "msg":"書き込み禁止: ..." }`。`id` は任意（応答に同じ値が返るので相関に使える）。
- **ホワイトリスト**：hmx-link の `config.json` の書込許可設定に載っているアドレスのみ書込可。許可外は `write_ack ok:false`（理由 `whitelist`）で拒否され、監査ログに `write_denied` が残ります。
- **`user`/`role`**：HMI は操作者IDとロールを付与しています（中央監査ログ用）。Unity からの書込も、可能なら識別子を付けてください（例 `user:"unity"`）。
- **内部デバイス（`IB`/`IW`）は PLC へ書けません**（§9）。

> デジタルツインで「3D上の操作→PLCへ反映」を行う場合のみ write を使用。閲覧専用なら不要です。

---

## 8. デバイスアドレス表記

- 文字列で `<デバイス記号><番号>`。例：`D100`, `M0`, `X10`, `Y20`, `W1F`, `L0` 等（PLC/ドライバの記法に準拠）。
- subscribe の `devices`、vals のキー、write の `addr` すべて同じ表記。

---

## 9. 内部デバイス（HMX 予約領域）— hmx-link 経由では取得不可

HMX は通信診断・認証状態などを内部デバイスに割り当てていますが、**これらは各クライアント（HMX View）内でローカル計算される値であり、hmx-link は関知しません**。したがって **Unity が hmx-link に接続しても、これらの値は配信されません**（取得できるのは実 PLC デバイスのみ）。

| ブロック | 内容 | 備考 |
|---|---|---|
| `IB9000`〜 | 通信状態（接続/エラー等） | クライアントローカル |
| `IW9000`〜`IW9099` | システム情報・時刻・接続先・通信性能・エラー | クライアントローカル |
| `IW9100`〜`IW9119` | メニュー/UI状態（サブメニューID 等） | クライアントローカル |
| `IB9500` / `IW9500`〜 | 認証状態（ログイン/ロール/Feature許可） | クライアントローカル |

- これらは PLC へも書き込まれません（HMI 側で `write` 時に `IB/IW` は弾く実装）。
- Unity がシステム/認証状態を必要とする場合は、**hmx-link に別途公開する仕組みが必要**（将来拡張の検討事項）。

---

## 10. Unity 側 実装チェックリスト

- [ ] **WebSocket クライアント**（Unity WebGL は `System.Net.WebSockets` が使えないため **NativeWebSocket** 等のプラグインを利用）。
- [ ] 接続後、`hello` 受信 → `hello_ack` 返信。
- [ ] **`subscribe` 送信**（必要デバイス配列 ＋ interval ＋ **HMIと同一の `connection`**）。※将来の read-only subscribe が入れば `connection` 省略可。
- [ ] **`vals` 受信** → デバイス→Transform/マテリアル/状態へのマッピングを適用（差分更新）。
- [ ] **`ping` を周期送信**（例 2秒）して死活維持。`pong` で往復時間も取得可。
- [ ] （必要時）**`write` 送信** ＋ `write_ack` 処理。アドレスはホワイトリスト内のみ。
- [ ] 切断時の**自動再接続**（バックオフ）。
- [ ] `status`/`stats`/`project`/`deploy` は不要なら無視。

---

## 11. メッセージ例（最小フロー）

```
S→C  {"type":"hello","version":1}
C→S  {"type":"hello_ack"}
C→S  {"type":"subscribe","devices":["D100","D101","M0"],"interval":200,
      "connection":{"host":"192.168.0.20","port":5511,"protocol":"mc","transport":"tcp"}}
S→C  {"type":"subscribed","count":3}
S→C  {"type":"status","plc":"connected","detail":"..."}
S→C  {"type":"vals","vals":{"D100":44.2,"D101":0,"M0":1}}    ← 初回スナップショット
S→C  {"type":"vals","vals":{"D100":44.8}}                    ← 以降は変化分のみ
C→S  {"type":"ping"}      S→C {"type":"pong"}
C→S  {"type":"write","id":1,"addr":"M0","val":0,"user":"unity"}
S→C  {"type":"write_ack","id":1,"ok":true}
```

### Unity(C#) 擬似コード（NativeWebSocket 例）
```csharp
ws = new WebSocket("ws://192.168.0.10:8765");
ws.OnMessage += bytes => {
    var msg = JObject.Parse(Encoding.UTF8.GetString(bytes));
    switch ((string)msg["type"]) {
        case "hello":     Send(new { type = "hello_ack" });
                          Send(new { type = "subscribe", devices = myDevices, interval = 200, connection = hmiConnection });
                          break;
        case "vals":      foreach (var kv in (JObject)msg["vals"]) ApplyDeviceValue(kv.Key, (double)kv.Value); break;
        case "pong":      /* latency */ break;
        case "write_ack": /* 結果確認 */ break;
    }
};
// 2秒ごとに ping、必要時 write
```

---

## 12. 現状の制約と推奨拡張（HMX 側の TODO）

| 項目 | 現状 | 推奨対応（デジタルツイン着手時） |
|---|---|---|
| 副クライアントの subscribe | `connection` を送るとドライバ再構成される | **read-only subscribe（ドライバ非再構成）**を追加 → Unity は `connection` 省略で安全に購読 |
| 値のスケーリング | 「表示値」で配信（HMI定義依存） | 必要なら raw 値配信オプション、または定義共有 |
| システム/認証デバイス | クライアントローカルで未配信 | 必要なら hmx-link から公開する仕組みを追加 |
| 配信元 | hmx-link は HMI 画面を配信 | 必要なら Unity ビルドも hmx-link から静的配信（1ボックス化） |

> Unity 側はまず **「WS クライアント＋ `vals` 受信＋デバイス→3Dマッピング」** を作り込めば、HMX 側の read-only subscribe 対応と合わせてデジタルツインが成立します。書込（操作の反映）は次段階で追加可能です。

---

**参照（HMX 側実装）**：WS クライアント `hmx-view/src/stores/wsStore.js`、サーバ `hmx-link/server.js`（プロトコル定義は同ファイル冒頭コメント）、表示パーツ `useComponentRenderer.js` の `case 'webgl'`。
