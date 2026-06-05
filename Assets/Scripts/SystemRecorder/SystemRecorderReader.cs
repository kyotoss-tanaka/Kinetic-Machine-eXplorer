using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SystemRecorderReader;

public static class SysRecReader
{
    /// <summary>
    /// デバイスデータ
    /// </summary>
    public class DeviceData
    {
        /// <summary>
        /// デバイス名
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// デバイス番号
        /// </summary>
        public int No { get; set; }
        /// <summary>
        /// ビット番号
        /// </summary>
        public int BitNo { get; set; }
        /// <summary>
        /// ビットデバイス番号
        /// </summary>
        public int BitDevNo { get; set; }
        /// <summary>
        /// データオフセット
        /// </summary>
        public uint DataOffset { get; set; }
        /// <summary>
        /// 16進データ
        /// </summary>
        public bool IsHex { get; set; }
        /// <summary>
        /// ビットデータ
        /// </summary>
        public bool IsBit { get; set; }
        /// <summary>
        /// 値
        /// </summary>
        public UInt16 Value { get; set; }
        /// <summary>
        /// ビット値
        /// </summary>
        public bool isValue
        {
            get
            {
                return Value != 0;
            }
        }
        public string Key
        {
            get
            {
                var no = IsBit ? (IsHex ? BitDevNo.ToString("X") : BitDevNo.ToString()) : No.ToString();
                return Name + no;
            }
        }
    }

    /// <summary>
    /// 保存データ
    /// </summary>
    public class RecordData
    {
        public string Name { get; set; } = "";
        public int No { get; set; }
        public int BitNo { get; set; }
        public int BitDevNo { get; set; }
        public uint DataOffset { get; set; }
        public bool IsHex { get; set; }
        public bool IsBit { get; set; }
        public List<RecordValue> Record { get; set; } = new();
        public RecordData? Next { get; set; }
        public TagInfo? tagInfo;
    }

    /// <summary>
    /// レコード時間
    /// </summary>
    public class RecordTime
    {
        public DateTime Timestamp { get; set; } = new();
        public ulong SamplingCounter { get; set; }
        public ulong ScanCounter { get; set; }
    }

    /// <summary>
    /// レコードバリュー
    /// </summary>
    public class RecordValue : RecordTime
    {
        public TimeSpan Laps { get; set; } = new TimeSpan();
        public uint Value { get; set; }
    }

    /// <summary>
    /// オフセットデータ
    /// </summary>
    public class OffsetData
    {
        public string Key { get; set; } = "";
        public bool IsBit { get; set; }
        public bool IsHex { get; set; }
        public string Name { get; set; } = "";
        public int No { get; set; }
    }

    /// <summary>
    /// データ保存エリア
    /// </summary>
    public class DataArea
    {
        public string Name { get; set; } = "";
        public uint DevNo { get; set; }
        public uint DevStart { get; set; }
        public uint DevCount { get; set; }
        public uint Start { get; set; }
        public uint End { get; set; }
        public bool isHex { get; set; }
    }

    public static Dictionary<string, RecordData> recordDatas = new();
    public static Dictionary<uint, OffsetData> offsetDatas = new();

    public static RecordTime dtStart = new();
    public static RecordTime dtEnd = new();

