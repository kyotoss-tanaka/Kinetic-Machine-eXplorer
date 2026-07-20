# ROBOGUIDE 連携 引き継ぎ資料（KMX → FANUC 経路再生）

作成: 2026-07-14 / 対象: KMX(Unity製HMI) と FANUC CRX-30iA + ROBOGUIDE

> この資料は「ROBOGUIDE 用チャット」に渡す前提知識・決定事項・未解決課題のまとめ。
> KMX/ROS2 側の詳細は本リポジトリのメモリ `fanuc-recovery-motion` と `kmx_ros2/` を参照。

---

## 1. ゴール（何がしたいか）
KMX(Unity製HMI)で作った**衝突フリー経路を FANUC ロボに送って、その通り再生**する。ティーチペンダントを使わない。**2モード**：

| モード | 作成 | 保存 | 実行 |
|---|---|---|---|
| **復帰** | 機械停止時に**その場で計画**(オンデマンド) | 都度 | 停止時に安全な既定位置へ戻す |
| **登録** | **オフラインで事前作成** | **品種ごとにパス保存** | **生産時に該当パスを呼び出して再生**(静的) |

- ロボット：**FANUC CRX-30iA**（6軸 J1..J6・協働ロボ）。
- 最重要要件：**計画した経路を守る**（角を丸めて障害物に当たらない）。

---

## 2. 方式の決定（★重要・ここが結論）
- **実時間で動かすのではなく「経路を送って再生」**なので、**Stream Motion（有償・実時間UDPオプション）は不要**。将来 実時間連続動作が要る時だけ検討。
- 採用方式＝**オフラインTP**：
  ```
  KMXで経路計画(BITstar・衝突フリー)
    → FANUC TPプログラム .LS を生成（KMX側・実装済）
    → MAKETP で .TP に翻訳（FANUC同梱ツール）
    → ROBOGUIDE / 実コントローラ で実行(再生)
  ```
- **各点を関節移動(J・度)で並べる**＝計画した関節角をそのまま再現＝**IK曖昧さ無し・経路を崩さない**。角丸め(CNT)は経路逸脱＝衝突リスクなので **FINE 既定**。
- ROS の `fanuc_driver`（~/ros2_ws/src/fanuc_driver）は **Stream Motion 専用**。**この用途では ROS を介さず、KMX→.LS/.TP→コントローラ**で完結する（fanuc_driver は今回未使用）。

---

## 3. KMX 側（実装済み・このリポジトリ）
- `Assets/Scripts/Com/Ros2/FanucLsExporter.cs` … 関節経路(度) → **.LS 変換器**（J移動・関節表現・FINE 既定。速度%/FINE-CNT/UF/UT/GP を Options で可変）。
- `Assets/Scripts/Com/Ros2/ComRos2PathPlanner.cs` … `TryBuildCurrentLs(progName, out ls, out error)`：現在の計画/プレビュー軌道 → .LS 文字列。
- `Assets/Scripts/Com/Ros2/ComRos2PlanPanel.cs` … 計画パネルに **「FANUC .LS 出力」ボタン**（OK/NG の下）。出力先＝ **`C:\KMX-Path\KMXPATH.LS`**。状態行に保存パス表示。
- 経路データ：`Ros2Trajectory.positions[点][関節]`＝**度**（ノードで rad→deg 済）、`jointNames`＝`J1..J6`。供給源は BITstar 計画結果 or 登録ステップ(poseDeg)。

---

## 4. 生成する .LS の書式（現状・要 ROBOGUIDE 検証）
- 構成：`/PROG` `/ATTR`（属性）→ `/MN`（動作行 `N:J P[N] <speed>% FINE ;`）→ `/POS`（位置 `P[N]{ GP1: UF:0 UT:1 J1..J6 deg }`・3軸/行）→ `/END`。
- **改行 CRLF、フィールド区切りはタブ**。関節値は度・小数3桁。
- サンプル実ファイル（バイト正確・タブ/CRLF入り）：**`C:\KMX-Deploy\KMXPATH_sample.LS`**（3点・6軸）。内容（タブは空白表示）：

