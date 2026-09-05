using System.Collections.Generic;
using Parameters;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// タッチでユニット選択 → ハイライト ＋ 画面オーバーレイ(uGUI/TMP)の JOG ボタンで手動操作（押下中ON）。
///
///  - 既存の選択イベント(EventManager.ProcessObjectSelect / タッチtap)にフックし、選択ユニットを
///    MaterialPropertyBlock(_Color/_Alpha) でハイライト。
///  - ManualOpInfo.json の手動操作(JOG)定義があれば、画面右に JOG ボタンを uGUI で表示。
///    日本語表示のため NotoSansJP-Medium SDF (TMP) を使用。
///  - 各ボタンに「動作方向」を画面投影した矢印(↑↓←→・回転は↻↺)を表示（カメラ回転に追従）。
///  - 押下中だけ ComHmi.BeginJog/EndJog（安全=デッドマンは ComHmi 側 §8）。
///
/// メモリリーク対策（F5リロード/再選択で増えない）:
///  - Canvas・情報ラベル・フォントは一度だけ生成/取得しキャッシュ。
///  - JOGボタンは「プール」（破棄せず再利用・余りは非表示）。new Material は作らない。
///  - 選択解除/ユニット破棄でジョグOFF＋ボタン非表示＋ハイライト解除。
///
/// WebGL 専用機能（JOGボタン/ユニット選択/視点リセット/フォーカス）。実WebGLビルドのみ生成し、
/// Windows/Android 実機・Editor（Windows上含む）では生成しない。Editorでプレビューしたい時のみ
/// debugInEditor=true。
/// </summary>
public class UnitOperationView : MonoBehaviour
{
    private static UnitOperationView instance;

    /// <summary>ユニット操作オーバーレイ（WebGL／エディタWebGLテスト）が有効か。
    /// MainProcess がこのモードでは非ユニットをクリック選択しないために参照。</summary>
    public static bool IsActive => instance != null;
#if UNITY_EDITOR
    private const bool debugInEditor = false;   // true にすると Editor でもプレビュー表示
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
        {
            return;
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        bool show = true;              // 実WebGLビルドのみ
#elif UNITY_EDITOR
        // Editor は既定で出さない。メニュー「Kyotoss/Editor で WebGL版を起動」ON か debugInEditor で表示。
        bool show = debugInEditor || UnityEditor.EditorPrefs.GetBool("KMX_EditorWebGLMode", false);
#else
        bool show = false;             // Windows/Android 実機では出さない
#endif
        if (!show)
        {
            return;
        }
        // WebGL 専用機能（JOG/ユニット選択/視点リセット/フォーカス）。
        var go = new GameObject("UnitOperationView");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<UnitOperationView>();
    }

    // 既存の選択ハイライトと同じシェーダプロパティ
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly Color HighlightColor = new Color(1f, 1f / 3f, 0f);   // 選択ハイライト(既存)

    // JOGボタン配色（KMXブランド：暗ガラス＋鳥居朱＋グロー縁）
    private static readonly Color BtnIdle = new Color(0.067f, 0.086f, 0.122f, 0.86f);   // 暗ガラス
    private static readonly Color BtnOn = new Color(0.91f, 0.27f, 0.118f, 0.97f);       // 朱（点灯＝PLC認識）
    private static readonly Color BtnPending = new Color(0.30f, 0.13f, 0.09f, 0.92f);   // くすんだ朱（押下済・PLC確認待ち）
    private static readonly Color BtnDisabled = new Color(0.11f, 0.118f, 0.141f, 0.72f);
    private static readonly Color EdgeIdle = new Color(0.91f, 0.27f, 0.118f, 0.55f);    // 朱の縁
    private static readonly Color EdgeOn = new Color(1f, 0.55f, 0.35f, 0.98f);          // 明朱グロー
    private static readonly Color EdgeDisabled = new Color(0.5f, 0.5f, 0.55f, 0.32f);
    private static readonly Color TxtIdle = new Color(0.92f, 0.94f, 0.97f, 1f);
    private static readonly Color TxtDisabled = new Color(0.6f, 0.6f, 0.64f, 1f);
    private static readonly Color IconIdle = new Color(0.95f, 0.42f, 0.22f, 1f);        // 朱アイコン
    private static readonly Color IconDisabled = new Color(0.55f, 0.55f, 0.6f, 1f);

    private const float BtnW = 240f, BtnH = 104f, BtnGap = 14f;

    private class JogButton
    {
        public GameObject go;
        public RectTransform rt;
        public Image bg;
        public Outline outline;
        public TextMeshProUGUI tmp;
        public Image icon;
        public RectTransform iconRt;
    }

    // 実行時生成スプライト（一度だけ作りキャッシュ）。直線=矢印 / 回転=円形矢印 / 角丸ボタン背景 / 奥行き⊙⊗。
    private static Sprite arrowSprite;
    private static Sprite rotateSprite;
    private static Sprite roundedSprite;
    private static Sprite depthOutSprite;   // ⊙ 画面手前向き（カメラに向かう）
    private static Sprite depthInSprite;    // ⊗ 画面奥向き（カメラから遠ざかる）

    // カメラ操作ボタン（常時表示）: 視点リセット(R) / フォーカス(F)
    private class CamButton
    {
        public RectTransform rt;
        public Image bg;
        public Outline outline;
        public TextMeshProUGUI tmp;
        public System.Action action;
        public bool wasDown;
    }
    private readonly List<CamButton> camButtons = new();
    private CamButton deselectBtn;   // 選択解除（選択中のみ・フォーカスの下）
    private int uiTries;
    private const float CamW = 156f, CamH = BtnH, CamGap = 12f;   // 高さはJOGボタン(BtnH)に統一（タッチ用）

    // 親子ユニットの論理ナビ（UnitInfo の children 由来）＋上下ボタン
    private CamButton upBtn, downBtn;
    private bool curHasParent, curHasChildren;            // 選択中ユニットに論理親/子があるか（↑↓表示判定）
    private Dictionary<string, string> childToParent;     // mechId|子ユニット名 → 親ユニット名（論理）
    private readonly List<UfEntry> scratchLevel = new();  // 子数算出の作業用

    // ユニット選択ドリルダウンパネル（group/path ベース）
    private struct UfEntry { public bool isGroup; public bool isSelf; public string name; public ManualOpData unit; }
    private bool unitPanelOpen;
    private readonly List<string> drillPrefix = new();   // 現在ドリルした祖先名（最上位→…）
    private int drillPage;
    private readonly List<UfEntry> ufLevel = new();       // 現在レベルのエントリ
    private readonly List<CamButton> ufEntries = new();   // エントリボタンプール
    private CamButton ufOpenBtn, ufBackBtn, ufCloseBtn, ufPrevBtn, ufNextBtn;
    private Image ufPanelBg;
    private TextMeshProUGUI ufTitle;
    private Dictionary<string, KssBaseScript> unitLookup;
    private const float UfPanelX = 10f, UfPanelY = 124f, UfPanelW = 380f;   // 開くボタンが高くなったぶんパネルを下げる
    private const float UfRowH = BtnH, UfRowGap = 6f;   // ユニット名(エントリ)もタッチ用に JOGボタン高(BtnH)

    // タップ判定（JOG以外のボタンは「離した瞬間」で発火＝トリガ。押下中に別ボタンが出ても誤発火しない）
    private bool ptrDownNow, ptrWasDown, pressConsumed, tapReleasedThisFrame;
    private Vector2 pressStartGui, curGui, lastGui, releaseGui;

    // --- キャッシュ（生成は一度きり） ---
    private Canvas canvas;
    private TextMeshProUGUI infoLabel;
    private Image infoBg;                            // 情報ラベルの背景（3D背景に文字が埋もれないよう）
    private TMP_FontAsset font;
    // MaterialPropertyBlock は ctor/フィールド初期化子で new すると Unity が CreateImpl 例外を投げ、
    // 以降のフィールド初期化子(ops/pool 等)が走らず null になる。→ 初回使用時に生成する。
    private MaterialPropertyBlock mpb;
    private readonly List<JogButton> pool = new();   // ボタンプール（破棄しない）

    // --- 選択状態 ---
    private bool registered;
    private KssBaseScript current;
    private Transform anchor;                       // 矢印の方向計算に使うユニットの基準
    private readonly List<ManualOp> ops = new();
    private bool[] pressed;
    private readonly List<Renderer> highlighted = new();
    private string selInfo = "";

    private void OnEnable()
    {
        TryRegister();
    }

    private void OnDisable()
    {
        EndAllPressed();
    }

    private void Start()
    {
        TryRegister();
    }

    private void TryRegister()
    {
        if (registered)
        {
            return;
        }
        if (EventManager.Instance != null)
        {
            EventManager.Instance.RegisterObjectSelect(OnObjectSelect);
            registered = true;
        }
    }

