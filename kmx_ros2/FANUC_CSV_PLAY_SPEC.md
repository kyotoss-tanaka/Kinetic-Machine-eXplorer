# FANUC 汎用再生（CSV経路データ）実装仕様

作成: 2026-07-17 / 対象: KMX(Unity製HMI) ＋ FANUC(CRX-30iA 他) / 関連: [[fanuc-recovery-motion]]、`ROBOGUIDE_HANDOFF.md`、`Assets/Scripts/Com/Ros2/FanucLsExporter.cs`

> 目的: KMX が計画した経路を **「経路ごとにプログラム(.LS)を生成」するのではなく**、FANUC 側に置いた **固定の汎用再生プログラム**が **CSVの経路データ（関節角）を読んでその通り再生**する方式にする。
> 主用途は **登録モード（品種別・生産再生）**。生産時は KMX/ROS に依存せず FANUC 単体で再生できるのが狙い（[[fanuc-recovery-motion]] の登録モード）。

---

## 0. 方式決定（A採用）
- **採用 = A: CSVファイル方式**（Karel がCSVを読み → 位置レジスタ `PR[]` → 汎用TPで再生）。
- 却下 = B: ソケット送信（既存DCS socket流用は魅力だが、再生時に KMX接続必須で生産の自立性が低い）。**復帰モード（オンデマンド・KMX常時オンライン）には B も可**だが、本仕様は登録=A に集中。
- 前提（実機で確認済/回答済）: **Karel オプションあり**（現状 DCS を Karel socket で運用中）／**経路データは関節角**。

---

## 1. アーキテクチャ / データフロー
```
KMX(Unity) 計画経路（関節角の点列・ROS2計画）
   │  ① CSV 出力（FanucLsExporter の点列を CSV 化・J2-J3換算）
   ▼
CSV ファイル（品番ごと）      例: P<品番>.CSV
   │  ② 転送（FTP / USB(UD1) / 共有）→ コントローラのストレージ
   ▼
FANUC 汎用再生（固定・検証済み1本）
   │  ③ Karel: CSV読取り → PR[] へ（バッチ/逐次）
   │  ④ TP FORループ: J PR[i] speed% CNT で滑らかに再生
   ▼
実機/ROBOGUIDE で経路再生（★DCSが実時間で安全検証）
```
- **KMX側は「データ(CSV)を出すだけ」**。プログラム書式生成が不要になり、今の `.LS` 生成より単純。
- **生産選択は PNS/RSR**：品番 → 対応CSV を選んで汎用再生を起動（§6）。

---

## 2. CSV フォーマット（v1・関節角）★確定（ROS回答 §68 反映）
- 文字コード ASCII / 改行 CRLF（FANUC 慣習）。区切りはカンマ。
- **コメント行なし**（Karel が `READ` で決定的に読めるように）。**データ行は固定 6 数値**（Karel の `READ f (j1..j6)` or カンマ分割に対応）。
- **1行目 = ヘッダ（整数6個）**、**2行目～ = 点（関節角6個）**。

```
count,group,uframe,utool,speed,cnt
J1,J2,J3,J4,J5,J6
J1,J2,J3,J4,J5,J6
...
```
実例（3点）:
```
3,1,0,1,100,50
0.000,-30.000,-90.000,0.000,-60.000,0.000
10.000,-20.000,-80.000,0.000,-60.000,0.000
20.000,-10.000,-70.000,0.000,-60.000,0.000
```
- ヘッダ: `count`=点数、`group`=動作グループ、`uframe`/`utool`=UF/UT番号、`speed`=速度%(1-100)、`cnt`=CNT値(0-100・0はFINE相当)。速度/CNTは**全点共通**（v1は点別上書きなし＝Karel読取り単純化。将来拡張可）。
- 各点は **J1..J6（度・小数3桁）**。**J3 は FANUC 規約へ換算**（`J3 = ROS_J3 − J2`。`FanucLsExporter` の `j2j3Coupling` と同一）。
- `count` ≤ `$MAXPREGNUM`（既定100）。超える経路は KMX 側で**間引いて**この範囲に収める（Phase1）。無制限は Phase2 のバッチ（§3.1）。

