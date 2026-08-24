using Parameters;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// EtherNet/IP (CIP over TCP) 通信クラス
/// Explicit Messaging (UCMM / SendRRData) によるシンボリックタグの読み書きを行う。
/// アドレスは NodeId（KMXTool出力の標準形式。OPC UAと同様）を優先し、
/// NodeIdが空の場合は「配列タグ名(RegisterType) + 添字(RegisterNo)」方式（docs/ComEthernetIP仕様.md §5.1）。
/// </summary>
public class ComEthernetIP : ComProtocolBase
{
    #region 定数
    /// <summary>カプセル化コマンド：セッション確立</summary>
    private const ushort EIP_CMD_REGISTER_SESSION = 0x0065;
    /// <summary>カプセル化コマンド：セッション解放</summary>
    private const ushort EIP_CMD_UNREGISTER_SESSION = 0x0066;
    /// <summary>カプセル化コマンド：CIP要求/応答</summary>
    private const ushort EIP_CMD_SEND_RR_DATA = 0x006F;

    /// <summary>CIPサービス：Read Tag</summary>
    private const byte CIP_SERVICE_READ = 0x4C;
    /// <summary>CIPサービス：Write Tag</summary>
    private const byte CIP_SERVICE_WRITE = 0x4D;

    /// <summary>CIPデータ型：BOOL</summary>
    private const ushort CIP_TYPE_BOOL = 0x00C1;
    /// <summary>CIPデータ型：INT(16bit)</summary>
    private const ushort CIP_TYPE_INT = 0x00C3;
    /// <summary>CIPデータ型：DINT(32bit)</summary>
    private const ushort CIP_TYPE_DINT = 0x00C4;
    /// <summary>CIPデータ型：LINT(64bit)</summary>
    private const ushort CIP_TYPE_LINT = 0x00C5;
    /// <summary>CIPデータ型：UINT(16bit符号なし)</summary>
    private const ushort CIP_TYPE_UINT = 0x00C7;
    /// <summary>CIPデータ型：UDINT(32bit符号なし)</summary>
    private const ushort CIP_TYPE_UDINT = 0x00C8;
    /// <summary>CIPデータ型：REAL(単精度実数)</summary>
    private const ushort CIP_TYPE_REAL = 0x00CA;
    /// <summary>CIPデータ型：LREAL(倍精度実数)</summary>
    private const ushort CIP_TYPE_LREAL = 0x00CB;
    /// <summary>CIPデータ型：DWORD(32bitビット列。BOOL配列のパック単位)</summary>
    private const ushort CIP_TYPE_DWORD = 0x00D3;

    /// <summary>カプセル化ヘッダ長</summary>
    private const int EIP_HEADER_SIZE = 24;
    /// <summary>SendRRData応答のCIP応答開始オフセット（ヘッダ24 + InterfaceHandle4 + Timeout2 + CPF10）</summary>
    private const int CIP_RESPONSE_OFFSET = 40;
    #endregion 定数

    #region 変数
    /// <summary>
    /// セッションハンドル（Register Sessionで取得。0は未確立）
    /// </summary>
    private uint sessionHandle = 0;

    /// <summary>
    /// コマンドID（Sender Contextに載せる要求識別子）
    /// </summary>
    private int _commandId = 0;

    /// <summary>
    /// ビットレジスタ定義（接続先タグから動的構築）
    /// </summary>
    private List<string> lstRegTypeBit = new();

    /// <summary>
    /// 16bitレジスタ定義（接続先タグから動的構築）
    /// </summary>
    private List<string> lstRegTypeData16 = new();

    /// <summary>
    /// 32bitレジスタ定義（接続先タグから動的構築）
    /// </summary>
    private List<string> lstRegTypeData32 = new();

    /// <summary>
    /// 64bitレジスタ定義（接続先タグから動的構築）
    /// </summary>
    private List<string> lstRegTypeData64 = new();

    /// <summary>
    /// 読み出し応答から学習したタグごとのCIPデータ型（書き込み時の型決定に使用。受信/送信は別スレッドのためConcurrent）
    /// </summary>
    private ConcurrentDictionary<string, ushort> dctCipTypes = new();
    #endregion 変数

