using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // 2.4.3. プログラム指定ブロック(ID=22h) 528バイト
    internal class ID22_ProgramSpecifyBlock
    {
        public uint AreaTotalSize { get; set; }  // +00h エリア全体サイズ          4バイト
        public ushort AutoSpecify { get; set; }  // +04h 自動設定有無(プログラム指定) 2バイト
        public ushort ProgramCount { get; set; }  // +06h 指定プログラム数           2バイト (0〜252)

        // +08h プログラム指定情報[n] 2バイト×n (最大252件=504バイト)
        public List<ProgramSpecifyInfo> Programs { get; set; } = new();

        // +200h グローバルデバイス種別指定情報 8バイト
        public GlobalDeviceTypeSpec GlobalDeviceSpec { get; set; } = new();

        // +208h ローカルデバイス種別指定情報   2バイト
        public LocalDeviceTypeSpec LocalDeviceSpec { get; set; } = new();

        // +20Ah ラベル種別指定情報             1バイト
        public LabelTypeSpec LabelSpec { get; set; } = new();

        // +20Bh SFC関連デバイス種別指定情報    1バイト
        public SfcDeviceTypeSpec SfcDeviceSpec { get; set; } = new();

        // +20Ch 安全デバイス種別指定情報       4バイト
        public SafetyDeviceTypeSpec SafetyDeviceSpec { get; set; } = new();

        // +210h 境界調整用領域(可変・0固定)

        public string AutoSpecifyName => AutoSpecify switch
        {
            0x00 => "自動設定",
            0x01 => "手動設定",
            _ => $"不明(0x{AutoSpecify:X2})"
        };

        public static ID22_ProgramSpecifyBlock Parse(byte[] data, int offset)
        {
            var block = new ID22_ProgramSpecifyBlock
            {
                AreaTotalSize = BitConverter.ToUInt32(data, offset + 0x00),
                AutoSpecify = BitConverter.ToUInt16(data, offset + 0x04),
                ProgramCount = BitConverter.ToUInt16(data, offset + 0x06),
            };

            // +08h プログラム指定情報[n]
            var pos = 0x08;
            for (int i = 0; i < block.ProgramCount; i++)
            {
                block.Programs.Add(ProgramSpecifyInfo.Parse(data, offset + pos));
                pos += 2;
            }
            block.GlobalDeviceSpec = GlobalDeviceTypeSpec.Parse(data, offset + pos);
            block.LocalDeviceSpec = LocalDeviceTypeSpec.Parse(data, offset + pos + 0x08);
            block.LabelSpec = LabelTypeSpec.Parse(data, offset + pos + 0x0A);
            block.SfcDeviceSpec = SfcDeviceTypeSpec.Parse(data, offset + pos + 0x0B);
            block.SafetyDeviceSpec = SafetyDeviceTypeSpec.Parse(data, offset + pos + 0x0C);

            return block;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"自動設定有無            : {AutoSpecifyName}");
            sb.AppendLine($"指定プログラム数        : {ProgramCount}");
            for (int i = 0; i < Programs.Count; i++)
                sb.AppendLine($"  プログラム[{i}]: {Programs[i]}");
            sb.AppendLine($"グローバルデバイス種別  : {GlobalDeviceSpec}");
            sb.AppendLine($"ローカルデバイス種別    : {LocalDeviceSpec}");
            sb.AppendLine($"ラベル種別              : {LabelSpec}");
            sb.AppendLine($"SFC関連デバイス種別     : {SfcDeviceSpec}");
            sb.AppendLine($"安全デバイス種別        : {SafetyDeviceSpec}");
            return sb.ToString();
        }
    }

    // 2.4.3.1. プログラム指定情報 2バイト
    public class ProgramSpecifyInfo
    {
        public ushort ProgramNo { get; set; }  // +00h プログラムNo. 2バイト (1〜252)

        public static ProgramSpecifyInfo Parse(byte[] data, int offset) => new()
        {
            ProgramNo = BitConverter.ToUInt16(data, offset + 0x00),
        };

        public override string ToString() => $"ProgramNo={ProgramNo}";
    }

    // グローバルデバイス種別指定情報 (8バイト=64ビット)
    public class GlobalDeviceTypeSpec
    {
        public ulong Bits { get; set; }

        public bool X_Input => (Bits & (1UL << 0)) != 0;
        public bool Y_Output => (Bits & (1UL << 1)) != 0;
        public bool M_InternalRelay => (Bits & (1UL << 2)) != 0;
        public bool B_LinkRelay => (Bits & (1UL << 3)) != 0;
        public bool F_Annunciator => (Bits & (1UL << 4)) != 0;
        public bool SB_LinkSpecial => (Bits & (1UL << 5)) != 0;
        public bool V_EdgeRelay => (Bits & (1UL << 6)) != 0;
        public bool T_Timer => (Bits & (1UL << 7)) != 0;
        public bool ST_AccumTimer => (Bits & (1UL << 8)) != 0;
        public bool LT_LongTimer => (Bits & (1UL << 9)) != 0;
        public bool LST_LongAccum => (Bits & (1UL << 10)) != 0;
        public bool C_Counter => (Bits & (1UL << 11)) != 0;
        public bool LC_LongCounter => (Bits & (1UL << 12)) != 0;
        public bool D_DataReg => (Bits & (1UL << 13)) != 0;
        public bool W_LinkReg => (Bits & (1UL << 14)) != 0;
        public bool SW_LinkSpecialReg => (Bits & (1UL << 15)) != 0;
        public bool L_LatchRelay => (Bits & (1UL << 16)) != 0;
        // b17-19: 空き
        public bool SM_SpecialRelay => (Bits & (1UL << 20)) != 0;
        public bool SD_SpecialReg => (Bits & (1UL << 21)) != 0;
        public bool JnX_LinkInput => (Bits & (1UL << 22)) != 0;
        public bool JnY_LinkOutput => (Bits & (1UL << 23)) != 0;
        public bool JnB_LinkRelay => (Bits & (1UL << 24)) != 0;
        public bool JnSB_LinkSpecial => (Bits & (1UL << 25)) != 0;
        public bool JnW_LinkReg => (Bits & (1UL << 26)) != 0;
        public bool JnSW_LinkSpecReg => (Bits & (1UL << 27)) != 0;
        public bool UnG_CpuBufAccess => (Bits & (1UL << 28)) != 0;
        public bool U3EnG_CpuBuf => (Bits & (1UL << 29)) != 0;
        public bool U3EnHG_CpuBuf => (Bits & (1UL << 30)) != 0;
        public bool ZLZ_IndexReg => (Bits & (1UL << 31)) != 0;
        public bool R_FileReg => (Bits & (1UL << 32)) != 0;
        public bool ZR_FileReg => (Bits & (1UL << 33)) != 0;
        public bool RD_RefreshDataReg => (Bits & (1UL << 34)) != 0;
        // b35-63: 空き

        public static GlobalDeviceTypeSpec Parse(byte[] data, int offset) => new()
        {
            Bits = BitConverter.ToUInt64(data, offset),
        };

        public override string ToString() => $"0x{Bits:X16}";
    }

    // ローカルデバイス種別指定情報 (2バイト=16ビット)
    public class LocalDeviceTypeSpec
    {
        public ushort Bits { get; set; }

        public bool M_InternalRelay => (Bits & (1 << 0)) != 0;
        public bool V_EdgeRelay => (Bits & (1 << 1)) != 0;
        public bool T_Timer => (Bits & (1 << 2)) != 0;
        public bool LT_LongTimer => (Bits & (1 << 3)) != 0;
        public bool ST_AccumTimer => (Bits & (1 << 4)) != 0;
        public bool LST_LongAccum => (Bits & (1 << 5)) != 0;
        public bool C_Counter => (Bits & (1 << 6)) != 0;
        public bool LC_LongCounter => (Bits & (1 << 7)) != 0;
        public bool D_DataReg => (Bits & (1 << 8)) != 0;
        // b9-15: 空き

        public static LocalDeviceTypeSpec Parse(byte[] data, int offset) => new()
        {
            Bits = BitConverter.ToUInt16(data, offset),
        };

        public override string ToString() => $"0x{Bits:X4}";
    }

    // ラベル種別指定情報 (1バイト)
    public class LabelTypeSpec
    {
        public byte Bits { get; set; }

        public bool GlobalLabel => (Bits & (1 << 0)) != 0;
        public bool LocalLabel => (Bits & (1 << 1)) != 0;
        public bool UnitLabel => (Bits & (1 << 2)) != 0;
        // b3-7: 空き

        public static LabelTypeSpec Parse(byte[] data, int offset) => new()
        {
            Bits = data[offset],
        };

        public override string ToString() =>
            $"グローバル={GlobalLabel} ローカル={LocalLabel} ユニット={UnitLabel}";
    }

    // SFC関連デバイス種別指定情報 (1バイト)
    public class SfcDeviceTypeSpec
    {
        public byte Bits { get; set; }

        public bool S_StepRelay => (Bits & (1 << 0)) != 0;  // 0:収集しない(固定)
        public bool BLnS_BlockStep => (Bits & (1 << 1)) != 0;
        public bool BL_SfcBlock => (Bits & (1 << 2)) != 0;
        public bool TR_SfcTransition => (Bits & (1 << 3)) != 0;
        // b4-7: 空き

        public static SfcDeviceTypeSpec Parse(byte[] data, int offset) => new()
        {
            Bits = data[offset],
        };

        public override string ToString() => $"0x{Bits:X2}";
    }

    // 安全デバイス種別指定情報 (4バイト=32ビット)
    public class SafetyDeviceTypeSpec
    {
        public uint Bits { get; set; }

        public bool SAX_SafeInput => (Bits & (1U << 0)) != 0;  // 0:収集しない(固定)
        public bool SAY_SafeOutput => (Bits & (1U << 1)) != 0;
        public bool SAM_SafeIntRelay => (Bits & (1U << 2)) != 0;
        public bool SAB_SafeLinkRelay => (Bits & (1U << 3)) != 0;
        public bool SAT_SafeTimer => (Bits & (1U << 4)) != 0;
        public bool SAST_SafeAccumTimer => (Bits & (1U << 5)) != 0;
        public bool SAC_SafeCounter => (Bits & (1U << 6)) != 0;
        public bool SAD_SafeDataReg => (Bits & (1U << 7)) != 0;
        public bool SAW_SafeLinkReg => (Bits & (1U << 8)) != 0;
        public bool SASM_SafeSpecRelay => (Bits & (1U << 9)) != 0;
        public bool SASD_SafeSpecReg => (Bits & (1U << 10)) != 0;
        public bool SAhM_SafeIntRelay => (Bits & (1U << 11)) != 0;
        public bool SAhT_SafeTimer => (Bits & (1U << 12)) != 0;
        public bool SAhST_SafeAccum => (Bits & (1U << 13)) != 0;
        public bool SAhC_SafeCounter => (Bits & (1U << 14)) != 0;
        public bool SAhD_SafeDataReg => (Bits & (1U << 15)) != 0;
        public bool SAhW_SafeLinkReg => (Bits & (1U << 16)) != 0;
        // b17-31: 空き

        public static SafetyDeviceTypeSpec Parse(byte[] data, int offset) => new()
        {
            Bits = BitConverter.ToUInt32(data, offset),
        };

        public override string ToString() => $"0x{Bits:X8}";
    }
}