    private void OnDestroy()
    {
        EndAllPressed();
        if (registered && EventManager.Instance != null)
        {
            EventManager.Instance.UnregisterObjectSelect(OnObjectSelect);
        }
    }

    // ---- 選択 -------------------------------------------------------------

    private void OnObjectSelect(GameObject go)
    {
        ClearSelection();
        EnsureUi();

        var unit = ResolveDeepestUnit(go);
        if (unit == null || unit.unitSetting == null)
        {
            current = null;
            selInfo = go != null ? $"非ユニット: {go.name}" : "";
            curHasParent = false;
            curHasChildren = false;
            ApplyButtons();
            return;
        }
        SelectUnit(unit);
    }

    /// <summary>指定ユニットを選択状態に（ハイライト・操作ボタン・情報・親子有無）。</summary>
    private void SelectUnit(KssBaseScript unit)
    {
        EndAllPressed();
        ops.Clear();
        pressed = null;
        ClearHighlight();
        current = unit;
        if (current == null || current.unitSetting == null)
        {
            anchor = null;
            selInfo = "";
            curHasParent = false;
            curHasChildren = false;
            ApplyButtons();
            return;
        }
        ApplyHighlight(current);
        var u = current.unitSetting;
        anchor = ResolveAnchor(u);
        var mo = GlobalScript.GetManualOp(u.mechId, u.name);
        if (mo != null && mo.ops != null)
        {
            foreach (var op in mo.ops)
            {
                if (op == null)
                {
                    continue;
                }
                // 同一 (axis, dir) は1つだけ（生成器の重複や旧JSONで JOGボタンが二重に出るのを防ぐ）
                if (ops.Exists(o => o.axis == op.axis && o.dir == op.dir))
                {
                    continue;
                }
                ops.Add(op);
                if (!string.IsNullOrEmpty(op.lamp))
                {
                    ComHmi.RegisterLamp(op.lamp);   // PLCのボタン認識返し（ランプ）を購読
                }
                if (!string.IsNullOrEmpty(op.interlock))
                {
                    ComHmi.RegisterInterlock(op.interlock);   // HMX側の操作許可（インターロック）を購読
                }
            }
        }
        pressed = new bool[ops.Count];
        // 所属グループ（論理 children 由来の祖先）をパンくずで上段に（表示は従来どおり）
        var crumbPath = PathFromRoot(current);
        string crumb = crumbPath.Count > 1 ? string.Join(" ＞ ", crumbPath.GetRange(0, crumbPath.Count - 1)) : "";
        selInfo = (string.IsNullOrEmpty(crumb) ? "" : $"<size=58%><color=#8a8f99>{crumb}</color></size>\n")
                + $"<size=135%>{u.name}</size>　<size=62%><color=#7f8a9a>{u.mechId}</color></size>";
        // 親/子の有無（↑↓表示判定。選択時に算出してキャッシュ）
        string pName = LogicalParentName(current);
        curHasParent = !string.IsNullOrEmpty(pName) && FindUnit(u.mechId, pName) != null;
        // 子↓の有無のみ ManualOpData.path 基準（パネルと同じ）で判定（PathFromRoot は group 不一致で子↓が出なかった）
        BuildLevel(UnitPath(current), scratchLevel, false);
        curHasChildren = scratchLevel.Count >= 1;
        ApplyButtons();
    }

    private void ClearSelection()
    {
        EndAllPressed();
        ops.Clear();
        pressed = null;
        anchor = null;
        current = null;
        curHasParent = false;
        curHasChildren = false;
        ClearHighlight();
        ApplyButtons();   // 全ボタン非表示
    }

    /// <summary>clicked go の最も内側（直近）ユニットを取得。</summary>
    private static KssBaseScript ResolveDeepestUnit(GameObject go)
    {
        if (go == null)
        {
            return null;
        }
        var k = go.GetComponentInParent<KssBaseScript>();
        if (k == null)
        {
            k = go.GetComponentInChildren<KssBaseScript>();
        }
        return k;
    }

    /// <summary>論理親ユニット名（UnitInfo children 由来。プレハブの transform.parent は使わない）。</summary>
    private string LogicalParentName(KssBaseScript u)
    {
        if (u == null || u.unitSetting == null)
        {
            return "";
        }
        EnsureLookups();
        childToParent.TryGetValue(u.unitSetting.mechId + "|" + u.unitSetting.name, out var p);
        return p ?? "";
    }

    /// <summary>選択ユニットの ManualOpData.path（ドリルダウンパネルと同じ group 始まりの論理パス）。
    /// パネルの BuildLevel/ComputeLevel は ManualOpData.path 基準なので、↑↓判定も同じ表現に揃える
    /// （PathFromRoot は children 由来で group を含まず不一致＝子ユニット↓が出ない不具合の原因だった）。</summary>
    private List<string> UnitPath(KssBaseScript u)
    {
        if (u != null && u.unitSetting != null)
        {
            var mo = GlobalScript.GetManualOp(u.unitSetting.mechId, u.unitSetting.name);
            if (mo != null && mo.path != null && mo.path.Count > 0)
            {
                return mo.path;
            }
        }
        return PathFromRoot(u);   // ManualOpData が無い場合のフォールバック
    }

    /// <summary>指定パス（group始まり）に厳密一致する JOG可能ユニットを返す（無ければ null）。</summary>
    private ManualOpData FindUnitByPath(List<string> path)
    {
        var ops = GlobalScript.manualOps;
        if (ops == null || path == null)
        {
            return null;
        }
        foreach (var u in ops)
        {
            if (!IsJoggable(u))
            {
                continue;
            }
            var p = PathOf(u);
            if (p.Count == path.Count && StartsWith(p, path, path.Count))
            {
                return u;
            }
        }
        return null;
    }

    /// <summary>最上位→自分 の論理パス（children 由来）。</summary>
    private List<string> PathFromRoot(KssBaseScript u)
    {
        var list = new List<string>();
        if (u == null || u.unitSetting == null)
        {
            return list;
        }
        EnsureLookups();
        string mech = u.unitSetting.mechId;
        string cur = u.unitSetting.name;
        var seen = new HashSet<string>();
        int guard = 0;
        while (!string.IsNullOrEmpty(cur) && guard++ < 64 && seen.Add(cur))
        {
            list.Insert(0, cur);
            childToParent.TryGetValue(mech + "|" + cur, out cur);
        }
        return list;
    }

    /// <summary>親ユニット↑：論理親へ選択を移す。</summary>
    private void SelectParent()
    {
        if (current == null || current.unitSetting == null)
        {
            return;
        }
        string pName = LogicalParentName(current);
        if (!string.IsNullOrEmpty(pName))
        {
            NavigateToUnit(current.unitSetting.mechId, pName);
        }
    }

    /// <summary>子ユニット↓：同一階層の子が1つ（孫以下は数えない）ならその子ユニットをそのまま選択、
    /// 2つ以上ならその階層をユニット選択リスト（パネル）で表示。</summary>
    private void DrillToChildren()
    {
        if (current == null)
        {
            return;
        }
        var path = UnitPath(current);   // group始まりの論理パス（パネルと同基準）
        BuildLevel(path, scratchLevel, false);   // 直下の階層のみ（孫はグループ1件に畳まれカウントされない）
        if (scratchLevel.Count == 1)
        {
            // 同一階層に子が1つだけ → その子ユニットをそのまま選択（孫がいる中間ユニットでも選択）
            var e = scratchLevel[0];
            var u = e.unit ?? FindUnitByPath(new List<string>(path) { e.name });
            if (u != null)
            {
                SelectFromList(u);
                return;
            }
            // ユニットの無い純グループのみ：その階層をリスト表示にフォールバック
        }
        if (scratchLevel.Count >= 1)
        {
            // 2つ以上 → その階層をユニット選択リスト（パネル）で表示
            drillPrefix.Clear();
            drillPrefix.AddRange(path);
            drillPage = 0;
            unitPanelOpen = true;
            RefreshEntries();
        }
    }

    /// <summary>mechId|name のユニットをグローバル選択（ハイライト＋操作ボタン発火）。</summary>
    private void NavigateToUnit(string mechId, string name)
    {
        var k = FindUnit(mechId, name);
        if (k != null && EventManager.Instance != null)
        {
            EventManager.Instance.ProcessObjectSelect(k.gameObject);
        }
    }