    /// <summary>
    /// ビットレジスタ定義
    /// </summary>
    protected override List<string> regTypeBit
    {
        get
        {
            return lstRegTypeBit;
        }
    }

    /// <summary>
    /// 16bitレジスタ定義
    /// </summary>
    protected override List<string> regTypeData16
    {
        get
        {
            return lstRegTypeData16;
        }
    }

    /// <summary>
    /// 32bitレジスタ定義
    /// </summary>
    protected override List<string> regTypeData32
    {
        get
        {
            return lstRegTypeData32;
        }
    }

    /// <summary>
    /// 64bitレジスタ定義
    /// </summary>
    protected override List<string> regTypeData64
    {
        get
        {
            return lstRegTypeData64;
        }
    }

    /// <summary>
    /// 一括受信カウント（UCMMの実用上限 約504バイト → INT 240個 = 480バイト）
    /// </summary>
    public override int BULK_RCV_COUNT
    {
        get
        {
            return 240;
        }
    }

    /// <summary>
    /// ビット数（CIPのBOOL配列はDWORD(32bit)単位でパックされる）
    /// </summary>
    public override int BIT_COUNT
    {
        get
        {
            return 32;
        }
    }

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Start()
    {
        base.Start();

        if (!GlobalScript.ethernetips.ContainsKey(Name))
        {
            GlobalScript.ethernetips.Add(Name, this);
        }
    }

    /// <summary>
    /// パラメータセット
    /// regType*を接続先タグのDataTypeから動的構築する。
    /// base.SetParameter()内でCreateSortedData()が呼ばれるため、リスト構築はbase呼び出しより前に行う。
    /// </summary>
    public override void SetParameter(int No, int Cycle, string Server, int Port, string Database, string User, string Password, bool isClientMode, DataExchangeSetting dataExchange, PostgresSetting.KmxDirectData directData)
    {
        lstRegTypeBit.Clear();
        lstRegTypeData16.Clear();
        lstRegTypeData32.Clear();
        lstRegTypeData64.Clear();
        foreach (var tag in directData.tags)
        {
            switch (tag.DataType)
            {
                case DBSetting.eDeviceSize.Bit:
                case DBSetting.eDeviceSize.Bool:
                    AddRegType(lstRegTypeBit, tag.RegisterType);
                    break;
                case DBSetting.eDeviceSize.W:
                    AddRegType(lstRegTypeData16, tag.RegisterType);
                    break;
                case DBSetting.eDeviceSize.QW:
                case DBSetting.eDeviceSize.LW:
                    AddRegType(lstRegTypeData64, tag.RegisterType);
                    break;
                default:
                    // DW / UnitTag / その他は32bit扱い
                    AddRegType(lstRegTypeData32, tag.RegisterType);
                    break;
            }
        }
        base.SetParameter(No, Cycle, Server, Port, Database, User, Password, isClientMode, dataExchange, directData);
    }

    /// <summary>
    /// レジスタ種別リストへ重複なしで追加
    /// </summary>
    private void AddRegType(List<string> list, string registerType)
    {
        if (!string.IsNullOrEmpty(registerType) && !list.Contains(registerType))
        {
            list.Add(registerType);
        }
    }

