using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // 2.4.10. SFCデバイス一括指定ブロック(ID=29h) 8バイト
    internal class ID29_SfcDeviceBulkSpecBlock
    {
        public uint AreaTotalSize { get; set; }  // +00h エリア全体サイズ      4バイト
        public ushort BulkSpecify { get; set; }  // +04h SFCデバイス一括指定有無 2バイト (固定:01h 収集する)
                                                 // +06h 境界調整用領域 2バイト(可変・0固定)
                                                 // ※本項目が作成される場合は01h固定

        public bool CollectsSfcDevice => BulkSpecify == 0x01;

        public static ID29_SfcDeviceBulkSpecBlock Parse(byte[] data, int offset) => new()
        {
            AreaTotalSize = BitConverter.ToUInt32(data, offset + 0x00),
            BulkSpecify = BitConverter.ToUInt16(data, offset + 0x04),
        };

        public override string ToString() =>
            $"SFCデバイス一括指定有無: {(CollectsSfcDevice ? "収集する" : $"不明(0x{BulkSpecify:X4})")}";
    }
}
