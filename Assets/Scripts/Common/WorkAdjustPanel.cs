using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// ワーク生成位置／変換オフセットを KMX 上で調整するパネル（F9 で表示切替）。
/// 「ツールで入力→出力→F5→確認」の往復を無くすため、実行中に数値を変えて即座に見た目へ反映し、
/// 決まった値をクリップボード経由で KMXTool へ戻す運用を想定している。
/// 自己生成（シーン編集不要）。対象は MultiObjectFactoryScript がロード時に登録する。
/// </summary>
public class WorkAdjustPanel : MonoBehaviour
{
    /// <summary>調整対象1件。設定への読み書きは呼び出し側のデリゲートに委ねる（設定クラスが非公開のため）</summary>
    public class Target
    {
        /// <summary>一覧に出す表示名</summary>
        public string label;
        /// <summary>位置(m)の取得／設定</summary>
        public Func<Vector3> getPos;
        public Action<Vector3> setPos;
        /// <summary>角度(度)の取得／設定</summary>
        public Func<Vector3> getRot;
        public Action<Vector3> setRot;
        /// <summary>値変更後にゴーストを再配置する</summary>
        public Action apply;
        /// <summary>
        /// 画面クリックで対象を切り替えるための当たり判定用オブジェクト（ゴースト／削除範囲の球）。
        /// これらはコライダを持たないため、パネル側でレンダラ境界にレイを当てて判定する
        /// </summary>
        public GameObject hitObject;
        /// <summary>原点軸マーカー（hitObject配下の"OriginAxes"）。選択中の目印として表示を切り替える</summary>
        public GameObject axes;
        /// <summary>軸マーカーの探索済みフラグ（毎フレームFindしないため）</summary>
        public bool axesResolved;
        /// <summary>この対象を駆動するタグ名（表示用。設定値の文字列をそのまま持つ）</summary>
        public string tagName;
        /// <summary>タグの現在値を返す（表示用。nullならタグ行を出さない）</summary>
        public Func<int> getTagValue;
    }

    private static readonly List<Target> targets = new();
    private static bool isOpen;

    /// <summary>位置の刻み(mm)。細かい値はテキスト欄へ直接入力する</summary>
    private const float STEP_MM = 10f;
    /// <summary>角度の刻み(度)</summary>
    private const float STEP_DEG = 1f;

    /// <summary>選択中の対象。Fキーのフォーカスから参照するためstatic</summary>
    private static int index;
    private Rect window = new Rect(20f, 60f, 430f, 0f);
    /// <summary>入力欄の文字列（X,Y,Z,RX,RY,RZ）。数値と別に持たないと入力途中の「-」や「1.」が消える</summary>
    private readonly string[] edits = new string[6];
    private int editedIndex = -1;
    private GUIStyle titleStyle;
    private GUIStyle dragLabelStyle;

