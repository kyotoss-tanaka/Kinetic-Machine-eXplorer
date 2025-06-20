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
    /// MQTT受信データ
    /// </summary>
    public class MqttItem
    {
        public string Tag { get; set; }
        public string Device { get; set; }
        public long Value { get; set; }
    }

    /// <summary>
    /// サーバー名
    /// </summary>
    public string Name { get { return Server + ":" + Port.ToString(); } }

    /// <summary>
    /// トピック
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
    /// 時間計測用
    /// </summary>
    private Stopwatch swRcv = new Stopwatch();
    /// <summary>
    /// サイクル時間
    /// </summary>
    private List<long> cycleRcvLaps = new List<long>();

    /// <summary>
    /// WebAPIアクセス
    /// </summary>
    private bool IsWebApi = false;

    /// <summary>
    /// 送信出来なかったタグバッファ(MQTTでデータ構成が確定する前に送信していたデータ)
    /// </summary>
    List<TagInfo> tagBuffer = new List<TagInfo>();

    // 受信データ
    private volatile Dictionary<string, string> latestRcvDatas = new();

    // 送信データ
    private volatile Dictionary<string, string> latestSndDatas = new();

    /// <summary>
    /// MQTTクライアント
    /// </summary>
    private IMqttClient mqttClient;

    /// <summary>
    /// WebAPIアクセス用URL
    /// </summary>
    private string url { get { return "http://" + Server + ":1880/api/mqtt/"; } }

    // Start is called before the first frame update
    protected override void Start()
    {
        if ((Application.platform == RuntimePlatform.Android) || (Application.platform == RuntimePlatform.IPhonePlayer))
        {
            //端末がAndroidかiOSだった場合の処理
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
    /// 接続
    /// </summary>
    private async void Connect()
    {
        // MQTTクライアント作成
        mqttClient = new MqttFactory().CreateMqttClient();
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(Server, Port)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(90))
            .Build();

        // 接続
        await mqttClient.ConnectAsync(options);

        // サブスクライブ登録
        _ = Task.Run(async () =>
        {
            await mqttClient.SubscribeAsync(new MqttTopicFilterBuilder()
           .WithTopic("+/" + Topic + "/read/#")
           .WithAtMostOnceQoS()
           .Build());
        });

        // データ受信
        mqttClient.ApplicationMessageReceivedAsync += OnMessageReceived;
    }

    /// <summary>
    /// 再接続
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
    /// 切断
    /// </summary>
    private async void Disconnect()
    {
        await mqttClient.DisconnectAsync();
        mqttClient.Dispose();
        mqttClient = null;
    }

    /// <summary>
    /// 受信割り込み
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
        // データ受信
        var topicLevels = e.ApplicationMessage.Topic.Split('/');
        var isInit = topicLevels.Length == 4;
        if (isInit || (latestRcvDatas[topicLevels[0]] == null))
        {
            latestRcvDatas[topicLevels[0]] = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment.ToArray());
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// API通信
    /// </summary>
    /// <returns></returns>
    private IEnumerator DataUpdate()
    {
        while (this.enabled)
        {
            // データ交換処理
            DataExchangeProcess();

            // データ更新処理
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
                var datas = new List<TagInfoCom>();
                var send = new List<TagInfo>();
                // 未送信データ更新
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
                // 送信済データ削除
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
    /// データ更新
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
                // WebAPIアクセス
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
                // データ送信
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
                // データ読込
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
    /// APIでのデータ更新
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
                    // 受信処理
                    var rcvDatas = JsonSerializer.Deserialize<List<LatestData>>(req.downloadHandler.text);
                    foreach (var data in rcvDatas)
                    {
                        var mech = data.mech_id;
                        if (!GlobalScript.tagDatas[Name].ContainsKey(mech))
                        {
                            // 機番作成
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
    /// データ送信
    /// </summary>
    /// <param name="mechId"></param>
    /// <param name="topic"></param>
    /// <param name="data"></param>
    public async void SendMessage(string mechId, List<MqttItem> datas)
    {
        try
        {
            // 再接続判定
            if (!mqttClient.IsConnected)
            {
                Reconnect();
            }
            // データ送信
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
    /// パラメータをセットする
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
