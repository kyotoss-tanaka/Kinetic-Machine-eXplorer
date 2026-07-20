# DCS安全ゾーン ROS経由ライブ取得 実装仕様（§2-2 / Phase2）

作成: 2026-07-15 / 対象: KMX(Unity製HMI) ＋ ROS2 / 前提: [[DCS_ZONE_IMPORT_SPEC]]（JSON手動運用=Phase1）は実装済

> 目的: **実機/ROBOGUIDE で更新した DCS(Dual Check Safety) の CPC ゾーンを、手動でJSON転記せず ROS経由でKMXへ受信**し、既存の可視化(`SafetyZoneScript`)にそのまま流す。
> 背景: Phase1 は `SafetyZoneInfo.json` の手動運用。実機DCSを更新するたびに転記が要る。本仕様でそれを自動化する。
> **読むだけは不変**（KMXからDCSは書かない。[[DCS_ZONE_IMPORT_SPEC]] §1）。

---

## 1. ゴール / スコープ
- **やること**: ロボットコントローラの `$DCS_CPC[i]`（カルテシアン位置チェック）を ROS経由で読み、KMXが **起動時 / リロード時 / 「DCS再読込」ボタン** で受信して箱を再描画。
- **やらないこと**: DCS設定の書込み。可視化ロジック/座標変換は Phase1 の資産を**そのまま再利用**（新規に作らない）。
- **フォールバック**: ROS未接続/未対応時は従来どおり `SafetyZoneInfo.json` を使う（併存・段階移行可）。

---

## 2. アーキテクチャ
```
FANUC コントローラ  $DCS_CPC[i]（X/Y/Z上下限・inside/outside・enable・frame, 単位mm）
   │  ← ★ここの「読み取り手段」が本仕様の肝（§3）
   ▼
ROS2  DCS読取りノード（kmx_dcs_reader または kmx_planner に同居）
   │   ・$DCS_CPC を読んで kmx_msgs/SafetyZone[] に整形
   │   ・サービス GetSafetyZones（オンデマンド）＋ latched topic /kmx/safety_zones（起動時取得用）
   ▼
Unity(KMX)  ISafetyZoneSource（ROS実装＝RosTcpConnector 経由）
   │   ・受信した SafetyZone[] → Parameters.SafetyZoneSetting/SafetyZone へ変換
   ▼
既存 ParameterLoader.ReloadSafetyZones 相当 → SafetyZoneScript（可視化・座標変換・arm1原点）
```
KMX の可視化・座標整合（mm→m、ROS→Unity 逆軸写像、**arm1(J1軸)=原点**）は Phase1 で確定済み（[[DCS_ZONE_IMPORT_SPEC]] §4.4 ＋ 本チャットで arm1 原点に修正）。ROSからは**素の DCS 値(mm・robot World/base フレーム)** をもらえば、その先は流用でよい。

---

## 3. ★最重要 / 未確定: `$DCS_CPC` の読み取り手段（ROS側・版/オプション依存）
DCS の CPC はコントローラのシステム変数 `$DCS_CPC[i]`（サブフィールド: `$DCS_CPC[i].$X1..$Z2`, `$ENABLE`, `$INOUT`(inside/outside), `$FRAME` 等）に入っている。これを ROS が読む経路の候補:

| 手段 | 概要 | 要否/前提 | 評価 |
|---|---|---|---|
| **A. Karel ソケット常駐** | コントローラに Karel プログラムを常駐させ `$DCS_CPC[i].*` を `GET_VAR`/直接参照でTCP配信。ROS側は生クライアント。 | Karel オプション＋TP転送。追加コスト小 | ◎ 汎用・確実。**第一候補** |
| **B. Web/Comet(HTTP)** | コントローラの Web Server(iPendant/HTTP)で system 変数を HTTP GET（KCL/COMET, `/karel/` CGI 等） | **Web Server オプション**必須 | ○ ノード側は簡単。オプション有無を要確認 |
| **C. PCDK(PC Interface)** | FANUC PCDK(COM)で `GetSysVar` | Windows＋PCDKライセンス。ROS(Linux)から遠い | △ WSL/Linux と相性悪。非推奨 |
| **D. SNPX/EtherNetIP レジスタ** | DCS値をレジスタへ横流しするTP/Karelを別途書く | 追加プログラム必要 | △ 二度手間 |
| **E. FTP で system.va 取得** | `MD:/` の system 変数ファイルをFTP取得しパース | FTP有効。値の即時性△ | △ ダンプ的。定期には可 |

