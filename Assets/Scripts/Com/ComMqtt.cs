using Npgsql;
using Parameters;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Server;
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

public class ComMqtt : ComBaseScript
{
    /// <summary>
    /// MQTT��M�f�[�^
    /// </summary>
    public class MqttItem
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
    private volatile Dictionary<string, string> latestRcvDatas = new();

    // ���M�f�[�^
    private volatile Dictionary<string, string> latestSndDatas = new();

    /// <summary>
    /// MQTT�N���C�A���g
    /// </summary>
    private IMqttClient mqttClient;

    /// <summary>
    /// WebAPI�A�N�Z�X�pURL
    /// </summary>
    private string url { get { return "http://" + Server + ":1880/api/mqtt/"; } }

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
        if (!GlobalScript.mqtts.ContainsKey(Name))
        {
            GlobalScript.mqtts.Add(Name, this);
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
        // MQTT�N���C�A���g�쐬
        mqttClient = new MqttFactory().CreateMqttClient();
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(Server, Port)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(90))
            .Build();

        // �ڑ�
        await mqttClient.ConnectAsync(options);

        // �T�u�X�N���C�u�o�^
        _ = Task.Run(async () =>
        {
            await mqttClient.SubscribeAsync(new MqttTopicFilterBuilder()
           .WithTopic("+/" + Topic + "/read/#")
           .WithAtMostOnceQoS()
           .Build());
        });

        // �f�[�^��M
        mqttClient.ApplicationMessageReceivedAsync += OnMessageReceived;
    }

    /// <summary>
    /// �Đڑ�
    /// </summary>
    private async void Reconnect()
    {
        try
        {
            await mqttClient.ReconnectAsync();
        }
        catch
        {
        }
    }

    /// <summary>
    /// �ؒf
    /// </summary>
    private async void Disconnect()
    {
        await mqttClient.DisconnectAsync();
        mqttClient.Dispose();
        mqttClient = null;
    }

    /// <summary>
    /// ��M���荞��
    /// </summary>
    /// <param name="e"></param>
    /// <returns></returns>
    public Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs e)
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
        var topicLevels = e.ApplicationMessage.Topic.Split('/');
        var isInit = topicLevels.Length == 4;
        if (isInit || (latestRcvDatas[topicLevels[0]] == null))
        {
            latestRcvDatas[topicLevels[0]] = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment.ToArray());
        }
        return Task.CompletedTask;
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
                    var sendDatas = new Dictionary<string, List<MqttItem>>();
                    foreach (var data in writeDatas)
                    {
                        if (!sendDatas.ContainsKey(data.MechId))
                        {
                            sendDatas.Add(data.MechId, new List<MqttItem>());
                        }
                        sendDatas[data.MechId].Add(new MqttItem
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
                        var message = payload.Value;
                        latestRcvDatas[payload.Key] = null;
                        var mech = payload.Key;
                        if (!GlobalScript.tagDatas[Name].ContainsKey(mech))
                        {
                            GlobalScript.tagDatas[Name].Add(mech, new Dictionary<string, TagInfo>());
                        }
                        var datas = JsonSerializer.Deserialize<List<MqttItem>>(message);
                        foreach (var data in datas)
                        {
                            if (!GlobalScript.tagDatas[Name][mech].ContainsKey(data.Tag))
                            {
                                GlobalScript.tagDatas[Name][mech].Add(data.Tag, ScriptableObject.CreateInstance<TagInfo>());
                            }
                            else if (GlobalScript.tagDatas[Name][mech][data.Tag] == null)
                            {
                                GlobalScript.tagDatas[Name][mech].Remove(data.Tag);
                                GlobalScript.tagDatas[Name][mech].Add(data.Tag, ScriptableObject.CreateInstance<TagInfo>());
                            }
                            GlobalScript.tagDatas[Name][mech][data.Tag].name = data.Tag;
                            GlobalScript.tagDatas[Name][mech][data.Tag].Database = Name;
                            GlobalScript.tagDatas[Name][mech][data.Tag].MechId = mech;
                            GlobalScript.tagDatas[Name][mech][data.Tag].Tag = data.Tag;
                            GlobalScript.tagDatas[Name][mech][data.Tag].Device = data.Device;
                            GlobalScript.tagDatas[Name][mech][data.Tag].Value = (int)data.Value;

                            GlobalScript.tagDatas[Name][mech][data.Tag].Value = (int)data.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
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
    public async void SendMessage(string mechId, List<MqttItem> datas)
    {
        try
        {
            // �Đڑ�����
            if (!mqttClient.IsConnected)
            {
                Reconnect();
            }
            // �f�[�^���M
            string json = JsonSerializer.Serialize(datas);
            if (true || latestSndDatas[mechId] != json)
            {
                latestSndDatas[mechId] = json;
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(mechId + "/" + Topic + "/write")
                    .WithPayload(json)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce)
                    .WithRetainFlag(false)
                    .Build();
                await mqttClient.PublishAsync(message);
            }
        }
        catch (Exception ex)
        {
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
