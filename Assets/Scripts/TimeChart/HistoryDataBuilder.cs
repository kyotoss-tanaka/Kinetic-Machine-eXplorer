using System.Collections.Generic;
using UnityEngine;

namespace KyotoSS.TimingChart
{
    /// <summary>
    /// GlobalScript.timeChartDatas から HistoryChannel リストを生成する。
    ///
    /// ルール：
    ///   tagIn ON   : start
    ///   tagOut ON  : start + time（サイクルまたぎは -cycleTime）
    ///   tagIn OFF  : tagOut ON と同時
    ///   tagOut OFF : 同じtagOutの1つ前の tagIn ON のタイミング
    ///               （1つ前がなければ最後の tagIn ON）
    ///
    /// 位置チャンネル（アナログ）：
    ///   tagIn ON → pos値に向かって線形変化開始
    ///   tagOut ON → pos値に到達
    ///   pos値の大きいものが上（AnalogMax）、小さいものが下（AnalogMin）
    /// </summary>
    public static class HistoryDataBuilder
    {
        private class IoEvent
        {
            public string ChannelName;
            public float TimeMs;
            public float Value;
        }

        // ----------------------------------------------------------------
        // エントリポイント
        // ----------------------------------------------------------------

        public static List<TimeChartController.HistoryChannel> Build(
            List<Parameters.TimeChartData> timeChartDatas, int cycles = 2)
        {
            var result = new List<TimeChartController.HistoryChannel>();
            foreach (var data in timeChartDatas)
            {
                var channels = BuildFromDevices(data.datas, data.cycle, cycles);
                foreach (var ch in channels)
                    if (!result.Exists(c => c.Name == ch.Name))
                        result.Add(ch);
            }
            return result;
        }

        public static List<TimeChartController.HistoryChannel> Build(
            Parameters.TimeChartData data, int cycles = 2)
            => BuildFromDevices(data.datas, data.cycle, cycles);