    /// <summary>
    /// ソートデータ作成
    /// NodeId指定タグ（KMXTool出力の標準形式。RegisterTypeが空）は個別のCIPタグのため、
    /// タグ単位でチャンク化してマージさせない（baseはRegisterTypeをキーにするため空文字で全タグが結合されてしまう）。
    /// </summary>
    protected override void CreateSortedData()
    {
        if (!GlobalScript.tagDatas.ContainsKey(Name))
        {
            GlobalScript.tagDatas.Add(Name, new Dictionary<string, Dictionary<string, TagInfo>>());
        }
        Dictionary<string, List<KMXDBSetting>> dctReadTags1 = new();
        Dictionary<string, List<KMXDBSetting>> dctReadTags2 = new();
        foreach (var tag in directData.tags)
        {
            // NodeId指定タグはDataTag（一意）をキーにして1タグ=1チャンクにする
            var key = string.IsNullOrEmpty(tag.NodeId) ? tag.RegisterType : tag.DataTag;
            if (tag.IsWrite)
            {
                if (!dctReadTags2.ContainsKey(key))
                {
                    dctReadTags2.Add(key, new List<KMXDBSetting>());
                }
                dctReadTags2[key].Add((KMXDBSetting)tag.Clone());
                if (!dctWriteSortedTags.ContainsKey(key))
                {
                    dctWriteSortedTags.Add(key, new List<KMXDBSetting>());
                }
                dctWriteSortedTags[key].Add((KMXDBSetting)tag.Clone());
            }
            else
            {
                if (!dctReadTags1.ContainsKey(key))
                {
                    dctReadTags1.Add(key, new List<KMXDBSetting>());
                }
                dctReadTags1[key].Add((KMXDBSetting)tag.Clone());
            }
            // DB登録
            SetDbData(tag);
        }
        CreateSorted(dctReadTags1, ref dctReadSortedTags1);
        CreateSorted(dctReadTags2, ref dctReadSortedTags2);
        // ソートされたタグにDBデータをセット
        foreach (var tags in dctWriteSortedTags)
        {
            foreach (var tag in tags.Value)
            {
                SetDbPointer(tag);
            }
        }
    }

    /// <summary>
    /// 接続処理
    /// TCP接続後にRegister Sessionを実行してセッションハンドルを取得する。
    /// </summary>
    /// <returns></returns>
    protected override bool Connect()
    {
        if (base.Connect())
        {
            if (sessionHandle != 0)
            {
                // セッション確立済み
                return true;
            }
            return RegisterSession();
        }
        return false;
    }

    /// <summary>
    /// 切断処理
    /// UnRegister Sessionを送信してからTCPを閉じる。
    /// </summary>
    protected override void Disconnect()
    {
        if ((sessionHandle != 0) && (tcp._tcpClient != null))
        {
            // UnRegister Session（応答なし）
            var pkt = new List<byte>();
            pkt.AddRange(BitConverter.GetBytes(EIP_CMD_UNREGISTER_SESSION));
            pkt.AddRange(BitConverter.GetBytes((ushort)0));     // Length
            pkt.AddRange(BitConverter.GetBytes(sessionHandle)); // Session Handle
            pkt.AddRange(BitConverter.GetBytes((uint)0));       // Status
            pkt.AddRange(new byte[8]);                          // Sender Context
            pkt.AddRange(BitConverter.GetBytes((uint)0));       // Options
            lock (m_ComLock)
            {
                StreamWrite(pkt);
            }
        }
        sessionHandle = 0;
        base.Disconnect();
    }

    /// <summary>
    /// Register Session実行
    /// </summary>
    /// <returns></returns>
    private bool RegisterSession()
    {
        var pkt = new List<byte>();
        pkt.AddRange(BitConverter.GetBytes(EIP_CMD_REGISTER_SESSION));
        pkt.AddRange(BitConverter.GetBytes((ushort)4)); // Length
        pkt.AddRange(BitConverter.GetBytes((uint)0));   // Session Handle
        pkt.AddRange(BitConverter.GetBytes((uint)0));   // Status
        pkt.AddRange(new byte[8]);                      // Sender Context
        pkt.AddRange(BitConverter.GetBytes((uint)0));   // Options
        pkt.AddRange(BitConverter.GetBytes((ushort)1)); // Protocol Version
        pkt.AddRange(BitConverter.GetBytes((ushort)0)); // Options Flags
        lock (m_ComLock)
        {
            if (!StreamWrite(pkt))
            {
                base.Disconnect();
                return false;
            }
            int size = 0;
            if (!StreamRead(readBuff, ref size))
            {
                base.Disconnect();
                return false;
            }
            if (size < EIP_HEADER_SIZE + 4)
            {
                return false;
            }
            if (BitConverter.ToUInt32(readBuff, 8) != 0)
            {
                // カプセル化Statusエラー
                CommonFunction.DebugLog($"EtherNet/IP RegisterSession失敗: Status=0x{BitConverter.ToUInt32(readBuff, 8):X8}");
                return false;
            }
            sessionHandle = BitConverter.ToUInt32(readBuff, 4);
            return sessionHandle != 0;
        }
    }

