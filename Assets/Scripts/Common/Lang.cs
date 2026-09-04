using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// 言語対応（日本語原文をキーにした対訳辞書方式）
/// 辞書: StreamingAssets/Datas/Lang/{lang}.json （"日本語原文": "訳文" の対応表）
/// 言語設定: BuildConfig.json の language（"auto"=OS言語に追従 / "ja" / "en"）
/// 辞書に無い文言は日本語のまま表示されるため、段階的に対応範囲を広げられる
/// </summary>
public static class Lang
{
    /// <summary>
    /// 現在の言語（"ja" / "en"）
    /// </summary>
    public static string Language { get; private set; } = "ja";

    /// <summary>
    /// 英語表示か
    /// </summary>
    public static bool IsEnglish
    {
        get
        {
            return Language == "en";
        }
    }

    /// <summary>
    /// 対訳辞書（日本語原文 → 訳文）
    /// </summary>
    private static Dictionary<string, string> dictionary = new();

    /// <summary>
    /// 初期化（BuildConfig読込後に呼ぶ）
    /// </summary>
    /// <param name="setting">"auto" / "ja" / "en"</param>
    public static async Task Initialize(string setting)
    {
        var lang = setting;
        if ((lang == null) || (lang == "") || (lang == "auto"))
        {
            // OS言語に追従（日本語以外は英語）
            lang = Application.systemLanguage == SystemLanguage.Japanese ? "ja" : "en";
        }
        Language = lang;
        dictionary = new Dictionary<string, string>();
        if (Language == "ja")
        {
            return;
        }
        try
        {
            var json = await GlobalScript.LoadJsonFromStreamingAssetsAsync($"Datas/Lang/{Language}.json");
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict != null)
            {
                dictionary = dict;
            }
            CommonFunction.DebugLog($"***** Language: {Language} ({dictionary.Count} entries) *****");
            // コード設定文言を拾う定期スイープを開始
            if ((dictionary.Count > 0) && (UnityEngine.Object.FindFirstObjectByType<LangSweeper>() == null))
            {
                new GameObject("LangSweeper").AddComponent<LangSweeper>();
            }
        }
        catch (Exception e)
        {
            CommonFunction.DebugLog($"言語辞書の読み込みに失敗: {Language} ({e.Message})");
        }
    }

    /// <summary>
    /// 対訳を返す（辞書に無ければ原文のまま）
    /// </summary>
    /// <param name="ja">日本語原文</param>
    /// <returns></returns>
    public static string T(string ja)
    {
        if ((ja == null) || (dictionary.Count == 0))
        {
            return ja;
        }
        return dictionary.TryGetValue(ja, out var translated) ? translated : ja;
    }

    /// <summary>
    /// シーン内の全テキスト（非アクティブ含む）を辞書一致で置換する
    /// プレハブ埋め込みの静的ラベル用。ロード完了時に呼ぶ
    /// </summary>
    public static void TranslateAllTexts()
    {
        if (dictionary.Count == 0)
        {
            return;
        }
        foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Translate(text);
        }
        foreach (var text in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            TranslateOne(text);
        }
    }

    /// <summary>1テキストを辞書一致で置換する（定期スイープ用の公開版）</summary>
    public static void TranslateOne(TMP_Text text)
    {
        Translate(text);
    }

    /// <summary>1テキストを辞書一致で置換する（uGUI Text）</summary>
    public static void TranslateOne(UnityEngine.UI.Text text)
    {
        if ((text != null) && (text.text != null) && dictionary.TryGetValue(text.text.Trim(), out var translated))
        {
            // 枠に収まらない場合は自動縮小して収める
            if (!text.resizeTextForBestFit)
            {
                text.resizeTextMaxSize = text.fontSize;
                text.resizeTextMinSize = Mathf.Max(1, text.fontSize / 2);
                text.resizeTextForBestFit = true;
            }
            text.text = translated;
        }
    }

    /// <summary>
    /// 指定オブジェクト配下のテキストを置換する（実行中に生成したUI用）
    /// </summary>
    /// <param name="root"></param>
    public static void Translate(GameObject root)
    {
        if ((dictionary.Count == 0) || (root == null))
        {
            return;
        }
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            Translate(text);
        }
    }

    /// <summary>
    /// テキストコンポーネントを辞書一致で置換する
    /// </summary>
    /// <param name="text"></param>
    private static void Translate(TMP_Text text)
    {
        if ((text != null) && (text.text != null) && dictionary.TryGetValue(text.text.Trim(), out var translated))
        {
            // 英訳は日本語より長くなりがちなので、枠に収まらない場合は自動縮小して収める
            if (!text.enableAutoSizing)
            {
                text.fontSizeMax = text.fontSize;
                text.fontSizeMin = text.fontSize * 0.5f;
                text.enableAutoSizing = true;
            }
            text.text = translated;
        }
    }
}
