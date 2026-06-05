using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{

    // ============================================================
    // 1.4.3. デバイスコードブロック (ID=D0h)
    // ============================================================
    internal class IDD0_DeviceCodeBlock
    {
        // 設定情報ヘッダ（デバイスコードブロック）56バイト固定
        public DeviceCodeBlockHeader Header { get; set; } = new();

        // デバイスコード解読情報ブロック(ID=D1h) 可変
        public IDD1_DeviceCodeDecodeBlock? DeviceCodeDecodeBlock { get; set; }

        // デバイスコード情報ブロック(ID=D2h) 可変
        public IDD2_WordCollectDeviceCodeBlock? WordCollectDeviceCodeBlock { get; set; }

        // トリガデバイス情報ブロック(ID=D3h) 可変
        public IDD3_TriggerDeviceBlock? TriggerDeviceBlock { get; set; }

        // ビット単位収集デバイスコード情報ブロック(ID=D4h) 可変
//        public IDD4_BitCollectDeviceCodeBlock BitCollectDeviceCodeBlock { get; set; }

        public static IDD0_DeviceCodeBlock Parse(byte[] data, int offset)
        {
            var block = new IDD0_DeviceCodeBlock();

            // ヘッダ解析（固定56バイト）
            block.Header = DeviceCodeBlockHeader.Parse(data, offset);

            // デバイスコード解読情報ブロック
            block.DeviceCodeDecodeBlock = IDD1_DeviceCodeDecodeBlock.Parse(data, offset + (int)block.Header.DeviceCodeDecodeInfo.BlockOffset);

            // ワード単位収集デバイスコード情報ブロック
            block.WordCollectDeviceCodeBlock = IDD2_WordCollectDeviceCodeBlock.Parse(data, offset + (int)block.Header.DeviceCodeInfo.BlockOffset);

            // トリガデバイス情報ブロック
            block.TriggerDeviceBlock = IDD3_TriggerDeviceBlock.Parse(data, offset + (int)block.Header.TriggerDeviceInfo.BlockOffset);

            // トリガデバイス情報ブロック
//            block.BitCollectDeviceCodeBlock = IDD4_BitCollectDeviceCodeBlock.Parse(data, offset + (int)block.Header..BlockOffset);
            return block;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== デバイスコードブロック (ID=D0h) ===");
            sb.AppendLine(Header.ToString());
            if (DeviceCodeDecodeBlock != null) sb.AppendLine(DeviceCodeDecodeBlock.ToString());
            if (WordCollectDeviceCodeBlock != null) sb.AppendLine(WordCollectDeviceCodeBlock.ToString());
            if (TriggerDeviceBlock != null) sb.AppendLine(TriggerDeviceBlock.ToString());
            return sb.ToString();
        }
    }
}
