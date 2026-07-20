# KMX 汎用再生（CSV→PR[]→TP）— Karel/TP 雛形

`FANUC_CSV_PLAY_SPEC.md` の FANUC 側実装（方式A）。KMX が出す CSV(関節角) を固定プログラムが読んで再生する。
**ROS/FANUC 側の担当分**（KMX は CSV 仕様＋`FanucCsvExporter`＋FTP push を担当）。

## ファイル
- **`kmx_load.kl`** … Karel: CSV → `PR[]`(joint) 充填＋ヘッダをレジスタへ。**ktrans V9.40-1 でコンパイル通過確認済**。
- **`KMX_PLAY.LS`** … TP: `CALL KMX_LOAD` → `FOR … J PR[R[i]] 速度% CNT …`。※ TP は機種定義が要るので**コントローラ/ROBOGUIDE の TP エディタでロード**（maketp standalone は robot.ini 必須）。

## レジスタ規約（kmx_load.kl と KMX_PLAY.LS で一致）
| R[] | 向き | 意味 |
|---|---|---|
| **R[89]** | in | **品番**（→ ファイル名の一部） |
| **R[88]** | in | **パス番号**（**0=復帰動作** / 1以上=登録経路） |
| R[90] | out | count（点数） |
| R[91] | out | group |
| R[92] | out | uframe（UFRAME_NUM） |
| R[93] | out | utool（UTOOL_NUM） |
| R[94] | out | speed（%） |
| R[95] | out | cnt（CNT値） |
| R[96] | out | **1=成功 / -1=失敗**（開けない・EOF・件数不正） |
| R[97..99] | out | 診断（OPEN状態 / READ状態 / デバイス番号） |
| R[1]  | work | FOR ループ変数 |
| PR[1..count] | out | 各点の関節位置（`SET_JPOS_REG`） |

**ファイル名 = `P<品番>_<パス番号>.CSV`**（例 `P1234_2.CSV`／復帰 `P1234_0.CSV`）。
読み先 `UD1:\KMX\P<品番>_<パス番号>.CSV`（ROBOGUIDE は `<セル>\Robot_1\UD1\KMX\`）。詳細は `FANUC_CSV_PLAY_UNITY_REQUEST.md`。

## CSV フォーマット（`FANUC_CSV_PLAY_SPEC.md` §2）
```
count,group,uframe,utool,speed,cnt      ← 整数6
J1,J2,J3,J4,J5,J6                        ← 度・小数・★J3はJ2換算済(そのまま)
...
```
ASCII/カンマ・コメント無し・`count ≤ $MAXPREGNUM`(既定100)。

## セットアップ
1. **KMX_LOAD**: `kmx_load.kl` を ROBOGUIDE/コントローラで翻訳→ロード（DCS の kmx_dcs_srv と同手順。ktrans は `.pc` 生成確認済）。
2. **KMX_PLAY**: `KMX_PLAY.LS` を TP としてロード（またはTPエディタで下記を入力）。
3. **CSV 配置**: `UD1:\KMX\` を作成し、`P<品番>.CSV` を置く（KMX が FTP push・§5）。
4. **品番設定**: `R[89]` に品番を入れる（生産は PNS/RSR や上位で設定）。

## 実行 / テスト（ROBOGUIDE）
1. `UD1:\KMX\P1.CSV` にサンプルCSV（例 §2 の3点・count=3）を置く。
2. `R[89]=1`。
3. `KMX_PLAY` を実行 → `KMX_LOAD` が PR[1..3] を充填（`R[96]=1`）→ FOR で `J PR[R[i]]` 再生 → ロボットが KMX プレビューと同じ経路をなぞる。
4. 品番違いCSVを置き `R[89]` を変える → 同じ `KMX_PLAY` で別経路（プログラム再生成なし）。

## KMX_PLAY の中身（TPエディタで入力する場合）
```
  1:  CALL KMX_LOAD ;
  2:  IF R[96]<>1,JMP LBL[99] ;      ; ロード失敗なら再生しない
  3:  UFRAME_NUM=R[92] ;
  4:  UTOOL_NUM=R[93] ;
  5:  FOR R[1]=1 TO R[90] ;
  6:  J PR[R[1]] R[94]% CNT R[95] ;
  7:  ENDFOR ;
  8:  LBL[99] ;
```

## 注意 / 残(△)
- **Karel `READ` のカンマ区切り**：`READ f (j1..j6)` がカンマを区切る前提（§2）。実機の版でカンマを区切らない場合は kmx_load.kl を「行READ→自前 `,` 分割（CNV_STR_REAL）」へ切替（kmx_dcs_srv の CSV 生成/DCS reader と同流儀）。
- **`$MAXPREGNUM`**：実機で実値確認。>100 は KMX 側間引き（Phase1）or Karel↔TP バッチ（Phase2）。
- **DCS 常駐 Karel(`kmx_dcs_srv`,%NOPAUSE) と共存**：別プログラム/別タスク。KMX_PLAY 実行中も DCS 配信は継続。
- **CRX 協働速度/DCS 制限**：speed/cnt は控えめから（DCS 内に収める）。
- **maketp standalone**：robot.ini 必須（`Setrobot`）。TP は実機/ROBOGUIDE でロードするのが確実。