# 【ROS2側への要求】DCS 安全ゾーンの service 応答 / topic 再配信

**方向**：**Unity(KMX)側 → ROS2側** への依頼（前 `DCS_ZONE_UNITY_SPEC.md` の逆方向）。
**作成**：2026-07-16 / 関連：`DCS_ZONE_ROS2_LIVE_SPEC.md`（全体設計）、`DCS_ZONE_UNITY_SPEC.md`（ROS→Unity契約）。

---

## 0. 現状（Unity 側で確認済み・2026-07-16）
Unity 側 P2-3（受信アダプタ `RosSafetyZoneSource`）実装済み・メッセージ生成済み。実機接続テストの結果:

- **ROS2 接続 OK**（計画パネル「ROS2 ●稼働・接続」、endpoint :10000 疎通、経路計画/障害物は動作）。
- **KMX_ROS2 有効**（DCSのROSコードはコンパイル済み）。
- **★ service `/kmx/get_safety_zones` が Unity から呼ぶと 3秒で無応答（タイムアウト）**。
  - Unity ログ: `[RosSafetyZoneSource] GetSafetyZones 呼び出し… → 応答なし(タイムアウト)`。
- topic `/kmx/safety_zones` は「live 配信中」との情報（ROS側申告）。

**＝ 接続・Unity実装は問題なし。DCS を出す唯一の欠けは「service が応答しない」こと。**

> ★重要（挙動変更）：Unity 側は **JSONフォールバックを廃止**しました。**ROSからゾーンが取れなければ箱は消えます**（古い値を出さない）。よって **ROS が確実に応答すること**が表示の必須条件になりました。

---

## 1. 要求（どちらか成立で可。R1 推奨）

### R1（推奨）：service `/kmx/get_safety_zones` が Unity 呼び出しに応答する
- Unity は `ROSConnection.SendServiceMessage<GetSafetyZonesResponse>("/kmx/get_safety_zones", {robot_id:""}, cb)` を **「DCS再読込」ボタン・起動時・F5** で呼ぶ。
- 期待：`ok=true` ＋ `zones`（SafetyZones）を **3秒以内に**返す。
- 現状これが無応答 → **要因を切り分けて解消**（§2 の確認手順）。

### R2（代替/併用）：latched topic `/kmx/safety_zones` を **poll_sec>0 で定期再配信**
- Unity は起動時に `/kmx/safety_zones` を購読するよう実装済み（service 失敗時のフォールバックにも使用）。
- ただし **ROS-TCP-Connector の購読は transient_local(latched) を取りこぼす**（購読前に配信された latch は受けられない）ため、**`kmx_dcs_reader` の `poll_sec` を >0（例 2.0）**にして定期再配信してほしい。
  - そうすれば購読中の Unity が再配信を受信し、「DCS再読込」で最新に更新できる。
- 起動: `ros2 launch kmx_planner kmx_bringup.launch.py ... poll_sec:=2.0`（既定0＝ボタン方式前提）。

---

## 2. 切り分け手順（ROS側で実施してほしい）
```bash
# (a) service が居るか
ros2 service list | grep safety
#   → 無い: kmx_dcs_reader が service サーバを立てていない（topic だけ）。service を追加。

# (b) service が ROS 内で応答するか（Unity 抜きで検証）
ros2 service call /kmx/get_safety_zones kmx_msgs/srv/GetSafetyZones "{robot_id: ''}"
#   → 応答しない/例外: reader 側の Karel 読取り or service 実装の問題。
#   → ok=true で zones が返る: ROS 内はOK → (c) endpoint 経路を疑う。

# (c) ros_tcp_endpoint が Unity→ROS の service を通すか
#   ・endpoint のバージョン/起動確認（default_server_endpoint）。
#   ・Unity からの service 呼びが endpoint に到達しているか（endpoint ログ）。
#   ・(b)は通るのに Unity だけ無応答 → endpoint のサービス対応（版/設定）が原因。

# (d) topic は出ているか / レート
ros2 topic echo /kmx/safety_zones --once
ros2 topic hz /kmx/safety_zones          # poll_sec>0 なら周期が出る
```

**最有力**：`ros2 service call`(b) は通るのに Unity(a→呼び)が無応答 → **endpoint がサービスを中継していない**。この場合は R2（topic + poll_sec>0）が近道。

---

