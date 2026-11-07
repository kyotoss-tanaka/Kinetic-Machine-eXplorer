#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// Addressables の Play Mode Script の戻し忘れを防ぐ。
/// プレハブの登録（AddressablePrefabRegistrar）とビルド（BuildAndRun）は一時的に「Use Existing Build」に切り替えて最後に
/// 「Use Asset Database」へ戻すが、途中で例外になったり固まって Unity を落としたりすると戻らず、
/// 再生が「Player content must be built before entering play mode…」で止まっていた（手で戻すしかなかった）。
/// 切り替えた事を EditorPrefs に控え、次の機会（エディタの起動・スクリプトの再読込・再生）に戻す。
/// 自分で「Use Existing Build」を選んだ場合（控えが無い）は触らない
/// </summary>
[InitializeOnLoad]
public static class AddressablesPlayModeGuard
{
    /// <summary>Use Asset Database（BuildScriptFastMode）。DataBuilders の並び順</summary>
    private const int AssetDatabaseIndex = 0;

    /// <summary>Use Existing Build（BuildScriptPackedPlayMode）</summary>
    private const int ExistingBuildIndex = 1;

    /// <summary>
    /// 一時的に切り替え中の控え（EditorPrefs はPC単位なのでプロジェクトのパスを付ける）
    /// </summary>
    private static string PrefKey => "KMX_AddressablesPlayModeTemp:" + Application.dataPath;

    /// <summary>
    /// 処理中（登録・ビルドの最中は戻さない。スクリプトの再読込で false に戻る）
    /// </summary>
    private static bool isBusy;

    static AddressablesPlayModeGuard()
    {
        // 起動・再読込の直後（固まって落とした後の起動もここ）。設定の読み込みが済んでから見る
        EditorApplication.delayCall += () => RestoreIfLeft("エディタの起動・スクリプトの再読込");
        // 再生の直前。Addressables の確認（AddressablesBuildScriptHooks）の方が先に動くので、その回の再生には間に合わない。
        // ここで戻しておき、次の再生から動くようにする（最後の手段）
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                RestoreIfLeft("再生の直前（今回の再生は止まった可能性があります。もう一度再生してください）");
            }
        };
    }

    /// <summary>
    /// 「Use Existing Build」に一時的に切り替える（戻すのは Restore）
    /// </summary>
    public static void UseExistingBuild(AddressableAssetSettings settings)
    {
        isBusy = true;
        EditorPrefs.SetBool(PrefKey, true);
        settings.ActivePlayModeDataBuilderIndex = ExistingBuildIndex;
    }

    /// <summary>
    /// 「Use Asset Database」に戻す（保存は呼び出し側）
    /// </summary>
    public static void Restore(AddressableAssetSettings settings)
    {
        if (settings != null)
        {
            settings.ActivePlayModeDataBuilderIndex = AssetDatabaseIndex;
        }
        EditorPrefs.DeleteKey(PrefKey);
        isBusy = false;
    }

    /// <summary>
    /// 切り替え中なら戻して保存する（処理が失敗した時用。切り替える前の失敗なら何もしない）
    /// </summary>
    public static void RestoreIfBusy(AddressableAssetSettings settings)
    {
        if (!isBusy)
        {
            return;
        }
        Restore(settings);
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// 一時的に切り替えたまま残っていたら戻す
    /// </summary>
    private static void RestoreIfLeft(string when)
    {
        if (isBusy || !EditorPrefs.GetBool(PrefKey, false))
        {
            return;
        }
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            return;
        }
        var left = settings.ActivePlayModeDataBuilderIndex != AssetDatabaseIndex;
        Restore(settings);
        if (left)
        {
            AssetDatabase.SaveAssets();
            Debug.LogWarning($"[Addressables] 登録・ビルドの途中で止まったため Play Mode Script が「Use Existing Build」のまま残っていました。「Use Asset Database」に戻しました（{when}）");
        }
    }
}
#endif
