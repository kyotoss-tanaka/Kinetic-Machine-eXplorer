# 【Unity(KMX)側 実装要望】DCS 安全ゾーンを ROS 経由で受信して表示する

**方向**：**ROS2側 → Unity側** への依頼。
**ROS2 側は実装・実機同等 socket 経路で live 検証済み**（2026-07-16）。Unity は **`/kmx/safety_zones`（latched topic）** の購読 or **`/kmx/get_safety_zones`（service）** の呼び出しで、既存の可視化パイプライン（`SafetyZoneScript`）に流すだけ。関連：`DCS_ZONE_ROS2_LIVE_SPEC.md`（全体設計）、`DCS_ZONE_IMPORT_SPEC.md`（Phase1・JSON手動＝座標/原点の資産）。

---

## 0. 現状（2026-07-16・ROS2 側で確認済み）
- ROBOGUIDE/実機の DCS `$DCSS_CPC[i]` を **Karel 常駐ソケット → `kmx_dcs_reader` ノード**が読み、**`/kmx/safety_zones` に配信**するところまで **live で動作確認済み**。
- 実測（ROBOGUIDE の CPC1）：
  ```
  zones=1  frame="world"  unit="mm"
  id="KMX_TEST"  enabled=True  inside_allowed=False  min_mm=[300,-300,0]  max_mm=[900,300,100]
  ```
- ＝**Unity は受信して箱を描くだけ**。座標変換・原点・色・ボタン導線は Phase1 の資産を再利用（新規に作らない）。

---

## 1. メッセージ契約（`kmx_msgs`・**Generate ROS Messages 要**）
kmx_msgs に以下を追加済み（ビルド済み）。**Unity で `Robotics > Generate ROS Messages` を実行して C# を再生成**してください（geometry_msgs 不要・kmx_msgs のみ）。

### `kmx_msgs/msg/SafetyZone.msg`
```
string id                 # ゾーン識別（例 "KMX_TEST"＝$COMMENT。空なら "CPC<idx>"）
bool enabled              # 有効/無効
bool inside_allowed       # true=内側が安全域 / false=内側が進入禁止(keep-out)
float64[3] min_mm         # [X下限, Y下限, Z下限]（mm・World/base 相対）
float64[3] max_mm         # [X上限, Y上限, Z上限]（mm）
```

### `kmx_msgs/msg/SafetyZones.msg`（topic / service で使用）
```
string robot_id           # 対象ロボ（""=既定/単機）
string frame              # "world"（UF0）
string unit               # "mm"
SafetyZone[] zones
```

### `kmx_msgs/srv/GetSafetyZones.srv`
```
string robot_id           # ""=既定
---
bool ok                   # 取得成功か
string message            # 失敗理由（"Karel 接続失敗…" 等）
SafetyZones zones
```

---

## 2. 受信方法（2通り・**service 推奨**）
| 手段 | 名前 | 型 | 用途 |
|---|---|---|---|
| **service（推奨・確実）** | `/kmx/get_safety_zones` | `kmx_msgs/GetSafetyZones` | 「DCS再読込」ボタン＝オンデマンド取得 |
| **topic（latched）** | `/kmx/safety_zones` | `kmx_msgs/SafetyZones` | 起動時に最新を1発受信 |

- **推奨は service 呼び**：request/response で確実。`ok=true` なら `zones` を使う。`ok=false` は `message` を表示し **JSON フォールバック**（Phase1）へ。
- **topic は latched（transient_local・reliable・depth 1）**：ROS-TCP-Connector 側の購読で最新値を受け取れる。ただし取りこぼしが不安なら **起動時にも service を1回呼ぶ**のが堅い。
- `RosSafetyZoneSource.FetchAsync()` は「service 呼び → 成功なら `List<SafetyZoneSetting>` に変換／失敗なら null（→JSON）」でよい（`DCS_ZONE_ROS2_LIVE_SPEC.md` §6）。

---

