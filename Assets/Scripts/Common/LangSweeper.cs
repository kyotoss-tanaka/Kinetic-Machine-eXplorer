using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 表示テキストの定期言語置換
/// コードから設定される文言（メニュー項目・ボタン等の静的リテラル）を、
/// 各所にラッパーを入れずに辞書一致で英語化するための軽量スイープ。
/// 英語設定時のみ Lang.Initialize() から生成される。
/// 負荷対策:
/// - 前回スイープから変更のないテキストは照合をスキップ（文字列参照の一致判定＝アロケーションなし）
/// - 照合はフレーム分割して行い、1秒ごとのスパイクを作らない
/// </summary>
public class LangSweeper : MonoBehaviour
{
    /// <summary>
    /// スイープ間隔(秒)
    /// </summary>
    private const float INTERVAL = 1f;

    /// <summary>
    /// 1フレームで照合する件数
    /// </summary>
    private const int CHUNK = 200;

    /// <summary>
    /// 前回スイープ時のテキスト参照（InstanceID→文字列。同一参照なら変更なし＝照合スキップ）
    /// </summary>
    private readonly Dictionary<int, string> lastSeen = new Dictionary<int, string>();

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
            yield return wait;
            if (!GlobalScript.isLoaded)
            {
                continue;
            }
            // 破棄済みテキストの記録が溜まりすぎたらリセット（次周で全照合し直す）
            if (lastSeen.Count > 20000)
            {
                lastSeen.Clear();
            }
            var done = 0;
            foreach (var text in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (text == null)
                {
                    continue;
                }
                var id = text.GetInstanceID();
                var current = text.text;
                if (lastSeen.TryGetValue(id, out var seen) && ReferenceEquals(seen, current))
                {
                    // 前回から変更なし（参照一致）＝照合不要
                    continue;
                }
                Lang.TranslateOne(text);
                lastSeen[id] = text.text;
                if ((++done % CHUNK) == 0)
                {
                    yield return null;
                }
            }
            foreach (var text in Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (text == null)
                {
                    continue;
                }
                var id = text.GetInstanceID();
                var current = text.text;
                if (lastSeen.TryGetValue(id, out var seen) && ReferenceEquals(seen, current))
                {
                    continue;
                }
                Lang.TranslateOne(text);
                lastSeen[id] = text.text;
                if ((++done % CHUNK) == 0)
                {
                    yield return null;
                }
            }
        }
    }
}
