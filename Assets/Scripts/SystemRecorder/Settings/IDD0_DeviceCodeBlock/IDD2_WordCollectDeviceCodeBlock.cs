using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // =====================================================
    // 2.6.3 ワード単位収集デバイスコード情報ブロック (ID=D2h)
    // =====================================================
    internal class IDD2_WordCollectDeviceCodeBlock
    {
        public uint AreaTotalSize { get; set; }  // +0h,  4byte
        public uint CollectDataSize { get; set; }  // +4h,  4byte (0～4294967294, 0xFFFFFFFF=4294967295)
        public uint DeviceCodeCount { get; set; }  // +8h,  4byte (0～131072)

        /// <summary>デバイスコード情報配列 +Ch～ (DeviceCodeCount個)</summary>
        public List<DeviceCodeInfo> DeviceCodes { get; set; } = new();

        // 境界調整領域は読み飛ばし（0固定パディング）

        public static IDD2_WordCollectDeviceCodeBlock Parse(byte[] data, int offset)
        {
            var block = new IDD2_WordCollectDeviceCodeBlock
            {
                AreaTotalSize = BitConverter.ToUInt32(data, offset + 0x00),
                CollectDataSize = BitConverter.ToUInt32(data, offset + 0x04),
                DeviceCodeCount = BitConverter.ToUInt32(data, offset + 0x08),
            };

            var pos = offset + 0x0C;
            for (int i = 0; i < block.DeviceCodeCount; i++)
            {
                block.DeviceCodes.Add(DeviceCodeInfo.Parse(data, pos));
                pos += DeviceCodeInfo.Size;
            }

            return block;
        }

        /// <summary>収集データ全サイズが未定義(0xFFFFFFFF)かどうか</summary>
        public bool IsCollectDataSizeUnlimited => CollectDataSize == uint.MaxValue;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== ワード単位収集デバイスコード情報ブロック (ID=D2h) ===");
            sb.AppendLine($"エリア全体サイズ    : {AreaTotalSize} byte");
            sb.AppendLine($"収集データ全体サイズ: {(IsCollectDataSizeUnlimited ? "0xFFFFFFFF (最大)" : $"{CollectDataSize} byte")}");
            sb.AppendLine($"デバイスコード情報数: {DeviceCodeCount}");
            for (int i = 0; i < DeviceCodes.Count; i++)
            {
                sb.AppendLine($"  [デバイスコード情報 {i}]");
                sb.Append(DeviceCodes[i]);
            }
            return sb.ToString();
        }
    }
}