## 3. メッセージ契約（不変・再掲）
`DCS_ZONE_UNITY_SPEC.md` §1 のまま。Unity 生成済み・フィールド一致確認済み。
- `SafetyZone`: `id / enabled / inside_allowed / min_mm[3] / max_mm[3]`（mm・World/base）。
- `SafetyZones`: `robot_id / frame / unit / zones[]`。
- `GetSafetyZones.srv`: req `robot_id` → resp `ok / message / zones`。
- 単位 mm・フレーム World(UF0)・素の値のみ（mm→m/軸写像/arm1原点は Unity 側）。色は `inside_allowed`（false=赤/keep-out・true=緑）。

---

## 4. 完了条件（Unity 側で確認する）
- 「DCS再読込」→ Unity ログに **`受信(service) … max_mm=[…]`**（R1）または **`受信(topic) … max_mm=[…]`**（R2）が出る。
- 箱が **ROBOGUIDE の現在値**（例 Z上限を 500 にしたら Z=500）で表示・更新される。
- ROBOGUIDE で DCS を変更 → 再読込 → **転記なしで更新**。
- ROS を止める/応答しない → **箱が消える**（JSONフォールバック廃止のため。これは仕様どおり）。

---

## 5. 補足
- Unity 側は R1/R2 どちらでも動くように実装済み（service 優先→topicキャッシュ→どちらも無ければ消去）。**ROS側は R1 を直すのが本筋**、すぐ動かすなら **R2（poll_sec:=2.0）**。
- robot_id は単機 `""` で可（Unity はレジストリの唯一の6軸ロボへ結線）。
- 診断ログ（`[RosSafetyZoneSource]`, `[SafetyZone]`）は解決後に削除予定。

---

## 6. ★ROS2側 対応結果（2026-07-16）
### 切り分け実施（§2）
```
(a) ros2 service list | grep safety   → /kmx/get_safety_zones  ✔ 存在
(b) ros2 service call /kmx/get_safety_zones …
      → ok=True, "1 zone(s)", KMX_TEST min[300,-300,0] max[900,300,500]  ✔ ROS内は正常応答
(d) ros2 topic hz /kmx/safety_zones   → 周期publishなし（poll_sec=0＝latched 1発のみ）
```
**結論**：**ROS のサービスは正常**（`ros2 service call` 成功）。**`ros2 service call` は通るのに Unity だけ無応答 ＝ ros_tcp_endpoint がこの service を Unity へ中継していない**（§2 最有力ケース）。R1 は endpoint/ROS-TCP-Connector の service relay 統合課題。

### 採用＝R2（即効・実施済み）
- **`kmx_dcs_reader` を `poll_sec=2.0` で運用**（`kmx_bringup.launch.py` の既定に組込み＝launch arg `dcs_poll_sec` 既定 2.0）。
  - 2秒ごとに Karel 再読込→`/kmx/safety_zones` 再配信。**Unity は topic 購読で自動更新**（latched 取りこぼしも解消）。
  - ROBOGUIDE/実機で DCS を変更 → **最大2秒で Unity に反映**。
- 検証（ROS側）：`ros2 topic hz /kmx/safety_zones` が約 0.5Hz（2秒周期）で出ることを確認。
- **追補（2026-07-17）**：Karel サーバは1接続ごと `MSG_DISCO`+`DELAY` で一瞬 listen を閉じる（フラップ）ため、poll がその窓に当たると接続拒否で**間欠更新**になっていた。→ `kmx_dcs_reader` に**接続リトライ `read_retries`(既定6・0.25s間隔)** を追加し、**2秒毎に安定配信**を確認（14秒で7件）。poll は**値変化時のみログ**（`DCS 変化検知 → publish`）で可視化。※「reader が -9 で落ちる」の -9 は OOM/クラッシュでなく、調査中の手動 `pkill -9` が原因（reader 自体は正常）。

### R1（残課題・後日）
- endpoint の service relay を成立させる（version/設定、または ROS-TCP-Connector の service 登録経路）。R2 で運用は成立するため優先度低。直れば Unity の「DCS再読込」service 呼びがそのまま通る。

### Unity 側への注意（R2 運用時）
- **JSONフォールバック廃止＋poll 配信**なので、**ROS/Karel を止めると 2秒以内に箱が消える**（＝仕様どおり・古い値を出さない）。Karel は `%NOPAUSE` で常駐継続させること。
- 受信のたび **同一 id も全置換で再描画**（§5 のとおり）。