    /// <summary>
    /// 電文作成
    /// values == null なら Read Tag(0x4C)、非nullなら Write Tag(0x4D) の要求電文を組み立てる。
    /// </summary>
    /// <param name="data"></param>
    /// <param name="commandId"></param>
    /// <param name="values"></param>
    /// <returns></returns>
    protected override List<byte> CreateMessage(KMXDBSetting data, ref int commandId, List<ulong> values = null)
    {
        var message = new List<byte>();
        if (sessionHandle == 0)
        {
            return message;
        }
        _commandId++;
        commandId = _commandId;
        var isBit = regTypeBit.Contains(data.RegisterType);
        var cip = new List<byte>();
        if (values == null)
        {
            // Read Tag
            // ビット種別はDWORD(32bit)単位のパック領域として添字・要素数を換算する
            var address = isBit ? (int)Math.Floor(data.RegisterNo / (double)BIT_COUNT) : data.RegisterNo;
            var count = isBit ? (int)Math.Ceiling((data.AllDataCount + (data.RegisterNo - address * BIT_COUNT)) / (double)BIT_COUNT) : data.AllDataCount;
            var path = MakeTagPath(data, address);
            cip.Add(CIP_SERVICE_READ);
            cip.Add((byte)(path.Count / 2));
            cip.AddRange(path);
            cip.AddRange(BitConverter.GetBytes((ushort)count));
        }
        else
        {
            // Write Tag
            var cipType = GetWriteCipType(data);
            var elemSize = GetElementSize(cipType);
            if (isBit)
            {
                // ビット種別はDWORD単位でパックして書き込む（RegisterNoは32bit境界前提）
                var address = data.RegisterNo / BIT_COUNT;
                var path = MakeTagPath(data, address);
                var packed = new List<uint>();
                for (var i = 0; i < values.Count; i++)
                {
                    if (i % BIT_COUNT == 0)
                    {
                        packed.Add(0);
                    }
                    if (values[i] != 0)
                    {
                        packed[packed.Count - 1] |= (uint)1 << (i % BIT_COUNT);
                    }
                }
                cip.Add(CIP_SERVICE_WRITE);
                cip.Add((byte)(path.Count / 2));
                cip.AddRange(path);
                cip.AddRange(BitConverter.GetBytes(CIP_TYPE_DWORD));
                cip.AddRange(BitConverter.GetBytes((ushort)packed.Count));
                foreach (var v in packed)
                {
                    cip.AddRange(BitConverter.GetBytes(v));
                }
            }
            else
            {
                var path = MakeTagPath(data, data.RegisterNo);
                cip.Add(CIP_SERVICE_WRITE);
                cip.Add((byte)(path.Count / 2));
                cip.AddRange(path);
                cip.AddRange(BitConverter.GetBytes(cipType));
                cip.AddRange(BitConverter.GetBytes((ushort)values.Count));
                foreach (var v in values)
                {
                    // 受け取ったulongの下位バイトをそのまま載せる（floatはIEEE754ビット列のまま転送する）
                    switch (elemSize)
                    {
                        case 1:
                            // BOOLは0x00/0xFFが慣例
                            cip.Add((cipType == CIP_TYPE_BOOL) ? (byte)((v != 0) ? 0xFF : 0x00) : (byte)v);
                            break;
                        case 2:
                            cip.AddRange(BitConverter.GetBytes((ushort)v));
                            break;
                        case 8:
                            cip.AddRange(BitConverter.GetBytes(v));
                            break;
                        default:
                            cip.AddRange(BitConverter.GetBytes((uint)v));
                            break;
                    }
                }
            }
        }
        // CPF + カプセル化ヘッダで包む
        return WrapEipPacket(cip, commandId);
    }

