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
    #region �N���X��`
    protected class ConnectionBase
    {
        /// <summary>
        /// ���b�N�p�I�u�W�F�N�g
        /// </summary>
        public object lockObj = new object();
    }

    protected class TcpConnection : ConnectionBase
    {
        /// <summary>
        /// TCP�N���C�A���g
        /// </summary>
        public TcpClient _tcpClient;
        /// <summary>
        /// TCP����M�p�X�g���[��
        /// </summary>
        public NetworkStream _tcpStream;
    }

    protected class UdpConnection : ConnectionBase
    {
        /// <summary>
        /// UDP�\�P�b�g
        /// </summary>
        public Socket _udpSocket;
        /// <summary>
        /// ����
        /// </summary>
        public IPEndPoint RemoteEndPoint;
    }
    #endregion �N���X��`

    /// <summary>
    /// �T�[�o�[��
    /// </summary>
    public string Name { get { return dbIpAddress + ":" + dbPort.ToString(); } }

    /// <summary>
    /// IP�A�h���X
    /// </summary>
    protected string dbIpAddress;

    /// <summary>
    /// �|�[�g�ԍ�
    /// </summary>
    protected int dbPort;

    /// <summary>
    /// �f�[�^
    /// </summary>
    protected KmxDirectData directData;

    /// <summary>
    /// Ping����
    /// </summary>
    [SerializeField]
    protected bool IsPing;

    /// <summary>
    /// �ڑ��ς�
    /// </summary>
    [SerializeField]
    protected bool IsConnected;

    /// <summary>
    /// ���M�J�E���^
    /// </summary>
    [SerializeField]
    protected ProcessStopWatch swRecieve;

    /// <summary>
    /// ���M�J�E���^
    /// </summary>
    [SerializeField]
    protected ProcessStopWatch swSend;

    /// <summary>
    /// �����J�E���^
    /// </summary>
    [SerializeField]
    protected int ExDataNum;

    /// <summary>
    /// ������
    /// </summary>
    protected bool IsProcessing;

    /// <summary>
    /// �������ݏ�����
    /// </summary>
    protected bool IsWriteProcessing;

    /// <summary>
    /// TCP�R�l�N�V����
    /// </summary>
    protected TcpConnection tcp = new();

    /// <summary>
    /// UDP�R�l�N�V����
    /// </summary>
    protected UdpConnection udp = new();

    /// <summary>
    /// Ping�Ԋu
    /// </summary>
    private float pingInterval = 5f;

    /// <summary>
    /// ���b�N�p�I�u�W�F�N�g
    /// </summary>
    private object m_PingLock = new();

    /// <summary>
    /// ���b�N�p�I�u�W�F�N�g
    /// </summary>
    protected object m_ComLock = new();

    /// <summary>
    /// ��M�^�C�v�A�E�g(msec)
    /// </summary>
    protected const int _RCVTIMEOUT = 3000;

    /// <summary>
    /// ��M�f�[�^
    /// </summary>
    protected List<DBSetting> rcvDatas = new();

    /// <summary>
    /// ���M�f�[�^
    /// </summary>
    protected List<DBSetting> sndDatas = new();

    /// <summary>
    /// ��M�p�^�O
    /// </summary>
    Dictionary<string, List<KMXDBSetting>> dctReadSortedTags1 = new();

    /// <summary>
    /// ��M�p�^�O(���M�p)
    /// </summary>
    Dictionary<string, List<KMXDBSetting>> dctReadSortedTags2 = new();

    /// <summary>
    /// ���M�p�^�O
    /// </summary>
    Dictionary<string, List<KMXDBSetting>> dctWriteSortedTags = new();

    /// <summary>
    /// �o�^�����^�O���X�g
    /// </summary>
    private List<string> lstRegTag = new();

    /// <summary>
    /// ��M�o�b�t�@
    /// </summary>
    protected byte[] readBuff;

    /// <summary>
    /// �r�b�g���W�X�^��`
    /// </summary>
    protected virtual List<string> regTypeBit
    {
        get
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// 16�i�r�b�g���W�X�^��`
    /// </summary>
    protected virtual List<string> regTypeBit16
    {
        get
        {
            return new List<string>();
        }
    }


    /// <summary>
    /// 16bit���W�X�^��`
    /// </summary>
    protected virtual List<string> regTypeData16
    {
        get
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// 32bit���W�X�^��`
    /// </summary>
    protected virtual List<string> regTypeData32
    {
        get
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// 64bit���W�X�^��`
    /// </summary>
    protected virtual List<string> regTypeData64
    {
        get
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// �v���O�����ԍ������݂��郌�W�X�^��`
    /// </summary>
    protected virtual List<string> regTypeExistPrg
    {
        get
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// �ꊇ��M�J�E���g
    /// </summary>
    public virtual int BULK_RCV_COUNT
    {
        get
        {
            return 900;
        }
    }

    /// <summary>
    /// �ꊇ��M�J�E���g
    /// </summary>
    public virtual int BIT_COUNT
    {
        get
        {
            return 16;
        }
    }

    /// <summary>
    /// �o�b�t�@�ő�T�C�Y
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
    /// �ؒf
    /// </summary>
    protected override void OnDestroy()
    {
    }

    /// <summary>
    /// �f�[�^�X�V
    /// </summary>
    protected IEnumerator DataUpdate()
    {
        while (true)
        {
            if (this.enabled)
            {
                if (GlobalScript.isLoaded)
                {
                    // �f�[�^��������
                    DataExchangeProcess();

                    // �f�[�^�X�V����
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
    /// �f�[�^��������
    /// </summary>
    protected override void DataExchangeProcess()
    {
        if ((dataExchange.datas.Count > 0) && dataExchanges.Count == 0)
        {
            // ����f�[�^�쐬
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
                        // ����������
                        d.InitValue = data.initValue;
                        initDatas.Add(d);
                    }
                    else
                    {
                        // �ʏ����
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
            // ����̂�
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
        // DB�̃f�[�^�쐬�������Ă��Ȃ��ƃX���[�����
        isFirst = !isRcvDb;
    }

    /// <summary>
    /// �f�[�^�X�V�����{��
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
                    // �ڑ��ς�
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            // ��M����
                            swRecieve.cycle = swRecieve.sw.ElapsedMilliseconds;
                            swRecieve.sw.Restart();
                            if (Recieve())
                            {
                                swRecieve.laps = swRecieve.sw.ElapsedMilliseconds;
                                // DB�o�^��
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
                        // �������ݏ���
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
                    // ���ڑ�
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            // �ڑ�����
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
    /// �ڑ�
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
    /// �ؒf
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
    /// ��M����
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
            // ����̂ݏ������݃f�[�^��M
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
    /// ���M����
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
    /// �f�[�^�Ǎ���
    /// </summary>
    /// <param name="data"></param>
    protected virtual bool Read(KMXDBSetting data, ref int commandId)
    {
        // �R�}���h�쐬����
        var message = CreateMessage(data, ref commandId);
        if (message.Count > 0)
        {
            // �f�[�^���M����
            var buff = SendCommand(message);
            if (buff.Count > 2)
            {
                // ��M�f�[�^���͏���
                return AnalysysMessage(data, buff);
            }
        }
        return false;
    }

    /// <summary>
    /// �f�[�^�Ǎ���
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
            // �f�[�^���M����
            var buff = SendCommand(message);
            if (buff.Count >= 2)
            {
                // ��M�f�[�^���͏���
                return AnalysysMessage(data, buff);
            }
        }
        return false;
    }

    /// <summary>
    /// �d���쐬
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
    /// ���͏���
    /// </summary>
    /// <param name="datas"></param>
    /// <returns></returns>
    protected virtual bool AnalysysMessage(KMXDBSetting data, List<byte> datas)
    {
        return false;
    }

    /// <summary>
    /// �R�}���h���M
    /// </summary>
    /// <param name="command">�R�}���h</param>
    /// <returns>��M�R�}���h</returns>
    protected List<byte> SendCommand(List<byte> command)
    {
        // ��M�o�b�t�@
        var buff = new List<byte> { 0xFF, 0xFF };
        lock (m_ComLock)
        {
            // ���M
            if (StreamWrite(command))
            {
                int size = 0;
                if (StreamRead(readBuff, ref size))
                {
                    List<byte> lstTmp = new List<byte>();
                    // �v���g�R���`�F�b�N
                    if (this is ComMcProtocol)
                    {
                        if (BitConverter.ToUInt16(readBuff, 0) != 0x00D0)
                        {
                            return readBuff.ToList();
                        }
                        // �f�[�^�T�C�Y�擾
                        ushort readSize = BitConverter.ToUInt16(readBuff, 7);
                        // �I���R�[�h�ȍ~���擾
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
    /// �N���C�A���g�f�[�^��M����
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
    /// �N���C�A���g�f�[�^���M����
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
    /// PING����
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
        // �������^�C���A�E�g�܂őҋ@
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
    /// DB�ɓo�^
    /// </summary>
    /// <param name="tag"></param>
    private void SetDbData(KMXDBSetting dbSetting)
    {
        if (dbSetting.AllDataCount == 1)
        {
            // �f�[�^�����
            if (dbSetting.unitTag != null)
            {
                // ���j�b�g�^�O
                foreach (var unit in dbSetting.unitTag.UnitTags)
                {
                    var j = dbSetting.unitTag.UnitTags.IndexOf(unit);
                    var tag = $"{dbSetting.DataTag}_{unit.DataTag}";
                    SetDbData(dbSetting, tag, j);
                }
            }
            else
            {
                // �ʏ�^�O
                var tag = dbSetting.DataTag;
                SetDbData(dbSetting, tag);
            }
        }
        else
        {
            // �����f�[�^
            if (dbSetting.unitTag != null)
            {
                // ���j�b�g�^�O
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
                // �ʏ�^�O
                for (var i = 0; i < dbSetting.DataCount; i++)
                {
                    var tag = $"{dbSetting.DataTag}[{i}]";
                    SetDbData(dbSetting, tag, i);
                }
            }
        }
    }

    /// <summary>
    /// DB�Ƀ^�O��o�^����
    /// </summary>
    /// <param name="tag"></param>
    private void SetDbData(KMXDBSetting dbSetting, string tag, int offset = 0)
    {
        var mech = dataExchange.mechId;
        if (!GlobalScript.tagDatas[Name].ContainsKey(mech))
        {
            // �@�ԍ쐬
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
            // 16�i��
            dct.Device += (dbSetting.RegisterNo + offset).ToString("X");
        }
        else
        {
            dct.Device += (dbSetting.RegisterNo + offset).ToString();
        }
        // �^�O���o�^
        lstRegTag.Add(tag);
    }

    /// <summary>
    /// DB�ɓo�^
    /// </summary>
    /// <param name="tag"></param>
    private void SetDbPointer(KMXDBSetting dbSetting)
    {
        var mech = dataExchange.mechId;
        if (dbSetting.AllDataCount == 1)
        {
            // �f�[�^�����
            if (dbSetting.unitTag != null)
            {
                // ���j�b�g�^�O
                foreach (var unit in dbSetting.unitTag.UnitTags)
                {
                    var tag = $"{dbSetting.DataTag}_{unit.DataTag}";
                    dbSetting.values.Add(GlobalScript.tagDatas[Name][mech].ContainsKey(tag) ? GlobalScript.tagDatas[Name][mech][tag] : null);
                }
            }
            else
            {
                // �ʏ�^�O
                var tag = dbSetting.DataTag;
                dbSetting.values.Add(GlobalScript.tagDatas[Name][mech].ContainsKey(tag) ? GlobalScript.tagDatas[Name][mech][tag] : null);
            }
        }
        else
        {
            // �����f�[�^
            if (dbSetting.unitTag != null)
            {
                // ���j�b�g�^�O
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
                // �ʏ�^�O
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
    /// �\�[�g�f�[�^�쐬
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
            // DB�o�^
            SetDbData(tag);
        }
        CreateSorted(dctReadTags1, ref dctReadSortedTags1);
        CreateSorted(dctReadTags2, ref dctReadSortedTags2);
        // �\�[�g���ꂽ�^�O��DB�f�[�^���Z�b�g
        foreach (var tags in dctWriteSortedTags)
        {
            foreach (var tag in tags.Value)
            {
                SetDbPointer(tag);
            }
        }
    }

    /// <summary>
    /// �\�[�g�f�[�^�쐬
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
                     * �傫���f�[�^��z�肵�ĂȂ��̂ŕ����͖��Ή�
                    if (regTypeBit.Contains(tags.Key))
                    {
                        // �r�b�g�f�[�^
                        if (tag.AllDataCount > BULK_RCV_COUNT * BIT_COUNT)
                        {
                            // ����
                        }
                    }
                    else
                    {
                        // ���[�h�f�[�^
                        if (tag.AllDataCount > BULK_RCV_COUNT)
                        {
                            // ����
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
                        // �ꊇ��M�ł��Ȃ�
                        var clone = (KMXDBSetting)tag.Clone();
                        dctSortedTags[tags.Key].Add(clone);
                        var sort = (KMXDBSetting)tag.Clone();
                        sort.RegisterNo = 0;
                        clone.sortedDatas.Add(sort);
                    }
                    else
                    {
                        // �ꊇ��M�\
                        prv.DataCount = count;
                        var clone = (KMXDBSetting)tag.Clone();
                        clone.RegisterNo -= prv.RegisterNo;
                        prv.sortedDatas.Add(clone);
                    }
                }
            }
        }
        // �\�[�g���ꂽ�^�O��DB�f�[�^���Z�b�g
        foreach (var tags in dctSortedTags)
        {
            foreach (var tag in tags.Value)
            {
                SetDbPointer(tag);
            }
        }
    }

    /// <summary>
    /// �^�O�ɒl���Z�b�g����
    /// </summary>
    /// <param name="tag"></param>
    /// <param name=""></param>
    public void SetDatas(List<TagInfo> tags)
    {
        if ((Application.platform != RuntimePlatform.Android) && (Application.platform != RuntimePlatform.IPhonePlayer))
        {
            // ���M�f�[�^�쐬
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
    /// �p�����[�^�Z�b�g
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

        // ��M�^�O�\�[�g����
        CreateSortedData();

        // �����^�O
        foreach (var data in dataExchange.datas)
        {
            if (lstRegTag.Contains(data.output))
            {
                this.dataExchange.datas.Add(data);
            }
        }

        // LAN�̎�M�o�b�t�@�쐬
        if (readBuff == null)
        {
            readBuff = new byte[LAN_BUFF_MAX];
        }
        IsProcessing = false;
        isFirst = true;
        isRcvDb = false;
    }
}