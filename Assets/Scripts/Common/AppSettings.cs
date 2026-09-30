using UnityEngine;

/// <summary>
/// Application Settings の設定値（PlayerPrefs に保存し、次の起動でも同じ値で始める）。
/// 保存先はこのPC・このアプリ単位（XR表示の設定と同じ仕組み）。データフォルダの設定ではないので KMXTool の出力で上書きされない
/// </summary>
public static class AppSettings
{
    private const string KeyLines = "kmx.setting.useLines";
    private const string KeyPhysics = "kmx.setting.usePhysics";
    private const string KeyCollision = "kmx.setting.useCollision";
    private const string KeyHistory = "kmx.setting.useHistory";

    /// <summary>
    /// 見るだけの履歴（TimeController の Prev/Next で過去の見た目を表示する）。記録の分だけ処理が重くなるので既定は OFF
    /// </summary>
    public static bool UseHistory
    {
        get
        {
            if (!historyLoaded)
            {
                useHistory = Get(KeyHistory, false);
                historyLoaded = true;
            }
            return useHistory;
        }
        set
        {
            useHistory = value;
            historyLoaded = true;
            Set(KeyHistory, value);
        }
    }
    private static bool useHistory;
    private static bool historyLoaded;

    /// <summary>
    /// 輪郭線の表示（既定は Prefab と同じ ON）
    /// </summary>
    public static bool UseLines
    {
        get => Get(KeyLines, true);
        set
        {
            GlobalScript.isLiens = value;
            Set(KeyLines, value);
        }
    }

    /// <summary>
    /// 物理（既定は Prefab と同じ ON）
    /// </summary>
    public static bool UsePhysics
    {
        get => Get(KeyPhysics, true);
        set
        {
            Physics.simulationMode = value ? SimulationMode.FixedUpdate : SimulationMode.Script;
            Set(KeyPhysics, value);
        }
    }

    /// <summary>
    /// 衝突判定（既定は Prefab と同じ OFF）
    /// </summary>
    public static bool UseCollision
    {
        get => Get(KeyCollision, false);
        set
        {
            GlobalScript.isCollision = value;
            Set(KeyCollision, value);
        }
    }

    /// <summary>
    /// 起動時に保存してある値を反映する（GlobalScript の初期化の後。メニューを開かなくても効くように）
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplySaved()
    {
        historyLoaded = false;
        GlobalScript.isLiens = UseLines;
        Physics.simulationMode = UsePhysics ? SimulationMode.FixedUpdate : SimulationMode.Script;
        GlobalScript.isCollision = UseCollision;
    }

    private static bool Get(string key, bool defaultValue)
    {
        return PlayerPrefs.GetInt(key, defaultValue ? 1 : 0) == 1;
    }

    private static void Set(string key, bool value)
    {
        PlayerPrefs.SetInt(key, value ? 1 : 0);
        PlayerPrefs.Save();
    }
}