```
/PROG  KMXPATH
/ATTR
OWNER		= MNEDITOR;
COMMENT		= "KMX path";
PROG_SIZE	= 0;
CREATE		= DATE 00-00-00  TIME 00:00:00;
MODIFIED	= DATE 00-00-00  TIME 00:00:00;
FILE_NAME	= ;
VERSION		= 0;
LINE_COUNT	= 5;
MEMORY_SIZE	= 0;
PROTECT		= READ_WRITE;
TCD:  STACK_SIZE	= 0,
      TASK_PRIORITY	= 50,
      TIME_SLICE	= 0,
      BUSY_LAMP_OFF	= 0,
      ABORT_REQUEST	= 0,
      PAUSE_REQUEST	= 0;
DEFAULT_GROUP	= 1,*,*,*,*;
CONTROL_CODE	= 00000000 00000000;
/APPL
/MN
  1:  UFRAME_NUM=0    ;
  2:  UTOOL_NUM=1    ;
  3:J P[1] 50% FINE    ;
  4:J P[2] 50% FINE    ;
  5:J P[3] 50% FINE    ;
/POS
P[1]{
   GP1:
	UF : 0, UT : 1,
	J1=     0.000 deg,	J2=     0.000 deg,	J3=     0.000 deg,
	J4=     0.000 deg,	J5=     0.000 deg,	J6=     0.000 deg
};
P[2]{
   GP1:
	UF : 0, UT : 1,
	J1=    10.000 deg,	J2=     5.000 deg,	J3=    -5.000 deg,
	J4=     0.000 deg,	J5=    30.000 deg,	J6=     0.000 deg
};
P[3]{
   GP1:
	UF : 0, UT : 1,
	J1=    20.000 deg,	J2=    10.000 deg,	J3=   -10.000 deg,
	J4=     0.000 deg,	J5=    45.000 deg,	J6=    15.000 deg
};
/END
```

---

## 5. ROBOGUIDE 側でやること（新チャットのタスク）

### 5.1 版・環境
- **CRX-30iA の機種ライブラリを含む新しめの ROBOGUIDE V9**。
- 実機コントローラの**ソフト版(V9.xx)に合わせる**（ペンダント: MENU→STATUS→Version ID）。パッケージは HandlingPRO 系でOK。

### 5.2 ★まず .LS 取り込み検証（最初にやる・Unity再ビルド不要）
- `C:\KMX-Deploy\KMXPATH_sample.LS` を ROBOGUIDE に取り込む（ROBOGUIDEは.LSを内部翻訳して読める）。
- 取り込めれば書式OK → 実データ出力へ。
- **翻訳/取り込みエラーが出たら、その文言をKMXチャットに渡して .LS 書式（タブ/属性欄/位置ブロック/名前規則）を調整**。FANUC の ASCII 翻訳は書式に厳しいので初回は微調整前提。

### 5.3 .LS → .TP（MAKETP）
- `...\FANUC\WinOLPC\bin\MAKETP.exe` で `.LS → .TP`。**robot.ini**（対象コントローラ版/グループ文脈・ROBOGUIDEセル内にある）が必要。
- KMX から MAKETP.exe を自動呼び出しして .TP まで一気に出す実装も可能（要 MAKETP パス＋robot.ini）。

### 5.4 実行 — 登録/生産（Phase 2）
- 品種ごとの **.TP を事前ロード** → 生産時に **PNS/RSR（プログラム番号選択）＋自動運転(Remote)** で機械 PLC/HMI が番号指定して実行。ディスパッチャ自作不要。

### 5.5 実行 — 復帰（Phase 2）
- 都度の .LS/.TP を **FTP 転送** → 常駐ディスパッチャTP or PNS/RSR で実行。**低速・単段・非常停止確保・人手ゲート**を最初は推奨。

---

## 6. 未解決 / 確認事項（新チャットで詰める）
1. ROBOGUIDE の**正確なリビジョン**、CRX-30iA での各機能可否。
2. **.LS 取り込みの書式適合**（5.2 の初回検証で判明）。
3. **MAKETP の CLI 仕様と robot.ini** の具体（.TP 自動化に必要）。
4. 実機：**PC Interface/SNPX** の可否、**PNS/RSR の配線**、**自動運転(Remote)** 運用可否、**品種数**（＝事前ロード本数）。
5. （将来・実時間化する場合のみ）**Stream Motion オプションが CRX-30iA で提供されるか**。

---

## 7. 使わないと決めたもの
- **Stream Motion**（今の用途では不要）。将来 実時間連続動作が要る時だけ導入し、その時は ROS `fanuc_driver` に載せ替え。
- 今回の経路再生は **ROS 非依存**（KMX→.LS/.TP→コントローラ）。

---

## 8. 参考ポインタ
- 方式・2モードの決定：メモリ `fanuc-recovery-motion`。
- KMX ROS2連携（計画・障害物・登録ステップ）：`kmx_ros2/`（ただし経路再生自体は ROS 非依存）。
- FANUC ドライバ（Stream Motion 方式・今回未使用）：`~/ros2_ws/src/fanuc_driver`（`fanuc_libs` に StreamMotion、要オプション）。
- .LS 変換器の書式：`Assets/Scripts/Com/Ros2/FanucLsExporter.cs`。
