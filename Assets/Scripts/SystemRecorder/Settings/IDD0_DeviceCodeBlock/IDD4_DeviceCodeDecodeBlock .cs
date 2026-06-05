using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // =====================================================
    // 2.6.5 ビット単位収集デバイスコード情報ブロック (ID=D4h)
    // =====================================================
    public class IDD4_BitCollectDeviceCodeBlock
    {
        public uint AreaTotalSize { get; set; }  // +0h, 4byte
        public uint CollectDataSize { get; set; }  // +4h, 4byte (0～40960)
        public uint DeviceCodeCount { get; set; }  // +8h, 4byte (0～10240)

        /// <summary>デバイスコード情報配列 +Ch～ (DeviceCodeCount個)</summary>
        public List<BitDeviceCodeInfo> DeviceCodes { get; set; } = new();

        // 境界調整領域は読み飛ばし（0固定パディング）

        public static IDD4_BitCollectDeviceCodeBlock Parse(ReadOnlySpan<byte> data, int baseOffset = 0)
        {
            if (data.Length < baseOffset + 12)
                throw new ArgumentException("データが不足しています。");

            var block = new IDD4_BitCollectDeviceCodeBlock
            {
                AreaTotalSize = BitConverter.ToUInt32(data.Slice(baseOffset + 0x00, 4)),
                CollectDataSize = BitConverter.ToUInt32(data.Slice(baseOffset + 0x04, 4)),
                DeviceCodeCount = BitConverter.ToUInt32(data.Slice(baseOffset + 0x08, 4)),
            };

            int offset = baseOffset + 0x0C;
            for (int i = 0; i < block.DeviceCodeCount; i++)
            {
                block.DeviceCodes.Add(BitDeviceCodeInfo.Parse(data, offset));
                offset += BitDeviceCodeInfo.Size;
            }

            return block;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== ビット単位収集デバイスコード情報ブロック ===");
            sb.AppendLine($"エリア全体サイズ    : {AreaTotalSize} byte");
            sb.AppendLine($"収集データ全体サイズ: {CollectDataSize} byte");
            sb.AppendLine($"デバイスコード情報数: {DeviceCodeCount}");
            for (int i = 0; i < DeviceCodes.Count; i++)
            {
                sb.AppendLine($"  [デバイスコード情報 {i}]");
                sb.Append(DeviceCodes[i]);
            }
            return sb.ToString();
        }
    }
    
    // =====================================================
     // 2.6.5.1 デバイスコード情報（ビット単位）18byte
     // =====================================================
    public class BitDeviceCodeInfo
    {
        /// <summary>修飾デバイスコード +0h, 16byte</summary>
        public byte[] ModifiedDeviceCode { get; set; } = new byte[16];

        /// <summary>点数（ビットデバイス換算）+10h, 2byte</summary>
        public ushort PointCount { get; set; }

        public const int Size = 18;

        public static BitDeviceCodeInfo Parse(ReadOnlySpan<byte> data, int offset)
        {
            if (data.Length < offset + Size)
                throw new ArgumentException($"データが不足しています。必要: {offset + Size} byte, 実際: {data.Length} byte");

            var info = new BitDeviceCodeInfo
            {
                PointCount = BitConverter.ToUInt16(data.Slice(offset + 0x10, 2)),
            };
            data.Slice(offset + 0x00, 16).CopyTo(info.ModifiedDeviceCode);
            return info;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"    修飾デバイスコード: {BitConverter.ToString(ModifiedDeviceCode)}");
            sb.AppendLine($"    点数            : {PointCount}");
            return sb.ToString();
        }
    }

}
