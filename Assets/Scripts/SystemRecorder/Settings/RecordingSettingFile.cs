using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    internal class RecordingSettingFile
    {
        public SettingHeader SettingHeader { get; set; } = new();      // (1) 設定ファイルヘッダ

        public SettingFileInfo SettingFileInfo { get; set; } = new();  // (2) 設定ファイル情報エリア

        public SettingInfo SettingInfo { get; set; } = new();          // (3) 設定情報エリア

        public static RecordingSettingFile Parse(byte[] data)
        {
            int pos = 0;
            var file = new RecordingSettingFile();

            // (1) ヘッダ
            file.SettingHeader = SettingHeader.Parse(data);
            pos += file.SettingHeader.HeaderSize;

            // (2) 設定ファイル情報エリア
            file.SettingFileInfo = SettingFileInfo.Parse(data, pos);
            pos += (int)file.SettingFileInfo.AreaTotalSize;

            // (3) 設定情報エリア
            file.SettingInfo = SettingInfo.Parse(data, pos);

            return file;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine(SettingHeader.ToString());
            sb.AppendLine(SettingFileInfo.ToString());
            sb.AppendLine(SettingInfo.ToString());
            return sb.ToString();
        }
    }
}
