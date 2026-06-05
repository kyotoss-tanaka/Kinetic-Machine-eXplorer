using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // タイムスタンプ構造体
    internal class CollectionDevInfo
    {
        // (1) ファイル情報共通ヘッダ（別途定義済み）
        public CommonHeader CommonHeader { get; set; } = new();

        // (2) 差分デバイスログ情報エリアのサイズ
        public ushort InfoAreaSize { get; set; }

        // 差分デバイスログ情報エリア
        public uint DiffRecordCount { get; set; }  // (3) 差分レコード合計数  4バイト
        public ushort DiffFileCount { get; set; }  // (4) 差分デバイスログファイル数 2バイト
        public ushort DiffCompareUnit { get; set; }  // (5) 差分比較単位       2バイト

        // (6) ファイル終端(0埋め) 2バイト

        public static CollectionDevInfo Parse(byte[] data)
        {
            var file = new CollectionDevInfo();

            // (1) 共通ヘッダ（0x2C バイト）
            file.CommonHeader = CommonHeader.Parse(data);
            int pos = 0x2C;

            // (2) 差分デバイスログ情報エリアのサイズ
            file.InfoAreaSize = BitConverter.ToUInt16(data, pos); pos += 2;

            // 差分デバイスログ情報エリア (+00h〜+07h)
            int areaBase = pos;
            file.DiffRecordCount = BitConverter.ToUInt32(data, areaBase + 0x00);  // +00h 4バイト
            file.DiffFileCount = BitConverter.ToUInt16(data, areaBase + 0x04);  // +04h 2バイト
            file.DiffCompareUnit = BitConverter.ToUInt16(data, areaBase + 0x06);  // +06h 2バイト

            // (6) ファイル終端は読み飛ばし
            return file;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine(CommonHeader.ToString());
            sb.AppendLine($"差分レコード合計数  : {DiffRecordCount}");
            sb.AppendLine($"差分ファイル数      : {DiffFileCount}");
            sb.AppendLine($"差分比較単位        : {DiffCompareUnit}");
            return sb.ToString();
        }
    }
}
