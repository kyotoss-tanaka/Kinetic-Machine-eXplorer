using Parameters;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using static Parameters.PostgresSetting;
using static OVRPlugin;
using System;
using static KssBaseScript;
using System.Linq;

public class ComProtocolBase : ComBaseScript
{
    #region クラス定義
    protected class ConnectionBase
    {
        /// <summary>
        /// ロック用オブジェクト
        /// </summary>
        public object lockObj = new object();
    }

    protected class TcpConnection : ConnectionBase
    {
        /// <summary>
        /// TCPクライアント
        /// </summary>
        public TcpClient _tcpClient;
        /// <summary>
        /// TCP送受信用ストリーム
        /// </summary>
        public NetworkStream _tcpStream;
    }

    protected class UdpConnection : ConnectionBase
    {
        /// <summary>
        /// UDPソケット
        /// </summary>
        public Socket _udpSocket;
        /// <summary>
        /// 宛先
        /// </summary>
        public IPEndPoint RemoteEndPoint;
    }
    #endregion クラス定義

    /// <summary>
    /// サーバー名
    /// </summary>
    public string Name { get { return dbIpAddress + ":" + dbPort.ToString(); } }

    /// <summary>
    /// IPアドレス
    /// </summary>
    protected string dbIpAddress;

    /// <summary>
    /// ポート番号
    /// </summary>
    protected int dbPort;

    /// <summary>
    /// データ
    /// </summary>
    protected KmxDirectData directData;

    /// <summary>
    /// Ping応答
    /// </summary>
    [SerializeField]
    protected bool IsPing;

    /// <summary>
    /// 接続済み
    /// </summary>
    [SerializeField]
    protected bool IsConnected;

    /// <summary>
    /// 送信カウンタ
    /// </summary>
    [SerializeField]
    protected ProcessStopWatch swRecieve;

    /// <summary>
    /// 送信カウンタ
    /// </summary>
    [SerializeField]
    protected ProcessStopWatch swSend;

    /// <summary>
    /// 交換カウンタ
    /// </summary>
    [SerializeField]
    protected int ExDataNum;

    /// <summary>
    /// 処理中
    /// </summary>
    protected bool IsProcessing;

    /// <summary>
    /// 書き込み処理中
    /// </summary>
    protected bool IsWriteProcessing;

    /// <summary>
    /// TCPコネクション
    /// </summary>
    protected TcpConnection tcp = new();

    /// <summary>
    /// UDPコネクション
    /// </summary>
    protected UdpConnection udp = new();

    /// <summary>
    /// Ping間隔
    /// </summary>
    private float pingInterval = 5f;

    /// <summary>
    /// ロック用オブジェクト
    /// </summary>
    private object m_PingLock = new();

    /// <summary>
    /// ロック用オブジェクト
    /// </summary>
    protected object m_ComLock = new();

    /// <summary>
    /// 受信タイプアウト(msec)
    /// </summary>
    protected const int _RCVTIMEOUT = 3000;

    /// <summary>
    /// 受信データ
    /// </summary>
    protected List<DBSetting> rcvDatas = new();

    /// <summary>
    /// 送信データ
    /// </summary>
    protected List<DBSetting> sndDatas = new();

    /// <summary>
    /// 受信用タグ
    /// </summary>
    Dictionary<string, List<KMXDBSetting>> dctReadSortedTags1 = new();

    /// <summary>
    /// 受信用タグ(送信用)
    /// </summary>
    Dictionary<string, List<KMXDBSetting>> dctReadSortedTags2 = new();

    /// <summary>
    /// 送信用タグ
    /// </summary>
    Dictionary<string, List<KMXDBSetting>> dctWriteSortedTags = new();

    /// <summary>
    /// 登録したタグリスト
    /// </summary>
    private List<string> lstRegTag = new();

    /// <summary>
    /// 受信バッファ
    /// </summary>
    protected byte[] readBuff;