    /// <summary>ラベルドラッグ中の軸（-1=なし）。Unity Inspectorと同じスクラブ操作用</summary>
    private int dragIndex = -1;
    /// <summary>ドラッグ開始時のマウスX座標と値</summary>
    private float dragStartX;
    private float dragStartValue;
    /// <summary>ドラッグ感度（1ピクセルあたり mm または 度）</summary>
    private const float DRAG_PER_PIXEL_MM = 1f;
    private const float DRAG_PER_PIXEL_DEG = 0.5f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("WorkAdjustPanel");
        DontDestroyOnLoad(go);
        go.AddComponent<WorkAdjustPanel>();
    }

    /// <summary>調整対象を登録する（ロード時に呼ぶ）</summary>
    public static void Register(Target target)
    {
        if ((target == null) || (target.getPos == null) || (target.setPos == null))
        {
            return;
        }
        targets.Add(target);
    }

    /// <summary>再ロード時に登録を消す（機番構成が変わっても古い対象が残らないように）</summary>
    public static void Clear()
    {
        targets.Clear();
        index = 0;
    }

    /// <summary>
    /// 調整パネルで選択中の対象の位置を返す（Fキーのフォーカス用）。
    /// 通常オブジェクトが未選択のときに、調整中の対象へ寄れるようにする。
    /// パネルを閉じているときや対象が無いときは false。
    /// </summary>
    /// <param name="pos">選択中の対象の位置（ワールド）</param>
    /// <returns>取得できたか</returns>
    public static bool TryGetSelectedPosition(out Vector3 pos)
    {
        pos = Vector3.zero;
        if (!isOpen || (targets.Count == 0))
        {
            return false;
        }
        var obj = targets[Mathf.Clamp(index, 0, targets.Count - 1)].hitObject;
        if (obj == null)
        {
            return false;
        }
        pos = obj.transform.position;
        return true;
    }

    private void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if ((kb != null) && kb.f9Key.wasPressedThisFrame)
        {
            isOpen = !isOpen;
            // 開いた時点の値を入力欄へ取り込む
            editedIndex = -1;
        }
        // パネル操作中はキーを押しっぱなしにできないため、確認表示（Ctrl+Shift相当）を維持する
        BacketPathOverlay.ForceShow = isOpen;
        if (isOpen)
        {
            PickTargetByClick();
        }
        UpdateAxesVisibility();
    }

    /// <summary>
    /// 原点軸マーカーの表示を切り替える。
    /// パネルを開いている間は「選択中の対象だけ」に出して選択状態が分かるようにし、
    /// 閉じているとき（Ctrl+Shiftの確認表示）は全対象に出して原点位置を見比べられるようにする。
    /// </summary>
    private void UpdateAxesVisibility()
    {
        for (var i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (!t.axesResolved)
            {
                t.axesResolved = true;
                if (t.hitObject != null)
                {
                    var found = t.hitObject.transform.Find("OriginAxes");
                    t.axes = (found != null) ? found.gameObject : null;
                }
            }
            if (t.axes == null)
            {
                continue;
            }
            var show = !isOpen || (i == index);
            if (t.axes.activeSelf != show)
            {
                t.axes.SetActive(show);
            }
        }
    }

    /// <summary>
    /// 画面クリックで調整対象を切り替える。表示中のゴースト／削除範囲の球にレイを当て、
    /// 最も近いものを選ぶ。表示オブジェクトはコライダを持たないためレンダラ境界で判定する。
    /// </summary>
    private void PickTargetByClick()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if ((mouse == null) || !mouse.leftButton.wasPressedThisFrame || (dragIndex >= 0))
        {
            return;
        }
        var pos = mouse.position.ReadValue();
        // パネル上のクリックは操作なので対象切り替えしない（GUI座標は上原点のためY反転）
        if (window.Contains(new Vector2(pos.x, Screen.height - pos.y)))
        {
            return;
        }
        var cam = Camera.main;
        if (cam == null)
        {
            return;
        }
        var ray = cam.ScreenPointToRay(pos);
        var best = -1;
        var bestDist = float.MaxValue;
        for (var i = 0; i < targets.Count; i++)
        {
            var obj = targets[i].hitObject;
            if ((obj == null) || !obj.activeInHierarchy)
            {
                continue;
            }
            foreach (var rend in obj.GetComponentsInChildren<Renderer>())
            {
                if ((rend is LineRenderer) || !rend.enabled)
                {
                    // 原点マーカーの軸線は当たり判定に含めない
                    continue;
                }
                if (rend.bounds.IntersectRay(ray, out var dist) && (dist < bestDist))
                {
                    bestDist = dist;
                    best = i;
                }
            }
        }
        if (best >= 0)
        {
            index = best;
            editedIndex = -1;
        }
    }

    private void OnDisable()
    {
        BacketPathOverlay.ForceShow = false;
    }

    /// <summary>
    /// 軸ラベルの左右ドラッグで値を増減する（Unity Inspector のスクラブ操作と同じ感覚）。
    /// Shift=×10（粗調）、Ctrl=×0.1（微調）。
    /// </summary>
    /// <param name="rect">ラベルの矩形（ウィンドウ座標）</param>
    /// <param name="i">対象軸</param>
    /// <param name="perPixel">1ピクセルあたりの変化量</param>
    /// <param name="values">編集中の値配列</param>
    /// <param name="changed">値が変わったらtrueにする</param>
    /// <returns>この軸をドラッグ操作したか</returns>
    private bool HandleLabelDrag(Rect rect, int i, float perPixel, float[] values, ref bool changed)
    {
        var e = Event.current;
        if (e == null)
        {
            return false;
        }
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if ((e.type == EventType.MouseDown) && (e.button == 0) && rect.Contains(e.mousePosition))
        {
            dragIndex = i;
            // GUIイベントのmousePositionはウィンドウ内でしか更新されないため、
            // ドラッグ量は実際のマウス座標（スクリーン）で測る
            dragStartX = (mouse != null) ? mouse.position.x.ReadValue() : 0f;
            dragStartValue = values[i];
            e.Use();
            return true;
        }
        if (dragIndex != i)
        {
            return false;
        }
        if ((mouse == null) || !mouse.leftButton.isPressed)
        {
            // ボタンを離したら終了（ウィンドウ外で離しても確実に解除される）
            dragIndex = -1;
            return false;
        }
        // 実行時のOnGUIは毎フレーム呼ばれるため、Repaintで1フレーム1回だけ反映する。
        // ウィンドウ外までドラッグしても追従する
        if (e.type == EventType.Repaint)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var scale = 1f;
            if (kb != null)
            {
                scale = kb.shiftKey.isPressed ? 10f : (kb.ctrlKey.isPressed ? 0.1f : 1f);
            }
            var v = dragStartValue + ((mouse.position.x.ReadValue() - dragStartX) * perPixel * scale);
            if (!Mathf.Approximately(v, values[i]))
            {
                values[i] = v;
                changed = true;
            }
        }
        return true;
    }

    private void OnGUI()
    {
        if (!isOpen)
        {
            return;
        }
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
            };
            // ドラッグできることが分かるよう軸ラベルは下線付きにする
            dragLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
            };
            dragLabelStyle.normal.textColor = new Color(0.35f, 0.55f, 0.85f);
        }
        window = GUILayout.Window(GetInstanceID(), window, DrawWindow, "ワーク位置調整 (F9)");
    }

    private void DrawWindow(int id)
    {
        if (targets.Count == 0)
        {
            GUILayout.Label("調整できる対象がありません。\n生成位置は「設計位置を使用」がOFFの設定のみ対象です。");
            GUI.DragWindow();
            return;
        }
        index = Mathf.Clamp(index, 0, targets.Count - 1);
        var t = targets[index];

        // 対象の切り替え
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("＜", GUILayout.Width(34f)))
        {
            index = (index - 1 + targets.Count) % targets.Count;
            editedIndex = -1;
        }
        GUILayout.Label($"{index + 1}/{targets.Count}", GUILayout.Width(44f));
        if (GUILayout.Button("＞", GUILayout.Width(34f)))
        {
            index = (index + 1) % targets.Count;
            editedIndex = -1;
        }
        GUILayout.EndHorizontal();
        GUILayout.Label(t.label, titleStyle);
        if (!string.IsNullOrEmpty(t.tagName))
        {
            // どのタグで動く設定なのかと、その現在値を出す。
            // 「位置は合っているのに動かない」ときにタグ側の問題かを切り分けられる
            GUILayout.BeginHorizontal();
            GUILayout.Label("タグ", GUILayout.Width(38f));
            GUILayout.Label(t.tagName, GUILayout.Width(180f));
            if (t.getTagValue != null)
            {
                var value = t.getTagValue();
                var style = new GUIStyle(GUI.skin.label);
                style.normal.textColor = value != 0 ? new Color(0.35f, 0.7f, 1f) : new Color(1f, 0.45f, 0.45f);
                style.fontStyle = FontStyle.Bold;
                GUILayout.Label(value != 0 ? "ON" : "OFF", style, GUILayout.Width(40f));
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.Space(4f);

        var pos = t.getPos();
        var rot = (t.getRot != null) ? t.getRot() : Vector3.zero;
        // 位置は内部m・表示mm。入力欄の桁が増えないよう丸めて扱う
        var values = new float[]
        {
            pos.x * 1000f, pos.y * 1000f, pos.z * 1000f,
            rot.x, rot.y, rot.z,
        };
        var names = new string[] { "X", "Y", "Z", "RX", "RY", "RZ" };
        var units = new string[] { "mm", "mm", "mm", "°", "°", "°" };
        var changed = false;

        for (var i = 0; i < 6; i++)
        {
            if ((i >= 3) && (t.getRot == null))
            {
                break;
            }
            var step = (i < 3) ? STEP_MM : STEP_DEG;
            GUILayout.BeginHorizontal();
            // ラベルは Unity Inspector と同じスクラブ操作のドラッグハンドルにする
            GUILayout.Label(names[i], dragLabelStyle, GUILayout.Width(38f));
            if (HandleLabelDrag(GUILayoutUtility.GetLastRect(), i,
                (i < 3) ? DRAG_PER_PIXEL_MM : DRAG_PER_PIXEL_DEG, values, ref changed))
            {
                editedIndex = -1;
            }
            if (GUILayout.Button("＜", GUILayout.Width(34f)))
            {
                values[i] -= step;
                changed = true;
                editedIndex = -1;
            }
            // 入力途中の文字列を保つため、編集中の欄だけ文字列を優先する
            var shown = (editedIndex == i) ? edits[i] : values[i].ToString("0.###", CultureInfo.InvariantCulture);
            var input = GUILayout.TextField(shown, GUILayout.Width(90f));
            if (input != shown)
            {
                editedIndex = i;
                edits[i] = input;
                if (float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    values[i] = parsed;
                    changed = true;
                }
            }
            if (GUILayout.Button("＞", GUILayout.Width(34f)))
            {
                values[i] += step;
                changed = true;
                editedIndex = -1;
            }
            GUILayout.Label(units[i], GUILayout.Width(26f));
            GUILayout.EndHorizontal();
        }

        if (changed)
        {
            t.setPos(new Vector3(values[0] / 1000f, values[1] / 1000f, values[2] / 1000f));
            if (t.setRot != null)
            {
                t.setRot(new Vector3(values[3], values[4], values[5]));
            }
            t.apply?.Invoke();
        }

        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("値をコピー"))
        {
            // KMXTool の入力欄へ貼る用。位置mm・角度度の順
            GUIUtility.systemCopyBuffer = string.Join(", ", new string[]
            {
                values[0].ToString("0.###", CultureInfo.InvariantCulture),
                values[1].ToString("0.###", CultureInfo.InvariantCulture),
                values[2].ToString("0.###", CultureInfo.InvariantCulture),
                values[3].ToString("0.###", CultureInfo.InvariantCulture),
                values[4].ToString("0.###", CultureInfo.InvariantCulture),
                values[5].ToString("0.###", CultureInfo.InvariantCulture),
            });
        }
        if (GUILayout.Button("0 に戻す"))
        {
            t.setPos(Vector3.zero);
            t.setRot?.Invoke(Vector3.zero);
            t.apply?.Invoke();
            editedIndex = -1;
        }
        GUILayout.EndHorizontal();
        GUILayout.Label("軸名(青)を左右ドラッグで連続調整（Shift=×10 / Ctrl=×0.1）。＜＞は10mm・1°刻み。"
            + "欄へ直接入力も可。値はKMXTool側へ転記してください（KMXの変更は保存されません）",
            new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true });
        GUI.DragWindow();
    }
}
