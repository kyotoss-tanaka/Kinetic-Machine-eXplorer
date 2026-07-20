# 【ROS/FANUC側への質問票】CSV経路データの汎用再生

**方向**: KMX(Unity)側 → ROS/FANUC側（実機コントローラ・Karel・ROBOGUIDE を扱う側）への確認。
**作成**: 2026-07-17 / 前提資料: `FANUC_CSV_PLAY_SPEC.md`（本方式の全体設計）。

> 目的: KMX が計画した経路を「プログラム生成」ではなく、**FANUC側の固定・汎用プログラムが CSV(関節角)を読んで再生**する方式（方式A）を実装したい。実装形（一括 or バッチ／TP or Karel motion／転送手段）を確定するため、以下を ROS/FANUC側で確認してほしい。各問に「なぜ聞くか＝KMX側の分岐」を添えた。

---

## 1. Karel の能力（ファイルI/O・動作）
- **Q1-1. Karel でコントローラ上のファイル（CSV/テキスト）を OPEN/READ できるか？** どのストレージが使えるか（`MD:` `MC:` `UD1:`(USB) `FR:` 共有 等）。
  - なぜ: 方式Aの根幹。読めないなら方式変更（socket B）検討。
- **Q1-2. Karel からの動作（MOVE）は可能か？** それとも動作は TP に限定され、Karel は `PR[]` 充填のみか。
  - なぜ: 「Karel充填＋TP再生」構成にするか「Karel直接MOVE」にするかが決まる（本命は前者）。
- **Q1-3. `PR[]`（位置レジスタ）に JOINTPOS（関節表現）をセットできるか？** Karel から関節角→`PR[]`(joint) 書込み可否。
  - なぜ: KMX は**関節角**で渡す（IK曖昧さ回避）。cartesianしか無理なら方式再考。

## 2. 位置レジスタ / 点数
- **Q2-1. 実機 CRX-30iA の `PR[]` 最大数は？**（例 100/200/…）
  - なぜ: **N ≤ 上限 → 一括ロード（簡単）／超える → バッチ(リングバッファ)実装**の分岐。
- **Q2-2. KMX 側で点数を間引いてよいか？（例 上限に収める）** それとも全点忠実が必須か。
  - なぜ: 一括で収めるなら実装が最小。忠実必須なら大点数バッチ前提。

## 3. 再生（動作・滑らかさ）
- **Q3-1. TP の FORループ `J PR[R[i]] speed% CNTn` で、`PR[]` を順に滑らかに（先読みブレンド）再生できるか？** CRX(協働)特有の制限は？
  - なぜ: 滑らかさ＝CNT先読みが効くか。CRXの速度/協働制限があれば速度設計に反映。
- **Q3-2. `UFRAME_NUM`/`UTOOL_NUM` を再生プログラム冒頭で設定する運用でよいか？**（`.LS` で実行-251回避に有効だった手・[[roboguide-eval]]）
  - なぜ: CSVヘッダの UF/UT をそのまま設定する設計。
- **Q3-3. 速度/CNT の推奨値は？**（DCS内に収めつつ滑らかにしたい）
  - なぜ: CSVヘッダ既定に入れる。

## 4. ファイル転送 / 保管
- **Q4-1. KMX が CSV をコントローラへ置く手段は？** FTP（FANUCのFTPサーバ）/ USB(UD1) / ネットワーク共有 のどれが使えるか。
  - なぜ: KMX側の出力先・転送実装が決まる。
- **Q4-2. コントローラ側の保管パスの規約は？**（例 `UD1:\KMX\P<品番>.CSV`）
  - なぜ: Karel が開くパス＆filename 組立ルール。

## 5. 生産での選択（PNS/RSR）
- **Q5-1. PNS/RSR は利用可能・設定済みか？** 生産で品番→再生を起動する方式の希望は？
  - なぜ: 「品番レジスタ→filename組立」か「品番別選択TP→CALL」かを決める。
- **Q5-2. 品番の受け渡し（GI / レジスタ / PNS番号）は何を使う想定か？**
  - なぜ: Karel が品番→CSV を引く方法。

