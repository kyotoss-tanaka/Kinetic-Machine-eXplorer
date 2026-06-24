# KMX ロゴ・ブランド仕様書

Kinetic Machine eXplorer（KMX）のロゴ／ローディング画面／UIアクセントの統一仕様。
HMX（human machine experience）と同方向性。**暗背景＋細字ワイドトラッキングの「K M X」＋鳥居色（朱）のアクセント**。
今後のロゴ・スプラッシュ・UIアクセントはすべて本仕様に統一する。

実装基準：`Assets/WebGLTemplates/KMX/index.html`（WebGLローディング画面）。

---

## 1. カラーパレット

| 用途 | 名称 | HEX | RGB |
|---|---|---|---|
| 背景 | ディープチャコール | `#0b0e15` | 11,14,21 |
| 文字（K・X） | オフホワイト | `#e9eef6` | 233,238,246 |
| **アクセント**（中央 M・ドット・区切り線・進捗バー・グロー） | **鳥居色（朱 / vermilion）** | `#e8451e` | 232,69,30 |
| アクセント明（グロー中心・バー右端・ハイライト） | 明朱 | `#ff7a45` | 255,122,69 |
| アクセント暗（バー左端） | 深朱 | `#c33b18` | 195,59,24 |
| サブタイトル | ミュート（やや暖色） | `#6b6570` | 107,101,112 |
| 区切り線（無地時） | ライン | `#2a3342` | 42,51,66 |

- アクセントは**鳥居の朱**で統一。青系（旧 `#3d9bff`）は使用しない。
- グロー（発光）は朱：`box-shadow` で `rgba(232,69,30,…)` を重ねる。

## 2. タイポグラフィ

- フォント：細いサンセリフ。`"Helvetica Neue","Segoe UI","Hiragino Kaku Gothic ProN","Noto Sans JP",Arial,sans-serif`、**font-weight 200**。
- ロゴ「**K M X**」：大文字、**letter-spacing 0.30em**、サイズ可変 `clamp(46px,9vw,104px)`。
  - **中央の M のみアクセント色（朱）**、K・X はオフホワイト。
  - 文字列の右に**ドット**（小円・朱・ゆっくり明滅 `pulse`）。
- サブタイトル「**kinetic machine explorer**」：小文字、**letter-spacing 0.46em**、ミュート色、`clamp(10px,1.6vw,14px)`。

## 3. レイアウト

中央寄せで縦に：**ロゴ → 区切り線（発光）→ サブタイトル →（ローディング時）進捗バー＋%**。

- **区切り線**：幅 `clamp(190px,30vw,380px)`、高さ 2px。中央が明るい朱のグラデ＋**朱のグロー**。鼓動（§3.1）で脈打つ。
- **進捗バー**：幅 `clamp(190px,30vw,300px)`、高さ 3px。トラック `#161d29`、フィルは深朱→明朱グラデ＋朱グロー。読込進捗に連動。
- ロゴ／サブタイトルは `fadeup` でフェードイン。

### 3.1 鼓動アニメーション（区切り線・ドット）

区切り線とドットは**心拍（鼓動）**で同期して脈打つ。単発ビート＋休止。確定値：

| パラメータ | 値 |
|---|---|
| 周期 | **1100 ms** |
| 開始（拡大/発光が動き出す） | **20 %** |
| ピーク到達 | **50 %** |
| 戻り完了（以降は休止） | **70 %** |
| ドット拡大（最大倍率 scale） | **1.20×** |
| バー拡大（最大倍率 scaleY） | **1.20×** |
| 休止時の明るさ（opacity） | **0.50** |
| ピーク時の明るさ（opacity） | **1.00** |

- 補間は `ease-in-out`（早く始まり・ゆっくり変化）。**バーとドットは同位相で同期**。

## 3.2 スプラッシュ → メインページ遷移（カーテン分割）

起動スプラッシュからメイン画面へ切り替わるときの遷移。**中央の区切り線（発光）から画面が上下に開き、奥のメインが現れる**＝「カーテン分割」。ロゴの区切り線をそのまま“継ぎ目”として使い、世界観を一貫させる。

構成：暗背景を**上下2枚**に分割（中央で 1px 重ねて継ぎ目を隠す）＋中央の発光ライン（seam）＋ロゴ等のコンテンツ層。

退場シーケンス（確定値）：

| 段階 | 動き | 値 |
|---|---|---|
| ① コンテンツ退場 | ロゴ／サブタイトルをフェードアウト | opacity 1→0 / **0.35s ease** |
| ② 継ぎ目フラッシュ | 中央ラインがアクセント色で一瞬発光（脈） | opacity 0→1→0 / **0.9s ease**（0% / 25% / 100% = .0 / 1 / .0） |
| ③ 上下に開く | 上半分↑・下半分↓へスライドしてメインを露出 | translateY ±100% / **0.8s cubic-bezier(.7,0,.25,1)** |
| ④ 除去 | スプラッシュ要素を削除 | 開始から約 **1000ms** 後 |

