using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// KMX ブランドのゲーム内ローディング画面（非WebGL／Editor）。常駐し、ロード中(GlobalScript.isLoading)
/// だけ表示、読込完了(GlobalScript.isLoaded)で「カーテン分割」退場(§3.2)。F5 再ロードでも再表示。
///
/// 退場演出（カーテン分割）：暗背景を上下2枚に分け、中央の発光シームから上下に開いて奥(メイン)を露出。
///   ① コンテンツ(ロゴ等) fade(0.35s) ② シーム flash(0.9s) ③ 上下スライド(0.8s) → 約1.0sで非表示。
///
/// 表示物: 「K M X」ロゴ・鼓動ドット/区切り線・サブタイトル・進捗バー(loadProgress)・%・コメント(loadLabel)。
/// 仕様: docs/KMXロゴ仕様.md。実WebGLビルドは HTMLテンプレートが担うため生成しない。
/// </summary>
public class KmxLoadingScreen : MonoBehaviour
{
    private static KmxLoadingScreen instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
        {
            return;
        }
#if !UNITY_WEBGL || UNITY_EDITOR
        var go = new GameObject("KmxLoadingScreen");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<KmxLoadingScreen>();
#endif
    }

    // 配色（KMXロゴ仕様 §1）
    private static readonly Color Bg = new Color(0.043f, 0.055f, 0.082f, 1f);      // #0b0e15
    private static readonly Color Fg = new Color(0.914f, 0.933f, 0.965f, 1f);      // #e9eef6
    private static readonly Color Accent = new Color(0.910f, 0.271f, 0.118f, 1f);  // 鳥居朱 #e8451e
    private static readonly Color Accent2 = new Color(1f, 0.478f, 0.271f, 1f);     // 明朱 #ff7a45
    private static readonly Color Muted = new Color(0.420f, 0.396f, 0.439f, 1f);   // #6b6570
    private static readonly Color TrackCol = new Color(0.086f, 0.114f, 0.161f, 1f); // #161d29
    private static readonly Color Comment = new Color(0.60f, 0.64f, 0.70f, 1f);
    private static readonly Color SeamCol = new Color(0.90f, 0.96f, 1f, 1f);    // 青白い光（継ぎ目フラッシュ）
    private static readonly Color SeamGlow = new Color(0.50f, 0.78f, 1f, 1f);   // 青グロー

    // 鼓動（仕様 §3.1）
    private const float Cycle = 1.1f;
    private const float Onset = 0.20f, Peak = 0.50f, Fall = 0.70f;
    private const float MaxScale = 1.20f, RestOp = 0.50f, PeakOp = 1.00f;

    // カーテン分割（仕様 §3.2）
    private const float ContentFade = 0.35f;   // コンテンツfade
    private const float SeamFlash = 0.9f;      // 継ぎ目フラッシュ
    private const float SlideDur = 0.8f;       // 上下スライド
    private const float ExitTotal = 1.0f;      // 退場完了

    private const float MinShow = 1.0f;        // 最低表示時間
    private const float BarWidth = 320f, BarH = 4f;

    private Canvas canvas;
    private RectTransform canvasRt;
    private CanvasGroup group, contentGroup;
    private RectTransform topHalf, botHalf, seamRt, content, dot, divider, barFill;
    private Image seamImg, dotImg, divImg;
    private TextMeshProUGUI logoTmp, pctText, commentTmp;

    private float startTime, showTime, dispProg, exitElapsed, curtainSlide;
    private bool visible = true, exiting, dotPlaced, jpAssigned;

    private static TMP_FontAsset cachedJp;

    private void Awake()
    {
        startTime = showTime = Time.unscaledTime;
        Build();
    }

    private void Build()
    {
        var cgo = new GameObject("KmxLoadingCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        cgo.transform.SetParent(transform, false);
        canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        canvasRt = (RectTransform)canvas.transform;
        var scaler = cgo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        group = cgo.GetComponent<CanvasGroup>();

        // カーテン（上下2枚・中央シーム）。背景＝暗。退場時に上下へスライド。
        topHalf = NewHalf(cgo.transform, "CurtainTop");
        botHalf = NewHalf(cgo.transform, "CurtainBot");

        seamImg = NewImage(cgo.transform, "Seam");
        seamImg.sprite = ToSprite(MakeBar(160));
        seamImg.color = new Color(SeamCol.r, SeamCol.g, SeamCol.b, 0f);
        var sglow = seamImg.gameObject.AddComponent<Outline>();
        sglow.effectColor = new Color(SeamGlow.r, SeamGlow.g, SeamGlow.b, 0.9f);
        sglow.effectDistance = new Vector2(0f, 4f);
        sglow.useGraphicAlpha = true;
        seamRt = seamImg.rectTransform;
        seamRt.anchorMin = seamRt.anchorMax = new Vector2(0.5f, 0.5f);
        seamRt.pivot = new Vector2(0.5f, 0.5f);

        // コンテンツ層（ロゴ等。退場でフェード）
        var contGo = new GameObject("Content", typeof(RectTransform), typeof(CanvasGroup));
        contGo.transform.SetParent(cgo.transform, false);
        content = (RectTransform)contGo.transform;
        Stretch(content);
        contentGroup = contGo.GetComponent<CanvasGroup>();

        // ロゴ "K M X"（中央 M のみ朱）
        logoTmp = NewText(content, "Logo", 96f, TextAlignmentOptions.Center);
        logoTmp.characterSpacing = 22f;
        logoTmp.color = Fg;
        logoTmp.text = "K<color=#e8451e>M</color>X";
        Center(logoTmp.rectTransform, new Vector2(900f, 150f), new Vector2(0f, 70f));

        // ドット（朱・鼓動）
        dotImg = NewImage(content, "Dot");
        dotImg.sprite = ToSprite(MakeDisc(48));
        dotImg.color = Accent;
        dot = dotImg.rectTransform;
        Center(dot, new Vector2(16f, 16f), new Vector2(180f, 96f));

        // 区切り線（明朱グラデ＋グロー・鼓動）
        divImg = NewImage(content, "Divider");
        divImg.sprite = ToSprite(MakeBar(160));
        divImg.color = Accent2;
        var glow = divImg.gameObject.AddComponent<Outline>();
        glow.effectColor = new Color(Accent.r, Accent.g, Accent.b, 0.55f);
        glow.effectDistance = new Vector2(0f, 3f);
        glow.useGraphicAlpha = true;
        divider = divImg.rectTransform;
        Center(divider, new Vector2(360f, 2f), new Vector2(0f, -2f));

        // サブタイトル
        var sub = NewText(content, "Subtitle", 16f, TextAlignmentOptions.Center);
        sub.characterSpacing = 18f;
        sub.color = Muted;
        sub.text = "kinetic   machine   explorer";
        Center(sub.rectTransform, new Vector2(900f, 30f), new Vector2(0f, -34f));

        // 進捗バー
        var track = NewImage(content, "BarTrack");
        track.color = TrackCol;
        Center(track.rectTransform, new Vector2(BarWidth, BarH), new Vector2(0f, -90f));
        var fill = NewImage(track.transform, "BarFill");
        fill.color = Accent2;
        var fglow = fill.gameObject.AddComponent<Outline>();
        fglow.effectColor = new Color(Accent.r, Accent.g, Accent.b, 0.5f);
        fglow.effectDistance = new Vector2(0f, 2f);
        fglow.useGraphicAlpha = true;
        barFill = fill.rectTransform;
        barFill.anchorMin = barFill.anchorMax = new Vector2(0f, 0.5f);
        barFill.pivot = new Vector2(0f, 0.5f);
        barFill.anchoredPosition = Vector2.zero;
        barFill.sizeDelta = new Vector2(0f, BarH);

        // %
        pctText = NewText(content, "Pct", 13f, TextAlignmentOptions.Center);
        pctText.characterSpacing = 6f;
        pctText.color = Muted;
        pctText.text = "0%";
        Center(pctText.rectTransform, new Vector2(300f, 20f), new Vector2(0f, -112f));

        // ロード中コメント（日本語含む→NotoSansJP を遅延適用）
        commentTmp = NewText(content, "Comment", 14f, TextAlignmentOptions.Center);
        commentTmp.color = Comment;
        commentTmp.text = "";
        Center(commentTmp.rectTransform, new Vector2(1100f, 24f), new Vector2(0f, -138f));
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        float dt = Time.unscaledDeltaTime;

        // 鼓動（区切り線・ドット同位相）
        float prog = Beat(Mathf.Repeat(now - startTime, Cycle) / Cycle);
        float op = RestOp + prog * (PeakOp - RestOp);
        float sc = 1f + prog * (MaxScale - 1f);
        if (dot != null)
        {
            dot.localScale = new Vector3(sc, sc, 1f);
            dotImg.color = new Color(Accent.r, Accent.g, Accent.b, op);
        }
        if (divider != null)
        {
            divider.localScale = new Vector3(1f, sc, 1f);
            divImg.color = new Color(Accent2.r, Accent2.g, Accent2.b, op);
        }
        if (!dotPlaced)
        {
            PlaceDot();
        }

        // 進捗バー＋%（loadProgress に滑らかに追従）
        float p = Mathf.Clamp01(GlobalScript.loadProgress);
        dispProg = Mathf.Lerp(dispProg, p, Mathf.Clamp01(dt * 8f));
        if (barFill != null)
        {
            barFill.sizeDelta = new Vector2(dispProg * BarWidth, BarH);
        }
        if (pctText != null)
        {
            pctText.text = Mathf.RoundToInt(dispProg * 100f) + "%";
        }

        // コメント（NotoSansJP を遅延適用）
        if (commentTmp != null)
        {
            if (!jpAssigned)
            {
                var f = LoadJpFont();
                if (f != null)
                {
                    commentTmp.font = f;
                    jpAssigned = true;
                }
            }
            string lbl = GlobalScript.loadLabel ?? "";
            if (commentTmp.text != lbl)
            {
                commentTmp.text = lbl;
            }
        }

        // 表示／カーテン分割退場
        bool loading = GlobalScript.isLoading && !GlobalScript.isLoaded;
        if (loading && !visible)
        {
            ReShow(now);   // 再ロードで再表示
        }
        if (visible && !exiting && GlobalScript.isLoaded && (now - showTime) >= MinShow)
        {
            exiting = true;
            exitElapsed = 0f;
        }

        if (exiting)
        {
            exitElapsed += dt;
            contentGroup.alpha = 1f - Mathf.Clamp01(exitElapsed / ContentFade);
            seamImg.color = new Color(SeamCol.r, SeamCol.g, SeamCol.b, SeamFlashAlpha(exitElapsed));
            curtainSlide = Smooth(Mathf.Clamp01(exitElapsed / SlideDur)) * (CanvasH() * 0.5f + 2f);
            if (exitElapsed >= ExitTotal)
            {
                exiting = false;
                visible = false;
            }
        }
        else if (visible)
        {
            contentGroup.alpha = 1f;
            seamImg.color = new Color(SeamCol.r, SeamCol.g, SeamCol.b, 0f);
            curtainSlide = 0f;   // 閉（画面を覆う）
        }
        else
        {
            contentGroup.alpha = 0f;
            seamImg.color = new Color(SeamCol.r, SeamCol.g, SeamCol.b, 0f);
            curtainSlide = CanvasH() * 0.5f + 2f;   // 開（オフスクリーン）
        }

        LayoutCurtain();

        bool block = visible || exiting;
        if (group != null)
        {
            group.blocksRaycasts = block;
            group.interactable = block;
        }
    }

    private void ReShow(float now)
    {
        visible = true;
        exiting = false;
        exitElapsed = 0f;
        showTime = now;
        dispProg = 0f;
        if (contentGroup != null)
        {
            contentGroup.alpha = 1f;
        }
    }

    /// <summary>上下2枚と中央シームを毎フレーム配置（解像度変化に追従）。curtainSlide で開閉。</summary>
    private void LayoutCurtain()
    {
        if (canvasRt == null)
        {
            return;
        }
        float h = CanvasH();
        float w = CanvasW();
        float halfH = h * 0.5f + 2f;
        if (topHalf != null)
        {
            topHalf.sizeDelta = new Vector2(w + 4f, halfH);
            topHalf.anchoredPosition = new Vector2(0f, h * 0.25f + curtainSlide);
        }
        if (botHalf != null)
        {
            botHalf.sizeDelta = new Vector2(w + 4f, halfH);
            botHalf.anchoredPosition = new Vector2(0f, -(h * 0.25f) - curtainSlide);
        }
        if (seamRt != null)
        {
            seamRt.sizeDelta = new Vector2(w + 4f, 3f);
            seamRt.anchoredPosition = Vector2.zero;
        }
    }

    private float CanvasH()
    {
        float h = canvasRt != null ? canvasRt.rect.height : 0f;
        return h < 1f ? 1080f : h;
    }

    private float CanvasW()
    {
        float w = canvasRt != null ? canvasRt.rect.width : 0f;
        return w < 1f ? 1920f : w;
    }

    /// <summary>継ぎ目フラッシュの不透明度（0→ピーク(25%)→0、SeamFlash 秒で終わる）。</summary>
    private static float SeamFlashAlpha(float e)
    {
        float u = e / SeamFlash;
        if (u >= 1f)
        {
            return 0f;
        }
        float a = u < 0.25f ? (u / 0.25f) : (1f - (u - 0.25f) / 0.75f);
        return Mathf.Clamp01(a);
    }

    private void PlaceDot()
    {
        if (logoTmp == null || dot == null)
        {
            return;
        }
        logoTmp.ForceMeshUpdate();
        float w = logoTmp.preferredWidth;
        if (w <= 1f)
        {
            return;
        }
        var lp = logoTmp.rectTransform.anchoredPosition;
        dot.anchoredPosition = new Vector2(lp.x + w * 0.5f + 14f, lp.y + 26f);
        dotPlaced = true;
    }

    private static float Beat(float ph)
    {
        if (ph < Onset)
        {
            return 0f;
        }
        if (ph < Peak)
        {
            float d = Peak - Onset;
            return d <= 0f ? 1f : Smooth((ph - Onset) / d);
        }
        if (ph < Fall)
        {
            float d = Fall - Peak;
            return d <= 0f ? 0f : Smooth(1f - (ph - Peak) / d);
        }
        return 0f;
    }

    private static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    private static TMP_FontAsset LoadJpFont()
    {
        if (cachedJp != null)
        {
            return cachedJp;
        }
        foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (f != null && f.name == "NotoSansJP-Medium SDF")
            {
                cachedJp = f;
                return f;
            }
        }
        cachedJp = Resources.Load<TMP_FontAsset>("Fonts/NotoSansJP-Medium SDF");
        return cachedJp;
    }

    // ---- UI ヘルパ ----

    private RectTransform NewHalf(Transform parent, string name)
    {
        var img = NewImage(parent, name);
        img.color = Bg;
        img.raycastTarget = true;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        return rt;
    }

    private static TextMeshProUGUI NewText(Transform parent, string name, float size, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.alignment = align;
        t.richText = true;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        return t;
    }

    private static Image NewImage(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        return go.GetComponent<Image>();
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void Center(RectTransform rt, Vector2 size, Vector2 pos)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    // ---- スプライト生成 ----

    private static Sprite ToSprite(Texture2D t)
    {
        return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Texture2D MakeDisc(int n)
    {
        var px = new Color32[n * n];
        float c = n * 0.5f, r = n * 0.5f - 1f;
        for (int x = 0; x < n; x++)
        {
            for (int y = 0; y < n; y++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    private static Texture2D MakeBar(int n)
    {
        const int h = 8;
        var px = new Color32[n * h];
        for (int x = 0; x < n; x++)
        {
            float u = (x + 0.5f) / n;
            float a = Mathf.Clamp01(1f - Mathf.Abs(u - 0.5f) * 2f);
            a = Mathf.Pow(a, 0.55f);
            byte ba = (byte)Mathf.RoundToInt(a * 255f);
            for (int y = 0; y < h; y++)
            {
                px[y * n + x] = new Color32(255, 255, 255, ba);
            }
        }
        var tex = new Texture2D(n, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }
}
