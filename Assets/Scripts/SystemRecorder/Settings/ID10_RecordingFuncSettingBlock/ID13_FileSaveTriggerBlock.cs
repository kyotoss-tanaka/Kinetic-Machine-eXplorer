using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // 2.3.4. ファイル保存トリガ設定ブロック(ID=13h)
    internal class ID13_FileSaveTriggerBlock
    {
        public uint AreaTotalSize { get; set; }  // +00h エリア全体サイズ  4バイト
        public ushort TriggerCount { get; set; }  // +04h ファイル保存トリガ数 2バイト (0〜16)

        // +06h ファイル保存トリガ情報[n] 416バイト×n (可変)
        public List<FileSaveTriggerInfo> Triggers { get; set; } = new();

        // +1A6h トリガコメント 2048バイト (64文字×16、Unicode)
        public List<string> TriggerComments { get; set; } = new();

        // +9A6h 設定ブロック有無 4バイト
        public uint BlockPresence { get; set; }

        // 設定ブロック有無フラグ
        public bool IsValid => (BlockPresence & (1 << 0)) != 0;  // b0 有効無効フラグ
        public bool HasRecordingFuncBlock => (BlockPresence & (1 << 1)) != 0;  // b1 固定:有
        public bool HasFileListBlock => (BlockPresence & (1 << 2)) != 0;  // b2 固定:有
        public bool HasSaveDestinationBlock => (BlockPresence & (1 << 3)) != 0;  // b3
        public bool HasVideoRecordingBlock => (BlockPresence & (1 << 4)) != 0;  // b4
        public bool HasRecorderVer07Block => (BlockPresence & (1 << 5)) != 0;  // b5
        public bool HasRecorderSfcCollectBlock => (BlockPresence & (1 << 6)) != 0;  // b6


        public static ID13_FileSaveTriggerBlock Parse(byte[] data, int offset)
        {
            var block = new ID13_FileSaveTriggerBlock
            {
                AreaTotalSize = BitConverter.ToUInt32(data, offset + 0x00),
                TriggerCount = BitConverter.ToUInt16(data, offset + 0x04),
            };

            // トリガ情報[n] +06h〜
            for (int i = 0; i < block.TriggerCount; i++)
                block.Triggers.Add(FileSaveTriggerInfo.Parse(data, offset + 0x06 + i * 0x1A));

            // トリガコメント
            var end = 0;
            for (int i = 0; i < block.TriggerCount; i++)
            {
                string comment = Encoding.Unicode.GetString(data, offset + (int)block.Triggers[i].CommentOffset, block.Triggers[i].CommentCharCount * 2).TrimEnd('\0');
                block.TriggerComments.Add(comment);
                if (end < block.Triggers[i].CommentOffset + block.Triggers[i].CommentCharCount * 2)
                {
                    end = (int)(block.Triggers[i].CommentOffset + block.Triggers[i].CommentCharCount * 2);
                }
            }

            // 設定ブロック
            block.BlockPresence = BitConverter.ToUInt32(data, offset + end);
            return block;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"ファイル保存トリガ数: {TriggerCount}");
            for (int i = 0; i < Triggers.Count; i++)
                sb.AppendLine($"  トリガ[{i}]: {Triggers[i]}  コメント={TriggerComments[i]}");
            sb.AppendLine($"設定ブロック有無              : 0x{BlockPresence:X8}");
            sb.AppendLine($"  有効フラグ                    : {IsValid}");
            sb.AppendLine($"  レコーディング機能設定ブロック: {HasRecordingFuncBlock}");
            sb.AppendLine($"  ファイルリストブロック        : {HasFileListBlock}");
            sb.AppendLine($"  保存先設定ブロック            : {HasSaveDestinationBlock}");
            sb.AppendLine($"  動画データ録画               : {HasVideoRecordingBlock}");
            sb.AppendLine($"  レコーダVer07設定ブロック     : {HasRecorderVer07Block}");
            sb.AppendLine($"  SFC収集設定ブロック           : {HasRecorderSfcCollectBlock}");
            return sb.ToString();
        }
    }

    // 2.3.4.1. ファイル保存トリガ情報 26バイト
    public class FileSaveTriggerInfo
    {
        public ushort TriggerNo { get; set; }  // +00h ファイル保存トリガNo.       2バイト (1〜16)
        public ushort TriggerCondition { get; set; }  // +02h ファイル保存トリガ条件      2バイト
        public byte[] ModifyDeviceCode { get; set; }  // +04h 修飾デバイスコード(表示用) 16バイト
        public ushort CommentCharCount { get; set; }  // +14h トリガコメント文字数        2バイト (0〜64)
        public uint CommentOffset { get; set; }  // +16h トリガコメントオフセット     4バイト
                                                 // +1Ah 末尾

        public string TriggerConditionName => TriggerCondition switch
        {
            0x00 => "立上がり(↑)",
            0x01 => "立下がり(↓)",
            _ => $"不明(0x{TriggerCondition:X2})"
        };

        public static FileSaveTriggerInfo Parse(byte[] data, int offset) => new()
        {
            TriggerNo = BitConverter.ToUInt16(data, offset + 0x00),
            TriggerCondition = BitConverter.ToUInt16(data, offset + 0x02),
            ModifyDeviceCode = data[(offset + 0x04)..(offset + 0x14)],
            CommentCharCount = BitConverter.ToUInt16(data, offset + 0x14),
            CommentOffset = BitConverter.ToUInt32(data, offset + 0x16),
        };

        // トリガコメントを取得 (ブロック先頭からのオフセットを使用)
        public string GetComment(byte[] blockData, int blockOffset)
        {
            if (CommentCharCount == 0) return string.Empty;
            // CommentOffsetはファイル保存トリガ設定ブロック先頭からのオフセット
            return Encoding.Unicode.GetString(blockData, blockOffset + (int)CommentOffset, CommentCharCount * 2);
        }

        public override string ToString() =>
            $"No={TriggerNo} 条件={TriggerConditionName} コメント文字数={CommentCharCount}";
    }
}