- **推奨: A（Karel常駐ソケット）**。ROBOGUIDE でも Karel は動くので**先にROBOGUIDEで検証**できる（実機前に確認）。
- **要確認（ブロッカー）**:
  1. `$DCS_CPC[i]` の**サブフィールド名/型**（X1..Z2 の単位=mm か、frame の持ち方、inside/outside フラグの値、enable）。← 実機/ROBOGUIDE の変数一覧で確定。
  2. 実機の **CPC ゾーン数**（配列長 i の最大）。
  3. 読み取り手段（A〜E）で**実際に露出しているか**（版・オプション）。
  4. 定義フレーム＝World/UF0 で確定済み（[[roboguide-eval]] §8'）だが、UF利用時のオフセットは要確認。

> この §3 が確定するまで ROSノードの中身（変数アクセス部）は書けない。**まず A で `$DCS_CPC[1]` を1件読めることを ROBOGUIDE で確認**してから配列化・全ゾーン化する（[[DCS_ZONE_IMPORT_SPEC]] §4.4 と同じ「1件で疎通→展開」流儀）。

---

## 4. ROS メッセージ / サービス（kmx_msgs）
既存 `Obstacles.msg`/`ObstaclePrimitive.msg` に倣う。**単位mm・robot World/base フレーム**で素の DCS 値を渡す（KMX側で m 変換・軸写像）。

`kmx_msgs/msg/SafetyZone.msg`:
```
# DCS CPC 1ゾーン。robot World/base フレーム・単位 mm（KMX側で ×0.001・軸写像）。
string id                 # 例 "CPC1"（$DCS_CPC のindex由来）
bool enabled              # $ENABLE
bool inside_allowed       # true=内側が安全域 / false=内側が進入禁止(keep-out)。$INOUT から変換
float64[3] min_mm         # [X1,Y1,Z1] 下限
float64[3] max_mm         # [X2,Y2,Z2] 上限
```

`kmx_msgs/msg/SafetyZones.msg`（topic 用・latched）:
```
string robot_id           # 対象ロボ（"" =既定/単機）。Ros2Info robots と対応
string frame              # 通常 "world"(UF0)
string unit               # "mm"
SafetyZone[] zones
```

`kmx_msgs/srv/GetSafetyZones.srv`（ボタン=オンデマンド取得）:
```
string robot_id           # "" =既定
---
bool ok
string message            # 失敗理由（DCS未読/未対応 等）
SafetyZones zones
```

- **topic** `/kmx/safety_zones`（**latched/transient_local**）: 起動時にKMXが購読すれば最新を1発で受け取れる。
- **service** `/kmx/get_safety_zones`: 「DCS再読込」ボタンで能動取得。
- CMake: `rosidl_generate_interfaces` に上記 msg/srv を追加。Unity 側は **Generate ROS Messages** で C# 生成が要る（[[MULTI_ROBOT_ROS2_SPEC]] の robot_id 再生成と同じ手順）。

---

## 5. ROS ノード（`kmx_dcs_reader`）
- 役割: §3 の手段で `$DCS_CPC[i]` を読み → `SafetyZone[]` に整形 → **latched topic 発行**＋**サービス応答**。
- 更新契機: (a) サービス呼び出し時に都度読む（確実）、(b) 起動時に1回発行、(c) 任意で低頻度ポーリング（DCSは静的なので不要寄り）。
- `inside_allowed = ($INOUT が「内側(inside)」)`。[[roboguide-eval]] §8': **「外側」＝内側が進入禁止＝`inside_allowed=false`（赤）**。ここの対応を実機値で確定。
- 単位はmmのまま渡す（KMXが変換）。frame は "world"。
- kmx_bringup に含める（`kmx_bringup.launch.py` にノード追加、`use_moveit` とは独立に起動可）。