    /// <summary>
    /// CIP要求をCPF + カプセル化ヘッダ(SendRRData)で包む
    /// </summary>
    /// <param name="cipData"></param>
    /// <param name="commandId"></param>
    /// <returns></returns>
    private List<byte> WrapEipPacket(List<byte> cipData, int commandId)
    {
        // Common Packet Format (CPF)
        var cpf = new List<byte>();
        cpf.AddRange(BitConverter.GetBytes((ushort)2));               // Item Count
        cpf.AddRange(BitConverter.GetBytes((ushort)0x0000));          // Item1: Null Address Item
        cpf.AddRange(BitConverter.GetBytes((ushort)0));               // Item1: Length = 0
        cpf.AddRange(BitConverter.GetBytes((ushort)0x00B2));          // Item2: Unconnected Data Item
        cpf.AddRange(BitConverter.GetBytes((ushort)cipData.Count));   // Item2: Length
        cpf.AddRange(cipData);

        // カプセル化ヘッダ + Interface Handle + Timeout
        var pkt = new List<byte>();
        pkt.AddRange(BitConverter.GetBytes(EIP_CMD_SEND_RR_DATA));    // Command
        pkt.AddRange(BitConverter.GetBytes((ushort)(cpf.Count + 6))); // Length
        pkt.AddRange(BitConverter.GetBytes(sessionHandle));           // Session Handle
        pkt.AddRange(BitConverter.GetBytes((uint)0));                 // Status
        pkt.AddRange(BitConverter.GetBytes((long)commandId));         // Sender Context（要求識別子）
        pkt.AddRange(BitConverter.GetBytes((uint)0));                 // Options
        pkt.AddRange(BitConverter.GetBytes((uint)0));                 // Interface Handle
        pkt.AddRange(BitConverter.GetBytes((ushort)0));               // Timeout
        pkt.AddRange(cpf);
        return pkt;
    }

    /// <summary>
    /// 応答からCIP応答部を切り出す
    /// カプセル化ヘッダ(24) + Interface Handle(4) + Timeout(2) + CPF(10) を読み飛ばす。
    /// </summary>
    /// <param name="buff"></param>
    /// <param name="size"></param>
    /// <returns></returns>
    protected override List<byte> ExtractPayload(byte[] buff, int size)
    {
        var lstTmp = new List<byte>();
        if (size < EIP_HEADER_SIZE)
        {
            return lstTmp;
        }
        if (BitConverter.ToUInt32(buff, 8) != 0)
        {
            // カプセル化Statusエラー
            return lstTmp;
        }
        if (size <= CIP_RESPONSE_OFFSET)
        {
            return lstTmp;
        }
        for (var i = CIP_RESPONSE_OFFSET; i < size; i++)
        {
            lstTmp.Add(buff[i]);
        }
        return lstTmp;
    }