## 3. 座標・単位・色の規約（★ここが表示の肝）
ROS からは**素の DCS 値（mm・World/UF0・軸整列 BOX）**が来る。**変換は Phase1 の資産をそのまま流用**：

| 項目 | 規約 |
|---|---|
| 単位 | **mm** → Unity 側で **×0.001（m 化）** |
| フレーム | **"world"（UF0）** = ロボ基準。**arm1(J1軸)=原点**に配置（`GetRobotOriginWorldPosition`・Phase1で確定） |
| 軸写像 | **ROS(FLU) → Unity 逆軸写像**（Phase1 の SafetyZoneScript と同じ） |
| 箱の作り方 | `min_mm`/`max_mm` から軸整列 AABB。中心 = (min+max)/2 ×0.001、サイズ = (max-min) ×0.001 |
| **色** | **`inside_allowed=false`＝内側が進入禁止＝赤（keep-out）** / **`inside_allowed=true`＝内側が安全域＝緑**（Phase1 の色規約に合わせる） |
| enable | `enabled=false` のゾーンは**表示しない**（既定で ROS 側が間引くが、来ても Unity で無視推奨） |
| id | ゾーン識別・更新の突合キー（同 id は置換／消えた id は削除、の全置換運用でよい） |

> ※ ROS 側は**二重補正しない**（素の DCS 値のみ）。mm→m・軸写像・原点合わせは**すべて Unity 側 Phase1 資産**が担当。

---

## 4. `$MODE`（内外）の対応（暫定・要実機確定）
- ROS 側は `inside_allowed = ($MODE ≠ 1)` で変換（**外側=`$MODE=1` は実機確定**）。
- 内側ゾーンの `$MODE` 値は未確定。内側 DCS を1個作って確定次第、ROS 側 param `mode_outside_value` を調整（**Unity 側は `inside_allowed` を見るだけでよい**・変更不要）。

---

## 5. 検証手順
1. Unity で `Robotics > Generate ROS Messages`（kmx_msgs 再生成）。
2. ROS 側 bringup 起動済み（`/kmx/safety_zones`・`/kmx/get_safety_zones` が生きている状態）。
3. Unity を KMX_ROS2 有効・endpoint(:10000) 接続。
4. **「DCS 再読込」ボタン**（or 起動時/F5）→ 箱が出るはず：
   - `KMX_TEST`（**赤＝keep-out**）、X 300〜900 / Y -300〜300 / Z 0〜100（mm）
5. 確認：位置・寸法・**色（inside_allowed）**・複数ゾーン・enable フィルタが ROBOGUIDE 表示と一致。
6. ROBOGUIDE で DCS を変更 → 「DCS 再読込」→ **転記なしで箱が更新**されれば完了。

---

## 6. 運用・注意
- **live 更新**：ROBOGUIDE/実機で DCS を変えたら「DCS 再読込」ボタン（service 呼び）で再取得。ROS 側 `kmx_dcs_reader` はサービス都度に Karel から読み直す。
- **フォールバック**：`ok=false`／未接続時は従来の `SafetyZoneInfo.json`（Phase1）を使う（併存・壊れない）。
- **robot_id**：単機は `""`。将来の複数ロボは `robot_id` で対応（`MULTI_ROBOT_ROS2_SPEC.md`）。
- **接続先の差（Unity は無関係）**：ROS 側 `dcs_host` で ROBOGUIDE(同一PC)/実機 を吸収。**Unity から見た topic/service 契約は同一**。

---

## 7. ROS2 側（触らない・参考）
- `kmx_dcs_reader`（`kmx_planner` パッケージ）：Karel 常駐ソケット(A案・`karel/kmx_dcs_srv.kl`)へ TCP 接続 → `SafetyZone[]` 整形 → latched topic ＋ service。
- `kmx_bringup.launch.py` に統合済み（`use_dcs_reader:=true` 既定・`use_moveit` と独立）。
- 実機は SM ポートを実イーサネットに出すので socket はそのまま。ROBOGUIDE でも `$SERVER_PORT` 設定で socket 疎通確認済み。