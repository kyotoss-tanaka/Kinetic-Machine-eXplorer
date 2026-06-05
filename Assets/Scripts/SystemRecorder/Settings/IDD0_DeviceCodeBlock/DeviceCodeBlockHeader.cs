using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // ============================================================
    // 2.6.1. 設定情報ヘッダ（デバイスコードブロック）56バイト固定
    // ============================================================
    internal class DeviceCodeBlockHeader
    {
        public uint AreaTotalSize { get; set; }  // +0h,  4byte
        public uint ConfigBlockCount { get; set; }  // +4h,  4byte

        public SettingBlockInfo DeviceCodeDecodeInfo { get; set; } = new(); // +8h
        public SettingBlockInfo DeviceCodeInfo { get; set; } = new(); // +18h
        public SettingBlockInfo TriggerDeviceInfo { get; set; } = new(); // +28h

        public const int HeaderSize = 56;

        public static DeviceCodeBlockHeader Parse(byte[] data, int baseOffset = 0)
        {
            if (data.Length < baseOffset + HeaderSize)
                throw new ArgumentException(
                    $"データが不足しています。必要: {baseOffset + HeaderSize} byte, 実際: {data.Length} byte");

            return new DeviceCodeBlockHeader
            {
                AreaTotalSize = BitConverter.ToUInt32(data, baseOffset + 0x00),
                ConfigBlockCount = BitConverter.ToUInt32(data, baseOffset + 0x04),
                DeviceCodeDecodeInfo = SettingBlockInfo.Parse(data, baseOffset + 0x08),
                DeviceCodeInfo = SettingBlockInfo.Parse(data, baseOffset + 0x18),
                TriggerDeviceInfo = SettingBlockInfo.Parse(data, baseOffset + 0x28),
            };
        }
        
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"エリア全体サイズ : {AreaTotalSize} byte (0x{AreaTotalSize:X8})");
            sb.AppendLine($"設定ブロック数   : {ConfigBlockCount}");
            sb.AppendLine();
            sb.AppendLine("--- デバイスコード解読情報ブロック (+8h) ---");
            sb.Append(DeviceCodeDecodeInfo);
            sb.AppendLine();
            sb.AppendLine("--- デバイスコード情報ブロック (+18h) ---");
            sb.Append(DeviceCodeInfo);
            sb.AppendLine();
            sb.AppendLine("--- トリガデバイス情報ブロック (+28h) ---");
            sb.Append(TriggerDeviceInfo);
            return sb.ToString();
        }

    }
}
