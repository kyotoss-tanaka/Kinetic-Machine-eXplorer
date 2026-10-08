using Parameters;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// ユニットのマニュアル操作の画面（ActUnitInfo でユニットを選ぶと右に出る）。
/// 「マニュアル」を ON にすると上位の指令（タグ）を無視し、ここで入れた値で動かす（Inspector で manual にして値を変えるのと同じ）。
/// 値は数値欄に入れるか、軸の名前を左右にドラッグして変える（Shift で10倍、Ctrl で1/10）。
/// 動かせる軸はユニットが KssBaseScript.GetManualAxes で返す（ロボット・外部入力・段取り替え・動作テーブル）
/// </summary>
public class UnitManualPanel : MonoBehaviour
{
    private const float Width = 280f;
    private const float RowHeight = 26f;

    /// <summary>
    /// 名前をドラッグした時の変化量の倍率（軸ごとの刻み × この倍率 が 1ピクセルあたりの変化量）
    /// </summary>
    private const float DragGain = 10f;

    private RectTransform panel;
    private RectTransform anchorPanel;
    private TMP_Text titleText;
    private Toggle manualToggle;
    private TMP_Text noteText;
    private readonly List<Row> rows = new();
    private readonly List<GameObject> rowObjects = new();

    private KssBaseScript script;
    private UnitSetting unit;
    private bool userMoved;

    private class Row
    {
        public KssBaseScript.ManualAxis axis;
        public TMP_InputField input;
        public TMP_Text label;
    }

