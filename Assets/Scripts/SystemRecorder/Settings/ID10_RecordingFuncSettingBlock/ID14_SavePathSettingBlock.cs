using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    internal class ID14_SavePathSettingBlock
    {
        public uint AreaTotalSize { get; set; }  // +00h エリア全体サイズ       4バイト
        public ushort ConnectionUpperLimit { get; set; }  // +04h 連番上限               2バイト (1〜999)
        public ushort ConnectionOverflow { get; set; }  // +06h 連番上限超過動作       2バイト
        public ushort DateType { get; set; }  // +08h 日時種別               2バイト
        public FormatTextDetail FormatText { get; set; } = new();  // +0Ah フォーマット文字詳細   64バイト(可変)
        public ushort SavePathInfoCount { get; set; }  // +4Ah 保存パス情報数         2バイト (0〜21)
        public List<SavePathInfo> SavePaths { get; set; } = new();  // +4Ch 保存パス情報[n] 4バイト×n
        public ushort PathSpecifyMethod { get; set; }  // +A0h パス名指定方式(表示用) 2バイト
        public ushort PathSpecifyDetail { get; set; }  // +A2h パス名指定詳細(表示用) 2バイト

        public string ConnectionOverflowName => ConnectionOverflow switch
        {
            0x00 => "保存する",
            0x01 => "保存しない",
            _ => $"不明(0x{ConnectionOverflow:X2})"
        };

        public string DateTypeName => DateType switch
        {
            0x00 => "ファイル保存トリガ日時",
            0x01 => "ファイル保存日時",
            _ => $"不明(0x{DateType:X2})"
        };

        public static ID14_SavePathSettingBlock Parse(byte[] data, int offset)
        {
            var block = new ID14_SavePathSettingBlock
            {
                AreaTotalSize = BitConverter.ToUInt32(data, offset + 0x00),
                ConnectionUpperLimit = BitConverter.ToUInt16(data, offset + 0x04),
                ConnectionOverflow = BitConverter.ToUInt16(data, offset + 0x06),
                DateType = BitConverter.ToUInt16(data, offset + 0x08),
                FormatText = FormatTextDetail.Parse(data, offset + 0x0A),
            };
            block.SavePathInfoCount = BitConverter.ToUInt16(data, offset + 0x0A + block.FormatText.DataSize);
            var pos = 0x0A + block.FormatText.DataSize + 2;
            for (int i = 0; i < block.SavePathInfoCount; i++)
            {
                block.SavePaths.Add(SavePathInfo.Parse(data, offset + pos));
                pos += 4;
            }
            block.PathSpecifyMethod = BitConverter.ToUInt16(data, offset + pos);
            block.PathSpecifyDetail = BitConverter.ToUInt16(data, offset + pos + 2);
            return block;
        }
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"連番上限          : {ConnectionUpperLimit}");
            sb.AppendLine($"連番上限超過動作  : {ConnectionOverflowName}");
            sb.AppendLine($"日時種別          : {DateTypeName}");
            sb.AppendLine($"フォーマット文字  : {FormatText}");
            sb.AppendLine($"保存パス情報数    : {SavePathInfoCount}");
            for (int i = 0; i < SavePaths.Count; i++)
                sb.AppendLine($"  パス[{i}]: {SavePaths[i]}");
            sb.AppendLine($"パス名指定方式    : 0x{PathSpecifyMethod:X4}");
            sb.AppendLine($"パス名指定詳細    : 0x{PathSpecifyDetail:X4}");
            return sb.ToString();
        }
    }
    // 2.3.5.1. フォーマット文字詳細 38バイト
    internal class FormatTextDetail
    {
        public ushort FormatTextCount { get; set; }  // +00h フォーマット文字数 2バイト (0〜35)
        public string FormatText { get; set; }  // +02h フォーマット文字  35バイト(ASCII) 可変
                                                // +25h 境界調整用領域 1バイト(0固定)
        public int DataSize
        {
            get
            {
                return FormatTextCount % 2 == 0 ? (int)FormatTextCount + 2 : (int)FormatTextCount + 2 + 1;
            }
        }

        public static FormatTextDetail Parse(byte[] data, int offset)
        {
            var ret = new FormatTextDetail
            {
                FormatTextCount = BitConverter.ToUInt16(data, offset + 0x00),
            };
            ret.FormatText = Encoding.ASCII.GetString(data, offset + 0x02, ret.FormatTextCount).TrimEnd('\0');
            return ret;
        }
    }

    // 2.3.5.2. 保存パス情報 4バイト×n
    internal class SavePathInfo
    {
        public ushort ItemId { get; set; }  // +00h 項目ID  2バイト
        public ushort CharCount { get; set; }  // +02h 文字数  2バイト

        public string ItemName => ItemId switch
        {
            0x00 => "フォーマット文字(日時)",
            0x01 => "フォーマット文字1",
            0x10 => "年(上2桁)",
            0x11 => "年(下2桁)",
            0x12 => "月",
            0x13 => "日",
            0x14 => "曜日",
            0x20 => "時",
            0x21 => "分",
            0x22 => "秒",
            0x30 => "数値データ1(10進)",
            0x31 => "数値データ1(16進)",
            0x32 => "数値データ2(10進)",
            0x33 => "数値データ2(16進)",
            _ => $"不明(0x{ItemId:X2})"
        };

        public static SavePathInfo Parse(byte[] data, int offset) => new()
        {
            ItemId = BitConverter.ToUInt16(data, offset + 0x00),
            CharCount = BitConverter.ToUInt16(data, offset + 0x02),
        };
        public override string ToString()
        {
            return $"項目={ItemName} 文字数={CharCount}";
        }
    }
}
