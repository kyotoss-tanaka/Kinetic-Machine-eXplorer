using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    internal class MainUnitExpandInfo
    {
        public ushort ExpandBlockCount { get; set; }  // +00h 展開ブロック数                          2バイト
        public uint RecordingFuncBlockOffset { get; set; }  // +02h レコーディング機能設定ブロックオフセット 4バイト
        public uint RecordingFuncBlockSize { get; set; }  // +06h レコーディング機能設定ブロックサイズ    4バイト
        public uint FileListBlockOffset { get; set; }  // +0Ah ファイルリストブロックオフセット        4バイト
        public uint FileListBlockSize { get; set; }  // +0Eh ファイルリストブロックサイズ            4バイト
        public uint SaveDestinationBlockOffset { get; set; }  // +12h 保存先設定ブロックオフセット            4バイト
        public uint SaveDestinationBlockSize { get; set; }  // +16h 保存先設定ブロックサイズ               4バイト
        public uint VideoReceiveTargetBlockOffset { get; set; }  // +1Ah 動画データ受信対象設定ブロックオフセット 4バイト
        public uint VideoReceiveTargetBlockSize { get; set; }  // +1Eh 動画データ受信対象設定ブロックサイズ   4バイト
        public uint RecorderVer07BlockOffset { get; set; }  // +22h レコーダVer07カメレコVer05設定ブロックオフセット 4バイト
        public uint RecorderVer07BlockSize { get; set; }  // +26h レコーダVer07カメレコVer05設定ブロックサイズ    4バイト
        public uint RecorderSfcCollectBlockOffset { get; set; }  // +2Ah レコーダ向けSFC収集設定ブロックオフセット       4バイト
        public uint RecorderSfcCollectBlockSize { get; set; }  // +2Eh レコーダ向けSFC収集設定ブロックサイズ          4バイト
                                                               // +32h 末尾 (50バイト)
        public int DataSize
        {
            get
            {
                if (ExpandBlockCount == 3)
                {
                    return 0x1A;
                }
                else if (ExpandBlockCount == 4)
                {
                    return 0x22;
                }
                else if (ExpandBlockCount == 5)
                {
                    return 0x2A;
                }
                else if (ExpandBlockCount == 6)
                {
                    return 0x32;
                }
                else
                {
                    return 0x12;
                }
            }
        }

        public static MainUnitExpandInfo Parse(byte[] data, int offset)
        {
            var ret =  new MainUnitExpandInfo
            {
                ExpandBlockCount = BitConverter.ToUInt16(data, offset + 0x00),
                RecordingFuncBlockOffset = BitConverter.ToUInt32(data, offset + 0x02),
                RecordingFuncBlockSize = BitConverter.ToUInt32(data, offset + 0x06),
                FileListBlockOffset = BitConverter.ToUInt32(data, offset + 0x0A),
                FileListBlockSize = BitConverter.ToUInt32(data, offset + 0x0E),
            };
            if (ret.ExpandBlockCount >= 3)
            {
                ret.SaveDestinationBlockOffset = BitConverter.ToUInt32(data, offset + 0x12);
                ret.SaveDestinationBlockSize = BitConverter.ToUInt32(data, offset + 0x16);
            }
            if (ret.ExpandBlockCount >= 4)
            {
                ret.VideoReceiveTargetBlockOffset = BitConverter.ToUInt32(data, offset + 0x1A);
                ret.VideoReceiveTargetBlockSize = BitConverter.ToUInt32(data, offset + 0x1E);
            }

            if (ret.ExpandBlockCount >= 5)
            {
                ret.RecorderVer07BlockOffset = BitConverter.ToUInt32(data, offset + 0x22);
                ret.RecorderVer07BlockSize = BitConverter.ToUInt32(data, offset + 0x26);
            }
            if (ret.ExpandBlockCount >= 6)
            {
                ret.RecorderSfcCollectBlockOffset = BitConverter.ToUInt32(data, offset + 0x2A);
                ret.RecorderSfcCollectBlockSize = BitConverter.ToUInt32(data, offset + 0x2E);
            }
            return ret;
        }
    }
}