---

## 6. KMX / Unity 側（受信して既存パイプラインへ）
**新規は「受信アダプタ」だけ。可視化・座標・原点・ボタン・起動/リロード契機は Phase1 の資産を再利用。**

1. `ISafetyZoneSource`（新規・小さいIF）:
   ```csharp
   public interface ISafetyZoneSource {
       // ROSからゾーンを取得（非同期）。取得不可なら null（→JSONフォールバック）。
       Task<List<SafetyZoneSetting>> FetchAsync();
   }
   ```
   - `RosSafetyZoneSource`（RosTcpConnector 実装）: `GetSafetyZones` サービス呼び or latched topic の最新値を `Parameters.SafetyZoneSetting/SafetyZone`（mm のまま）へ変換。
   - `JsonSafetyZoneSource`: 現行の `SafetyZoneInfo.json` 読み（フォールバック）。
2. `ParameterLoader.ReloadSafetyZones()` を改修:
   - `if (ROS利用可) ros.FetchAsync()` → 失敗/未接続なら JSON。取得した `List<SafetyZoneSetting>` を**現行と同じ**に各unitへ結線＋`AttachSafetyZone`（＝`SafetyZoneScript` 再描画）。
   - **起動時 / F5 / ボタン** は既に `ReloadSafetyZones` 相当を通るので、ソースを差し替えるだけで3経路とも ROS 受信になる（本チャットで実装済の導線をそのまま使う）。
3. `robot_id` 対応: mechId/name 結線は現行流用。将来の複数ロボは `Ros2Info robots` と `robot_id` で対応（[[MULTI_ROBOT_ROS2_SPEC]]）。
4. 座標変換は不変: KMX が `min_mm/max_mm` を ×0.001 → ROS→Unity 逆軸写像 → **arm1原点**に配置（`GetRobotOriginWorldPosition`）。**ROSからは素のDCS値だけ**もらう。

---

## 7. 段階プラン
- **P2-0（疎通・最小）**: ROBOGUIDE で Karel(A)により `$DCS_CPC[1]` を1件TCP配信 → ROSノードが受信しログ。KMX変更なし。**§3の確定が目的**。
- **P2-1（メッセージ）**: `SafetyZone/SafetyZones/GetSafetyZones` を kmx_msgs に追加・ビルド → Unity メッセージ再生成。
- **P2-2（ROSノード）**: `kmx_dcs_reader` で latched topic＋service。1ゾーンで疎通。
- **P2-3（KMX受信）**: `RosSafetyZoneSource` ＋ `ReloadSafetyZones` ソース差し替え。ボタン/起動/F5でROS受信→箱更新を確認。
- **P2-4（全ゾーン・複数ロボ・実機）**: 配列全件、robot_id、実機DCSで検証。JSONフォールバック確認。

---

## 8. 検証（完了条件）
1. ROBOGUIDE/実機で DCS(CPC) を変更 → KMXの「DCS再読込」ボタン → **転記なしで箱が更新**。
2. 起動時・F5 でも最新DCSで表示。
3. ROS未接続時は `SafetyZoneInfo.json` にフォールバック（壊れない）。
4. inside/outside の色・enable・複数ゾーン・複数ロボが正しい。
5. 位置/寸法が ROBOGUIDE 表示と一致（arm1原点・mm→m は Phase1 検証済）。

---

## 9. 未確定 / 確認事項（ROS側で要確認＝ブロッカー）
1. **`$DCS_CPC[i]` のサブフィールド名・単位・inside/outside・enable・frame・配列長**（§3-要確認1,2）。← 実機/ROBOGUIDE の変数一覧。
2. **読み取り手段の実現性**（A: Karel ソケットが第一候補。Web Server(B)オプション有無）。
3. UF 使用時のフレームオフセット（World/UF0 は確定済）。
4. 更新頻度（静的前提でサービス都度読み＋起動時1発で足りるか）。
5. Unity メッセージ再生成のタイミング（kmx_msgs 追加後）。

