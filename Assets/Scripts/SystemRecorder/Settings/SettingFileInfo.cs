using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    internal class SettingFileInfo
    {
        public uint AreaTotalSize { get; set; }  // +00h ファイル情報エリア全体サイズ 4バイト
        public ushort FileVersion { get; set; }  // +04h ファイルバージョン           2バイト
        public MainUnitInfo MainUnit { get; set; } = new();  // +06h 対象メインユニット情報  可変(10バイト)
        public MainUnitExpandInfo ExpandInfo { get; set; } = new();  // +10h 対象メインユニット展開情報 可変(26バイト)
                                                                     // +42h 末尾

        public static SettingFileInfo Parse(byte[] data, int offset)
        {
            return new SettingFileInfo
            {
                AreaTotalSize = BitConverter.ToUInt32(data, offset + 0x00),
                FileVersion = BitConverter.ToUInt16(data, offset + 0x04),
                MainUnit = MainUnitInfo.Parse(data, offset + 0x06),
                ExpandInfo = MainUnitExpandInfo.Parse(data, offset + 0x10),
            };
        }

        /*
        public override string ToString()
        {
            return $"""
            ****************************************
            エリア全体サイズ : {AreaTotalSize}
            ファイルバージョン: 0x{FileVersion:X4}
            メインユニット   : {MainUnit}
            ****************************************
            メインユニット展開情報サイズ   : {ExpandInfo.DataSize}
            展開ブロック数   : {ExpandInfo.ExpandBlockCount}
            レコーディング機能設定ブロック: offset={ExpandInfo.RecordingFuncBlockOffset} size={ExpandInfo.RecordingFuncBlockSize}
            ファイルリストブロック        : offset={ExpandInfo.FileListBlockOffset} size={ExpandInfo.FileListBlockSize}
            保存先設定ブロック            : offset={ExpandInfo.SaveDestinationBlockOffset} size={ExpandInfo.SaveDestinationBlockSize}
            動画データ受信対象設定ブロック        : offset={ExpandInfo.VideoReceiveTargetBlockOffset} size={ExpandInfo.VideoReceiveTargetBlockSize}
            レコーダVer07カメレコVer05設定ブロック: offset={ExpandInfo.RecorderVer07BlockOffset} size={ExpandInfo.RecorderVer07BlockSize}
            レコーダ向けSFC収集設定ブロック       : offset={ExpandInfo.RecorderSfcCollectBlockOffset} size={ExpandInfo.RecorderSfcCollectBlockSize}
            """;
        }
        */
    }
}