    /// <summary>
    /// レコーダーデータ受信
    /// </summary>
    /// <returns></returns>
    public static bool ReadRecoderData(string path)
    {
        var ret = false;
        if (Directory.Exists(path))
        {
            // 設定ファイル
            var rsi = Directory.GetFiles(path, "*.rsi", SearchOption.AllDirectories);
            // 収集設定
            var mri = Directory.GetFiles(path, "dev.mri", SearchOption.AllDirectories);
            // 基準ファイル
            var mrb = Directory.GetFiles(path, "*.mrb", SearchOption.AllDirectories);
            // 差分ファイル
            var mrd = Directory.GetFiles(path, "*.mrd", SearchOption.AllDirectories);
            if ((rsi.Length > 0) && (mri.Length > 0) && (mrb.Length > 0) && (mrd.Length > 0))
            {
                // データ存在
                var setting = RecordingSettingFile.Parse(File.ReadAllBytes(rsi[0]));
                var collection = CollectionDevInfo.Parse(File.ReadAllBytes(mri[0]));
                var baseData = BaseDeviceLogFile.Parse(File.ReadAllBytes(mrb[0]));
                var diffData = DiffDeviceLogFile.Parse(File.ReadAllBytes(mrd[0]), collection.DiffCompareUnit);
                dtStart = new RecordTime
                {
                    Timestamp = diffData.diffRecords.Last().Timestamp.DateTime,
                    SamplingCounter = diffData.diffRecords.Last().SamplingCounter,
                    ScanCounter = diffData.diffRecords.Last().ScanCounter
                };
                dtEnd = new RecordTime
                {
                    Timestamp = baseData.Timestamp.DateTime,
                    SamplingCounter = baseData.SamplingCounter,
                    ScanCounter = baseData.ScanCounter
                };
                for (var i = 1; i < mrd.Length; i++)
                {
                    var diff = DiffDeviceLogFile.Parse(File.ReadAllBytes(mrd[i]), collection.DiffCompareUnit);
                    diffData.diffRecords.AddRange(diff.diffRecords);
                    diffData.DiffRecordCount = (uint)diffData.diffRecords.Count;
                }
                var deviceDates = new Dictionary<string, DeviceData>();
                // 基準データ作成
                var offset = 0;
                foreach (var device in setting.SettingInfo.DeviceCodeBlock.WordCollectDeviceCodeBlock!.DeviceCodes)
                {
                    var no = device.OffsetDec;
                    var bitDevNo = device.OffsetDec / 16;
                    for (var i = 0; i < device.PointCount; i++)
                    {
                        var value = BitConverter.ToUInt16(baseData.BaseData, offset);
                        if (device.IsBit)
                        {
                            // ビット分解
                            for (var j = 0; j < 16; j++)
                            {
                                var dev = new DeviceData
                                {
                                    Name = device.DeviceName,
                                    No = no,
                                    BitDevNo = bitDevNo,
                                    BitNo = j,
                                    DataOffset = (ushort)offset,
                                    IsHex = device.IsHex,
                                    IsBit = true,
                                    Value = (value & (1 << j)) != 0 ? (ushort)1 : (ushort)0
                                };
                                deviceDates.Add(dev.Key, dev);
                                bitDevNo++;
                            }
                        }
                        else
                        {
                            // ワードデータ
                            var dev = new DeviceData
                            {
                                Name = device.DeviceName,
                                No = no,
                                DataOffset = (ushort)offset,
                                IsHex = device.IsHex,
                                IsBit = false,
                                Value = value
                            };
                            deviceDates.Add(dev.Key, dev);
                        }
                        no++;
                        offset += 2;
                    }
                }
                // 必要データのタイムチャート取得(X0-X4095, Y0-Y4095, D12000-D12255)
                recordDatas = new();
                offsetDatas = new();
                var dataAreas = new List<DataArea>();

                // データエリア作成
                void CreateDataArea(string name, uint start, uint count)
                {
                    var isHex = name == "X" || name == "Y";
                    if (name == "T")
                    {
                        // TCとTNを作成
                        var devStart = deviceDates["TN" + start];
                        var devEnd = deviceDates["TN" + (start + count)];
                        dataAreas.Add(new DataArea
                        {
                            Name = "TN",
                            DevStart = (uint)start,
                            DevCount = count,
                            DevNo = 0,
                            Start = devStart!.DataOffset,
                            End = devEnd!.DataOffset
                        });
                        devStart = deviceDates["TC" + start * 8];
                        devEnd = deviceDates["TC" + (start + count) * 8];
                        dataAreas.Add(new DataArea
                        {
                            Name = "TC",
                            DevStart = (uint)start,
                            DevCount = count * 8,
                            DevNo = 0,
                            Start = devStart!.DataOffset,
                            End = devEnd!.DataOffset
                        });
                    }
                    else
                    {
                        var prm = isHex ? "X" : "";
                        var startName = name + start.ToString(prm);
                        var endName = name + (start + count - 1).ToString(prm);
                        if ((deviceDates.ContainsKey(startName)) && (deviceDates.ContainsKey(endName)))
                        {
                            var devStart = deviceDates[startName];
                            var devEnd = deviceDates[endName];
                            dataAreas.Add(new DataArea
                            {
                                Name = name,
                                DevStart = (uint)start,
                                DevCount = count,
                                DevNo = 0,
                                Start = devStart!.DataOffset,
                                End = devEnd!.DataOffset,
                                isHex = isHex
                            });
                        }
                    }
                }

                // 使用デバイスリスト作成
                foreach (var mechData in GlobalScript.useDeviceDatas)
                {
                    foreach (var area in mechData.devices)
                    {
                        CreateDataArea(area.name, (uint)area.no, (uint)area.size);
                    }
                }

                // オフセットデータ作成
                var allOffset = new Dictionary<uint, OffsetData>();
                foreach (var data in deviceDates.Values)
                {
                    if (!allOffset.ContainsKey(data.DataOffset))
                    {
                        allOffset.Add(data.DataOffset, new OffsetData
                        {
                            Key = data.Key,
                            Name = data.Name,
                            No = data.IsBit ? data.BitDevNo : data.No,
                            IsBit = data.IsBit,
                            IsHex = data.IsHex
                        });
                    }
                }

                foreach (var dataArea in dataAreas)
                {
                    RecordData? prev = null;
                    for (var i = 0; i < dataArea.DevCount; i++)
                    {
                        var prm = dataArea.isHex ? "X" : "";
                        var name = dataArea.Name + (i + dataArea.DevStart).ToString(prm);
                        var data = deviceDates[name];
                        if (data != null)
                        {
                            // オフセット作成
                            if (allOffset.ContainsKey(data.DataOffset) && !offsetDatas.ContainsKey(data.DataOffset))
                            {
                                offsetDatas.Add(data.DataOffset, allOffset[data.DataOffset]);
                                // DiffCompareUnit単位のデータもセット
                                var no = data.IsBit ? (int)(data.BitDevNo / (16 * collection.DiffCompareUnit)) * (16 * collection.DiffCompareUnit) : (int)(data.No / (collection.DiffCompareUnit / 2)) * (collection.DiffCompareUnit / 2);
                                var data2 = deviceDates.Where(d => (data.Name == d.Value.Name)  && (data.IsBit == d.Value.IsBit) && (data.IsBit ? 
                                    no == d.Value.BitDevNo : 
                                    no == d.Value.No)).ToList();
                                if (data2.Count > 0)
                                {
                                    if (allOffset.ContainsKey(data2[0].Value.DataOffset) && !offsetDatas.ContainsKey(data2[0].Value.DataOffset))
                                    {
                                        offsetDatas.Add(data2[0].Value.DataOffset, allOffset[data2[0].Value.DataOffset]);
                                    }
                                }
                            }
                            var record = new RecordData
                            {
                                Name = name,
                                BitDevNo = data.BitDevNo,
                                BitNo = data.BitNo,
                                No = data.No,
                                DataOffset = data.DataOffset,
                                IsBit = data.IsBit,
                                IsHex = data.IsHex,
                            };
                            recordDatas.Add(data.Key, record);
                            record.Record.Add(new RecordValue
                            {
                                ScanCounter = baseData.ScanCounter,
                                SamplingCounter = baseData.SamplingCounter,
                                Timestamp = baseData.Timestamp.DateTime,
                                Value = data.Value,
                            });
                            if (prev != null)
                            {
                                prev.Next = record;
                            }
                            prev = record;
                        }
                    }
                }

                // 差分データ
                foreach (var diff in diffData.diffRecords)
                {
                    foreach (var block in diff.DiffBlocks)
                    {
                        if (offsetDatas.ContainsKey(block.DeviceOffset))
                        {
                            var data = offsetDatas[block.DeviceOffset];
                            if (data.IsBit)
                            {
                                var prm = data.IsHex ? "X" : "";
                                for (var i = 0; i < collection.DiffCompareUnit / 2; i++)
                                {
                                    var value = block.Values[i];
                                    for (var j = 0; j < 16; j++)
                                    {
                                        var name = data.Name + (data.No + i * 16 + j).ToString(prm);
                                        if (recordDatas.ContainsKey(name))
                                        {
                                            var last = recordDatas[name].Record.Last();
                                            if (last != null)
                                            {
                                                var bit = (value & (1 << j)) != 0 ? (ushort)1 : (ushort)0;
                                                if (last.Value != bit)
                                                {
                                                    // 値の変更あり
                                                    recordDatas[name].Record.Add(new RecordValue
                                                    {
                                                        SamplingCounter = diff.SamplingCounter,
                                                        ScanCounter = diff.ScanCounter,
                                                        Timestamp = diff.Timestamp.DateTime,
                                                        Value = bit
                                                    });
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                for (var i = 0; i < collection.DiffCompareUnit / 2; i++)
                                {
                                    var name = data.Name + (data.No + i).ToString();
                                    if (recordDatas.ContainsKey(name))
                                    {
                                        var value = block.Values[i];
                                        var last = recordDatas[name].Record.Last();
                                        if (last.Value != value)
                                        {
                                            // 値の変更あり
                                            recordDatas[name].Record.Add(new RecordValue
                                            {
                                                SamplingCounter = diff.SamplingCounter,
                                                ScanCounter = diff.ScanCounter,
                                                Timestamp = diff.Timestamp.DateTime,
                                                Value = value
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                ret = true;
            }
        }
        return ret;
    }
}