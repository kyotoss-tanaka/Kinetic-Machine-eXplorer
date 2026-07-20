# KMX DCS 読取り — Karel 常駐ソケット（A案）

FANUC コントローラの DCS(Dual Check Safety) CPC ゾーン `$DCSS_CPC[1..32]` を TCP で吐く
Karel 常駐サーバ。ROS 側 `kmx_dcs_reader`（TCP クライアント）が接続して読む。
仕様: `../DCS_ZONE_ROS2_LIVE_SPEC.md` §3'/§4/§5。

## ファイル
- `kmx_dcs_srv.kl` … TCP サーバ本体（接続ごとに全ゾーンを CSV で出力）。

## ワイヤプロトコル（Karel → kmx_dcs_reader・ASCII 行・改行終端）
```
DCS,<n>                                                     ← 任意（件数）
CPC,<idx>,<comment>,<enable>,<mode>,<grp>,<ufrm>,<x1>,<x2>,<y1>,<y2>,<z1>,<z2>
   例: CPC,1,KMX_TEST,1,1,1,0,300,900,-300,300,0,600
END                                                         ← 終端（無ければ接続クローズで終端）
```
- 単位 **mm**。X/Y は配列 `[1]/[2]`、Z はスカラ `$Z1/$Z2`（非対称・§3'）。
- `comment` に `,` を含めない（Karel 側で `_` 置換）。
- `$MODE` → `inside_allowed`：外側=`$MODE=1`（確定）→ 内側 keep-out → `inside_allowed=false`。
  内側の値は未確定（ROS 側 param `mode_outside_value` で調整）。

## セットアップ手順（コントローラ / ROBOGUIDE）
1. **Host Comm サーバタグ設定**: `MENU > SETUP > Host Comm > Servers`
   - 空きタグ（例 `S3:`）を選び **Protocol = SM（Socket Messaging）**、**Port = 60011**、
     **Startup State = START**（電源投入で常駐）に。ポートは ROS 側 `dcs_port`(=60011) と一致。
   - `.kl` 内の `TAG = 'S3:'` を、設定したタグ名に合わせる。
2. **Karel 転送・実行**: `kmx_dcs_srv.kl` を ROBOGUIDE でコンパイル→ロード（または `.pc` を実機へ）。
   `SELECT` から `kmx_dcs_srv` を実行（常駐ループ）。ロボット動作は伴わない（`%NOLOCKGROUP`）。

## 疎通確認（P2-0）
ROS 側（WSL）:
```bash
ros2 launch kmx_planner kmx_bringup.launch.py use_moveit:=false dcs_host:=<コントローラIP>
# ROBOGUIDE ローカルなら dcs_host:=127.0.0.1（既定）
ros2 topic echo /kmx/safety_zones --once           # 起動時 latched を1発受信
ros2 service call /kmx/get_safety_zones kmx_msgs/srv/GetSafetyZones "{robot_id: ''}"
```
`$DCSS_CPC[1]` の値が `min_mm/max_mm`（mm）で出れば疎通OK。まず1件で確認→全件へ。

## コンパイル（ktrans・検証済 2026-07-15）
本 `.kl` は **ktrans V9.40-1 でクリーンにコンパイル通過**（`.pc` 生成）確認済み。WSL から Windows の ktrans を直接呼べる:
```bash
KT="/mnt/c/Program Files (x86)/FANUC/WinOLPC/bin/ktrans.exe"
mkdir -p /mnt/c/kmx_karel_build && cp kmx_dcs_srv.kl /mnt/c/kmx_karel_build/
( cd /mnt/c/kmx_karel_build && "$KT" /ver V9.40-1 kmx_dcs_srv.kl /l )   # /l で .ls リスト
```
実機ロード用は**版一致が安全**なので、通常は ROBOGUIDE の KAREL トランスレータで翻訳→ロードするのが確実
（ROBOGUIDE がワークセルの版を自動で使う）。上記 `.pc` を直接ロードしてもよいが版差で弾かれたら ROBOGUIDE で再翻訳。

### 判明した Karel の落とし穴（今後の編集用メモ）
- **`ENABLE` は予約語** → 変数名に使えない（`zenab` に改名済）。`MODE` も回避（`zmode`）。
- **`WRITE var(...)` は var が FILE 型のみ**。STRING への WRITE は不可 → 文字列組み立ては
  **`+` 連結 ＋ `CNV_INT_STR`** で行う（本ファイルの `mk_name` 参照）。
- `GET_VAR(entry,'*SYSTEM*','$DCSS_CPC[i].$X[1]',val,status)` は**変数名を文字列で渡す**ので、
  `$DCSS_CPC`/`DCSS_CPC_T` の型定義はコンパイル時に不要（標準ビルトインだけで通る）。

## 注意（実行時＝ROBOGUIDE/実機でのみ検証可）
- コンパイルは通るが**実行時挙動**（`MSG_CONNECT` とサーバタグ起動状態の相性、`GET_VAR` の実値、
  実数書式 `::1::1` の桁）は ROBOGUIDE/実機で要確認。まず `$DCSS_CPC[1]` 1件を
  `kmx_dcs_reader` のログ／`ros2 topic echo` で確認し、ズレたら微調整。
- `$COMMENT` に `,` を含めないこと（CSV が壊れる）。空コメントは空フィールドで送ってよい（ROS 側が `CPC<idx>` に補完）。
- DCS は**読むだけ**（KMX から DCS 設定は書かない）。
