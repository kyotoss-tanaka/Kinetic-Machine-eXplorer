using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SystemRecorderReader
{
    // 2.4.1. 設定情報ヘッダ(設定復元情報圧縮ブロック) 24バイト
    public class RestoreInfoCompressSettingHeader
    {
        public uint AreaTotalSize { get; set; }  // +00h エリア全体サイズ 4バイト
        public uint BlockCount { get; set; }  // +04h 設定ブロック数   4バイト (固定:1)
        public SettingBlockInfo RestoreInfoBlock { get; set; } = new();  // +08h 設定ブロック情報(設定復元情報ブロック) 16バイト
                                                                         // +18h 末尾

        public static RestoreInfoCompressSettingHeader Parse(byte[] data, int offset) => new()
        {
            AreaTotalSize = BitConverter.ToUInt32(data, offset + 0x00),
            BlockCount = BitConverter.ToUInt32(data, offset + 0x04),
            RestoreInfoBlock = SettingBlockInfo.Parse(data, offset + 0x08),
        };

        /*
        public override string ToString() =>
            $"""
        エリア全体サイズ        : {AreaTotalSize}
        設定ブロック数          : {BlockCount}
        設定復元情報ブロック    : {RestoreInfoBlock}
        """;
        */
    }

    // 2.4.2. 設定情報ヘッダ(設定復元情報ブロック) 152バイト
    public class RestoreInfoSettingHeader
    {
        public uint AreaTotalSize { get; set; }  // +00h エリア全体サイズ 4バイト
        public uint BlockCount { get; set; }  // +04h 設定ブロック数   4バイト
        public SettingBlockInfo ProgramSpecifyBlock { get; set; }  // +08h プログラム指定ブロック         16バイト
        public SettingBlockInfo UnitSpecifyBlock { get; set; }  // +18h ユニット指定ブロック           16バイト
        public SettingBlockInfo IndividualDeviceSpecBlock { get; set; }  // +28h 個別デバイス指定ブロック       16バイト
        public SettingBlockInfo BulkSpecifyOptionBlock { get; set; }  // +38h 一括指定オプションブロック     16バイト
        public SettingBlockInfo DeviceLabelListBlock { get; set; }  // +48h デバイスラベル一覧指定ブロック 16バイト
        public SettingBlockInfo DeviceLabelExtBlock { get; set; }  // +58h デバイスラベル一覧拡張指定ブロック 16バイト
        public SettingBlockInfo LastDeviceInfoBlock { get; set; }  // +68h 最終デバイス情報ブロック       16バイト
        public SettingBlockInfo SfcDeviceBulkSpecBlock { get; set; }  // +78h SFCデバイス一括指定ブロック    16バイト
        public SettingBlockInfo IndividualDeviceInfoBlock { get; set; }  // +88h 個別デバイス情報ブロック       16バイト
                                                                         // +98h 末尾

        public static RestoreInfoSettingHeader Parse(byte[] data, int offset)
        {
            var ret = new RestoreInfoSettingHeader
            {
                AreaTotalSize = BitConverter.ToUInt32(data, offset + 0x00),
                BlockCount = BitConverter.ToUInt32(data, offset + 0x04),
            };
            ret.ProgramSpecifyBlock = ret.BlockCount >= 1 ? SettingBlockInfo.Parse(data, offset + 0x08) : new SettingBlockInfo();
            ret.UnitSpecifyBlock = ret.BlockCount >= 2 ? SettingBlockInfo.Parse(data, offset + 0x18) : new SettingBlockInfo();
            ret.IndividualDeviceSpecBlock = ret.BlockCount >= 3 ? SettingBlockInfo.Parse(data, offset + 0x28) : new SettingBlockInfo();
            ret.BulkSpecifyOptionBlock = ret.BlockCount >= 4 ? SettingBlockInfo.Parse(data, offset + 0x38) : new SettingBlockInfo();
            ret.DeviceLabelListBlock = ret.BlockCount >= 5 ? SettingBlockInfo.Parse(data, offset + 0x48) : new SettingBlockInfo();
            ret.DeviceLabelExtBlock = ret.BlockCount >= 6 ? SettingBlockInfo.Parse(data, offset + 0x58) : new SettingBlockInfo();
            ret.LastDeviceInfoBlock = ret.BlockCount >= 7 ? SettingBlockInfo.Parse(data, offset + 0x68) : new SettingBlockInfo();
            ret.SfcDeviceBulkSpecBlock = ret.BlockCount >= 8 ? SettingBlockInfo.Parse(data, offset + 0x78) : new SettingBlockInfo();
            ret.IndividualDeviceInfoBlock = ret.BlockCount >= 9 ? SettingBlockInfo.Parse(data, offset + 0x88) : new SettingBlockInfo();
            return ret;
        }
        /*
        public override string ToString() =>
            $"""
        エリア全体サイズ                        : {AreaTotalSize}
        設定ブロック数                          : {BlockCount}
        プログラム指定ブロック                  : {ProgramSpecifyBlock}
        ユニット指定ブロック                    : {UnitSpecifyBlock}
        個別デバイス指定ブロック                : {(IndividualDeviceSpecBlock.Exists ? IndividualDeviceSpecBlock.ToString() : "未生成")}
        一括指定オプションブロック              : {(BulkSpecifyOptionBlock.Exists ? BulkSpecifyOptionBlock.ToString() : "未生成")}
        デバイスラベル一覧指定ブロック          : {(DeviceLabelListBlock.Exists ? DeviceLabelListBlock.ToString() : "未生成")}
        デバイスラベル一覧拡張指定ブロック      : {(DeviceLabelExtBlock.Exists ? DeviceLabelExtBlock.ToString() : "未生成")}
        最終デバイス情報ブロック                : {(LastDeviceInfoBlock.Exists ? LastDeviceInfoBlock.ToString() : "未生成")}
        SFCデバイス一括指定ブロック             : {(SfcDeviceBulkSpecBlock.Exists ? SfcDeviceBulkSpecBlock.ToString() : "未生成")}
        個別デバイス情報ブロック                : {(IndividualDeviceInfoBlock.Exists ? IndividualDeviceInfoBlock.ToString() : "未生成")}
        """;
        */
    }
}