        // ----------------------------------------------------------------
        // 内部処理
        // ----------------------------------------------------------------
        private static List<TimeChartController.HistoryChannel> BuildFromDevices(
            List<Parameters.TimeChartDevice> devices, float cycleTimeMs, int cycles)
        {
            var ioEvents = new List<IoEvent>();
            var tagInNames = new HashSet<string>();
            // チャンネル名 → PLCデバイス名（SubLabel用）key="デバイス名/IO名", value="X300"
            var devLabels = new Dictionary<string, string>();

            // tagIn名を事前収集（0時点初期値判定用）"デバイス名/IO名" 形式
            foreach (var dev in devices)
            {
                if (dev.positions == null) continue;
                if (dev.devType == Parameters.TimeChartDevice.DeviceType.External) continue;
                foreach (var pos in dev.positions)
                    if (!string.IsNullOrEmpty(pos.tagIn))
                        tagInNames.Add($"{dev.name}/{pos.tagIn}");
            }

            // 位置チャンネル用：デバイスごとにサンプルを直接生成
            var posChannels = new Dictionary<string, TimeChartController.HistoryChannel>();

            for (int cycle = 0; cycle < cycles; cycle++)
            {
                float offset = cycle * cycleTimeMs;

                foreach (var dev in devices)
                {
                    if (dev.positions == null || dev.positions.Count == 0) continue;

                    // ---- IO イベント生成 ----
                    // External デバイス（Mechanism）は tagIn がレジスタ名のためIOとして扱わない
                    if (dev.devType != Parameters.TimeChartDevice.DeviceType.External)
                    {
                        // 同じ pos 値（終了位置）のエントリは同一IOを共有する
                        var posIoMap = new Dictionary<string, (string tagIn, string tagOut)>();
                        int autoIdx = 1;

                        foreach (var pos in dev.positions)
                        {
                            string key = pos.pos.ToString("F4");
                            if (!posIoMap.ContainsKey(key))
                            {
                                string ti = string.IsNullOrEmpty(pos.tagIn) ? "" : pos.tagIn;
                                string to = string.IsNullOrEmpty(pos.tagOut) ? "" : pos.tagOut;
                                if (!string.IsNullOrEmpty(ti) || !string.IsNullOrEmpty(to))
                                    posIoMap[key] = (ti, to);
                            }
                        }
                        foreach (var pos in dev.positions)
                        {
                            string key = pos.pos.ToString("F4");
                            if (!posIoMap.ContainsKey(key))
                            {
                                posIoMap[key] = (
                                    $"{dev.name}_Pos{autoIdx}_Command",
                                    $"{dev.name}_Pos{autoIdx}_AS"
                                );
                                autoIdx++;
                            }
                        }

                        for (int i = 0; i < dev.positions.Count; i++)
                        {
                            var pos = dev.positions[i];
                            string key = pos.pos.ToString("F4");
                            var (resolvedTagIn, resolvedTagOut) = posIoMap[key];

                            if (!string.IsNullOrEmpty(pos.tagIn)) resolvedTagIn = pos.tagIn;
                            if (!string.IsNullOrEmpty(pos.tagOut)) resolvedTagOut = pos.tagOut;

                            if (string.IsNullOrEmpty(resolvedTagIn) && string.IsNullOrEmpty(resolvedTagOut))
                                continue;

                            float tagInOnMs = offset + pos.start;
                            float rawTagOut = pos.start + pos.time;
                            float tagOutOnMs = rawTagOut >= cycleTimeMs
                                ? offset + (rawTagOut - cycleTimeMs)
                                : offset + rawTagOut;
                            // tagIn OFF = tagOut ON と同時（指令は到達で解除）
                            float tagInOffMs = tagOutOnMs;
                            // tagOut（AS）OFF = 対となる指令（反対方向）がONしたタイミング
                            float tagOutOffMs = GetOppositeTagInOnTime(dev.positions, key, i, cycle, cycleTimeMs, cycles);

                            // "デバイス名/IO名" 形式でデバイスごとに独立したチャンネルとして登録
                            string chTagIn = string.IsNullOrEmpty(resolvedTagIn) ? "" : $"{dev.name}/{resolvedTagIn}";
                            string chTagOut = string.IsNullOrEmpty(resolvedTagOut) ? "" : $"{dev.name}/{resolvedTagOut}";

                            // DevIn/DevOut（PLCデバイス名）をサブラベル用に収集
                            if (!string.IsNullOrEmpty(chTagIn) && !string.IsNullOrEmpty(pos.devIn))
                                devLabels[chTagIn] = pos.devIn;
                            if (!string.IsNullOrEmpty(chTagOut) && !string.IsNullOrEmpty(pos.devOut))
                                devLabels[chTagOut] = pos.devOut;

                            if (!string.IsNullOrEmpty(chTagIn))
                            {
                                ioEvents.Add(new IoEvent { ChannelName = chTagIn, TimeMs = tagInOnMs, Value = 1f });
                                ioEvents.Add(new IoEvent { ChannelName = chTagIn, TimeMs = tagInOffMs, Value = 0f });
                            }
                            if (!string.IsNullOrEmpty(chTagOut))
                            {
                                ioEvents.Add(new IoEvent { ChannelName = chTagOut, TimeMs = tagOutOffMs, Value = 0f });
                                ioEvents.Add(new IoEvent { ChannelName = chTagOut, TimeMs = tagOutOnMs, Value = 1f });
                            }
                        }
                    }

                    // ---- 位置チャンネル生成 ----
                    // デバイス名が空の場合はスキップ
                    string devName = dev.name;
                    if (string.IsNullOrEmpty(devName)) continue;
                    if (!posChannels.TryGetValue(devName, out var posCh))
                    {
                        // External デバイスは devIn（PLCデバイス名）をサブラベルに表示
                        string extDevName = "";
                        if (dev.devType == Parameters.TimeChartDevice.DeviceType.External
                            && dev.positions != null && dev.positions.Count > 0)
                            extDevName = dev.positions[0].devIn ?? "";

                        posCh = new TimeChartController.HistoryChannel
                        {
                            Name = devName,
                            IsAnalog = true,
                            DeviceName = extDevName,
                        };
                        // pos値の最大・最小をAnalogMax/Minに設定
                        float minPos = float.MaxValue, maxPos = float.MinValue;
                        foreach (var p in dev.positions)
                        {
                            if (p.pos < minPos) minPos = p.pos;
                            if (p.pos > maxPos) maxPos = p.pos;
                        }
                        posCh.AnalogMin = minPos;
                        posCh.AnalogMax = maxPos;
                        posChannels[devName] = posCh;
                    }

                    // 各ポジションの移動を2点（開始・到達）で表現
                    // 前のポジションから今のポジションへ線形移動
                    for (int i = 0; i < dev.positions.Count; i++)
                    {
                        var pos = dev.positions[i];

                        float tagInOnMs = offset + pos.start;
                        float rawTagOut = pos.start + pos.time;
                        float tagOutOnMs = rawTagOut >= cycleTimeMs
                            ? offset + (rawTagOut - cycleTimeMs)
                            : offset + rawTagOut;

                        // tagIn ON のタイミング = 移動開始（前の pos 値）
                        // tagOut ON のタイミング = 到達（この pos 値）
                        // 前のポジションを取得
                        float prevPosVal = GetPrevPosValue(dev.positions, i, cycle, cycleTimeMs, cycles);

                        posCh.Samples.Add(new TimeChartController.HistorySample(tagInOnMs, prevPosVal));
                        posCh.Samples.Add(new TimeChartController.HistorySample(tagOutOnMs, pos.pos));
                    }
                }
            }

            // ---- IO イベントをソート・チャンネル化 ----
            ioEvents.Sort((a, b) =>
            {
                int c = a.TimeMs.CompareTo(b.TimeMs);
                if (c != 0) return c;
                return b.Value.CompareTo(a.Value);
            });

            var dict = new Dictionary<string, TimeChartController.HistoryChannel>();
            foreach (var ev in ioEvents)
            {
                if (string.IsNullOrEmpty(ev.ChannelName)) continue;
                if (!dict.TryGetValue(ev.ChannelName, out var ch))
                {
                    ch = new TimeChartController.HistoryChannel { Name = ev.ChannelName };
                    dict[ev.ChannelName] = ch;
                }
                if (ch.Samples.Count > 0 &&
                    ch.Samples[ch.Samples.Count - 1].Value == ev.Value) continue;
                ch.Samples.Add(new TimeChartController.HistorySample(ev.TimeMs, ev.Value));
            }

            // ---- DeviceName（SubLabel）を設定 ----
            foreach (var ch in dict.Values)
                if (devLabels.TryGetValue(ch.Name, out var devLabel))
                    ch.DeviceName = devLabel;

            // ---- 0時点初期値 ----
            foreach (var ch in dict.Values)
            {
                if (ch.Samples.Count == 0) continue;
                float firstTime = ch.Samples[0].TimeMs;
                float firstValue = ch.Samples[0].Value;
                if (firstTime <= 0f) continue;

                float initValue = tagInNames.Contains(ch.Name)
                    ? 0f                                     // tagIn は常にOFF
                    : (firstValue > 0.5f ? 0f : 1f);       // tagOut は最初のイベントから逆算

                if (ch.Samples[0].Value != initValue)
                    ch.Samples.Insert(0, new TimeChartController.HistorySample(0f, initValue));
            }

            // ---- 位置チャンネルのサンプルをタイムスタンプ順にソート・重複除去 ----
            foreach (var posCh in posChannels.Values)
            {
                posCh.Samples.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));

