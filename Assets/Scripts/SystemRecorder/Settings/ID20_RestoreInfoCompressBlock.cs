using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using SharpCompress.Compressors.LZMA;

namespace SystemRecorderReader
{
    // 1.4.2. 設定復元情報圧縮ブロック(ID=20h)
    internal class ID20_RestoreInfoCompressBlock
    {
        // +00h 設定情報ヘッダ(設定復元情報圧縮ブロック) 24バイト
        public RestoreInfoCompressSettingHeader SettingHeader { get; set; } = new();

        // +18h 設定復元情報ブロック圧縮有無 2バイト
        public ushort IsCompressed { get; set; }

        // +1Ah 設定復元情報ブロックサイズ(圧縮前) 4バイト
        public uint OriginalSize { get; set; }

        // +1Eh 設定復元情報ブロック格納サイズ 4バイト
        public uint StoredSize { get; set; }

        // +22h 設定復元情報ブロック(ID=21h) 可変(最大524288バイト=512KB)
        public IDD1_RestoreInfoBlock RestoreInfo { get; set; } = new();

        // 境界調整用領域 可変(0固定)

        public bool Compressed => IsCompressed != 0;

        public static ID20_RestoreInfoCompressBlock Parse(byte[] data, int offset)
        {
            var block = new ID20_RestoreInfoCompressBlock
            {
                SettingHeader = RestoreInfoCompressSettingHeader.Parse(data, offset + 0x00),
                IsCompressed = BitConverter.ToUInt16(data, offset + 0x18),
                OriginalSize = BitConverter.ToUInt32(data, offset + 0x1A),
                StoredSize = BitConverter.ToUInt32(data, offset + 0x1E),
            };

            // 圧縮されている場合はzipを展開してから解析
            if (block.Compressed)
            {
                byte[] compressed = data[(offset + 0x22)..(offset + 0x22 + (int)block.StoredSize)];
                byte[] decompressed = DecompressGxw3(compressed, (int)block.OriginalSize);
                block.RestoreInfo = IDD1_RestoreInfoBlock.Parse(decompressed, 0);
            }
            else
            {
                block.RestoreInfo = IDD1_RestoreInfoBlock.Parse(data, offset + 0x22);
            }

            return block;
        }

        // GXW3圧縮(zip圧縮)の展開
        private static byte[] DecompressGxw3(byte[] compressed, int originalSize)
        {
            // LZMA形式
            // [0..4]  : プロパティ 5バイト
            // [5..12] : 非圧縮サイズ 8バイト (リトルエンディアン)
            // [13..]  : 圧縮データ本体
            byte[] properties = compressed[0..5];
            long uncompressedSize = BitConverter.ToInt64(compressed, 5);
            int dataOffset = 13;
            int dataLength = compressed.Length - dataOffset;
            using var input = new MemoryStream(compressed, dataOffset, dataLength);
            using var lzma = new LzmaStream(properties, input, dataLength, uncompressedSize);
            using var output = new MemoryStream(originalSize);
            lzma.CopyTo(output);

            byte[] result = output.ToArray();

            // 後ろからoriginalSize分を返す
            return result[(result.Length - originalSize)..];
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 設定情報ヘッダ(設定復元情報圧縮ブロック) ===");
            sb.AppendLine(SettingHeader.ToString());
            sb.AppendLine($"圧縮有無    : {(Compressed ? "圧縮あり" : "圧縮なし")}");
            sb.AppendLine($"圧縮前サイズ: {OriginalSize}バイト");
            sb.AppendLine($"格納サイズ  : {StoredSize}バイト");
            sb.AppendLine("=== 設定復元情報ブロック ===");
            sb.AppendLine(RestoreInfo.ToString());
            return sb.ToString();
        }
    }
}
