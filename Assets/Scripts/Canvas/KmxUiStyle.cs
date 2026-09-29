using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// KMX の画面パネル共通の見た目。経路計画パネル（ComRos2PlanPanel）の見た目を基準にする
/// メニューパネル（Prefab）は CanvasMenuBaseScript が実行時にこの定義で塗り替える
/// </summary>
public static class KmxUiStyle
{
    /// <summary>タイトルバー</summary>
    public static readonly Color TitleBar = new Color(0.15f, 0.3f, 0.55f, 0.98f);

    /// <summary>パネルの背景</summary>
    public static readonly Color PanelBackground = new Color(0f, 0f, 0f, 0.6f);

    /// <summary>表の見出しの行の帯（タイトルバーと同じ系統の青を半透明で）</summary>
    public static readonly Color HeaderBand = new Color(0.18f, 0.34f, 0.6f, 0.6f);

    /// <summary>表の見出し・行ラベルの文字（値の白と区別する淡い青灰色）</summary>
    public static readonly Color HeaderText = new Color(0.82f, 0.88f, 0.98f, 1f);

    /// <summary>表の1行おきの帯（横に目で追いやすくする）</summary>
    public static readonly Color RowStripe = new Color(1f, 1f, 1f, 0.04f);

    /// <summary>
    /// 帯の画像を作る（親の幅いっぱい・上から y の位置に高さ h。当たり判定なし・最背面）
    /// </summary>
    public static Image AddBand(RectTransform parent, string name, float y, float h, Color color)
    {
        var rt = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.SetAsFirstSibling();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(0f, h);
        var img = rt.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>ボタン（通常）</summary>
    public static readonly Color Button = new Color(0.2f, 0.4f, 0.7f, 0.95f);

    /// <summary>ボタン（選択中・強調）</summary>
    public static readonly Color ButtonActive = new Color(0.8f, 0.5f, 0.1f, 0.95f);

    /// <summary>スライダーの溝</summary>
    public static readonly Color SliderTrack = new Color(1f, 1f, 1f, 0.25f);

    /// <summary>スライダーの塗り</summary>
    public static readonly Color SliderFill = new Color(0.1f, 0.7f, 1f, 0.9f);

    /// <summary>入力欄の背景</summary>
    public static readonly Color InputBackground = new Color(1f, 1f, 1f, 0.15f);

    /// <summary>チェックボックスの枠</summary>
    public static readonly Color CheckBox = new Color(1f, 1f, 1f, 0.3f);

    /// <summary>チェックの印</summary>
    public static readonly Color CheckMark = new Color(0.1f, 0.8f, 1f, 1f);

    /// <summary>文字</summary>
    public static readonly Color Text = Color.white;

    /// <summary>メニューパネル（Prefab）のタイトルバーの高さ。Prefab の中身はこの下から並んでいる</summary>
    public const float MenuTitleHeight = 30f;

    /// <summary>タイトルの文字の大きさ</summary>
    public const float TitleFontSize = 16f;

    /// <summary>本文の文字の大きさ</summary>
    public const float BodyFontSize = 15f;

    /// <summary>本文のフォント名（日本語・英数字とも）</summary>
    private const string BodyFontName = "NotoSansJP-Medium SDF";

    /// <summary>アイコンのフォント名（再生・一時停止など。システムレコーダー・メニューバーと同じ）</summary>
    private const string IconFontName = "MaterialSymbolsRounded-VariableFont_FILL,GRAD,opsz,wght SDF";

    private static TMP_FontAsset bodyFont;
    private static TMP_FontAsset iconFont;

    /// <summary>本文のフォント（NotoSansJP）。Resources の動作画面の Prefab から参照をたどって取得する</summary>
    public static TMP_FontAsset BodyFont => bodyFont != null ? bodyFont : (bodyFont = FindFont(BodyFontName, "Prefabs/Canvas/ActUnitInfo"));

    /// <summary>アイコンのフォント（MaterialSymbolsRounded）。Resources のシステムレコーダーの Prefab から取得する</summary>
    public static TMP_FontAsset IconFont => iconFont != null ? iconFont : (iconFont = FindFont(IconFontName, "Prefabs/Canvas/SysRecSetting"));

    /// <summary>アイコン用フォントか（本文のフォントにそろえる対象から外す）</summary>
    public static bool IsIconFont(TMP_FontAsset font)
    {
        return (font != null) && font.name.StartsWith("Material");
    }

    /// <summary>
    /// フォントを名前で探す。読み込み済みならそれを使い、なければ Resources の Prefab が参照しているものを取る
    /// （フォント自体は Resources の外にあるため直接は読めない）
    /// </summary>
    private static TMP_FontAsset FindFont(string name, string prefabPath)
    {
        foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if ((f != null) && (f.name == name))
            {
                return f;
            }
        }
        var prefab = Resources.Load<GameObject>(prefabPath);
        if (prefab != null)
        {
            foreach (var t in prefab.GetComponentsInChildren<TMP_Text>(true))
            {
                if ((t.font != null) && (t.font.name == name))
                {
                    return t.font;
                }
            }
        }
        Debug.LogWarning($"[KmxUiStyle] フォント \"{name}\" が見つかりません（{prefabPath}）");
        return null;
    }

