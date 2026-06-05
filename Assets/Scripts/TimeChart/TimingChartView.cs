using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Runtime.InteropServices;
#endif
using TMPro;

namespace KyotoSS.TimingChart
{
    /// <summary>
    /// uGUI タイミングチャート表示コンポーネント。
    /// Canvas 配下の RectTransform にアタッチし、DataAsset を設定して使う。
    /// </summary>
    public class TimingChartView : MonoBehaviour
    {
        // ----------------------------------------------------------------
        // Inspector
        // ----------------------------------------------------------------
        [Header("データ")]
        public TimingChartDataAsset Data;

        [Header("表示設定")]
        public float ChannelHeight = 36f;
        public float AnalogHeight = 56f;
        public float LabelWidth = 160f;
        public float TimeHeaderH = 24f;
        public float ToolbarH = 32f;
        public float InitViewMs = 5000f;

        [Header("フォント（省略時はデフォルト）")]
        public TMP_FontAsset Font;

        // ----------------------------------------------------------------
        // 内部状態
        // ----------------------------------------------------------------
        internal float m_ViewStartMs = 0f;
        internal float m_ViewEndMs = 5000f;
        internal float m_CursorMs = -1f;
        internal bool m_Dragging = false;
        private bool m_AutoScroll = false;

        private float m_DragStartX = 0f;
        private float m_DragStartMs = 0f;

        private const float ZOOM_SPEED = 0.15f;
        private const int TIME_LABEL_COUNT = 20;

        // uGUI 参照
        private RectTransform m_Root;
        private RectTransform m_TimeHeader;
        private RectTransform m_ScrollRT;
        private ScrollRect m_VertScroll;
        private RectTransform m_Content;
        private RectTransform m_InputTarget;
        private RectTransform m_CursorLine;
        // 計測モード
        private bool m_MeasureMode = false;
        private float m_MeasureAMs = -1f;  // カーソルA 時刻(ms)
        private float m_MeasureBMs = -1f;  // カーソルB 時刻(ms)
        private bool m_DraggingA = false;
        private bool m_DraggingB = false;
        private bool m_DraggingDelta = false;  // A・B同時ドラッグ
        private float m_DragDeltaStartMs = 0f;   // 同時ドラッグ開始時のカーソルAのms
        private float m_DragDeltaBStartMs = 0f;  // 同時ドラッグ開始時のカーソルBのms
        private float m_DragStartCurMs = 0f;     // ドラッグ開始時のマウスms
        private RectTransform m_MeasureCursorA;
        private RectTransform m_MeasureCursorB;
        private RectTransform m_MeasureDeltaFill;
        private TextMeshProUGUI m_MeasureLabelA;
        private TextMeshProUGUI m_MeasureLabelB;
        private TextMeshProUGUI m_MeasureLabelDelta;
        private Image m_MeasureBtnImg;
        private bool m_MeasureModeLocked = false;  // 絶対モード中に計測ボタンをロック
        private TextMeshProUGUI m_CursorLabel;
        private RectTransform m_Tooltip;
        private TextMeshProUGUI m_TooltipText;
        private TMPro.TMP_Dropdown m_CompareUnitDropdown;
        private Dictionary<string, List<SignalChannel>> m_OverlayChannels = new();
        private HashSet<string> m_DashedBaseChannels = new();
        private Image m_BtnDesignImg;
        private Image m_BtnRecordImg;
        private Image m_BtnCompareImg;
        // アナログチャンネルの現在値ラベル: key=チャンネル名
        private readonly Dictionary<string, TextMeshProUGUI> m_AnalogValueLabels = new();

        private List<WaveformRenderer> m_Renderers = new();

        // モード切り替えボタン
        private Image m_ModeButtonImg;
        private TextMeshProUGUI m_ModeButtonLbl;

        /// <summary>ツールバーのモードボタン押下時に TimeChartController へ通知するコールバック</summary>
        public System.Action OnModeToggleRequested;
        /// <summary>RectTransformのサイズが変わったときに通知（width, height）</summary>
        public System.Action<float, float> OnSizeChanged;
        private Vector2 m_LastSize = Vector2.zero;
        public System.Action<bool> OnDataSwitchRequested;
        public System.Action<string> OnCompareRequested;
        public System.Action<string> OnCompareUnitChanged;
        public System.Action<int> OnCompareChangeIndexChanged;
        /// <summary>データ切り替えボタン押下時のコールバック（isSysRec: true=レコードデータ, false=設計値データ）</summary>

        /// <summary>現在のモード（ボタン表示更新用）</summary>
        public TimeChartController.ChartMode CurrentMode { get; set; } = TimeChartController.ChartMode.Realtime;
        private List<TextMeshProUGUI> m_TimeLabels = new();

        // チャンネル行 GameObject（表示切り替え用）key=チャンネル名
        private Dictionary<string, GameObject> m_ChannelRows = new();
        // シリンダIOグループ: key=シリンダ名、value=IO系チャンネル名リスト（指令・AS）
        private Dictionary<string, List<string>> m_CylIOGroups = new();
        // IO表示状態: key=シリンダ名、value=IO表示中か
        private Dictionary<string, bool> m_CylIOVisible = new();

        // ---- グループ管理 ----
        public class GroupDef
        {
            public string Name;
            public List<string> ChannelNames = new List<string>(); // 所属チャンネル名
            public Color Color = new Color(0.4f, 0.4f, 0.4f);
            public bool Visible = true;
            // 折りたたみヘッダ行（常に表示・ダブルクリックで展開）
            public GameObject HeaderRow = null;
        }
        // key=グループ名
        private Dictionary<string, GroupDef> m_Groups = new();
        // key=チャンネル名、value=グループ名
        private Dictionary<string, string> m_ChannelGroup = new();
        // グループチェックボックス Image参照（チェック状態表示用）key=グループ名
        private Dictionary<string, Image> m_GroupCheckImgs = new();

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("user32.dll")] static extern System.IntPtr LoadCursor(System.IntPtr h, int id);
        [DllImport("user32.dll")] static extern System.IntPtr SetCursor(System.IntPtr h);
#endif
        private const float GROUP_BAR_W = 10f;  // グループ帯の幅
        private const float UNIT_LIST_W = 140f; // 右側ユニット一覧パネルの幅
        private RectTransform m_UnitListPanel;
        private RectTransform m_UnitListContent;
        private Dictionary<string, Toggle> m_UnitToggles = new();
        private bool m_RebuildingUnitList = false;  // 無限ループ防止フラグ
        private bool m_LastHandCursor = false;    // 前フレームのカーソル状態キャッシュ
        // 自動計測
        private enum AutoMeasureMode { Off, Relative, Absolute }
        private AutoMeasureMode m_AutoMeasureMode = AutoMeasureMode.Off;
        private Image m_BtnRelImg;
        private Image m_BtnAbsImg;
        private RectTransform m_AutoMeasureContainer;
        private RectTransform m_GfxLayer;    // 線・破線レイヤー（下）
        private RectTransform m_LabelLayer;  // ラベルレイヤー（上）

        // 自動計測プール（毎フレームの生成・破棄を避けてオブジェクトを再利用）
        private class AutoMeasureArrow
        {
            public RectTransform LineRT; public Image LineImg;
            public RectTransform HeadLRT; public Image HeadLImg;
            public RectTransform HeadRRT; public Image HeadRImg;
            public RectTransform BGRT; public Image BGImg;
            public TextMeshProUGUI Label;
        }
        private readonly List<AutoMeasureArrow> m_ArrowPool = new();
        private int m_ArrowUsed = 0;
        private readonly List<(RectTransform RT, Image Img)> m_DashPool = new();
        private int m_DashUsed = 0;
        private static readonly Color k_RelColor = new Color(0.22f, 0.55f, 0.90f);
        private static readonly Color k_AbsColor = new Color(0.87f, 0.45f, 0.10f);
        private static readonly Color k_BtnRel = new Color(0.10f, 0.30f, 0.60f);
        private static readonly Color k_BtnAbs = new Color(0.55f, 0.25f, 0.05f);
        // 自動計測
        // ユニットOFF前のIO展開状態を保存: key=ユニット名, value=IO展開中かどうか
        private Dictionary<string, bool> m_UnitIOStateBeforeHide = new();

        // ----------------------------------------------------------------
        // ライフサイクル
        // ----------------------------------------------------------------
        private bool m_UIBuilt = false;
        private bool m_NeedsRebuild = false;

        private void Awake()
        {
            m_ViewEndMs = InitViewMs;
        }

        private void Start()
        {
            // TimeChartController から Initialize() が呼ばれていない場合のフォールバック
            if (!m_UIBuilt) Initialize();
        }

        /// <summary>
        /// UI を構築する。TimeChartController から Font/LabelWidth 設定後に呼ぶこと。
        /// 初回のみ BuildUI() を実行し、以降は RebuildChannels() のみ実行する。
        /// </summary>
        public void Initialize()
        {
            if (m_UIBuilt) return;
            m_UIBuilt = true;
            BuildUI();
            if (Data != null) RebuildChannels();
        }

        /// <summary>
        /// チャンネル・IOグループをクリアして表示を再構築する。
        /// ResetAndRegister() から呼ばれる。UI は再構築しない（BuildUI は初回のみ）。
        /// </summary>
        public void Reinitialize()
        {
            if (!m_UIBuilt)
            {
                Initialize();
                return;
            }
            // 次の Update で RebuildChannels を呼ぶ（メインスレッド安全）
            m_NeedsRebuild = true;
        }

        private void Update()
        {
            if (Data == null) return;
            if (m_Root != null)
            {
#if ENABLE_INPUT_SYSTEM
                Vector2 mp = UnityEngine.InputSystem.Mouse.current != null
                    ? UnityEngine.InputSystem.Mouse.current.position.ReadValue()
                    : Vector2.zero;
#else
                Vector2 mp = Input.mousePosition;
#endif
                GlobalScript.IsInTimeChart = RectTransformUtility.RectangleContainsScreenPoint(
                    m_Root, mp, null);
            }

            // IsInTimeChart をマウス位置で毎フレーム更新（ツールチップ経由の抜けを防ぐ）
            if (m_Root != null)
            {
#if ENABLE_INPUT_SYSTEM
                Vector2 mp = UnityEngine.InputSystem.Mouse.current != null
                    ? UnityEngine.InputSystem.Mouse.current.position.ReadValue()
                    : Vector2.zero;
#else
                Vector2 mp = Input.mousePosition;
#endif
                GlobalScript.IsInTimeChart = RectTransformUtility.RectangleContainsScreenPoint(
                    m_Root, mp, null);
            }
            // m_Renderers は SetActive(false) の行も含めて全チャンネル分持つ
            // → Data.Channels.Count との比較で再構築を判定する
            if (m_NeedsRebuild) { m_NeedsRebuild = false; RebuildChannels(); }

            if (m_AutoScroll)
            {
                float latest = GetMaxTime();
                float span = m_ViewEndMs - m_ViewStartMs;
                m_ViewStartMs = Mathf.Max(0f, latest - span);
                m_ViewEndMs = m_ViewStartMs + span;
            }

            UpdateMouseCursor();

            foreach (var r in m_Renderers)
            {
                r.ViewStartMs = m_ViewStartMs;
                r.ViewEndMs = m_ViewEndMs;
                r.Redraw();
            }

            UpdateTimeHeader();
            UpdateCursor();
            if (m_MeasureMode) UpdateMeasure();
            if (m_AutoMeasureMode != AutoMeasureMode.Off)
            {
                UpdateAutoMeasure();
            }

            // サイズ変化を検知して通知
            if (OnSizeChanged != null && m_Root != null)
            {
                Vector2 cur = new Vector2(m_Root.rect.width, m_Root.rect.height);
                if (cur != m_LastSize)
                {
                    m_LastSize = cur;
                    OnSizeChanged.Invoke(cur.x, cur.y);
                }
            }
        }

        // ================================================================
        // UI 構築
        // ================================================================
        private void BuildUI()
        {
            m_Root = GetComponent<RectTransform>();
            if (m_Root == null)
            {
                Debug.LogError("[TimingChartView] RectTransform がありません。");
                return;
            }
            m_Root.anchorMin = Vector2.zero;
            m_Root.anchorMax = Vector2.one;
            m_Root.offsetMin = Vector2.zero;
            m_Root.offsetMax = Vector2.zero;

            var bg = GetOrAdd<Image>(m_Root.gameObject);
            bg.color = new Color(0.10f, 0.10f, 0.10f, 1f);

            BuildToolbar();
            BuildTimeHeader();
            BuildScrollArea();
            BuildOverlays();
        }

        // ---- ツールバー ----
        private void BuildToolbar()
        {
            var rt = MakeChild("Toolbar", m_Root);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -ToolbarH);
            rt.offsetMax = new Vector2(0f, 0f);
            rt.gameObject.AddComponent<Image>().color = new Color(0.18f, 0.18f, 0.18f, 1f);

            // ツールバーも横スクロール可能に（ボタンが収まらない時のため）
            var sr = rt.gameObject.AddComponent<ScrollRect>();
            sr.vertical = false;
            sr.horizontal = true;
            sr.scrollSensitivity = 20f;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.horizontalScrollbar = null;
            sr.verticalScrollbar = null;

            var vpGo = MakeChild("TBViewport", rt);
            vpGo.anchorMin = Vector2.zero;
            vpGo.anchorMax = Vector2.one;
            vpGo.offsetMin = Vector2.zero;
            vpGo.offsetMax = Vector2.zero;
            vpGo.gameObject.AddComponent<RectMask2D>();
            sr.viewport = vpGo;

            var contentGo = MakeChild("TBContent", vpGo);
            contentGo.anchorMin = new Vector2(0f, 0f);
            contentGo.anchorMax = new Vector2(0f, 1f);
            contentGo.pivot = new Vector2(0f, 0.5f);
            contentGo.offsetMin = Vector2.zero;
            contentGo.offsetMax = Vector2.zero;
            contentGo.gameObject.AddComponent<ContentSizeFitter>().horizontalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            var hlg = contentGo.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.padding = new RectOffset(6, 6, 4, 4);
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = false;
            hlg.childControlHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            sr.content = contentGo;