    /// <summary>ユニット検索＆子→親マップを構築（mechId|name→KssBaseScript / mechId|子名→親名）。</summary>
    private void EnsureLookups(bool force = false)
    {
        if (!force && unitLookup != null && childToParent != null)
        {
            return;
        }
        unitLookup = new Dictionary<string, KssBaseScript>();
        childToParent = new Dictionary<string, string>();
        foreach (var k in FindObjectsByType<KssBaseScript>(FindObjectsSortMode.None))
        {
            var us = k != null ? k.unitSetting : null;
            if (us == null || string.IsNullOrEmpty(us.name))
            {
                continue;
            }
            unitLookup[us.mechId + "|" + us.name] = k;
            if (us.children != null)
            {
                foreach (var c in us.children)
                {
                    if (c != null && !string.IsNullOrEmpty(c.name))
                    {
                        childToParent[us.mechId + "|" + c.name] = us.name;
                    }
                }
            }
        }
    }

    private static Transform ResolveAnchor(UnitSetting u)
    {
        if (u.moveObject != null) return u.moveObject.transform;
        if (u.unitObject != null) return u.unitObject.transform;
        return null;
    }

    // ---- ハイライト -------------------------------------------------------

    private void ApplyHighlight(KssBaseScript kss)
    {
        if (mpb == null)
        {
            mpb = new MaterialPropertyBlock();   // ctorではなく初回使用時に生成（CreateImpl例外回避）
        }
        var root = HighlightRoot(kss);
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (r is LineRenderer)
            {
                continue;
            }
            r.GetPropertyBlock(mpb);
            mpb.SetColor(ColorId, HighlightColor);
            mpb.SetFloat(AlphaId, 1f);
            r.SetPropertyBlock(mpb);
            highlighted.Add(r);
        }
    }

    private void ClearHighlight()
    {
        foreach (var r in highlighted)
        {
            if (r != null)
            {
                r.SetPropertyBlock(null);
            }
        }
        highlighted.Clear();
    }

    private static GameObject HighlightRoot(KssBaseScript kss)
    {
        var u = kss.unitSetting;
        if (u != null && u.unitObject != null) return u.unitObject;
        if (u != null && u.moveObject != null) return u.moveObject;
        return kss.gameObject;
    }

    // ---- JOG（押下中だけ ON）---------------------------------------------

    private bool loggedUpdateError;

    private void Update()
    {
        if (!registered)
        {
            TryRegister();
        }
        try
        {
            EnsureUi();            // フォント読込後にUI生成（カメラボタンは常時表示）
            UpdatePointerState();  // タップ判定（JOG以外は離した瞬間で発火）
            ProcessCamButtons();   // 視点リセット/フォーカスは選択に関係なく常時処理
            ProcessHierarchyButtons();   // 親ユニット↑/子ユニット↓（選択中のみ表示）
            ProcessUnitPanel();          // ユニット選択ドリルダウン（group/path）
            // F5リロード等でユニットが破棄されたら選択解除（ジョグOFF）
            if (current == null)
            {
                if (ops != null && ops.Count > 0)
                {
                    ClearSelection();
                }
                return;
            }
            if (ops == null || ops.Count == 0 || pressed == null)
            {
                return;
            }
            for (int i = 0; i < ops.Count && i < pressed.Length; i++)
            {
                var op = ops[i];
                if (op == null)
                {
                    continue;
                }
                // 操作不可（writer未認証/allow外/インターロックOFF）のボタンは押下を受け付けない（ONにもしない）
                bool held = ComHmi.CanJogAny(op.dev) && ComHmi.IsInterlockOn(op.interlock) && AnyPointerInRect(ButtonRect(i, ops.Count));
                if (held && !pressed[i])
                {
                    pressed[i] = true;
                    ComHmi.BeginJog(op.dev);
                }
                else if (!held && pressed[i])
                {
                    pressed[i] = false;
                    ComHmi.EndJog(op.dev);
                }
            }
            UpdateButtonVisuals();
        }
        catch (System.Exception e)
        {
            // 実際の発生箇所を特定するため、真のスタックと各フィールドのnull状態を1回だけ出力
            if (!loggedUpdateError)
            {
                loggedUpdateError = true;
                Debug.LogError(
                    $"[UnitOperationView] Update例外: {e.GetType().Name}: {e.Message}\n" +
                    $"current={(current == null)} ops={(ops == null ? -1 : ops.Count)} " +
                    $"pressed={(pressed == null ? -1 : pressed.Length)} canvas={(canvas == null)} " +
                    $"infoLabel={(infoLabel == null)} anchor={(anchor == null)} " +
                    $"font={(font == null)} pool={(pool == null ? -1 : pool.Count)} mainCam={(Camera.main == null)}\n" +
                    $"--- 真のスタック ---\n{e.StackTrace}");
            }
        }
    }

    private void EndAllPressed()
    {
        if (pressed == null)
        {
            return;
        }
        for (int i = 0; i < pressed.Length && i < ops.Count; i++)
        {
            if (pressed[i])
            {
                pressed[i] = false;
                ComHmi.EndJog(ops[i].dev);
            }
        }
    }

    // ---- UI（生成は一度きり・以後再利用） --------------------------------

    private void EnsureUi()
    {
        if (canvas != null)
        {
            return;
        }
        if (font == null)
        {
            font = LoadFont();
        }
        if (font == null && uiTries++ < 180)
        {
            return;   // フォント読込を最大~3秒待つ（日本語が□にならないように）。以降は既定で作成。
        }
        EnsureIcons();
        var cgo = new GameObject("UnitOpCanvas", typeof(Canvas), typeof(CanvasScaler));
        cgo.transform.SetParent(transform, false);
        canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var scaler = cgo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;   // 1px=1screen px（自前rectと一致）

        // 情報ラベル背景（先に作って背面に。文字が3D背景に埋もれないよう半透明の暗パネル）
        var ibg = new GameObject("InfoBg", typeof(RectTransform), typeof(Image));
        ibg.transform.SetParent(canvas.transform, false);
        infoBg = ibg.GetComponent<Image>();
        infoBg.sprite = roundedSprite;
        infoBg.type = Image.Type.Sliced;
        infoBg.color = new Color(0.04f, 0.05f, 0.08f, 0.80f);
        infoBg.raycastTarget = false;
        var ibgrt = infoBg.rectTransform;
        ibgrt.anchorMin = ibgrt.anchorMax = new Vector2(0.5f, 1f);   // 中央上部
        ibgrt.pivot = new Vector2(0.5f, 1f);
        infoBg.gameObject.SetActive(false);

        infoLabel = CreateText(canvas.transform, "InfoLabel", 24, TextAlignmentOptions.Top);   // 中央上部・中央揃え
        var lrt = infoLabel.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 1f);
        lrt.pivot = new Vector2(0.5f, 1f);

        // カメラ操作ボタン（常時表示・左中央）。既存キー機能(R/F)を発火。
        camButtons.Add(MakeCamButton("視点リセット", () => FireKey(UnityEngine.InputSystem.Key.R)));
        camButtons.Add(MakeCamButton("フォーカス", () => FireKey(UnityEngine.InputSystem.Key.F)));
        // 選択解除（選択中のみ表示・フォーカスの下）。タップで現在の選択を解除。
        deselectBtn = MakeCamButton("選択解除", DeselectByUser);
        deselectBtn.rt.gameObject.SetActive(false);

        // 親子ユニットの階層ナビ（親が居る/子が居るときだけ表示・左上）
        upBtn = MakeCamButton("親ユニット ↑", SelectParent);
        downBtn = MakeCamButton("子ユニット ↓", DrillToChildren);
        upBtn.rt.gameObject.SetActive(false);
        downBtn.rt.gameObject.SetActive(false);

        // ユニット選択ドリルダウン（group/path ベース）。開くボタンは常時、パネルは開時のみ。
        ufOpenBtn = MakeCamButton("ユニット選択", ToggleUnitPanel);
        var pbg = new GameObject("UnitPanelBg", typeof(RectTransform), typeof(Image));
        pbg.transform.SetParent(canvas.transform, false);
        ufPanelBg = pbg.GetComponent<Image>();
        ufPanelBg.sprite = roundedSprite;
        ufPanelBg.type = Image.Type.Sliced;
        ufPanelBg.color = new Color(0.04f, 0.05f, 0.08f, 0.97f);
        ufPanelBg.raycastTarget = false;
        var pbgrt = ufPanelBg.rectTransform;
        pbgrt.anchorMin = pbgrt.anchorMax = new Vector2(0f, 1f);
        pbgrt.pivot = new Vector2(0f, 1f);
        ufPanelBg.gameObject.SetActive(false);
        ufTitle = CreateText(canvas.transform, "UnitPanelTitle", 22, TextAlignmentOptions.Left);
        ufTitle.color = TxtIdle;
        var tprt = ufTitle.rectTransform;
        tprt.anchorMin = tprt.anchorMax = new Vector2(0f, 1f);
        tprt.pivot = new Vector2(0f, 1f);
        ufTitle.gameObject.SetActive(false);
        ufBackBtn = MakeCamButton("戻る", () => { if (drillPrefix.Count > 0) { drillPrefix.RemoveAt(drillPrefix.Count - 1); drillPage = 0; RefreshEntries(); } });
        ufCloseBtn = MakeCamButton("閉じる", () => unitPanelOpen = false);
        ufPrevBtn = MakeCamButton("前へ", () => { drillPage--; RefreshEntries(); });
        ufNextBtn = MakeCamButton("次へ", () => { drillPage++; RefreshEntries(); });
        ufBackBtn.rt.gameObject.SetActive(false);
        ufCloseBtn.rt.gameObject.SetActive(false);
        ufPrevBtn.rt.gameObject.SetActive(false);
        ufNextBtn.rt.gameObject.SetActive(false);
    }

    private static void FireKey(UnityEngine.InputSystem.Key key)
    {
        var im = InputManager.Instance;
        if (im != null)
        {
            im.TriggerKey(key);
        }
    }

    private CamButton MakeCamButton(string label, System.Action action)
    {
        var go = new GameObject("CamBtn", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(canvas.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        var bg = go.GetComponent<Image>();
        bg.sprite = roundedSprite;
        bg.type = Image.Type.Sliced;
        bg.color = BtnIdle;
        bg.raycastTarget = false;
        var outline = go.AddComponent<Outline>();
        outline.effectColor = EdgeIdle;
        outline.effectDistance = new Vector2(2.5f, -2.5f);
        var tmp = CreateText(go.transform, "Label", 22, TextAlignmentOptions.Center);
        var trt = tmp.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(6f, 0f);
        trt.offsetMax = new Vector2(-6f, 0f);
        tmp.text = label;
        tmp.color = TxtIdle;
        return new CamButton { rt = rt, bg = bg, outline = outline, tmp = tmp, action = action };
    }

    /// <summary>カメラ操作ボタン（常時）。押下立ち上がりでアクション発火、押下中は朱表示。</summary>
    private void ProcessCamButtons()
    {
        // ユニット選択パネル表示中は、その背後にあるカメラ系ボタン（視点リセット/フォーカス/選択解除）の
        // タップ処理をスキップ＝反応させない（パネルを貫通して押される誤動作を防止）。
        // パネル背景(0.97α)が視覚的に覆うので非表示にはしない（閉じれば即復帰）。
        if (unitPanelOpen)
        {
            return;
        }
        for (int i = 0; i < camButtons.Count; i++)
        {
            TapButton(camButtons[i], CamButtonRect(i, camButtons.Count));   // タップ(離した瞬間)で発火
        }
        // 選択解除はユニット選択中のみ「フォーカスの下」に表示（タップで解除）
        HierButton(deselectBtn, current != null, DeselectRect());
    }

    /// <summary>選択解除ボタンの矩形（カメラボタン最下段＝フォーカスの下）。</summary>
    private Rect DeselectRect()
    {
        int n = Mathf.Max(1, camButtons.Count);
        Rect below = CamButtonRect(n - 1, n);   // 最下段のカメラボタン（フォーカス）
        return new Rect(below.x, below.yMax + CamGap, CamW, CamH);
    }

    /// <summary>選択解除：現在の選択を解除（全リスナ＝JOG/情報/軸 とグローバル選択をクリア）。</summary>
    private void DeselectByUser()
    {
        if (EventManager.Instance != null)
        {
            EventManager.Instance.ProcessObjectSelect(null);   // GlobalScript.selectedObject=null＋全リスナ解除
        }
        else
        {
            ClearSelection();
        }
    }

    private static Rect CamButtonRect(int i, int n)
    {
        float totalH = n * CamH + (n - 1) * CamGap;
        float x = 24f;
        float y0 = (Screen.height - totalH) * 0.5f;
        return new Rect(x, y0 + i * (CamH + CamGap), CamW, CamH);
    }

    // ユニット名（中央上部）の下に、中央寄せで配置（被らないよう）。タッチ用に高さ=JOGボタン(BtnH)。
    private static Rect HierUpRect() { return new Rect(Screen.width * 0.5f - 162f, 92f, 158f, BtnH); }
    private static Rect HierDownRect() { return new Rect(Screen.width * 0.5f + 4f, 92f, 158f, BtnH); }

    // ===== ユニット選択ドリルダウンパネル（group/path ベース） =====
    // すべてのボタン・ユニット名(エントリ)はタッチ用に高さ=JOGボタン(BtnH)。

    private const float UfPad = 10f;       // パネル内余白
    private const float UfTitleH = 30f;    // タイトル(ラベル)高
    private static float UfHeaderTop() { return UfPanelY + UfPad; }                  // 戻る/閉じる 行
    private static float UfTitleTop() { return UfHeaderTop() + BtnH + UfPad; }       // タイトル行
    private static float UfEntriesTop() { return UfTitleTop() + UfTitleH + UfPad; }  // 先頭エントリ
    private static Rect UfOpenRect() { return new Rect(10f, 12f, 180f, BtnH); }   // 画面左上
    private static float UfPanelH() { return Mathf.Max(300f, Screen.height - UfPanelY - 16f); }
    private static Rect UfPanelRect() { return new Rect(UfPanelX, UfPanelY, UfPanelW, UfPanelH()); }
    private static Rect UfBackRect() { return new Rect(UfPanelX + 10f, UfHeaderTop(), 110f, BtnH); }
    private static Rect UfCloseRect() { return new Rect(UfPanelX + UfPanelW - 120f, UfHeaderTop(), 110f, BtnH); }
    private static Rect UfTitleRect() { return new Rect(UfPanelX + 12f, UfTitleTop(), UfPanelW - 24f, UfTitleH); }
    private static Rect UfEntryRect(int i) { return new Rect(UfPanelX + 12f, UfEntriesTop() + i * (UfRowH + UfRowGap), UfPanelW - 24f, UfRowH); }
    private static Rect UfPrevRect() { float y = UfEntriesTop() + UfPerPageNow() * (UfRowH + UfRowGap) + UfPad; return new Rect(UfPanelX + 12f, y, (UfPanelW - 30f) * 0.5f, BtnH); }
    private static Rect UfNextRect() { var p = UfPrevRect(); return new Rect(p.xMax + 6f, p.y, p.width, p.height); }

    /// <summary>パネル高に収まるエントリ数（行高=BtnH なので画面高に応じて動的）。</summary>
    private static int UfPerPageNow()
    {
        float top = UfEntriesTop();
        float bottomReserve = BtnH + UfPad + 8f;   // 前へ/次へ 行＋下余白
        float avail = (UfPanelY + UfPanelH()) - top - bottomReserve;
        int n = Mathf.FloorToInt((avail + UfRowGap) / (UfRowH + UfRowGap));
        return Mathf.Clamp(n, 1, 12);
    }

    private void ToggleUnitPanel()
    {
        unitPanelOpen = !unitPanelOpen;
        if (unitPanelOpen)
        {
            drillPrefix.Clear();
            drillPage = 0;
            RefreshEntries();   // 内部で LayoutPanel（配置→表示）
        }
        else
        {
            LayoutPanel();      // 非表示も即反映
        }
    }

    /// <summary>現在の drillPrefix 配下のエントリを算出。最上位＝グループ、構造は UnitInfo の親子(path)。
    /// JOG可能ユニット（devあり）のみ対象。中間の操作可能ユニットはドリルイン先で「このユニット」として選択可。</summary>
    private void ComputeLevel()
    {
        BuildLevel(drillPrefix, ufLevel, true);
    }

    /// <summary>prefix 配下のエントリを dst へ。最上位＝グループ、構造は UnitInfo の親子(path)。
    /// JOG可能ユニット（devあり）のみ対象。includeSelf=true で prefix 自身が操作ユニットなら「このユニット」を先頭に。</summary>
    private void BuildLevel(List<string> prefix, List<UfEntry> dst, bool includeSelf)
    {
        dst.Clear();
        var ops = GlobalScript.manualOps;
        if (ops == null)
        {
            return;
        }
        int pc = prefix.Count;
        if (includeSelf && pc > 0)
        {
            foreach (var u in ops)
            {
                if (!IsJoggable(u))
                {
                    continue;
                }
                var path = PathOf(u);
                if (path.Count == pc && StartsWith(path, prefix, pc))
                {
                    dst.Add(new UfEntry { isGroup = false, isSelf = true, name = u.name, unit = u });
                    break;
                }
            }
        }

        // 子ノード（次階層）を収集：深い子がいれば group、リーフなら unit
        var order = new List<string>();
        var seen = new HashSet<string>();
        var unitAt = new Dictionary<string, ManualOpData>();
        var hasDeeper = new HashSet<string>();
        foreach (var u in ops)
        {
            if (!IsJoggable(u))
            {
                continue;
            }
            var path = PathOf(u);
            if (path.Count <= pc || !StartsWith(path, prefix, pc))
            {
                continue;
            }
            string seg = path[pc];
            if (seen.Add(seg))
            {
                order.Add(seg);
            }
            if (path.Count == pc + 1)
            {
                unitAt[seg] = u;
            }
            else
            {
                hasDeeper.Add(seg);
            }
        }
        order.Sort(string.CompareOrdinal);
        foreach (var seg in order)
        {
            if (hasDeeper.Contains(seg))
            {
                dst.Add(new UfEntry { isGroup = true, isSelf = false, name = seg, unit = null });
            }
            else if (unitAt.TryGetValue(seg, out var u))
            {
                dst.Add(new UfEntry { isGroup = false, isSelf = false, name = seg, unit = u });
            }
        }
    }

    private static bool StartsWith(List<string> path, List<string> prefix, int n)
    {
        for (int i = 0; i < n; i++)
        {
            if (i >= path.Count || path[i] != prefix[i])
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>JOG可能（dev が割り当たった op を1つ以上持つ）ユニットか。</summary>
    private static bool IsJoggable(ManualOpData u)
    {
        if (u == null || u.ops == null)
        {
            return false;
        }
        foreach (var op in u.ops)
        {
            if (op != null && !string.IsNullOrEmpty(op.dev))
            {
                return true;
            }
        }
        return false;
    }

    private static List<string> PathOf(ManualOpData u)
    {
        return (u.path != null && u.path.Count > 0) ? u.path : new List<string> { u.name };
    }

    /// <summary>現在レベル＋ページのエントリのラベルを設定し、レイアウト（配置→表示）する。</summary>
    private void RefreshEntries()
    {
        ComputeLevel();
        int per = UfPerPageNow();
        int pages = Mathf.Max(1, Mathf.CeilToInt(ufLevel.Count / (float)per));
        drillPage = Mathf.Clamp(drillPage, 0, pages - 1);
        for (int i = 0; i < per; i++)
        {
            if (i >= ufEntries.Count)
            {
                ufEntries.Add(MakeEntryButton());
            }
            int idx = drillPage * per + i;
            if (idx < ufLevel.Count)
            {
                var e = ufLevel[idx];
                ufEntries[i].tmp.text = e.isGroup ? Trunc(e.name) + "　＞"
                            : (e.isSelf ? Trunc(e.name) + "（このユニット）" : Trunc(e.name));
            }
        }
        LayoutPanel();   // 位置設定→表示（開いた瞬間/階層移動でフラッシュしない）
    }

    private void OnEntryTap(UfEntry e)
    {
        if (e.isGroup)
        {
            drillPrefix.Add(e.name);
            drillPage = 0;
            RefreshEntries();
        }
        else
        {
            SelectFromList(e.unit);
        }
    }

    /// <summary>リストから選択：グローバル選択を発火（ハイライト＋JOGボタン）＋カメラフォーカス。</summary>
    private void SelectFromList(ManualOpData u)
    {
        if (u == null)
        {
            return;
        }
        var kss = FindUnit(u.mechId, u.name);
        if (kss != null && EventManager.Instance != null)
        {
            EventManager.Instance.ProcessObjectSelect(kss.gameObject);
            FireKey(UnityEngine.InputSystem.Key.F);   // 選択ユニットへカメラフォーカス
        }
        unitPanelOpen = false;
    }

    /// <summary>mechId|name から KssBaseScript を取得（EnsureLookups のキャッシュ。未ヒットは再構築）。</summary>
    private KssBaseScript FindUnit(string mechId, string name)
    {
        string key = mechId + "|" + name;
        EnsureLookups();
        if (!unitLookup.ContainsKey(key))
        {
            EnsureLookups(true);   // 未ヒットなら再構築（ロード後に増えた分を取り込む）
        }
        unitLookup.TryGetValue(key, out var kss);
        return kss;
    }

    private CamButton MakeEntryButton()
    {
        var cb = MakeCamButton("", null);
        cb.tmp.alignment = TextAlignmentOptions.Left;
        cb.tmp.fontSize = 20f;
        cb.tmp.rectTransform.offsetMin = new Vector2(14f, 0f);
        cb.tmp.rectTransform.offsetMax = new Vector2(-8f, 0f);
        cb.rt.gameObject.SetActive(false);
        return cb;
    }

    private static string Trunc(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "";
        }
        return s.Length > 22 ? s.Substring(0, 21) + "…" : s;
    }

    /// <summary>ボタン配置＋押下色＋タップ判定。タップ＝離した瞬間に発火（押下開始も離した位置も
    /// このrect内・1押下につき1ボタンのみ）。押下中の指の上に別ボタンが出ても誤発火しない（トリガ判定）。発火で true。</summary>
    private bool TapButton(CamButton cb, Rect r)
    {
        cb.rt.anchoredPosition = new Vector2(r.x, -r.y);
        cb.rt.sizeDelta = new Vector2(r.width, r.height);
        bool over = ptrDownNow && r.Contains(curGui);   // 押下中＆この上＝押下表示
        cb.bg.color = over ? BtnOn : BtnIdle;
        if (cb.outline != null)
        {
            cb.outline.effectColor = over ? EdgeOn : EdgeIdle;
        }
        cb.tmp.color = over ? Color.white : TxtIdle;
        if (tapReleasedThisFrame && !pressConsumed && r.Contains(pressStartGui) && r.Contains(releaseGui))
        {
            pressConsumed = true;   // 1押下で発火するのは1ボタンのみ
            cb.action?.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>位置を設定してから表示（フラッシュ防止）＋押下色。</summary>
    private void PlaceButton(CamButton cb, Rect r, bool show)
    {
        if (cb == null || cb.rt == null)
        {
            return;
        }
        if (show)
        {
            cb.rt.anchoredPosition = new Vector2(r.x, -r.y);   // 先に配置
            cb.rt.sizeDelta = new Vector2(r.width, r.height);
            bool over = ptrDownNow && r.Contains(curGui);
            cb.bg.color = over ? BtnOn : BtnIdle;
            if (cb.outline != null)
            {
                cb.outline.effectColor = over ? EdgeOn : EdgeIdle;
            }
            cb.tmp.color = over ? Color.white : TxtIdle;
        }
        var go = cb.rt.gameObject;
        if (go.activeSelf != show)
        {
            go.SetActive(show);   // 配置後に表示
            if (!show) cb.wasDown = false;
        }
    }

    private static void SetRect(RectTransform rt, Rect r)
    {
        rt.anchoredPosition = new Vector2(r.x, -r.y);
        rt.sizeDelta = new Vector2(r.width, r.height);
    }

    private static void SetGoActive(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on)
        {
            go.SetActive(on);
        }
    }

    /// <summary>パネル全要素の配置＋表示切替（位置を設定してから表示＝フラッシュ防止）。タップ判定はしない。</summary>
    private void LayoutPanel()
    {
        if (ufOpenBtn == null)
        {
            return;
        }
        ufOpenBtn.tmp.text = unitPanelOpen ? Lang.T("閉じる") : Lang.T("ユニット選択");
        PlaceButton(ufOpenBtn, UfOpenRect(), true);   // 開くボタンは常時

        bool open = unitPanelOpen;
        if (ufPanelBg != null)
        {
            if (open) SetRect(ufPanelBg.rectTransform, UfPanelRect());
            SetGoActive(ufPanelBg.gameObject, open);
        }
        if (ufTitle != null)
        {
            if (open)
            {
                SetRect(ufTitle.rectTransform, UfTitleRect());
                ufTitle.text = drillPrefix.Count == 0 ? Lang.T("ユニット一覧") : string.Join(" ＞ ", drillPrefix);
            }
            SetGoActive(ufTitle.gameObject, open);
        }
        int per = UfPerPageNow();
        int pages = Mathf.Max(1, Mathf.CeilToInt(ufLevel.Count / (float)per));
        PlaceButton(ufBackBtn, UfBackRect(), open && drillPrefix.Count > 0);
        PlaceButton(ufCloseBtn, UfCloseRect(), open);
        PlaceButton(ufPrevBtn, UfPrevRect(), open && drillPage > 0);
        PlaceButton(ufNextBtn, UfNextRect(), open && drillPage < pages - 1);
        for (int i = 0; i < ufEntries.Count; i++)
        {
            int idx = drillPage * per + i;
            PlaceButton(ufEntries[i], UfEntryRect(i), open && i < per && idx < ufLevel.Count);
        }
    }

    /// <summary>離した瞬間のタップ判定（押下開始も離した位置も rect 内・1押下1ボタン）。発火で true。</summary>
    private bool TapAt(Rect r)
    {
        if (tapReleasedThisFrame && !pressConsumed && r.Contains(pressStartGui) && r.Contains(releaseGui))
        {
            pressConsumed = true;
            return true;
        }
        return false;
    }

    /// <summary>ユニット選択ドリルダウンパネル：配置→表示（LayoutPanel）してからタップ判定。</summary>
    private void ProcessUnitPanel()
    {
        if (ufOpenBtn == null)
        {
            return;
        }
        LayoutPanel();   // 先に配置・表示（フラッシュ防止）

        if (TapAt(UfOpenRect()))
        {
            ToggleUnitPanel();
            return;
        }
        if (!unitPanelOpen)
        {
            return;
        }
        if (drillPrefix.Count > 0 && TapAt(UfBackRect()))
        {
            drillPrefix.RemoveAt(drillPrefix.Count - 1);
            drillPage = 0;
            RefreshEntries();
            return;
        }
        if (TapAt(UfCloseRect()))
        {
            unitPanelOpen = false;
            LayoutPanel();
            return;
        }
        int per = UfPerPageNow();
        int pages = Mathf.Max(1, Mathf.CeilToInt(ufLevel.Count / (float)per));
        if (drillPage > 0 && TapAt(UfPrevRect()))
        {
            drillPage--;
            RefreshEntries();
            return;
        }
        if (drillPage < pages - 1 && TapAt(UfNextRect()))
        {
            drillPage++;
            RefreshEntries();
            return;
        }
        for (int i = 0; i < ufEntries.Count; i++)
        {
            int idx = drillPage * per + i;
            if (idx < ufLevel.Count && ufEntries[i].rt != null && ufEntries[i].rt.gameObject.activeSelf && TapAt(UfEntryRect(i)))
            {
                OnEntryTap(ufLevel[idx]);
                return;
            }
        }
    }

    /// <summary>親子ユニット階層ナビ（左上）。親が居れば↑、子が居れば↓を表示。</summary>
    private void ProcessHierarchyButtons()
    {
        HierButton(upBtn, current != null && curHasParent, HierUpRect());     // 論理親があれば↑
        HierButton(downBtn, current != null && curHasChildren, HierDownRect()); // 論理子があれば↓
    }

    private void HierButton(CamButton cb, bool show, Rect r)
    {
        if (cb == null || cb.rt == null)
        {
            return;
        }
        var go = cb.rt.gameObject;
        if (go.activeSelf != show)
        {
            go.SetActive(show);
        }
        if (show)
        {
            TapButton(cb, r);   // タップ(離した瞬間)で発火
        }
    }

    private static TMP_FontAsset cachedFontSearch;

    private static TMP_FontAsset LoadFont()
    {
        if (cachedFontSearch != null)
        {
            return cachedFontSearch;
        }
        // 1) 既にロード済みのアセットから名前で取得（既存UIが使用＝アセット移動不要）
        foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (f != null && f.name == "NotoSansJP-Medium SDF")
            {
                cachedFontSearch = f;
                return f;
            }
        }
        // 2) Resources/Fonts にあれば。見つからなければ null を返し、次回再試行（起動直後のロード待ち対策）
        cachedFontSearch = Resources.Load<TMP_FontAsset>("Fonts/NotoSansJP-Medium SDF");
        return cachedFontSearch;
    }

    private TextMeshProUGUI CreateText(Transform parent, string name, float size, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (font != null)
        {
            tmp.font = font;
        }
        tmp.fontSize = size;
        tmp.alignment = align;
        tmp.color = Color.white;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;   // 自前rect判定。EventSystem干渉なし
        tmp.richText = true;
        return tmp;
    }

    private JogButton CreateButton()
    {
        var go = new GameObject("JogBtn", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(canvas.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        var bg = go.GetComponent<Image>();
        bg.sprite = roundedSprite;            // 角丸背景（9スライス）
        bg.type = Image.Type.Sliced;
        bg.color = BtnIdle;
        bg.raycastTarget = false;             // 押下判定は自前rect。uGUIレイキャスト不要
        var outline = go.AddComponent<Outline>();   // 朱のグロー縁
        outline.effectColor = EdgeIdle;
        outline.effectDistance = new Vector2(2.5f, -2.5f);

        // ラベル（上部）
        var tmp = CreateText(go.transform, "Label", 26, TextAlignmentOptions.Center);
        var trt = tmp.rectTransform;
        trt.anchorMin = new Vector2(0f, 0.52f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.offsetMin = new Vector2(6f, 0f);
        trt.offsetMax = new Vector2(-6f, -4f);
        tmp.margin = new Vector4(4f, 2f, 4f, 2f);

        // 方向アイコン（下部中央）
        var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(go.transform, false);
        var iconRt = iconGo.GetComponent<RectTransform>();
        iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 0.27f);
        iconRt.pivot = new Vector2(0.5f, 0.5f);
        iconRt.sizeDelta = new Vector2(46f, 46f);
        var icon = iconGo.GetComponent<Image>();
        icon.raycastTarget = false;
        icon.preserveAspect = true;

        return new JogButton { go = go, rt = rt, bg = bg, outline = outline, tmp = tmp, icon = icon, iconRt = iconRt };
    }

    /// <summary>選択に合わせてボタンの表示/非表示を切替（プール再利用・破棄しない）</summary>
    private void ApplyButtons()
    {
        if (canvas == null)
        {
            return;
        }
        infoLabel.rectTransform.anchoredPosition = new Vector2(0f, -10f);   // 中央上部
        infoLabel.rectTransform.sizeDelta = new Vector2(Mathf.Min(1000f, Screen.width - 40f), 72f);
        infoLabel.text = selInfo;

        for (int i = 0; i < ops.Count; i++)
        {
            if (i >= pool.Count)
            {
                pool.Add(CreateButton());
            }
            pool[i].go.SetActive(true);
        }
        for (int i = ops.Count; i < pool.Count; i++)
        {
            pool[i].go.SetActive(false);
        }
        UpdateButtonVisuals();
    }

    private void UpdateButtonVisuals()
    {
        bool anyDisabled = false;
        string disableReason = "";   // 最初の不可ボタンの具体理由（インターロックOFF / allow外 / 未認証 / 切断）
        for (int i = 0; i < ops.Count && i < pool.Count; i++)
        {
            var op = ops[i];
            var b = pool[i];
            Rect r = ButtonRect(i, ops.Count);
            b.rt.anchoredPosition = new Vector2(r.x, -r.y);
            b.rt.sizeDelta = new Vector2(r.width, r.height);

            // 操作可否＝認証/allow/接続/レコーダ（CanJogAny）＋ インターロック成立（IsInterlockOn）。
            // インターロックOFF/不明は安全側で操作不可＝ボタン灰色（§5）。
            bool ilOk = ComHmi.IsInterlockOn(op.interlock);
            bool canJog = ComHmi.CanJogAny(op.dev) && ilOk;
            if (!canJog)
            {
                anyDisabled = true;
                if (disableReason == "")
                {
                    // インターロックOFF を優先、それ以外は具体理由（allow外/未認証/切断/レコーダ）
                    disableReason = !ilOk ? "インターロックOFF" : ComHmi.JogBlockReason(op.dev);
                    if (string.IsNullOrEmpty(disableReason)) disableReason = "操作不可";
                }
            }
            bool pressedNow = pressed != null && i < pressed.Length && pressed[i];
            bool hasLamp = !string.IsNullOrEmpty(op.lamp);
            // 点灯はPLCのランプ読み戻しで決定（PLCがボタン認識→ON）。ランプ未定義は従来の押下即点灯。
            bool lit = hasLamp ? ComHmi.IsLampOn(op.lamp) : pressedNow;
            bool pending = hasLamp && pressedNow && !lit;   // 押下済みだがPLC未確認（ランプ待ち）

            // 状態別の配色。操作不可（未認証/allow外/インターロックOFF）は最優先で灰色＝
            // ランプ点灯より優先（押せない状態を朱で見せない）。操作可のとき 暗ガラス→PLC確認待ち→点灯。
            Color iconCol;
            if (!canJog)
            {
                b.bg.color = BtnDisabled;
                if (b.outline != null) b.outline.effectColor = EdgeDisabled;
                b.tmp.color = TxtDisabled;
                iconCol = IconDisabled;
                b.rt.localScale = Vector3.one;
            }
            else if (lit)
            {
                b.bg.color = BtnOn;
                if (b.outline != null) b.outline.effectColor = EdgeOn;
                b.tmp.color = Color.white;
                iconCol = Color.white;
            }
            else if (pending)
            {
                b.bg.color = BtnPending;
                if (b.outline != null) b.outline.effectColor = EdgeOn;
                b.tmp.color = TxtIdle;
                iconCol = IconIdle;
                b.rt.localScale = Vector3.one;
            }
            else
            {
                // 操作可・非点灯＝待機（暗ガラス）
                b.bg.color = BtnIdle;
                if (b.outline != null) b.outline.effectColor = EdgeIdle;
                b.tmp.color = TxtIdle;
                iconCol = IconIdle;
                b.rt.localScale = Vector3.one;
            }

            // ラベル（ワールド上下方向の直線動作は 上昇/下降 に置換）、方向はアイコンで表示
            b.tmp.text = LabelFor(op);
            UpdateIcon(b, op);
            if (b.icon != null) b.icon.color = iconCol;
        }
        if (infoLabel != null)
        {
            infoLabel.text = (anyDisabled && ops.Count > 0)
                ? selInfo + $"　<size=66%><color=#c8805e>JOG操作不可（{disableReason}）</color></size>"
                : selInfo;
        }
        FitInfoBg();
    }

    /// <summary>情報ラベルの背景を文字幅・高さに合わせる（空なら非表示）。</summary>
    private void FitInfoBg()
    {
        if (infoBg == null || infoLabel == null)
        {
            return;
        }
        bool show = !string.IsNullOrEmpty(infoLabel.text);
        if (infoBg.gameObject.activeSelf != show)
        {
            infoBg.gameObject.SetActive(show);
        }
        if (!show)
        {
            return;
        }
        infoLabel.ForceMeshUpdate();
        float w = infoLabel.preferredWidth + 28f;    // 左右パディング
        float h = infoLabel.preferredHeight + 14f;   // 上下パディング
        var lp = infoLabel.rectTransform.anchoredPosition;
        infoBg.rectTransform.anchoredPosition = new Vector2(lp.x, lp.y + 6f);   // 中央上部に合わせる
        infoBg.rectTransform.sizeDelta = new Vector2(w, h);
    }

    /// <summary>方向アイコンを設定（直線=矢印を画面方向へ回転 / 回転=円形矢印を正逆で反転）</summary>
    private void UpdateIcon(JogButton b, ManualOp op)
    {
        if (b.icon == null)
        {
            return;
        }
        bool rot = !string.IsNullOrEmpty(op.label) && op.label.Contains("転");
        if (rot)
        {
            b.icon.sprite = rotateSprite;
            b.iconRt.localEulerAngles = Vector3.zero;
            b.iconRt.localScale = new Vector3(op.dir >= 0 ? 1f : -1f, 1f, 1f);   // 逆転=左右反転
        }
        else
        {
            if (!IsDirectionExpressible(op, out bool towardCam))
            {
                // 動作軸が奥行き（視線）方向に近く矢印で向きを表せない →
                // 「画面に対し奥/手前」だと分かるよう ⊙(手前/出る) ⊗(奥/入る) を表示。
                // カメラを回して表現できる角度になると通常の矢印に戻る。
                b.icon.sprite = towardCam ? depthOutSprite : depthInSprite;
                b.iconRt.localEulerAngles = Vector3.zero;
                b.iconRt.localScale = Vector3.one;
            }
            else
            {
                b.icon.sprite = arrowSprite;
                b.iconRt.localScale = Vector3.one;
                b.iconRt.localEulerAngles = new Vector3(0f, 0f, ScreenAngleDeg(op));
            }
        }
        b.icon.enabled = b.icon.sprite != null;
    }

    // 動作軸が視線（奥行き）方向からこの角度以内なら矢印では表現できない（⊙⊗で明示）
    private const float ExpressibleMinAngleDeg = 30f;

    /// <summary>直線動作の向きが画面上で矢印として表現可能か。不可時は towardCamera で手前/奥を返す。</summary>
    private bool IsDirectionExpressible(ManualOp op, out bool towardCamera)
    {
        towardCamera = false;
        var cam = CommonFunction.MainCamera;   // Camera.main キャッシュ（毎フレーム×ボタン数で呼ぶため）
        if (cam == null || anchor == null)
        {
            return true;   // 判定不能時は従来通り矢印表示
        }
        Vector3 wd = anchor.TransformDirection(AxisVec(op.axis)) * (op.dir >= 0 ? 1f : -1f);
        if (wd.sqrMagnitude < 1e-6f)
        {
            return true;
        }
        wd.Normalize();
        Vector3 viewRay = anchor.position - cam.transform.position;
        if (viewRay.sqrMagnitude < 1e-6f)
        {
            return true;
        }
        viewRay.Normalize();
        float signed = Vector3.Dot(wd, viewRay);   // >0:視線と同方向＝画面奥へ / <0:手前へ
        towardCamera = signed < 0f;                // 手前(カメラに向かう)＝画面から出る ⊙
        // |signed|=1: 視線と平行＝矢印で表現不可 / 0: 画面内＝良好
        return Mathf.Abs(signed) <= Mathf.Cos(ExpressibleMinAngleDeg * Mathf.Deg2Rad);   // cos30°≈0.866
    }

    /// <summary>ボタンのラベル。ワールド上下方向の直線動作は「上昇/下降」に置換（回転はそのまま）。</summary>
    private string LabelFor(ManualOp op)
    {
        bool rot = !string.IsNullOrEmpty(op.label) && op.label.Contains("転");
        if (!rot && anchor != null)
        {
            Vector3 wd = anchor.TransformDirection(AxisVec(op.axis)).normalized * (op.dir >= 0 ? 1f : -1f);
            if (Mathf.Abs(wd.y) > Mathf.Abs(wd.x) && Mathf.Abs(wd.y) > Mathf.Abs(wd.z))
            {
                return wd.y >= 0f ? "上昇" : "下降";
            }
        }
        return op.label;
    }

    /// <summary>直線動作の画面投影方向（度）。+x=0°、反時計回り正。アイコン回転に使用。</summary>
    private float ScreenAngleDeg(ManualOp op)
    {
        var cam = CommonFunction.MainCamera;   // Camera.main キャッシュ（毎フレーム×ボタン数で呼ぶため）
        if (cam == null || anchor == null)
        {
            return op.dir >= 0 ? 0f : 180f;
        }
        Vector3 c = anchor.position;
        Vector3 wd = anchor.TransformDirection(AxisVec(op.axis)).normalized * (op.dir >= 0 ? 1f : -1f);
        Vector3 s0 = cam.WorldToScreenPoint(c);
        Vector3 s1 = cam.WorldToScreenPoint(c + wd);
        Vector2 d = new Vector2(s1.x - s0.x, s1.y - s0.y);
        if (d.sqrMagnitude < 0.5f)
        {
            return op.dir >= 0 ? 0f : 180f;   // ほぼ奥行き方向
        }
        return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
    }

    // ---- アイコン生成（実行時・一度きり） --------------------------------

    private static void EnsureIcons()
    {
        if (arrowSprite == null)
        {
            arrowSprite = ToSprite(MakeArrowTex(64));
        }
        if (rotateSprite == null)
        {
            rotateSprite = ToSprite(MakeRotateTex(64));
        }
        if (roundedSprite == null)
        {
            roundedSprite = MakeRoundedSprite(48, 16f);
        }
        if (depthOutSprite == null)
        {
            depthOutSprite = ToSprite(MakeDepthTex(64, true));
        }
        if (depthInSprite == null)
        {
            depthInSprite = ToSprite(MakeDepthTex(64, false));
        }
    }

    /// <summary>奥行き方向インジケータ。toward=true:⊙(手前/画面から出る) / false:⊗(奥/画面へ入る)。</summary>
    private static Texture2D MakeDepthTex(int n, bool toward)
    {
        var px = NewCanvas(n);
        float c = n * 0.5f;
        float r = n * 0.32f;        // 円の半径
        float th = n * 0.10f;       // 円の線幅
        float inner = r - th * 0.5f;
        const float sqrt2 = 1.41421356f;
        for (int x = 0; x < n; x++)
        {
            for (int y = 0; y < n; y++)
            {
                float dx = x + 0.5f - c;
                float dy = y + 0.5f - c;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                // 外周リング
                if (Mathf.Abs(dist - r) <= th * 0.5f)
                {
                    SetPx(px, n, x, y);
                    continue;
                }
                if (toward)
                {
                    // ⊙: 中央の塗りドット
                    if (dist <= n * 0.12f)
                    {
                        SetPx(px, n, x, y);
                    }
                }
                else
                {
                    // ⊗: 円内に太い×
                    if (dist <= inner - n * 0.02f)
                    {
                        float dA = Mathf.Abs(dx - dy) / sqrt2;
                        float dB = Mathf.Abs(dx + dy) / sqrt2;
                        if (dA <= n * 0.06f || dB <= n * 0.06f)
                        {
                            SetPx(px, n, x, y);
                        }
                    }
                }
            }
        }
        return Bake(px, n);
    }

    /// <summary>角丸矩形スプライト（9スライス・縁AA付き）。ボタン背景用。</summary>
    private static Sprite MakeRoundedSprite(int n, float radius)
    {
        var px = NewCanvas(n);
        float half = n * 0.5f;
        for (int x = 0; x < n; x++)
        {
            for (int y = 0; y < n; y++)
            {
                float pxx = x + 0.5f - half;
                float pyy = y + 0.5f - half;
                float qx = Mathf.Abs(pxx) - (half - radius);
                float qy = Mathf.Abs(pyy) - (half - radius);
                float mx = Mathf.Max(qx, 0f), my = Mathf.Max(qy, 0f);
                float outside = Mathf.Sqrt(mx * mx + my * my) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                float a = Mathf.Clamp01(0.5f - outside);   // 縁アンチエイリアス
                if (a > 0f)
                {
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
        }
        var t = Bake(px, n);
        return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
    }

    private static Sprite ToSprite(Texture2D t)
    {
        return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Color32[] NewCanvas(int n)
    {
        var px = new Color32[n * n];
        var clear = new Color32(255, 255, 255, 0);
        for (int i = 0; i < px.Length; i++)
        {
            px[i] = clear;
        }
        return px;
    }

    private static Texture2D Bake(Color32[] px, int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        t.SetPixels32(px);
        t.Apply();
        return t;
    }

    private static void SetPx(Color32[] px, int n, int x, int y)
    {
        if (x >= 0 && x < n && y >= 0 && y < n)
        {
            px[y * n + x] = new Color32(255, 255, 255, 255);
        }
    }

    /// <summary>右向き矢印（軸方向は RectTransform 回転で合わせる）</summary>
    private static Texture2D MakeArrowTex(int n)
    {
        var px = NewCanvas(n);
        float cy = n * 0.5f;
        float shaftX0 = n * 0.12f, shaftX1 = n * 0.58f, shaftH = n * 0.13f;
        float headX0 = n * 0.50f, headX1 = n * 0.90f, headH = n * 0.30f;
        for (int x = 0; x < n; x++)
        {
            for (int y = 0; y < n; y++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                bool shaft = fx >= shaftX0 && fx <= shaftX1 && Mathf.Abs(fy - cy) <= shaftH;
                bool head = false;
                if (fx >= headX0 && fx <= headX1)
                {
                    float hh = headH * (headX1 - fx) / (headX1 - headX0);
                    head = Mathf.Abs(fy - cy) <= hh;
                }
                if (shaft || head)
                {
                    SetPx(px, n, x, y);
                }
            }
        }
        return Bake(px, n);
    }

    /// <summary>円形矢印（時計回り）。逆転は RectTransform を左右反転して使う。</summary>
    private static Texture2D MakeRotateTex(int n)
    {
        var px = NewCanvas(n);
        float c = n * 0.5f, r = n * 0.30f, th = n * 0.11f;
        for (int x = 0; x < n; x++)
        {
            for (int y = 0; y < n; y++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (Mathf.Abs(dist - r) <= th * 0.5f)
                {
                    float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;   // 90=上
                    if (ang < 58f || ang > 122f)                       // 上部に隙間（矢印を置く）
                    {
                        SetPx(px, n, x, y);
                    }
                }
            }
        }
        // 上部の矢印（右向き＝時計回り）
        float topY = c + r;
        FillTri(px, n,
            new Vector2(c + n * 0.15f, topY),
            new Vector2(c - n * 0.03f, topY + n * 0.12f),
            new Vector2(c - n * 0.03f, topY - n * 0.12f));
        return Bake(px, n);
    }

    private static void FillTri(Color32[] px, int n, Vector2 a, Vector2 b, Vector2 cc)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, cc.x))));
        int maxX = Mathf.Min(n - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, cc.x))));
        int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, cc.y))));
        int maxY = Mathf.Min(n - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, cc.y))));
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                if (InTri(new Vector2(x + 0.5f, y + 0.5f), a, b, cc))
                {
                    SetPx(px, n, x, y);
                }
            }
        }
    }

    private static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
        bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
        bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(neg && pos);
    }

    private static float Cross(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }

    private static Rect ButtonRect(int i, int n)
    {
        float totalH = n * BtnH + (n - 1) * BtnGap;
        float x = Screen.width - BtnW - 24f;
        float y0 = (Screen.height - totalH) * 0.5f;
        return new Rect(x, y0 + i * (BtnH + BtnGap), BtnW, BtnH);
    }

    /// <summary>毎フレームのポインタ状態更新（タップ＝離した瞬間の判定用）。Update先頭で1回呼ぶ。</summary>
    private void UpdatePointerState()
    {
        bool down = TryPointer(out Vector2 gui);
        ptrDownNow = down;
        if (down)
        {
            if (!ptrWasDown)
            {
                pressStartGui = gui;     // 新規押下の開始位置
                pressConsumed = false;
            }
            curGui = gui;
            lastGui = gui;
        }
        tapReleasedThisFrame = !down && ptrWasDown;   // この瞬間に離した
        releaseGui = lastGui;                         // 離した位置＝最後に握っていた位置
        ptrWasDown = down;
    }

    /// <summary>押下中ポインタ(タッチ/マウス)のGUI座標(上原点)を返す。押下中なら true。</summary>
    private static bool TryPointer(out Vector2 gui)
    {
        gui = default;
        // EnhancedTouch が未有効だと activeTouches が例外。ロード順に依存しないよう使用直前に保証。
        if (!UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.enabled)
        {
            UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Enable();
        }
        var touches = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches;
        foreach (var t in touches)
        {
            var ph = t.phase;
            if (ph == UnityEngine.InputSystem.TouchPhase.Began
                || ph == UnityEngine.InputSystem.TouchPhase.Moved
                || ph == UnityEngine.InputSystem.TouchPhase.Stationary)
            {
                var p = t.screenPosition;
                gui = new Vector2(p.x, Screen.height - p.y);
                return true;
            }
        }
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null && mouse.leftButton.isPressed)
        {
            var p = mouse.position.ReadValue();
            gui = new Vector2(p.x, Screen.height - p.y);
            return true;
        }
        return false;
    }

    /// <summary>押下中ポインタ(タッチ/マウス)が GUI rect(上原点) 内にあるか（JOGのhold判定用）</summary>
    private static bool AnyPointerInRect(Rect guiRect)
    {
        if (!UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.enabled)
        {
            UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Enable();
        }
        var touches = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches;
        foreach (var t in touches)
        {
            var ph = t.phase;
            if (ph == UnityEngine.InputSystem.TouchPhase.Began
                || ph == UnityEngine.InputSystem.TouchPhase.Moved
                || ph == UnityEngine.InputSystem.TouchPhase.Stationary)
            {
                var p = t.screenPosition;
                if (guiRect.Contains(new Vector2(p.x, Screen.height - p.y)))
                {
                    return true;
                }
            }
        }
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null && mouse.leftButton.isPressed)
        {
            var p = mouse.position.ReadValue();
            if (guiRect.Contains(new Vector2(p.x, Screen.height - p.y)))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>screenPos(下原点) がオーバーレイボタン（JOG＋カメラ操作）上にあるか（InputManager がカメラ操作抑止に使用）</summary>
    public static bool IsPointerOverButton(Vector2 screenPos)
    {
        var inst = instance;
        if (inst == null)
        {
            return false;
        }
        var gui = new Vector2(screenPos.x, Screen.height - screenPos.y);
        for (int i = 0; i < inst.camButtons.Count; i++)
        {
            if (CamButtonRect(i, inst.camButtons.Count).Contains(gui))
            {
                return true;
            }
        }
        if (inst.ufOpenBtn != null && UfOpenRect().Contains(gui))
        {
            return true;   // 「ユニット選択」開くボタン
        }
        if (inst.unitPanelOpen && UfPanelRect().Contains(gui))
        {
            return true;   // ドリルダウンパネル全体
        }
        if (inst.upBtn != null && inst.upBtn.rt != null && inst.upBtn.rt.gameObject.activeSelf && HierUpRect().Contains(gui))
        {
            return true;
        }
        if (inst.downBtn != null && inst.downBtn.rt != null && inst.downBtn.rt.gameObject.activeSelf && HierDownRect().Contains(gui))
        {
            return true;
        }
        for (int i = 0; i < inst.ops.Count; i++)
        {
            if (ButtonRect(i, inst.ops.Count).Contains(gui))
            {
                return true;
            }
        }
        return false;
    }

    private static Vector3 AxisVec(int axis)
    {
        return axis == 0 ? Vector3.right : (axis == 1 ? Vector3.up : Vector3.forward);
    }
}
