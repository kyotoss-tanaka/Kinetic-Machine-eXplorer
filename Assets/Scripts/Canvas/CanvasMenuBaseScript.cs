using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class CanvasMenuBaseScript : KssBaseScript, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    /// <summary>
    /// 右
    /// </summary>
    protected bool isRight;
    /// <summary>
    /// クリック検知用
    /// </summary>
    protected GraphicRaycaster raycaster;
    /// <summary>
    /// ポインタイベントデータ
    /// </summary>
    private PointerEventData pointerEventData;
    /// <summary>
    /// イベントシステム
    /// </summary>
    private EventSystem eventSystem;
    /// <summary>
    /// キャンバス
    /// </summary>
    private Canvas canvas;
    /// <summary>
    /// 有効無効切り替えボタン
    /// </summary>
    private Button btnEnable;
    /// <summary>
    /// コンテンツ
    /// </summary>
    private List<Transform> objContents = new();
    /// <summary>
    /// タイトルバー（ドラッグはここで押し始めた時だけ受け付ける）
    /// </summary>
    private RectTransform titleBar;
    /// <summary>
    /// パネルの背景。中身がパネル本体の外へはみ出す画面もあるため、中身の範囲に毎フレーム合わせる
    /// </summary>
    private RectTransform background;
    /// <summary>
    /// 最小化アイコン（▼。最小化中は回して右向き）
    /// </summary>
    private RectTransform collapseIcon;
    /// <summary>
    /// タイトル文字
    /// </summary>
    private TextMeshProUGUI titleText;
    /// <summary>
    /// 最小化中
    /// </summary>
    private bool collapsed;
    /// <summary>
    /// タイトルバーで押し始めたドラッグの最中（判定はドラッグ開始時に1回だけ行う）
    /// </summary>
    private bool draggingTitle;
    /// <summary>
    /// 最小化する直前の大きさ（コードで大きさを変えるパネルもあるため、Awake時ではなく最小化時に控える）
    /// </summary>
    private Vector2 expandedSize;
    /// <summary>
    /// 最小化する直前の各コンテンツの表示状態（展開時にそのまま戻す）
    /// </summary>
    private readonly Dictionary<Transform, bool> contentsActive = new();
    /// <summary>
    /// 幅
    /// </summary>
    private int lastWidth;
    /// <summary>
    /// 高さ
    /// </summary>
    protected int lastHeight;
    /// <summary>
    /// ダブルクリック用
    /// </summary>
    private float lastClickTime = 0f;
    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Awake()
    {
        canvas = this.transform.parent.GetComponent<Canvas>();
        raycaster = canvas.GetComponent<GraphicRaycaster>();
        eventSystem = EventSystem.current;
        btnEnable = GetComponentsInChildren<Button>().ToList().Find(d => d.name.Contains("Expand"));
        titleText = btnEnable.gameObject.GetComponentInChildren<TextMeshProUGUI>();

        // 見た目を共通の定義にそろえる（Prefab は変えずに実行時に組み立てる）
        BuildChrome();

        // 初期位置セット
        // 全パネルを左上基準にそろえる（右基準だと最小化で右へ縮み ▼ の位置が動くため）。
        // 重なりは開いた時の自動配置（KmxPanelLayout）で避ける
        var rt = (RectTransform)transform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        isRight = false;
        rt.anchoredPosition = new Vector2(0, 0);
        KmxPanelLayout.Register(rt);

        // 初期値セット
        lastWidth = (int)canvas.pixelRect.width;
        lastHeight = (int)canvas.pixelRect.height;
    }

    /// <summary>
    /// パネルの中身の大きさ（最小化中は展開時の大きさ。余白は含まない）
    /// </summary>
    protected Vector2 PanelSize
    {
        get
        {
            var actual = collapsed ? expandedSize : ((RectTransform)transform).sizeDelta;
            return new Vector2(actual.x, actual.y - KmxUiStyle.PanelPadding);
        }
    }

    /// <summary>
    /// パネルの中身の大きさを変える（余白は自動で足す）。最小化中は展開時の大きさとして控えるだけにする
    /// （最小化中に内容の更新で大きさだけ戻り、中身が空の大きなパネルになるのを防ぐ）
    /// </summary>
    protected void SetPanelSize(Vector2 size)
    {
        // 指定は中身の大きさ。余白とタイトルが収まる幅を足してパネルの大きさにする
        var actual = ToActualSize(size);
        if (collapsed)
        {
            expandedSize = actual;
            return;
        }
        ((RectTransform)transform).sizeDelta = actual;
    }

    /// <summary>
    /// 表示時：他の表示中のパネルと重なっていれば空いている位置へ移す
    /// </summary>
    protected override void OnEnable()
    {
        base.OnEnable();
        if (canvas != null)
        {
            KmxPanelLayout.PlaceWithoutOverlap((RectTransform)transform);
            RenewPosition();
        }
    }

    /// <summary>
    /// 破棄時：自動配置の対象から外す
    /// </summary>
    protected override void OnDestroy()
    {
        base.OnDestroy();
        KmxPanelLayout.Unregister((RectTransform)transform);
    }

    /// <summary>
    /// パネルの枠（背景・タイトルバー・最小化アイコン）を共通の見た目で組み立てる
    /// </summary>
    private void BuildChrome()
    {
        var root = (RectTransform)transform;
        // 背景：本体の画像は透明にし（当たり判定は残す）、中身の範囲に合わせる専用の背景を最背面に置く
        var bg = GetComponent<Image>();
        if (bg != null)
        {
            bg.color = new Color(0f, 0f, 0f, 0f);
        }
        // タイトルバー（最背面・パネルの幅に追従。Prefab の中身はこの帯の下から並んでいる）
        titleBar = new GameObject("TitleBar", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        titleBar.SetParent(root, false);
        titleBar.SetAsFirstSibling();
        titleBar.anchorMin = new Vector2(0f, 1f);
        titleBar.anchorMax = new Vector2(1f, 1f);
        titleBar.pivot = new Vector2(0.5f, 1f);
        titleBar.anchoredPosition = Vector2.zero;
        titleBar.sizeDelta = new Vector2(0f, KmxUiStyle.MenuTitleHeight);
        titleBar.GetComponent<Image>().color = KmxUiStyle.TitleBar;
        background = new GameObject("PanelBackground", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        background.SetParent(root, false);
        background.SetAsFirstSibling();   // タイトルバーより後ろ
        background.anchorMin = new Vector2(0f, 1f);
        background.anchorMax = new Vector2(0f, 1f);
        background.pivot = new Vector2(0f, 1f);
        background.GetComponent<Image>().color = KmxUiStyle.PanelBackground;
        // Prefab の中身の帯（Contents）は背景と二重になるので透明にする。表の見出しの行だけごく薄く残す
        foreach (var img in root.GetComponentsInChildren<Image>(true))
        {
            if ((img.GetComponent<Selectable>() != null) || (img.transform == background) || (img.transform == titleBar))
            {
                continue;
            }
            if (img.name.Contains("Contents") || (img.name == "Scroll View"))
            {
                img.color = img.name.Contains("Title") ? KmxUiStyle.HeaderBand : new Color(0f, 0f, 0f, 0f);
            }
        }
        // 最小化ボタン：画像はやめて当たり判定だけ残し、▼の文字を載せる
        btnEnable.transition = Selectable.Transition.None;
        if (btnEnable.image != null)
        {
            btnEnable.image.sprite = null;
            btnEnable.image.color = new Color(1f, 1f, 1f, 0f);
        }
        var icon = new GameObject("CollapseIcon", typeof(RectTransform), typeof(TextMeshProUGUI));
        collapseIcon = icon.GetComponent<RectTransform>();
        collapseIcon.SetParent(btnEnable.transform, false);
        collapseIcon.anchorMin = new Vector2(0.5f, 0.5f);
        collapseIcon.anchorMax = new Vector2(0.5f, 0.5f);
        collapseIcon.pivot = new Vector2(0.5f, 0.5f);
        collapseIcon.anchoredPosition = Vector2.zero;
        collapseIcon.sizeDelta = new Vector2(24f, 24f);
        var iconText = icon.GetComponent<TextMeshProUGUI>();
        if (titleText != null)
        {
            iconText.font = titleText.font;
        }
        iconText.text = KmxUiStyle.CollapseGlyph;
        iconText.fontSize = 16f;
        iconText.color = KmxUiStyle.Text;
        iconText.alignment = TextAlignmentOptions.Center;
        iconText.raycastTarget = false;
        KmxUiStyle.SetCollapseIcon(collapseIcon, false);
        if (titleText != null)
        {
            titleText.color = KmxUiStyle.Text;
            titleText.raycastTarget = false;   // タイトルバーでドラッグを拾わせる
        }
        // 閉じるボタン（タイトルバー右端。最小化中も右端に出る）
        // 下のメニューのボタンの押下状態は CanvasMenuInfoScript がパネルの表示状態に合わせて戻す
        var close = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
        var closeRt = close.GetComponent<RectTransform>();
        closeRt.SetParent(titleBar, false);
        closeRt.anchorMin = new Vector2(1f, 0.5f);
        closeRt.anchorMax = new Vector2(1f, 0.5f);
        closeRt.pivot = new Vector2(1f, 0.5f);
        closeRt.anchoredPosition = new Vector2(-2f, 0f);
        closeRt.sizeDelta = new Vector2(KmxUiStyle.CloseButtonWidth, KmxUiStyle.MenuTitleHeight);
        close.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);   // 透明（当たり判定のみ）
        var closeBtn = close.GetComponent<Button>();
        closeBtn.transition = Selectable.Transition.None;
        closeBtn.onClick.AddListener(() => gameObject.SetActive(false));
        var closeLabel = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        var closeLabelRt = closeLabel.GetComponent<RectTransform>();
        closeLabelRt.SetParent(closeRt, false);
        closeLabelRt.anchorMin = Vector2.zero;
        closeLabelRt.anchorMax = Vector2.one;
        closeLabelRt.offsetMin = Vector2.zero;
        closeLabelRt.offsetMax = Vector2.zero;
        var closeText = closeLabel.GetComponent<TextMeshProUGUI>();
        if (titleText != null)
        {
            closeText.font = titleText.font;
        }
        closeText.text = KmxUiStyle.CloseGlyph;
        closeText.fontSize = 22f;
        closeText.color = KmxUiStyle.Text;
        closeText.alignment = TextAlignmentOptions.Center;
        closeText.raycastTarget = false;

        // 通常のテキスト（チェックのラベル等）を TextMeshPro に置き換える
        KmxUiStyle.ConvertLegacyTexts(root);
        // 中身の部品（ボタン・チェック・スライダー・入力欄・ドロップダウン・スクロールバー）の色をそろえる
        KmxUiStyle.ApplyToParts(root, new Selectable[] { btnEnable, closeBtn });
        // フォントを NotoSansJP に、大きさをタイトル16pt・本文15ptにそろえる（アイコンはそのまま）
        KmxUiStyle.NormalizeFonts(root, titleText);

        // 中身の入れ物（帯）はパネルの幅いっぱいのまま。端に接する文字と部品だけ内側へ寄せる
        InsetEdgeItems(root);
        foreach (Transform child in root)
        {
            var rt = child as RectTransform;
            if ((rt == null) || (child == titleBar) || (child == btnEnable.transform) || (child == background))
            {
                continue;
            }
            if (!Mathf.Approximately(rt.anchorMin.y, rt.anchorMax.y))
            {
                // 縦に伸びる中身は下に余白を空ける
                rt.offsetMin += new Vector2(0f, KmxUiStyle.PanelPadding);
            }
        }
        var size = root.sizeDelta;
        root.sizeDelta = ToActualSize(new Vector2(size.x, size.y));
    }

    /// <summary>
    /// パネルの左端・右端に接する文字は内側の余白（margin）を付け、部品は接する側を縮めて内側へ寄せる
    /// （帯はパネル幅いっぱいのまま、文字や部品が枠にくっつかないようにする）
    /// </summary>
    private void InsetEdgeItems(RectTransform root)
    {
        var pad = KmxUiStyle.PanelPadding;
        // パネルの左端・右端（ローカル座標）。この時点ではまだ元の基準（右上基準のパネルもある）なので、
        // 0〜幅ではなくパネルの矩形の端と比べる
        var panelRect = root.rect;
        var corners = new Vector3[4];
        bool TouchLeft(RectTransform rt, out bool right)
        {
            rt.GetWorldCorners(corners);
            var min = root.InverseTransformPoint(corners[0]).x;
            var max = root.InverseTransformPoint(corners[2]).x;
            right = max >= panelRect.xMax - 0.5f;
            return min <= panelRect.xMin + 0.5f;
        }
        // 部品（部品の中の文字は部品ごと寄せるので、下の文字の処理では除く）
        var inParts = new HashSet<Transform>();
        foreach (var sel in root.GetComponentsInChildren<Selectable>(true))
        {
            if ((sel == btnEnable) || sel.transform.IsChildOf(titleBar))
            {
                continue;
            }
            foreach (var t in sel.GetComponentsInChildren<Transform>(true))
            {
                inParts.Add(t);
            }
            var rt = (RectTransform)sel.transform;
            if (!Mathf.Approximately(rt.anchorMin.x, rt.anchorMax.x))
            {
                continue;   // 親に合わせて伸びる部品は対象外
            }
            var left = TouchLeft(rt, out var right);
            if (left && right)
            {
                continue;   // 幅いっぱいの部品（ドロップダウン等）は帯と同じ扱い。中に余白を持つので寄せない
            }
            if (left)
            {
                // 左側を縮める（右端の位置は変えない）
                rt.anchoredPosition += new Vector2(pad * (1f - rt.pivot.x), 0f);
                rt.sizeDelta -= new Vector2(pad, 0f);
            }
            if (right)
            {
                rt.anchoredPosition -= new Vector2(pad * rt.pivot.x, 0f);
                rt.sizeDelta -= new Vector2(pad, 0f);
            }
        }
        // 文字
        foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if ((tmp == titleText) || inParts.Contains(tmp.transform) || tmp.transform.IsChildOf(titleBar) || tmp.transform.IsChildOf(btnEnable.transform))
            {
                continue;
            }
            var left = TouchLeft(tmp.rectTransform, out var right);
            var m = tmp.margin;
            if (left)
            {
                m.x = Mathf.Max(m.x, pad);
            }
            if (right)
            {
                m.z = Mathf.Max(m.z, pad);
            }
            if (!left && !right)
            {
                continue;
            }
            tmp.margin = m;
            // 余白を付けると1行に収まらなくなる文字だけ、収まるように少し小さくする（最大70%まで）。
            // 文字幅は、表示中でフォントとマテリアルが揃っている文字だけ測る。実行ファイルでは、まだ表示していない文字は
            // マテリアルが無く、GetPreferredValues が例外になる（Awake が途中で止まり、ロードが最後まで進まなかった）
            if (!tmp.isActiveAndEnabled || (tmp.font == null) || (tmp.font.material == null) || (tmp.fontSharedMaterial == null))
            {
                continue;
            }
            float need;
            try
            {
                need = tmp.GetPreferredValues(tmp.text).x + m.x + m.z;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Menu] 文字幅を測れないため、自動縮小を付けません（{tmp.name}）：{ex.Message}");
                continue;
            }
            if ((need > tmp.rectTransform.rect.width) && !tmp.enableAutoSizing)
            {
                tmp.fontSizeMax = tmp.fontSize;
                tmp.fontSizeMin = Mathf.Max(8f, tmp.fontSize * 0.7f);
                tmp.enableAutoSizing = true;
            }
        }
    }

    /// <summary>
    /// 背景を、パネル本体と表示中の中身（本体の外へはみ出す分を含む）を覆う範囲に合わせる。
    /// 中身は下に余白を付ける。直下の子の矩形だけを足し合わせる（割り当てなし）
    /// </summary>
    private void UpdateBackground()
    {
        if (background == null)
        {
            return;
        }
        var root = (RectTransform)transform;
        var size = root.rect.size;
        // パネル本体（ピボット左上）の範囲から始める
        float xMin = 0f, xMax = size.x, yMin = -size.y, yMax = 0f;
        foreach (Transform child in root)
        {
            // タイトルバーと最小化ボタンはパネル本体の範囲に含まれる（余白を足すと右へはみ出す）
            if ((child == background) || (child == titleBar) || (child == btnEnable.transform) || !child.gameObject.activeSelf)
            {
                continue;
            }
            var rt = child as RectTransform;
            if (rt == null)
            {
                continue;
            }
            var r = rt.rect;
            var p = (Vector2)rt.localPosition;
            xMin = Mathf.Min(xMin, p.x + r.xMin);
            xMax = Mathf.Max(xMax, p.x + r.xMax);
            yMin = Mathf.Min(yMin, p.y + r.yMin - KmxUiStyle.PanelPadding);
            yMax = Mathf.Max(yMax, p.y + r.yMax);
        }
        var pos = new Vector2(xMin, yMax);
        var sizeDelta = new Vector2(xMax - xMin, yMax - yMin);
        if ((background.anchoredPosition != pos) || (background.sizeDelta != sizeDelta))
        {
            background.anchoredPosition = pos;
            background.sizeDelta = sizeDelta;
        }
    }

    /// <summary>
    /// タイトル（▼・文字・×）が収まる最小の幅
    /// </summary>
    private float TitleMinWidth => 40f + (titleText != null ? titleText.preferredWidth : 120f) + 12f + KmxUiStyle.CloseButtonWidth;

    /// <summary>
    /// 中身の大きさ → パネルの実際の大きさ（下の余白を足し、タイトルが収まる幅は確保する）
    /// </summary>
    private Vector2 ToActualSize(Vector2 contents)
    {
        return new Vector2(Mathf.Max(contents.x, TitleMinWidth), contents.y + KmxUiStyle.PanelPadding);
    }

    /// <summary>
    /// 更新
    /// </summary>
    protected override void Update()
    {
        base.Update();
        UpdateBackground();
        if ((lastWidth != (int)canvas.pixelRect.width) || (lastHeight != (int)canvas.pixelRect.height))
        {
            RenewPosition();
            lastWidth = (int)canvas.pixelRect.width;
            lastHeight = (int)canvas.pixelRect.height;
        }
        if (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame)
        {
            // 履歴の表示中は timeScale=0 で Time.time が進まず、全てのクリックがダブルクリック扱いになるため実時間で測る
            float time = Time.unscaledTime;
            DetectClickedText(Mouse.current.rightButton.wasPressedThisFrame, time - lastClickTime < 0.3f);
            lastClickTime = time;
        }
    }

    /// <summary>
    /// イベントセット
    /// </summary>
    public virtual void SetEvents()
    {
        objContents = GetComponentsInChildren<Transform>(true).ToList().FindAll(d => d.name.Contains("Contents"));
        ResetEvents();
        btnEnable.onClick.AddListener(expand_onClick);
    }

    /// <summary>
    /// イベントセット
    /// </summary>
    public virtual void ResetEvents()
    {
        btnEnable.onClick.RemoveAllListeners();
    }

    /// <summary>
    /// 最小化/展開
    /// 以前は「高さが30なら最小化中」と判定しており、元々高さ30のパネルは最小化できず、
    /// 展開時は生成時の大きさに戻すためコードで大きさを変えたパネルが違う大きさになっていた。
    /// 状態を持ち、最小化の直前の大きさと各コンテンツの表示状態を控えて戻す
    /// </summary>
    private void expand_onClick()
    {
        var rect = (RectTransform)transform;
        if (collapsed)
        {
            foreach (var obj in objContents)
            {
                if ((obj != null) && contentsActive.TryGetValue(obj, out var active))
                {
                    obj.gameObject.SetActive(active);
                }
            }
            contentsActive.Clear();
            rect.sizeDelta = expandedSize;
            collapsed = false;
        }
        else
        {
            expandedSize = rect.sizeDelta;
            contentsActive.Clear();
            foreach (var obj in objContents)
            {
                if (obj != null)
                {
                    contentsActive[obj] = obj.gameObject.activeSelf;
                    obj.gameObject.SetActive(false);
                }
            }
            // タイトル文字の右端＋余白＋閉じるボタンの幅にする（タイトルは左から40の位置）
            var width = 40f + (titleText != null ? titleText.preferredWidth : 120f) + 12f + KmxUiStyle.CloseButtonWidth;
            rect.sizeDelta = new Vector2(width, KmxUiStyle.MenuTitleHeight);
            collapsed = true;
        }
        KmxUiStyle.SetCollapseIcon(collapseIcon, collapsed);
        RenewPosition();
    }

    /// <summary>
    /// 移動
    /// </summary>
    /// <param name="eventData"></param>
    public void OnBeginDrag(PointerEventData eventData)
    {
        // タイトルバーで押し始めた時だけ動かす（スライダー等の操作中にパネルが動かないように）
        // ※押し始めの位置は固定でタイトルバーは動くため、ドラッグ中に判定し直すと途中で外れて止まる
        draggingTitle = (titleBar != null) && RectTransformUtility.RectangleContainsScreenPoint(titleBar, eventData.pressPosition, eventData.pressEventCamera);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        draggingTitle = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!draggingTitle)
        {
            return;
        }
        var rectTransform = (RectTransform)transform;
        var x = rectTransform.anchoredPosition.x + eventData.delta.x;
        var y = rectTransform.anchoredPosition.y + eventData.delta.y;
        RenewPosition(x, y);
    }

    /// <summary>
    /// 位置を更新
    /// </summary>
    private void RenewPosition()
    {
        var rectTransform = (RectTransform)transform;
        RenewPosition(rectTransform.anchoredPosition.x, rectTransform.anchoredPosition.y);
    }

    /// <summary>
    /// 位置を更新
    /// </summary>
    private void RenewPosition(float x, float y)
    {
        var rectTransform = (RectTransform)transform;
        if (isRight)
        {
            if (x > 0)
            {
                x = 0;
            }
            else if (x < rectTransform.sizeDelta.x - canvas.pixelRect.width)
            {
                x = rectTransform.sizeDelta.x - canvas.pixelRect.width;
            }
        }
        else
        {
            if (x < 0)
            {
                x = 0;
            }
            else if (x > canvas.pixelRect.width - rectTransform.sizeDelta.x)
            {
                x = canvas.pixelRect.width - rectTransform.sizeDelta.x;
            }
        }
        if (y > 0)
        {
            y = 0;
        }
        else if (y < -canvas.pixelRect.height + rectTransform.sizeDelta.y)
        {
            y = -canvas.pixelRect.height + rectTransform.sizeDelta.y;
        }
        rectTransform.anchoredPosition = new Vector2(x, y);
    }

    /// <summary>
    /// クリックイベント
    /// </summary>
    private void DetectClickedText(bool isRight, bool isDoubleClick)
    {
        Vector2 mousePos = Mouse.current.position.ReadValue();
        pointerEventData = new PointerEventData(eventSystem)
        {
            position = mousePos
        };
        var results = new List<RaycastResult>();
        raycaster.Raycast(pointerEventData, results);
        foreach (var result in results)
        {
            ClickObject(result.gameObject, isRight, isDoubleClick);
        }
    }

    /// <summary>
    /// オブジェクトクリック
    /// </summary>
    /// <param name="name"></param>
    protected virtual void ClickObject(GameObject clickedObject, bool isRight, bool isDoubleClick)
    {
    }
}