            AddButton(contentGo, "全体表示", () => FitView());
            m_BtnDesignImg = AddButtonEx(contentGo, "設計データ", () => { SetDataBtnActive(0); OnDataSwitchRequested?.Invoke(false); });
            m_BtnRecordImg = AddButtonEx(contentGo, "レコードデータ", () => { SetDataBtnActive(1); OnDataSwitchRequested?.Invoke(true); });
            m_BtnCompareImg = AddButtonEx(contentGo, "比較", () => {
                SetDataBtnActive(2);
                string cur = m_CompareUnitDropdown != null && m_CompareUnitDropdown.value > 0
                    ? m_CompareUnitDropdown.options[m_CompareUnitDropdown.value].text : "";
                UpdateSpinnerActive(!string.IsNullOrEmpty(cur));
                OnCompareRequested?.Invoke(cur);
            });
            m_CompareUnitDropdown = AddDropdown(contentGo, new List<string> { "(なし)" },
                (idx) => {
                    string unitName = m_CompareUnitDropdown.options[idx].text == "(なし)" ? "" :
                        m_CompareUnitDropdown.options[idx].text;
                    OnCompareUnitChanged?.Invoke(unitName);
                    UpdateSpinnerActive(!string.IsNullOrEmpty(unitName));
                });
            AddSpinner(contentGo, 1, 1, 20, (v2) => OnCompareChangeIndexChanged?.Invoke(v2));
            AddMeasureButton(contentGo);
            AddSpacer(contentGo, 8f);
            // 自動計測ボタン（相対・絶対）
            AddAutoMeasureButtons(contentGo);
            // AutoScrollと履歴/リアルタイムボタンは非表示

        }

        // ---- 時間ヘッダ ----
        private void BuildTimeHeader()
        {
            m_TimeHeader = MakeChild("TimeHeader", m_Root);
            m_TimeHeader.anchorMin = new Vector2(0f, 1f);
            m_TimeHeader.anchorMax = new Vector2(1f, 1f);
            m_TimeHeader.pivot = new Vector2(0.5f, 1f);
            m_TimeHeader.offsetMin = new Vector2(GROUP_BAR_W + LabelWidth, -(ToolbarH + TimeHeaderH));
            m_TimeHeader.offsetMax = new Vector2(-UNIT_LIST_W, -ToolbarH);
            m_TimeHeader.gameObject.AddComponent<Image>().color = new Color(0.13f, 0.13f, 0.13f, 1f);

            for (int i = 0; i < TIME_LABEL_COUNT; i++)
            {
                var lbl = MakeTMP("TL" + i, m_TimeHeader, 10, new Color(0.6f, 0.6f, 0.6f));
                lbl.rectTransform.anchorMin = new Vector2(0f, 0f);
                lbl.rectTransform.anchorMax = new Vector2(0f, 1f);
                lbl.rectTransform.pivot = new Vector2(0f, 0.5f);
                lbl.rectTransform.sizeDelta = new Vector2(60f, 0f);
                lbl.gameObject.SetActive(false);
                m_TimeLabels.Add(lbl);
            }
        }

        // ---- チャンネルスクロールエリア ----
        private void BuildScrollArea()
        {
            float topH = ToolbarH + TimeHeaderH;

            m_ScrollRT = MakeChild("ScrollRect", m_Root);
            m_ScrollRT.anchorMin = new Vector2(0f, 0f);
            m_ScrollRT.anchorMax = new Vector2(1f, 1f);
            m_ScrollRT.offsetMin = new Vector2(0f, 0f);
            m_ScrollRT.offsetMax = new Vector2(-UNIT_LIST_W, -topH);

            // 右側ユニット一覧パネル
            m_UnitListPanel = MakeChild("UnitListPanel", m_Root);
            m_UnitListPanel.anchorMin = new Vector2(1f, 0f);
            m_UnitListPanel.anchorMax = new Vector2(1f, 1f);
            m_UnitListPanel.offsetMin = new Vector2(-UNIT_LIST_W, 0f);
            m_UnitListPanel.offsetMax = new Vector2(0f, -topH);
            m_UnitListPanel.gameObject.AddComponent<Image>().color = new Color(0.13f, 0.13f, 0.13f, 1f);

            // スクロール可能なコンテンツ
            var ulVP = MakeChild("ULViewport", m_UnitListPanel);
            ulVP.anchorMin = Vector2.zero; ulVP.anchorMax = Vector2.one;
            ulVP.offsetMin = Vector2.zero; ulVP.offsetMax = Vector2.zero;
            ulVP.gameObject.AddComponent<RectMask2D>();

            var ulContent = MakeChild("ULContent", ulVP);
            ulContent.anchorMin = new Vector2(0f, 1f);
            ulContent.anchorMax = new Vector2(1f, 1f);
            ulContent.pivot = new Vector2(0.5f, 1f);
            ulContent.offsetMin = Vector2.zero;
            ulContent.offsetMax = Vector2.zero;
            var ulCsf = ulContent.gameObject.AddComponent<ContentSizeFitter>();
            ulCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var ulVlg = ulContent.gameObject.AddComponent<VerticalLayoutGroup>();
            ulVlg.childControlHeight = false;
            ulVlg.childControlWidth = true;
            ulVlg.childForceExpandHeight = false;
            ulVlg.childForceExpandWidth = true;
            ulVlg.spacing = 0f;
            ulVlg.padding = new RectOffset(0, 0, 0, 0);

            var ulSR = m_UnitListPanel.gameObject.AddComponent<ScrollRect>();
            ulSR.viewport = ulVP;
            ulSR.content = ulContent;
            ulSR.horizontal = false;
            ulSR.vertical = true;
            ulSR.movementType = ScrollRect.MovementType.Clamped;
            ulSR.scrollSensitivity = 20f;

            // タイトル
            var titleLbl = MakeTMP("Title", ulContent, 9, new Color(0.7f, 0.7f, 0.7f));
            titleLbl.text = "表示ユニット";
            titleLbl.alignment = TextAlignmentOptions.Center;
            titleLbl.raycastTarget = true;  // クリック受け付け
            var titleLE = titleLbl.gameObject.AddComponent<LayoutElement>();
            titleLE.preferredHeight = 24f;
            // ダブルクリックで全表示/全非表示
            var titleET = titleLbl.gameObject.AddComponent<EventTrigger>();
            {
                var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                entry.callback.AddListener(data =>
                {
                    var pd = (PointerEventData)data;
                    if (pd.clickCount < 2) return;
                    // 現在の状態を確認：1つでもONがあれば全OFF、全OFF状態なら全ON
                    bool anyOn = false;
                    foreach (var kv in m_UnitToggles) if (kv.Value.isOn) { anyOn = true; break; }
                    bool newState = !anyOn;
                    foreach (var kv in m_UnitToggles) kv.Value.isOn = newState;
                });
                titleET.triggers.Add(entry);
            }

            // コンテンツ参照を保持（RebuildUnitListで使う）
            m_UnitListContent = ulContent;

            m_VertScroll = m_ScrollRT.gameObject.AddComponent<ScrollRect>();
            m_VertScroll.horizontal = false;
            m_VertScroll.vertical = true;
            m_VertScroll.scrollSensitivity = 0f;  // スクロールは EventTrigger 側で制御
            m_VertScroll.movementType = ScrollRect.MovementType.Clamped;

            // Viewport
            var vpRT = MakeChild("Viewport", m_ScrollRT);
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = Vector2.zero;
            vpRT.offsetMax = Vector2.zero;
            vpRT.gameObject.AddComponent<RectMask2D>();
            m_VertScroll.viewport = vpRT;
            m_InputTarget = vpRT;

            // 縦スクロールバー
            var sbGo = MakeChild("VScrollbar", m_ScrollRT);
            sbGo.anchorMin = new Vector2(1f, 0f);
            sbGo.anchorMax = new Vector2(1f, 1f);
            sbGo.pivot = new Vector2(1f, 0.5f);
            sbGo.sizeDelta = new Vector2(8f, 0f);
            sbGo.anchoredPosition = Vector2.zero;
            var sb = sbGo.gameObject.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            sbGo.gameObject.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
            // スクロールバーのハンドル
            var handleGo = MakeChild("Handle", sbGo);
            handleGo.anchorMin = Vector2.zero;
            handleGo.anchorMax = Vector2.one;
            handleGo.offsetMin = Vector2.zero;
            handleGo.offsetMax = Vector2.zero;
            handleGo.gameObject.AddComponent<Image>().color = new Color(0.45f, 0.45f, 0.45f, 1f);
            sb.handleRect = handleGo;
            sb.targetGraphic = handleGo.gameObject.GetComponent<Image>();
            m_VertScroll.verticalScrollbar = sb;
            m_VertScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            // Viewport を左端からスクロールバー分狭める
            vpRT.offsetMax = new Vector2(-8f, 0f);

            // Content
            var contentRT = MakeChild("Content", vpRT);
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0f, 1f);
            contentRT.offsetMin = Vector2.zero;
            contentRT.offsetMax = Vector2.zero;
            m_Content = contentRT;

            contentRT.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            var vlg = contentRT.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 0f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;

            m_VertScroll.content = contentRT;
            RegisterInputEvents(vpRT);
        }

        // ---- オーバーレイ（カーソル・ツールチップ）----
        private void BuildOverlays()
        {
            var cRt = MakeChild("CursorLine", m_Root);
            cRt.anchorMin = new Vector2(0f, 0f);
            cRt.anchorMax = new Vector2(0f, 1f);
            cRt.pivot = new Vector2(0f, 0f);
            cRt.sizeDelta = new Vector2(1f, -(ToolbarH + TimeHeaderH));
            cRt.anchoredPosition = Vector2.zero;
            var cursorImg = cRt.gameObject.AddComponent<Image>();
            cursorImg.color = new Color(1f, 1f, 0.3f, 0.7f);
            cursorImg.raycastTarget = false;  // クリックをブロックしない
            cRt.gameObject.SetActive(false);
            m_CursorLine = cRt;

            m_CursorLabel = MakeTMP("CursorLabel", m_Root, 10, Color.yellow);
            var clRT = m_CursorLabel.rectTransform;
            clRT.anchorMin = new Vector2(0f, 1f);
            clRT.anchorMax = new Vector2(0f, 1f);
            clRT.pivot = new Vector2(0f, 1f);
            clRT.sizeDelta = new Vector2(70f, 16f);
            clRT.anchoredPosition = Vector2.zero;
            m_CursorLabel.gameObject.SetActive(false);

            // ---- 計測カーソルA（青）----
            m_MeasureCursorA = MakeChild("MeasureCursorA", m_Root);
            m_MeasureCursorA.anchorMin = new Vector2(0f, 0f);
            m_MeasureCursorA.anchorMax = new Vector2(0f, 1f);
            m_MeasureCursorA.pivot = new Vector2(0f, 0f);
            m_MeasureCursorA.sizeDelta = new Vector2(1.5f, -(ToolbarH + TimeHeaderH));
            m_MeasureCursorA.anchoredPosition = new Vector2(0f, 0f);
            var caImg = m_MeasureCursorA.gameObject.AddComponent<Image>();
            caImg.color = new Color(0.90f, 0.25f, 0.25f, 0.9f);
            caImg.raycastTarget = false;
            m_MeasureLabelA = MakeTMP("LblA", m_Root, 10, new Color(0.90f, 0.25f, 0.25f));
            m_MeasureLabelA.alignment = TextAlignmentOptions.TopLeft;
            m_MeasureLabelA.rectTransform.anchorMin = new Vector2(0f, 1f);
            m_MeasureLabelA.rectTransform.anchorMax = new Vector2(0f, 1f);
            m_MeasureLabelA.rectTransform.pivot = new Vector2(0f, 1f);
            m_MeasureLabelA.rectTransform.sizeDelta = new Vector2(90f, 16f);
            m_MeasureLabelA.gameObject.SetActive(false);
            m_MeasureCursorA.gameObject.SetActive(false);

            // ---- 計測カーソルB（オレンジ）----
            m_MeasureCursorB = MakeChild("MeasureCursorB", m_Root);
            m_MeasureCursorB.anchorMin = new Vector2(0f, 0f);
            m_MeasureCursorB.anchorMax = new Vector2(0f, 1f);
            m_MeasureCursorB.pivot = new Vector2(0f, 0f);
            m_MeasureCursorB.sizeDelta = new Vector2(1.5f, -(ToolbarH + TimeHeaderH));
            m_MeasureCursorB.anchoredPosition = new Vector2(0f, 0f);
            var cbImg = m_MeasureCursorB.gameObject.AddComponent<Image>();
            cbImg.color = new Color(0.22f, 0.55f, 0.90f, 0.9f);
            cbImg.raycastTarget = false;
            m_MeasureLabelB = MakeTMP("LblB", m_Root, 10, new Color(0.22f, 0.55f, 0.90f));
            m_MeasureLabelB.alignment = TextAlignmentOptions.TopLeft;
            m_MeasureLabelB.rectTransform.anchorMin = new Vector2(0f, 1f);
            m_MeasureLabelB.rectTransform.anchorMax = new Vector2(0f, 1f);
            m_MeasureLabelB.rectTransform.pivot = new Vector2(0f, 1f);
            m_MeasureLabelB.rectTransform.sizeDelta = new Vector2(90f, 16f);
            m_MeasureLabelB.gameObject.SetActive(false);
            m_MeasureCursorB.gameObject.SetActive(false);

            // ---- Delta塗りつぶし ----
            m_MeasureDeltaFill = MakeChild("MeasureDeltaFill", m_Root);
            m_MeasureDeltaFill.anchorMin = new Vector2(0f, 0f);
            m_MeasureDeltaFill.anchorMax = new Vector2(0f, 1f);
            m_MeasureDeltaFill.pivot = new Vector2(0f, 0f);
            m_MeasureDeltaFill.sizeDelta = new Vector2(0f, -(ToolbarH + TimeHeaderH));
            m_MeasureDeltaFill.anchoredPosition = new Vector2(0f, 0f);
            var dfImg = m_MeasureDeltaFill.gameObject.AddComponent<Image>();
            dfImg.color = new Color(0.22f, 0.55f, 0.90f, 0.08f);
            dfImg.raycastTarget = false;
            m_MeasureDeltaFill.gameObject.SetActive(false);

            // ---- ΔTラベル（波形エリア上部に表示）----
            m_MeasureLabelDelta = MakeTMP("LblDelta", m_Root, 11, new Color(0.11f, 0.72f, 0.54f));
            m_MeasureLabelDelta.alignment = TextAlignmentOptions.MidlineLeft;
            m_MeasureLabelDelta.fontStyle = TMPro.FontStyles.Bold;
            m_MeasureLabelDelta.rectTransform.anchorMin = new Vector2(0f, 1f);
            m_MeasureLabelDelta.rectTransform.anchorMax = new Vector2(1f, 1f);
            m_MeasureLabelDelta.rectTransform.pivot = new Vector2(0f, 1f);
            m_MeasureLabelDelta.rectTransform.anchoredPosition = new Vector2(LabelWidth + GROUP_BAR_W + 8f, -(ToolbarH + TimeHeaderH));
            m_MeasureLabelDelta.rectTransform.sizeDelta = new Vector2(200f, 18f);
            m_MeasureLabelDelta.gameObject.SetActive(false);

            // 自動計測オーバーレイコンテナ
            m_AutoMeasureContainer = MakeChild("AutoMeasureOverlay", m_Root);
            m_AutoMeasureContainer.anchorMin = new Vector2(0f, 0f);
            m_AutoMeasureContainer.anchorMax = new Vector2(1f, 1f);
            m_AutoMeasureContainer.offsetMin = new Vector2(LabelWidth + GROUP_BAR_W, 0f);
            m_AutoMeasureContainer.offsetMax = new Vector2(-UNIT_LIST_W, -(ToolbarH + TimeHeaderH));
            m_AutoMeasureContainer.gameObject.AddComponent<RectMask2D>();

            // 線レイヤー（先に描画 = 後ろ）/ ラベルレイヤー（後に描画 = 前面）
            m_GfxLayer = MakeChild("AML_Gfx", m_AutoMeasureContainer);
            m_GfxLayer.anchorMin = Vector2.zero; m_GfxLayer.anchorMax = Vector2.one;
            m_GfxLayer.offsetMin = m_GfxLayer.offsetMax = Vector2.zero;
            m_LabelLayer = MakeChild("AML_Labels", m_AutoMeasureContainer);
            m_LabelLayer.anchorMin = Vector2.zero; m_LabelLayer.anchorMax = Vector2.one;
            m_LabelLayer.offsetMin = m_LabelLayer.offsetMax = Vector2.zero;

            var ttRT = MakeChild("Tooltip", m_Root);
            ttRT.anchorMin = new Vector2(0f, 0f);
            ttRT.anchorMax = new Vector2(0f, 0f);
            ttRT.pivot = new Vector2(0f, 0f);
            ttRT.sizeDelta = new Vector2(190f, 80f);
            ttRT.anchoredPosition = new Vector2(0f, 40f);
            var ttImg = ttRT.gameObject.AddComponent<Image>();
            ttImg.color = new Color(0.06f, 0.06f, 0.06f, 0.6f);
            ttImg.raycastTarget = false;  // マウス操作をViewportに透過させる
            m_Tooltip = ttRT;



            m_TooltipText = MakeTMP("TTText", ttRT, 10, Color.white);
            var tttRT = m_TooltipText.rectTransform;
            tttRT.anchorMin = Vector2.zero;
            tttRT.anchorMax = Vector2.one;
            tttRT.offsetMin = new Vector2(4f, 4f);
            tttRT.offsetMax = new Vector2(-4f, -4f);
            m_Tooltip.gameObject.SetActive(false);
        }

        // ================================================================
        // チャンネル行構築
        // ================================================================
        public void RebuildChannels()
        {
            // ユニット一覧を先に再構築（m_UnitTogglesを最新状態にする）
            RebuildUnitList();
            foreach (Transform child in m_Content) Destroy(child.gameObject);
            m_Renderers.Clear();
            m_ChannelRows.Clear();
            m_GroupCheckImgs.Clear();
            m_AnalogValueLabels.Clear();
            if (Data == null) return;

            // グループ定義順に行を構築する
            // 同じチャンネルが複数グループに属する場合は各グループの下に表示
            // グループに属さないチャンネルは末尾に追加
            var channelDataMap = new Dictionary<string, SignalChannel>();
            foreach (var ch in Data.Channels)
                channelDataMap[ch.Name] = ch;

            // 表示順リスト: (チャンネル名, グループ名) のペア
            var displayList = new List<(string chName, string grpName)>();
            var builtGroupHeaders = new HashSet<string>();

            // グループ登録順に並べる
            foreach (var grpKv in m_Groups)
            {
                string grpName = grpKv.Key;
                var grpDef = grpKv.Value;
                foreach (var chName in grpDef.ChannelNames)
                    if (channelDataMap.ContainsKey(chName))
                        displayList.Add((chName, grpName));
            }

            // グループに属さないチャンネルを末尾に追加
            var grouped = new HashSet<string>();
            foreach (var (chName, _) in displayList) grouped.Add(chName);
            // ただし m_ChannelGroup に登録済みのもの（グループ名が分かる）は除外済み
            foreach (var ch in Data.Channels)
                if (!grouped.Contains(ch.Name) && !m_ChannelGroup.ContainsKey(ch.Name))
                    displayList.Add((ch.Name, null));

            foreach (var (chName, chGrpName) in displayList)
            {
                if (!channelDataMap.TryGetValue(chName, out var ch)) continue;

                float rowH = ch.Type == SignalType.Analog ? AnalogHeight : ChannelHeight;

                GroupDef chGrp = null;
                if (chGrpName != null) m_Groups.TryGetValue(chGrpName, out chGrp);

                // グループの先頭チャンネルであれば折りたたみヘッダ行を先に挿入
                string headerKey = chGrpName ?? "";
                if (chGrp != null && !builtGroupHeaders.Contains(headerKey))
                {
                    builtGroupHeaders.Add(headerKey);
                    chGrp.HeaderRow = BuildGroupHeaderRow(chGrp, chGrpName);
                }

                // チャンネルがIO系かアナログ行かを判定して表示状態を決める
                bool visible = true;
                bool isIOChannel = false;
                string ioOwner = null;
                foreach (var kv in m_CylIOGroups)
                    if (kv.Value.Contains(ch.Name)) { isIOChannel = true; ioOwner = kv.Key; break; }

                if (isIOChannel)
                {
                    // IO行：ユニットトグルON かつ m_CylIOVisibleがtrue のみ表示
                    bool ioV2 = m_CylIOVisible.TryGetValue(ioOwner, out bool iov) && iov;
                    bool unitOn3 = !m_UnitToggles.TryGetValue(ioOwner, out var togIO3) || togIO3.isOn;
                    visible = ioV2 && unitOn3;
                }
                else if (ch.Type == SignalType.Analog)
                {
                    // アナログ行：ユニットトグルで制御（初回はトグル未生成なのでtrue）
                    visible = !m_UnitToggles.TryGetValue(ch.Name, out var togCh) || togCh.isOn;
                }
                // グループが折りたたまれていれば非表示
                if (chGrp != null && !chGrp.Visible)
                    visible = false;

                // 行コンテナ（HLGをやめてRectTransform直接配置）
                var rowRT = MakeChild("Row_" + ch.Name, m_Content);
                var rowLE = rowRT.gameObject.AddComponent<LayoutElement>();
                rowLE.preferredHeight = rowH;
                rowLE.minHeight = rowH;
                rowLE.flexibleHeight = 0f;
                rowLE.flexibleWidth = 1f;
                rowRT.gameObject.AddComponent<Image>().color = new Color(0.10f, 0.10f, 0.10f, 1f);

                // ---- グループ帯（左端 絶対配置）----
                var grpBarRT = MakeChild("GroupBar", rowRT);
                grpBarRT.anchorMin = new Vector2(0f, 0f);
                grpBarRT.anchorMax = new Vector2(0f, 1f);
                grpBarRT.pivot = new Vector2(0f, 0.5f);
                grpBarRT.sizeDelta = new Vector2(GROUP_BAR_W, 0f);
                grpBarRT.anchoredPosition = Vector2.zero;
                var grpBarImg = grpBarRT.gameObject.AddComponent<Image>();
                grpBarImg.color = chGrp != null
                    ? new Color(chGrp.Color.r * 0.55f, chGrp.Color.g * 0.55f, chGrp.Color.b * 0.55f, 1f)
                    : new Color(0.10f, 0.10f, 0.10f, 1f);

                // ---- ラベル列（グループ帯の右に絶対配置）----
                var lblRT = MakeChild("Label", rowRT);
                lblRT.anchorMin = new Vector2(0f, 0f);
                lblRT.anchorMax = new Vector2(0f, 1f);
                lblRT.pivot = new Vector2(0f, 0.5f);
                lblRT.sizeDelta = new Vector2(LabelWidth, 0f);
                lblRT.anchoredPosition = new Vector2(GROUP_BAR_W, 0f);
                lblRT.gameObject.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);

                // 色バー
                var barRT = MakeChild("ColorBar", lblRT);
                barRT.anchorMin = new Vector2(0f, 0f);
                barRT.anchorMax = new Vector2(0f, 1f);
                barRT.pivot = new Vector2(0f, 0.5f);
                barRT.sizeDelta = new Vector2(3f, 0f);
                barRT.anchoredPosition = Vector2.zero;
                barRT.gameObject.AddComponent<Image>().color = ch.Color;

                // チャンネル名
                string ioName = ch.Name.Contains("/")
                    ? ch.Name.Substring(ch.Name.LastIndexOf('/') + 1)
                    : ch.Name;
                string subLabel = !string.IsNullOrEmpty(ch.SubLabel)
                    ? ch.SubLabel
                    : "";

                if (!string.IsNullOrEmpty(subLabel))
                {
                    // タグ名：中央より上半分
                    var nameLbl = MakeTMP("Name", lblRT, 12, ch.Color);
                    nameLbl.text = ioName;
                    nameLbl.alignment = TextAlignmentOptions.BottomLeft;
                    nameLbl.overflowMode = TextOverflowModes.Ellipsis;
                    var nameRT = nameLbl.rectTransform;
                    nameRT.anchorMin = new Vector2(0f, 0.5f);
                    nameRT.anchorMax = Vector2.one;
                    nameRT.offsetMin = new Vector2(8f, 1f);
                    nameRT.offsetMax = new Vector2(-4f, 0f);

                    // PLCデバイス名：中央より下半分
                    var devLbl = MakeTMP("SubLabel", lblRT, 10, new Color(0.6f, 0.6f, 0.6f));
                    devLbl.text = subLabel;
                    devLbl.alignment = TextAlignmentOptions.TopLeft;
                    devLbl.overflowMode = TextOverflowModes.Ellipsis;
                    var devRT = devLbl.rectTransform;
                    devRT.anchorMin = Vector2.zero;
                    devRT.anchorMax = new Vector2(1f, 0.5f);
                    devRT.offsetMin = new Vector2(8f, 0f);
                    devRT.offsetMax = new Vector2(-4f, -1f);
                }
                else
                {
                    // 通常の1行表示
                    var nameLbl = MakeTMP("Name", lblRT, 13, ch.Color);
                    nameLbl.text = ioName;
                    nameLbl.alignment = TextAlignmentOptions.MidlineLeft;
                    nameLbl.overflowMode = TextOverflowModes.Ellipsis;
                    var nameRT = nameLbl.rectTransform;
                    nameRT.anchorMin = Vector2.zero;
                    nameRT.anchorMax = Vector2.one;
                    nameRT.offsetMin = new Vector2(8f, 2f);
                    nameRT.offsetMax = new Vector2(-4f, -2f);
                }

                // カテゴリ略称
                var catLbl = MakeTMP("Cat", lblRT, 11, new Color(0.5f, 0.5f, 0.5f));
                catLbl.text = CategoryShort(ch.Category);
                catLbl.alignment = TextAlignmentOptions.MidlineRight;
                var catRT = catLbl.rectTransform;
                catRT.anchorMin = Vector2.zero;
                catRT.anchorMax = Vector2.one;
                catRT.offsetMin = new Vector2(4f, 2f);
                catRT.offsetMax = new Vector2(-4f, -2f);

                // アナログチャンネルの場合は現在値ラベルと位置名称を追加
                if (ch.Type == SignalType.Analog)
                {


                    // 位置名称ラベル（波形の高さに合わせてラベル列右端に表示）
                    if (ch.PositionLabels != null && ch.PositionLabels.Count > 0)
                    {
                        foreach (var pl in ch.PositionLabels)
                        {
                            // NormValue は既に0〜1に正規化済みなのでそのまま使う
                            float yRatio = 0.1f + (1f - pl.NormValue) * 0.8f;
                            float anchorY = 1f - yRatio;

                            // 名称がない場合は値のみ表示
                            string labelKey = string.IsNullOrEmpty(pl.Name) ? $"pos_{pl.RealValue:F0}" : pl.Name;
                            string labelText = string.IsNullOrEmpty(pl.Name)
                                ? $"{pl.RealValue:F0}"
                                : $"{pl.Name}: {pl.RealValue:F0}";

                            var plLbl = MakeTMP("PosLabel_" + labelKey, lblRT, 12,
                                new Color(ch.Color.r, ch.Color.g, ch.Color.b, 0.75f));
                            plLbl.text = labelText;
                            plLbl.alignment = TextAlignmentOptions.MidlineRight;
                            plLbl.overflowMode = TextOverflowModes.Ellipsis;
                            plLbl.raycastTarget = false;
                            var plRT = plLbl.rectTransform;
                            plRT.anchorMin = new Vector2(0f, anchorY);
                            plRT.anchorMax = new Vector2(1f, anchorY);
                            plRT.pivot = new Vector2(1f, 0.5f);
                            plRT.sizeDelta = new Vector2(0f, 21f);
                            plRT.anchoredPosition = new Vector2(-4f, 0f);
                        }
                    }
                }

                // シリンダ位置チャンネルにダブルクリック登録（IO展開）
                if (ch.Type == SignalType.Analog && ch.Category == DeviceCategory.Other
                    && m_CylIOGroups.ContainsKey(ch.Name))
                {
                    string cylName = ch.Name;
                    var hintLbl = MakeTMP("Hint", lblRT, 10, new Color(0.7f, 0.7f, 0.7f));
                    hintLbl.text = m_CylIOVisible.TryGetValue(cylName, out bool shown) && shown ? "▲" : "▼";
                    hintLbl.alignment = TextAlignmentOptions.MidlineRight;
                    var hintRT = hintLbl.rectTransform;
                    hintRT.anchorMin = new Vector2(1f, 0f);
                    hintRT.anchorMax = Vector2.one;
                    hintRT.offsetMin = new Vector2(-20f, 2f);
                    hintRT.offsetMax = new Vector2(-4f, -2f);

                    var trigger = lblRT.gameObject.AddComponent<EventTrigger>();
                    float lastCk = -1f;
                    var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                    entry.callback.AddListener((_) =>
                    {
                        float now = Time.unscaledTime;
                        if (now - lastCk < 0.35f)
                        {
                            ToggleCylIO(cylName);
                            bool vis = m_CylIOVisible.TryGetValue(cylName, out bool v2) && v2;
                            hintLbl.text = vis ? "▲" : "▼";
                            lastCk = -1f;
                        }
                        else lastCk = now;
                    });
                    trigger.triggers.Add(entry);
                    var enterE = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    enterE.callback.AddListener((_) => lblRT.GetComponent<Image>().color = new Color(0.22f, 0.22f, 0.22f, 1f));
                    trigger.triggers.Add(enterE);
                    var exitE = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    exitE.callback.AddListener((_) => lblRT.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f));
                    trigger.triggers.Add(exitE);
                }

                // ---- 波形列（ラベル右端〜行右端まで）----
                var waveRT = MakeChild("Wave_" + ch.Name, rowRT);
                waveRT.anchorMin = new Vector2(0f, 0f);
                waveRT.anchorMax = new Vector2(1f, 1f);
                waveRT.offsetMin = new Vector2(GROUP_BAR_W + LabelWidth, 0f);
                waveRT.offsetMax = new Vector2(0f, 0f);
                waveRT.gameObject.AddComponent<RawImage>();

                var wr = waveRT.gameObject.AddComponent<WaveformRenderer>();
                // 同じチャンネルが複数グループに表示される場合は別オブジェクトとして生成
                // SignalChannel をコピーして独立した参照にする
                var chForRenderer = chGrpName != null ? ch.ShallowCopy() : ch;
                wr.Init(chForRenderer, 512, Mathf.RoundToInt(rowH));
                m_Renderers.Add(wr);
                if (m_OverlayChannels.TryGetValue(ch.Name, out var overlays))
                { wr.Overlays.Clear(); foreach (var oc in overlays) wr.Overlays.Add(oc); }
                else { if (wr.Overlays != null) wr.Overlays.Clear(); }


                // m_ChannelRows はグループ名付きキーで登録（複数グループ対応）
                string rowKey = chGrpName != null ? $"{chGrpName}/{ch.Name}" : ch.Name;
                m_ChannelRows[rowKey] = rowRT.gameObject;
                // 後方互換：最初の登録を ch.Name でも引けるようにする
                if (!m_ChannelRows.ContainsKey(ch.Name))
                    m_ChannelRows[ch.Name] = rowRT.gameObject;

                // アナログ行にユニット境界線（最後の子として追加→全子の最前面に描画）
                // GROUP_BAR_W の右端から全幅、グループヘッダと同色の 2px ライン
                if (ch.Type == SignalType.Analog && chGrp != null)
                {
                    var uSepGo = new GameObject("UnitSep");
                    uSepGo.transform.SetParent(rowRT.gameObject.transform, false);
                    var uSepRT = uSepGo.AddComponent<RectTransform>();
                    uSepRT.localScale = Vector3.one;
                    uSepRT.anchorMin = new Vector2(0f, 1f);
                    uSepRT.anchorMax = new Vector2(1f, 1f);
                    uSepRT.pivot = new Vector2(0.5f, 1f);
                    uSepRT.offsetMin = new Vector2(GROUP_BAR_W, -1f);
                    uSepRT.offsetMax = new Vector2(0f, 0f);
                    uSepGo.AddComponent<Image>().color = ch.Color;
                }

                // 初期表示状態を適用
                rowRT.gameObject.SetActive(visible);
                var le2 = rowRT.gameObject.GetComponent<LayoutElement>();
                if (le2 != null) le2.ignoreLayout = !visible;
            }

            Canvas.ForceUpdateCanvases();

            // グループヘッダ行の高さをCanvas更新後に強制再設定
            // （ContentSizeFitter/LayoutGroupによる上書きを防ぐ）
            foreach (var grp in m_Groups.Values)
            {
                if (grp.HeaderRow == null) continue;
                var le = grp.HeaderRow.GetComponent<LayoutElement>();
                if (le != null)
                {
                    le.preferredHeight = ChannelHeight;
                    le.minHeight = ChannelHeight;
                }
                var rt = grp.HeaderRow.GetComponent<RectTransform>();
                if (rt != null) rt.sizeDelta = new Vector2(rt.sizeDelta.x, ChannelHeight);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(m_Content);
        }

        /// <summary>グループの折りたたみヘッダ行を生成してContentに追加する</summary>
        private GameObject BuildGroupHeaderRow(GroupDef grp, string groupName)
        {
            float headerH = ChannelHeight;

            // 行コンテナ（高さ固定）
            var rowGo = new GameObject("GroupHeader_" + groupName);
            rowGo.transform.SetParent(m_Content, false);
            var rowRT = rowGo.AddComponent<RectTransform>();
            rowRT.localScale = Vector3.one;

            var rowLE = rowGo.AddComponent<LayoutElement>();
            rowLE.preferredHeight = headerH;
            rowLE.minHeight = headerH;
            rowLE.flexibleHeight = 0f;
            rowLE.flexibleWidth = 1f;

            rowGo.AddComponent<Image>().color = new Color(
                grp.Color.r * 0.2f, grp.Color.g * 0.2f, grp.Color.b * 0.2f, 1f);

            // 上端境界線（グループ区切りを明確化）
            var sepGo = new GameObject("Separator");
            sepGo.transform.SetParent(rowGo.transform, false);
            var sepRT = sepGo.AddComponent<RectTransform>();
            sepRT.localScale = Vector3.one;
            sepRT.anchorMin = new Vector2(0f, 1f);
            sepRT.anchorMax = new Vector2(1f, 1f);
            sepRT.pivot = new Vector2(0.5f, 1f);
            sepRT.sizeDelta = new Vector2(0f, 2f);
            sepRT.anchoredPosition = Vector2.zero;
            sepGo.AddComponent<Image>().color = new Color(
                Mathf.Min(grp.Color.r * 0.6f + 0.25f, 1f),
                Mathf.Min(grp.Color.g * 0.6f + 0.25f, 1f),
                Mathf.Min(grp.Color.b * 0.6f + 0.25f, 1f), 1f);

            // グループ帯（左端 固定幅 GROUP_BAR_W）
            var barGo = new GameObject("Bar");
            barGo.transform.SetParent(rowGo.transform, false);
            var barRT = barGo.AddComponent<RectTransform>();
            barRT.localScale = Vector3.one;
            barRT.anchorMin = new Vector2(0f, 0f);
            barRT.anchorMax = new Vector2(0f, 1f);
            barRT.pivot = new Vector2(0f, 0.5f);
            barRT.sizeDelta = new Vector2(GROUP_BAR_W, 0f);
            barRT.anchoredPosition = Vector2.zero;
            var barImg = barGo.AddComponent<Image>();
            barImg.color = new Color(grp.Color.r * 0.7f, grp.Color.g * 0.7f, grp.Color.b * 0.7f, 1f);
            m_GroupCheckImgs[groupName] = barImg;

            // グループ名テキスト（グループ帯の右 〜 ラベル幅の右端）
            var nameLbl = MakeTMP("GrpName", rowRT, 12, grp.Color);
            nameLbl.text = grp.Name;
            nameLbl.alignment = TextAlignmentOptions.MidlineLeft;
            nameLbl.fontStyle = FontStyles.Bold;
            nameLbl.overflowMode = TextOverflowModes.Ellipsis;
            nameLbl.enableWordWrapping = false;
            var nameRT = nameLbl.rectTransform;
            nameRT.anchorMin = new Vector2(0f, 0f);
            nameRT.anchorMax = new Vector2(0f, 1f);
            nameRT.pivot = new Vector2(0f, 0.5f);
            nameRT.sizeDelta = new Vector2(LabelWidth - 20f, 0f);
            nameRT.anchoredPosition = new Vector2(GROUP_BAR_W + 6f, 0f);

            // インジケーター（▼/▶）
            var indLbl = MakeTMP("Ind", rowRT, 12, grp.Color);
            indLbl.text = grp.Visible ? "▼" : "▶";
            indLbl.alignment = TextAlignmentOptions.MidlineRight;
            var indRT = indLbl.rectTransform;
            indRT.anchorMin = new Vector2(0f, 0f);
            indRT.anchorMax = new Vector2(0f, 1f);
            indRT.pivot = new Vector2(0f, 0.5f);
            indRT.sizeDelta = new Vector2(20f, 0f);
            indRT.anchoredPosition = new Vector2(GROUP_BAR_W + LabelWidth - 22f, 0f);

            // ダブルクリックイベント（行全体）
            var trigger = rowGo.AddComponent<EventTrigger>();
            float lastClick = -1f;
            var clickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            clickEntry.callback.AddListener((_) =>
            {
                float now = Time.unscaledTime;
                if (now - lastClick < 0.35f)
                {
                    ToggleGroup(groupName, indLbl);
                    lastClick = -1f;
                }
                else lastClick = now;
            });
            trigger.triggers.Add(clickEntry);

            // ホバー
            var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enterEntry.callback.AddListener((_) => rowGo.GetComponent<Image>().color = new Color(
                grp.Color.r * 0.3f, grp.Color.g * 0.3f, grp.Color.b * 0.3f, 1f));
            trigger.triggers.Add(enterEntry);
            var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exitEntry.callback.AddListener((_) => rowGo.GetComponent<Image>().color = new Color(
                grp.Color.r * 0.2f, grp.Color.g * 0.2f, grp.Color.b * 0.2f, 1f));
            trigger.triggers.Add(exitEntry);

            return rowGo;
        }

        /// <summary>ユニットのアナログ行とIO行をまとめて表示/非表示にする</summary>
        private void SetUnitRowsActive(string unitName, bool showAnalog, bool showIO)
        {
            // アナログ行（unitName キー）
            foreach (var kv in m_ChannelRows)
            {
                bool isThisUnit = kv.Key == unitName || kv.Key.EndsWith("/" + unitName);
                if (!isThisUnit) continue;
                // IO行でないことを確認
                bool isIO = false;
                foreach (var iokv in m_CylIOGroups)
                    if (iokv.Value.Contains(unitName)) { isIO = true; break; }
                if (isIO) continue;
                if (kv.Value == null) continue;
                kv.Value.SetActive(showAnalog);
                var le = kv.Value.GetComponent<LayoutElement>();
                if (le != null) le.ignoreLayout = !showAnalog;
            }
            // IO行
            if (m_CylIOGroups.TryGetValue(unitName, out var ioChannels))
            {
                m_CylIOVisible[unitName] = showIO;
                foreach (var chName in ioChannels)
                {
                    if (!m_ChannelRows.TryGetValue(chName, out var go) || go == null) continue;
                    go.SetActive(showIO);
                    var le = go.GetComponent<LayoutElement>();
                    if (le != null) le.ignoreLayout = !showIO;
                }
            }
        }

        /// <summary>シリンダのIO系表示を切り替える（行の再生成はしない）</summary>
        private void ToggleCylIO(string cylName)
        {
            bool current = m_CylIOVisible.TryGetValue(cylName, out bool v) && v;
            ToggleCylIO(cylName, !current);
        }

        private void ToggleCylIO(string cylName, bool show)
        {
            if (!m_CylIOGroups.TryGetValue(cylName, out var ioChannels)) return;
            m_CylIOVisible[cylName] = show;

            foreach (var chName in ioChannels)
            {
                if (m_ChannelRows.TryGetValue(chName, out var go))
                {
                    go.SetActive(show);
                    var le = go.GetComponent<LayoutElement>();
                    if (le != null) le.ignoreLayout = !show;
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(m_Content);
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>
        /// シリンダIOグループを登録する。TimeChartController から呼ぶ。
        /// cylName: シリンダ名（位置チャンネル名）
        /// ioChannelNames: 非表示にするIO系チャンネル名リスト（指令・AS）
        /// initialVisible: 初期表示状態
        /// </summary>
        public void RegisterCylIOGroup(string cylName, List<string> ioChannelNames, bool initialVisible = false)
        {
            m_CylIOGroups[cylName] = ioChannelNames;
            m_CylIOVisible[cylName] = initialVisible;
        }

        // ================================================================
        // グループ管理
        // ================================================================

        /// <summary>グループを登録する（TimeChartController から呼ぶ）</summary>
        public void RegisterGroup(string groupName, List<string> channelNames, Color color)
        {
            // グループが既に存在する場合は追記、なければ新規作成
            if (!m_Groups.TryGetValue(groupName, out var def))
            {
                def = new GroupDef { Name = groupName, Color = color, Visible = true };
                m_Groups[groupName] = def;
            }

            foreach (var ch in channelNames)
            {
                if (string.IsNullOrEmpty(ch)) continue;

                // 同じグループへの重複登録はスキップ
                if (def.ChannelNames.Contains(ch)) continue;

                // 共有IO対応：別グループに登録済みでもこのグループにも追加
                def.ChannelNames.Add(ch);

                // m_ChannelGroup は先着グループを保持（折りたたみ判定の基準）
                if (!m_ChannelGroup.ContainsKey(ch))
                    m_ChannelGroup[ch] = groupName;
            }
        }

        /// <summary>グループ情報をクリアする</summary>
        public void ClearGroups()
        {
            m_Groups.Clear();
            m_ChannelGroup.Clear();
            m_GroupCheckImgs.Clear();
        }

        /// <summary>
        /// グループ先頭行の帯にインジケーターとダブルクリックを設定する。
        /// barImg は RebuildChannels 側で既に付与済みの Image を渡す。
        /// </summary>

        /// <summary>グループの展開/折りたたみを切り替える（ヘッダ行ダブルクリックから呼ばれる）</summary>
        private void ToggleGroup(string groupName, TextMeshProUGUI indicatorLbl)
        {
            if (!m_Groups.TryGetValue(groupName, out var grp)) return;

            grp.Visible = !grp.Visible;

            // インジケーター更新（▶ = 折りたたみ中 / ▼ = 展開中）
            if (indicatorLbl != null) indicatorLbl.text = grp.Visible ? "▼" : "▶";

            // ヘッダ行の帯色更新
            if (m_GroupCheckImgs.TryGetValue(groupName, out var headerBarImg))
                headerBarImg.color = grp.Visible
                    ? new Color(grp.Color.r * 0.7f, grp.Color.g * 0.7f, grp.Color.b * 0.7f, 1f)
                    : new Color(grp.Color.r * 0.35f, grp.Color.g * 0.35f, grp.Color.b * 0.35f, 1f);

            // チャンネル行の表示切り替え
            foreach (var chName in grp.ChannelNames)
            {
                if (!m_ChannelRows.TryGetValue(chName, out var go)) continue;

                // IO系チャンネルかアナログ行かを判定
                bool isIOCh = false;
                string ownerUnit = null;
                foreach (var kv in m_CylIOGroups)
                    if (kv.Value.Contains(chName)) { isIOCh = true; ownerUnit = kv.Key; break; }

                bool finalVisible;
                if (isIOCh)
                {
                    // IO行：グループ表示中 かつ ユニットトグルON かつ m_CylIOVisibleがtrue のみ表示
                    bool ioVis = m_CylIOVisible.TryGetValue(ownerUnit, out bool v) && v;
                    bool unitOn = !m_UnitToggles.TryGetValue(ownerUnit, out var togIO) || togIO.isOn;
                    finalVisible = grp.Visible && ioVis && unitOn;
                }
                else
                {
                    // アナログ行：グループ表示中 かつ ユニットトグルがON のみ表示
                    bool togVis = !m_UnitToggles.TryGetValue(chName, out var tog) || tog.isOn;
                    finalVisible = grp.Visible && togVis;
                }
                go.SetActive(finalVisible);
                var le = go.GetComponent<LayoutElement>();
                if (le != null) le.ignoreLayout = !finalVisible;
            }

            // グループキー付きチャンネル行も同様に処理
            foreach (var chName in grp.ChannelNames)
            {
                string grpKey = $"{groupName}/{chName}";
                if (!m_ChannelRows.TryGetValue(grpKey, out var go2) || go2 == null) continue;

                bool isIOCh2 = false;
                string ownerUnit2 = null;
                foreach (var kv in m_CylIOGroups)
                    if (kv.Value.Contains(chName)) { isIOCh2 = true; ownerUnit2 = kv.Key; break; }

                bool fv2;
                if (isIOCh2)
                {
                    bool ioVis2 = m_CylIOVisible.TryGetValue(ownerUnit2, out bool v2) && v2;
                    bool unitOn2 = !m_UnitToggles.TryGetValue(ownerUnit2, out var togIO2) || togIO2.isOn;
                    fv2 = grp.Visible && ioVis2 && unitOn2;
                }
                else
                {
                    bool togVis2 = !m_UnitToggles.TryGetValue(chName, out var tog2) || tog2.isOn;
                    fv2 = grp.Visible && togVis2;
                }
                go2.SetActive(fv2);
                var le2 = go2.GetComponent<LayoutElement>();
                if (le2 != null) le2.ignoreLayout = !fv2;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(m_Content);
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>PendingCylIOGroups リストを一括登録する（ResetAndRegister から呼ばれる）</summary>
        public void RegisterCylIOGroupsFromPending(List<(string, List<string>)> pending)
        {
            m_CylIOGroups.Clear();
            m_CylIOVisible.Clear();
            foreach (var (cylName, ioNames) in pending)
                RegisterCylIOGroup(cylName, ioNames, initialVisible: false); // IOは初期非表示のまま
        }

        // ================================================================
        // 時間ヘッダ更新
        // ================================================================
        private void UpdateTimeHeader()
        {
            float span = Mathf.Max(m_ViewEndMs - m_ViewStartMs, 1f);
            float w = m_TimeHeader.rect.width;
            if (w <= 0f) return;

            float interval = CalcTickInterval(span, w);
            float first = Mathf.Ceil(m_ViewStartMs / interval) * interval;

            int idx = 0;
            for (float t = first; t <= m_ViewEndMs + interval && idx < m_TimeLabels.Count; t += interval, idx++)
            {
                float x = (t - m_ViewStartMs) / span * w;
                var lbl = m_TimeLabels[idx];
                lbl.gameObject.SetActive(true);
                lbl.text = t >= 1000f ? $"{t / 1000f:F2}s" : $"{t:F0}ms";
                lbl.rectTransform.anchoredPosition = new Vector2(x + 2f, 0f);
            }
            for (; idx < m_TimeLabels.Count; idx++)
                m_TimeLabels[idx].gameObject.SetActive(false);
        }

        // ================================================================
        // カーソル更新
        // ================================================================
        /// <summary>計測モードのカーソルA/Bをマウスに追従させる</summary>
        private void UpdateMeasure()
        {
            if (m_InputTarget == null) return;

#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            Vector2 mousePos = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            bool mouseDown = mouse != null && mouse.leftButton.isPressed;
            bool mouseDownThisFrame = mouse != null && mouse.leftButton.wasPressedThisFrame;
            bool mouseUpThisFrame = mouse != null && mouse.leftButton.wasReleasedThisFrame;
            bool shiftHeld = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
#else
            Vector2 mousePos        = Input.mousePosition;
            bool mouseDown          = Input.GetMouseButton(0);
            bool mouseDownThisFrame = Input.GetMouseButtonDown(0);
            bool mouseUpThisFrame   = Input.GetMouseButtonUp(0);
            bool shiftHeld          = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
            bool inside = RectTransformUtility.RectangleContainsScreenPoint(m_InputTarget, mousePos, null);
            float span = Mathf.Max(m_ViewEndMs - m_ViewStartMs, 1f);
            float plotW = m_InputTarget.rect.width - LabelWidth - GROUP_BAR_W;
            float rootW = m_Root.rect.width - UNIT_LIST_W;

            // マウスのms・Root座標Xを計算
            float curMs = -1f;
            float curRootX = -1f;
            if (inside && plotW > 0f)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    m_Root, mousePos, null, out Vector2 localInRoot);
                curRootX = localInRoot.x + m_Root.rect.width * 0.5f;
                float relX = curRootX - LabelWidth - GROUP_BAR_W;
                float ratio = Mathf.Clamp01(relX / (rootW - LabelWidth - GROUP_BAR_W));
                curMs = m_ViewStartMs + ratio * span;
            }

            // Shift中：マウス位置の行チャンネルの変化点にスナップ
            if (shiftHeld && curMs >= 0f && Data != null)
            {
                float snapRange = span * 0.015f;
                float bestMs = curMs;
                float bestDist = snapRange;

                // マウスが乗っているチャンネル行を特定
                string hoveredCh = null;
                foreach (var kv in m_ChannelRows)
                {
                    if (kv.Value == null || !kv.Value.activeInHierarchy) continue;
                    var rt2 = kv.Value.GetComponent<RectTransform>();
                    if (rt2 == null) continue;
                    if (RectTransformUtility.RectangleContainsScreenPoint(rt2, mousePos, null))
                    { hoveredCh = kv.Key; break; }
                }


                if (hoveredCh != null && Data != null)
                {
                    foreach (var ch in Data.Channels)
                    {
                        bool match = hoveredCh == ch.Name || hoveredCh.EndsWith("/" + ch.Name);
                        if (!match) continue;
                        if (ch.Samples == null || ch.Samples.Count < 2) continue;

                        if (ch.Type == SignalType.Digital)
                        {
                            // デジタル：値が変わる点
                            for (int si = 1; si < ch.Samples.Count; si++)
                            {
                                if (Mathf.Approximately(ch.Samples[si].Value, ch.Samples[si - 1].Value)) continue;
                                float t = ch.Samples[si].TimeMs;
                                float d = Mathf.Abs(t - curMs);
                                if (d < bestDist) { bestDist = d; bestMs = t; }
                            }
                        }
                        else
                        {
                            // アナログ：傾きが変わる点（変化率が前後と異なる点）
                            // = 動き始め・止まり（平坦→傾き、傾き→平坦）
                            for (int si = 1; si < ch.Samples.Count - 1; si++)
                            {
                                float dt0 = ch.Samples[si].TimeMs - ch.Samples[si - 1].TimeMs;
                                float dt1 = ch.Samples[si + 1].TimeMs - ch.Samples[si].TimeMs;
                                if (dt0 <= 0f || dt1 <= 0f) continue;
                                float slope0 = (ch.Samples[si].Value - ch.Samples[si - 1].Value) / dt0;
                                float slope1 = (ch.Samples[si + 1].Value - ch.Samples[si].Value) / dt1;
                                // 一方が平坦（0に近い）でもう一方が傾いている点 = 変化点
                                bool flat0 = Mathf.Abs(slope0) < 0.001f;
                                bool flat1 = Mathf.Abs(slope1) < 0.001f;
                                if (flat0 == flat1) continue; // 両方平坦 or 両方傾き → スキップ
                                float t = ch.Samples[si].TimeMs;
                                float d = Mathf.Abs(t - curMs);
                                if (d < bestDist) { bestDist = d; bestMs = t; }
                            }
                            // 最初と最後のサンプルも対象
                            for (int si = 0; si < ch.Samples.Count; si += ch.Samples.Count - 1)
                            {
                                float t = ch.Samples[si].TimeMs;
                                float d = Mathf.Abs(t - curMs);
                                if (d < bestDist) { bestDist = d; bestMs = t; }
                            }
                        }
                    }
                }
                curMs = bestMs;
            }

            // カーソルA・BのRoot座標Xを計算
            float MsToRootX(float ms)
            {
                if (ms < 0f) return -9999f;
                float r = (ms - m_ViewStartMs) / span;
                return LabelWidth + GROUP_BAR_W + r * (rootW - LabelWidth - GROUP_BAR_W);
            }
            float axRoot = MsToRootX(m_MeasureAMs);
            float bxRoot = MsToRootX(m_MeasureBMs);

            // ドラッグ開始
            if (mouseDownThisFrame && inside && curMs >= 0f)
            {
                float distA = Mathf.Abs(curRootX - axRoot);
                float distB = Mathf.Abs(curRootX - bxRoot);
                bool nearA = m_MeasureAMs >= 0f && distA <= 8f;
                bool nearB = m_MeasureBMs >= 0f && distB <= 8f;

                float msL = Mathf.Min(m_MeasureAMs, m_MeasureBMs);
                float msR = Mathf.Max(m_MeasureAMs, m_MeasureBMs);
                bool inDelta = m_MeasureAMs >= 0f && m_MeasureBMs >= 0f
                    && curMs >= msL && curMs <= msR && !nearA && !nearB;

                m_DraggingA = false; m_DraggingB = false; m_DraggingDelta = false;
                m_DragStartCurMs = curMs;

                if (nearA && (!nearB || distA <= distB)) m_DraggingA = true;
                else if (nearB && m_AutoMeasureMode != AutoMeasureMode.Absolute) m_DraggingB = true;
                else if (inDelta)
                {
                    m_DraggingDelta = true;
                    m_DragDeltaStartMs = m_MeasureAMs;
                    m_DragDeltaBStartMs = m_MeasureBMs;
                }
                else if (m_MeasureAMs < 0f) m_DraggingA = true;
                else if (m_MeasureBMs < 0f && m_AutoMeasureMode != AutoMeasureMode.Absolute) m_DraggingB = true;
            }
            if (mouseUpThisFrame)
            { m_DraggingA = false; m_DraggingB = false; m_DraggingDelta = false; }

            // ドラッグ中の更新
            if (mouseDown && curMs >= 0f)
            {
                if (m_DraggingA) m_MeasureAMs = curMs;
                if (m_DraggingB) m_MeasureBMs = curMs;
                if (m_DraggingDelta)
                {
                    float delta = curMs - m_DragStartCurMs;
                    m_MeasureAMs = m_DragDeltaStartMs + delta;
                    m_MeasureBMs = m_DragDeltaBStartMs + delta;
                }
            }

            // カーソル表示
            void ShowCursor(RectTransform rt, TextMeshProUGUI lbl, float ms, string tag)
            {
                if (ms < 0f) { rt.gameObject.SetActive(false); lbl.gameObject.SetActive(false); return; }
                float r = (ms - m_ViewStartMs) / span;
                if (r < 0f || r > 1f) { rt.gameObject.SetActive(false); lbl.gameObject.SetActive(false); return; }
                float x = LabelWidth + GROUP_BAR_W + r * (rootW - LabelWidth - GROUP_BAR_W);
                rt.gameObject.SetActive(true);
                rt.anchoredPosition = new Vector2(x, 0f);
                lbl.gameObject.SetActive(true);
                lbl.text = $"{tag}: {ms:F1}ms";
                lbl.rectTransform.anchoredPosition = new Vector2(x + 3f, -(ToolbarH + TimeHeaderH) - 2f);
            }
            ShowCursor(m_MeasureCursorA, m_MeasureLabelA, m_MeasureAMs, "A");
            // 絶対モード中はカーソルBを表示しない
            if (m_AutoMeasureMode != AutoMeasureMode.Absolute)
                ShowCursor(m_MeasureCursorB, m_MeasureLabelB, m_MeasureBMs, "B");

            // Delta塗りつぶしとΔTラベル（絶対モード中は非表示）
            if (m_MeasureAMs >= 0f && m_MeasureBMs >= 0f
                && m_AutoMeasureMode != AutoMeasureMode.Absolute)
            {
                float msL = Mathf.Min(m_MeasureAMs, m_MeasureBMs);
                float msR = Mathf.Max(m_MeasureAMs, m_MeasureBMs);
                float rL = Mathf.Clamp01((msL - m_ViewStartMs) / span);
                float rR = Mathf.Clamp01((msR - m_ViewStartMs) / span);
                float pw = rootW - LabelWidth - GROUP_BAR_W;
                float xL = LabelWidth + GROUP_BAR_W + rL * pw;
                float xR = LabelWidth + GROUP_BAR_W + rR * pw;
                m_MeasureDeltaFill.gameObject.SetActive(true);
                m_MeasureDeltaFill.anchoredPosition = new Vector2(xL, 0f);
                m_MeasureDeltaFill.sizeDelta = new Vector2(xR - xL, -(ToolbarH + TimeHeaderH));
                float dt = Mathf.Abs(m_MeasureBMs - m_MeasureAMs);
                m_MeasureLabelDelta.gameObject.SetActive(true);
                m_MeasureLabelDelta.rectTransform.anchoredPosition =
                    new Vector2((xL + xR) * 0.5f - 35f, -(ToolbarH + TimeHeaderH) - 2f);
                m_MeasureLabelDelta.text = $"ΔT={dt:F1}ms";
            }
            else
            {
                m_MeasureDeltaFill.gameObject.SetActive(false);
                m_MeasureLabelDelta.gameObject.SetActive(false);
            }

            // ホバー時ハンドカーソル
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (inside && curRootX >= 0f)
            {
                float distA2 = Mathf.Abs(curRootX - axRoot);
                float distB2 = Mathf.Abs(curRootX - bxRoot);
                bool hoverAB = (m_MeasureAMs >= 0f && distA2 <= 8f) || (m_MeasureBMs >= 0f && distB2 <= 8f);
                float msL2 = Mathf.Min(m_MeasureAMs, m_MeasureBMs);
                float msR2 = Mathf.Max(m_MeasureAMs, m_MeasureBMs);
                bool hoverDelta = m_MeasureAMs >= 0f && m_MeasureBMs >= 0f
                    && curMs >= msL2 && curMs <= msR2 && !hoverAB;
                if (hoverAB || hoverDelta || m_DraggingA || m_DraggingB || m_DraggingDelta)
                    SetCursor(LoadCursor(System.IntPtr.Zero, 32649));
                else
                    SetCursor(LoadCursor(System.IntPtr.Zero, 32512));
            }
#endif
        }

        private void UpdateCursor()
        {
            if (m_CursorMs < 0f || Data == null || m_Root == null)
            {
                m_CursorLine.gameObject.SetActive(false);
                m_CursorLabel.gameObject.SetActive(false);
                m_Tooltip.gameObject.SetActive(false);
                return;
            }

            float span = Mathf.Max(m_ViewEndMs - m_ViewStartMs, 1f);
            float ratio = (m_CursorMs - m_ViewStartMs) / span;
            if (ratio < 0f || ratio > 1f)
            {
                m_CursorLine.gameObject.SetActive(false);
                m_Tooltip.gameObject.SetActive(false);
                return;
            }

            float plotW = m_Root.rect.width - LabelWidth - GROUP_BAR_W - UNIT_LIST_W;
            float cursorX = LabelWidth + GROUP_BAR_W + ratio * plotW;

            m_CursorLine.gameObject.SetActive(true);
            m_CursorLine.anchoredPosition = new Vector2(cursorX, 0f);

            m_CursorLabel.gameObject.SetActive(true);
            m_CursorLabel.text = $"{m_CursorMs:F1}ms";
            m_CursorLabel.rectTransform.anchoredPosition = new Vector2(cursorX + 3f, -(ToolbarH + TimeHeaderH) + 14f);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"T = {m_CursorMs:F1} ms");
            int visibleCount = 0;
            foreach (var ch in Data.Channels)
            {
                // 表示中のチャンネルのみ（非表示行はスキップ）
                if (m_ChannelRows.TryGetValue(ch.Name, out var chRow) && !chRow.activeInHierarchy)
                    continue;
                float v = SampleAt(ch, m_CursorMs);
                sb.AppendLine(ch.Type == SignalType.Digital
                    ? $"{ch.Name}: {(v > 0.5f ? "ON" : "OFF")}"
                    : $"{ch.Name}: {v:F2}");
                visibleCount++;
            }
            m_TooltipText.text = sb.ToString().TrimEnd();
            m_Tooltip.gameObject.SetActive(true);

            float lines = visibleCount + 1;
            m_Tooltip.sizeDelta = new Vector2(190f, lines * 15f + 12f);
            float ttX = Mathf.Min(cursorX + 8f, m_Root.rect.width - 200f);
            m_Tooltip.anchoredPosition = new Vector2(ttX, 40f);
        }

        // ================================================================
        // 入力イベント
        // ================================================================
        private void RegisterInputEvents(RectTransform target)
        {
            var trigger = target.gameObject.AddComponent<EventTrigger>();

            Add(EventTriggerType.BeginDrag, data =>
            {
                var pd = (PointerEventData)data;
                if (pd.button != PointerEventData.InputButton.Middle) return;
                m_Dragging = true;
                m_DragStartX = pd.position.x;
                m_DragStartMs = m_ViewStartMs;
            });

            Add(EventTriggerType.Drag, data =>
            {
                if (!m_Dragging) return;
                var pd = (PointerEventData)data;
                float span = m_ViewEndMs - m_ViewStartMs;
                float plotW = target.rect.width - LabelWidth - GROUP_BAR_W;
                if (plotW <= 0f) return;
                float deltaMs = -(pd.position.x - m_DragStartX) / plotW * span;
                m_ViewStartMs = Mathf.Max(0f, m_DragStartMs + deltaMs);
                m_ViewEndMs = m_ViewStartMs + span;
                m_CursorMs = -1f;
            });

            Add(EventTriggerType.EndDrag, data =>
            {
                var pd = (PointerEventData)data;
                if (pd.button == PointerEventData.InputButton.Middle) m_Dragging = false;
            });

            // マウスが画面内に入った時のコールバック（ExitはUpdateで毎フレーム判定）
            Add(EventTriggerType.PointerEnter, _ => { GlobalScript.IsInTimeChart = true; });

            Add(EventTriggerType.Scroll, data =>
            {
                var pd = (PointerEventData)data;
                float dir = pd.scrollDelta.y;

                // Ctrl + ホイール → 時間軸ズーム
                bool ctrl = false;
#if ENABLE_INPUT_SYSTEM
                ctrl = UnityEngine.InputSystem.Keyboard.current != null &&
                       (UnityEngine.InputSystem.Keyboard.current.ctrlKey.isPressed ||
                        UnityEngine.InputSystem.Keyboard.current.leftCtrlKey.isPressed ||
                        UnityEngine.InputSystem.Keyboard.current.rightCtrlKey.isPressed);
#else
                ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
#endif
                if (ctrl)
                {
                    // 時間軸ズーム（カーソル位置基点）
                    float span = m_ViewEndMs - m_ViewStartMs;
                    float pivot = m_CursorMs >= 0f ? m_CursorMs : m_ViewStartMs + span * 0.5f;
                    float factor = dir > 0f ? (1f - ZOOM_SPEED) : (1f + ZOOM_SPEED);
                    float newSpan = Mathf.Clamp(span * factor, 50f, 3_600_000f);
                    float ratio = Mathf.Clamp01((pivot - m_ViewStartMs) / span);
                    m_ViewStartMs = Mathf.Max(0f, pivot - ratio * newSpan);
                    m_ViewEndMs = m_ViewStartMs + newSpan;
                }
                else
                {
                    // ホイール単体 → 縦スクロール（ScrollRect に委譲）
                    if (m_VertScroll != null)
                    {
                        float scrollAmt = dir * 0.005f;  // スクロール量（小さいほど移動量が少ない）
                        m_VertScroll.verticalNormalizedPosition =
                            Mathf.Clamp01(m_VertScroll.verticalNormalizedPosition + scrollAmt);
                    }
                }
            });

            void Add(EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> cb)
            {
                var e = new EventTrigger.Entry { eventID = type };
                e.callback.AddListener(cb);
                trigger.triggers.Add(e);
            }
        }

        private void UpdateMouseCursor()
        {
            if (m_InputTarget == null) return;
            Vector2 mousePos;
#if ENABLE_INPUT_SYSTEM
            mousePos = UnityEngine.InputSystem.Mouse.current != null
                ? UnityEngine.InputSystem.Mouse.current.position.ReadValue()
                : (Vector2)Input.mousePosition;
#else
            mousePos = Input.mousePosition;
#endif
            bool inside = RectTransformUtility.RectangleContainsScreenPoint(m_InputTarget, mousePos, null);
            if (!inside || m_Dragging)
            {
                if (!m_Dragging) m_CursorMs = -1f;
                return;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                m_InputTarget, mousePos, null, out Vector2 local);

            float plotW = m_InputTarget.rect.width - LabelWidth - GROUP_BAR_W;
            if (plotW <= 0f) return;
            float relX = local.x + m_InputTarget.rect.width * 0.5f - LabelWidth - GROUP_BAR_W;
            m_CursorMs = m_ViewStartMs + relX / plotW * (m_ViewEndMs - m_ViewStartMs);
        }

        // ================================================================
        // アナログ値ラベル更新
        // ================================================================
        private void UpdateAnalogValueLabels()
        {
            if (m_AnalogValueLabels.Count == 0) return;
            float t = m_CursorMs >= 0f ? m_CursorMs : m_ViewEndMs;

            foreach (var kv in m_AnalogValueLabels)
            {
                var lbl = kv.Value;
                if (lbl == null) continue;

                // チャンネルを探す
                SignalChannel ch = null;
                foreach (var c in Data.Channels)
                    if (c.Name == kv.Key) { ch = c; break; }
                if (ch == null) continue;

                float val = SampleAt(ch, t);

                // 最近傍の位置名称を探す
                string posName = "";
                if (ch.PositionLabels != null && ch.PositionLabels.Count > 0)
                {
                    float minDist = float.MaxValue;
                    foreach (var pl in ch.PositionLabels)
                    {
                        float d = Mathf.Abs(pl.NormValue - val);
                        if (d < minDist) { minDist = d; posName = pl.Name; }
                    }
                }

                lbl.text = string.IsNullOrEmpty(posName)
                    ? $"{val:F1}"
                    : $"{posName}\n{val:F1}";
            }
        }

        // ================================================================
        // ビュー操作
        // ================================================================
        public void FitView()
        {
            float max = GetMaxTime();
            m_ViewStartMs = 0f;
            m_ViewEndMs = max * 1.05f;
        }

        private float GetMaxTime()
        {
            float max = InitViewMs;
            if (Data == null) return max;
            foreach (var ch in Data.Channels)
                if (ch.Samples != null && ch.Samples.Count > 0)
                    max = Mathf.Max(max, ch.Samples[ch.Samples.Count - 1].TimeMs);
            return max;
        }

        // ================================================================
        // JSON I/O
        // ================================================================
        private void LoadJson()
        {
#if UNITY_STANDALONE || UNITY_EDITOR
            string path = GetLoadPath();
            if (string.IsNullOrEmpty(path)) return;
            if (Data == null) { Debug.LogWarning("[TimingChart] DataAsset 未設定"); return; }
            Data.FromJson(File.ReadAllText(path));
            RebuildChannels();
            FitView();
#endif
        }

        private void SaveJson()
        {
#if UNITY_STANDALONE || UNITY_EDITOR
            if (Data == null) return;
            string path = GetSavePath();
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, Data.ToJson());
#endif
        }

#if UNITY_STANDALONE || UNITY_EDITOR
        private static string GetLoadPath()
        {
#if USE_SFB
            var p = SFB.StandaloneFileBrowser.OpenFilePanel("JSONを開く", "", "json", false);
            return p.Length > 0 ? p[0] : "";
#else
            string p = Path.Combine(Application.streamingAssetsPath, "timingchart.json");
            return File.Exists(p) ? p : "";
#endif
        }

        private static string GetSavePath()
        {
#if USE_SFB
            return SFB.StandaloneFileBrowser.SaveFilePanel("JSONを保存", "", "timingchart", "json");
#else
            return Path.Combine(Application.streamingAssetsPath, "timingchart.json");
#endif
        }
#endif

        // ================================================================
        // ユーティリティ
        // ================================================================
        private void AddAutoMeasureButtons(RectTransform parent)
        {
            m_BtnRelImg = AddButtonEx(parent, "相対", () =>
            {
                m_AutoMeasureMode = m_AutoMeasureMode == AutoMeasureMode.Relative
                    ? AutoMeasureMode.Off : AutoMeasureMode.Relative;
                m_BtnRelImg.color = m_AutoMeasureMode == AutoMeasureMode.Relative ? k_BtnRel : k_BtnNormal;
                m_BtnAbsImg.color = k_BtnNormal;
                RefreshAutoMeasure();
            });
            m_BtnAbsImg = AddButtonEx(parent, "絶対", () =>
            {
                bool turningOn = m_AutoMeasureMode != AutoMeasureMode.Absolute;
                m_AutoMeasureMode = turningOn ? AutoMeasureMode.Absolute : AutoMeasureMode.Off;
                m_BtnAbsImg.color = turningOn ? k_BtnAbs : k_BtnNormal;
                m_BtnRelImg.color = k_BtnNormal;

                if (turningOn)
                {
                    // 計測モードを強制ON
                    if (!m_MeasureMode)
                    {
                        m_MeasureMode = true;
                    }
                    // 計測ボタンをロック（暗くして押せない）
                    m_MeasureModeLocked = true;
                    m_MeasureBtnImg.color = new Color(0.10f, 0.25f, 0.42f);
                    // カーソルBをリセット・非表示
                    m_MeasureBMs = -1f;
                    m_DraggingB = false;
                    if (m_MeasureCursorB != null) m_MeasureCursorB.gameObject.SetActive(false);
                    if (m_MeasureLabelB != null) m_MeasureLabelB.gameObject.SetActive(false);
                    if (m_MeasureDeltaFill != null) m_MeasureDeltaFill.gameObject.SetActive(false);
                    if (m_MeasureLabelDelta != null) m_MeasureLabelDelta.gameObject.SetActive(false);
                }
                else
                {
                    // 計測ボタンのロック解除・色を計測状態に合わせて復元
                    m_MeasureModeLocked = false;
                    m_MeasureBtnImg.color = m_MeasureMode
                        ? new Color(0.15f, 0.40f, 0.70f) : new Color(0.28f, 0.28f, 0.28f);
                }
                RefreshAutoMeasure();
            });
        }

        private void RefreshAutoMeasure()
        {
            HideAutoMeasurePool();
        }

        // プール全体を非表示にしてカウントをリセット（毎フレーム先頭で呼ぶ）
        private void HideAutoMeasurePool()
        {
            for (int i = 0; i < m_ArrowUsed; i++)
            {
                var a = m_ArrowPool[i];
                a.LineRT.gameObject.SetActive(false);
                a.HeadLRT.gameObject.SetActive(false);
                a.HeadRRT.gameObject.SetActive(false);
                a.BGRT.gameObject.SetActive(false);
            }
            m_ArrowUsed = 0;
            for (int i = 0; i < m_DashUsed; i++)
                m_DashPool[i].RT.gameObject.SetActive(false);
            m_DashUsed = 0;
        }

        private void UpdateAutoMeasure()
        {
            if (m_AutoMeasureMode == AutoMeasureMode.Off) return;
            if (Data == null || m_AutoMeasureContainer == null) return;

            HideAutoMeasurePool();

            float span = Mathf.Max(m_ViewEndMs - m_ViewStartMs, 1f);
            float plotW = m_AutoMeasureContainer.rect.width;
            float containerH = m_AutoMeasureContainer.rect.height;   // コンテナ高さ（anchoredPosition変換に必要）
            if (plotW <= 0f || containerH <= 0f) return;

            bool absMode = m_AutoMeasureMode == AutoMeasureMode.Absolute;
            if (absMode && m_MeasureAMs < 0f) return;
            float baseMs = absMode ? m_MeasureAMs : 0f;

            var chDict = new Dictionary<string, SignalChannel>();
            foreach (var c in Data.Channels)
                if (!chDict.ContainsKey(c.Name)) chDict[c.Name] = c;

            var worldCorners = new Vector3[4];
            var processedGO = new HashSet<GameObject>();
            foreach (var kv in m_ChannelRows)
            {
                if (kv.Value == null || !kv.Value.activeInHierarchy) continue;
                if (!processedGO.Add(kv.Value)) continue;

                string chKey = kv.Key.Contains("/")
                    ? kv.Key.Substring(kv.Key.LastIndexOf('/') + 1) : kv.Key;
                if (!chDict.TryGetValue(chKey, out var ch)) continue;
                if (ch.Samples == null || ch.Samples.Count < 2) continue;
                if (ch.Type == SignalType.Analog && ch.Category == DeviceCategory.Motor) continue;

                var rowRT = kv.Value.GetComponent<RectTransform>();
                if (rowRT == null) continue;
                float rowH = rowRT.rect.height;

                // ----------------------------------------------------------------
                // 行の上端Y をコンテナの anchoredPosition 空間で正確に取得
                // GetWorldCorners で実際のワールド座標を取得し、コンテナ local に変換する。
                // anchorMin=(0,1) の anchoredPosition.y = localY - containerH/2
                // ----------------------------------------------------------------
                rowRT.GetWorldCorners(worldCorners);
                // worldCorners[1] = top-left（Y up のワールド空間）
                var topInContainer = m_AutoMeasureContainer.InverseTransformPoint(worldCorners[1]);
                // コンテナ pivot = (0.5,0.5) → local top = +containerH/2
                float rowTopY = topInContainer.y - containerH * 0.5f;
                // rowTopY = 0: コンテナ上端 / 負値: コンテナ上端より下

                // 変化点を収集
                var pts = new List<float>();
                for (int si = 1; si < ch.Samples.Count; si++)
                {
                    float t = ch.Samples[si].TimeMs;
                    if (t < m_ViewStartMs - span * 0.1f) continue;
                    if (t > m_ViewEndMs + span * 0.1f) break;
                    if (ch.Type == SignalType.Digital)
                    {
                        if (!Mathf.Approximately(ch.Samples[si].Value, ch.Samples[si - 1].Value))
                            pts.Add(t);
                    }
                    else
                    {
                        if (si >= ch.Samples.Count - 1) continue;
                        float dt0 = ch.Samples[si].TimeMs - ch.Samples[si - 1].TimeMs;
                        float dt1 = ch.Samples[si + 1].TimeMs - ch.Samples[si].TimeMs;
                        if (dt0 <= 0f || dt1 <= 0f) continue;
                        float s0 = (ch.Samples[si].Value - ch.Samples[si - 1].Value) / dt0;
                        float s1 = (ch.Samples[si + 1].Value - ch.Samples[si].Value) / dt1;
                        if ((Mathf.Abs(s0) < 0.001f) != (Mathf.Abs(s1) < 0.001f)) pts.Add(t);
                    }
                }
                if (pts.Count == 0) continue;

                Color col = absMode ? k_AbsColor : k_RelColor;
                // 行中央（rowTopY = 行上端, 下方向が負値 → -0.5*rowH で中央）
                float centerY = rowTopY - rowH * 0.5f;
                float fontSz = Mathf.Clamp(rowH * 0.38f, 8f, 14f);

                // WaveformRenderer と同じ波形描画域: 行上端から 10% ~ 90%
                float waveTopY = rowTopY - rowH * 0.10f;
                float waveH = rowH * 0.80f;
                float dashH = Mathf.Max(3f, fontSz * 0.4f);
                float gapH = dashH * 0.8f;
                Color dashCol = new Color(col.r, col.g, col.b, 0.55f);

                // ---- 縦破線：変化点ごとに1本（矢印ループとは独立）----
                // 絶対モードはカーソルA位置にも破線を描く
                if (absMode)
                {
                    float xBase = ((baseMs - m_ViewStartMs) / span) * plotW;
                    if (xBase >= 0f && xBase <= plotW)
                        DrawPoolDashLine(xBase, dashCol, waveTopY, waveH, dashH, gapH);
                }
                foreach (float t in pts)
                {
                    float xT = ((t - m_ViewStartMs) / span) * plotW;
                    if (xT >= 0f && xT <= plotW)
                        DrawPoolDashLine(xT, dashCol, waveTopY, waveH, dashH, gapH);
                }

                // ---- 計測矢印・ラベル ----
                // 絶対モード: 矢印なし・ラベルを変化点X・行中央Yに表示
                // 相対モード: 矢印あり・ラベルを矢印中央X・行中央Yに表示
                float bgW = Mathf.Clamp(fontSz * 4.5f, 40f, 70f);
                float bgH = fontSz + 5f;
                for (int pi = absMode ? 0 : 1; pi < pts.Count; pi++)
                {
                    float tCur = pts[pi];
                    float tPrev = absMode ? baseMs : pts[pi - 1];
                    float diff = tCur - tPrev;

                    float xC = ((tCur - m_ViewStartMs) / span) * plotW;
                    float xP = ((tPrev - m_ViewStartMs) / span) * plotW;
                    float xCC = Mathf.Clamp(xC, 0f, plotW);
                    float xPC = Mathf.Clamp(xP, 0f, plotW);

                    var arrow = GetPoolArrow();
                    arrow.Label.text = $"{diff:F0}";
                    arrow.Label.fontSize = fontSz;
                    arrow.BGRT.sizeDelta = new Vector2(bgW, bgH);

                    if (absMode)
                    {
                        // 絶対モードは矢印不要：先に Line/Heads を非表示にしてから範囲チェック
                        arrow.LineRT.gameObject.SetActive(false);
                        arrow.HeadLRT.gameObject.SetActive(false);
                        arrow.HeadRRT.gameObject.SetActive(false);
                        if (xC < 0f || xC > plotW) { arrow.BGRT.gameObject.SetActive(false); continue; }
                        arrow.BGRT.anchoredPosition = new Vector2(xCC, centerY);
                    }
                    else
                    {
                        // 矢印 + ラベルを矢印中央（行中央）に配置
                        if (xC < 0f && xP < 0f) { arrow.BGRT.gameObject.SetActive(false); continue; }
                        if (xC > plotW && xP > plotW) { arrow.BGRT.gameObject.SetActive(false); continue; }
                        float w = Mathf.Max(xCC - xPC, 1f);
                        float mid = (xPC + xCC) * 0.5f;

                        arrow.LineRT.anchoredPosition = new Vector2(xPC, centerY);
                        arrow.LineRT.sizeDelta = new Vector2(w, 1.5f);
                        arrow.LineImg.color = col;

                        bool showL = xP >= -1f;
                        bool showR = xC <= plotW + 1f;
                        arrow.HeadLRT.gameObject.SetActive(showL);
                        if (showL) { arrow.HeadLRT.anchoredPosition = new Vector2(xPC, centerY); arrow.HeadLImg.color = col; }
                        arrow.HeadRRT.gameObject.SetActive(showR);
                        if (showR) { arrow.HeadRRT.anchoredPosition = new Vector2(xCC, centerY); arrow.HeadRImg.color = col; }

                        arrow.BGRT.anchoredPosition = new Vector2(mid, centerY);
                    }
                }
            }
        }

        private void DrawPoolDashLine(float x, Color dashCol, float waveTopY, float waveH,
                                      float dashH, float gapH)
        {
            float yOff = 0f;
            while (yOff < waveH)
            {
                float segH = Mathf.Min(dashH, waveH - yOff);
                var (dRT, dImg) = GetPoolDash();
                dRT.anchoredPosition = new Vector2(x, waveTopY - yOff);
                dRT.sizeDelta = new Vector2(1f, segH);
                dImg.color = dashCol;
                yOff += dashH + gapH;
            }
        }

        // ---- プール取得ヘルパー ----

        private AutoMeasureArrow GetPoolArrow()
        {
            AutoMeasureArrow a;
            if (m_ArrowUsed < m_ArrowPool.Count)
            {
                a = m_ArrowPool[m_ArrowUsed];
            }
            else
            {
                a = new AutoMeasureArrow();
                // 水平線（GfxLayer = 下レイヤー）
                var lineGo = new GameObject("AML_L");
                lineGo.transform.SetParent(m_GfxLayer, false);
                a.LineRT = lineGo.AddComponent<RectTransform>();
                a.LineRT.anchorMin = new Vector2(0f, 1f); a.LineRT.anchorMax = new Vector2(0f, 1f);
                a.LineRT.pivot = new Vector2(0f, 0.5f);
                a.LineImg = lineGo.AddComponent<Image>(); a.LineImg.raycastTarget = false;
                // 左矢印頭（GfxLayer）
                var hlGo = new GameObject("AML_HL");
                hlGo.transform.SetParent(m_GfxLayer, false);
                a.HeadLRT = hlGo.AddComponent<RectTransform>();
                a.HeadLRT.anchorMin = new Vector2(0f, 1f); a.HeadLRT.anchorMax = new Vector2(0f, 1f);
                a.HeadLRT.pivot = new Vector2(0.5f, 0.5f);
                a.HeadLRT.sizeDelta = new Vector2(6f, 6f);
                a.HeadLImg = hlGo.AddComponent<Image>(); a.HeadLImg.raycastTarget = false;
                hlGo.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                // 右矢印頭（GfxLayer）
                var hrGo = new GameObject("AML_HR");
                hrGo.transform.SetParent(m_GfxLayer, false);
                a.HeadRRT = hrGo.AddComponent<RectTransform>();
                a.HeadRRT.anchorMin = new Vector2(0f, 1f); a.HeadRRT.anchorMax = new Vector2(0f, 1f);
                a.HeadRRT.pivot = new Vector2(0.5f, 0.5f);
                a.HeadRRT.sizeDelta = new Vector2(6f, 6f);
                a.HeadRImg = hrGo.AddComponent<Image>(); a.HeadRImg.raycastTarget = false;
                hrGo.transform.localRotation = Quaternion.Euler(0f, 0f, -45f);
                // 背景 + ラベル（LabelLayer = 上レイヤー）
                var bgGo = new GameObject("AML_BG");
                bgGo.transform.SetParent(m_LabelLayer, false);
                a.BGRT = bgGo.AddComponent<RectTransform>();
                a.BGRT.anchorMin = new Vector2(0f, 1f); a.BGRT.anchorMax = new Vector2(0f, 1f);
                a.BGRT.pivot = new Vector2(0.5f, 0.5f);
                a.BGImg = bgGo.AddComponent<Image>();
                a.BGImg.color = new Color(0.06f, 0.06f, 0.06f, 0.85f); a.BGImg.raycastTarget = false;
                a.Label = MakeTMP("T", bgGo.transform, 10, Color.white);
                a.Label.enableAutoSizing = false;
                a.Label.alignment = TextAlignmentOptions.Center;
                a.Label.rectTransform.anchorMin = Vector2.zero;
                a.Label.rectTransform.anchorMax = Vector2.one;
                a.Label.rectTransform.offsetMin = Vector2.zero;
                a.Label.rectTransform.offsetMax = Vector2.zero;
                m_ArrowPool.Add(a);
            }
            m_ArrowUsed++;
            a.LineRT.gameObject.SetActive(true);
            a.HeadLRT.gameObject.SetActive(true);
            a.HeadRRT.gameObject.SetActive(true);
            a.BGRT.gameObject.SetActive(true);
            return a;
        }

        private (RectTransform RT, Image Img) GetPoolDash()
        {
            if (m_DashUsed < m_DashPool.Count)
            {
                var d = m_DashPool[m_DashUsed++];
                d.RT.gameObject.SetActive(true);
                return d;
            }
            var go = new GameObject("AML_DL");
            go.transform.SetParent(m_GfxLayer, false);   // 破線も GfxLayer（下レイヤー）
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            var img = go.AddComponent<Image>(); img.raycastTarget = false;
            var entry = (rt, img);
            m_DashPool.Add(entry);
            m_DashUsed++;
            return entry;
        }

        private void AddMeasureButton(RectTransform parent)
        {
            var go = new GameObject("Btn_Measure");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>(); rt.localScale = Vector3.one;
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 60f;
            le.preferredHeight = 24f;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.28f, 0.28f, 0.28f);
            m_MeasureBtnImg = img;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var cb = ColorBlock.defaultColorBlock;
            cb.normalColor = Color.white; cb.colorMultiplier = 1f;
            btn.colors = cb;
            btn.onClick.AddListener(() =>
            {
                if (m_MeasureModeLocked) return;  // 絶対モード中はロック
                m_MeasureMode = !m_MeasureMode;
                m_MeasureBtnImg.color = m_MeasureMode
                    ? new Color(0.15f, 0.40f, 0.70f)
                    : new Color(0.28f, 0.28f, 0.28f);
                if (!m_MeasureMode)
                {
                    // 計測モード終了：カーソルをリセット
                    m_MeasureAMs = -1f; m_MeasureBMs = -1f;
                    m_DraggingA = false; m_DraggingB = false;
                    if (m_MeasureCursorA != null) m_MeasureCursorA.gameObject.SetActive(false);
                    if (m_MeasureCursorB != null) m_MeasureCursorB.gameObject.SetActive(false);
                    if (m_MeasureDeltaFill != null) m_MeasureDeltaFill.gameObject.SetActive(false);
                    if (m_MeasureLabelDelta != null) m_MeasureLabelDelta.gameObject.SetActive(false);
                    if (m_MeasureLabelA != null) m_MeasureLabelA.gameObject.SetActive(false);
                    if (m_MeasureLabelB != null) m_MeasureLabelB.gameObject.SetActive(false);
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                    // 通常カーソルに戻す
                    SetCursor(LoadCursor(System.IntPtr.Zero, 32512));
                    m_LastHandCursor = false;
#endif
                }
            });
            var lbl = MakeTMP("Text", go.transform, 11, Color.white);
            lbl.text = "計測";
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.rectTransform.anchorMin = Vector2.zero;
            lbl.rectTransform.anchorMax = Vector2.one;
            lbl.rectTransform.offsetMin = Vector2.zero;
            lbl.rectTransform.offsetMax = Vector2.zero;
        }

        private void AddModeButton(RectTransform parent)
        {
            var go = new GameObject("Btn_Mode");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 100f;
            le.preferredHeight = 24f;
            m_ModeButtonImg = go.AddComponent<Image>();
            m_ModeButtonImg.color = new Color(0.2f, 0.35f, 0.55f); // リアルタイム=青系
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = m_ModeButtonImg;
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.3f, 0.45f, 0.65f);
            colors.pressedColor = new Color(0.15f, 0.25f, 0.45f);
            btn.colors = colors;
            btn.onClick.AddListener(() => OnModeToggleRequested?.Invoke());
            m_ModeButtonLbl = MakeTMP("Text", go.transform, 11, Color.white);
            m_ModeButtonLbl.text = "● リアルタイム";
            m_ModeButtonLbl.alignment = TextAlignmentOptions.Center;
            m_ModeButtonLbl.rectTransform.anchorMin = Vector2.zero;
            m_ModeButtonLbl.rectTransform.anchorMax = Vector2.one;
            m_ModeButtonLbl.rectTransform.offsetMin = Vector2.zero;
            m_ModeButtonLbl.rectTransform.offsetMax = Vector2.zero;
        }

        /// <summary>モードボタンの表示をモードに合わせて更新する</summary>
        /// <summary>右側ユニット一覧パネルを再構築する</summary>
        private void RebuildUnitList()
        {
            if (m_UnitListContent == null) return;
            if (m_RebuildingUnitList) return;
            m_RebuildingUnitList = true;

            // レイアウト設定を強制
            var ulVlgForce = m_UnitListContent.GetComponent<VerticalLayoutGroup>();
            if (ulVlgForce != null)
            {
                ulVlgForce.spacing = 0f;
                ulVlgForce.padding = new RectOffset(0, 0, 0, 0);
                ulVlgForce.childControlHeight = true;   // 子の高さを制御する
                ulVlgForce.childForceExpandHeight = false;
            }

            // 既存トグルの状態を保存してから削除
            var savedStates = new Dictionary<string, bool>();
            foreach (var kv in m_UnitToggles)
                savedStates[kv.Key] = kv.Value.isOn;

            m_UnitToggles.Clear();
            var children = new List<Transform>();
            foreach (Transform t in m_UnitListContent) children.Add(t);
            foreach (var t in children)
                if (t != null && t.name != "Title") DestroyImmediate(t.gameObject);

            // 各グループのアナログチャンネルをリストアップ
            foreach (var grpKv in m_Groups)
            {
                foreach (var chName in grpKv.Value.ChannelNames)
                {
                    var sigCh = Data != null ? FindChannelByName(Data, chName) : null;
                    if (sigCh == null || sigCh.Type != SignalType.Analog) continue;
                    if (m_UnitToggles.ContainsKey(chName)) continue;

                    Color unitColor = sigCh.Color;
                    // 保存済み状態を復元、なければtrue（全表示）
                    bool visible = savedStates.TryGetValue(chName, out bool sv) ? sv : true;

                    var rowGo = new GameObject("ULRow_" + chName);
                    rowGo.transform.SetParent(m_UnitListContent, false);
                    var rowLE2 = rowGo.AddComponent<LayoutElement>();
                    rowLE2.preferredHeight = 24f;
                    rowLE2.minHeight = 24f;
                    rowLE2.flexibleHeight = 0f;
                    rowGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

                    var bgGo = new GameObject("Bg");
                    bgGo.transform.SetParent(rowGo.transform, false);
                    var bgRT = bgGo.AddComponent<RectTransform>();
                    bgRT.anchorMin = new Vector2(0f, 0.15f); bgRT.anchorMax = new Vector2(0f, 0.85f);
                    bgRT.offsetMin = new Vector2(2f, 0f); bgRT.offsetMax = new Vector2(14f, 0f);
                    var bgImg = bgGo.AddComponent<Image>();
                    bgImg.color = visible ? new Color(0.2f, 0.5f, 0.8f) : new Color(0.2f, 0.2f, 0.2f);

                    var toggle = rowGo.AddComponent<Toggle>();
                    toggle.targetGraphic = bgImg;
                    toggle.isOn = visible;

                    var ckGo = new GameObject("Ck");
                    ckGo.transform.SetParent(bgGo.transform, false);
                    var ckRT = ckGo.AddComponent<RectTransform>();
                    ckRT.anchorMin = Vector2.zero; ckRT.anchorMax = Vector2.one;
                    ckRT.offsetMin = new Vector2(2f, 2f); ckRT.offsetMax = new Vector2(-2f, -2f);
                    var ckImg = ckGo.AddComponent<Image>(); ckImg.color = Color.white;
                    toggle.graphic = ckImg;
                    ckGo.SetActive(visible);

                    var cb = ColorBlock.defaultColorBlock;
                    cb.normalColor = Color.white; cb.colorMultiplier = 1f;
                    toggle.colors = cb;

                    var lbl = MakeTMP("Lbl", rowGo.transform, 10, unitColor);
                    lbl.text = chName;
                    lbl.alignment = TextAlignmentOptions.MidlineLeft;
                    lbl.rectTransform.anchorMin = Vector2.zero;
                    lbl.rectTransform.anchorMax = Vector2.one;
                    lbl.rectTransform.offsetMin = new Vector2(16f, 0f);
                    lbl.rectTransform.offsetMax = new Vector2(-2f, 0f);

                    string cap = chName;
                    toggle.onValueChanged.AddListener((on) =>
                    {
                        bgImg.color = on ? new Color(0.2f, 0.5f, 0.8f) : new Color(0.2f, 0.2f, 0.2f);
                        ckGo.SetActive(on);

                        // グループが非表示中はトグル状態だけ保存して行は触らない
                        bool grpVisible = true;
                        foreach (var grpKv in m_Groups)
                            if (grpKv.Value.ChannelNames.Contains(cap) && !grpKv.Value.Visible)
                            { grpVisible = false; break; }

                        if (!on)
                        {
                            // 非表示にする：IO展開状態を保存してアナログ行とIO行を隠す
                            bool ioExpanded = m_CylIOVisible.TryGetValue(cap, out bool ioV) && ioV;
                            m_UnitIOStateBeforeHide[cap] = ioExpanded;

                            SetUnitRowsActive(cap, false, false); // analog=false, io=false
                        }
                        else if (grpVisible)
                        {
                            // 表示にする（グループが表示中の場合のみ）：保存状態を復元
                            bool restoreIO = m_UnitIOStateBeforeHide.TryGetValue(cap, out bool saved) && saved;
                            SetUnitRowsActive(cap, true, restoreIO);
                            if (restoreIO) m_CylIOVisible[cap] = true;
                        }

                        LayoutRebuilder.ForceRebuildLayoutImmediate(m_Content);
                    });

                    m_UnitToggles[chName] = toggle;
                }
            }
            m_RebuildingUnitList = false;
        }


        private static SignalChannel FindChannelByName(TimingChartDataAsset data, string name)
        {
            foreach (var ch in data.Channels)
                if (ch.Name == name) return ch;
            return null;
        }


        public void UpdateModeButton(TimeChartController.ChartMode mode)
        {
            if (m_ModeButtonImg == null || m_ModeButtonLbl == null) return;
            if (mode == TimeChartController.ChartMode.Realtime)
            {
                m_ModeButtonImg.color = new Color(0.2f, 0.35f, 0.55f);
                m_ModeButtonLbl.text = "● リアルタイム";
            }
            else
            {
                m_ModeButtonImg.color = new Color(0.45f, 0.3f, 0.15f);
                m_ModeButtonLbl.text = "■ 履歴";
            }
        }

        private void AddToggleButton(RectTransform parent, string label)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 90f;
            le.preferredHeight = 24f;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.25f, 0.25f, 0.25f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                m_AutoScroll = !m_AutoScroll;
                img.color = m_AutoScroll ? new Color(0.2f, 0.5f, 0.2f) : new Color(0.25f, 0.25f, 0.25f);
            });
            var lbl = MakeTMP("Text", go.transform, 11, Color.white);
            lbl.text = label;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.rectTransform.anchorMin = Vector2.zero;
            lbl.rectTransform.anchorMax = Vector2.one;
            lbl.rectTransform.offsetMin = Vector2.zero;
            lbl.rectTransform.offsetMax = Vector2.zero;
        }

        private void AddButton(RectTransform parent, string label, System.Action onClick)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = label.Length * 8f + 16f;
            le.preferredHeight = 24f;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.28f, 0.28f, 0.28f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.38f, 0.38f, 0.38f);
            colors.pressedColor = new Color(0.18f, 0.18f, 0.18f);
            btn.colors = colors;
            btn.onClick.AddListener(() => onClick());
            var lbl = MakeTMP("Text", go.transform, 11, Color.white);
            lbl.text = label;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.rectTransform.anchorMin = Vector2.zero;
            lbl.rectTransform.anchorMax = Vector2.one;
            lbl.rectTransform.offsetMin = Vector2.zero;
            lbl.rectTransform.offsetMax = Vector2.zero;
        }


        private static readonly Color k_BtnNormal = new Color(0.28f, 0.28f, 0.28f);
        private static readonly Color k_BtnActive = new Color(0.20f, 0.45f, 0.70f);

        private int m_ActiveDataBtn = -1; // 0=設計, 1=レコード, 2=比較

        private void SetDataBtnActive(int idx)
        {
            m_ActiveDataBtn = idx;
            if (m_BtnDesignImg != null) m_BtnDesignImg.color = idx == 0 ? k_BtnActive : k_BtnNormal;
            if (m_BtnRecordImg != null) m_BtnRecordImg.color = idx == 1 ? k_BtnActive : k_BtnNormal;
            if (m_BtnCompareImg != null) m_BtnCompareImg.color = idx == 2 ? k_BtnActive : k_BtnNormal;
            // 比較ボタン以外が選択されたらスピナーを無効化
            if (idx != 2) UpdateSpinnerActive(false);
        }

        private Image AddButtonEx(RectTransform parent, string label, System.Action onClick)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = label.Length * 8f + 16f;
            le.preferredHeight = 24f;
            var img = go.AddComponent<Image>();
            img.color = k_BtnNormal;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            // normalColorをwhiteにしてImage.colorで色を管理（Unityが上書きしないよう）
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            colors.selectedColor = Color.white;
            colors.colorMultiplier = 1f;
            btn.colors = colors;
            btn.onClick.AddListener(() => onClick());
            var lbl = MakeTMP("Text", go.transform, 11, Color.white);
            lbl.text = label;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.rectTransform.anchorMin = Vector2.zero;
            lbl.rectTransform.anchorMax = Vector2.one;
            lbl.rectTransform.offsetMin = Vector2.zero;
            lbl.rectTransform.offsetMax = Vector2.zero;
            return img;
        }

        private Button m_SpinBtnMinus;
        private Button m_SpinBtnPlus;

        private void AddSpinner(RectTransform parent, int initial, int min, int max,
            System.Action<int> onChanged)
        {
            int value = initial;
            TextMeshProUGUI valTxt = null;

            // < ボタン（幅を半分程度に）
            var goBtnM = new GameObject("SpinBtn_<");
            goBtnM.transform.SetParent(parent, false);
            var rtM = goBtnM.AddComponent<RectTransform>(); rtM.localScale = Vector3.one; rtM.sizeDelta = new Vector2(36f, 24f);
            var leM = goBtnM.AddComponent<LayoutElement>(); leM.preferredWidth = 36f; leM.minWidth = 36f; leM.preferredHeight = 24f;
            var imgM = goBtnM.AddComponent<Image>(); imgM.color = new Color(0.28f, 0.28f, 0.28f);
            m_SpinBtnMinus = goBtnM.AddComponent<Button>(); m_SpinBtnMinus.targetGraphic = imgM;
            m_SpinBtnMinus.interactable = false;
            m_SpinBtnMinus.onClick.AddListener(() => { value = Mathf.Max(min, value - 1); if (valTxt != null) valTxt.text = value.ToString(); onChanged(value); });
            var lblM = MakeTMP("Text", goBtnM.transform, 11, Color.white);
            lblM.text = "<"; lblM.alignment = TextAlignmentOptions.Center;
            lblM.rectTransform.anchorMin = Vector2.zero; lblM.rectTransform.anchorMax = Vector2.one;
            lblM.rectTransform.offsetMin = Vector2.zero; lblM.rectTransform.offsetMax = Vector2.zero;

            // 値テキスト
            var valGo = new GameObject("SpinVal");
            valGo.transform.SetParent(parent, false);
            var valRT2 = valGo.AddComponent<RectTransform>(); valRT2.localScale = Vector3.one; valRT2.sizeDelta = new Vector2(36f, 24f);
            var valLE = valGo.AddComponent<LayoutElement>(); valLE.preferredWidth = 36f; valLE.minWidth = 36f; valLE.preferredHeight = 24f;
            valGo.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f);
            valTxt = MakeTMP("Text", valGo.transform, 11, Color.white);
            valTxt.text = value.ToString(); valTxt.alignment = TextAlignmentOptions.Center;
            valTxt.rectTransform.anchorMin = Vector2.zero; valTxt.rectTransform.anchorMax = Vector2.one;
            valTxt.rectTransform.offsetMin = Vector2.zero; valTxt.rectTransform.offsetMax = Vector2.zero;

            // > ボタン
            var goBtnP = new GameObject("SpinBtn_>");
            goBtnP.transform.SetParent(parent, false);
            var rtP = goBtnP.AddComponent<RectTransform>(); rtP.localScale = Vector3.one; rtP.sizeDelta = new Vector2(36f, 24f);
            var leP = goBtnP.AddComponent<LayoutElement>(); leP.preferredWidth = 36f; leP.minWidth = 36f; leP.preferredHeight = 24f;
            var imgP = goBtnP.AddComponent<Image>(); imgP.color = new Color(0.28f, 0.28f, 0.28f);
            m_SpinBtnPlus = goBtnP.AddComponent<Button>(); m_SpinBtnPlus.targetGraphic = imgP;
            m_SpinBtnPlus.interactable = false;
            m_SpinBtnPlus.onClick.AddListener(() => { value = Mathf.Min(max, value + 1); valTxt.text = value.ToString(); onChanged(value); });
            var lblP = MakeTMP("Text", goBtnP.transform, 11, Color.white);
            lblP.text = ">"; lblP.alignment = TextAlignmentOptions.Center;
            lblP.rectTransform.anchorMin = Vector2.zero; lblP.rectTransform.anchorMax = Vector2.one;
            lblP.rectTransform.offsetMin = Vector2.zero; lblP.rectTransform.offsetMax = Vector2.zero;
        }

        /// <summary>比較選択中かつユニット選択時のみスピナーを有効化</summary>
        private void UpdateSpinnerActive(bool active)
        {
            if (m_SpinBtnMinus != null) m_SpinBtnMinus.interactable = active;
            if (m_SpinBtnPlus != null) m_SpinBtnPlus.interactable = active;
        }

        private TMPro.TMP_Dropdown AddDropdown(RectTransform parent, List<string> options, System.Action<int> onChanged)
        {
            var go = new GameObject("Dropdown");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>(); rt.localScale = Vector3.one; rt.sizeDelta = new Vector2(240f, 24f);
            var le = go.AddComponent<LayoutElement>(); le.preferredWidth = 240f; le.minWidth = 240f; le.preferredHeight = 24f;
            var img = go.AddComponent<Image>(); img.color = new Color(0.28f, 0.28f, 0.28f);
            var dd = go.AddComponent<TMPro.TMP_Dropdown>(); dd.targetGraphic = img;

            var tmplGo = new GameObject("Template"); tmplGo.transform.SetParent(go.transform, false);
            var tmplRT = tmplGo.AddComponent<RectTransform>();
            tmplRT.anchorMin = new Vector2(0f, 0f); tmplRT.anchorMax = new Vector2(1f, 0f);
            tmplRT.pivot = new Vector2(0.5f, 1f); tmplRT.sizeDelta = new Vector2(0f, 150f);
            tmplGo.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 1f);
            tmplGo.AddComponent<RectMask2D>();

            var vpGo = new GameObject("Viewport"); vpGo.transform.SetParent(tmplGo.transform, false);
            var vpRT = vpGo.AddComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero; vpRT.anchorMax = Vector2.one; vpRT.offsetMin = Vector2.zero; vpRT.offsetMax = Vector2.zero;
            vpGo.AddComponent<RectMask2D>();

            var cGo = new GameObject("Content"); cGo.transform.SetParent(vpGo.transform, false);
            var cRT = cGo.AddComponent<RectTransform>();
            cRT.anchorMin = new Vector2(0f, 1f); cRT.anchorMax = new Vector2(1f, 1f);
            cRT.pivot = new Vector2(0.5f, 1f); cRT.offsetMin = Vector2.zero; cRT.offsetMax = Vector2.zero;
            var csf = cGo.AddComponent<ContentSizeFitter>(); csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var vlg = cGo.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = false; vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = true; vlg.spacing = 0f;
            var sr2 = tmplGo.AddComponent<ScrollRect>();
            sr2.viewport = vpRT; sr2.content = cRT; sr2.horizontal = false; sr2.vertical = true;
            sr2.movementType = ScrollRect.MovementType.Clamped;
            dd.template = tmplRT;

            var itemGo = new GameObject("Item"); itemGo.transform.SetParent(cGo.transform, false);
            var itemRT = itemGo.AddComponent<RectTransform>();
            itemRT.anchorMin = new Vector2(0f, 1f); itemRT.anchorMax = new Vector2(1f, 1f);
            itemRT.pivot = new Vector2(0.5f, 1f); itemRT.sizeDelta = new Vector2(0f, 22f);
            itemGo.AddComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f);
            var toggle = itemGo.AddComponent<Toggle>(); toggle.targetGraphic = itemGo.GetComponent<Image>();
            var itemLbl = MakeTMP("Item Label", itemGo.transform, 10, Color.white);
            itemLbl.alignment = TextAlignmentOptions.MidlineLeft;
            itemLbl.rectTransform.anchorMin = Vector2.zero; itemLbl.rectTransform.anchorMax = Vector2.one;
            itemLbl.rectTransform.offsetMin = new Vector2(4f, 0f); itemLbl.rectTransform.offsetMax = Vector2.zero;
            dd.itemText = itemLbl;

            var capLbl = MakeTMP("Label", go.transform, 10, Color.white);
            capLbl.alignment = TextAlignmentOptions.MidlineLeft;
            capLbl.rectTransform.anchorMin = Vector2.zero; capLbl.rectTransform.anchorMax = Vector2.one;
            capLbl.rectTransform.offsetMin = new Vector2(4f, 0f); capLbl.rectTransform.offsetMax = new Vector2(-4f, 0f);
            dd.captionText = capLbl;

            dd.ClearOptions(); dd.AddOptions(options);
            dd.onValueChanged.AddListener((idx) => onChanged(idx));
            tmplGo.SetActive(false);
            return dd;
        }

        private static void AddSpacer(RectTransform parent, float width)
        {
            var go = new GameObject("Spacer");
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredWidth = width;
        }

        public void UpdateCompareUnitList(List<string> unitNames)
        {
            if (m_CompareUnitDropdown == null) return;
            int cur = m_CompareUnitDropdown.value;
            string curText = m_CompareUnitDropdown.options.Count > cur
                ? m_CompareUnitDropdown.options[cur].text : "";
            m_CompareUnitDropdown.ClearOptions();
            var opts = new List<string> { "(なし)" };
            opts.AddRange(unitNames);
            m_CompareUnitDropdown.AddOptions(opts);
            int restore = opts.IndexOf(curText);
            m_CompareUnitDropdown.SetValueWithoutNotify(restore >= 0 ? restore : 0);
        }

        public void ClearOverlays()
        {
            m_OverlayChannels.Clear();
            m_DashedBaseChannels.Clear();
        }

        public void AddOverlay(string baseChannelName, SignalChannel overlayCh)
        {
            if (!m_OverlayChannels.ContainsKey(baseChannelName))
                m_OverlayChannels[baseChannelName] = new List<SignalChannel>();
            m_OverlayChannels[baseChannelName].Add(overlayCh);
        }

        private static float CalcTickInterval(float spanMs, float w)
        {
            float[] bases = { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 30000, 60000 };
            float target = 80f * spanMs / w;
            foreach (var b in bases) if (b >= target) return b;
            return bases[bases.Length - 1];
        }

        private static float SampleAt(SignalChannel ch, float ms)
        {
            if (ch.Samples == null || ch.Samples.Count == 0) return 0f;
            for (int i = ch.Samples.Count - 1; i >= 0; i--)
                if (ch.Samples[i].TimeMs <= ms) return ch.Samples[i].Value;
            return ch.Samples[0].Value;
        }

        private static string CategoryShort(DeviceCategory c) => c switch
        {
            DeviceCategory.Cylinder => "[CYL]",
            DeviceCategory.AutoSwitch => "[AS]",
            DeviceCategory.Motor => "[MOT]",
            DeviceCategory.Sensor => "[SEN]",
            _ => "",
        };

        // ---- uGUI ヘルパー ----
        private static RectTransform MakeChild(string name, RectTransform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;
            rt.localPosition = Vector3.zero;
            return rt;
        }

        // フォントを適用した TMP を生成
        private TextMeshProUGUI MakeTMP(string name, Transform parent, int size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;
            rt.localPosition = Vector3.zero;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.color = color;
            t.raycastTarget = false;  // デフォルトでマウス操作をブロックしない
            if (Font != null) t.font = Font;
            return t;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
            => go.GetComponent<T>() ?? go.AddComponent<T>();
    }
}