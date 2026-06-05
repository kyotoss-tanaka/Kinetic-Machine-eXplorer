using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    internal class DiffDeviceLogFile
    {
        // (1) ファイル情報共通ヘッダ（別途定義済み）
        public CommonHeader CommonHeader { get; set; } = new();

        // 差分デバイスログエリア
        public ushort DiffInfoNo { get; set; }
        public uint DiffRecordCount { get; set; }
        public List<DiffRecordInfo> diffRecords { get; set; } = new();

        public static DiffDeviceLogFile Parse(byte[] data, int diffSize)
        {
            var file = new DiffDeviceLogFile();

            // (1) 共通ヘッダ（0x2C バイト）
            file.CommonHeader = CommonHeader.Parse(data);
            int pos = 0x2C;

            // 差分デバイスログエリア
            file.DiffInfoNo = BitConverter.ToUInt16(data, pos + 0x00);
            file.DiffRecordCount = BitConverter.ToUInt32(data, pos + 0x02);
            var offset = pos + 0x06;
            for (var i = 0; i < file.DiffRecordCount; i++)
            {
                var diffRecord = DiffRecordInfo.Parse(data, offset, diffSize);
                file.diffRecords.Add(diffRecord);
                offset += (int)diffRecord.RecordSize;
            }
            return file;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine(CommonHeader.ToString());
            sb.AppendLine("****************************************");
            sb.AppendLine($"差分情報ファイル番号      : {DiffInfoNo}");
            sb.AppendLine($"本ファイルの差分レコード数: {DiffRecordCount}");
            return sb.ToString();
        }
    }

    internal class DiffRecordInfo
    {
        public uint RecordSize { get; set; }
        public ulong SamplingCounter { get; set; }
        public ulong ScanCounter { get; set; }
        public ulong TimerCounter { get; set; }
        public TimeStamp Timestamp { get; set; } = new();
        public uint DiffBlockCount { get; set; }
        public List<DiffBlock> DiffBlocks { get; set; } = new();

        public static DiffRecordInfo Parse(byte[] data, int offset, int diffSize)
        {
            var record = new DiffRecordInfo();

            // 差分デバイスログエリア
            record.RecordSize = BitConverter.ToUInt32(data, offset + 0x00);
            record.SamplingCounter = BitConverter.ToUInt64(data, offset + 0x04);
            record.ScanCounter = BitConverter.ToUInt64(data, offset + 0x14);
            record.Timestamp = TimeStamp.Parse(data, offset + 0x1C);
            record.DiffBlockCount = BitConverter.ToUInt32(data, offset + 0x28);
            var pos = offset + 0x2C;
            for (var i = 0; i < record.DiffBlockCount; i++)
            {
                var diffBlock = DiffBlock.Parse(data, pos, diffSize);
                record.DiffBlocks.Add(diffBlock);
                pos += diffSize + 4;
            }
            return record;
        }
    }

    public class DiffBlock
    {
        public uint DeviceOffset { get; set; }
        public List<ushort> Values { get; set; } = new();

        public static DiffBlock Parse(byte[] data, int offset, int diffSize)
        {
            var record = new DiffBlock();
            record.DeviceOffset = BitConverter.ToUInt32(data, offset + 0x00);
            for (var i = 0; i < diffSize; i += 2)
            {
                record.Values.Add(BitConverter.ToUInt16(data, offset + 0x04 + i));
            }
            return record;
        }
    }
}
