using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;

/// <summary>
/// 経路計画ステップの「教示済みキャッシュ軌道」の永続ストア。
/// 登録モードで教示した軌道を robotId + step 単位で保存し、自動再生モードで再生する。
/// 保存先は StreamingAssets/Datas/Ros2TrajCache.json（Standalone のみ書込可＝ROS2 も Standalone 前提）。
/// 登録時の start/end 姿勢も保持し、現在姿勢ズレ（開始点≠登録時）や poseDeg 変更の検出に使う。
/// ※このファイルは実行時に KMX が生成・更新する（KMX Tool の *Info.json とは別系統）。
/// </summary>
public static class Ros2TrajCacheStore
{
    /// <summary>1ステップ分の教示済み軌道キャッシュ。</summary>
    [Serializable]
    public class Entry
    {
        public string robotId { get; set; } = "";
        public int stepIndex { get; set; }
        public string name { get; set; } = "";
        /// <summary>登録時の開始姿勢（＝前ステップの終了姿勢）。現在姿勢との一致判定に使う。</summary>
        public List<float> startDeg { get; set; } = new();
        /// <summary>登録時の終了姿勢（＝step.poseDeg）。poseDeg 変更検出に使う。</summary>
        public List<float> endDeg { get; set; } = new();
        /// <summary>各点の時刻(秒)。長さ=点数。</summary>
        public List<float> timesSec { get; set; } = new();
        /// <summary>各点の関節角(度)。positions[点][関節]。</summary>
        public List<List<float>> positions { get; set; } = new();
        /// <summary>軌道の関節名（順序）。</summary>
        public List<string> jointNames { get; set; } = new();
        /// <summary>ROS2 最適化が返した達成可能な最短時間(秒)。0=不明（旧キャッシュ）。表示専用。</summary>
        public float minTimeSec { get; set; }
    }

    private static readonly List<Entry> entries = new();
    private static bool loaded;

    private static string FilePath =>
        Path.Combine(Application.streamingAssetsPath, "Datas", "Ros2TrajCache.json");

    /// <summary>ファイルから読み込む（初回アクセス時に自動でも呼ばれる）。</summary>
    public static void Load()
    {
        entries.Clear();
        loaded = true;
        try
        {
            var path = FilePath;
            if (!File.Exists(path))
            {
                return;
            }
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<Entry>>(json);
            if (list != null)
            {
                entries.AddRange(list);
            }
            Debug.Log($"[Ros2TrajCache] ロード {entries.Count}件: {path}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Ros2TrajCache] ロード失敗: {e.Message}");
        }
    }

    private static void EnsureLoaded()
    {
        if (!loaded)
        {
            Load();
        }
    }

    private static void Save()
    {
        try
        {
            var dir = Path.Combine(Application.streamingAssetsPath, "Datas");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
            Debug.Log($"[Ros2TrajCache] 保存 {entries.Count}件: {FilePath}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Ros2TrajCache] 保存失敗: {e.Message}");
        }
    }

    /// <summary>該当 robotId+step の登録キャッシュを返す（無ければ null）。</summary>
    public static Entry Get(string robotId, int stepIndex)
    {
        EnsureLoaded();
        return entries.Find(e => e.robotId == robotId && e.stepIndex == stepIndex);
    }

    /// <summary>登録（同一 robotId+step があれば置換）。</summary>
    public static void Put(Entry e)
    {
        EnsureLoaded();
        entries.RemoveAll(x => x.robotId == e.robotId && x.stepIndex == e.stepIndex);
        entries.Add(e);
        Save();
    }

    /// <summary>削除（再登録可能にする）。</summary>
    public static void Delete(string robotId, int stepIndex)
    {
        EnsureLoaded();
        int n = entries.RemoveAll(x => x.robotId == robotId && x.stepIndex == stepIndex);
        if (n > 0)
        {
            Save();
        }
    }
}
