using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // タイムスタンプ構造体
    internal class TimeStamp
    {
        public byte TimezoneFlag { get; set; }  // タイムゾーン/サマータイムフラグ
        public int Month { get; set; }  // 月
        public int WestL { get; set; }  // 西暦L
        public int Hour { get; set; }  // 時
        public int Day { get; set; }  // 日
        public int Second { get; set; }  // 秒
        public int Minute { get; set; }  // 分
        public int WestH { get; set; }  // 西暦H
        public int DayOfWeek { get; set; }  // 曜日
        public int MsL { get; set; }  // ミリ秒L
        public int MsH { get; set; }  // ミリ秒H
        private static int Bcd(byte b) => (b >> 4) * 10 + (b & 0x0F);
        public int Year
        {
            get
            {

                return WestH * 100 + WestL;
            }
        }
        public int Ms
        {
            get
            {
                return MsH * 100 + MsL;
            }
        }
        public DateTime DateTime
        {
            get
            {
                return new DateTime(Year, Month, Day, Hour, Minute, Second, Ms);
            }
        }

        public static TimeStamp Parse(byte[] data, int offset)
        {
            // 12バイト、16bit単位で読む
            var ret = new TimeStamp
            {
                TimezoneFlag = data[offset + 1],
                WestL = Bcd(data[offset + 2]),
                Month = Bcd(data[offset + 3]),
                Day = Bcd(data[offset + 4]),
                Hour = Bcd(data[offset + 5]),
                Minute = Bcd(data[offset + 6]),
                Second = Bcd(data[offset + 7]),
                DayOfWeek = Bcd(data[offset + 8]),
                WestH = Bcd(data[offset + 9]),
                MsH = Bcd(data[offset + 10]),
                MsL = Bcd(data[offset + 11]),
            };
            return ret;
        }

        public override string ToString()
        {
            return $"{Year}/{Month:D2}/{Day:D2} {Hour:D2}:{Minute:D2}:{Second:D2}.{Ms:D3}";
        }
    }

    // トリガ情報
    internal class TriggerInfo
    {
        public ushort SettingNumber { get; set; }  // (14) 設定番号
        public ushort TriggerNumber { get; set; }  // (15) トリガ番号
        public ushort TriggerCondition { get; set; }  // (16) ファイル保存トリガの条件成立要因
        public ushort IoNumber { get; set; }  // (17) 自ユニットの先頭I/O No.
        public ulong SamplingCounter { get; set; }  // (18) サンプリングカウンタ値
        public string Comment { get; set; } = "";  // (20) トリガコメント
    }

    internal class OverallControlFile
    {
        // ファイル情報共通ヘッダ
        public CommonHeader CommonHeader { get; set; } = new();   // (1) 別途定義済み

        // 設定情報エリア
        public ushort ManagementCpuCode { get; set; }  // (2) 管理CPU固有コード
        public ushort SettingAreaSize { get; set; }  // (3) 設定情報エリアのサイズ
        public ushort CollectionTargetBits { get; set; }  // (4) 収集対象ビット
        public ushort RetentionMethod { get; set; }  // (5) 保存期間の指定方法

        // 保存期間情報エリア
        public ushort RetentionAreaSize { get; set; }  // (6) 保存期間情報エリアのサイズ
        public ulong StartSamplingCounter { get; set; }  // (7) 開始サンプリングカウンタ値
        public ulong EndSamplingCounter { get; set; }  // (8) 終了サンプリングカウンタ値
        public ulong StartScanCounter { get; set; }  // (9) 開始スキャンカウンタ値
        public ulong EndScanCounter { get; set; }  // (10) 終了スキャンカウンタ値
        public TimeStamp StartTimestamp { get; set; } = new();  // (11) 開始タイムスタンプ
        public TimeStamp EndTimestamp { get; set; } = new();  // (12) 終了タイムスタンプ

        // トリガ情報エリア
        public ushort TriggerAreaSize { get; set; }  // (13) トリガ情報エリアのサイズ
        public List<TriggerInfo> Triggers { get; set; } = new();

        // 収集対象ビットのフラグ判定
        public bool HasVideoData => (CollectionTargetBits & (1 << 3)) != 0;  // b3
        public bool HasProjectData => (CollectionTargetBits & (1 << 2)) != 0;  // b2
        public bool HasEventHistory => (CollectionTargetBits & (1 << 1)) != 0;  // b1
        public bool HasDeviceLabel => (CollectionTargetBits & (1 << 0)) != 0;  // b0

        public static OverallControlFile Parse(byte[] data)
        {
            var file = new OverallControlFile();

            // (1) 共通ヘッダ（0x2C バイト）
            file.CommonHeader = CommonHeader.Parse(data);
            var pos = 0x2C;

            // (2)(3) 設定情報ヘッダ
            file.ManagementCpuCode = BitConverter.ToUInt16(data, pos); pos += 2;
            file.SettingAreaSize = BitConverter.ToUInt16(data, pos); pos += 2;

            // 設定情報エリア (+00h〜+03h)
            int settingBase = pos;
            file.CollectionTargetBits = BitConverter.ToUInt16(data, settingBase + 0x00);
            file.RetentionMethod = BitConverter.ToUInt16(data, settingBase + 0x02);
            pos = settingBase + file.SettingAreaSize;

            // (6) 保存期間情報エリア
            file.RetentionAreaSize = BitConverter.ToUInt16(data, pos); pos += 2;
            int retentionBase = pos;
            file.StartSamplingCounter = BitConverter.ToUInt64(data, retentionBase + 0x00);
            file.EndSamplingCounter = BitConverter.ToUInt64(data, retentionBase + 0x08);
            file.StartScanCounter = BitConverter.ToUInt64(data, retentionBase + 0x10);
            file.EndScanCounter = BitConverter.ToUInt64(data, retentionBase + 0x18);
            file.StartTimestamp = TimeStamp.Parse(data, retentionBase + 0x20);
            file.EndTimestamp = TimeStamp.Parse(data, retentionBase + 0x2C);
            pos = retentionBase + file.RetentionAreaSize;

            // (13) トリガ情報エリア
            file.TriggerAreaSize = BitConverter.ToUInt16(data, pos); pos += 2;
            int triggerBase = pos;
            int triggerEnd = triggerBase + file.TriggerAreaSize;

            while (pos < triggerEnd)
            {
                var trig = new TriggerInfo
                {
                    SettingNumber = BitConverter.ToUInt16(data, pos + 0x00),
                    TriggerNumber = BitConverter.ToUInt16(data, pos + 0x02),
                    TriggerCondition = BitConverter.ToUInt16(data, pos + 0x04),
                    IoNumber = BitConverter.ToUInt16(data, pos + 0x06),
                    SamplingCounter = BitConverter.ToUInt64(data, pos + 0x08),
                };
                pos += 0x10;

                // (19) トリガコメントサイズ
                ushort commentSize = BitConverter.ToUInt16(data, pos); pos += 2;
                trig.Comment = Encoding.Unicode.GetString(data, pos, commentSize);
                pos += commentSize;

                // システムエリア(8バイト) + ファイル終端(2バイト) をスキップ
                pos += 10;

                file.Triggers.Add(trig);
            }

            return file;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine(CommonHeader.ToString());
            sb.AppendLine($"管理CPU固有コード : 0x{ManagementCpuCode:X4}");
            sb.AppendLine($"収集対象ビット    : 0x{CollectionTargetBits:X4}");
            sb.AppendLine($"  映像データ      : {HasVideoData}");
            sb.AppendLine($"  プロジェクトデータ: {HasProjectData}");
            sb.AppendLine($"  イベント履歴    : {HasEventHistory}");
            sb.AppendLine($"  デバイス/ラベル : {HasDeviceLabel}");
            sb.AppendLine($"開始サンプリング  : {StartSamplingCounter}");
            sb.AppendLine($"終了サンプリング  : {EndSamplingCounter}");
            sb.AppendLine($"開始スキャン      : {StartScanCounter}");
            sb.AppendLine($"終了スキャン      : {EndScanCounter}");
            sb.AppendLine($"開始タイムスタンプ: {StartTimestamp}");
            sb.AppendLine($"終了タイムスタンプ: {EndTimestamp}");
            foreach (var t in Triggers)
            {
                sb.AppendLine($"--- トリガ ---");
                sb.AppendLine($"  設定番号  : {t.SettingNumber}");
                sb.AppendLine($"  トリガ番号: {t.TriggerNumber}");
                sb.AppendLine($"  コメント  : {t.Comment}");
            }
            return sb.ToString();
        }
    }
}
