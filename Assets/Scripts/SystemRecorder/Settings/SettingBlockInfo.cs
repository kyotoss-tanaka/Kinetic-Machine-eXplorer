using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // 2.2.2. 設定ブロック情報(共通) 16バイト
    public class SettingBlockInfo
    {
        public ushort BlockId { get; set; }  // +00h 設定ブロックID      2バイト (00h〜FFh)
        public ushort BlockVersion { get; set; }  // +02h 設定ブロックバージョン 2バイト (1〜255)
        public uint BlockOffset { get; set; }  // +04h 設定ブロックオフセット 4バイト (存在しない場合は0)
        public ulong BlockCrc { get; set; }  // +08h 設定ブロックCRC      8バイト
                                             // +10h 末尾

        public static SettingBlockInfo Parse(byte[] data, int offset)
        {
            return new SettingBlockInfo
            {
                BlockId = BitConverter.ToUInt16(data, offset + 0x00),
                BlockVersion = BitConverter.ToUInt16(data, offset + 0x02),
                BlockOffset = BitConverter.ToUInt32(data, offset + 0x04),
                BlockCrc = BitConverter.ToUInt64(data, offset + 0x08),
            };
        }

        public bool Exists => BlockOffset != 0 && BlockId != 0x00;

        public override string ToString() =>
            $"ID=0x{BlockId:X4} Ver={BlockVersion} offset={BlockOffset} CRC=0x{BlockCrc:X16}";

    }
}