    /// <summary>
    /// 配下の通常のテキスト（UnityEngine.UI.Text）を TextMeshPro に置き換える（同じ位置・文言・色・配置）
    /// </summary>
    public static void ConvertLegacyTexts(Transform root)
    {
        foreach (var legacy in root.GetComponentsInChildren<UnityEngine.UI.Text>(true))
        {
            var go = legacy.gameObject;
            var text = legacy.text;
            var color = legacy.color;
            var size = legacy.fontSize;
            var anchor = legacy.alignment;
            var raycast = legacy.raycastTarget;
            Object.DestroyImmediate(legacy);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = BodyFont;
            tmp.text = text;
            tmp.color = color;
            tmp.fontSize = size;
            tmp.alignment = ToTmpAlignment(anchor);
            tmp.raycastTarget = raycast;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
        }
    }

    /// <summary>通常のテキストの配置 → TextMeshPro の配置</summary>
    public static TextAlignmentOptions ToTmpAlignment(TextAnchor anchor)
    {
        switch (anchor)
        {
            case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
            case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
            case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
            case TextAnchor.MiddleLeft: return TextAlignmentOptions.MidlineLeft;
            case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
            case TextAnchor.MiddleRight: return TextAlignmentOptions.MidlineRight;
            case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
            case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
            default: return TextAlignmentOptions.BottomRight;
        }
    }