## 6. 大点数（点数無制限化）
- **Q6-1. 点数が `PR[]` 上限を超える場合、「Karelがバッチ充填 ↔ TPが再生」の producer-consumer（レジスタでフラグ同期）は実現可能か？** 推奨パターンは？
  - なぜ: Phase2（点数無制限）の実装可否・形。

## 7. 既存 Karel(DCS) との共存
- **Q7-1. 現在 DCS用に常駐している Karel socket と、この再生用 Karel/TP は共存できるか？**（別プログラム/別タスクで良いか、資源競合は無いか）
  - なぜ: DCS常駐(%NOPAUSE)を止めずに再生を足せるか。
- **Q7-2. 再生用 Karel/TP は ROS側で用意してもらえるか？** それとも KMX側で雛形を出すか（KMXはCSV仕様を提供）。
  - なぜ: 実装分担の確認。

## 8. モード方針（任意）
- **Q8-1. 復帰モード（オンデマンド）も CSV(A) で統一するか、既存 DCS socket を流用した socket送信(B) にするか？**
  - なぜ: 復帰の実装方式。登録=A は確定、復帰は選択。

---

## 参考（KMX側で確定済み・変更なし）
- データは**関節角**（`J1..J6` 度）。**J3 は FANUC 規約へ換算**（`J3 = ROS_J3 − J2`。既存 `.LS` の `FanucLsExporter.j2j3Coupling` と同一）。
- CSV フォーマット案は `FANUC_CSV_PLAY_SPEC.md` §2。
- 安全は方式に依らず **DCS が実時間検証**（`DCS_ZONE_ROS2_LIVE_SPEC.md`）。
- **最優先の回答**: Q1-1(Karelファイル読取り) / Q1-3(PR jointセット) / Q2-1(PR最大数) / Q4-1(転送手段)。この4つで実装形（一括 vs バッチ・転送）が決まる。

---

# 回答（ROS/FANUC側・2026-07-17）
※ ★=ktrans(V9.40-1) で実コンパイル検証済み。△=実機/ROBOGUIDE で最終確認が要る項目。

## ★最優先4問（実装形が確定）
- **Q1-1（CSV読取り）→ 可**。★Karel で `OPEN FILE f ('RO','UD1:P0001.CSV')` → `READ f (line::254)`（文字列＝ヘッダ）／`READ f (j1,j2,j3,j4,j5,j6)`（数値＝関節角）がコンパイル通過。ストレージは **`UD1:`(USB)／`MD:`(RAM)／`MC:`(メモリカード)／`FR:`(FROM)** が使える（DCS で `UD1:` は実在確認済み）。※Karel の `READ` はカンマ区切りをそのまま数値6個で読めるか要実機確認(△)＝ダメなら1行 `READ ...::n` で受けて `,` で自前分割（DCS reader と同流儀）に切替。
- **Q1-3（PR[]へ関節セット）→ 可**。★`CNV_REL_JPOS(joint_array, jp, status)` で関節角配列→`JOINTPOS`、`SET_VAR(ent,'*SYSTEM*','$POSREG[grp,idx]', jp, status)` で **PR[] へ joint 表現で書込み**（両方コンパイル通過）。＝**関節角のまま渡せる**（IK 曖昧さ回避OK・KMX 方針と一致）。※`SET_POS_REG`/`SET_JPOS_REG` built-in は ktrans-basic 辞書に無い（実機フル support にはある）が、上記 `$POSREG` SET_VAR 経路は可搬で確実。
- **Q2-1（PR[]最大数）→ `$MAXPREGNUM` で決まる。既定 100（R-30iB Plus/CRX 系）**(△実機で `$MAXPREGNUM` を確認)。コントロールドスタートで増やせる（機種/メモリ次第）。**推奨：軌道は 100 点超が普通なので、初手は「間引いて ≤上限」で一括ロード、Phase2 でバッチ（Q6）**。
- **Q4-1（転送手段）→ FTP と USB が使える**。プロトコル一覧に **FTP** あり（FANUC FTP サーバ・△サーバ有効化を確認）＝**KMX から programmatic に push できる本命**。**USB=`UD1:`** も可（DCS で実在確認済み）。ネットワーク共有もFTP経由で実質可。**推奨：FTP で `UD1:\KMX\P<品番>.CSV` に置く**。