- 発光ラインの色は**青白い光**（朱の暗背景の中では赤いシームが視認しづらいため）。core `#e6f3ff`＋青グロー `rgba(130,200,255,.95)`／`rgba(90,160,255,.5)`。※ロゴ・区切り線・ドット・進捗バーは朱のまま、**退場シームのみ青白**。グローは `box-shadow`。
- スプラッシュは「最低表示時間」（ロゴアニメ完了＋静止）経過後に、この退場を開始する。
- 同じ方向性で、KMX（WebGL ローディング）・HMX（Studio / View 起動）双方に適用する。

### CSS 基準値（抜粋）

```css
/* 背景は上下2枚（退場時に上下へ開く）。中央で1px重ねて継ぎ目を隠す */
.sp-half { position:absolute; left:0; right:0; height:calc(50% + 1px); background:var(--bg);
  transition: transform .8s cubic-bezier(.7,0,.25,1); }
.sp-top { top:0; }  .sp-bot { bottom:0; }
/* 分割の継ぎ目で一瞬光るライン（青白い光。暗背景で視認しやすい） */
.sp-seam { position:absolute; left:0; right:0; top:50%; height:2px; transform:translateY(-50%); opacity:0;
  background:linear-gradient(90deg,transparent,#e6f3ff,transparent);
  box-shadow:0 0 14px 2px rgba(130,200,255,.95), 0 0 30px 4px rgba(90,160,255,.5); }
.content { transition: opacity .35s ease; }          /* ロゴ等のコンテンツ層 */
/* 退場（クラス付与で発火） */
.exit .content { opacity:0; }
.exit .sp-top  { transform:translateY(-100%); }
.exit .sp-bot  { transform:translateY(100%); }
.exit .sp-seam { animation: seamFlash .9s ease forwards; }
@keyframes seamFlash { 0%{opacity:0} 25%{opacity:1} 100%{opacity:0} }
```

## 4. 用途・運用

- **WebGL ローディング画面**：`Assets/WebGLTemplates/KMX/index.html`。`BuildAndRun.cs` の WebGL ビルドで `PlayerSettings.WebGL.template = "PROJECT:KMX"` を自動設定。
- 起動時の Unity 既定ロゴは本テンプレートで置換。なお「Made with Unity」スプラッシュ（Player Settings > Splash Image）は Personal ライセンスでは無効化不可（別物）。
- 今後のロゴ／アイコン／UIアクセント（選択ハイライト等）も本配色・字体に合わせる。

## 5. CSS 基準値（抜粋）

```css
:root{
  --bg:#0b0e15; --fg:#e9eef6;
  --accent:#e8451e;   /* 鳥居色（朱） */
  --accent2:#ff7a45;  /* 明朱 */
  --muted:#6b6570; --line:#2a3342;
}
.logo{ font-weight:200; letter-spacing:.30em; font-size:clamp(46px,9vw,104px); color:var(--fg); }
.logo .accent{ color:var(--accent); }                 /* 中央 M */
.logo .dot{ width:.14em; height:.14em; border-radius:50%; background:var(--accent);
  box-shadow:0 0 6px rgba(232,69,30,.7); transform:translateY(-.18em); transform-origin:center;
  animation:heartbeatDot 1100ms ease-in-out infinite; }                 /* 朱ドット・鼓動 */
.divider{
  height:2px; transform-origin:center;
  background:linear-gradient(90deg,transparent,rgba(232,69,30,.14) 14%,rgba(255,140,95,.98) 50%,rgba(232,69,30,.14) 86%,transparent);
  box-shadow:0 0 6px rgba(232,69,30,.9),0 0 16px rgba(232,69,30,.55),0 0 30px rgba(232,69,30,.25);
  animation:heartbeatLine 1100ms ease-in-out infinite; }
.bar > i{ background:linear-gradient(90deg,#c33b18,var(--accent2)); box-shadow:0 0 8px rgba(232,69,30,.6); }
/* 鼓動: 周期1100ms 開始20% ピーク50% 戻り70% 拡大1.2 休止0.5 ピーク1.0 */
@keyframes heartbeatDot{
  0%,20%  { transform:translateY(-.18em) scale(1);   opacity:.5; }
  50%     { transform:translateY(-.18em) scale(1.2); opacity:1;  }
  70%,100%{ transform:translateY(-.18em) scale(1);   opacity:.5; }
}
@keyframes heartbeatLine{
  0%,20%  { transform:scaleY(1);   opacity:.5; }
  50%     { transform:scaleY(1.2); opacity:1;  }
  70%,100%{ transform:scaleY(1);   opacity:.5; }
}
```

## 6. 改訂メモ
- 2026-06-23 初版。HMX方向性＋鳥居色（朱 `#e8451e`）でロゴ統一。中央M・ドット・区切り線・進捗バーを朱系に。
- 2026-06-23 §3.2 追記。スプラッシュ→メインページ遷移「カーテン分割」（中央の発光ラインから上下に開く）を確定値で規定。HMXは青系で実装済み。
- 2026-06-23 §3.2 をKMXにも実装。WebGL=`index.html`（CSS `.sp-half`/`.sp-seam`/`.content`/`.exit`＋`seamFlash`、`hideLoading()`で`exit`付与→約1000ms後に除去）。非WebGL=`KmxLoadingScreen.cs`（上下2枚`topHalf`/`botHalf`＋シーム＋`contentGroup`、読込完了で退場：content fade0.35s／seam flash0.9s／slide0.8s smoothstep／計1.0s）。
