using Npgsql;
using Parameters;
using StackExchange.Redis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Xml.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using MongoDB.Driver;
using System.Text;
using static OVRPlugin;
using System.Collections.Concurrent;
using System.Diagnostics;

public class ComRedis : ComBaseScript
{
    /// <summary>
    /// MQTT��M�f�[�^
    /// </summary>
    public class RedisItem
    {
        public string Tag { get; set; }
        public string Device { get; set; }
        public long Value { get; set; }
    }

    /// <summary>
    /// �T�[�o�[��
    /// </summary>
    public string Name { get { return Server + ":" + Port.ToString(); } }

    /// <summary>
    /// �g�s�b�N
    /// </summary>
    [SerializeField]
    public string Topic = "UnityData";
    [SerializeField]
    public long nowRcvCycle = 0;
    [SerializeField]
    public long maxRcvCycle = 0;
    [SerializeField]
    public long minRcvCycle = 0;
    [SerializeField]
    public double avgRcvCycle = 0;

    /// <summary>
    /// ���Ԍv���p
    /// </summary>
    private Stopwatch swRcv = new Stopwatch();
    /// <summary>
    /// �T�C�N������
    /// </summary>
    private List<long> cycleRcvLaps = new List<long>();

    /// <summary>
    /// WebAPI�A�N�Z�X
    /// </summary>
    private bool IsWebApi = false;

    /// <summary>
    /// ���M�o���Ȃ������^�O�o�b�t�@(MQTT�Ńf�[�^�\�����m�肷��O�ɑ��M���Ă����f�[�^)
    /// </summary>
    List<TagInfo> tagBuffer = new List<TagInfo>();

    // ��M�f�[�^
    private volatile Dictionary<string, List<HashEntry>> latestRcvDatas = new();

    // ���M�f�[�^
    private volatile Dictionary<string, string> latestSndDatas = new();

    /// <summary>
    /// Redis
    /// </summary>
    private ConnectionMultiplexer redis;

    /// <summary>
    /// �f�[�^�x�[�X
    /// </summary>
    private IDatabase db;

    /// <summary>
    /// �T�u�X�N���C�o�[
    /// </summary>
    private ISubscriber sub;

    /// <summary>
    /// WebAPI�A�N�Z�X�pURL
    /// </summary>
    private string url { get { return "http://" + Server + ":1880/api/redis/"; } }

    /// <summary>
    /// �ڑ���
    /// </summary>
    private bool IsConnecting;

    // Start is called before the first frame update
    protected override void Start()
    {
        if ((Application.platform == RuntimePlatform.Android) || (Application.platform == RuntimePlatform.IPhonePlayer))
        {
            //�[����Android��iOS�������ꍇ�̏���
            IsWebApi = true;
        }
        if (!GlobalScript.tagDatas.ContainsKey(Name))
        {
            GlobalScript.tagDatas.Add(Name, new Dictionary<string, Dictionary<string, TagInfo>>());
        }
        if (!GlobalScript.redises.ContainsKey(Name))
        {
            GlobalScript.redises.Add(Name, this);
        }
        if (IsWebApi)
        {
            StartCoroutine(RenewDataApi());
        }
        else
        {
            StartCoroutine(DataUpdate());
        }
        Connect();
    }
    /// <summary>
    /// �ڑ�
    /// </summary>
    private async void Connect()
    {
        try
        {
            // �N���C�A���g�쐬
            IsConnecting = true;
            redis = await ConnectionMultiplexer.ConnectAsync((Server == "localhost" ? "127.0.0.1" : Server) + ":" + Port);
            db = redis.GetDatabase();
            sub = redis.GetSubscriber();
            sub.Subscribe(RedisChannel.Literal("latestdata"), OnMessageReceived);
        }
        catch(Exception ex)
        {
        }
        IsConnecting = false;
    }

