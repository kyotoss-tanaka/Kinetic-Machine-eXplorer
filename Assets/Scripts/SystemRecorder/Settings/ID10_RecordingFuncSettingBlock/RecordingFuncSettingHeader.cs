using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    internal class RecordingFuncSettingHeader
    {
        public uint AreaTotalSize { get; set; }  // +00h エリア全体サイズ              4バイト
        public uint BlockCount { get; set; }  // +04h 設定ブロック数                4バイト
        public SettingBlockInfo CollectionBlock { get; set; } = new(); // +08h 設定ブロック情報(収集設定ブロック)         16バイト
        public SettingBlockInfo RecordingMethodBlock { get; set; } = new();  // +18h 設定ブロック情報(レコーディング方式設定ブロック) 16バイト
        public SettingBlockInfo FileSaveTriggerBlock { get; set; } = new();  // +28h 設定ブロック情報(ファイル保存トリガ設定ブロック) 16バイト
        public SettingBlockInfo SavePathBlock { get; set; } = new();  // +38h 設定ブロック情報(保存パス設定ブロック)     16バイト
                                                                      // +48h 末尾

        public static RecordingFuncSettingHeader Parse(byte[] data, int offset) => new()
        {
            AreaTotalSize = BitConverter.ToUInt32(data, offset + 0x00),
            BlockCount = BitConverter.ToUInt32(data, offset + 0x04),
            CollectionBlock = SettingBlockInfo.Parse(data, offset + 0x08),
            RecordingMethodBlock = SettingBlockInfo.Parse(data, offset + 0x18),
            FileSaveTriggerBlock = SettingBlockInfo.Parse(data, offset + 0x28),
            SavePathBlock = SettingBlockInfo.Parse(data, offset + 0x38),
        };

        /*
        public override string ToString()
        {
            return $"""
        エリア全体サイズ                        : {AreaTotalSize}
        設定ブロック数                          : {BlockCount}
        収集設定ブロック                        : {CollectionBlock}
        レコーディング方式設定ブロック          : {RecordingMethodBlock}
        ファイル保存トリガ設定ブロック          : {FileSaveTriggerBlock}
        保存パス設定ブロック                    : {SavePathBlock}
        """;
        }
        */
    }
}
