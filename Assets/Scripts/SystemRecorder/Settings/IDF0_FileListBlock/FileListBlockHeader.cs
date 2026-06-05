using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    internal class FileListBlockHeader
    {
        public uint AreaTotalSize { get; set; }  // +0h,  4byte
        public uint ConfigBlockCount { get; set; }  // +4h,  4byte (固定値:2)

        /// <summary>設定ブロック情報（ファイルタイプ1ブロック）+8h, 16byte</summary>
        public SettingBlockInfo FileType1BlockInfo { get; set; } = new();

        /// <summary>設定ブロック情報（ファイルタイプ2ブロック）+18h, 16byte</summary>
        public SettingBlockInfo FileType2BlockInfo { get; set; } = new();

        public const int HeaderSize = 40;

        public static FileListBlockHeader Parse(byte[] data, int offset)
        {
            if (data.Length < offset + HeaderSize)
                throw new ArgumentException(
                    $"データが不足しています。必要: {offset + HeaderSize} byte, 実際: {data.Length} byte");

            return new FileListBlockHeader
            {
                AreaTotalSize = BitConverter.ToUInt32(data, offset + 0x00),
                ConfigBlockCount = BitConverter.ToUInt32(data, offset + 0x04),
                FileType1BlockInfo = SettingBlockInfo.Parse(data, offset + 0x08),
                FileType2BlockInfo = SettingBlockInfo.Parse(data, offset + 0x18),
            };
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== ファイルリストブロック ヘッダ (ID=F0) ===");
            sb.AppendLine($"エリア全体サイズ: {AreaTotalSize} byte (0x{AreaTotalSize:X8})");
            sb.AppendLine($"設定ブロック数  : {ConfigBlockCount}");
            sb.AppendLine();
            sb.AppendLine("--- 設定ブロック情報（ファイルタイプ1ブロック）+8h ---");
            sb.Append(FileType1BlockInfo);
            sb.AppendLine();
            sb.AppendLine("--- 設定ブロック情報（ファイルタイプ2ブロック）+18h ---");
            sb.Append(FileType2BlockInfo);
            return sb.ToString();
        }
    }
}
