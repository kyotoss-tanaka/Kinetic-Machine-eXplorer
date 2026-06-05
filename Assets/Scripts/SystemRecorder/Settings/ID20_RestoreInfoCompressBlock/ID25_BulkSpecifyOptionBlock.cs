using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // 2.4.6. 一括指定オプションブロック(ID=25h) 8バイト
    internal class ID25_BulkSpecifyOptionBlock
    {
        public uint AreaTotalSize { get; set; }  // +00h エリア全体サイズ  4バイト
        public ushort ParameterCollect { get; set; }  // +04h パラメータ収集有無 2バイト
                                                      // +06h 境界調整用領域 2バイト(可変・0固定)

        public bool CollectsParameter => ParameterCollect == 0x00;

        public string ParameterCollectName => ParameterCollect switch
        {
            0x00 => "収集する",
            0x01 => "収集しない",
            _ => $"不明(0x{ParameterCollect:X2})"
        };

        public static ID25_BulkSpecifyOptionBlock Parse(byte[] data, int offset) => new()
        {
            AreaTotalSize = BitConverter.ToUInt32(data, offset + 0x00),
            ParameterCollect = BitConverter.ToUInt16(data, offset + 0x04),
        };

        public override string ToString() =>
            $"パラメータ収集有無: {ParameterCollectName}";
    }
}
