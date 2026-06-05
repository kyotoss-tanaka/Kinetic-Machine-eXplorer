using System;
using System.Collections.Generic;
using UnityEngine;

namespace KyotoSS.TimingChart
{
    /// <summary>
    /// SysRecReader.recordDatas から HistoryChannel リストを生成する。
    ///
    /// データ構造：
    ///   Record[0]     = 基準データ（最新時刻 = dtEnd）
    ///   Record[1以降] = 差分データ（古い順、dtStart が最古）
    ///
    /// タイムチャートは dtStart を 0ms の起点として表示する。
    /// </summary>
    public static class SysRecHistoryBuilder
    {
        /// <summary>
        /// timeChartDatas の構成定義を使い、recordDatas から HistoryChannel を生成する。
        /// recordDatas にデータがない tagIn/tagOut は HistoryDataBuilder でフォールバック。
        /// </summary>
        public static List<TimeChartController.HistoryChannel> BuildFromTimeChartDatas(
            List<Parameters.TimeChartData> timeChartDatas, int cycles = 2)
        {
            bool hasSysRec = SysRecReader.recordDatas != null
                          && SysRecReader.recordDatas.Count > 0;

            // recordDatas がない場合はユニット（アナログ位置チャンネル）のみ返す
            // IO・波形データは表示しない
            if (!hasSysRec)
                return BuildEmptyUnitChannels(timeChartDatas);

            DateTime origin = SysRecReader.dtStart.Timestamp;
            var result = new List<TimeChartController.HistoryChannel>();
            // 重複追加防止（チャンネル名をキーに管理）
            var built = new HashSet<string>();

            foreach (var tmData in timeChartDatas)
            {
                foreach (var dev in tmData.datas)
                {
                    if (dev.positions == null) continue;

                    // External デバイスは devIn を recordDatas キーとしてアナログチャンネルを生成
                    if (dev.devType == Parameters.TimeChartDevice.DeviceType.External)
                    {
                        if (string.IsNullOrEmpty(dev.name)) continue;
                        if (built.Contains(dev.name)) continue;

                        // 全 position の devIn からアナログ値を読む（最初に見つかったもの）
                        string extDevKey = "";
                        string extDevLabel = "";
                        foreach (var pos in dev.positions)
                        {
                            if (!string.IsNullOrEmpty(pos.devIn))
                            {
                                extDevKey = pos.devIn;
                                extDevLabel = pos.devIn;
                                break;
                            }
                        }
                        if (string.IsNullOrEmpty(extDevKey)) continue;
                        if (!SysRecReader.recordDatas.ContainsKey(extDevKey)) continue;

                        // sizeIn=2 の場合は extDevKey と次アドレスを組み合わせて32bit値に
                        int extSize = dev.positions.Count > 0 ? dev.positions[0].sizeIn : 1;
                        TimeChartController.HistoryChannel extCh;
                        if (extSize >= 2)
                            extCh = BuildChannelFromSysRec32(dev.name, extDevKey, extDevLabel, origin);
                        else
                            extCh = BuildChannelFromSysRec(dev.name, extDevKey, extDevLabel, origin);

                        if (extCh != null)
                        {
                            // 実際のサンプル値の最小・最大でAnalogMin/Maxを設定
                            float minVal = float.MaxValue, maxVal = float.MinValue;
                            foreach (var s in extCh.Samples)
                            {
                                if (s.Value < minVal) minVal = s.Value;
                                if (s.Value > maxVal) maxVal = s.Value;
                            }
                            if (minVal >= float.MaxValue) { minVal = 0f; maxVal = 1f; }
                            if (Mathf.Approximately(minVal, maxVal))
                            { minVal -= 1f; maxVal += 1f; }

                            extCh.IsAnalog = true;
                            extCh.AnalogMin = minVal;
                            extCh.AnalogMax = maxVal;
                            built.Add(dev.name);
                            result.Add(extCh);
                        }
                        continue;
                    }

                    // pos値ごとに同一IOをまとめる（同じpos値は同一IO）
                    var posIoMap = new Dictionary<string, (string tagIn, string tagOut, string devIn, string devOut)>();
                    foreach (var pos in dev.positions)
                    {
                        string key = pos.pos.ToString("F4");
                        if (!posIoMap.ContainsKey(key))
                        {
                            string ti = string.IsNullOrEmpty(pos.tagIn) ? "" : pos.tagIn;
                            string to = string.IsNullOrEmpty(pos.tagOut) ? "" : pos.tagOut;
                            if (!string.IsNullOrEmpty(ti) || !string.IsNullOrEmpty(to))
                                posIoMap[key] = (ti, to, pos.devIn, pos.devOut);
                        }
                    }

                    // 位置チャンネル（アナログ）をSysRecデータから生成
                    // 指令IOのON → 移動開始（前のpos値から）
                    // AS IOのON  → 到達（このpos値に）
                    var posCh = BuildPositionChannelFromSysRec(dev, posIoMap, origin);
                    if (posCh != null && !built.Contains(posCh.Name))
                    {
                        built.Add(posCh.Name);
                        result.Add(posCh);
                    }

                    foreach (var kv in posIoMap)
                    {
                        var (tagIn, tagOut, devIn, devOut) = kv.Value;

                        // tagIn（指令IO）
                        if (!string.IsNullOrEmpty(tagIn))
                        {
                            string chName = $"{dev.name}/{tagIn}";
                            string devKey = !string.IsNullOrEmpty(devIn) ? devIn : tagIn;
                            if (!built.Contains(chName))
                            {
                                built.Add(chName);
                                var ch = BuildChannelFromSysRec(chName, devKey, devIn, origin);
                                if (ch != null) result.Add(ch);
                            }
                        }

                        // tagOut（AS IO）- 自動生成名はスキップ
                        if (!string.IsNullOrEmpty(tagOut) && !tagOut.Contains("_Pos"))
                        {
                            string chName = $"{dev.name}/{tagOut}";
                            string devKey = !string.IsNullOrEmpty(devOut) ? devOut : tagOut;
                            if (!built.Contains(chName))
                            {
                                built.Add(chName);
                                var ch = BuildChannelFromSysRec(chName, devKey, devOut, origin);
                                if (ch != null) result.Add(ch);
                            }
                        }
                    }
                }
            }

            // recordDatas にないチャンネルは HistoryDataBuilder でフォールバック
            var fallback = HistoryDataBuilder.Build(timeChartDatas, cycles);
            foreach (var fbCh in fallback)
            {
                if (!built.Contains(fbCh.Name))
                    result.Add(fbCh);
            }

            return result;
        }