---

## 3'. ROBOGUIDE実測: `$DCSS_CPC` の実構造（2026-07-15・このチャットで確定）
**★変数名の訂正**: `$DCS_CPC` ではなく **`$DCSS_CPC`**（S二つ＝DCS Safety系）。**`$DCSS_CPC[32]`（最大32ゾーン）of `DCSS_CPC_T`**。本仕様(§3/§4/§5/§9)の `$DCS_CPC` は全て **`$DCSS_CPC`** に読み替え。

`DCSS_CPC_T` の主要フィールド（`$DCSS_CPC[i]`）とメッセージ(§4)対応:
| フィールド | 例(CPC1) | メッセージ対応 |
|---|---|---|
| `$COMMENT` | 'KMX_TEST' | id/name |
| `$ENABLE` | 1 | enabled |
| `$MODE` | 1 | inside_allowed（★内外の**値対応は未確定**＝内側ゾーンと比較して確定） |
| `$GRP_NUM` | 1 | robot（mechId） |
| `$UFRM_NUM` | 0 | frame（**0=World/UF0** 確定） |
| `$NUM_VTX` | 8 | 形状=箱 |
| `$X[8]` | [1]=300,[2]=900 | `min_mm[0]=$X[1]`, `max_mm[0]=$X[2]` |
| `$Y[8]` | [1]=-300,[2]=300 | `min_mm[1]=$Y[1]`, `max_mm[1]=$Y[2]` |
| `$Z1` / `$Z2` | 0 / 600 | `min_mm[2]=$Z1`, `max_mm[2]=$Z2` |
| `$STOP_TYP` ほか(I/O・速度制限・`$UTOOL_NUM`・`$STOP_TOL`) | — | 可視化不要 |

読取り実装の注意（Karel/ROSノード）:
- **X/Y は配列 `[1]/[2]`、Z はスカラ `$Z1/$Z2`** の非対称。読むのは `$DCSS_CPC[i].$X[1]`,`.$X[2]`,`.$Y[1]`,`.$Y[2]`,`.$Z1`,`.$Z2`,`.$ENABLE`,`.$MODE`,`.$UFRM_NUM`,`.$COMMENT`。
- **単位 mm 確定**（KMX側で×0.001）。
- **残ブロッカー**: `$MODE` の inside/outside 値対応（外側ゾーンで `$MODE=1`。**内側ゾーンを1つ定義して値を比較**して確定＝次の一手）。

---

## 11. ★ROS2側 実装状況（2026-07-15・P2-1/P2-2 実装・モック検証済）
- **kmx_msgs（P2-1）**: `msg/SafetyZone.msg` / `msg/SafetyZones.msg` / `srv/GetSafetyZones.srv` を §4 どおり追加（正本＋WSL、CMakeLists 登録、`sync.sh` のコピー対象に追加）。ビルド済み。**Unity は `Robotics > Generate ROS Messages` で C# 再生成が必要**（geometry_msgs 不要・kmx_msgs のみ）。
- **kmx_dcs_reader ノード（P2-2）**: `kmx_planner` パッケージに追加（entry point `kmx_dcs_reader`）。latched topic `/kmx/safety_zones`(transient_local) ＋ service `/kmx/get_safety_zones`。**ノードは TCP クライアント**（Karel 常駐サーバ=A案 へ接続して読む）。起動時に1回 latched publish＋サービス都度読み。params: `dcs_host`(=127.0.0.1)/`dcs_port`(=60011)/`robot_id`/`frame`(=world)/`unit`(=mm)/`mode_outside_value`(=1)/`include_disabled`(=false)/`id_source`(=comment)/`read_timeout_sec`(=3)/`poll_sec`(=0=off)/`publish_on_start`(=true)/`zones_topic`/`get_service`。
- **launch**: `kmx_bringup.launch.py` に `kmx_dcs_reader` を追加。`use_dcs_reader`(=true・**use_moveit と独立**)/`dcs_host`/`dcs_port` 引数。
- **Karel（①）**: `karel/kmx_dcs_srv.kl` ＋ `karel/README.md`（Host Comm サーバタグ設定・疎通手順）。**controller側・ROBOGUIDE で要検証リファレンス**。

