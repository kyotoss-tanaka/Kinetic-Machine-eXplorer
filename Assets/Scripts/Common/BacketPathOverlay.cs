using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// バケット経路の周長情報表示：Ctrl+Shift を押している間だけ画面左上に
/// 「ユニット名 [経路名] 幾何周長 / 周長設定 / 差」を表示する。
/// 自己生成（シーン編集不要）。エントリはバケット生成時に AxisMotionBase から登録される。
/// 周長設定の値決めに使う想定（幾何周長＝外形ラップで生成した経路の実測長）。
/// </summary>
public class BacketPathOverlay : MonoBehaviour
{
    private class Entry
    {
        public string unitName;
        public string pathName;
        public float geomMm;   // 幾何経路長(mm)
        public float loopMm;   // 周長設定(mm)。0=未設定（幾何長で動作）
    }

    private static readonly Dictionary<string, Entry> entries = new();
    /// <summary>経路確認ライン（Ctrl+Shift押下中のみアクティブ化する）</summary>
    private static readonly Dictionary<string, GameObject> lines = new();
    /// <summary>Prefab非表示時にも表示を維持するモデル（経路のスプロケット/経由点で参照しているモデル）</summary>
    public static readonly HashSet<Transform> KeepVisibleModels = new();
    private GUIStyle style;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("BacketPathOverlay");
        DontDestroyOnLoad(go);
        go.AddComponent<BacketPathOverlay>();
    }

    /// <summary>バケット生成時に経路情報を登録する（同名ユニットは上書き）</summary>
    public static void Register(string unitName, string pathName, float geomLengthMm, float loopLengthMm)
    {
        entries[unitName] = new Entry
        {
            unitName = unitName,
            pathName = pathName,
            geomMm = geomLengthMm,
            loopMm = loopLengthMm,
        };
    }

    /// <summary>経路確認ラインを登録する（同名ユニットは上書き。GameObject自体の破棄は生成元が行う）</summary>
    public static void RegisterLine(string unitName, GameObject line)
    {
        lines[unitName] = line;
    }

    /// <summary>再ロード時に全エントリを消す（機番構成が変わっても古い表示が残らないように）</summary>
    public static void Clear()
    {
        entries.Clear();
        lines.Clear();
        KeepVisibleModels.Clear();
    }

    private void Update()
    {
        if (lines.Count == 0)
        {
            return;
        }
        // Ctrl+Shift 押下中のみ経路ラインを表示（周長オーバーレイと同じ操作系）
        var kb = UnityEngine.InputSystem.Keyboard.current;
        var show = kb != null && kb.ctrlKey.isPressed && kb.shiftKey.isPressed;
        foreach (var line in lines.Values)
        {
            if (line != null && line.activeSelf != show)
            {
                line.SetActive(show);
            }
        }
    }

    private void OnGUI()
    {
        if (entries.Count == 0)
        {
            return;
        }
        // Ctrl+Shift 押下中のみ表示（WebGlFpsDebug と同じ操作系）
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null || !(kb.ctrlKey.isPressed && kb.shiftKey.isPressed))
        {
            return;
        }

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
            };
        }

        var y = 8f;
        foreach (var e in entries.Values.OrderBy(d => d.unitName))
        {
            var pathLabel = string.IsNullOrEmpty(e.pathName) ? "" : $" [{e.pathName}]";
            var loopLabel = e.loopMm > 0f
                ? $"周長設定={e.loopMm:F1}mm (差 {e.geomMm - e.loopMm:+0.0;-0.0}mm)"
                : "周長設定なし（幾何周長で動作）";
            var text = $"{e.unitName}{pathLabel} 幾何周長={e.geomMm:F1}mm {loopLabel}";
            var rect = new Rect(8f, y, 1200f, 24f);

            // 影（視認性向上）→ 本体
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, style);
            style.normal.textColor = new Color(0.4f, 0.9f, 1f, 1f);   // 水色
            GUI.Label(rect, text, style);
            y += 24f;
        }
    }
}