        /// <summary>
        /// SysRecデータがない場合にユニット（アナログ位置チャンネル）のみ返す。
        /// サンプルは空なので波形は表示されない。
        /// </summary>
        private static List<TimeChartController.HistoryChannel> BuildEmptyUnitChannels(
            List<Parameters.TimeChartData> timeChartDatas)
        {
            var result = new List<TimeChartController.HistoryChannel>();
            var built = new HashSet<string>();

            foreach (var tmData in timeChartDatas)
            {
                foreach (var dev in tmData.datas)
                {
                    if (dev.positions == null) continue;
                    if (string.IsNullOrEmpty(dev.name)) continue;
                    if (built.Contains(dev.name)) continue;

                    // pos値の最大・最小を取得
                    float minP = float.MaxValue, maxP = float.MinValue;
                    foreach (var p in dev.positions)
                    {
                        if (p.pos < minP) minP = p.pos;
                        if (p.pos > maxP) maxP = p.pos;
                    }
                    if (minP >= float.MaxValue) { minP = 0f; maxP = 1f; }

                    // Externalはdevラベルを設定
                    string devLabel = "";
                    if (dev.devType == Parameters.TimeChartDevice.DeviceType.External
                        && dev.positions.Count > 0)
                        devLabel = dev.positions[0].devIn ?? "";

                    built.Add(dev.name);
                    result.Add(new TimeChartController.HistoryChannel
                    {
                        Name = dev.name,
                        IsAnalog = true,
                        AnalogMin = minP,
                        AnalogMax = maxP,
                        HasInitialValue = true,
                        DeviceName = devLabel,
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// SysRecデータから位置チャンネル（アナログ）を生成する。
        /// 指令IO（tagIn）のON時刻 → AS IO（tagOut）のON時刻 で移動を表現する。
        /// </summary>
        private static TimeChartController.HistoryChannel BuildPositionChannelFromSysRec(
            Parameters.TimeChartDevice dev,
            Dictionary<string, (string tagIn, string tagOut, string devIn, string devOut)> posIoMap,
            DateTime origin)
        {
            if (dev.positions == null || dev.positions.Count == 0) return null;

            float minPos = float.MaxValue, maxPos = float.MinValue;
            foreach (var pos in dev.positions)
            {
                if (pos.pos < minPos) minPos = pos.pos;
                if (pos.pos > maxPos) maxPos = pos.pos;
            }
            if (Mathf.Approximately(minPos, maxPos)) return null;

            var ch = new TimeChartController.HistoryChannel
            {
                Name = dev.name,
                IsAnalog = true,
                AnalogMin = minPos,
                AnalogMax = maxPos,
                HasInitialValue = true,
            };

            // pos値ごとに (posVal, cmdDevKey, asDevKey) を収集
            var posEntries = new List<(float posVal, string cmdDevKey, string asDevKey)>();
            foreach (var pos in dev.positions)
            {
                string key = pos.pos.ToString("F4");
                if (!posIoMap.TryGetValue(key, out var io)) continue;
                var (tagIn, tagOut, devIn, devOut) = io;
                string cmdKey = !string.IsNullOrEmpty(devIn) ? devIn : tagIn;
                string asKey = !string.IsNullOrEmpty(devOut) ? devOut : tagOut;
                if (posEntries.Exists(e => Mathf.Approximately(e.posVal, pos.pos))) continue;
                posEntries.Add((pos.pos, cmdKey, asKey));
            }

            if (posEntries.Count == 0) return null;

            // 全イベントを時系列で収集
            // (timeMs, posVal, isCmd) : isCmd=指令ON、!isCmd=AS ON（到達）
            var allEvents = new List<(float timeMs, float posVal, bool isCmd)>();

            foreach (var (posVal, cmdDevKey, asDevKey) in posEntries)
            {
                foreach (var t in GetOnTimes(cmdDevKey, origin))
                    allEvents.Add((t, posVal, true));
                foreach (var t in GetOnTimes(asDevKey, origin))
                    allEvents.Add((t, posVal, false));
            }

            if (allEvents.Count == 0) return null;
            allEvents.Sort((a, b) => a.timeMs.CompareTo(b.timeMs));

            // 最初の指令ONを探す
            int firstCmdIdx = allEvents.FindIndex(e => e.isCmd);
            if (firstCmdIdx < 0) return null;

            float firstCmdMs = allEvents[firstCmdIdx].timeMs;

            // ----------------------------------------------------------------
            // 初期位置の決定：優先順位
            //   1. 記録開始時にONのASを生データで直接確認（最も確実）
            //      sorted[0].Value==1 → 0ms からONだったことを示す
            //   2. 最初の指令ONより前にAS ONイベントがあればそのpos値
            //   3. 最初の指令の対向pos値（fallback）
            // ----------------------------------------------------------------
            float initPos = -1f;

            // 優先1: 生データからAS初期状態を確認
            // Record[0]=最新値(dtEnd)、Record[1以降]=差分(新→古順) のため
            // Timestamp 昇順ソート後に GetForwardValue(sorted, origin) で
            // origin 時刻における正確な値を取得する
            foreach (var (posVal, _, asDevKey) in posEntries)
            {
                if (string.IsNullOrEmpty(asDevKey)) continue;
                if (!SysRecReader.recordDatas.TryGetValue(asDevKey, out var asData)) continue;
                if (asData.Record == null || asData.Record.Count == 0) continue;
                var sorted0 = new List<SysRecReader.RecordValue>(asData.Record);
                sorted0.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
                float valAtOrigin = GetForwardValue(sorted0, origin);
                Debug.Log($"[PosInit] {dev.name}  AS={asDevKey}  posVal={posVal}" +
                          $"  valAtOrigin={valAtOrigin}" +
                          $"  sorted0[0].Value={sorted0[0].Value}" +
                          $"  sorted0[0].Ts={sorted0[0].Timestamp:HH:mm:ss.fff}" +
                          $"  origin={origin:HH:mm:ss.fff}");
                if (valAtOrigin == 1f) { initPos = posVal; break; }
            }
            Debug.Log($"[PosInit] {dev.name}  initPos={initPos}  (minPos={minPos} maxPos={maxPos})");

            // 優先2: 最初の指令ONより前のAS ONイベント
            if (initPos < 0f)
                for (int i = firstCmdIdx - 1; i >= 0; i--)
                    if (!allEvents[i].isCmd) { initPos = allEvents[i].posVal; break; }

            // 優先3: 最初の指令の対向pos値（fallback）
            if (initPos < 0f)
                foreach (var (pv, _, __) in posEntries)
                    if (!Mathf.Approximately(pv, allEvents[firstCmdIdx].posVal))
                    { initPos = pv; break; }

            if (initPos < 0f) initPos = posEntries[0].posVal;

            // 最初の指令ONまでは待機（初期値）
            ch.Samples.Add(new TimeChartController.HistorySample(0f, initPos));
            float currentPos = initPos;
            bool moving = false;
            float movingToPos = initPos;

            foreach (var (timeMs, posVal, isCmd) in allEvents)
            {
                if (timeMs < firstCmdMs) continue;

                if (isCmd)
                {
                    // 指令ON：移動開始 → 指令ON時刻に現在値を置く（線形移動の始点）
                    float lastMs = ch.Samples[ch.Samples.Count - 1].TimeMs;
                    if (timeMs > lastMs + 0.05f)
                        ch.Samples.Add(new TimeChartController.HistorySample(timeMs, currentPos));
                    moving = true;
                    movingToPos = posVal;
                }
                else
                {
                    // AS ON：到達 → AS ON時刻に到達値を置く（線形移動の終点）
                    if (moving && Mathf.Approximately(posVal, movingToPos))
                    {
                        ch.Samples.Add(new TimeChartController.HistorySample(timeMs, posVal));
                        currentPos = posVal;
                        moving = false;
                    }
                }
            }

            // 最終サンプルを記録終端時刻に追加（波形を最後まで延長）
            float totalMs = (float)(SysRecReader.dtEnd.Timestamp - origin).TotalMilliseconds;
            if (ch.Samples.Count > 0 && totalMs > ch.Samples[ch.Samples.Count - 1].TimeMs + 0.1f)
                ch.Samples.Add(new TimeChartController.HistorySample(totalMs, currentPos));

            return ch.Samples.Count > 1 ? ch : null;
        }

        /// <summary>
        /// 指定デバイスの「ON開始時刻（ms）」リストを返す。
        /// レコードが「次のタイムスタンプまで有効な値」形式のため
        /// sorted[i].Value=1 → sorted[i-1].Timestamp が ON 開始時刻。
        /// </summary>
        private static List<float> GetOnTimes(string deviceKey, DateTime origin)
        {
            var result = new List<float>();
            if (string.IsNullOrEmpty(deviceKey)) return result;
            if (!SysRecReader.recordDatas.TryGetValue(deviceKey, out var recordData)) return result;
            if (recordData.Record == null || recordData.Record.Count == 0) return result;

            var sorted = new List<SysRecReader.RecordValue>(recordData.Record);
            sorted.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

            // 仕様：sorted[i].Value は sorted[i-1].Timestamp から sorted[i].Timestamp まで有効
            // sorted[i].Value==1 → sorted[i-1].Timestamp がON開始時刻
            // i==0 かつ val==1 は 0ms からON
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].Value == 1)
                {
                    float timeMs = i == 0
                        ? 0f
                        : (float)(sorted[i - 1].Timestamp - origin).TotalMilliseconds;
                    if (timeMs < 0f) timeMs = 0f;
                    result.Add(timeMs);
                }
            }
            return result;
        }

        /// <summary>
        /// sizeIn=2 の場合に devKey（下位16bit）と devKey+1（上位16bit）を組み合わせて
        /// 32bit値のアナログチャンネルを生成する。
        /// 例: D100（下位）+ D101（上位）→ value = (D101 << 16) | (D100 & 0xFFFF)
        /// </summary>
        private static TimeChartController.HistoryChannel BuildChannelFromSysRec32(
            string channelName, string deviceKey, string deviceLabel, DateTime origin)
        {
            // 次アドレスを生成（末尾の数字を+1）
            string hiKey = IncrementDeviceAddress(deviceKey);
            if (string.IsNullOrEmpty(hiKey)) return null;
            if (!SysRecReader.recordDatas.ContainsKey(deviceKey)) return null;
            if (!SysRecReader.recordDatas.ContainsKey(hiKey)) return null;

            // 両方のデータを取得してタイムスタンプをマージ
            var loData = SysRecReader.recordDatas[deviceKey];
            var hiData = SysRecReader.recordDatas[hiKey];

            var loSorted = new List<SysRecReader.RecordValue>(loData.Record);
            var hiSorted = new List<SysRecReader.RecordValue>(hiData.Record);
            loSorted.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            hiSorted.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

            // 全タイムスタンプを収集してマージ
            var allTimes = new SortedSet<DateTime>();
            foreach (var r in loSorted) allTimes.Add(r.Timestamp);
            foreach (var r in hiSorted) allTimes.Add(r.Timestamp);

            var ch = new TimeChartController.HistoryChannel
            {
                Name = channelName,
                IsAnalog = true,
                DeviceName = !string.IsNullOrEmpty(deviceLabel) ? deviceLabel : deviceKey,
                HasInitialValue = true,
            };

            // 各タイムスタンプで lo/hi を前方参照（次の変化点の値を先読み）で取得して32bit結合
            // lo/hi のタイムスタンプがずれている場合、未更新側は次の変化点の値を使う
            var allTimesList = new List<DateTime>(allTimes);

            foreach (var ts in allTimesList)
            {
                // lo: ts 以降で最初に有効な値（前方参照）
                float loVal = GetForwardValue(loSorted, ts);
                float hiVal = GetForwardValue(hiSorted, ts);

                float timeMs = (float)(ts - origin).TotalMilliseconds;
                if (timeMs < 0f) timeMs = 0f;

                // 上位16bit（hi）と下位16bit（lo）を結合
                int lo = (int)loVal & 0xFFFF;
                int hi = (int)hiVal & 0xFFFF;
                float combined = (float)((hi << 16) | lo);
                ch.Samples.Add(new TimeChartController.HistorySample(timeMs, combined));
            }

            // 記録終端時刻を追加
            float endMs = (float)(SysRecReader.dtEnd.Timestamp - origin).TotalMilliseconds;
            if (ch.Samples.Count > 0 && endMs > ch.Samples[ch.Samples.Count - 1].TimeMs + 0.1f)
                ch.Samples.Add(new TimeChartController.HistorySample(
                    endMs, ch.Samples[ch.Samples.Count - 1].Value));

            return ch.Samples.Count > 0 ? ch : null;
        }

        /// <summary>
        /// 指定時刻 ts に対して「ts 以降で最初に有効な値」を返す（前方参照）。
        /// sorted[i].Value は sorted[i-1].Timestamp から sorted[i].Timestamp まで有効な仕様。
        /// ts より後にデータがなければ最後の値を返す。
        /// </summary>
        private static float GetForwardValue(List<SysRecReader.RecordValue> sorted, DateTime ts)
        {
            if (sorted.Count == 0) return 0f;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].Timestamp >= ts)
                    return sorted[i].Value;
            }
            return sorted[sorted.Count - 1].Value;
        }

