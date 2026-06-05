using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // 2.3.3. レコーディング方式設定ブロック(ID=12h) 40バイト
    internal class ID12_RecordingMethodSettingBlock
    {
        public uint AreaTotalSize { get; set; }  // +00h エリア全体サイズ   4バイト
        public ushort RecordingTarget { get; set; }  // +04h レコーディング対象指定 2バイト
        public ushort RecordingMethod { get; set; }  // +06h レコーディング方式設定 2バイト

        // レコーディング対象指定ビット
        public bool CollectDeviceLabel => (RecordingTarget & (1 << 0)) != 0;  // b0 デバイス/ラベル(固定:収集する)
        public bool CollectEventHistory => (RecordingTarget & (1 << 1)) != 0;  // b1 イベント履歴(固定:収集する)
        public bool CollectProjectData => (RecordingTarget & (1 << 2)) != 0;  // b2 プロジェクトデータ(固定:収集しない)
        public bool CollectVideoData => (RecordingTarget & (1 << 3)) != 0;  // b3 動画データ

        public string RecordingMethodName => RecordingMethod switch
        {
            0x00 => "ファイル保存トリガのみ",
            0x01 => "レコーディング開始トリガ+ファイル保存トリガ",
            _ => $"不明(0x{RecordingMethod:X2})"
        };

        // +08h レコーディング方式設定詳細(可変 最大32バイト)
        public RecordingMethodDetailFileTrigger? DetailFileTrigger { get; set; }
        public RecordingMethodDetailStartTrigger? DetailStartTrigger { get; set; }

        public static ID12_RecordingMethodSettingBlock Parse(byte[] data, int offset)
        {
            var block = new ID12_RecordingMethodSettingBlock
            {
                AreaTotalSize = BitConverter.ToUInt32(data, offset + 0x00),
                RecordingTarget = BitConverter.ToUInt16(data, offset + 0x04),
                RecordingMethod = BitConverter.ToUInt16(data, offset + 0x06),
            };

            switch (block.RecordingMethod)
            {
                case 0x00:
                    block.DetailFileTrigger = RecordingMethodDetailFileTrigger.Parse(data, offset + 0x08);
                    break;
                case 0x01:
                    block.DetailStartTrigger = RecordingMethodDetailStartTrigger.Parse(data, offset + 0x08);
                    break;
            }

            return block;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"レコーディング方式      : {RecordingMethodName}");
            sb.AppendLine($"デバイス/ラベル収集     : {CollectDeviceLabel}");
            sb.AppendLine($"イベント履歴収集        : {CollectEventHistory}");
            sb.AppendLine($"プロジェクトデータ収集  : {CollectProjectData}");
            sb.AppendLine($"動画データ収集          : {CollectVideoData}");
            if (DetailFileTrigger != null) sb.AppendLine(DetailFileTrigger.ToString());
            if (DetailStartTrigger != null) sb.AppendLine(DetailStartTrigger.ToString());
            return sb.ToString();
        }
    }
    
    // 2.3.3.1. レコーディング方式設定詳細(ファイル保存トリガのみ) 10バイト
    public class RecordingMethodDetailFileTrigger
    {
        public uint PreTriggerTime { get; set; }  // +00h ファイル保存トリガ前時間 4バイト (0〜86400秒)
        public ushort PreTriggerTimeUnit { get; set; }  // +04h 指定単位(表示用)        2バイト
        public uint PostTriggerTime { get; set; }  // +06h ファイル保存トリガ後時間 4バイト (0〜60秒)

        public string PreUnitName => PreTriggerTimeUnit switch
        {
            0x00 => "秒",
            0x01 => "分",
            0x02 => "時間",
            _ => $"不明(0x{PreTriggerTimeUnit:X2})"
        };

        public static RecordingMethodDetailFileTrigger Parse(byte[] data, int offset) => new()
        {
            PreTriggerTime = BitConverter.ToUInt32(data, offset + 0x00),
            PreTriggerTimeUnit = BitConverter.ToUInt16(data, offset + 0x04),
            PostTriggerTime = BitConverter.ToUInt32(data, offset + 0x06),
        };

        public override string ToString() =>
            $"前時間={PreTriggerTime}{PreUnitName} 後時間={PostTriggerTime}秒";
    }

    // 2.3.3.2. レコーディング方式設定詳細(レコーディング開始トリガ+ファイル保存トリガ) 32バイト
    public class RecordingMethodDetailStartTrigger
    {
        public ushort StartTriggerCondition { get; set; }  // +00h レコーディング開始トリガ条件  2バイト
        public byte[] ModifyDeviceCode { get; set; }  // +02h 修飾デバイスコード(表示用)   16バイト
        public uint CollectionTime { get; set; }  // +12h 収集時間                     4バイト (0〜86400秒)
        public ushort CollectionTimeUnit { get; set; }  // +16h 指定単位(表示用)収集時間      2バイト
        public ushort AutoSaveAfterCollect { get; set; }  // +18h 収集完了後保存指定            2バイト
        public uint WaitTimeAfterCollect { get; set; }  // +1Ah 収集完了後待ち時間(可変)      4バイト (0〜86400秒)
        public ushort WaitTimeUnit { get; set; }  // +1Eh 指定単位(表示用)収集完了後待ち時間(可変) 2バイト

        public string StartTriggerName => StartTriggerCondition switch
        {
            0x00 => "立上がり",
            0x01 => "立下がり",
            _ => $"不明(0x{StartTriggerCondition:X2})"
        };

        public string CollectionTimeUnitName => CollectionTimeUnit switch
        {
            0x00 => "秒",
            0x01 => "分",
            0x02 => "時間",
            _ => $"不明(0x{CollectionTimeUnit:X2})"
        };

        public string AutoSaveName => AutoSaveAfterCollect switch
        {
            0x00 => "指定なし",
            0x01 => "指定あり",
            _ => $"不明(0x{AutoSaveAfterCollect:X2})"
        };

        public string WaitTimeUnitName => WaitTimeUnit switch
        {
            0x00 => "秒",
            0x01 => "分",
            0x02 => "時間",
            _ => $"不明(0x{WaitTimeUnit:X2})"
        };

        public static RecordingMethodDetailStartTrigger Parse(byte[] data, int offset) => new()
        {
            StartTriggerCondition = BitConverter.ToUInt16(data, offset + 0x00),
            ModifyDeviceCode = data[(offset + 0x02)..(offset + 0x12)],
            CollectionTime = BitConverter.ToUInt32(data, offset + 0x12),
            CollectionTimeUnit = BitConverter.ToUInt16(data, offset + 0x16),
            AutoSaveAfterCollect = BitConverter.ToUInt16(data, offset + 0x18),
            WaitTimeAfterCollect = BitConverter.ToUInt32(data, offset + 0x1A),
            WaitTimeUnit = BitConverter.ToUInt16(data, offset + 0x1E),
        };
        /*
        public override string ToString() =>
            $"""
        開始トリガ条件      : {StartTriggerName}
        収集時間            : {CollectionTime}{CollectionTimeUnitName}
        収集完了後保存      : {AutoSaveName}
        収集完了後待ち時間  : {(AutoSaveAfterCollect == 0x01 ? $"{WaitTimeAfterCollect}{WaitTimeUnitName}" : "なし")}
        """;
        */
    }
}
