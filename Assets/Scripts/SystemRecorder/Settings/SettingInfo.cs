using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // 1.4. 設定情報（全体）
    internal class SettingInfo
    {
        // (1) 設定ファイルヘッダ
        public SettingInfoHeader? SettingInfoHeader { get; set; } = new();

        // (2) レコーディング機能設定ブロック(ID=10h
        public ID10_RecordingFuncSettingBlock RecordingFuncSettingBlock { get; set; } = new(); 

        // (3) 設定復元情報圧縮ブロック(ID=20h)
        public ID20_RestoreInfoCompressBlock RestoreInfoCompressBlock { get; set; } = new();

        // (4) デバイスコードブロック(ID=D0h) ※別途仕様参照
        public IDD0_DeviceCodeBlock DeviceCodeBlock { get; set; } = new();

        // (5) ファイルリストブロック(ID=F0h) ※別途仕様参照
        public IDF0_FileListBlock FileListBlock { get; set; } = new();

        public static SettingInfo Parse(byte[] data, int offset)
        {
            var file = new SettingInfo();

            // (1) ヘッダ
            file.SettingInfoHeader = SettingInfoHeader.Parse(data, offset);

            // (2) レコーディング機能設定ブロック
            file.RecordingFuncSettingBlock = ID10_RecordingFuncSettingBlock.Parse(data, offset + (int)file.SettingInfoHeader.RecordingFuncBlock.BlockOffset);

            // (3) 設定復元情報圧縮ブロック
            file.RestoreInfoCompressBlock = ID20_RestoreInfoCompressBlock.Parse(data, offset + (int)file.SettingInfoHeader.RestoreInfoCompressBlock.BlockOffset);

            // (4) デバイスコードブロック
            file.DeviceCodeBlock = IDD0_DeviceCodeBlock.Parse(data, offset + (int)file.SettingInfoHeader.DeviceCodeBlock.BlockOffset);

            // (5) ファイルリストブロック
            file.FileListBlock = IDF0_FileListBlock.Parse(data, offset + (int)file.SettingInfoHeader.FileListBlock.BlockOffset);
            return file;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine(SettingInfoHeader.ToString());
            sb.AppendLine("****************************************");
            sb.AppendLine("=== ID10 レコーディング機能設定ブロック ===");
            sb.AppendLine(RecordingFuncSettingBlock.ToString());
            sb.AppendLine("=== ID20 設定復元情報圧縮ブロック ===");
            sb.AppendLine(RestoreInfoCompressBlock.ToString());
            sb.AppendLine("=== IDD0 デバイスコードブロック ===");
            sb.AppendLine(DeviceCodeBlock.ToString());
            sb.AppendLine("=== IDF0 ファイルリストブロック ===");
            sb.AppendLine(FileListBlock.ToString());
            return sb.ToString();
        }
    }
}