    /// <summary>
    /// 画面を作る（ActUnitInfo の Awake から1回）
    /// </summary>
    /// <param name="parent">置く先（ActUnitInfo と同じ親）</param>
    /// <param name="anchor">横に並べる ActUnitInfo の画面</param>
    public static UnitManualPanel Create(RectTransform parent, RectTransform anchor)
    {
        var go = new GameObject("UnitManualPanel", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var p = go.AddComponent<UnitManualPanel>();
        p.Build((RectTransform)go.transform, anchor);
        go.SetActive(false);
        return p;
    }

    private void Build(RectTransform root, RectTransform anchor)
    {
        panel = root;
        anchorPanel = anchor;
        // 親の左上を基準に置く（タイトルバーのドラッグ（Ros2PanelDrag）が左上基準で画面内に収めるため）
        panel.anchorMin = new Vector2(0f, 1f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 1f);
        panel.sizeDelta = new Vector2(Width, 120f);
        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = KmxUiStyle.PanelBackground;

        // タイトルバー（ドラッグで移動）
        var title = MakeRect("TitleBar", panel);
        title.anchorMin = new Vector2(0f, 1f);
        title.anchorMax = new Vector2(1f, 1f);
        title.pivot = new Vector2(0f, 1f);
        title.anchoredPosition = Vector2.zero;
        title.sizeDelta = new Vector2(0f, KmxUiStyle.MenuTitleHeight);
        title.gameObject.AddComponent<Image>().color = KmxUiStyle.TitleBar;
        var drag = title.gameObject.AddComponent<Ros2PanelDrag>();
        drag.target = panel;
        title.gameObject.AddComponent<MovedFlag>().owner = this;
        titleText = MakeLabel(title, "Title", "マニュアル操作", 14, new Vector2(8f, 0f), Width - 16f, KmxUiStyle.MenuTitleHeight);
        titleText.raycastTarget = false;

        manualToggle = MakeToggle(panel, "TogManual", "マニュアル（タグを無視してここの値で動かす）", new Vector2(8f, -KmxUiStyle.MenuTitleHeight - 6f), OnManualChanged);
        noteText = MakeLabel(panel, "Note", "", 12, new Vector2(8f, -KmxUiStyle.MenuTitleHeight - 32f), Width - 16f, 20f);
        noteText.color = new Color(1f, 1f, 1f, 0.6f);
        noteText.raycastTarget = false;
    }

    /// <summary>
    /// ユニットを表示する（マニュアルに対応していなければその旨を出す。null なら閉じる）
    /// </summary>
    public void Show(UnitSetting unitSetting)
    {
        if ((unitSetting == null) || (unitSetting.unitObject == null))
        {
            Hide();
            return;
        }
        if ((unitSetting == unit) && gameObject.activeSelf)
        {
            return;
        }
        unit = unitSetting;
        script = FindScript(unitSetting, out var axes);
        foreach (var o in rowObjects)
        {
            Destroy(o);
        }
        rowObjects.Clear();
        rows.Clear();

        titleText.text = $"マニュアル操作：{unitSetting.name}";
        var y = -KmxUiStyle.MenuTitleHeight - 56f;
        if (axes == null)
        {
            manualToggle.gameObject.SetActive(false);
            noteText.text = script == null ? "動かすユニットではありません" : "このユニットはマニュアルに対応していません";
            noteText.rectTransform.anchoredPosition = new Vector2(8f, -KmxUiStyle.MenuTitleHeight - 8f);
            y = -KmxUiStyle.MenuTitleHeight - 36f;
        }
        else
        {
            manualToggle.gameObject.SetActive(true);
            manualToggle.SetIsOnWithoutNotify(script.ManualMode);
            noteText.text = "名前を左右にドラッグでも変わります（Shift×10 / Ctrl×0.1）";
            noteText.rectTransform.anchoredPosition = new Vector2(8f, -KmxUiStyle.MenuTitleHeight - 32f);
            for (var i = 0; i < axes.Count; i++)
            {
                rows.Add(MakeRow(axes[i], i, y));
                y -= RowHeight;
            }
        }
        panel.sizeDelta = new Vector2(Width, -y + 8f);
        gameObject.SetActive(true);
        if (!userMoved)
        {
            PlaceNextToAnchor();
        }
        Refresh(true);
    }

    /// <summary>
    /// 閉じる
    /// </summary>
    public void Hide()
    {
        unit = null;
        script = null;
        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// ユニットのマニュアルに対応したスクリプトを探す（ロボットはモデル側、軸ユニットはユニット側に付いている）
    /// </summary>
    private static KssBaseScript FindScript(UnitSetting unitSetting, out List<KssBaseScript.ManualAxis> axes)
    {
        axes = null;
        KssBaseScript first = null;
        foreach (var owner in new[] { unitSetting.moveObject, unitSetting.unitObject })
        {
            if (owner == null)
            {
                continue;
            }
            foreach (var s in owner.GetComponents<KssBaseScript>())
            {
                first ??= s;
                var a = s.GetManualAxes();
                if ((a != null) && (a.Count > 0))
                {
                    axes = a;
                    return s;
                }
            }
        }
        return first;
    }

    private void Update()
    {
        if ((script == null) || (unit == null))
        {
            if (gameObject.activeSelf && (unit != null))
            {
                Hide();
            }
            return;
        }
        Refresh(false);
    }

    /// <summary>
    /// 今の値とマニュアルの状態を画面に合わせる（入力中の欄は書き換えない）
    /// </summary>
    private void Refresh(bool force)
    {
        if (script == null)
        {
            return;
        }
        var manual = script.ManualMode;
        if (manualToggle.isOn != manual)
        {
            manualToggle.SetIsOnWithoutNotify(manual);
        }
        foreach (var r in rows)
        {
            if (r.input.interactable != manual)
            {
                r.input.interactable = manual;
            }
            r.label.color = manual ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            if (force || !r.input.isFocused)
            {
                var text = Format(r.axis, r.axis.get());
                if (r.input.text != text)
                {
                    r.input.SetTextWithoutNotify(text);
                }
            }
        }
    }

    private static string Format(KssBaseScript.ManualAxis axis, float v)
    {
        return axis.integer ? Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture) : v.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private void OnManualChanged(bool on)
    {
        if (script != null)
        {
            script.ManualMode = on;
        }
        Refresh(true);
    }

    /// <summary>
    /// 値を入れる（範囲と整数に合わせる）。マニュアルの時だけ
    /// </summary>
    private void SetValue(Row r, float v)
    {
        if ((script == null) || !script.ManualMode)
        {
            return;
        }
        v = Mathf.Clamp(v, r.axis.min, r.axis.max);
        if (r.axis.integer)
        {
            v = Mathf.Round(v);
        }
        r.axis.set(v);
        if (!r.input.isFocused)
        {
            r.input.SetTextWithoutNotify(Format(r.axis, v));
        }
    }

    private Row MakeRow(KssBaseScript.ManualAxis axis, int index, float y)
    {
        var row = new Row { axis = axis };
        var label = MakeLabel(panel, $"Lbl{index}", "↔ " + axis.name, 14, new Vector2(8f, y), 70f, 22f);
        rowObjects.Add(label.gameObject);
        row.label = label;
        var drag = label.gameObject.AddComponent<DragLabel>();
        drag.owner = this;
        drag.row = row;

        var input = MakeInput(panel, $"Inp{index}", new Vector2(82f, y), 130f, 22f);
        input.contentType = axis.integer ? TMP_InputField.ContentType.IntegerNumber : TMP_InputField.ContentType.DecimalNumber;
        input.onEndEdit.AddListener(text =>
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                SetValue(row, v);
            }
            input.SetTextWithoutNotify(Format(axis, axis.get()));
        });
        rowObjects.Add(input.gameObject);
        row.input = input;

        var unitLabel = MakeLabel(panel, $"Unit{index}", axis.unit, 13, new Vector2(218f, y), 50f, 22f);
        unitLabel.raycastTarget = false;
        rowObjects.Add(unitLabel.gameObject);
        return row;
    }