---

## 3. FANUC 側: 汎用再生プログラム（固定・1本を検証）
**役割分担**: Karel＝ファイルI/O＋`PR[]`充填、TP＝動作（先読み/CNTブレンドが堅い）。

### 3.1 Karel `KMX_LOAD.KL`（CSV → PR[]）
1. ファイルを開く（パスは §5。品番はレジスタ経由で filename 組立）。
2. ヘッダ読取り → `R[90]=count, R[91]=group, R[92]=uframe, R[93]=utool, R[94]=speed, R[95]=cnt`（レジスタ番号は例）。
3. `UFRAME_NUM=uframe / UTOOL_NUM=utool` を設定（座標系不一致=実行-251 回避。[[roboguide-eval]] の教訓）。
4. 点を読み、**JOINTPOS として `PR[1..N]` にセット**（関節表現）。
   - **N ≤ PR上限**: 全点を一括ロード（§10で実機のPR最大数を確定）。
   - **N > PR上限 / 不明で大きい**: **リングバッファでバッチ充填**（K点ロード→TPが再生→次のK点、を register フラグで同期）＝**点数無制限**。
5. 完了フラグをレジスタに立てる。

### 3.2 TP `KMX_PLAY.TP`（PR[] を再生）
```
  1:  UFRAME_NUM=R[92] ;
  2:  UTOOL_NUM=R[93] ;
  3:  CALL KMX_LOAD ;               ; Karelで CSV→PR[] 充填
  4:  FOR R[1]=1 TO R[90] ;         ; R[90]=count
  5:    J PR[R[1]] R[94]% CNT R[95] ;
  6:  ENDFOR ;
```
- **CNT** でコントローラ先読みブレンド＝滑らか（1点ずつ同期読み→FINE的停止を回避）。
- バッチ運用時は 4-6 のFORを「バッチ内ループ＋Karel再充填待ち」に拡張。

> ※ 全部 Karel で `MOVE TO PR[i]` でも可だが、動作の先読み/CNTは **TPのFORループが堅い**ので上記分担を推奨。

---

## 4. KMX 側: CSV 出力（`.LS` 生成の置換/併設）
- 既存 `FanucLsExporter` は **関節角の点列 `jointsDegPerPoint` を保持**しているので、**同じ点列を CSV に書き出す出力器を追加**（例 `FanucCsvExporter.Build(...)`）。
- **J2-J3 換算は .LS と同一**（`j2j3Coupling`: 出力J3 = 入力J3 − J2）。速度%・CNT・UF/UT・group もヘッダに反映。
- 出力先は転送しやすい固定フォルダ（例 `C:\KMX-Path\<品番>.CSV`。現行 `.LS` と同様）。
- UI: 計画パネルに「CSV 出力」ボタン（`.LS 出力`の隣）or 出力形式を切替。復帰=その場出力、登録=品番付きで保存。

---

## 5. ファイル転送 / 保管（★実機都合で確定＝§10）
- 候補: **FTP**（FANUCコントローラのFTPサーバへ `MD:` / `UD1:`）、**USB(UD1:)**、**ネットワーク共有**。
- コントローラ側の保管例: `UD1:\KMX\P<品番>.CSV`。Karel は品番レジスタから filename を組立てて開く。
- 生産では「品番CSVを事前配置 → 再生時はFANUCがローカル読取り」＝**KMX/ROS非依存で自走**。

---

## 6. 生産での選択（PNS/RSR）
- **PNS/RSR で品番→再生を起動**。方式例:
  - 品番を GI/レジスタに入れる → `KMX_PLAY` が `R[品番]` から filename を組み立て → 該当CSVを再生。
  - or 品番ごとに薄い選択TP（`RSR0001`→品番セット→`CALL KMX_PLAY`）。
