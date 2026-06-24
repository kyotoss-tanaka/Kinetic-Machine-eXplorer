using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// WebGL専用：パラメータ読込の進捗(GlobalScript.loadProgress)・コメント(loadLabel)・完了(isLoaded)を
/// HTMLテンプレート(index.html)のローディング画面へ JS 経由で渡す。
///
/// これにより WebGL は「HTMLのDL画面」と「ゲーム内画面」を切り替えずに、HTML1枚で
/// DL→パラメータ読込まで通し（進捗バーもリセット無しで 0→100%）、つなぎ目を作らない。
/// jslib: Assets/Plugins/WebGL/KmxLoading.jslib。HTML側の window.KmxLoading が受ける。
/// </summary>
public class WebGlLoadingBridge : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void KmxLoadingProgress(float p, string label);
    [DllImport("__Internal")] private static extern void KmxLoadingDone();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("WebGlLoadingBridge");
        DontDestroyOnLoad(go);
        go.AddComponent<WebGlLoadingBridge>();
    }

    private bool done;

    private void Update()
    {
        if (done)
        {
            return;
        }
        try
        {
            KmxLoadingProgress(Mathf.Clamp01(GlobalScript.loadProgress), GlobalScript.loadLabel ?? "");
            if (GlobalScript.isLoaded)
            {
                KmxLoadingDone();
                done = true;
            }
        }
        catch
        {
            // JS未定義等は無視（HTML側にフォールバックの自動非表示あり）
        }
    }
#endif
}