## その他
- **Q1-2（Karel MOVE 可否）**：Karel 動作は可能（motion group をロックすれば `MOVE TO`）。ただし**滑らか再生は TP の `J PR[] CNTn` 先読みブレンドが有利**。→ **本命どおり「Karel が PR[] 充填＋TP が再生」構成を推奨**。
- **Q2-2（間引き可否）**：KMX 側で間引いて上限に収めてよい（Phase1 最小実装）。全点忠実が要るなら Q6 のバッチ前提。
- **Q3-1（TP FOR ループ滑らか再生）→ 可**。`FOR i=1 TO n: J PR[R[i]] R[spd]% CNT R[cnt] ENDFOR`（間接 PR[R[i]]＋CNT 先読みブレンド）は標準。CRX(協働)は**協働速度監視/DCS 速度制限**が効くので、速度は控えめから。
- **Q3-2（UF/UT を冒頭設定）→ 推奨**。再生 TP/Karel 冒頭で `UFRAME_NUM`/`UTOOL_NUM` を CSV ヘッダ値に設定（実行-251 回避に有効・[[roboguide-eval]] と同手）。
- **Q3-3（速度/CNT 推奨）**：関節 J 移動で **速度 50〜100%（DCS/協働制限内）・CNT50〜CNT100**（滑らか）。CSV ヘッダ既定に入れ、実機で詰める(△)。
- **Q5-1/5-2（PNS/RSR・品番受け渡し）**：CSV 方式なら **PNS より「品番レジスタ(R[] or GI)→Karel が filename 組立」が素直**（PNS は TP 番号選択で CSV 名に不向き）。PNS/RSR 自体は UOP 設定次第で利用可(△)。推奨：**品番を R[] に入れ、Karel が `P<品番>.CSV` を開く**。
- **Q6-1（大点数バッチ）→ 実現可能**。**Karel(producer) が PR[] リングバッファを充填 ↔ TP(consumer) が再生、R[] フラグでハンドシェイク**（書込み済み点数 vs 再生済み点数）。標準パターン。Phase2 で。
- **Q7-1（DCS Karel と共存）→ 可**。DCS socket 常駐 Karel(`%NOPAUSE`) と再生 Karel/TP は**別プログラム/別タスク**で共存可（別 socket/別ファイル・資源競合なし。FANUC は複数タスク同時実行可）。DCS 常駐は止めなくてよい。
- **Q7-2（実装分担）**：**ROS/FANUC 側（私）で再生 Karel/TP の雛形を用意可能**（kmx_dcs_srv と同様に ktrans 検証して出す）。KMX は CSV 仕様（`FANUC_CSV_PLAY_SPEC.md` §2）を提供。
- **Q8-1（復帰モード A or B）**：登録=A(CSV) 確定。復帰(オンデマンド)は **既存 DCS socket を持つので B(socket 送信) が低レイテンシで有利**。ただし A に統一すると機構が1本化。→ **推奨：復帰は B(socket)**（速い・既存資産流用）、登録は A(CSV)。最終判断は KMX 側で。

## 実装形の確定（4問回答より）
1. **一括 or バッチ**：まず **間引いて ≤`$MAXPREGNUM`(既定100) で一括ロード**（Phase1）。超える/忠実要求は **Karel↔TP バッチ**（Phase2・Q6）。
2. **関節の渡し方**：**関節角 → `CNV_REL_JPOS` → `$POSREG` SET_VAR で PR[](joint)**。
3. **転送**：**FTP で `UD1:\KMX\P<品番>.CSV`**。
4. **再生**：**TP FOR ループ `J PR[R[i]] 速度% CNTn`**（Karel は PR[] 充填担当）。冒頭で UF/UT 設定。
5. **雛形**：私が再生 Karel/TP のたたき台を出す（CSV 仕様を確定後）。

## 実機で確認したい残(△)
`$MAXPREGNUM` の実値／FTP サーバ有効化／Karel `READ` のカンマ区切り挙動／CRX 協働の速度上限／PNS/RSR/UOP 設定。