    /// <summary>
    /// 受信データ分析処理
    /// datas: [0]=Serviceエコー, [1]=Reserved, [2]=General Status, [3]=Additional Status Size,
    ///        以降 CIPデータ型(2バイト) + 値列
    /// </summary>
    /// <param name="data"></param>
    /// <param name="datas"></param>
    /// <returns></returns>
    protected override bool AnalysysMessage(KMXDBSetting data, List<byte> datas)
    {
        var buff = datas.ToArray();
        if (buff.Length < 4)
        {
            return false;
        }
        if (buff[2] != 0x00)
        {
            // General Statusエラー
            return false;
        }
        if ((buff[0] & 0x7F) == CIP_SERVICE_WRITE)
        {
            // 書き込み応答はステータスのみ
            return true;
        }
        var top = 4 + buff[3] * 2;
        if (buff.Length < top + 2)
        {
            return false;
        }
        var cipType = BitConverter.ToUInt16(buff, top);
        top += 2;
        // 応答のデータ型を学習（書き込み時の型決定に使用）
        dctCipTypes[data.DataTag] = cipType;
        var index = 0;
        if (regTypeBit.Contains(data.RegisterType))
        {
            // ビットデータ
            if (GetElementSize(cipType) == 1)
            {
                // BOOL単体形式（1要素1バイト）で返された場合
                for (var i = top; i < buff.Length; i++)
                {
                    if (index >= data.values.Count)
                    {
                        break;
                    }
                    if (data.values[index] != null)
                    {
                        data.values[index].Value = (buff[i] != 0) ? 1 : 0;
                        data.values[index].isFloat = false;
                    }
                    index++;
                }
            }
            else
            {
                // DWORDパック領域をビット展開
                // 要求はDWORD境界に切り下げているため、先頭の余りビットを読み飛ばす
                var skip = data.RegisterNo % BIT_COUNT;
                var bitIndex = 0;
                for (var i = top; i + 4 <= buff.Length; i += 4)
                {
                    var dword = BitConverter.ToUInt32(buff, i);
                    for (var j = 0; j < BIT_COUNT; j++)
                    {
                        if (bitIndex >= skip)
                        {
                            if (index >= data.values.Count)
                            {
                                return true;
                            }
                            if (data.values[index] != null)
                            {
                                data.values[index].Value = (int)((dword >> j) & 1);
                                data.values[index].isFloat = false;
                            }
                            index++;
                        }
                        bitIndex++;
                    }
                }
            }
        }
        else
        {
            // ワードデータ（応答のCIPデータ型で要素幅と実数判定を行う）
            var elemSize = GetElementSize(cipType);
            for (var i = top; i + elemSize <= buff.Length; i += elemSize)
            {
                if (index >= data.values.Count)
                {
                    break;
                }
                if (data.values[index] != null)
                {
                    if (cipType == CIP_TYPE_REAL)
                    {
                        // float: Value(切り捨て) / fValue(本体) / isFloat の3点セットで格納
                        var f = BitConverter.ToSingle(buff, i);
                        data.values[index].Value = (int)f;
                        data.values[index].fValue = f;
                        data.values[index].isFloat = true;
                    }
                    else if (cipType == CIP_TYPE_LREAL)
                    {
                        // LREALは単精度に丸めて格納（ComOpcUaと同じ扱い）
                        var d = BitConverter.ToDouble(buff, i);
                        data.values[index].Value = (int)d;
                        data.values[index].fValue = (float)d;
                        data.values[index].isFloat = true;
                    }
                    else
                    {
                        switch (elemSize)
                        {
                            case 1:
                                // BOOLは0xFF=ONの慣例のため0/1に正規化する
                                data.values[index].Value = (cipType == CIP_TYPE_BOOL) ? ((buff[i] != 0) ? 1 : 0) : buff[i];
                                break;
                            case 2:
                                data.values[index].Value = BitConverter.ToInt16(buff, i);
                                break;
                            case 8:
                                data.values[index].Value = (int)BitConverter.ToInt64(buff, i);
                                break;
                            default:
                                data.values[index].Value = BitConverter.ToInt32(buff, i);
                                break;
                        }
                        // 整数型は明示的にfalseを代入する（floatの残留による非対称バグ防止）
                        data.values[index].isFloat = false;
                    }
                }
                index++;
            }
        }
        return true;
    }

    /// <summary>
    /// データ書き込み
    /// 基底実装はValue(int)のみを転送するため、floatタグはIEEE754ビット列をulongへ詰めて転送する。
    /// </summary>
    /// <param name="data"></param>
    /// <param name="commandId"></param>
    /// <returns></returns>
    protected override bool Write(KMXDBSetting data, ref int commandId)
    {
        var values = new List<ulong>();
        var is64 = regTypeData64.Contains(data.RegisterType);
        foreach (var tag in data.values)
        {
            if (tag == null)
            {
                values.Add(0);
            }
            else if (tag.isFloat)
            {
                // IEEE754のビットパターンをそのまま転送（値変換ではない）
                if (is64)
                {
                    values.Add(BitConverter.ToUInt64(BitConverter.GetBytes((double)tag.fValue), 0));
                }
                else
                {
                    values.Add(BitConverter.ToUInt32(BitConverter.GetBytes(tag.fValue), 0));
                }
            }
            else
            {
                values.Add((ulong)tag.Value);
            }
        }
        var message = CreateMessage(data, ref commandId, values);
        if (message.Count > 0)
        {
            // データ送信処理
            var buff = SendCommand(message);
            if (buff.Count >= 2)
            {
                // 受信データ分析処理
                return AnalysysMessage(data, buff);
            }
        }
        return false;
    }

