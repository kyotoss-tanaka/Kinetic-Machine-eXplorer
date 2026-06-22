# hmx-link 拡張要求：read-only subscribe（connection 不要の購読）

宛先：hmx-link / HMX View 開発者
起案：Unity（デジタルツイン）側
関連：`docs/Unity連携仕様.md` §5・§12

---

## 1. 背景・課題

Unity（デジタルツイン）は hmx-link に **副クライアント**として接続し、PLC デバイス値を購読する。現状の `subscribe` 仕様には次の問題がある（仕様 §5）：

- `subscribe` の **`connection` を受けると hmx-link は PLC ドライバを構成/再構成**する。
- Unity が `connection` を **省略すると、Unity が購読したデバイスが PLC 読み取り対象に入らず**、（HMI が読んでいる範囲しか）値が届かない。
- 一方 Unity が `connection` を **送ると、HMI と異なる/誤った値だと HMI 側の PLC 接続（ドライバ）を巻き込んで切り替えてしまう**。
- 回避策として「Unity も HMI と同一の connection を送る」運用は、**設定の二重管理**になり事故りやすい。
- **【実機確認】`connection` を空にしていても、Unity の接続/切断/再subscribe（デバイス union の増減）だけで hmx-link が PLC ドライバを再構成し、HMI⇔PLC 通信が一時切断される。** 例: Unity 側で F5 リロード（WS 切断→再接続）すると HMI の通信が途切れる。＝**副クライアントの接続・切断・購読変更の存在自体がドライバを揺らしてはならない**（これが本要求の最重要点）。

## 2. 目的

副クライアント（Unity 等）が **`connection` を持たずに、安全に**デバイス値を購読できるようにする。
**既存（主クライアント=HMI）の PLC 接続・ドライバを一切変更しない**こと。

## 3. プロトコル拡張

`subscribe` に **`readOnly` フラグ**を追加（`connection` は省略可）。

```json
{ "type": "subscribe", "readOnly": true,
  "devices": ["D100","D101","D12244","D12245","M0"], "interval": 200 }
```

- `readOnly`(bool, 省略時 false)：true のとき「読み取り専用購読」。
- `readOnly:true` の場合 `connection` は省略してよい（送られても無視する＝後述）。
- `subscribed` 応答は従来どおり（`{type:"subscribed", count}`）。

## 4. hmx-link 側の動作要件

| # | 要件 |
|---|---|
| R1 | **ドライバ非再構成（最重要）**：read-only クライアントの **接続・切断・subscribe（デバイス union の増減）いずれの操作でも**、PLC ドライバ（接続）を**新規構成・再構成・リセット・中断しない**。＝副クライアントの出入りが主接続(HMI⇔PLC)に一切影響しないこと。 |
| R2 | **connection 無視**：`readOnly:true` に `connection` が含まれていても**無視**する（主接続に一切影響させない＝誤設定で HMI を巻き込まない）。 |
| R3 | **読み取りデバイスの追加**：read-only クライアントの `devices` を、**既存ドライバの読み取り union に追加**してポーリングする（＝接続はそのまま、スキャン対象デバイスだけ拡張）。これにより HMI が読んでいないデバイスも読まれる。 |
| R4 | **配信**：read-only クライアントへ、購読 devices の **vals（初回スナップショット＋以降は変化分）** を従来の subscribe と同様に配信。 |
| R5 | **write 拒否**：read-only クライアントからの `write` は拒否（`write_ack {ok:false, msg:"readonly"}`）。監査ログに `write_denied(readonly)` を残す。 |
| R6 | **切断時の解放**：read-only クライアント切断時、そのクライアント専用デバイスを union から外す（他クライアントが要求していなければポーリング対象から除外）。ドライバ自体は維持。 |
| R7 | **接続未確立時**：主接続（ドライバ）が未確立の状態で read-only subscribe を受けたら、**エラーにせず待機**。ドライバ確立後に読み開始。状態は `status {plc:"disconnected"}` 等で通知してよい。 |
| R8 | **再subscribe**：read-only クライアントが devices を増やして再 subscribe しても、ドライバ再構成は起こさず union を更新するのみ（Unity は読まれたタグを順次追加購読するため、再 subscribe が複数回発生する）。 |

## 5. 受け入れ条件（Acceptance）

1. Unity が `connection` 無し＋`readOnly:true` で subscribe しても、**HMI の PLC 接続は切断/切替されない**。
2. **Unity の接続・切断・再subscribe（F5 リロード＝WS 切断→再接続 を含む）を繰り返しても、HMI⇔PLC 通信が途切れない。**
3. Unity が購読したデバイス（**HMI が購読していないデバイスを含む**）の vals が Unity に届く。
4. read-only クライアントの write が拒否される。
5. read-only クライアント切断後、そのデバイス購読が解放される（他が要求していなければ）。

## 6. エッジケース／確認事項

- **read-only クライアントのみ**（主接続 HMI 無し）の場合の方針：ドライバを作らない（待機）か、別途主接続を要する設計か。要決定。
- read-only が要求したデバイスが**主接続の PLC に存在しない**場合のエラー扱い（個別 device error／無視）。
- **主クライアント(HMI)が切断**してドライバが消えた場合、残った read-only クライアントの扱い（待機／配信停止）。
- 複数 read-only クライアントの union 管理。

## 7. 互換性

- `readOnly` 未指定 or false の `subscribe`（＝主クライアント/HMI、connection あり）は**従来どおり**。
- 既存クライアント（HMI View）は無改修で動作すること。

## 8. Unity 側の対応（本拡張後）

- ComHmi の `subscribe` に **`readOnly:true` を付与、`connection` を省略**する。
- `HmxLink.json` の `connection` 設定が不要になる（設定の二重管理が解消）。
- ＝この拡張が入れば、Unity 側はプラットフォーム判定だけで安全にデジタルツイン購読ができる。