    /// <summary>
    /// ビットレジスタ定義
    /// </summary>
    protected virtual List<string> regTypeBit
    {
        get
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// 16進ビットレジスタ定義
    /// </summary>
    protected virtual List<string> regTypeBit16
    {
        get
        {
            return new List<string>();
        }
    }


    /// <summary>
    /// 16bitレジスタ定義
    /// </summary>
    protected virtual List<string> regTypeData16
    {
        get
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// 32bitレジスタ定義
    /// </summary>
    protected virtual List<string> regTypeData32
    {
        get
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// 64bitレジスタ定義
    /// </summary>
    protected virtual List<string> regTypeData64
    {
        get
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// プログラム番号が存在するレジスタ定義
    /// </summary>
    protected virtual List<string> regTypeExistPrg
    {
        get
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// 一括受信カウント
    /// </summary>
    public virtual int BULK_RCV_COUNT
    {
        get
        {
            return 900;
        }
    }

    /// <summary>
    /// 一括受信カウント
    /// </summary>
    public virtual int BIT_COUNT
    {
        get
        {
            return 16;
        }
    }

    /// <summary>
    /// バッファ最大サイズ
    /// </summary>
    protected virtual int LAN_BUFF_MAX
    {
        get
        {
            return 4096;
        }
    }

    // Start is called before the first frame update
    protected override void Start()
    {
        swRecieve = new();
        swSend = new();
        StartCoroutine(DataUpdate());
        StartCoroutine(CheckPingLoop());
    }

    /// <summary>
    /// 切断
    /// </summary>
    protected override void OnDestroy()
    {
    }

    /// <summary>
    /// データ更新
    /// </summary>
    protected IEnumerator DataUpdate()
    {
        while (true)
        {
            if (this.enabled)
            {
                if (GlobalScript.isLoaded)
                {
                    // データ交換処理
                    DataExchangeProcess();

                    // データ更新処理
                    RenewData();
                }
                var sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                if (Cycle < 30)
                {
                    yield return new WaitForFixedUpdate();
                    //yield return null;
                }
                else
                {
                    yield return new WaitForSecondsRealtime(Cycle / 1000f);
                }
                waitTime = sw.ElapsedMilliseconds;
            }
            else
            {
                yield return new WaitForSecondsRealtime(Cycle / 1000f);
            }
        }
    }

    /// <summary>
    /// データ交換処理
    /// </summary>
    protected override void DataExchangeProcess()
    {
        if ((dataExchange.datas.Count > 0) && dataExchanges.Count == 0)
        {
            // 初回データ作成
            foreach (var data in dataExchange.datas)
            {
                DataExchange d = new DataExchange();
                var find = false;
                if (GlobalScript.tagDatas[Name][dataExchange.mechId].ContainsKey(data.output))
                {
                    d.Output = GlobalScript.tagDatas[Name][dataExchange.mechId][data.output];
                    find = true;
                }
                if (find)
                {
                    if (data.isInit)
                    {
                        // 初期化処理
                        d.InitValue = data.initValue;
                        initDatas.Add(d);
                    }
                    else
                    {
                        // 通常交換
                        if (GlobalScript.tagDatas[Name][dataExchange.mechId].ContainsKey(data.input))
                        {
                            d.Input = GlobalScript.tagDatas[Name][dataExchange.mechId][data.input];
                            dataExchanges.Add(d);
                        }
                    }
                }
            }
        }
        if (isFirst)
        {
            // 初回のみ
            foreach (var data in initDatas)
            {
                data.Output.Value = data.InitValue;
            }
        }
        foreach (var data in dataExchanges)
        {
            data.Output.Value = data.Input.Value;
        }
        ExDataNum = dataExchanges.Count;
        // DBのデータ作成完了していないとスルーされる
        isFirst = !isRcvDb;
    }

    /// <summary>
    /// データ更新処理本体
    /// </summary>
    public override void RenewData()
    {
        base.RenewData();
        if (GlobalScript.isLoaded)
        {
            if (!IsProcessing)
            {
                IsProcessing = true;
                if (IsConnected)
                {
                    // 接続済み
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            // 受信処理
                            swRecieve.cycle = swRecieve.sw.ElapsedMilliseconds;
                            swRecieve.sw.Restart();
                            if (Recieve())
                            {
                                swRecieve.laps = swRecieve.sw.ElapsedMilliseconds;
                                // DB登録済
                                isRcvDb = true;
                            }
                        }
                        catch
                        {
                        }
                        IsProcessing = false;
                    });
                    if (!isFirst)
                    {
                        // 書き込み処理
                        IsWriteProcessing = true;
                        _ = Task.Run(() =>
                        {
                            try
                            {
                                swSend.cycle = swSend.sw.ElapsedMilliseconds;
                                swSend.sw.Restart();
                                if (Send())
                                {
                                    swSend.laps = swSend.sw.ElapsedMilliseconds;
                                }
                            }
                            catch
                            {
                            }
                            IsWriteProcessing = false;
                        });
                    }
                }
                else
                {
                    // 未接続
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            // 接続処理
                            IsConnected = Connect();
                        }
                        catch(Exception ex)
                        {
                        }
                        IsProcessing = false;
                    });
                }
            }
        }
    }

    /// <summary>
    /// 接続
    /// </summary>
    /// <returns></returns>
    protected virtual bool Connect()
    {
        if (IsPing)
        {
            if (directData.isUdp)
            {
                // UDP
                if (udp.RemoteEndPoint == null)
                {
                    udp.RemoteEndPoint = new IPEndPoint(IPAddress.Parse(Server), Port);
                    udp._udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    EndPoint localEP = new IPEndPoint(IPAddress.Any, 5000);
                    udp._udpSocket.Bind(localEP);
                    return true;
                }
                else
                {
                    Disconnect();
                }
            }
            else
            {
                // TCP
                if (tcp._tcpClient == null)
                {
                    tcp._tcpClient = new TcpClient(Server, Port);
                    tcp._tcpClient.ReceiveTimeout = 1000;
                    tcp._tcpClient.NoDelay = false;
                    tcp._tcpStream = tcp._tcpClient.GetStream();
                    return true;
                }
                else
                {
                    if (tcp._tcpClient.Connected)
                    {
                        return true;
                    }
                    Disconnect();
                }
            }
        }
        return false;
    }

    /// <summary>
    /// 切断
    /// </summary>
    protected virtual void Disconnect()
    {
        IsConnected = false;
        if (directData.isUdp)
        {
            if (udp._udpSocket != null)
            {
//                udp._udpSocket.Shutdown(SocketShutdown.Both);
                udp._udpSocket.Close();
                udp._udpSocket = null;
            }
            udp.RemoteEndPoint = null;
        }
        else
        {
            if (tcp._tcpStream != null)
            {
                if (tcp._tcpClient != null)
                {
                    tcp._tcpClient.Close();
                }
            }
            if (tcp._tcpClient != null)
            {
                tcp._tcpClient = null;
            }
        }
    }

    /// <summary>
    /// 受信処理
    /// </summary>
    protected virtual bool Recieve()
    {
        var ret = true;
        foreach (var tags in dctReadSortedTags1)
        {
            foreach (var tag in tags.Value)
            {
                int commandId = 0;
                ret &= Read(tag, ref commandId);
                if (!IsConnected)
                {
                    return false;
                }
            }
        }
        if (isFirst)
        {
            // 初回のみ書き込みデータ受信
            foreach (var tags in dctReadSortedTags2)
            {
                foreach (var tag in tags.Value)
                {
                    int commandId = 0;
                    ret &= Read(tag, ref commandId);
                    if (!IsConnected)
                    {
                        return false;
                    }
                }
            }
        }
        return ret;
    }

    /// <summary>
    /// 送信処理
    /// </summary>
    /// <returns></returns>
    protected virtual bool Send()
    {
        var ret = true;
        foreach (var tags in dctWriteSortedTags)
        {
            foreach (var tag in tags.Value)
            {
                int commandId = 0;
                ret &= Write(tag, ref commandId);
                if (!IsConnected)
                {
                    return false;
                }
            }
        }
        return ret;
    }

    /// <summary>
    /// データ読込み
    /// </summary>
    /// <param name="data"></param>
    protected virtual bool Read(KMXDBSetting data, ref int commandId)
    {
        // コマンド作成処理
        var message = CreateMessage(data, ref commandId);
        if (message.Count > 0)
        {
            // データ送信処理
            var buff = SendCommand(message);
            if (buff.Count > 2)
            {
                // 受信データ分析処理
                return AnalysysMessage(data, buff);
            }
        }
        return false;
    }

    /// <summary>
    /// データ読込み
    /// </summary>
    /// <param name="data"></param>
    protected virtual bool Write(KMXDBSetting data, ref int commandId)
    {
        var values = new List<ulong>();
        foreach (var tag in data.values)
        {
            if (tag == null)
            {
                values.Add(0);
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
    /// 電文作成
    /// </summary>
    /// <param name="data"></param>
    /// <param name="read"></param>
    /// <returns></returns>
    protected virtual List<byte> CreateMessage(KMXDBSetting data, ref int commandId, List<ulong>? values = null)
    {
        var message = new List<byte>();
        return message;
    }

    /// <summary>
    /// 分析処理
    /// </summary>
    /// <param name="datas"></param>
    /// <returns></returns>
    protected virtual bool AnalysysMessage(KMXDBSetting data, List<byte> datas)
    {
        return false;
    }

    /// <summary>
    /// コマンド送信
    /// </summary>
    /// <param name="command">コマンド</param>
    /// <returns>受信コマンド</returns>
    protected List<byte> SendCommand(List<byte> command)
    {
        // 受信バッファ
        var buff = new List<byte> { 0xFF, 0xFF };
        lock (m_ComLock)
        {
            // 送信
            if (StreamWrite(command))
            {
                int size = 0;
                if (StreamRead(readBuff, ref size))
                {
                    List<byte> lstTmp = new List<byte>();
                    // プロトコルチェック
                    if (this is ComMcProtocol)
                    {
                        if (BitConverter.ToUInt16(readBuff, 0) != 0x00D0)
                        {
                            return readBuff.ToList();
                        }
                        // データサイズ取得
                        ushort readSize = BitConverter.ToUInt16(readBuff, 7);
                        // 終了コード以降を取得
                        for (int i = 0; i < readSize; i++)
                        {
                            lstTmp.Add(readBuff[9 + i]);
                        }
                    }
                    else if(this is ComMicks)
                    {
                        for (int i = 0; i < size; i++)
                        {
                            lstTmp.Add(readBuff[i]);
                        }
                    }
                    return lstTmp;
                }
                else
                {
                    Disconnect();
                }
            }
            else
            {
                Disconnect();
            }
        }
        return buff;
    }

    /// <summary>
    /// クライアントデータ受信処理
    /// </summary>
    protected bool StreamRead(byte[] buffer, ref int size)
    {
        try
        {
            if (directData.isUdp)
            {
                var remote = new IPEndPoint(IPAddress.Parse(Server), Port) as EndPoint;
                //var remote = new IPEndPoint(IPAddress.Any, PortNo) as EndPoint;
                udp._udpSocket.ReceiveTimeout = _RCVTIMEOUT;
                size = udp._udpSocket.ReceiveFrom(buffer, ref remote);
            }
            else
            {
                size = tcp._tcpStream.Read(buffer, 0, buffer.Length);
            }
        }
        catch
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// クライアントデータ送信処理
    /// </summary>
    protected bool StreamWrite(List<byte> buffer)
    {
        try
        {
            if (directData.isUdp)
            {
                udp._udpSocket.SendTo(buffer.ToArray(), udp.RemoteEndPoint);
            }
            else
            {
                tcp._tcpStream.Write(buffer.ToArray(), 0, buffer.Count);
            }
        }
        catch (Exception ex)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// PING処理
    /// </summary>
    /// <returns></returns>
    private IEnumerator CheckPingLoop()
    {
        while (true)
        {
            yield return StartCoroutine(SendPing());
            yield return new WaitForSeconds(pingInterval);
        }
    }

    private IEnumerator SendPing()
    {
        Ping ping = new Ping(Server);
        float startTime = Time.time;
        // 完了かタイムアウトまで待機
        while (!ping.isDone && Time.time - startTime < 5f)
        {
            yield return null;
        }
        if (ping.isDone && ping.time >= 0)
        {
            IsPing = true;
            //            Debug.Log($"[{System.DateTime.Now}] Ping OK: {ping.ip} {ping.time} ms");
        }
        else
        {
            IsPing = false;
            //            Debug.LogWarning($"[{System.DateTime.Now}] Ping NG: {Server}");
        }
    }

    /// <summary>
    /// DBに登録
    /// </summary>
    /// <param name="tag"></param>
    private void SetDbData(KMXDBSetting dbSetting)
    {
        if (dbSetting.AllDataCount == 1)
        {
            // データが一つ
            if (dbSetting.unitTag != null)
            {
                // ユニットタグ
                foreach (var unit in dbSetting.unitTag.UnitTags)
                {
                    var j = dbSetting.unitTag.UnitTags.IndexOf(unit);
                    var tag = $"{dbSetting.DataTag}_{unit.DataTag}";
                    SetDbData(dbSetting, tag, j);
                }
            }
            else
            {
                // 通常タグ
                var tag = dbSetting.DataTag;
                SetDbData(dbSetting, tag);
            }
        }
        else
        {
            // 複数データ
            if (dbSetting.unitTag != null)
            {
                // ユニットタグ
                for (var i = 0; i < dbSetting.DataCount; i++)
                {
                    foreach (var unit in dbSetting.unitTag.UnitTags)
                    {
                        var j = dbSetting.unitTag.UnitTags.IndexOf(unit);
                        var tag = $"{dbSetting.DataTag}_{unit.DataTag}[{i}]";
                        SetDbData(dbSetting, tag, i * dbSetting.unitTag.UnitTags.Count + j);
                    }
                }
            }
            else
            {
                // 通常タグ
                for (var i = 0; i < dbSetting.DataCount; i++)
                {
                    var tag = $"{dbSetting.DataTag}[{i}]";
                    SetDbData(dbSetting, tag, i);
                }
            }
        }
    }

    /// <summary>
    /// DBにタグを登録する
    /// </summary>
    /// <param name="tag"></param>
    private void SetDbData(KMXDBSetting dbSetting, string tag, int offset = 0)
    {
        var mech = dataExchange.mechId;
        if (!GlobalScript.tagDatas[Name].ContainsKey(mech))
        {
            // 機番作成
            GlobalScript.tagDatas[Name].Add(mech, new Dictionary<string, TagInfo>());
        }
        if (!GlobalScript.tagDatas[Name][mech].ContainsKey(tag))
        {
            GlobalScript.tagDatas[Name][mech].Add(tag, ScriptableObject.CreateInstance<TagInfo>());
        }
        else if (GlobalScript.tagDatas[Name][mech][tag] == null)
        {
            GlobalScript.tagDatas[Name][mech].Remove(tag);
            GlobalScript.tagDatas[Name][mech].Add(tag, ScriptableObject.CreateInstance<TagInfo>());
        }
        var dct = GlobalScript.tagDatas[Name][mech][tag];
        dct.name = tag;
        dct.Database = Name;
        dct.MechId = mech;
        dct.Tag = tag;
        dct.Device = dbSetting.RegisterType;
        dct.Value = 0;
        if (regTypeBit16.Contains(dbSetting.RegisterType))
        {
            // 16進数
            dct.Device += (dbSetting.RegisterNo + offset).ToString("X");
        }
        else
        {
            dct.Device += (dbSetting.RegisterNo + offset).ToString();
        }
        // タグ名登録
        lstRegTag.Add(tag);
    }

    /// <summary>
    /// DBに登録
    /// </summary>
    /// <param name="tag"></param>
    private void SetDbPointer(KMXDBSetting dbSetting)
    {
        var mech = dataExchange.mechId;
        if (dbSetting.AllDataCount == 1)
        {
            // データが一つ
            if (dbSetting.unitTag != null)
            {
                // ユニットタグ
                foreach (var unit in dbSetting.unitTag.UnitTags)
                {
                    var tag = $"{dbSetting.DataTag}_{unit.DataTag}";
                    dbSetting.values.Add(GlobalScript.tagDatas[Name][mech].ContainsKey(tag) ? GlobalScript.tagDatas[Name][mech][tag] : null);
                }
            }
            else
            {
                // 通常タグ
                var tag = dbSetting.DataTag;
                dbSetting.values.Add(GlobalScript.tagDatas[Name][mech].ContainsKey(tag) ? GlobalScript.tagDatas[Name][mech][tag] : null);
            }
        }
        else
        {
            // 複数データ
            if (dbSetting.unitTag != null)
            {
                // ユニットタグ
                for (var i = 0; i < dbSetting.DataCount; i++)
                {
                    foreach (var unit in dbSetting.unitTag.UnitTags)
                    {
                        var tag = $"{dbSetting.DataTag}_{unit.DataTag}[{i}]";
                        dbSetting.values.Add(GlobalScript.tagDatas[Name][mech].ContainsKey(tag) ? GlobalScript.tagDatas[Name][mech][tag] : null);
                    }
                }
            }
            else
            {
                // 通常タグ
                for (var i = 0; i < dbSetting.DataCount; i++)
                {
                    var tag = $"{dbSetting.DataTag}[{i}]";
                    if (dbSetting.sortedDatas.Count > 0)
                    {
                        var sort = dbSetting.sortedDatas.Find(d => (i >= d.RegisterNo) && (i < d.RegisterNo + d.DataCount));
                        if (sort != null)
                        {
                            tag = $"{sort.DataTag}[{i - sort.RegisterNo}]";
                        }
                        else
                        {
                        }
                    }
                    dbSetting.values.Add(GlobalScript.tagDatas[Name][mech].ContainsKey(tag) ? GlobalScript.tagDatas[Name][mech][tag] : null);
                }
            }
        }
    }

    /// <summary>
    /// ソートデータ作成
    /// </summary>
    private void CreateSortedData()
    {
        if (!GlobalScript.tagDatas.ContainsKey(Name))
        {
            GlobalScript.tagDatas.Add(Name, new Dictionary<string, Dictionary<string, TagInfo>>());
        }
        Dictionary<string, List<KMXDBSetting>> dctReadTags1 = new();
        Dictionary<string, List<KMXDBSetting>> dctReadTags2 = new();
        foreach (var tag in directData.tags)
        {
            if (tag.IsWrite)
            {
                if (!dctReadTags2.ContainsKey(tag.RegisterType))
                {
                    dctReadTags2.Add(tag.RegisterType, new List<KMXDBSetting>());
                }
                dctReadTags2[tag.RegisterType].Add((KMXDBSetting)tag.Clone());
                if (!dctWriteSortedTags.ContainsKey(tag.RegisterType))
                {
                    dctWriteSortedTags.Add(tag.RegisterType, new List<KMXDBSetting>());
                }
                dctWriteSortedTags[tag.RegisterType].Add((KMXDBSetting)tag.Clone());
            }
            else
            {
                if (!dctReadTags1.ContainsKey(tag.RegisterType))
                {
                    dctReadTags1.Add(tag.RegisterType, new List<KMXDBSetting>());
                }
                dctReadTags1[tag.RegisterType].Add((KMXDBSetting)tag.Clone());
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
    /// ソートデータ作成
    /// </summary>
    /// <param name="dctTags"></param>
    /// <param name="dctSortedTags"></param>
    private void CreateSorted(Dictionary<string, List<KMXDBSetting>> dctTags, ref Dictionary<string, List<KMXDBSetting>> dctSortedTags)
    {
        dctSortedTags = new();
        foreach (var tags in dctTags)
        {
            dctSortedTags.Add(tags.Key, new());
            tags.Value.Sort((a, b) => a.RegisterNo - b.RegisterNo);
            foreach (var tag in tags.Value)
            {
                if (dctSortedTags[tags.Key].Count == 0)
                {
                    /*
                     * 大きいデータを想定してないので分割は未対応
                    if (regTypeBit.Contains(tags.Key))
                    {
                        // ビットデータ
                        if (tag.AllDataCount > BULK_RCV_COUNT * BIT_COUNT)
                        {
                            // 分割
                        }
                    }
                    else
                    {
                        // ワードデータ
                        if (tag.AllDataCount > BULK_RCV_COUNT)
                        {
                            // 分割
                        }
                    }
                    */
                    var clone = (KMXDBSetting)tag.Clone();
                    dctSortedTags[tags.Key].Add(clone);
                    var sort = (KMXDBSetting)tag.Clone();
                    sort.RegisterNo = 0;
                    clone.sortedDatas.Add(sort);
                }
                else
                {
                    var prv = dctSortedTags[tags.Key][dctSortedTags[tags.Key].Count - 1];
                    var rcvMax = BULK_RCV_COUNT * (regTypeBit.Contains(tags.Key) ? BIT_COUNT : 1);
                    var start = prv.RegisterNo;
                    var end = tag.RegisterNo + tag.AllDataCount;
                    var count = end - start;
                    if (count > rcvMax)
                    {
                        // 一括受信できない
                        var clone = (KMXDBSetting)tag.Clone();
                        dctSortedTags[tags.Key].Add(clone);
                        var sort = (KMXDBSetting)tag.Clone();
                        sort.RegisterNo = 0;
                        clone.sortedDatas.Add(sort);
                    }
                    else
                    {
                        // 一括受信可能
                        prv.DataCount = count;
                        var clone = (KMXDBSetting)tag.Clone();
                        clone.RegisterNo -= prv.RegisterNo;
                        prv.sortedDatas.Add(clone);
                    }
                }
            }
        }
        // ソートされたタグにDBデータをセット
        foreach (var tags in dctSortedTags)
        {
            foreach (var tag in tags.Value)
            {
                SetDbPointer(tag);
            }
        }
    }

    /// <summary>
    /// タグに値をセットする
    /// </summary>
    /// <param name="tag"></param>
    /// <param name=""></param>
    public void SetDatas(List<TagInfo> tags)
    {
        if ((Application.platform != RuntimePlatform.Android) && (Application.platform != RuntimePlatform.IPhonePlayer))
        {
            // 送信データ作成
            lock (objLock)
            {
                foreach (var tag in tags)
                {
                    if (GlobalScript.tagDatas[Name].ContainsKey(tag.MechId) && GlobalScript.tagDatas[Name][tag.MechId].ContainsKey(tag.Tag))
                    {
                        if (isFirst || (GlobalScript.tagDatas[Name][tag.MechId][tag.Tag].Value != tag.Value))
                        {
                            GlobalScript.tagDatas[Name][tag.MechId][tag.Tag].Value = tag.Value;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// パラメータセット
    /// </summary>
    /// <param name="No"></param>
    /// <param name="Cycle"></param>
    /// <param name="Server"></param>
    /// <param name="Port"></param>
    /// <param name="Database"></param>
    /// <param name="User"></param>
    /// <param name="Password"></param>
    /// <param name="isClientMode"></param>
    public void SetParameter(int No, int Cycle, string Server, int Port, string Database, string User, string Password, bool isClientMode, DataExchangeSetting dataExchange, KmxDirectData directData)
    {
        dbIpAddress = Server;
        dbPort = Port;
        SetParameter(No, Cycle, directData.IpAddress, directData.PortNo, Database, User, Password, isClientMode, new DataExchangeSetting { dbNo = dataExchange.dbNo, mechId = dataExchange.mechId, datas = new()});
        this.directData = directData;
        dataExchanges.Clear();
        lstRegTag.Clear();

        // 受信タグソート処理
        CreateSortedData();

        // 交換タグ
        foreach (var data in dataExchange.datas)
        {
            if (lstRegTag.Contains(data.output))
            {
                this.dataExchange.datas.Add(data);
            }
        }

        // LANの受信バッファ作成
        if (readBuff == null)
        {
            readBuff = new byte[LAN_BUFF_MAX];
        }
        IsProcessing = false;
        isFirst = true;
        isRcvDb = false;
    }
}