    /// <summary>
    /// ActUnitInfo の右に置く（画面からはみ出す時は左）
    /// </summary>
    private void PlaceNextToAnchor()
    {
        var parent = panel.parent as RectTransform;
        if ((parent == null) || (anchorPanel == null))
        {
            return;
        }
        var corners = new Vector3[4];
        anchorPanel.GetWorldCorners(corners);   // 0=左下 1=左上 2=右上 3=右下
        var topRight = (Vector2)parent.InverseTransformPoint(corners[2]);
        var topLeft = (Vector2)parent.InverseTransformPoint(corners[1]);
        var pos = topRight + new Vector2(8f, 0f);
        if (pos.x + Width > parent.rect.xMax)
        {
            pos = topLeft - new Vector2(8f + Width, 0f);
        }
        panel.anchoredPosition = pos - new Vector2(parent.rect.xMin, parent.rect.yMax);
    }

    // ---- 部品 ----

    private static RectTransform MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static TMP_Text MakeLabel(RectTransform parent, string name, string text, int size, Vector2 pos, float w, float h)
    {
        var rt = MakeRect(name, parent);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(w, h);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = KmxUiStyle.BodyFont;
        t.fontSize = size;
        t.color = Color.white;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.text = text;
        return t;
    }

    private static TMP_InputField MakeInput(RectTransform parent, string name, Vector2 pos, float w, float h)
    {
        var rt = MakeRect(name, parent);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(w, h);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = KmxUiStyle.InputBackground;
        // 部品をそろえてから入力欄を付ける（付けた時点で有効化の処理が走るため、いったん無効にしておく）
        rt.gameObject.SetActive(false);
        var area = MakeRect("Text Area", rt);
        area.anchorMin = Vector2.zero;
        area.anchorMax = Vector2.one;
        area.offsetMin = new Vector2(4f, 0f);
        area.offsetMax = new Vector2(-4f, 0f);
        area.gameObject.AddComponent<RectMask2D>();
        var textRt = MakeRect("Text", area);
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        var text = textRt.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = KmxUiStyle.BodyFont;
        text.fontSize = 14;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineRight;
        text.richText = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        var input = rt.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = area;
        input.textComponent = text;
        input.targetGraphic = img;
        input.text = "0";
        rt.gameObject.SetActive(true);
        return input;
    }

    private static Toggle MakeToggle(RectTransform parent, string name, string label, Vector2 pos, UnityEngine.Events.UnityAction<bool> onChange)
    {
        var rt = MakeRect(name, parent);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(Width - 16f, 22f);
        rt.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);   // 行全体をクリック領域に

        var boxRt = MakeRect("Box", rt);
        boxRt.anchorMin = new Vector2(0f, 0.5f);
        boxRt.anchorMax = new Vector2(0f, 0.5f);
        boxRt.pivot = new Vector2(0f, 0.5f);
        boxRt.anchoredPosition = new Vector2(2f, 0f);
        boxRt.sizeDelta = new Vector2(18f, 18f);
        var boxImg = boxRt.gameObject.AddComponent<Image>();
        boxImg.color = KmxUiStyle.CheckBox;

        var ckRt = MakeRect("Check", boxRt);
        ckRt.anchorMin = new Vector2(0.15f, 0.15f);
        ckRt.anchorMax = new Vector2(0.85f, 0.85f);
        ckRt.offsetMin = Vector2.zero;
        ckRt.offsetMax = Vector2.zero;
        var ckImg = ckRt.gameObject.AddComponent<Image>();
        ckImg.color = KmxUiStyle.CheckMark;

        var toggle = rt.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = boxImg;
        toggle.graphic = ckImg;
        toggle.isOn = false;
        toggle.onValueChanged.AddListener(onChange);

        var lbl = MakeLabel(rt, "Label", label, 13, new Vector2(26f, 0f), Width - 50f, 22f);
        lbl.raycastTarget = false;
        return toggle;
    }

    /// <summary>
    /// タイトルバーをドラッグしたら、以後は ActUnitInfo の横へ戻さない
    /// </summary>
    private class MovedFlag : MonoBehaviour, IBeginDragHandler
    {
        public UnitManualPanel owner;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (owner != null)
            {
                owner.userMoved = true;
            }
        }
    }

    /// <summary>
    /// 軸の名前を左右にドラッグして値を変える（Inspector と同じ操作）
    /// </summary>
    private class DragLabel : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        public UnitManualPanel owner;
        public Row row;
        private float start;
        private float moved;

        public void OnBeginDrag(PointerEventData e)
        {
            start = row.axis.get();
            moved = 0f;
        }

        public void OnDrag(PointerEventData e)
        {
            if ((owner == null) || (owner.script == null) || !owner.script.ManualMode)
            {
                return;
            }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var scale = 1f;
            if ((kb != null) && kb.shiftKey.isPressed)
            {
                scale = 10f;
            }
            else if ((kb != null) && kb.ctrlKey.isPressed)
            {
                scale = 0.1f;
            }
            moved += e.delta.x * row.axis.dragStep * DragGain * scale;
            owner.SetValue(row, start + moved);
        }
    }
}
