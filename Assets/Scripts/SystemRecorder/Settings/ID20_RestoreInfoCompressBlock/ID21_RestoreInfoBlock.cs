using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // 設定復元情報ブロック(ID=21h) 内の各サブブロック
    internal class IDD1_RestoreInfoBlock
    {
        public RestoreInfoSettingHeader? SettingHeader { get; set; }  // +00h    設定情報ヘッダ(設定復元情報ブロック) 152バイト
        public ID22_ProgramSpecifyBlock? ProgramSpecifyBlock { get; set; }  // +98h    プログラム指定ブロック(ID=22h)      528バイト(可変)
        public ID23_UnitSpecifyBlock? UnitSpecifyBlock { get; set; }  // +2A8h   ユニット指定ブロック(ID=23h)        278バイト(可変)
        public ID24_IndividualDeviceSpecBlock? IndividualDeviceSpecBlock { get; set; }  // +3BEh   個別デバイス指定ブロック(ID=24h)    1048568バイト(可変)
        public ID25_BulkSpecifyOptionBlock? BulkSpecifyOptionBlock { get; set; }  // +1003B6h 一括指定オプションブロック(ID=25h)  8バイト
        public ID26_DeviceLabelListBlock? DeviceLabelListBlock { get; set; }  // +1003BEh デバイスラベル一覧指定ブロック(ID=26h) 140094バイト(可変)
        public ID27_DeviceLabelExtBlock? DeviceLabelExtBlock { get; set; }  // +1226FCh デバイスラベル一覧拡張指定ブロック(ID=27h) 5768バイト
        public ID28_LastDeviceInfoBlock? LastDeviceInfoBlock { get; set; }  // +123D84h 最終デバイス情報ブロック(ID=28h)   24968バイト
        public ID29_SfcDeviceBulkSpecBlock? SfcDeviceBulkSpecBlock { get; set; }  // +129F0Ch SFCデバイス一括指定ブロック(ID=29h) 8バイト
        public ID2A_IndividualDeviceInfoBlock? IndividualDeviceInfoBlock { get; set; }  // +129F14h 個別デバイス情報ブロック(ID=2Ah)   8バイト

        public static IDD1_RestoreInfoBlock Parse(byte[] data, int offset) {

            var block = new IDD1_RestoreInfoBlock
            {
                SettingHeader = RestoreInfoSettingHeader.Parse(data, offset + 0x000000),
            };
            if (block.SettingHeader.ProgramSpecifyBlock.Exists)
            {
                block.ProgramSpecifyBlock = ID22_ProgramSpecifyBlock.Parse(data, (int)block.SettingHeader.ProgramSpecifyBlock.BlockOffset);
            }
            if (block.SettingHeader.UnitSpecifyBlock.Exists)
            {
                block.UnitSpecifyBlock = ID23_UnitSpecifyBlock.Parse(data, (int)block.SettingHeader.UnitSpecifyBlock.BlockOffset);
            }
            if (block.SettingHeader.IndividualDeviceSpecBlock.Exists)
            {
                block.IndividualDeviceSpecBlock = ID24_IndividualDeviceSpecBlock.Parse(data, (int)block.SettingHeader.IndividualDeviceSpecBlock.BlockOffset);
            }
            if (block.SettingHeader.BulkSpecifyOptionBlock.Exists)
            {
                block.BulkSpecifyOptionBlock = ID25_BulkSpecifyOptionBlock.Parse(data, (int)block.SettingHeader.BulkSpecifyOptionBlock.BlockOffset);
            }
            if (block.SettingHeader.DeviceLabelListBlock.Exists)
            {
                block.DeviceLabelListBlock = ID26_DeviceLabelListBlock.Parse(data, (int)block.SettingHeader.DeviceLabelListBlock.BlockOffset);
            }
            if (block.SettingHeader.DeviceLabelExtBlock.Exists)
            {
                block.DeviceLabelExtBlock = ID27_DeviceLabelExtBlock.Parse(data, (int)block.SettingHeader.DeviceLabelExtBlock.BlockOffset);
            }
            if (block.SettingHeader.LastDeviceInfoBlock.Exists)
            {
                block.LastDeviceInfoBlock = ID28_LastDeviceInfoBlock.Parse(data, (int)block.SettingHeader.LastDeviceInfoBlock.BlockOffset);
            }
            if (block.SettingHeader.SfcDeviceBulkSpecBlock.Exists)
            {
                block.SfcDeviceBulkSpecBlock = ID29_SfcDeviceBulkSpecBlock.Parse(data, (int)block.SettingHeader.SfcDeviceBulkSpecBlock.BlockOffset);
            }
            if (block.SettingHeader.IndividualDeviceInfoBlock.Exists)
            {
                block.IndividualDeviceInfoBlock = ID2A_IndividualDeviceInfoBlock.Parse(data, (int)block.SettingHeader.IndividualDeviceInfoBlock.BlockOffset);
            }
            return block;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("===  ID21 設定情報ヘッダ(設定復元情報ブロック) ===");
            sb.AppendLine(SettingHeader.ToString());
            if (ProgramSpecifyBlock != null)
            {
                sb.AppendLine("===  ID22 プログラム指定ブロック ===");
                sb.AppendLine(ProgramSpecifyBlock.ToString());
            }
            if (UnitSpecifyBlock != null)
            {
                sb.AppendLine("===  ID23 ユニット指定ブロック ===");
                sb.AppendLine(UnitSpecifyBlock.ToString());
            }
            if (IndividualDeviceSpecBlock != null)
            {
                sb.AppendLine("===  ID24 個別デバイス指定ブロック ===");
                sb.AppendLine(IndividualDeviceSpecBlock.ToString());
            }
            if (BulkSpecifyOptionBlock != null)
            {
                sb.AppendLine("===  ID25 一括指定オプションブロック ===");
                sb.AppendLine(BulkSpecifyOptionBlock.ToString());
            }
            if (DeviceLabelListBlock != null)
            {
                sb.AppendLine("===  ID26 デバイスラベル一覧指定ブロック ===");
                sb.AppendLine(DeviceLabelListBlock.ToString());
            }
            if (DeviceLabelExtBlock != null)
            {
                sb.AppendLine("===  ID27 デバイスラベル一覧拡張指定ブロック ===");
                sb.AppendLine(DeviceLabelExtBlock.ToString());
            }
            if (LastDeviceInfoBlock != null)
            {
                sb.AppendLine("===  ID28 最終デバイス情報ブロック ===");
                sb.AppendLine(LastDeviceInfoBlock.ToString());
            }
            if (SfcDeviceBulkSpecBlock != null)
            {
                sb.AppendLine("===  ID29 SFCデバイス一括指定ブロック ===");
                sb.AppendLine(SfcDeviceBulkSpecBlock.ToString());
            }
            if (IndividualDeviceInfoBlock != null)
            {
                sb.AppendLine("===  ID2A 個別デバイス情報ブロック ===");
                sb.AppendLine(IndividualDeviceInfoBlock.ToString());
            }
            return sb.ToString();
        }
    }
}