**★ワイヤプロトコル（Karel → node・私設計・ASCII 行）**:
```
DCS,<n>                                                            (任意・件数)
CPC,<idx>,<comment>,<enable>,<mode>,<grp>,<ufrm>,<x1>,<x2>,<y1>,<y2>,<z1>,<z2>
   例: CPC,1,KMX_TEST,1,1,1,0,300,900,-300,300,0,600
END                                                                (終端・無ければ接続クローズ)
```
単位 mm、X/Y は配列[1]/[2]・Z はスカラ$Z1/$Z2、comment に','禁止。ROS 側は `enable=0` を間引き（`include_disabled` で変更可）、`inside_allowed = (mode != mode_outside_value)`。

**検証（モック Karel サーバでE2E・2026-07-15）**: service→`ok=True "2 zone(s)"`、latched topic→同内容。mode=1→inside_allowed=false / mode=2→true、無効ゾーン間引き、min/max_mm が §3' 実測例と一致。

**★socket E2E 完全成功（2026-07-16・ROBOGUIDE→ROS→Unity）**: `ROBOGUIDE実DCS → Karel常駐socket(0.0.0.0:60011) → NAT gw経由 → kmx_dcs_reader → /kmx/safety_zones → endpoint → Unity` を**実機同等の socket 経路で live 確認**（実測 KMX_TEST inside_allowed=False min[300,-300,0] max[900,300,100]・Unity 表示OK）。**決定要因**: ①Karel 内で `SET_VAR $HOSTS_CFG[3].$SERVER_PORT=60011`（Host Comm GUI「ポート」欄と別物・無効だと **MSG_CONNECT 67206=空回り**）→ Karel が 0.0.0.0 listen ②WSL は **NAT**（mirrored はこの複数NIC機で ROS2 DDS を壊す）＋Karel 0.0.0.0 なので Windowsホスト(default gw)経由で到達＝ノード param **`dcs_host=auto`**（gw 自動検出・bringup 既定） ③Karel 常駐＝**`%NOPAUSE/%NOABORT/%NOBUSYLAMP`**（T2 で一度起動すれば離しても serve 継続）。**Karel 実行時ハマり所（修正済）**: `ENABLE`/`MODE`予約語→`zenab/zmode`／`WRITE`はFILE型のみ（文字列は`+`＋`CNV_INT_STR`）／`GET_VAR`第1引数`entry=0`必須／`GET_VAR`失敗時 `IF status<>0 THEN val=既定` ガード必須／Karel IF は複数行必須。ktrans は WSL から直接コンパイル可。※検証専用だった `kmxdcsf.kl`(TP表示版) は socket 疎通確立につき削除済。

**残（次の一手）**: ①`$MODE` の**内側**値を実測（内側ゾーンを1個作って `kmxdcsf` 再実行→md 値確認。外側=1は確定・`mode_outside_value` で調整）②**実機で `kmx_dcs_srv.kl`(socket) の live 疎通**（実機は SM ポートを実イーサネットに出す）③Unity メッセージ再生成＋`RosSafetyZoneSource`（P2-3）④配列全件・robot_id・実機（P2-4）。

## 10. 参考ポインタ
- Phase1（JSON手動・可視化・座標・arm1原点）: [[DCS_ZONE_IMPORT_SPEC]]、`Assets/Scripts/Devices/SafetyZoneScript.cs`、`Assets/Scripts/Kinematics/6Aixs/Fanuc/CRX-30iA.cs`(`GetRobotOriginWorldPosition`)。
- メッセージ/トランスポートの流儀: `kmx_msgs/msg/Obstacles.msg`、`Assets/Scripts/Com/Ros2/RosTcpConnectorTransport.cs`、[[OBSTACLES_ROS2_SPEC]]。
- ROBOGUIDE実測(inside/outside・World/UF0・mm): [[roboguide-eval]] §8'。
- 複数ロボ/robot_id: [[MULTI_ROBOT_ROS2_SPEC]]。