                // 重複タイムスタンプを除去（後のサンプルを優先）
                for (int i = posCh.Samples.Count - 1; i > 0; i--)
                    if (Mathf.Approximately(posCh.Samples[i].TimeMs, posCh.Samples[i - 1].TimeMs))
                        posCh.Samples.RemoveAt(i - 1);
            }

            // ---- 0時点と最終時点を追加 ----
            foreach (var kv in posChannels)
            {
                var devName = kv.Key;
                var posCh = kv.Value;
                if (posCh.Samples.Count == 0) continue;

                // 対応するデバイスを検索
                Parameters.TimeChartDevice foundDev = null;
                foreach (var dev in devices)
                    if (dev.name == devName) { foundDev = dev; break; }
                if (foundDev == null || foundDev.positions.Count == 0) continue;

                // ---- 0時点初期位置：AS チャンネルの初期値から決定 ----
                // dict には上で初期値を設定済みの IO チャンネルが入っている
                // Samples[0].Value > 0.5f → t=0 で ON → その位置が初期位置
                float initPos = float.NaN;
                foreach (var pos in foundDev.positions)
                {
                    if (string.IsNullOrEmpty(pos.tagOut)) continue;
                    string chTagOut = $"{devName}/{pos.tagOut}";
                    if (dict.TryGetValue(chTagOut, out var asCh) &&
                        asCh.Samples.Count > 0 &&
                        asCh.Samples[0].Value > 0.5f)
                    {
                        initPos = pos.pos;
                        Debug.Log($"[PosInit] {devName}: AS {pos.tagOut} is ON at t=0 → initPos={initPos}");
                        break;
                    }
                }

                // AS から判断できない場合：サイクル内で最後に到達する位置を初期値に
                // （配列の末尾インデックスではなく start+time が最大の位置）
                if (float.IsNaN(initPos))
                {
                    float latestArrival = float.MinValue;
                    foreach (var pos in foundDev.positions)
                    {
                        float arrival = pos.start + pos.time;
                        if (arrival > latestArrival) { latestArrival = arrival; initPos = pos.pos; }
                    }
                    Debug.Log($"[PosInit] {devName}: max-arrival fallback initPos={initPos}");
                }

                // t=0 のサンプルを設定（既存なら上書き、なければ挿入）
                if (posCh.Samples[0].TimeMs <= 0f)
                    posCh.Samples[0] = new TimeChartController.HistorySample(0f, initPos);
                else
                    posCh.Samples.Insert(0, new TimeChartController.HistorySample(0f, initPos));

                // 最終時点：全サイクル終了時点（cycles * cycleTimeMs）の位置を追加
                // 直前サンプルの値をそのまま延長（インデックス末尾の pos 値に依存しない）
                float totalMs = cycles * cycleTimeMs;
                float lastSampleMs = posCh.Samples[posCh.Samples.Count - 1].TimeMs;
                if (lastSampleMs < totalMs)
                {
                    float lastVal = posCh.Samples[posCh.Samples.Count - 1].Value;
                    posCh.Samples.Add(new TimeChartController.HistorySample(totalMs, lastVal));
                }
            }

