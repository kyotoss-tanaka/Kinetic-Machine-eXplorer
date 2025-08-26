using Meta.XR.ImmersiveDebugger.UserInterface;
using Parameters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

public class ComMcProtocol : ComProtocolBase
{
    /// <summary>
    /// デバイス種別
    /// </summary>
    public enum eDeviceType : byte
    {
        /// <summary>
        /// 入力
        /// </summary>
        X = 0x9C,
        /// <summary>
        /// 出力
        /// </summary>
        Y = 0x9D,
        /// <summary>
        /// 内部リレー
        /// </summary>
        M = 0x90,
        /// <summary>
        /// ラッチリレー
        /// </summary>
        L = 0x92,
        /// <summary>
        /// リンクリレー
        /// </summary>
        B = 0xA0,
        /// <summary>
        /// データ
        /// </summary>
        D = 0xA8,
        /// <summary>
        /// リンク
        /// </summary>
        W = 0xB4,
        /// <summary>
        /// ファイルレジスタ
        /// </summary>
        R = 0xAF,
        /// <summary>
        /// ファイルレジスタ
        /// </summary>
        ZR = 0xB0,
    }

    /// <summary>
    /// アクセスタイプ
    /// </summary>
    private enum eAccesstype
    {
        Read = 0,
        Write
    }

    /// <summary>
    /// ビットレジスタ定義
    /// </summary>
    protected override List<string> regTypeBit
    {
        get
        {
            return new List<string>(new string[] { "M", "X", "Y", "L", "B" });
        }
    }

    /// <summary>
    /// ビットレジスタ定義
    /// </summary>
    protected override List<string> regTypeBit16
    {
        get
        {
            return new List<string>(new string[] { "X", "Y", "B" });
        }
    }

    /// <summary>
    /// 16bitレジスタ定義
    /// </summary>
    protected override List<string> regTypeData16
    {
        get
        {
            return new List<string>(new string[] { "D", "W", "R", "ZR" });
        }
    }

    /// <summary>
    /// 一括受信設定
    /// </summary>
    public override int BULK_RCV_COUNT
    {
        get
        {
            return 960;
        }
    }

    /// <summary>
    /// ビット数
    /// </summary>
    public override int BIT_COUNT
    {
        get
        {
            return 16;
        }
    }

    /// <summary>
    /// 読み出しコマンド
    /// </summary>
    protected ushort ReadCommand = 0x0401;

    /// <summary>
    /// 書き込みコマンド
    /// </summary>
    protected ushort WriteCommand = 0x1401;

    /// <summary>
    /// 固定コマンド
    /// </summary>
    protected ushort ReadSubCommand = 0;

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Start()
    {
        base.Start();

        if (!GlobalScript.mcprotocols.ContainsKey(Name))
        {
            GlobalScript.mcprotocols.Add(Name, this);
        }
    }

    /// <summary>
    /// 電文作成
    /// </summary>
    /// <param name="data"></param>
    /// <param name="values"></param>
    /// <returns></returns>
    protected override List<byte> CreateMessage(KMXDBSetting data, ref int commandId, List<ulong> values = null)
    {
        // 要求電文
        var message = new List<byte>
        {
            // サブヘッダ
            0x50, 0x00,
            // ネットワーク番号
            (byte)directData.NetAddress,
            // PC番号
            (byte)directData.PcNo,
            // 要求先ユニットI/O番号
            0xFF, 0x03,
            // 要求先ユニット局番号
            0x00
        };
        // 本文
        var body = new List<byte>();
        byte deviceType = (byte)Enum.Parse(typeof(eDeviceType), data.RegisterType);
        ushort command = values == null ? ReadCommand : WriteCommand;
        ushort subcommand = values == null ? ReadSubCommand : (ushort)(regTypeBit.Contains(data.RegisterType) ? 1 : 0);

        // 監視タイマ
        ushort timer = 0x0010;
        body.AddRange(BitConverter.GetBytes(timer));
        // コマンド
        body.AddRange(BitConverter.GetBytes(command));
        // サブコマンド
        body.AddRange(BitConverter.GetBytes(subcommand));
        // 先頭デバイス番号
        body.AddRange(BitConverter.GetBytes(data.RegisterNo));
        body.RemoveAt(body.Count - 1);
        // デバイスコード
        body.Add(deviceType);
        // デバイス点数
        if (values == null)
        {
            // リード
            var dataCount = data.AllDataCount;
            if (regTypeBit.Contains(data.RegisterType))
            {
                // ビットなら
                if (dataCount % BIT_COUNT == 0)
                {
                    dataCount /= BIT_COUNT;
                }
                else
                {
                    dataCount = (int)Math.Ceiling((decimal)dataCount / BIT_COUNT);
                }
            }
            body.AddRange(BitConverter.GetBytes((ushort)dataCount));
        }
        else
        {
            // ライト
            body.AddRange(BitConverter.GetBytes((ushort)values.Count));
            if (subcommand == 1)
            {
                // ビットデバイス用
                for (int i = 0; i < values.Count; i += 2)
                {
                    byte tmp = 0;
                    if (values.Count > i + 1)
                    {
                        tmp = (byte)values[i + 1];
                    }
                    tmp += (byte)(values[i] << 4);
                    body.Add(tmp);
                }
            }
            else
            {
                // ワードデバイス用
                for (int i = 0; i < values.Count; i++)
                {
                    body.AddRange(BitConverter.GetBytes((ushort)values[i]));
                }
            }
        }
        // コマンド長
        message.AddRange(BitConverter.GetBytes((ushort)body.Count));
        // 本文
        message.AddRange(body);
        return message;
    }

    /// <summary>
    /// 受信データ分析処理
    /// </summary>
    /// <param name="datas"></param>
    /// <returns></returns>
    protected override bool AnalysysMessage(KMXDBSetting data, List<byte> datas)
    {
        var buff = datas.ToArray();
        var errCode = BitConverter.ToInt16(buff, 0);
        // 終了コードが0なら受信成功
        if (errCode == 0)
        {
            var index = 0;
            if (regTypeBit.Contains(data.RegisterType))
            {
                // ビットデータ
                for (var i = 2; i < buff.Length; i++)
                {
                    for (var j = 0; j < 8; j++)
                    {
                        var value = (buff[i] & (1 << j)) != 0 ? 1 : 0;
                        if (index < data.values.Count)
                        {
                            if (data.values[index] != null)
                            {
                                data.values[index].Value = value;
                            }
                        }
                        else
                        {
                            break;
                        }
                        index++;
                    }
                }
            }
            else
            {
                // ワードデータ
                for (var i = 2; i < buff.Length; i += sizeof(ushort))
                {
                    if (index < data.values.Count)
                    {
                        if (data.values[index] != null)
                        {
                            data.values[index].Value = BitConverter.ToInt16(buff, i);
                        }
                    }
                    else
                    {
                        break;
                    }
                    index++;
                }
            }
        }
        else
        {
            return false;
        }
        return true;
    }
}
