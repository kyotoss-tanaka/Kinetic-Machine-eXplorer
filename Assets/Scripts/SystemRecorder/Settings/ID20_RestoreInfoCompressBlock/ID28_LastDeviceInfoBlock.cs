using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{

    // 2.4.9. 最終デバイス情報ブロック(ID=28h) 24968バイト
    internal class ID28_LastDeviceInfoBlock
    {
        public uint AreaTotalSize { get; set; }  // +00h エリア全体サイズ 4バイト
                                                 // +04h 収集対象情報        24962バイト
        public LastDeviceCollectionTargetInfo CollectionTarget { get; set; } = new();
        // +6186h 境界調整用領域   2バイト(可変・0固定)

        public static ID28_LastDeviceInfoBlock Parse(byte[] data, int offset) => new()
        {
            AreaTotalSize = BitConverter.ToUInt32(data, offset + 0x00),
            CollectionTarget = LastDeviceCollectionTargetInfo.Parse(data, offset + 0x04),
        };

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 最終デバイス収集対象情報 ===");
            sb.AppendLine(CollectionTarget.ToString());
            return sb.ToString();
        }
    }

    // 2.4.9.1.1. 最終デバイス情報 78バイト
    public class LastDeviceInfo
    {
        public byte[] DeviceCode { get; set; }  // +00h デバイスコード        10バイト
        public uint LastDeviceMapSize { get; set; } // +0Ah 最終デバイスマップサイズ(要素数) 4バイト (最大64)
        public ushort[] LastDeviceMap { get; set; }  // +0Eh 最終デバイスマップ    2バイト×n (最大64要素=128バイト)
                                                     // 最終デバイスNo.がない収集ブロックは0xFFFF埋め

        public static LastDeviceInfo Parse(byte[] data, int offset)
        {
            var info = new LastDeviceInfo
            {
                DeviceCode = data[(offset + 0x00)..(offset + 0x0A)],
                LastDeviceMapSize = BitConverter.ToUInt32(data, offset + 0x0A),
            };

            // 最終デバイスマップ: 2バイト×LastDeviceMapSize
            info.LastDeviceMap = new ushort[info.LastDeviceMapSize];
            for (int i = 0; i < info.LastDeviceMapSize; i++)
                info.LastDeviceMap[i] = BitConverter.ToUInt16(data, offset + 0x0E + i * 2);

            return info;
        }

        public int TotalSize => 0x0E + (int)LastDeviceMapSize * 2;

        // 収集ブロックiの最終デバイスNo.を取得 (0xFFFF=指定なし)
        public ushort GetLastDeviceNo(int blockIndex)
        {
            if (blockIndex >= LastDeviceMap.Length) return 0xFFFF;
            return LastDeviceMap[blockIndex];
        }

        public bool HasLastDevice(int blockIndex) => GetLastDeviceNo(blockIndex) != 0xFFFF;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"デバイスコード        : {BitConverter.ToString(DeviceCode)}");
            sb.AppendLine($"最終デバイスマップ数  : {LastDeviceMapSize}");
            for (int i = 0; i < LastDeviceMap.Length; i++)
            {
                string val = LastDeviceMap[i] == 0xFFFF ? "指定なし" : $"0x{LastDeviceMap[i]:X4}";
                sb.AppendLine($"  ブロック[{i:D3}]: {val}");
            }
            return sb.ToString();
        }
    }

    // 2.4.9.1. 収集対象情報(最終デバイス) 24962バイト
    public class LastDeviceCollectionTargetInfo
    {
        public ushort LastDeviceCount { get; set; }  // +00h 最終デバイス情報数 2バイト (0〜8192, 実際最大320)
        public List<LastDeviceInfo> LastDevices { get; set; } = new();  // +02h 最終デバイス情報[n]

        public static LastDeviceCollectionTargetInfo Parse(byte[] data, int offset)
        {
            var info = new LastDeviceCollectionTargetInfo
            {
                LastDeviceCount = BitConverter.ToUInt16(data, offset + 0x00),
            };

            int pos = offset + 0x02;
            for (int i = 0; i < info.LastDeviceCount; i++)
            {
                var device = LastDeviceInfo.Parse(data, pos);
                info.LastDevices.Add(device);
                pos += device.TotalSize;
            }

            return info;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"最終デバイス情報数: {LastDeviceCount}");
            for (int i = 0; i < LastDevices.Count; i++)
            {
                sb.AppendLine($"--- 最終デバイス[{i:D3}] ---");
                sb.AppendLine(LastDevices[i].ToString());
            }
            return sb.ToString();
        }
    }
}
