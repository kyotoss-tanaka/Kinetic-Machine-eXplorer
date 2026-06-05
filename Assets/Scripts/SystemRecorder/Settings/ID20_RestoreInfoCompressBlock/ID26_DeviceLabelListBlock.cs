using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{

    // 2.4.7. デバイスラベル一覧指定ブロック(ID=26h) 140102バイト
    internal class ID26_DeviceLabelListBlock
    {
        public uint AreaTotalSize { get; set; }  // +00h エリア全体サイズ  4バイト

        // +04h 収集対象情報        140094バイト
        public CollectionTargetInfo CollectionTarget { get; set; } = new();

        // +22342h オプション設定情報 1バイト
        public DeviceLabelOptionInfo OptionInfo { get; set; } = new();

        // +22343h 境界調整用領域    3バイト(可変・0固定)

        public static ID26_DeviceLabelListBlock Parse(byte[] data, int offset) => new()
        {
            AreaTotalSize = BitConverter.ToUInt32(data, offset + 0x00),
            CollectionTarget = CollectionTargetInfo.Parse(data, offset + 0x04),
            OptionInfo = DeviceLabelOptionInfo.Parse(data, offset + 0x22342),
        };

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 収集対象情報 ===");
            sb.AppendLine(CollectionTarget.ToString());
            sb.AppendLine($"オプション設定情報: {OptionInfo}");
            return sb.ToString();
        }
    }

    // 2.4.7.1.1. ローカルラベル情報 1バイト
    public class LocalLabelInfo
    {
        public byte ProgramNo { get; set; }  // +00h プログラムNo. 1バイト (1〜252)

        public static LocalLabelInfo Parse(byte[] data, int offset) => new()
        {
            ProgramNo = data[offset],
        };

        public override string ToString() => $"ProgramNo={ProgramNo}";
    }

    // 2.4.7.1.2. デバイス情報 39542バイト
    public class DeviceInfo
    {
        public byte[] DeviceCode { get; set; }  // +00h デバイスコード    10バイト
        public uint BitmapSize { get; set; }  // +0Ah バイナリマップサイズ 4バイト (1〜4294967295)
        public byte[] Bitmap { get; set; }  // +0Eh バイナリマップ     可変(最大39528バイト)

        public static DeviceInfo Parse(byte[] data, int offset)
        {
            var info = new DeviceInfo
            {
                DeviceCode = data[(offset + 0x00)..(offset + 0x0A)],
                BitmapSize = BitConverter.ToUInt32(data, offset + 0x0A),
            };
            info.Bitmap = data[(offset + 0x0E)..(offset + 0x0E + (int)info.BitmapSize)];
            return info;
        }

        public int TotalSize => 0x0E + (int)BitmapSize;

        public override string ToString() =>
            $"デバイスコード={BitConverter.ToString(DeviceCode)} バイナリマップサイズ={BitmapSize}";
    }

    // 2.4.7.1. 収集対象情報 140094バイト
    public class CollectionTargetInfo
    {
        // ラベル種別指定情報 1バイト (LabelTypeSpecを再利用)
        public LabelTypeSpec LabelSpec { get; set; } = new();  // +00h

        // ローカルラベル情報数 1バイト (0〜252)
        public byte LocalLabelCount { get; set; }  // +01h

        // ローカルラベル情報[n] 1バイト×n (最大252件)
        public List<LocalLabelInfo> LocalLabels { get; set; } = new();  // +02h

        // デバイス情報数 2バイト (0〜8192)
        public ushort DeviceInfoCount { get; set; }  // +FEh

        // デバイス情報[n] 可変×n (最大7165件)
        public List<DeviceInfo> Devices { get; set; } = new();  // +100h

        public static CollectionTargetInfo Parse(byte[] data, int offset)
        {
            var info = new CollectionTargetInfo
            {
                LabelSpec = LabelTypeSpec.Parse(data, offset + 0x00),
                LocalLabelCount = data[offset + 0x01],
            };

            // +02h ローカルラベル情報[n] 1バイト×n
            for (int i = 0; i < info.LocalLabelCount; i++)
                info.LocalLabels.Add(LocalLabelInfo.Parse(data, offset + 0x02 + i));

            // +FEh デバイス情報数
            info.DeviceInfoCount = BitConverter.ToUInt16(data, offset + 0xFE);

            // +100h デバイス情報[n] (可変サイズのため都度オフセット計算)
            int pos = offset + 0x100;
            for (int i = 0; i < info.DeviceInfoCount; i++)
            {
                var device = DeviceInfo.Parse(data, pos);
                info.Devices.Add(device);
                pos += device.TotalSize;
            }

            return info;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"ラベル種別          : {LabelSpec}");
            sb.AppendLine($"ローカルラベル数    : {LocalLabelCount}");
            for (int i = 0; i < LocalLabels.Count; i++)
                sb.AppendLine($"  ローカルラベル[{i}]: {LocalLabels[i]}");
            sb.AppendLine($"デバイス情報数      : {DeviceInfoCount}");
            for (int i = 0; i < Devices.Count; i++)
                sb.AppendLine($"  デバイス[{i:D4}]: {Devices[i]}");
            return sb.ToString();
        }
    }

    // 2.4.7.2. オプション設定情報 1バイト ※別途仕様参照のため生データで保持
    public class DeviceLabelOptionInfo
    {
        public byte[] RawData { get; set; }

        public static DeviceLabelOptionInfo Parse(byte[] data, int offset) => new()
        {
            RawData = data[offset..(offset + 1)],
        };

        public override string ToString() => $"0x{RawData[0]:X2}";
    }
}
