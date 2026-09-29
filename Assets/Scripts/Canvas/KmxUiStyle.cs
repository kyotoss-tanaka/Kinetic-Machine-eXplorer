using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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

    /// <summary>閉じるボタンの文字（NotoSansJP の SDF に入っている ×）</summary>
    public const string CloseGlyph = "×";

    /// <summary>閉じるボタンの幅（タイトルバー右端）</summary>
    public const float CloseButtonWidth = 28f;

    /// <summary>
    /// 最小化アイコンの文字。NotoSansJP の SDF には ▶(U+25B6) が無いため ▼ を回して右向きにする
    /// </summary>
    public const string CollapseGlyph = "▼";

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