        /// <summary>
        /// デバイスアドレス末尾の数字を+1する（例: D100 → D101, W1A → W1B）
        /// </summary>
        private static string IncrementDeviceAddress(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            // 末尾の数字部分を取得
            int i = key.Length - 1;
            while (i >= 0 && char.IsDigit(key[i])) i--;
            if (i == key.Length - 1) return null; // 数字なし
            string prefix = key.Substring(0, i + 1);
            string numStr = key.Substring(i + 1);
            if (!int.TryParse(numStr, out int num)) return null;
            return prefix + (num + 1).ToString();
        }

        /// <summary>
        /// recordDatas からチャンネルを生成する。
        /// データがなければ null を返す（呼び出し側でフォールバック）。
        /// </summary>
        private static TimeChartController.HistoryChannel BuildChannelFromSysRec(
            string channelName, string deviceKey, string deviceLabel, DateTime origin)
        {
            if (string.IsNullOrEmpty(deviceKey)) return null;
            if (!SysRecReader.recordDatas.TryGetValue(deviceKey, out var recordData)) return null;
            if (recordData.Record == null || recordData.Record.Count == 0) return null;

            var ch = new TimeChartController.HistoryChannel
            {
                Name = channelName,
                IsAnalog = !recordData.IsBit,
                DeviceName = !string.IsNullOrEmpty(deviceLabel) ? deviceLabel : deviceKey,
                HasInitialValue = true,  // SysRecデータは既に正しい初期値を持つ
            };

            // Record[0] = 基準データ（最新時刻 = dtEnd）
            // Record[1以降] = 差分データ（変化点のみ記録、新しい→古い順）
            // Timestamp 昇順にソートして 古い→新しい の時系列にする
            var sorted = new List<SysRecReader.RecordValue>(recordData.Record);
            sorted.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

            // 仕様：sorted[i].Value は sorted[i-1].Timestamp から sorted[i].Timestamp まで有効
            // → sorted[i].Value を sorted[i-1].Timestamp に置く（1つ前にずらす）
            // → sorted[0].Value は 0ms に置く

            for (int i = 0; i < sorted.Count; i++)
            {
                float timeMs = i == 0
                    ? 0f
                    : (float)(sorted[i - 1].Timestamp - origin).TotalMilliseconds;
                if (timeMs < 0f) timeMs = 0f;

                float value = sorted[i].Value;

                // 直前と同値はスキップ
                if (ch.Samples.Count > 0 &&
                    Mathf.Approximately(ch.Samples[ch.Samples.Count - 1].Value, value))
                    continue;

                ch.Samples.Add(new TimeChartController.HistorySample(timeMs, value));
            }

            if (ch.Samples.Count == 0) return null;

            // 最終サンプルを記録終端時刻に追加（波形を最後まで延長）
            float endMs = (float)(SysRecReader.dtEnd.Timestamp - origin).TotalMilliseconds;
            if (endMs > ch.Samples[ch.Samples.Count - 1].TimeMs + 0.1f)
                ch.Samples.Add(new TimeChartController.HistorySample(
                    endMs, ch.Samples[ch.Samples.Count - 1].Value));

            return ch;
        }
    }
}