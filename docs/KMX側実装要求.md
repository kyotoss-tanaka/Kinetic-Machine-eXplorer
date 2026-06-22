# KMX（Unity デジタルツイン）側 実装要求

宛先：KMX / Unity 開発者
起案：HMX（hmx-link / HMX View）側
関連：`docs/Unity連携仕様.md`（プロトコル詳細）／`docs/hmx-link_readonly_subscribe要求.md`（read-only 実装）

KMX は hmx-link に **副クライアント（read-only）** として接続し、PLC デバイス値を購読して 3D に反映する。
**HMX 本体（HMI）の PLC 通信を絶対に妨げない**ことが最優先要件。以下の MUST を守ること。

---

## 1. 接続（MUST）
- **M1**：WebSocket で `ws://<hmx-linkのPCのIP>:<port>`（既定 `8765`）へ接続。
- **M2**：受信した `{type:"hello"}` に `{type:"hello_ack"}` を返す。
- **M3**：JSON 1メッセージ＝1フレーム、UTF-8。

## 2. 購読（MUST）— ★通信途切れ防止の最重要事項
- **M4：必ず `readOnly:true` を付けて subscribe する。**
  ```json
  { "type":"subscribe", "readOnly":true, "devices":["D100","D101","M0"], "interval":300 }
  ```
- **M5：`connection` を絶対に送らない。**（read-only でも、実 `protocol` 付き `connection` を送ると hmx-link が PLC ドライバを切り替え＝**HMI の接続を切断**してしまう。）
- **M6**：`subscribed` 応答の `readOnly:true` を確認してから本運用に入る（受理確認）。
- **M7**：監視デバイスを増やすときは、`readOnly:true`＋更新後 `devices` で**再 subscribe**してよい（hmx-link はドライバを再構成せず読取対象 union を更新する）。

> ⚠️ hmx-link 側にも「connection 無しの subscribe では既存ドライバを維持」する防御を入れてあるが、**KMX が実 `connection` を送ればそれでも切り替わる**。M5 は必ず守ること。

## 3. 書き込み（MUST）
- **M8**：read-only クライアントの `write` は hmx-link に**拒否される**（`write_ack {ok:false, msg:"readonly"}`＋監査 `write_denied`）。KMX から `write` は**送らない**こと。
  - 将来モデルから操作を反映したくなった場合は、別途「書込権限つきクライアント」仕様を協議する（認証・監査が必要）。

## 4. 周期・負荷（SHOULD）
- **S1**：`interval` は **200〜500ms 程度**を推奨。**HMI より極端に速い値にしない**（PLC、特に MX Component/GX Simulator は1点ずつ読むため、全体ポール周期が KMX の速い interval に引っ張られて過負荷→タイムアウト→切断の恐れ）。hmx-link 側は `[minInterval, maxInterval]` にクランプする。
- **S2**：購読 `devices` は**必要最小限**に。大量・広範囲は読出ブロックを肥大化させ全体を遅くする。

## 5. 死活・再接続（SHOULD）
- **S3**：`{type:"ping"}` を周期送信（例 2秒）。`{type:"pong"}` で疎通確認（往復時間も取得可）。
- **S4**：切断時は指数バックオフ等で**自動再接続**（HMX View は 2s→×1.5・最大30s）。再接続後は M4〜M6 をやり直す。

## 6. 受信処理（MUST/SHOULD）
- **M9**：`{type:"vals", vals:{addr:val}}` を受信して反映（初回は購読分スナップショット、以降は**変化分のみ**）。
- **S5**：`{type:"status", plc}` を見て PLC 未接続/再接続を UI 反映してよい（任意）。
- **S6**：`{type:"stats"}`・`{type:"project"}`・`{type:"deploy"}` は KMX には不要＝無視。

## 7. 値・デバイスの意味（MUST 理解）
- **M10**：`vals` の値は **「表示値」**（hmx-link がデバイス定義に従い decode・スケーリング後）。生レジスタ値ではない。3Dマッピングでスケーリングが要る場合は HMI 側定義と整合させる。
- **M11**：**内部デバイス（`IW9000`〜/`IB9500`〜 等の HMX 予約領域）は hmx-link 経由では取得できない**（各クライアントでローカル計算される値のため）。KMX が読めるのは**実 PLC デバイスのみ**。
- **S7**：要求デバイスが PLC に存在しない場合、値が来ない/0 のことがある（個別に許容する設計に）。

## 8. 接続確立の前提（MUST 理解）
- **M12**：PLC への接続（ドライバ確立）は **HMI（主クライアント）または hmx-link の `config.json` の `connection`** が行う。**KMX は接続を確立しない**（M5）。主接続が未確立の間は値が来ない/`status:disconnected` になる＝**エラー扱いにせず待機**する。

---

## 受け入れ条件（KMX 実装の合否）
1. KMX 接続・購読・切断のいずれでも、**HMI の PLC 通信が途切れない**。
2. `subscribe` に `readOnly:true` があり、`connection` を送っていない。
3. `write` を送っていない（送っても拒否される）。
4. `interval` が常識的範囲（過負荷を招かない）。
5. 切断後に自動再接続し、復帰後も 1〜4 を満たす。

## チェックリスト（実装時）
- [ ] hello_ack を返す
- [ ] `subscribe { readOnly:true, devices, interval }`（**connection 無し**）
- [ ] `subscribed.readOnly===true` を確認
- [ ] `vals` を受信→3D反映（変化分対応）
- [ ] `ping` 周期送信／`pong` 受信
- [ ] `write` を送らない
- [ ] 自動再接続（バックオフ）
- [ ] 内部デバイス(IW9000+/IB9500+)に依存しない
