using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    internal class SettingHeader
    {
        public ushort SystemArea1 { get; set; }     // (1) システムエリア      2バイト
        public ushort HeaderSize { get; set; }      // (2) ヘッダサイズ        2バイト
        public uint DataTypeInfo { get; set; }      // (3) データ型式情報      4バイト
        public ulong SystemArea2 { get; set; }      // (4) システムエリア      8バイト
        public byte[] FilePassword { get; set; }    // (5) ファイルパスワード  68バイト
        public string Sentence { get; set; } = "";        // (6) 見出し文            可変バイト
        public uint HeaderEnd { get; set; }         // (7) ファイルヘッダ終了  4バイト
        public int SentenceSize
        {
            get
            {
                return HeaderSize - (2 + 2 + 4 + 8 + 68 + 4);
            }
        }
        public static SettingHeader Parse(byte[] data)
        {
            if (data.Length < 0x46)
                throw new ArgumentException("データが短すぎます");

            var ret = new SettingHeader
            {
                SystemArea1 = BitConverter.ToUInt16(data, 0x00),
                HeaderSize = BitConverter.ToUInt16(data, 0x02),
                DataTypeInfo = BitConverter.ToUInt32(data, 0x04),
                SystemArea2 = BitConverter.ToUInt64(data, 0x08),
                FilePassword = data[0x10..0x54],
            };
            ret.Sentence = ret.SentenceSize == 0 ? "" : Encoding.ASCII.GetString(data, 0x54, ret.SentenceSize).TrimEnd('\0');
            ret.HeaderEnd = BitConverter.ToUInt32(data, 0x54 + ret.SentenceSize);
            return ret;
        }

        /*
        public override string ToString()
        {
            return $"""
            ****************************************
            ヘッダサイズ       : {HeaderSize}
            見出し文サイズ     : {SentenceSize}
            データ型式情報     : 0x{DataTypeInfo:X8}
            見出し文           : {Sentence}
            """;
        }
        */
    }
}
