using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // =====================================================
    // 2.7.3 ファイルタイプ2ブロック (ID=F2)
    // =====================================================
    internal class IDF2_FileType2Block
    {
        public uint AreaTotalSize { get; set; }  // +0h, 4byte
        public uint FileInfoOffset { get; set; }  // +4h, 4byte
        public ushort ExtensionCount { get; set; }  // +8h, 2byte (0～7)

        /// <summary>拡張子情報(タイプ2)配列 +Ah～ (ExtensionCount個, 各130byte)</summary>
        public List<ExtensionInfoType2> Extensions { get; set; } = new();

        /// <summary>ファイル情報(タイプ2)配列 (各128byte)</summary>
        public List<FileInfoType2> Files { get; set; } = new();

        // 境界調整領域は読み飛ばし（0固定パディング）

        private int TotalFileCount => Extensions.Sum(e => e.FileCount);

        public static IDF2_FileType2Block Parse(ReadOnlySpan<byte> data, int baseOffset = 0)
        {
            if (data.Length < baseOffset + 10)
                throw new ArgumentException("データが不足しています。");

            var block = new IDF2_FileType2Block
            {
                AreaTotalSize = BitConverter.ToUInt32(data.Slice(baseOffset + 0x00, 4)),
                FileInfoOffset = BitConverter.ToUInt32(data.Slice(baseOffset + 0x04, 4)),
                ExtensionCount = BitConverter.ToUInt16(data.Slice(baseOffset + 0x08, 2)),
            };

            // 拡張子情報(タイプ2) +Ah～
            int offset = baseOffset + 0x0A;
            for (int i = 0; i < block.ExtensionCount; i++)
            {
                var extension = ExtensionInfoType2.Parse(data, offset);
                block.Extensions.Add(extension);
                offset += extension.Size;
            }

            // ファイル情報(タイプ2) FileInfoOffsetが示すアドレスから
            int fileAreaStart = baseOffset + (int)block.FileInfoOffset;
            int totalFiles = block.TotalFileCount;
            offset = fileAreaStart;
            for (int i = 0; i < totalFiles; i++)
            {
                var fileInfo = FileInfoType2.Parse(data, offset);
                block.Files.Add(fileInfo);
                offset += fileInfo.Size;
            }

            return block;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== ファイルタイプ2ブロック (ID=F2) ===");
            sb.AppendLine($"エリア全体サイズ            : {AreaTotalSize} byte");
            sb.AppendLine($"ファイル情報(タイプ2)オフセット: 0x{FileInfoOffset:X8}");
            sb.AppendLine($"拡張子数(タイプ2)           : {ExtensionCount}");
            sb.AppendLine();

            for (int i = 0; i < Extensions.Count; i++)
            {
                sb.AppendLine($"  [拡張子情報 {i}]");
                sb.Append(Extensions[i]);
            }

            sb.AppendLine();
            for (int i = 0; i < Files.Count; i++)
            {
                sb.AppendLine($"  [ファイル情報 {i}]");
                sb.Append(Files[i]);
            }

            return sb.ToString();
        }
    }

    // =====================================================
    // 2.7.3.1 拡張子情報（タイプ2）130byte
    // =====================================================
    public class ExtensionInfoType2
    {
        /// <summary>ファイル数 +0h, 2byte</summary>
        public ushort FileCount { get; set; }

        /// <summary>拡張子該当ファイル情報オフセット +2h, 4byte（ファイル情報(タイプ2)エリア先頭からのオフセット）</summary>
        public uint FileInfoOffset { get; set; }

        /// <summary>拡張子名長 +6h, 2byte（Unicode文字数、EOS含む、2～61）</summary>
        public ushort ExtensionNameLength { get; set; }

        /// <summary>拡張子名 +8h, 122byte（Unicode、EOS付き、ピリオド含まず）</summary>
        public string ExtensionName { get; set; } = string.Empty;

        public int charCount;

        public int Size
        {
            get
            {
                return 0x08 + charCount * 2;
            }
        }

        public static ExtensionInfoType2 Parse(ReadOnlySpan<byte> data, int offset)
        {
            var info = new ExtensionInfoType2
            {
                FileCount = BitConverter.ToUInt16(data.Slice(offset + 0x00, 2)),
                FileInfoOffset = BitConverter.ToUInt32(data.Slice(offset + 0x02, 4)),
                ExtensionNameLength = BitConverter.ToUInt16(data.Slice(offset + 0x06, 2)),
            };

            info.charCount = Math.Min((int)info.ExtensionNameLength, 61);
            info.ExtensionName = System.Text.Encoding.Unicode
                .GetString(data.Slice(offset + 0x08, info.charCount * 2))
                .TrimEnd('\0');

            return info;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"    ファイル数                    : {FileCount}");
            sb.AppendLine($"    拡張子該当ファイル情報オフセット: 0x{FileInfoOffset:X8}");
            sb.AppendLine($"    拡張子名長                    : {ExtensionNameLength}");
            sb.AppendLine($"    拡張子名                      : {ExtensionName}");
            return sb.ToString();
        }
    }

    // =====================================================
    // 2.7.3.1 ファイル情報（タイプ2）128byte
    // =====================================================
    public class FileInfoType2
    {
        /// <summary>I/O No. +0h, 2byte（CPU:3E00h～3E03h、CPU以外:0000h～0040h）</summary>
        public ushort IoNo { get; set; }

        /// <summary>ドライブNo. +2h, 2byte（SD:0002h、ファイル格納:0003h、データメモリ:0004h、CPU以外:FFFFh）</summary>
        public ushort DriveNo { get; set; }

        /// <summary>ファイル名長 +4h, 2byte（Unicode文字数、EOS含む、2～61）</summary>
        public ushort FileNameLength { get; set; }

        /// <summary>ファイル名 +6h, 122byte（Unicode、EOS付き、ピリオド・拡張子含まず）</summary>
        public string FileName { get; set; } = string.Empty;

        public int charCount;
        public int Size
        {
            get
            {
                return 0x06 + charCount * 2;
            }
        }

        public static FileInfoType2 Parse(ReadOnlySpan<byte> data, int offset)
        {
            var info = new FileInfoType2
            {
                IoNo = BitConverter.ToUInt16(data.Slice(offset + 0x00, 2)),
                DriveNo = BitConverter.ToUInt16(data.Slice(offset + 0x02, 2)),
                FileNameLength = BitConverter.ToUInt16(data.Slice(offset + 0x04, 2)),
            };

            info.charCount = Math.Min((int)info.FileNameLength, 61);
            info.FileName = System.Text.Encoding.Unicode
                .GetString(data.Slice(offset + 0x06, info.charCount * 2))
                .TrimEnd('\0');

            return info;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"    I/O No.   : 0x{IoNo:X4}");
            sb.AppendLine($"    ドライブNo.: 0x{DriveNo:X4}");
            sb.AppendLine($"    ファイル名長: {FileNameLength}");
            sb.AppendLine($"    ファイル名  : {FileName}");
            return sb.ToString();
        }
    }
}