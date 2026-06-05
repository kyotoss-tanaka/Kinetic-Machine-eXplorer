using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // 2.4.4. ユニット指定ブロック(ID=23h) 278バイト
    internal class ID23_UnitSpecifyBlock
    {
        public uint AreaTotalSize { get; set; }  // +00h エリア全体サイズ            4バイト
        public ushort AutoSpecify { get; set; }  // +04h 自動設定有無(ユニット指定)  2バイト (固定:01h 手動設定)
        public ushort UnitMonitorBulk { get; set; }  // +06h ユニットモニタ項目一括指定  2バイト (固定:00h 収集しない)
        public ushort UnitCount { get; set; }  // +08h 指定ユニット数              2バイト (0固定)

        // +0Ah ユニット指定情報[n] 4バイト×n 最大65件=260バイト
        public List<UnitSpecifyInfo> Units { get; set; } = new();

        // +10Eh デバイス種別指定(ユニット指定) 4バイト
        public UnitDeviceTypeSpec DeviceSpec { get; set; } = new();

        // +112h ラベル種別指定情報(ユニット指定) 1バイト
        public UnitLabelTypeSpec LabelSpec { get; set; } = new();

        // +113h 境界調整用領域 3バイト(可変・0固定)

        public static ID23_UnitSpecifyBlock Parse(byte[] data, int offset)
        {
            var block = new ID23_UnitSpecifyBlock
            {
                AreaTotalSize = BitConverter.ToUInt32(data, offset + 0x00),
                AutoSpecify = BitConverter.ToUInt16(data, offset + 0x04),
                UnitMonitorBulk = BitConverter.ToUInt16(data, offset + 0x06),
                UnitCount = BitConverter.ToUInt16(data, offset + 0x08),
            };

            // +0Ah ユニット指定情報[n]
            var pos = 0x0A;
            for (int i = 0; i < block.UnitCount; i++)
            {
                block.Units.Add(UnitSpecifyInfo.Parse(data, offset + pos));
                pos += 4;
            }
            block.DeviceSpec = UnitDeviceTypeSpec.Parse(data, offset + pos);
            block.LabelSpec = UnitLabelTypeSpec.Parse(data, offset + pos + 0x04);

            return block;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"指定ユニット数          : {UnitCount}");
            for (int i = 0; i < Units.Count; i++)
                sb.AppendLine($"  ユニット[{i}]: {Units[i]}");
            sb.AppendLine($"デバイス種別指定        : {DeviceSpec}");
            sb.AppendLine($"ラベル種別指定          : {LabelSpec}");
            return sb.ToString();
        }
    }

    // デバイス種別指定(ユニット指定) 4バイト=32ビット
    public class UnitDeviceTypeSpec
    {
        public uint Bits { get; set; }

        public bool X_Input => (Bits & (1U << 0)) != 0;  // 1:収集する(固定)
        public bool Y_Output => (Bits & (1U << 1)) != 0;  // 1:収集する(固定)
        public bool M_InternalRelay => (Bits & (1U << 2)) != 0;
        public bool L_LatchRelay => (Bits & (1U << 3)) != 0;
        public bool B_LinkRelay => (Bits & (1U << 4)) != 0;
        public bool D_DataReg => (Bits & (1U << 5)) != 0;
        public bool W_LinkReg => (Bits & (1U << 6)) != 0;
        public bool R_FileReg => (Bits & (1U << 7)) != 0;
        public bool ZR_FileReg => (Bits & (1U << 8)) != 0;
        public bool RD_RefreshData => (Bits & (1U << 9)) != 0;
        public bool SB_LinkSpecial => (Bits & (1U << 10)) != 0;
        public bool SW_LinkSpecReg => (Bits & (1U << 11)) != 0;
        public bool SM_SpecialRelay => (Bits & (1U << 12)) != 0;
        public bool SD_SpecialReg => (Bits & (1U << 13)) != 0;
        public bool UnG_UnitAccess => (Bits & (1U << 14)) != 0;
        // b15-31: 空き

        public static UnitDeviceTypeSpec Parse(byte[] data, int offset) => new()
        {
            Bits = BitConverter.ToUInt32(data, offset),
        };

        public override string ToString() => $"0x{Bits:X8}";
    }

    // ラベル種別指定情報(ユニット指定) 1バイト
    public class UnitLabelTypeSpec
    {
        public byte Bits { get; set; }

        public bool UnitLabel => (Bits & (1 << 0)) != 0;  // 1:収集する(固定)
                                                          // b1-7: 空き

        public static UnitLabelTypeSpec Parse(byte[] data, int offset) => new()
        {
            Bits = data[offset],
        };

        public override string ToString() => $"ユニットラベル={UnitLabel}";
    }

    // 2.4.4.1. ユニット指定情報 4バイト
    public class UnitSpecifyInfo
    {
        public ushort SlotPosition { get; set; }  // +00h スロット位置           2バイト (0:CPUスロット, 1〜65:スロットNo.+1)
        public ushort UnitMonitorItemSpec { get; set; }  // +02h ユニットモニタ項目指定情報 2バイト

        public static UnitSpecifyInfo Parse(byte[] data, int offset) => new()
        {
            SlotPosition = BitConverter.ToUInt16(data, offset + 0x00),
            UnitMonitorItemSpec = BitConverter.ToUInt16(data, offset + 0x02),
        };

        public string SlotName => SlotPosition == 0 ? "CPUスロット" : $"スロット{SlotPosition - 1}";

        public override string ToString() =>
            $"スロット={SlotName} モニタ項目=0x{UnitMonitorItemSpec:X4}";
    }
}
