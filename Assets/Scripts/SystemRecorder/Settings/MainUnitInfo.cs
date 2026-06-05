using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    internal class MainUnitInfo
    {
        // +00h システムエリア(非公開) 4バイト
        public byte CpuUnitNumber { get; set; }  // +04h 管理CPU号機番号  1バイト
        public byte SlotNo { get; set; }  // +05h スロットNo.      1バイト
        public uint FirstIoNo { get; set; }  // +06h 先頭I/O No.(16進3桁表記) 4バイト
                                             // +0Ah 末尾

        public static MainUnitInfo Parse(byte[] data, int offset)
        {
            return new MainUnitInfo
            {
                CpuUnitNumber = data[offset + 0x04],
                SlotNo = data[offset + 0x05],
                FirstIoNo = BitConverter.ToUInt32(data, offset + 0x06),
            };
        }

        public override string ToString()
        {
            return $"CPU号機:{CpuUnitNumber} スロット:{SlotNo} 先頭I/O:0x{FirstIoNo:X3}";
        }
    }
}