    /// <summary>
    /// 書き込み時のCIPデータ型を決定する
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    private ushort GetWriteCipType(KMXDBSetting data)
    {
        if (regTypeBit.Contains(data.RegisterType))
        {
            return CIP_TYPE_DWORD;
        }
        // 初回読み出しで学習した型があればそれを使う（デバイス側の型と確実に一致させる）
        if (dctCipTypes.TryGetValue(data.DataTag, out var learned))
        {
            return learned;
        }
        // 同一タグ内でintとfloatは混在しない前提（1タグ = 1CIP型）
        var isFloat = data.values.Exists(d => (d != null) && d.isFloat);
        if (regTypeData64.Contains(data.RegisterType))
        {
            return isFloat ? CIP_TYPE_LREAL : CIP_TYPE_LINT;
        }
        if (regTypeData16.Contains(data.RegisterType))
        {
            return data.IsUnsigned ? CIP_TYPE_UINT : CIP_TYPE_INT;
        }
        if (isFloat)
        {
            return CIP_TYPE_REAL;
        }
        return data.IsUnsigned ? CIP_TYPE_UDINT : CIP_TYPE_DINT;
    }

    /// <summary>
    /// CIPデータ型 → 1要素のバイト数
    /// </summary>
    /// <param name="cipType"></param>
    /// <returns></returns>
    private int GetElementSize(ushort cipType)
    {
        switch (cipType)
        {
            case 0x00C1: // BOOL
            case 0x00C2: // SINT
            case 0x00C6: // USINT
                return 1;
            case 0x00C3: // INT
            case 0x00C7: // UINT
                return 2;
            case 0x00C5: // LINT
            case 0x00C9: // ULINT
            case 0x00CB: // LREAL
                return 8;
            default:     // DINT / UDINT / REAL / DWORD
                return 4;
        }
    }

    /// <summary>
    /// タグのCIP Request Pathを生成する
    /// NodeId（KMXTool出力の標準形式）を優先し、"Tag[5]"形式の添字にも対応する。
    /// NodeIdが空の場合はRegisterTypeを配列タグ名として添字(index)を付与する。
    /// </summary>
    /// <param name="data"></param>
    /// <param name="index"></param>
    /// <returns></returns>
    private List<byte> MakeTagPath(KMXDBSetting data, int index)
    {
        if (!string.IsNullOrEmpty(data.NodeId))
        {
            var tagName = data.NodeId;
            var match = Regex.Match(tagName, @"^(.+)\[(\d+)\]$");
            if (match.Success)
            {
                tagName = match.Groups[1].Value;
                index = int.Parse(match.Groups[2].Value);
            }
            return MakeSymbolicPath(tagName, index);
        }
        return MakeSymbolicPath(data.RegisterType, index);
    }

    /// <summary>
    /// シンボリックタグ名 → CIP Request Path (EPATH)
    /// 例: "MyTag" + 添字5 → 0x91, 0x05, 'M','y','T','a','g', 0x00(パディング), 0x28, 0x05
    /// </summary>
    /// <param name="tagName"></param>
    /// <param name="index"></param>
    /// <returns></returns>
    private List<byte> MakeSymbolicPath(string tagName, int index)
    {
        var path = new List<byte>();
        var nameBytes = Encoding.ASCII.GetBytes(tagName);
        path.Add(0x91);                     // ANSI拡張シンボルセグメント
        path.Add((byte)nameBytes.Length);
        path.AddRange(nameBytes);
        if (nameBytes.Length % 2 != 0)
        {
            path.Add(0x00);                 // 奇数長パディング
        }
        if (index > 0)
        {
            if (index <= 0xFF)
            {
                path.Add(0x28);             // 8bit添字
                path.Add((byte)index);
            }
            else if (index <= 0xFFFF)
            {
                path.Add(0x29);             // 16bit添字
                path.Add(0x00);
                path.AddRange(BitConverter.GetBytes((ushort)index));
            }
            else
            {
                path.Add(0x2A);             // 32bit添字
                path.Add(0x00);
                path.AddRange(BitConverter.GetBytes((uint)index));
            }
        }
        return path;
    }
}
