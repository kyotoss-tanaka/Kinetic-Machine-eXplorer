using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // 2.4.11. 個別デバイス情報ブロック(ID=2Ah) 8バイト
    internal class ID2A_IndividualDeviceInfoBlock
    {
        public uint AreaTotalSize { get; set; }  // +00h エリア全体サイズ 4バイト
        public uint DevicePresence { get; set; }  // +04h 設定デバイス有無 4バイト (Phase5以降)
                                                  // +08h 境界調整用領域 可変(0固定)

        // ▼ビット詳細: 設定デバイス有無
        // b0: SFCデバイス (0:無、1:有)
        // b1-31: 補足参照
        public bool HasSfcDevice => (DevicePresence & (1U << 0)) != 0;

        public static ID2A_IndividualDeviceInfoBlock Parse(byte[] data, int offset) => new()
        {
            AreaTotalSize = BitConverter.ToUInt32(data, offset + 0x00),
            DevicePresence = BitConverter.ToUInt32(data, offset + 0x04),
        };

        /*
        public override string ToString() =>
            $"""
        設定デバイス有無 : 0x{DevicePresence:X8}
          SFCデバイス    : {(HasSfcDevice ? "有" : "無")}
        """;
        */
    }
}
