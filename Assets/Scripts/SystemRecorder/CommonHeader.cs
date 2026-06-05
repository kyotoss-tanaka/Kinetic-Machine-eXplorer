using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    internal class CommonHeader
    {
        public ushort FileTypeCode { get; set; }   // (1) ファイル種別コード 2バイト
        public ushort HeaderSize { get; set; }   // (2) ヘッダサイズ     2バイト
        public uint Checksum { get; set; }   // (3) チェックサム     4バイト
        public ushort FileVersion { get; set; }   // (4) ファイルバージョン 2バイト
        public ushort FwVersion { get; set; }   // (5) F/Wバージョン   2バイト
        public string ModelName { get; set; }   // (6) 形名情報        16バイト
        public byte[] Uuid { get; set; }   // (7) 識別用UUID      16バイト

        public static CommonHeader Parse(byte[] data)
        {
            return new CommonHeader
            {
                FileTypeCode = BitConverter.ToUInt16(data, 0x00),
                HeaderSize = BitConverter.ToUInt16(data, 0x02),
                Checksum = BitConverter.ToUInt32(data, 0x04),
                FileVersion = BitConverter.ToUInt16(data, 0x08),
                FwVersion = BitConverter.ToUInt16(data, 0x0A),
                ModelName = Encoding.ASCII.GetString(data, 0x0C, 16).TrimEnd('\0'),
                Uuid = data[0x1C..0x2C]
            };
        }
        /*
        public override string ToString()
        {
            return $"""
            ****************************************
            ファイル種別コード : 0x{FileTypeCode:X4}
            ヘッダサイズ       : {HeaderSize}
            チェックサム       : 0x{Checksum:X8}
            ファイルバージョン : 0x{FileVersion:X4}
            F/Wバージョン      : 0x{FwVersion:X4}
            形名情報           : {ModelName}
            識別用UUID         : {new Guid(Uuid)}
            """;
        }
        */
    }
}