            // ---- 結果マージ（位置チャンネルを先頭に追加）----
            var result = new List<TimeChartController.HistoryChannel>();
            foreach (var ch in posChannels.Values) result.Add(ch);
            result.AddRange(dict.Values);
            return result;
        }

        /// <summary>
        /// positions[posIdx] の1つ前のポジション値を返す。
        /// 1つ前がない場合は前サイクルの最後のポジション値を返す。
        /// </summary>
        private static float GetPrevPosValue(
            List<Parameters.TimeChartDevice.Position> positions,
            int posIdx, int cycle, float cycleTimeMs, int totalCycles)
        {
            if (posIdx > 0)
                return positions[posIdx - 1].pos;

            // 先頭なら前サイクルの最後のpos値
            return positions[positions.Count - 1].pos;
        }

        /// <summary>
        /// AS OFF タイミング：対となる指令（異なるpos値）がONするタイミングを返す。
        /// </summary>
        private static float GetOppositeTagInOnTime(
            List<Parameters.TimeChartDevice.Position> positions,
            string posKey, int posIdx, int cycle, float cycleTimeMs, int totalCycles)
        {
            // 異なるpos値を持つpositionのインデックスリスト
            var otherIndices = new List<int>();
            for (int i = 0; i < positions.Count; i++)
                if (positions[i].pos.ToString("F4") != posKey)
                    otherIndices.Add(i);

            if (otherIndices.Count == 0)
                return GetTagOutOffTimeByKey(positions, posKey, posIdx, cycle, cycleTimeMs, totalCycles);

            // 現在のAS ON時刻（= start + time）の直後に来る反対指令ON時刻を探す
            float tagOutOnMs = cycle * cycleTimeMs + positions[posIdx].start + positions[posIdx].time;

            // 同サイクル内で直後の反対指令ON時刻
            float best = float.MaxValue;
            foreach (var idx in otherIndices)
            {
                float t = cycle * cycleTimeMs + positions[idx].start;
                if (t > tagOutOnMs && t < best)
                    best = t;
            }
            if (best < float.MaxValue) return best;

            // 次サイクルの最初の反対指令ON
            int nextCycle = (cycle + 1) % totalCycles;
            return nextCycle * cycleTimeMs + positions[otherIndices[0]].start;
        }

        /// <summary>
        /// 同じposキー（終了位置）を持つ1つ前のpositionの tagIn ON タイムスタンプを返す。
        /// </summary>
        private static float GetTagOutOffTimeByKey(
            List<Parameters.TimeChartDevice.Position> positions,
            string posKey, int posIdx, int cycle, float cycleTimeMs, int totalCycles)
        {
            // 同じ pos キーを持つ positions のインデックスリスト
            var sameKeyIndices = new List<int>();
            for (int i = 0; i < positions.Count; i++)
                if (positions[i].pos.ToString("F4") == posKey)
                    sameKeyIndices.Add(i);

            int myRank = sameKeyIndices.IndexOf(posIdx);
            if (myRank > 0)
            {
                int prevIdx = sameKeyIndices[myRank - 1];
                return cycle * cycleTimeMs + positions[prevIdx].start;
            }
            else
            {
                int lastIdx = sameKeyIndices[sameKeyIndices.Count - 1];
                if (cycle > 0)
                    return (cycle - 1) * cycleTimeMs + positions[lastIdx].start;
                else
                    return (totalCycles - 1) * cycleTimeMs + positions[lastIdx].start;
            }
        }

        /// <summary>
        /// positions[posIdx] の tagOut の OFF タイムスタンプを返す。
        /// = 同じtagOutを持つ1つ前の tagIn ON のタイミング
        /// （1つ前がなければ最後サイクルの最後の tagIn ON）
        /// </summary>
        private static float GetTagOutOffTime(
            List<Parameters.TimeChartDevice.Position> positions,
            int posIdx, int cycle, float cycleTimeMs, int totalCycles)
        {
            string targetTagOut = positions[posIdx].tagOut;
            var sameIndices = new List<int>();
            for (int i = 0; i < positions.Count; i++)
                if (positions[i].tagOut == targetTagOut)
                    sameIndices.Add(i);

            if (sameIndices.Count == 0)
                return cycle * cycleTimeMs + positions[posIdx].start;

            int myRank = sameIndices.IndexOf(posIdx);
            if (myRank > 0)
            {
                int prevIdx = sameIndices[myRank - 1];
                return cycle * cycleTimeMs + positions[prevIdx].start;
            }
            else
            {
                int lastIdx = sameIndices[sameIndices.Count - 1];
                if (cycle > 0)
                    return (cycle - 1) * cycleTimeMs + positions[lastIdx].start;
                else
                    return (totalCycles - 1) * cycleTimeMs + positions[lastIdx].start;
            }
        }
    }
}