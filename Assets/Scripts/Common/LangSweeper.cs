using System.Collections;
using UnityEngine;

/// <summary>
/// 表示テキストの定期言語置換
/// コードから設定される文言（メニュー項目・ボタン等の静的リテラル）を、
/// 各所にラッパーを入れずに辞書一致で英語化するための軽量スイープ。
/// 英語設定時のみ Lang.Initialize() から生成される。
/// </summary>
public class LangSweeper : MonoBehaviour
{
    /// <summary>
    /// スイープ間隔(秒)
    /// </summary>
    private const float INTERVAL = 1f;

    private void Start()
    {
        DontDestroyOnLoad(gameObject);
        StartCoroutine(Sweep());
    }

    /// <summary>
    /// 定期スイープ
    /// </summary>
    /// <returns></returns>
    private IEnumerator Sweep()
    {
        var wait = new WaitForSecondsRealtime(INTERVAL);
        while (true)
        {
            if (GlobalScript.isLoaded)
            {
                Lang.TranslateAllTexts();
            }
            yield return wait;
        }
    }
}
