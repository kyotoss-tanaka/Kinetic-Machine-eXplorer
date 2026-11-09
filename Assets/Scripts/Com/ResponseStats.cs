using System.Collections.Generic;

/// <summary>
/// 外部通信の応答時間（PLC からの開始IOを受けてから、完了IOを送り終えるまで）の集計。Direct Com Info に表示する。
/// MotionInternal が通信スレッドとメインスレッドの両方から足すので lock で守る（Unity の機能は使わない）
/// </summary>
public static class ResponseStats
{
    /// <summary>
    /// 遅れとして数える閾値(ms)。予定より これ以上遅れた回を数える（[応答遅れ] のログと同じ）
    /// </summary>
    public const float LateThresholdMs = 100f;

    /// <summary>
    /// 1動作（ユニット＋完了IO）ごとの集計
    /// </summary>
    public class Entry
    {
        /// <summary>ユニット名</summary>
        public string unit;
        /// <summary>完了IO名</summary>
        public string end;
        /// <summary>予定時間(ms)＝動作時間－通信遅れ時間</summary>
        public float expectedMs;
        /// <summary>直近の実測(ms)</summary>
        public float lastMs;
        /// <summary>予定より遅れた最大(ms)</summary>
        public float maxLateMs;
        /// <summary>計測回数</summary>
        public int count;
        /// <summary>閾値以上遅れた回数</summary>
        public int lateCount;
    }

    private static readonly object sync = new();

    /// <summary>
    /// ユニット名＋完了IO名 → 集計
    /// </summary>
    private static readonly Dictionary<string, Entry> entries = new();

    /// <summary>
    /// 変わるたびに増える番号（表示側が書き直すか決める）
    /// </summary>
    public static int Version { get; private set; }

    /// <summary>
    /// 1回分を足す
    /// </summary>
    /// <param name="unit">ユニット名</param>
    /// <param name="end">完了IO名</param>
    /// <param name="expectedMs">予定時間(ms)</param>
    /// <param name="totalMs">開始IOを受けてから完了IOを送り終えるまで(ms)</param>
    public static void Add(string unit, string end, float expectedMs, float totalMs)
    {
        var lateMs = totalMs - expectedMs;
        lock (sync)
        {
            var key = unit + "\n" + end;
            if (!entries.TryGetValue(key, out var e))
            {
                e = new Entry { unit = unit, end = end, maxLateMs = lateMs };
                entries.Add(key, e);
            }
            e.expectedMs = expectedMs;
            e.lastMs = totalMs;
            e.maxLateMs = e.count == 0 ? lateMs : System.Math.Max(e.maxLateMs, lateMs);
            e.count++;
            if (lateMs >= LateThresholdMs)
            {
                e.lateCount++;
            }
            Version++;
        }
    }

    /// <summary>
    /// 集計を消す（ロードし直した時・表示のリセット）
    /// </summary>
    public static void Clear()
    {
        lock (sync)
        {
            entries.Clear();
            Version++;
        }
    }

    /// <summary>
    /// 今の集計の写し（遅れの大きい順）と合計
    /// </summary>
    public static List<Entry> Snapshot(out int totalCount, out int totalLate)
    {
        var list = new List<Entry>();
        totalCount = 0;
        totalLate = 0;
        lock (sync)
        {
            foreach (var e in entries.Values)
            {
                list.Add(new Entry
                {
                    unit = e.unit,
                    end = e.end,
                    expectedMs = e.expectedMs,
                    lastMs = e.lastMs,
                    maxLateMs = e.maxLateMs,
                    count = e.count,
                    lateCount = e.lateCount,
                });
                totalCount += e.count;
                totalLate += e.lateCount;
            }
        }
        list.Sort((a, b) => b.maxLateMs.CompareTo(a.maxLateMs));
        return list;
    }
}
