using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{

    // =====================================================
    // 2.6.2 デバイスコード解読情報ブロック (ID=D1h)
    // =====================================================
    internal class IDD1_DeviceCodeDecodeBlock
    {
        public uint AreaTotalSize { get; set; }  // +0h, 4byte
        public ushort DeviceBlockCount { get; set; } // +4h, 2byte (0～8192)

        /// <summary>デバイスブロック情報配列 +6h～ (DeviceBlockCount個)</summary>
        public List<DeviceBlockInfo> DeviceBlocks { get; set; } = new();

        // 境界調整領域は読み飛ばし（0固定パディング）

        public static IDD1_DeviceCodeDecodeBlock Parse(ReadOnlySpan<byte> data, int baseOffset = 0)
        {
            if (data.Length < baseOffset + 6)
                throw new ArgumentException("データが不足しています。");

            var block = new IDD1_DeviceCodeDecodeBlock
            {
                AreaTotalSize = BitConverter.ToUInt32(data.Slice(baseOffset + 0x00, 4)),
                DeviceBlockCount = BitConverter.ToUInt16(data.Slice(baseOffset + 0x04, 2)),
            };

            int offset = baseOffset + 0x06;
            for (int i = 0; i < block.DeviceBlockCount; i++)
            {
                block.DeviceBlocks.Add(DeviceBlockInfo.Parse(data, offset));
                offset += DeviceBlockInfo.Size;
            }

            return block;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== デバイスコード解読情報ブロック (ID=D1h) ===");
            sb.AppendLine($"エリア全体サイズ   : {AreaTotalSize} byte");
            sb.AppendLine($"デバイスブロック数 : {DeviceBlockCount}");
            for (int i = 0; i < DeviceBlocks.Count; i++)
            {
                sb.AppendLine($"  [デバイスブロック {i}]");
                sb.AppendLine(DeviceBlocks[i].ToString());
            }
            return sb.ToString();
        }
    }

    // =====================================================
    // 2.6.2.1 デバイスブロック情報 4byte
    // =====================================================
    public class DeviceBlockInfo
    {
        public uint DeviceCodeCount { get; set; }  // +0h, 4byte: デバイスコード数

        public const int Size = 4;

        public static DeviceBlockInfo Parse(ReadOnlySpan<byte> data, int offset)
        {
            if (data.Length < offset + Size)
                throw new ArgumentException($"データが不足しています。必要: {offset + Size} byte, 実際: {data.Length} byte");

            return new DeviceBlockInfo
            {
                DeviceCodeCount = BitConverter.ToUInt32(data.Slice(offset + 0x00, 4)),
            };
        }

        public override string ToString()
            => $"  デバイスコード数: {DeviceCodeCount}";
    }

    // =====================================================
    // 2.6.3.1 デバイスコード情報 18byte
    // =====================================================
    public class DeviceCodeInfo
    {
        public enum DeviceCodeType : byte
        {
            M = 0x01,
            X = 0x10,
            Y = 0x11,
            D = 0x20,
            ZR = 0x28,
            W = 0x30,
            TC = 0x40,
            TN = 0x42,
            CC = 0x44,
            CN = 0x46,
        }

        /// <summary>修飾デバイスコード +0h, 16byte</summary>
        public byte[] ModifiedDeviceCode { get; set; } = new byte[16];

        /// <summary>点数（ワードデバイス換算）+10h, 2byte</summary>
        public ushort PointCount { get; set; }
        public string DeviceName { get; set; } = "";

        public const int Size = 18;

        public bool IsHex
        {
            get
            {
                return (DeviceName == "X") || (DeviceName == "Y");
            }
        }

        public bool IsBit
        {
            get
            {
                return (DeviceName == "X") || (DeviceName == "Y") || (DeviceName == "M") || (DeviceName == "TC");
            }
        }

        public int OffsetDec
        {
            get
            {
                return BitConverter.ToInt32(ModifiedDeviceCode, 2);

            }
        }


        public string OffsetHex
        {
            get
            {
                return $"0x{OffsetDec:X4}";
            }
        }

        public static DeviceCodeInfo Parse(ReadOnlySpan<byte> data, int offset)
        {
            if (data.Length < offset + Size)
                throw new ArgumentException($"データが不足しています。必要: {offset + Size} byte, 実際: {data.Length} byte");

            var info = new DeviceCodeInfo
            {
                PointCount = BitConverter.ToUInt16(data.Slice(offset + 0x10, 2)),
            };
            data.Slice(offset + 0x00, 16).CopyTo(info.ModifiedDeviceCode);
            info.DeviceName = Enum.IsDefined(typeof(DeviceCodeType), info.ModifiedDeviceCode[0])
                    ? ((DeviceCodeType)info.ModifiedDeviceCode[0]).ToString()
                    : $"Unknown(0x{info.ModifiedDeviceCode[0]:X2})";
            return info;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"    修飾デバイスコード: {BitConverter.ToString(ModifiedDeviceCode)}");
            sb.AppendLine($"    デバイス名称      : {DeviceName}");
            sb.AppendLine($"    デバイスオフセット: {OffsetDec}({OffsetHex})");
            sb.AppendLine($"    点数              : {PointCount}");
            return sb.ToString();
        }
    }
}
