using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    internal class IDF0_FileListBlock
    {
        /// <summary>設定情報ヘッダ（ファイルリストブロック）+0h, 40byte</summary>
        public FileListBlockHeader Header { get; set; } = new();

        /// <summary>ファイルタイプ1ブロック(ID=F1h) +28h～</summary>
        public IDF1_FileType1Block? FileType1Block { get; set; }

        /// <summary>ファイルタイプ2ブロック(ID=F2h) +CC1Ch～</summary>
        public IDF2_FileType2Block? FileType2Block { get; set; }

        public static IDF0_FileListBlock Parse(byte[] data, int baseOffset = 0)
        {
            if (data.Length < baseOffset + FileListBlockHeader.HeaderSize)
                throw new ArgumentException("データが不足しています。");

            var block = new IDF0_FileListBlock
            {
                Header = FileListBlockHeader.Parse(data, baseOffset),
            };

            // ファイルタイプ1ブロック +28h (固定オフセット)
            block.FileType1Block = IDF1_FileType1Block.Parse(data, baseOffset + (int)block.Header.FileType1BlockInfo.BlockOffset);

            // ファイルタイプ2ブロック +CC1Ch (固定オフセット)
            block.FileType2Block = IDF2_FileType2Block.Parse(data, baseOffset + (int)block.Header.FileType2BlockInfo.BlockOffset);

            return block;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("====================================================");
            sb.AppendLine(" ファイルリストブロック (ID=F0h)");
            sb.AppendLine("====================================================");
            sb.AppendLine(Header.ToString());

            if (FileType1Block is not null)
            {
                sb.AppendLine();
                sb.AppendLine(FileType1Block.ToString());
            }

            if (FileType2Block is not null)
            {
                sb.AppendLine();
                sb.AppendLine(FileType2Block.ToString());
            }

            return sb.ToString();
        }
    }
}
