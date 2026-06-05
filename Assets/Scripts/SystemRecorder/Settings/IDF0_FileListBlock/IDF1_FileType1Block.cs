using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // =====================================================
    // 2.7.2 ファイルタイプ1ブロック (ID=F1)
    // =====================================================
    internal class IDF1_FileType1Block
    {
        public uint AreaTotalSize { get; set; }  // +0h,  4byte
        public uint FileInfoOffset { get; set; }  // +4h,  4byte
        public ushort ExtensionCount { get; set; }  // +8h,  2byte (0～4)

        /// <summary>拡張子情報(タイプ1)配列 +Ah～ (ExtensionCount個, 各130byte)</summary>
        public List<ExtensionInfoType1> Extensions { get; set; } = new();

        /// <summary>ファイル情報(タイプ1)配列 +212h～ (各136byte)</summary>
        public List<FileInfoType1> Files { get; set; } = new();

        // 境界調整領域は読み飛ばし（0固定パディング）

        /// <summary>拡張子情報の合計ファイル数から自動算出</summary>
        private int TotalFileCount => Extensions.Sum(e => e.FileCount);

        public static IDF1_FileType1Block Parse(byte[] data, int baseOffset = 0)
        {
            if (data.Length < baseOffset + 10)
                throw new ArgumentException("データが不足しています。");

            var block = new IDF1_FileType1Block
            {
                AreaTotalSize = BitConverter.ToUInt32(data, baseOffset + 0x00),
                FileInfoOffset = BitConverter.ToUInt32(data, baseOffset + 0x04),
                ExtensionCount = BitConverter.ToUInt16(data, baseOffset + 0x08),
            };

            // 拡張子情報(タイプ1) +Ah～
            int offset = baseOffset + 0x0A;
            for (int i = 0; i < block.ExtensionCount; i++)
            {
                var extension = ExtensionInfoType1.Parse(data, offset);
                block.Extensions.Add(extension);
                offset += extension.Size;
            }

            // ファイル情報(タイプ1) FileInfoOffsetが示すアドレスから
            int fileAreaStart = baseOffset + (int)block.FileInfoOffset;
            int totalFiles = block.TotalFileCount;
            offset = fileAreaStart;
            for (int i = 0; i < totalFiles; i++)
            {
                var fileInfo = FileInfoType1.Parse(data, offset);
                block.Files.Add(fileInfo);
                offset += fileInfo.Size;
            }

            return block;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== ファイルタイプ1ブロック (ID=F1) ===");
            sb.AppendLine($"エリア全体サイズ            : {AreaTotalSize} byte");
            sb.AppendLine($"ファイル情報(タイプ1)オフセット: 0x{FileInfoOffset:X8}");
            sb.AppendLine($"拡張子数(タイプ1)           : {ExtensionCount}");
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
    // 2.7.2.1 拡張子情報（タイプ1）130byte
    // =====================================================
    public class ExtensionInfoType1
    {
        /// <summary>ファイル数 +0h, 2byte</summary>
        public ushort FileCount { get; set; }

        /// <summary>拡張子該当ファイル情報オフセット +2h, 4byte（ファイル情報(タイプ1)エリア先頭からのオフセット）</summary>
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
        public static ExtensionInfoType1 Parse(ReadOnlySpan<byte> data, int offset)
        {
            var info = new ExtensionInfoType1
            {
                FileCount = BitConverter.ToUInt16(data.Slice(offset + 0x00, 2)),
                FileInfoOffset = BitConverter.ToUInt32(data.Slice(offset + 0x02, 4)),
                ExtensionNameLength = BitConverter.ToUInt16(data.Slice(offset + 0x06, 2)),
            };

            // Unicode文字列（EOS含む最大61文字 = 122byte）をデコード
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
    // 2.7.2.2 ファイル情報（タイプ1）136byte
    // =====================================================
    public class FileInfoType1
    {
        /// <summary>収集フラグ +0h, 2byte（00h=収集しない, 01h=収集する）</summary>
        public ushort CollectFlag { get; set; }

        /// <summary>プログラム/FBNo. +2h, 2byte</summary>
        public ushort ProgramFbNo { get; set; }

        /// <summary>ファイルCRC値 +4h, 8byte</summary>
        public ulong FileCrc { get; set; }

        /// <summary>ファイル名長 +Ch, 2byte（Unicode文字数、EOS含む、2～61）</summary>
        public ushort FileNameLength { get; set; }

        /// <summary>ファイル名 +Eh, 122byte（Unicode、EOS付き、ピリオド・拡張子含まず）</summary>
        public string FileName { get; set; } = string.Empty;

        public bool IsCollect => CollectFlag == 0x0001;

        public int charCount;

        public int Size
        {
            get
            {
                return 0x0E + charCount * 2;
            }
        }

        public static FileInfoType1 Parse(ReadOnlySpan<byte> data, int offset)
        {
            var info = new FileInfoType1
            {
                CollectFlag = BitConverter.ToUInt16(data.Slice(offset + 0x00, 2)),
                ProgramFbNo = BitConverter.ToUInt16(data.Slice(offset + 0x02, 2)),
                FileCrc = BitConverter.ToUInt64(data.Slice(offset + 0x04, 8)),
                FileNameLength = BitConverter.ToUInt16(data.Slice(offset + 0x0C, 2)),
            };

            info.charCount = Math.Min((int)info.FileNameLength, 61);
            info.FileName = System.Text.Encoding.Unicode
                .GetString(data.Slice(offset + 0x0E, info.charCount * 2))
                .TrimEnd('\0');

            return info;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"    収集フラグ      : 0x{CollectFlag:X4} ({(IsCollect ? "収集する" : "収集しない")})");
            sb.AppendLine($"    プログラム/FBNo.: {ProgramFbNo}");
            sb.AppendLine($"    ファイルCRC値   : 0x{FileCrc:X16}");
            sb.AppendLine($"    ファイル名長    : {FileNameLength}");
            sb.AppendLine($"    ファイル名      : {FileName}");
            return sb.ToString();
        }
    }
}