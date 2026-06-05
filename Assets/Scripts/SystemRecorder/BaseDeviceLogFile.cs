using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // タイムスタンプ構造体
    internal class BaseDeviceLogFile
    {
        // (1) ファイル情報共通ヘッダ（別途定義済み）
        public CommonHeader CommonHeader { get; set; } = new();

        // 基準デバイスログエリア
        public ulong SamplingCounter { get; set; }  // (2) サンプリングカウンタ値 8バイト +00h
        public ulong ScanCounter { get; set; }  // (3) スキャンカウンタ値     8バイト +08h
        public ulong TimerCounter { get; set; }  // (4) タイマカウンタ値       8バイト +10h
        public TimeStamp Timestamp { get; set; } = new();  // (5) タイムスタンプ        12バイト +18h
        public uint BaseDataSize { get; set; }  // (6) 基準データサイズ       4バイト +24h
        public byte[] BaseData { get; set; }  // (7) 基準データ            可変     +28h

        public static BaseDeviceLogFile Parse(byte[] data)
        {
            var file = new BaseDeviceLogFile();

            // (1) 共通ヘッダ（0x2C バイト）
            file.CommonHeader = CommonHeader.Parse(data);
            int pos = 0x2C;

            // 基準デバイスログエリア
            file.SamplingCounter = BitConverter.ToUInt64(data, pos + 0x00);  // +00h
            file.ScanCounter = BitConverter.ToUInt64(data, pos + 0x08);  // +08h
            file.TimerCounter = BitConverter.ToUInt64(data, pos + 0x10);  // +10h
            file.Timestamp = TimeStamp.Parse(data, pos + 0x18);  // +18h 12バイト
            file.BaseDataSize = BitConverter.ToUInt32(data, pos + 0x24);  // +24h

            // (7) 基準データ (+28h〜)
            int dataOffset = pos + 0x28;
            file.BaseData = data[dataOffset..(dataOffset + (int)file.BaseDataSize)];

            return file;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine(CommonHeader.ToString());
            sb.AppendLine($"サンプリングカウンタ : {SamplingCounter}");
            sb.AppendLine($"スキャンカウンタ     : {ScanCounter}");
            sb.AppendLine($"タイマカウンタ       : {TimerCounter}");
            sb.AppendLine($"タイムスタンプ       : {Timestamp}");
            sb.AppendLine($"基準データサイズ     : {BaseDataSize}");
            return sb.ToString();
        }
    }
}
