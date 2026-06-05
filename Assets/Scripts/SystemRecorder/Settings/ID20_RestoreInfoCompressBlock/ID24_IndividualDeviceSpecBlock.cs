using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // 2.4.5. 個別デバイス指定ブロック(ID=24h) 最大1048568バイト(1024KB)
    internal class ID24_IndividualDeviceSpecBlock
    {
        public uint AreaTotalSize { get; set; }  // +00h エリア全体サイズ  4バイト
        public uint DeviceCount { get; set; }  // +04h 個別指定デバイス数 4バイト (0〜65535)

        // +08h 個別デバイス指定情報[n] 16バイト×n 最大65535件
        public List<IndividualDeviceSpecInfo> Devices { get; set; } = new();

        public static ID24_IndividualDeviceSpecBlock Parse(byte[] data, int offset)
        {
            var block = new ID24_IndividualDeviceSpecBlock
            {
                AreaTotalSize = BitConverter.ToUInt32(data, offset + 0x00),
                DeviceCount = BitConverter.ToUInt32(data, offset + 0x04),
            };

            for (int i = 0; i < block.DeviceCount; i++)
            {
                block.Devices.Add(IndividualDeviceSpecInfo.Parse(data, offset + 0x08 + i * 0x10));
            }

            return block;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"個別指定デバイス数: {DeviceCount}");
            for (int i = 0; i < Devices.Count; i++)
                sb.AppendLine($"  デバイス[{i:D5}]: {Devices[i]}");
            return sb.ToString();
        }
    }

    // 2.4.5.1. 個別デバイス指定情報 16バイト
    public class IndividualDeviceSpecInfo
    {
        public ushort ProgramNo { get; set; }  // +00h プログラムNo.  2バイト
        public byte[] DeviceCode { get; set; }  // +02h デバイスコード 10バイト
        public uint PointCount { get; set; }  // +0Ch 点数           4バイト (0:空欄, 1〜4294967295)

        // プログラムNo.の意味
        public string ProgramNoName => ProgramNo switch
        {
            0x0000 => "グローバルデバイス",
            0xFFFF => "空欄",
            _ => $"ローカルデバイス ProgramNo.={ProgramNo}"
        };

        public bool IsEmpty => ProgramNo == 0xFFFF;

        public static IndividualDeviceSpecInfo Parse(byte[] data, int offset) => new()
        {
            ProgramNo = BitConverter.ToUInt16(data, offset + 0x00),
            DeviceCode = data[(offset + 0x02)..(offset + 0x0C)],
            PointCount = BitConverter.ToUInt32(data, offset + 0x0C),
        };

        public override string ToString() =>
            IsEmpty
                ? "空欄"
                : $"{ProgramNoName} デバイスコード={BitConverter.ToString(DeviceCode)} 点数={PointCount}";
    }
}
