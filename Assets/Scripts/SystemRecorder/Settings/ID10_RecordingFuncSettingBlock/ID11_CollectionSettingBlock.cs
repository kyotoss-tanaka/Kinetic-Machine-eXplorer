using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // 2.3.2. 収集設定ブロック(ID=11h) 12バイト
    internal class ID11_CollectionSettingBlock
    {
        public uint AreaTotalSize { get; set; }  // +00h エリア全体サイズ 4バイト
        public ushort CollectionMethod { get; set; }  // +04h 収集方式         2バイト
        public CollectionMethodDetail Detail { get; set; } = new(); // +06h 収集方式詳細(時間指定時のみ) 6バイト

        public string MethodName => CollectionMethod switch
        {
            0x00 => "毎スキャン",
            0x01 => "時間指定",
            0x02 => "トリガ命令",
            0x03 => "安全サイクル時間",
            _ => $"不明(0x{CollectionMethod:X2})"
        };

        public static ID11_CollectionSettingBlock Parse(byte[] data, int offset)
        {
            var block = new ID11_CollectionSettingBlock
            {
                AreaTotalSize = BitConverter.ToUInt32(data, offset + 0x00),
                CollectionMethod = BitConverter.ToUInt16(data, offset + 0x04),
            };
            // 時間指定の場合のみ詳細を解析
            if (block.CollectionMethod == 0x01)
                block.Detail = CollectionMethodDetail.Parse(data, offset + 0x06);
            return block;
        }

        /*
        public override string ToString()
        {
            return $"""
                    収集方式: {MethodName}
                    {(CollectionMethod == 0x01 && Detail != null ? $"収集方式詳細: {Detail}" : "")}
                    """;
        }
        */
    }
    internal class CollectionMethodDetail
    {
        public uint SpecifiedTime { get; set; }  // +00h 指定時間 4バイト (1〜86400000ミリ秒)
        public ushort SpecifiedUnit { get; set; }  // +04h 指定単位(表示用) 2バイト

        public string UnitName => SpecifiedUnit switch
        {
            0x00 => "ミリ秒",
            0x01 => "秒",
            0x02 => "分",
            0x03 => "時間",
            _ => $"不明(0x{SpecifiedUnit:X2})"
        };

        public static CollectionMethodDetail Parse(byte[] data, int offset) => new()
        {
            SpecifiedTime = BitConverter.ToUInt32(data, offset + 0x00),
            SpecifiedUnit = BitConverter.ToUInt16(data, offset + 0x04),
        };
        public override string ToString()
        {
            return $"指定時間={SpecifiedTime}ミリ秒 単位={UnitName}";
        }
    }
}