    /// <summary>
    /// 配下の文字のフォントを本文のフォントにそろえ、大きさを本文の大きさにそろえる。
    /// アイコン用フォントと大きな文字（アイコン等、28pt以上）、小さな文字（15pt以下）はそのまま
    /// </summary>
    public static void NormalizeFonts(Transform root, TMP_Text title)
    {
        var body = BodyFont;
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (IsIconFont(t.font))
            {
                continue;
            }
            if (body != null)
            {
                t.font = body;
            }
            if (t == title)
            {
                t.fontSize = TitleFontSize;
            }
            else if ((t.fontSize > BodyFontSize) && (t.fontSize < 28f))
            {
                t.fontSize = BodyFontSize;
            }
        }
    }

    /// <summary>パネルの内側の余白（下の余白と、端に接する文字・部品を内側へ寄せる量。経路計画パネルと同じ8px）</summary>
    public const float PanelPadding = 8f;

    /// <summary>丸い画像の角の半径（元画像のピクセル）。KmxRoundCorners が表示の大きさに合わせて縮める</summary>
    public const float RoundBorder = 32f;

    /// <summary>丸い画像（9スライス。つまみ等を太さの半分の半径で丸くする）</summary>
    private static Sprite roundSprite;

    /// <summary>
    /// 丸い画像（白・縁をなめらかにした円を9スライスにしたもの）。一度だけ作って使い回す
    /// </summary>
    public static Sprite RoundSprite
    {
        get
        {
            if (roundSprite == null)
            {
                var size = (int)(RoundBorder * 2f);
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                var c = (size - 1) / 2f;
                var r = size / 2f;
                var pixels = new Color32[size * size];
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        var d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                        var a = Mathf.Clamp01(r - d);   // 縁の1pxをなめらかにする
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply();
                roundSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(RoundBorder, RoundBorder, RoundBorder, RoundBorder));
                roundSprite.name = "KmxRound";
            }
            return roundSprite;
        }
    }

    /// <summary>
    /// 画像を丸くする（正方形なら円、細長ければ両端が丸い形）
    /// </summary>
    public static void MakeRound(Image img, Color color)
    {
        if (img == null)
        {
            return;
        }
        img.sprite = RoundSprite;
        img.type = Image.Type.Sliced;
        img.color = color;
        if (img.GetComponent<KmxRoundCorners>() == null)
        {
            img.gameObject.AddComponent<KmxRoundCorners>();
        }
    }

    /// <summary>閉じるボタンの文字（NotoSansJP の SDF に入っている ×）</summary>
    public const string CloseGlyph = "×";

    /// <summary>閉じるボタンの幅（タイトルバー右端）</summary>
    public const float CloseButtonWidth = 28f;

    /// <summary>
    /// 最小化アイコンの文字。NotoSansJP の SDF には ▶(U+25B6) が無いため ▼ を回して右向きにする
    /// </summary>
    public const string CollapseGlyph = "▼";

    /// <summary>スクロールバーのつまみ</summary>
    public static readonly Color ScrollHandle = new Color(1f, 1f, 1f, 0.5f);

    /// <summary>入力欄の未入力時の案内文字</summary>
    public static readonly Color Placeholder = new Color(1f, 1f, 1f, 0.5f);

    /// <summary>ドロップダウンの一覧の背景</summary>
    public static readonly Color DropdownList = new Color(0.08f, 0.1f, 0.16f, 0.97f);

    /// <summary>ドロップダウンの一覧の項目（通常）</summary>
    public static readonly Color DropdownItem = new Color(0.08f, 0.1f, 0.16f, 1f);

    /// <summary>
    /// 配下の部品（ボタン・チェック・スライダー・入力欄・ドロップダウン・スクロールバー）を共通の見た目に塗り替える。
    /// 背景は画像なしの平らな塗りにする（経路計画パネルと同じ）。チェックの印とドロップダウンの矢印は形を残す。
    /// 非表示の部品（行の元の型・ドロップダウンの一覧の元の型）も塗り替えるので、あとから複製される分も同じ見た目になる
    /// </summary>
    /// <param name="root">対象の親</param>
    /// <param name="skip">対象外にする部品（タイトルバーの最小化・閉じるボタン等）</param>
    public static void ApplyToParts(Transform root, ICollection<Selectable> skip)
    {
        // ドロップダウン（一覧の項目のトグルは下のチェックの塗り替えから外す）
        var dropdownItems = new HashSet<Toggle>();
        foreach (var dd in root.GetComponentsInChildren<TMP_Dropdown>(true))
        {
            Flat(dd.targetGraphic as Image, InputBackground);
            dd.colors = ColorBlock.defaultColorBlock;
            if (dd.captionText != null)
            {
                dd.captionText.color = Text;
            }
            foreach (var img in dd.GetComponentsInChildren<Image>(true))
            {
                if (img.name == "Arrow")
                {
                    img.color = Text;
                }
            }
            if (dd.template != null)
            {
                Flat(dd.template.GetComponent<Image>(), DropdownList);
                foreach (var item in dd.template.GetComponentsInChildren<Toggle>(true))
                {
                    dropdownItems.Add(item);
                    if (item.targetGraphic is Image bg)
                    {
                        // 項目の背景は白にして、色の変化（ColorBlock）で通常=紺・マウスを乗せた時=青にする
                        bg.sprite = null;
                        bg.color = Color.white;
                        var cb = ColorBlock.defaultColorBlock;
                        cb.normalColor = DropdownItem;
                        cb.selectedColor = DropdownItem;
                        cb.highlightedColor = Button;
                        cb.pressedColor = ButtonActive;
                        item.colors = cb;
                    }
                    if (item.graphic != null)
                    {
                        item.graphic.color = CheckMark;
                    }
                    SetLabelColor(item.transform, Text);
                }
            }
            if (dd.itemText != null)
            {
                dd.itemText.color = Text;
            }
        }
        // ボタン
        foreach (var b in root.GetComponentsInChildren<Button>(true))
        {
            if ((skip != null) && skip.Contains(b))
            {
                continue;
            }
            Flat(b.targetGraphic as Image, Button);
            b.colors = ColorBlock.defaultColorBlock;
            SetLabelColor(b.transform, Text);
        }
        // チェック
        foreach (var t in root.GetComponentsInChildren<Toggle>(true))
        {
            if (dropdownItems.Contains(t) || ((skip != null) && skip.Contains(t)))
            {
                continue;
            }
            Flat(t.targetGraphic as Image, CheckBox);
            t.colors = ColorBlock.defaultColorBlock;
            if (t.graphic != null)
            {
                t.graphic.color = CheckMark;
            }
            SetLabelColor(t.transform, Text);
        }
        // スライダー
        foreach (var s in root.GetComponentsInChildren<Slider>(true))
        {
            var track = s.transform.Find("Background");
            if (track != null)
            {
                Flat(track.GetComponent<Image>(), SliderTrack);
            }
            if (s.fillRect != null)
            {
                Flat(s.fillRect.GetComponent<Image>(), SliderFill);
            }
            if (s.handleRect != null)
            {
                MakeRound(s.handleRect.GetComponent<Image>(), Color.white);
            }
            s.colors = ColorBlock.defaultColorBlock;
        }
        // 入力欄
        foreach (var f in root.GetComponentsInChildren<TMP_InputField>(true))
        {
            Flat((f.targetGraphic as Image) ?? f.GetComponent<Image>(), InputBackground);
            f.colors = ColorBlock.defaultColorBlock;
            if (f.textComponent != null)
            {
                f.textComponent.color = Text;
            }
            if (f.placeholder != null)
            {
                f.placeholder.color = Placeholder;
            }
        }
        // スクロールバー
        foreach (var sb in root.GetComponentsInChildren<Scrollbar>(true))
        {
            Flat(sb.GetComponent<Image>(), SliderTrack);
            if (sb.handleRect != null)
            {
                MakeRound(sb.handleRect.GetComponent<Image>(), ScrollHandle);
            }
            sb.colors = ColorBlock.defaultColorBlock;
        }
    }

    /// <summary>画像なしの平らな塗りにする</summary>
    private static void Flat(Image img, Color color)
    {
        if (img != null)
        {
            img.sprite = null;
            img.color = color;
        }
    }

    /// <summary>部品の中の文字（TextMeshPro・通常のテキスト）の色をそろえる</summary>
    private static void SetLabelColor(Transform parent, Color color)
    {
        foreach (var tmp in parent.GetComponentsInChildren<TMP_Text>(true))
        {
            tmp.color = color;
        }
        foreach (var txt in parent.GetComponentsInChildren<UnityEngine.UI.Text>(true))
        {
            txt.color = color;
        }
    }

    /// <summary>
    /// 最小化アイコンの向きを状態に合わせる（▼=展開中、右向き=最小化中）
    /// </summary>
    public static void SetCollapseIcon(RectTransform icon, bool collapsed)
    {
        if (icon != null)
        {
            // Z軸の正回転は反時計回り。下向き(▼)を+90°回すと右向きになる
            icon.localEulerAngles = new Vector3(0f, 0f, collapsed ? 90f : 0f);
        }
    }
}