- 復帰モード（オンデマンド）は KMX から直接起動 or 専用PNS。

---

## 7. モード対応
| モード | 生成 | 再生の起点 | 方式 |
|---|---|---|---|
| **登録（生産）** | KMXでオフラインCSV（品番別） | PNS/RSR（生産ライン） | **A: CSV（本仕様）** |
| **復帰（オンデマンド）** | KMXがその場で経路生成 | KMXから起動 | A(CSV即出力)でも可 / 既存DCS socket流用の **B** でも可 |

---

## 8. 安全 / DCS
- **経路生成方式に依らず、DCS が実時間で安全域を検証**（[[dcs-zone-import]]）。CSV再生でも同じ。
- KMX は **DCS内側で計画**しているので、その関節経路をそのまま再生すれば安全域を守る。CNTで角を丸める分の逸脱に注意（密点 or 小CNT）。
- FANUC 側の関節上下限・速度制限も従来通り効く。

---

## 9. 検証（完了条件）
1. KMX で経路 → **CSV 出力**（点列・J2-J3換算・ヘッダ）。
2. CSV を **ROBOGUIDE に転送** → `KMX_PLAY` 実行 → ロボットがKMXプレビューと同じ経路を再生。
3. 実機で同様（DCSが効くこと）。
4. **品番違いのCSVを差し替え → 同じ `KMX_PLAY` で別経路が再生**（プログラム再生成なし）。
5. **PNS/RSR で品番選択 → 対応経路が再生**（生産フロー）。
6. 大点数（PR上限超）で **バッチ再生が途切れず滑らか**（Phase2）。

---

## 10. 未確定 / 確認事項
> ※ 本節は起票時のもの。**大半は ROS/FANUC 回答で解決済み（→ §13 確定事項）**。実機で残る確認だけ §13 末尾(△)にまとめた。
1. **実機の `PR[]` 最大数**（＝一括ロード可能な点数の閾値。超える/大きいならバッチ実装が要る）。
2. **ファイル転送手段**（FTP / USB / 共有）と**コントローラ側の保管パス**。
3. **PNS/RSR の品番→CSV 対応方式**（レジスタ組立 or 選択TP）。
4. KMX 計画経路の**点数の目安**（多ければ KMX 側で間引き or バッチ再生）。
5. **速度/CNT のチューニング**（滑らかさ vs 経路忠実度＝DCS内に収める）。
6. 復帰モードを A(CSV) で統一するか、B(socket) 併用にするか。

---

## 11. 段階プラン
- **P1（最小・N≤PR上限）**: `FanucCsvExporter`（KMX）＋ `KMX_LOAD.KL`/`KMX_PLAY.TP`（一括ロード）→ ROBOGUIDE で1経路再生。
- **P2（大点数）**: リングバッファのバッチ再生（点数無制限）。
- **P3（生産）**: 品番別CSV配置＋PNS/RSR選択＋実機検証。
- **P4（任意）**: 復帰モードの socket(B) 対応。

---

## 12. 参考
- 現行の関節点列/換算: `Assets/Scripts/Com/Ros2/FanucLsExporter.cs`（`jointsDegPerPoint`, `j2j3Coupling`, UFRAME_NUM/UTOOL_NUM）。
- 2モードの位置づけ: [[fanuc-recovery-motion]]。ROBOGUIDE検証の勘所: [[roboguide-eval]]、`ROBOGUIDE_HANDOFF.md`。
- 安全: [[dcs-zone-import]]、`DCS_ZONE_ROS2_LIVE_SPEC.md`。

---

## 13. 確定事項（2026-07-17・ROS/FANUC回答反映）
`FANUC_CSV_PLAY_ROS_QUESTIONS.md` の回答（★ktrans実コンパイル検証済）で実装形が確定:

