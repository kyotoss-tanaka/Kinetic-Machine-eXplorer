using Parameters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
            body.AddRange(BitConverter.GetBytes((ushort)GetReadPoints(data)));
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
    /// 応答の全長（3Eフレーム・バイナリ：サブヘッダD0 00、応答データ長=オフセット7の2バイト、ヘッダ9バイト）
    /// </summary>
    protected override int GetFrameLength(byte[] buffer, int size)
    {
        if ((size < 2) || (buffer[0] != 0xD0) || (buffer[1] != 0x00))
        {
            return -1;
        }
        if (size < 9)
        {
            // まずヘッダ（データ長まで）をそろえる
            return 9;
        }
        return 9 + BitConverter.ToUInt16(buffer, 7);
    }

    /// <summary>
    /// 読出し点数（ワード単位。ビットデバイスは16点=1点、DW=2点、QW=4点）
    /// </summary>
    private int GetReadPoints(KMXDBSetting data)
    {
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
        else
        {
            if (data.DataType == DBSetting.eDeviceSize.DW)
            {
                dataCount *= 2;
            }
            else if (data.DataType == DBSetting.eDeviceSize.QW)
            {
                dataCount *= 4;
            }
        }
        return dataCount;
    }

    #region 複数ブロック一括読出し（0406）
    // 根拠：MELSEC Communication Protocol Reference Manual (SH-080008) 8.4 Batch Read and Write Multiple Blocks
    //  ・ワードブロック数＋ビットブロック数 ≦ 120（サブコマンド0000。Q/L・iQ-R・iQ-L共通）
    //  ・全ブロックの点数合計 1〜960（ワードは1ワード=1点、ビットは16点=1点）
    //  ・要求：ワードブロック数(1B) ビットブロック数(1B) 各ブロック[先頭デバイス番号(3B) デバイスコード(1B) 点数(2B)]
    //    をワードブロック→ビットブロックの順に並べる。応答も同じ順にデータが並ぶ

    /// <summary>
    /// 複数ブロック一括読出しコマンド
    /// </summary>
    private const ushort MultiBlockReadCommand = 0x0406;

    /// <summary>
    /// 1回の要求で指定できるブロック数の上限
    /// </summary>
    private const int MultiBlockMax = 120;

    /// <summary>
    /// 1回の要求で読み出せる点数の上限（ブロックの合計）
    /// </summary>
    private const int MultiPointMax = 960;

    /// <summary>
    /// 読込計画（1要素=1往復。中身のブロックが1個なら0401、複数なら0406）
    /// </summary>
    private List<List<KMXDBSetting>> readPlan;

    /// <summary>
    /// 初回読込（書込タグの現在値）の計画
    /// </summary>
    private List<List<KMXDBSetting>> readPlanFirst;

    /// <summary>
    /// 相手が0406に応答しなかったら以後は0401（ブロックごと）に戻す
    /// </summary>
    private bool multiBlockDisabled;

    /// <summary>
    /// 受信処理：ブロックを往復回数が最少になるよう0406にまとめて読む
    /// </summary>
    protected override bool Recieve()
    {
        readPlan ??= CreateReadPlan(dctReadSortedTags1, "読込");
        var ret = ReadByPlan(readPlan);
        if (!IsConnected)
        {
            return false;
        }
        if (isFirst)
        {
            // 初回のみ書き込みデータ受信
            readPlanFirst ??= CreateReadPlan(dctReadSortedTags2, "初回読込");
            ret &= ReadByPlan(readPlanFirst);
        }
        return ret;
    }

    /// <summary>
    /// 読込計画を作る。ブロック（0401で1回に読める単位）を、点数の大きい順に
    /// 「点数合計≦960・ブロック数≦120」を満たす要求へ詰める（First Fit Decreasing）
    /// </summary>
    private List<List<KMXDBSetting>> CreateReadPlan(Dictionary<string, List<KMXDBSetting>> dct, string label)
    {
        var bins = new List<List<KMXDBSetting>>();
        var binPoints = new List<int>();
        var blocks = dct.Values.SelectMany(d => d).OrderByDescending(GetReadPoints).ToList();
        foreach (var block in blocks)
        {
            var points = GetReadPoints(block);
            var index = -1;
            for (var i = 0; i < bins.Count; i++)
            {
                if ((binPoints[i] + points <= MultiPointMax) && (bins[i].Count < MultiBlockMax))
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                bins.Add(new List<KMXDBSetting>());
                binPoints.Add(0);
                index = bins.Count - 1;
            }
            bins[index].Add(block);
            binPoints[index] += points;
        }
        // 要求内はワードブロック→ビットブロックの順（電文と応答の並び）
        var plan = bins.Select(b => b.Where(d => !regTypeBit.Contains(d.RegisterType))
                                     .Concat(b.Where(d => regTypeBit.Contains(d.RegisterType))).ToList()).ToList();
        Debug.Log($"[ComBlocks] {GetType().Name} {Server}:{Port} {label}計画: ブロック{blocks.Count}個 → 往復{plan.Count}回 " +
                  string.Join(" ", plan.Select((b, i) => $"[{i}]{(b.Count == 1 ? "0401" : "0406")} {b.Count}ブロック {b.Sum(GetReadPoints)}点")));
        return plan;
    }

    /// <summary>
    /// 読込計画どおりに読む
    /// </summary>
    private bool ReadByPlan(List<List<KMXDBSetting>> plan)
    {
        var ret = true;
        foreach (var request in plan)
        {
            if ((request.Count == 1) || multiBlockDisabled)
            {
                // 1ブロックなら従来の一括読出し（0401）
                foreach (var block in request)
                {
                    var commandId = 0;
                    ret &= Read(block, ref commandId);
                    if (!IsConnected)
                    {
                        return false;
                    }
                }
                continue;
            }
            var buff = SendCommand(CreateMultiBlockMessage(request));
            if (!IsConnected)
            {
                return false;
            }
            var bytes = buff.ToArray();
            var errCode = (bytes.Length >= 2) ? BitConverter.ToUInt16(bytes, 0) : (ushort)0xFFFF;
            if (errCode != 0)
            {
                // 0406 に対応していない相手（異常終了コード）→ 以後はブロックごとの0401に戻す
                multiBlockDisabled = true;
                Debug.LogWarning($"[ComBlocks] {GetType().Name} {Server}:{Port} 複数ブロック一括読出し(0406)が異常終了しました" +
                                 $"（終了コード=0x{errCode:X4}）。以後は一括読出し(0401)で通信します");
                foreach (var block in request)
                {
                    var commandId = 0;
                    ret &= Read(block, ref commandId);
                    if (!IsConnected)
                    {
                        return false;
                    }
                }
                continue;
            }
            // 応答をブロックごとに切り分け、終了コードを付けて従来の解析に渡す
            var expected = 2 + request.Sum(GetReadPoints) * 2;
            if (bytes.Length < expected)
            {
                Debug.LogWarning($"[ComBlocks] {GetType().Name} {Server}:{Port} 0406の応答長が不足しています（{bytes.Length}/{expected}バイト）");
                ret = false;
                continue;
            }
            var offset = 2;
            foreach (var block in request)
            {
                var size = GetReadPoints(block) * 2;
                var part = new List<byte>(size + 2) { 0x00, 0x00 };
                for (var i = 0; i < size; i++)
                {
                    part.Add(bytes[offset + i]);
                }
                offset += size;
                ret &= AnalysysMessage(block, part);
            }
        }
        return ret;
    }

    /// <summary>
    /// 複数ブロック一括読出し(0406)の要求電文（3Eフレーム・バイナリ）
    /// </summary>
    /// <param name="request">ワードブロック→ビットブロックの順に並んだブロック</param>
    private List<byte> CreateMultiBlockMessage(List<KMXDBSetting> request)
    {
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
        var body = new List<byte>();
        // 監視タイマ
        body.AddRange(BitConverter.GetBytes((ushort)0x0010));
        // コマンド・サブコマンド
        body.AddRange(BitConverter.GetBytes(MultiBlockReadCommand));
        body.AddRange(BitConverter.GetBytes((ushort)0x0000));
        // ワードブロック数・ビットブロック数
        body.Add((byte)request.Count(d => !regTypeBit.Contains(d.RegisterType)));
        body.Add((byte)request.Count(d => regTypeBit.Contains(d.RegisterType)));
        foreach (var block in request)
        {
            // 先頭デバイス番号(3バイト)・デバイスコード・点数
            var no = BitConverter.GetBytes(block.RegisterNo);
            body.Add(no[0]);
            body.Add(no[1]);
            body.Add(no[2]);
            body.Add((byte)Enum.Parse(typeof(eDeviceType), block.RegisterType));
            body.AddRange(BitConverter.GetBytes((ushort)GetReadPoints(block)));
        }
        // 要求データ長
        message.AddRange(BitConverter.GetBytes((ushort)body.Count));
        message.AddRange(body);
        return message;
    }
    #endregion 複数ブロック一括読出し（0406）

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
                var size = data.DataType == DBSetting.eDeviceSize.DW ? sizeof(int) : (data.DataType == DBSetting.eDeviceSize.QW ? sizeof(long) : sizeof(short));
                var isUnit = data.DataType == DBSetting.eDeviceSize.UnitTag;
                for (var i = 2; i < buff.Length; i += size)
                {
                    if (index < data.values.Count)
                    {
                        if (data.values[index] != null)
                        {
                            var dataType = data.DataType;
                            if (isUnit)
                            {
                                if (data.values[index].Size == 2)
                                {
                                    dataType = DBSetting.eDeviceSize.DW;
                                }
                                else if (data.values[index].Size == 4)
                                {
                                    dataType = DBSetting.eDeviceSize.QW;
                                }
                                size = data.values[index].Size * 2;
                            }
                            if (dataType == DBSetting.eDeviceSize.DW)
                            {
                                data.values[index].Value = BitConverter.ToInt32(buff, i);
                            }
                            else if (dataType == DBSetting.eDeviceSize.QW)
                            {
                                data.values[index].Value = (int)BitConverter.ToInt64(buff, i);
                            }
                            else
                            {
                                data.values[index].Value = BitConverter.ToInt16(buff, i);
                            }
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