/// <summary>
/// パネルの自動配置。開いた時に表示中の他のパネルと重なっていれば、重ならない位置へ移す
/// 画面座標（Overlay・等倍前提）で判定し、中身がパネル本体の外へはみ出す分も含めた見た目の範囲で比べる
/// </summary>
public static class KmxPanelLayout
{
    /// <summary>パネル同士の間隔(px)</summary>
    private const float Gap = 4f;

    /// <summary>対象のパネル（配置する側・避けられる側の両方）</summary>
    private static readonly List<RectTransform> panels = new List<RectTransform>();

    /// <summary>重ならないように扱うパネルを登録する（下のメニューバー等の避けるだけのものも含む）</summary>
    public static void Register(RectTransform panel)
    {
        if ((panel != null) && !panels.Contains(panel))
        {
            panels.Add(panel);
        }
    }

    /// <summary>登録を外す</summary>
    public static void Unregister(RectTransform panel)
    {
        panels.Remove(panel);
    }

    /// <summary>
    /// 表示中の他のパネルと重なっていれば、空いている位置へ移す（重なっていなければ動かさない）
    /// 候補は画面左上と、各パネルの右隣・真下・左端の真下。上→左の順に試す
    /// panel は左上基準（アンカー・ピボットとも左上）であること
    /// </summary>
    public static void PlaceWithoutOverlap(RectTransform panel)
    {
        if (panel == null)
        {
            return;
        }
        panels.RemoveAll(d => d == null);
        var others = panels.Where(d => (d != panel) && d.gameObject.activeInHierarchy).Select(VisualRect).ToList();
        var self = VisualRect(panel);
        if (!others.Any(d => d.Overlaps(self)))
        {
            return;
        }
        var candidates = new List<Vector2> { Vector2.zero };
        foreach (var o in others)
        {
            candidates.Add(new Vector2(o.xMax + Gap, o.yMin));
            candidates.Add(new Vector2(o.xMin, o.yMax + Gap));
            candidates.Add(new Vector2(0f, o.yMax + Gap));
        }
        foreach (var c in candidates.OrderBy(d => d.y).ThenBy(d => d.x))
        {
            var r = new Rect(c.x, c.y, self.width, self.height);
            if ((r.xMax > Screen.width) || (r.yMax > Screen.height))
            {
                continue;
            }
            if (others.Any(d => d.Overlaps(r)))
            {
                continue;
            }
            MoveTo(panel, self, c);
            return;
        }
        // 空きがなければ左上から少しずつずらして重ねる
        MoveTo(panel, self, new Vector2(30f * others.Count, 30f * others.Count));
    }