    /// <summary>
    /// �Đڑ�
    /// </summary>
    private void Reconnect()
    {
        try
        {
            if (!IsConnecting)
            {
                Connect();
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// �ؒf
    /// </summary>
    private void Disconnect()
    {
        redis.Close();
        redis.Dispose();
        db = null;
    }

    /// <summary>
    /// ��M���荞��
    /// </summary>
    /// <param name="e"></param>
    /// <returns></returns>
    private void OnMessageReceived(RedisChannel channel, RedisValue value)
    {
        if (isCylceClear)
        {
            cycleRcvLaps.Clear();
        }
        nowRcvCycle = swRcv.ElapsedMilliseconds;
        cycleRcvLaps.Add(nowRcvCycle);
        maxRcvCycle = cycleRcvLaps.Max();
        minRcvCycle = cycleRcvLaps.Min();
        avgRcvCycle = cycleRcvLaps.Average();
        swRcv.Restart();

        // �f�[�^��M
        var topic = value.ToString().Split('.');
        latestRcvDatas[topic[1]] = db.HashGetAll(value.ToString()).ToList();
    }

    /// <summary>
    /// API�ʐM
    /// </summary>
    /// <returns></returns>
    private IEnumerator DataUpdate()
    {
        while (this.enabled)
        {
            // �f�[�^��������
            DataExchangeProcess();

            // �f�[�^�X�V����
            lock (objLock)
            {
                RenewData();
            }
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            if (Cycle < 30)
            {
                yield return null;
            }
            else
            {
                yield return new WaitForSecondsRealtime(Cycle / 1000f);
            }
            waitTime = sw.ElapsedMilliseconds;
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
                var datas = new List<TagInfoCom>();
                var send = new List<TagInfo>();
                // �����M�f�[�^�X�V
                foreach (var tag in tags)
                {
                    var buff = tagBuffer.Find(d => d.Tag == tag.Tag);
                    if (buff != null)
                    {
                        tagBuffer.Remove(buff);
                    }
                    tagBuffer.Add(tag);
                }
                foreach (var tag in tagBuffer)
                {
                    if (GlobalScript.tagDatas[Name].ContainsKey(tag.MechId))
                    {
                        if (!GlobalScript.tagDatas[Name][tag.MechId].ContainsKey(tag.Tag))
                        {
                            GlobalScript.tagDatas[Name][tag.MechId].Add(tag.Tag, ScriptableObject.CreateInstance<TagInfo>());
                        }
                        if (GlobalScript.tagDatas[Name][tag.MechId][tag.Tag].Value != tag.Value)
                        {
                            GlobalScript.tagDatas[Name][tag.MechId][tag.Tag].Value = tag.Value;
                            datas.Add(new TagInfoCom { MechId = tag.MechId, Tag = tag.Tag, Value = tag.Value, fValue = tag.fValue, isFloat = tag.isFloat });
                            send.Add(tag);
                        }
                        else
                        {
                            send.Add(tag);
                        }
                    }
                }
                // ���M�σf�[�^�폜
                tagBuffer.RemoveAll(d => send.Contains(d));
                if (datas.Count == 0)
                {
                    return;
                }
                foreach (var data in datas)
                {
                    var tag = writeDatas.Find(d => (d.MechId == data.MechId) && (d.Tag == data.Tag));
                    if (tag == null)
                    {
                        writeDatas.Add(data);
                    }
                    else
                    {
                        tag.Value = data.Value;
                    }
                }
            }
        }
    }

    /// <summary>
    /// �f�[�^�X�V
    /// </summary>
    public override void RenewData()
    {
        base.RenewData();
        if (!GlobalScript.tagDatas.ContainsKey(Name))
        {
            GlobalScript.tagDatas.Add(Name, new Dictionary<string, Dictionary<string, TagInfo>>());
        }
        if (IsWebApi)
        {
#if UNITY_EDITOR
            if (!EditorApplication.isPlaying)
            {
                // WebAPI�A�N�Z�X
                StartCoroutine(RenewDataApi());
            }
#endif
        }
        else
        {
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            try
            {
                // �f�[�^���M
                if (!isClientMode && writeDatas.Count > 0)
                {
                    var sendDatas = new Dictionary<string, List<RedisItem>>();
                    foreach (var data in writeDatas)
                    {
                        if (!sendDatas.ContainsKey(data.MechId))
                        {
                            sendDatas.Add(data.MechId, new List<RedisItem>());
                        }
                        sendDatas[data.MechId].Add(new RedisItem
                        {
                            Tag = data.Tag,
                            Value = data.Value
                        });
                    }
                    writeDatas.Clear();
                    foreach (var data in sendDatas)
                    {
                        SendMessage(data.Key, data.Value);
                    }
                }
                // �f�[�^�Ǎ�
                foreach (var payload in latestRcvDatas)
                {
                    if (latestRcvDatas[payload.Key] != null)
                    {
                        var datas = payload.Value;
                        latestRcvDatas[payload.Key] = null;
                        var mech = payload.Key;
                        if (!GlobalScript.tagDatas[Name].ContainsKey(mech))
                        {
                            GlobalScript.tagDatas[Name].Add(mech, new Dictionary<string, TagInfo>());
                        }
                        foreach (var data in datas)
                        {
                            if (!GlobalScript.tagDatas[Name][mech].ContainsKey(data.Name))
                            {
                                GlobalScript.tagDatas[Name][mech].Add(data.Name, ScriptableObject.CreateInstance<TagInfo>());
                            }
                            else if (GlobalScript.tagDatas[Name][mech][data.Name] == null)
                            {
                                GlobalScript.tagDatas[Name][mech].Remove(data.Name);
                                GlobalScript.tagDatas[Name][mech].Add(data.Name, ScriptableObject.CreateInstance<TagInfo>());
                            }
                            GlobalScript.tagDatas[Name][mech][data.Name].name = data.Name;
                            GlobalScript.tagDatas[Name][mech][data.Name].Database = Name;
                            GlobalScript.tagDatas[Name][mech][data.Name].MechId = mech;
                            GlobalScript.tagDatas[Name][mech][data.Name].Tag = data.Name;
//                            GlobalScript.tagDatas[Name][mech][data.Name].Device = data.Device;
                            GlobalScript.tagDatas[Name][mech][data.Name].Value = (int)data.Value;

                            GlobalScript.tagDatas[Name][mech][data.Name].Value = (int)data.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("ComRedis : " + ex.Message);
            }
            processTime = sw.ElapsedMilliseconds;
        }
    }

    /// <summary>
    /// API�ł̃f�[�^�X�V
    /// </summary>
    /// <returns></returns>
    public IEnumerator RenewDataApi()
    {
        while (true)
        {
            /*
            UnityWebRequest req = UnityWebRequest.Get(url + $"latestdata/read/all");
            yield return req.SendWebRequest();
            try
            {
                if (req.isNetworkError || req.isHttpError)
                {
                    Debug.Log(req.error);
                }
                else if (req.responseCode == 200)
                {
                    // ��M����
                    var rcvDatas = JsonSerializer.Deserialize<List<LatestData>>(req.downloadHandler.text);
                    foreach (var data in rcvDatas)
                    {
                        var mech = data.mech_id;
                        if (!GlobalScript.tagDatas[Name].ContainsKey(mech))
                        {
                            // �@�ԍ쐬
                            GlobalScript.tagDatas[Name].Add(mech, new Dictionary<string, TagInfo>());
                        }
                        var tag = data.event_id;
                        var dev = data.device_name;
                        var val = int.Parse(data.data_value.ToString());
                        if (!GlobalScript.tagDatas[Name][mech].ContainsKey(tag))
                        {
                            GlobalScript.tagDatas[Name][mech].Add(tag, ScriptableObject.CreateInstance<TagInfo>());
                        }
                        else if (GlobalScript.tagDatas[Name][mech][tag] == null)
                        {
                            GlobalScript.tagDatas[Name][mech].Remove(tag);
                            GlobalScript.tagDatas[Name][mech].Add(tag, ScriptableObject.CreateInstance<TagInfo>());
                        }
                        GlobalScript.tagDatas[Name][mech][tag].name = tag;
                        GlobalScript.tagDatas[Name][mech][tag].Database = Name;
                        GlobalScript.tagDatas[Name][mech][tag].MechId = mech;
                        GlobalScript.tagDatas[Name][mech][tag].Tag = tag;
                        GlobalScript.tagDatas[Name][mech][tag].Device = dev;
                        GlobalScript.tagDatas[Name][mech][tag].Value = val;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
#if UNITY_EDITOR
            if (!EditorApplication.isPlaying)
            {
                break;
            }
#endif
            */
            yield return new WaitForSeconds(Cycle / 1000f);
        }
    }

    /// <summary>
    /// �f�[�^���M
    /// </summary>
    /// <param name="mechId"></param>
    /// <param name="topic"></param>
    /// <param name="data"></param>
    public void SendMessage(string mechId, List<RedisItem> datas)
    {
        try
        {
            // �Đڑ�����
            if (!redis.IsConnected)
            {
                Reconnect();
            }
            // �f�[�^�Z�b�g
            var hash = new List<HashEntry>();
            foreach (var data in datas)
            {
                hash.Add(new HashEntry(data.Tag, data.Value));
            }
            db.HashSet("latest_data." + mechId + "." + Topic, hash.ToArray());
            sub.Publish(RedisChannel.Literal(mechId), Topic);
        }
        catch (Exception ex)
        {
           UnityEngine.Debug.Log("ComRedis : " + ex.Message);
        }
    }

    /// <summary>
    /// �p�����[�^���Z�b�g����
    /// </summary>
    /// <param name="components"></param>
    /// <param name="scriptables"></param>
    /// <param name="kssInstanceIds"></param>
    /// <param name="root"></param>
    public override void SetParameter(List<Component> components, List<KssPartsBase> scriptables, List<KssInstanceIds> kssInstanceIds, JsonElement root)
    {
        base.SetParameter(components, scriptables, kssInstanceIds, root);
        Server = GetStringFromPrm(root, "Server");
        Port = GetInt32FromPrm(root, "Port");
        Database = GetStringFromPrm(root, "Database");
        User = GetStringFromPrm(root, "User");
        Password = GetStringFromPrm(root, "Password");
    }
}
