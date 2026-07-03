using UnityEngine;

/// <summary>
/// WebGL専用デバッグ表示：Ctrl+Shift を押している間だけ画面右上に FPS を表示する。
/// 自己生成（シーン編集不要）。WebGL ビルド（およびWebGLターゲットのEditor）でのみ有効。
/// ASCII のみなので OnGUI（既定フォント）で軽量実装し、JOG等の uGUI とは独立。
/// </summary>
public class WebGlFpsDebug : MonoBehaviour
{
#if UNITY_WEBGL
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("WebGlFpsDebug");
        DontDestroyOnLoad(go);
        go.AddComponent<WebGlFpsDebug>();
    }

    private float fps;
    private float accum;
    private int frames;
    private GUIStyle style;

    private void Update()
    {
        // 0.5秒平均で FPS 算出（unscaled＝timeScaleの影響を受けない実フレームレート）
        accum += Time.unscaledDeltaTime;
        frames++;
        if (accum >= 0.5f)
        {
            fps = frames / accum;
            accum = 0f;
            frames = 0;
        }
    }

    private void OnGUI()
    {
        // Ctrl+Shift 押下中のみ表示（新 Input System）
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null || !(kb.ctrlKey.isPressed && kb.shiftKey.isPressed))
        {
            return;
        }

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperRight,
            };
        }

        float ms = fps > 0f ? 1000f / fps : 0f;
        int tgt = Application.targetFrameRate;
        string tgtStr = tgt > 0 ? tgt.ToString() : "uncap";   // -1/0 = 未設定(無制限)
        string text = $"FPS {fps:0}  ({ms:0.0} ms)  target {tgtStr}";
        var rect = new Rect(Screen.width - 310f, 8f, 300f, 28f);

        // 影（視認性向上）→ 本体
        style.normal.textColor = Color.black;
        GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, style);
        style.normal.textColor = new Color(0.3f, 1f, 0.4f, 1f);   // 緑
        GUI.Label(rect, text, style);
    }
#endif
}
