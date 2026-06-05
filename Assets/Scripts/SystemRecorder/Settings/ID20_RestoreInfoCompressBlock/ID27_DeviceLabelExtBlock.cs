using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{

    // 2.4.8. デバイスラベル一覧拡張指定ブロック(ID=27h) 5768バイト
    internal class ID27_DeviceLabelExtBlock
    {
        public uint AreaTotalSize { get; set; }  // +00h エリア全体サイズ  4バイト
                                                 // +04h 収集対象情報        5762バイト
        public ExtCollectionTargetInfo CollectionTarget { get; set; } = new();
        // +1686h 境界調整用領域   2バイト(可変・0固定)

        public static ID27_DeviceLabelExtBlock Parse(byte[] data, int offset) => new()
        {
            AreaTotalSize = BitConverter.ToUInt32(data, offset + 0x00),
            CollectionTarget = ExtCollectionTargetInfo.Parse(data, offset + 0x04),
        };

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 収集対象情報(拡張) ===");
            sb.AppendLine(CollectionTarget.ToString());
            return sb.ToString();
        }
    }

    // 2.4.8.1.1. デバイス情報(拡張) 18バイト固定
    // ※DeviceInfo(2.4.7.1.2)と構造は同じだがBitmapの最大値が4バイト
    public class ExtDeviceInfo
    {
        public byte[] DeviceCode { get; set; }  // +00h デバイスコード    10バイト
        public uint BitmapSize { get; set; }  // +0Ah バイナリマップサイズ 4バイト (最大4バイト)
        public byte[] Bitmap { get; set; }  // +0Eh バイナリマップ     可変(最大4バイト)

        public static ExtDeviceInfo Parse(byte[] data, int offset)
        {
            var info = new ExtDeviceInfo
            {
                DeviceCode = data[(offset + 0x00)..(offset + 0x0A)],
                BitmapSize = BitConverter.ToUInt32(data, offset + 0x0A),
            };
            info.Bitmap = data[(offset + 0x0E)..(offset + 0x0E + (int)info.BitmapSize)];
            return info;
        }

        public int TotalSize => 0x0E + (int)BitmapSize;

        // バイナリマップのビット解釈: bit=0:収集しない、1:収集する
        public bool IsCollected(int wordIndex)
        {
            int byteIdx = wordIndex / 8;
            int bitIdx = wordIndex % 8;
            if (byteIdx >= Bitmap.Length) return false;
            return (Bitmap[byteIdx] & (1 << bitIdx)) != 0;
        }

        public override string ToString() =>
            $"デバイスコード={BitConverter.ToString(DeviceCode)} バイナリマップサイズ={BitmapSize}";
    }

    // 2.4.8.1. 収集対象情報(拡張) 5762バイト
    public class ExtCollectionTargetInfo
    {
        public ushort DeviceInfoCount { get; set; }  // +00h デバイス情報数 2バイト (0〜8192, 実際最大320)
        public List<ExtDeviceInfo> Devices { get; set; } = new();  // +02h デバイス情報[n] 可変×n

        public static ExtCollectionTargetInfo Parse(byte[] data, int offset)
        {
            var info = new ExtCollectionTargetInfo
            {
                DeviceInfoCount = BitConverter.ToUInt16(data, offset + 0x00),
            };

            int pos = offset + 0x02;
            for (int i = 0; i < info.DeviceInfoCount; i++)
            {
                var device = ExtDeviceInfo.Parse(data, pos);
                info.Devices.Add(device);
                pos += device.TotalSize;
            }

            return info;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"デバイス情報数: {DeviceInfoCount}");
            for (int i = 0; i < Devices.Count; i++)
                sb.AppendLine($"  デバイス[{i:D3}]: {Devices[i]}");
            return sb.ToString();
        }
    }
}
