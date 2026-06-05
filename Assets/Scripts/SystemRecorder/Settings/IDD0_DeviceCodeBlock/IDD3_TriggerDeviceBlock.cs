using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // =====================================================
    // 2.6.4 トリガデバイス情報ブロック (ID=D3h)
    // =====================================================
    internal class IDD3_TriggerDeviceBlock
    {
        public uint AreaTotalSize { get; set; }  // +0h, 4byte
        public ushort TriggerDeviceCount { get; set; }  // +4h, 2byte (0～17)

        /// <summary>トリガデバイス情報配列 +6h～ (TriggerDeviceCount個)</summary>
        public List<TriggerDeviceInfo> TriggerDevices { get; set; } = new();

        // 境界調整領域は読み飛ばし（0固定パディング）

        public static IDD3_TriggerDeviceBlock Parse(ReadOnlySpan<byte> data, int baseOffset = 0)
        {
            if (data.Length < baseOffset + 6)
                throw new ArgumentException("データが不足しています。");

            var block = new IDD3_TriggerDeviceBlock
            {
                AreaTotalSize = BitConverter.ToUInt32(data.Slice(baseOffset + 0x00, 4)),
                TriggerDeviceCount = BitConverter.ToUInt16(data.Slice(baseOffset + 0x04, 2)),
            };

            int offset = baseOffset + 0x06;
            for (int i = 0; i < block.TriggerDeviceCount; i++)
            {
                block.TriggerDevices.Add(TriggerDeviceInfo.Parse(data, offset));
                offset += TriggerDeviceInfo.Size;
            }

            return block;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== トリガデバイス情報ブロック (ID=D3h) ===");
            sb.AppendLine($"エリア全体サイズ    : {AreaTotalSize} byte");
            sb.AppendLine($"トリガデバイス情報数: {TriggerDeviceCount}");
            for (int i = 0; i < TriggerDevices.Count; i++)
            {
                sb.AppendLine($"  [トリガデバイス情報 {i}]");
                sb.Append(TriggerDevices[i]);
            }
            return sb.ToString();
        }
    }

    // =====================================================
    // 2.6.4.1 トリガデバイス情報 32byte
    // =====================================================
    public class TriggerDeviceInfo
    {
        /// <summary>修飾デバイスコード（指定デバイス）+0h, 16byte</summary>
        public byte[] SpecifiedDeviceCode { get; set; } = new byte[16];

        /// <summary>修飾デバイスコード（書込み先デバイス）+10h, 16byte</summary>
        public byte[] WriteDestDeviceCode { get; set; } = new byte[16];

        public const int Size = 32;

        public static TriggerDeviceInfo Parse(ReadOnlySpan<byte> data, int offset)
        {
            if (data.Length < offset + Size)
                throw new ArgumentException($"データが不足しています。必要: {offset + Size} byte, 実際: {data.Length} byte");

            var info = new TriggerDeviceInfo();
            data.Slice(offset + 0x00, 16).CopyTo(info.SpecifiedDeviceCode);
            data.Slice(offset + 0x10, 16).CopyTo(info.WriteDestDeviceCode);
            return info;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"    修飾デバイスコード（指定デバイス）  : {BitConverter.ToString(SpecifiedDeviceCode)}");
            sb.AppendLine($"    修飾デバイスコード（書込み先デバイス）: {BitConverter.ToString(WriteDestDeviceCode)}");
            return sb.ToString();
        }
    }
}
