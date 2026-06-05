using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    internal class SettingInfoHeader
    {
        public uint AreaTotalSize { get; set; }  // +00h 設定情報(全体)エリア全体サイズ 4バイト
        public uint BlockCount { get; set; }  // +04h 設定ブロック数                 4バイト (5〜8)

        // +08h〜 各設定ブロック情報 (各16バイト)
        public SettingBlockInfo RecordingFuncBlock { get; set; } = new();  // +08h レコーディング機能設定ブロック
        public SettingBlockInfo RestoreInfoCompressBlock { get; set; } = new();  // +18h 設定復元情報圧縮ブロック
        public SettingBlockInfo DeviceCodeBlock { get; set; } = new();  // +28h デバイスコードブロック
        public SettingBlockInfo FileListBlock { get; set; } = new();  // +38h ファイルリストブロック
        public SettingBlockInfo SaveDestinationBlock { get; set; } = new();  // +48h 保存先設定ブロック
        public SettingBlockInfo OfflineMonitorBlock { get; set; } = new();  // +58h オフラインモニタ機能設定ブロック
        public SettingBlockInfo VideoReceiveTargetBlock { get; set; } = new();  // +68h 動画データ受信対象設定ブロック
        public SettingBlockInfo RecorderVer07Block { get; set; } = new();  // +78h レコーダVer07カメレコVer05設定ブロック
        public SettingBlockInfo RecorderSfcCollectBlock { get; set; } = new();  // +88h レコーダ向けSFC収集設定ブロック
                                                                                // +98h 末尾

        public static SettingInfoHeader Parse(byte[] data, int offset)
        {
            var ret = new SettingInfoHeader
            {
                AreaTotalSize = BitConverter.ToUInt32(data, offset + 0x00),
                BlockCount = BitConverter.ToUInt32(data, offset + 0x04),
                RecordingFuncBlock = SettingBlockInfo.Parse(data, offset + 0x08),
                RestoreInfoCompressBlock = SettingBlockInfo.Parse(data, offset + 0x18),
                DeviceCodeBlock = SettingBlockInfo.Parse(data, offset + 0x28),
                FileListBlock = SettingBlockInfo.Parse(data, offset + 0x38),
                SaveDestinationBlock = SettingBlockInfo.Parse(data, offset + 0x48),
            };
            if (ret.BlockCount >= 6)
            {
                ret.OfflineMonitorBlock = SettingBlockInfo.Parse(data, offset + 0x58);
            }
            if (ret.BlockCount >= 7)
            {
                ret.VideoReceiveTargetBlock = SettingBlockInfo.Parse(data, offset + 0x68);
            }
            if (ret.BlockCount >= 8)
            {
                ret.RecorderVer07Block = SettingBlockInfo.Parse(data, offset + 0x78);
            }
            if (ret.BlockCount >= 9)
            {
                ret.RecorderSfcCollectBlock = SettingBlockInfo.Parse(data, offset + 0x88);
            }
            return ret;
        }

        /*
        public override string ToString()
        {
            return $"""
            ****************************************
            エリア全体サイズ                        : {AreaTotalSize}
            設定ブロック数                          : {BlockCount}
            レコーディング機能設定ブロック          : {RecordingFuncBlock}
            設定復元情報圧縮ブロック                : {RestoreInfoCompressBlock}
            デバイスコードブロック                  : {(DeviceCodeBlock.Exists ? DeviceCodeBlock.ToString() : "未生成")}
            ファイルリストブロック                  : {(FileListBlock.Exists ? FileListBlock.ToString() : "未生成")}
            保存先設定ブロック                      : {SaveDestinationBlock}
            オフラインモニタ機能設定ブロック        : {(BlockCount >= 6 ? OfflineMonitorBlock : "未精製")}
            動画データ受信対象設定ブロック          : {(BlockCount >= 7 ? VideoReceiveTargetBlock : "未精製")}
            レコーダVer07カメレコVer05設定ブロック  : {(BlockCount >= 8 && RecorderVer07Block.Exists ? RecorderVer07Block.ToString() : "未生成")}
            レコーダ向けSFC収集設定ブロック         : {(BlockCount >= 9 && RecorderSfcCollectBlock.Exists ? RecorderSfcCollectBlock.ToString() : "未生成")}
            """;
        }
        */
    }
}