**確定した実装形**
1. **Karel が CSV 読取り → `PR[]`(joint) 充填**：`CNV_REL_JPOS`(関節角→JOINTPOS) ＋ `SET_VAR $POSREG[grp,idx]` で PR[] へ joint 表現で書込み（★検証済）。**関節角のまま渡せる**。
2. **`PR[]` 最大数 = `$MAXPREGNUM`（既定100）**。→ Phase1 は **KMX側で ≤100 に間引いて一括ロード**。無制限は Phase2 の **Karel↔TP バッチ（リングバッファ・R[]でハンドシェイク）**。
3. **転送 = FTP**。KMX が `UD1:\KMX\P<品番>.CSV` へ push（USB も可）。
4. **再生 = TP FOR ループ** `J PR[R[i]] R[spd]% CNT R[cnt]`（Karel は PR[] 充填担当）。冒頭で `UFRAME_NUM`/`UTOOL_NUM` を CSV ヘッダ値に設定。速度50〜100%・CNT50〜100。
5. **品番選択**：PNS より **品番を R[]/GI に入れ、Karel が `P<品番>.CSV` を組み立て**が素直（PNS はTP番号選択でCSV名に不向き）。

**実装分担**
- **ROS/FANUC側**：再生 Karel(`KMX_LOAD`) ＋ TP(`KMX_PLAY`) の雛形を用意（ktrans検証）。DCS常駐Karel(`%NOPAUSE`)とは**別タスクで共存可**。
- **KMX(Unity)側**：**本CSV仕様(§2)の提供**＋**CSV出力器 `FanucCsvExporter`**（`FanucLsExporter` の関節点列を CSV 化・J2-J3換算・≤100 間引き）＋UIの「CSV出力」。（＋FTP push は §5・実機のFTP有効化後）。

**モード方針**：**登録=A(CSV)** 確定。**復帰=B(socket)** 推奨（既存DCS socket流用・低レイテンシ）＝機能追加時に別途。

**実機で確認する残(△)**：`$MAXPREGNUM` 実値／FTPサーバ有効化／Karel `READ` のカンマ区切り挙動（ダメなら行READ＋自前分割）／CRX協働の速度上限／PNS/RSR/UOP 設定。

---

## 14. Unity 実装反映（2026-07-17・`FANUC_CSV_PLAY_UNITY_REQUEST.md` 対応）
ROS/FANUC 側の E2E 検証完了を受け、Unity 側で命名と出力先設定化を実装:

- **命名 `P<品番>_<パス番号>.CSV`**（例 `P1234_2.CSV`）。**品番=R[89] / パス番号=R[88]**（**パス0=復帰**・省略しない・整数）。計画パネルに **品番/パス番号 の入力欄**を追加（`ComRos2PlanPanel`・既定 品番1/パス0）。
- **出力先フォルダを設定化**（ハードコード禁止）：`Ros2Info.json` の **`csvOutputDir`**。ROBOGUIDE は `<ワークセルルート>\Robot_1\UD1\KMX`、実機は USB の `UD1\KMX` 等を指定。KMX は `<csvOutputDir>\P<品番>_<パス番号>.CSV` へ書き（サブフォルダ自動作成）。FANUC側は共通で `UD1:\KMX\...` を読む。
- **実装ファイル**: `Assets/Scripts/Com/Ros2/FanucCsvExporter.cs`（CSV生成）、`ComRos2PathPlanner.TryBuildCurrentCsv`（軌道→CSV）、`ComRos2PlanPanel`（「FANUC CSV 出力」ボタン＋品番/パス入力＋`csvOutputDir` 書込）、`ComRos2.Ros2Setting.csvOutputDir`。
- **残（後日）**: FTP push モード（今はフォルダ/USB書込のみ。実機FTP有効化後に追加）。大点数バッチ（Phase2）。
- 分担・レジストリ規約・実行フローは `FANUC_CSV_PLAY_UNITY_REQUEST.md` を正とする。