    /// <summary>
    /// 見た目の範囲（画面左上原点・下向きy）。中身が本体の外へはみ出す分も含める
    /// </summary>
    private static Rect VisualRect(RectTransform panel)
    {
        var b = RectTransformUtility.CalculateRelativeRectTransformBounds(panel);
        // Overlay の Canvas ではワールド座標＝画面のピクセル座標
        var p0 = panel.TransformPoint(b.min);
        var p1 = panel.TransformPoint(b.max);
        var left = Mathf.Min(p0.x, p1.x);
        var right = Mathf.Max(p0.x, p1.x);
        var bottom = Mathf.Min(p0.y, p1.y);
        var top = Mathf.Max(p0.y, p1.y);
        return Rect.MinMaxRect(left, Screen.height - top, right, Screen.height - bottom);
    }

    /// <summary>見た目の左上が topLeft（画面左上原点・下向きy）に来るよう動かす</summary>
    private static void MoveTo(RectTransform panel, Rect self, Vector2 topLeft)
    {
        var scale = (panel.parent != null) ? panel.parent.lossyScale.x : 1f;
        if (scale < 1e-6f)
        {
            scale = 1f;
        }
        var delta = new Vector2(topLeft.x - self.xMin, -(topLeft.y - self.yMin)) / scale;
        panel.anchoredPosition += delta;
    }
}

/// <summary>
/// 丸い画像（KmxUiStyle.RoundSprite）の角の半径を、表示の太さの半分に合わせる
/// </summary>
[RequireComponent(typeof(Image))]
public sealed class KmxRoundCorners : UIBehaviour
{
    protected override void OnEnable()
    {
        base.OnEnable();
        Apply();
    }

    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        Apply();
    }

    private void Apply()
    {
        var img = GetComponent<Image>();
        var rect = ((RectTransform)transform).rect;
        var thickness = Mathf.Min(rect.width, rect.height);
        if ((img != null) && (thickness > 0.5f))
        {
            // 表示される角の半径 = RoundBorder / 倍率。太さの半分にする
            var multiplier = KmxUiStyle.RoundBorder * 2f / thickness;
            if (!Mathf.Approximately(img.pixelsPerUnitMultiplier, multiplier))
            {
                img.pixelsPerUnitMultiplier = multiplier;
            }
        }
    }
}
