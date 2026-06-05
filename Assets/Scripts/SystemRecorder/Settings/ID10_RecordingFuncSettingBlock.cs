using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // 2.3.1. レコーディング機能設定ブロック(ID=10h) 2764バイト
    internal class ID10_RecordingFuncSettingBlock
    {
        // +00h 設定情報ヘッダ(レコーディング機能設定ブロック) 72バイト ※別途仕様参照
        public RecordingFuncSettingHeader SettingInfoHeader { get; set; } = new();

        // +48h 収集設定ブロック(ID=11h)          12バイト
        public ID11_CollectionSettingBlock CollectionBlock { get; set; } = new();

        // +54h レコーディング方式設定ブロック(ID=12h) 40バイト ※別途仕様参照
        public ID12_RecordingMethodSettingBlock RecordingMethodBlock { get; set; } = new();

        // +7Ch ファイル保存トリガ設定ブロック(ID=13h) 2476バイト
        public ID13_FileSaveTriggerBlock FileSaveTriggerBlock { get; set; } = new();

        // +A28h 保存パス設定ブロック(ID=14h)     164バイト
        public ID14_SavePathSettingBlock SavePathBlock { get; set; } = new();

        public static ID10_RecordingFuncSettingBlock Parse(byte[] data, int offset)
        {
            var ret = new ID10_RecordingFuncSettingBlock
            {
                SettingInfoHeader = RecordingFuncSettingHeader.Parse(data, offset)
            };
            ret.CollectionBlock = ID11_CollectionSettingBlock.Parse(data, offset + (int)ret.SettingInfoHeader.CollectionBlock.BlockOffset);
            ret.RecordingMethodBlock = ID12_RecordingMethodSettingBlock.Parse(data, offset + (int)ret.SettingInfoHeader.RecordingMethodBlock.BlockOffset);
            ret.FileSaveTriggerBlock = ID13_FileSaveTriggerBlock.Parse(data, offset + (int)ret.SettingInfoHeader.FileSaveTriggerBlock.BlockOffset);
            ret.SavePathBlock = ID14_SavePathSettingBlock.Parse(data, offset + (int)ret.SettingInfoHeader.SavePathBlock.BlockOffset);
            return ret;
        }
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== ID11 収集設定ブロック ===");
            sb.AppendLine(CollectionBlock.ToString());
            sb.AppendLine("=== ID12 収集設定ブロック ===");
            sb.AppendLine(RecordingMethodBlock.ToString());
            sb.AppendLine("=== ID13 ファイル保存トリガ設定ブロック ===");
            sb.AppendLine(FileSaveTriggerBlock.ToString());
            sb.AppendLine("=== ID14 保存パス設定ブロック ===");
            sb.AppendLine(SavePathBlock.ToString());
            return sb.ToString();
        }
    }
}
