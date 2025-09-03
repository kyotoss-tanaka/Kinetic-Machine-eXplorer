using Npgsql;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using static OpcUaTagInfo;

public class ComOpcUaApi : ComBaseScript
{
    /// <summary>
    /// IPアドレス
    /// </summary>
    [SerializeField]
    private string IpAddress = "127.0.0.1";

    /*
    /// <summary>
    /// ポート設定
    /// </summary>
    [SerializeField]
    private int Port = 1880;
    */

    /// <summary>
    /// チャンネル設定
    /// </summary>
    [SerializeField]
    public int Ch = 0;

    /// <summary>
    /// APIのURL
    /// </summary>
    private string url = "";

    /// <summary>
    /// タグ情報
    /// </summary>
    private OpcUaTagInfo tagInfo;

    /// <summary>
    /// キー
    /// </summary>
    public string Name { get { return IpAddress + ":" + Port.ToString() + "(" + Ch.ToString() + ")"; } }

    /// <summary>
    /// 機番
    /// </summary>
    private string MechId = "OpcUA";

    public class OpcUaUrls
    {
        public int ns;
        public List<OpcUaUrl> urls = new List<OpcUaUrl>();
    }

    public class OpcUaUrl
    {
        public bool isKey;
        public bool isWrite;
        public bool isArray;
        public string tag;
        public string datatype;
    }

    [Serializable]
    public class OpcUaResData
    {
        public string tag { get; set; }
        public object value { get; set; }
    }

    [Serializable]
    public class OpcUaTopic
    {
        public int ch { get; set; }
        public List<string> topics { get; set; }
    }

    /// <summary>
    /// 受信データ
    /// </summary>
    Dictionary<string, OpcUaUrl> rcvData = new Dictionary<string, OpcUaUrl>();

    /// <summary>
    /// 受信URLリスト
    /// </summary>
    List<OpcUaUrls> urls = new List<OpcUaUrls>();

    // Start is called before the first frame update
    protected override void Start()
    {
        SetTagInfo();

        if (!GlobalScript.opcuaapis.ContainsKey(Name))
        {
            GlobalScript.opcuaapis.Add(Name, this);
        }
        StartCoroutine(RenewDataApi());
    }

    /// <summary>
    /// API通信
    /// </summary>
    /// <returns></returns>
    private IEnumerator RenewDataApi()
    {
        while (true)
        {
            var topics = new OpcUaTopic { ch = Ch, topics = new List<string>() };
            foreach (var tmpUrl in urls)
            {
                foreach (var url in tmpUrl.urls)
                {
                    if (url.isKey && url.isWrite)
                    {
                        continue;
                    }
                    var topic = $"ns={tmpUrl.ns};s={url.tag}";
                    topics.topics.Add(topic);
                    if (!rcvData.ContainsKey(topic))
                    {
                        rcvData.Add(topic, url);
                    }
                }
            }
            UnityWebRequest req = UnityWebRequest.Post(this.url + $"read/multiple", JsonSerializer.Serialize(topics), "application/json");
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
                    var rcvDatas = JsonSerializer.Deserialize<OpcUaResData[]>(req.downloadHandler.text);
                    foreach (var data in rcvDatas)
                    {
                        var value = data.value.ToString();
                        var url = rcvData[data.tag];
                        if (url.isArray)
                        {
                            var datas = new List<string>();
                            if (value[0] == '[')
                            {
                                datas = value.Replace("[", "").Replace("]", "").Split(',').ToList();
                            }
                            else if (value[0] == '{')
                            {
                                var tmp = value.Replace("{", "").Replace("}", "").Split(',').ToList();
                                foreach (var s in tmp)
                                {
                                    datas.Add(s.Split(':')[1]);
                                }
                            }
                            for (var i = 0; i < datas.Count; i++)
                            {
                                var tag = $"{url.tag}[{i}]";
                                SetTagData(url, tag, datas[i]);
                            }
                        }
                        else
                        {
                            SetTagData(url, url.tag, value);
                        }
                    }
                }
                else
                {
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
            yield return new WaitForSeconds(Cycle / 1000f);
        }
    }

    /// <summary>
    /// タグにデータをセットする
    /// </summary>
    /// <param name="url"></param>
    /// <param name="tag"></param>
    /// <param name="value"></param>
    private void SetTagData(OpcUaUrl url, string tag, string value)
    {
        if (!GlobalScript.tagDatas[Name][MechId].ContainsKey(tag))
        {
            var tagInfo = ScriptableObject.CreateInstance<TagInfo>();
            tagInfo.Database = Name;
            tagInfo.MechId = MechId;
            tagInfo.name = tag;
            tagInfo.Tag = tag;
            GlobalScript.tagDatas[Name][MechId].Add(tag, tagInfo);
            url.isKey = true;
        }
        if (url.datatype == "Boolean")
        {
            GlobalScript.tagDatas[Name][MechId][tag].Value = value == "true" ? 1 : 0;
        }
        else if (url.datatype == "Int32")
        {
            GlobalScript.tagDatas[Name][MechId][tag].Value = int.Parse(value);
        }
        else if (url.datatype == "Float")
        {
            GlobalScript.tagDatas[Name][MechId][tag].fValue = float.Parse(value);
            GlobalScript.tagDatas[Name][MechId][tag].Value = (int)GlobalScript.tagDatas[Name][MechId][tag].fValue;
            GlobalScript.tagDatas[Name][MechId][tag].isFloat = true;
        }
    }

    /// <summary>
    /// タグに値をセットする
    /// </summary>
    /// <param name="tag"></param>
    /// <param name=""></param>
    public void SetData(string tag, bool isArray, int index, int value)
    {
        var tmpTag = tag + (isArray ? "[" + index + "]" : "");
        SetData(tmpTag, value);
    }

    /// <summary>
    /// タグに値をセットする
    /// </summary>
    /// <param name="tag"></param>
    /// <param name=""></param>
    public void SetData(string tag, int value)
    {
        if (!isClientMode)
        {
            if ((Application.platform != RuntimePlatform.Android) && (Application.platform != RuntimePlatform.IPhonePlayer))
            {
                if (GlobalScript.tagDatas.ContainsKey(Name))
                {
                    if (GlobalScript.tagDatas[Name].ContainsKey(MechId) && GlobalScript.tagDatas[Name][MechId].ContainsKey(tag))
                    {
                        GlobalScript.tagDatas[Name][MechId][tag].Value = value;
                        string url = "";
                        foreach (var tmpUrl in urls)
                        {
                            var urlData = tmpUrl.urls.Find(d => tag.Contains(d.tag));
                            if (urlData != null)
                            {
                                if (urlData.datatype == "Boolean")
                                {
                                    var tmp = value == 0 ? "false" : "true";
                                    url = this.url + $"write?ns={tmpUrl.ns}&datatype={urlData.datatype}&tag={tag}&value={tmp}&ch={Ch}";
                                }
                                else
                                {
                                    url = this.url + $"write?ns={tmpUrl.ns}&datatype={urlData.datatype}&tag={tag}&value={value}&ch={Ch}";
                                }
                                break;
                            }
                        }
                        if (url != "")
                        {
                            // 書き込み処理
                            UnityWebRequest req = UnityWebRequest.Get(url);
                            req.SendWebRequest();
                        }
                    }
                }
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
        foreach (var tag in tags)
        {
            SetData(tag.Tag, tag.Value);
        }
    }

    /// <summary>
    /// データ更新
    /// </summary>
    public override void RenewData()
    {
#if UNITY_EDITOR
        if (!EditorApplication.isPlaying)
        {
            SetTagInfo();

            StartCoroutine(RenewDataApi());
        }
#endif
    }

    /// <summary>
    /// タグ情報をセットする
    /// </summary>
    private void SetTagInfo()
    {
        if (!GlobalScript.tagDatas.ContainsKey(Name))
        {
            GlobalScript.tagDatas.Add(Name, new Dictionary<string, Dictionary<string, TagInfo>>());
            GlobalScript.tagDatas[Name].Add(MechId, new Dictionary<string, TagInfo>());
        }
        if ((Application.platform == RuntimePlatform.Android) || (Application.platform == RuntimePlatform.IPhonePlayer))
        {
            // AndroidとiOSならコード直書き
            tagInfo = JsonUtility.FromJson<OpcUaTagInfo>("{\r\n\t\"tags\": [\r\n\t\t{\r\n\t\t\t\"ns\": 6,\r\n\t\t\t\"booleanTag\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"isWrite\": true,\r\n\t\t\t\t\t\"isArray\": true,\r\n\t\t\t\t\t\"count\": 1,\r\n\t\t\t\t\t\"name\": \"::AsGlobalPV:iActivateController\",\r\n\t\t\t\t\t\"children\": []\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"isWrite\": true,\r\n\t\t\t\t\t\"isArray\": true,\r\n\t\t\t\t\t\"count\": 2,\r\n\t\t\t\t\t\"name\": \"::AsGlobalPV:iSecgRun\",\r\n\t\t\t\t\t\"children\": []\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"isWrite\": true,\r\n\t\t\t\t\t\"isArray\": true,\r\n\t\t\t\t\t\"count\": 64,\r\n\t\t\t\t\t\"name\": \"::AsGlobalPV:iPpDone\",\r\n\t\t\t\t\t\"children\": []\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"isWrite\": false,\r\n\t\t\t\t\t\"isArray\": true,\r\n\t\t\t\t\t\"count\": 64,\r\n\t\t\t\t\t\"name\": \"::AsGlobalPV:oPpMoveDone\",\r\n\t\t\t\t\t\"children\": []\r\n\t\t\t\t}\r\n\t\t\t],\r\n\t\t\t\"int32Tag\": [],\r\n\t\t\t\"floatTag\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"isWrite\": false,\r\n\t\t\t\t\t\"isArray\": true,\r\n\t\t\t\t\t\"count\": 20,\r\n\t\t\t\t\t\"name\": \"::Vis6D:Vis6DComData.Cyclic.Sh.PosX\",\r\n\t\t\t\t\t\"children\": []\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"isWrite\": false,\r\n\t\t\t\t\t\"isArray\": true,\r\n\t\t\t\t\t\"count\": 20,\r\n\t\t\t\t\t\"name\": \"::Vis6D:Vis6DComData.Cyclic.Sh.PosY\",\r\n\t\t\t\t\t\"children\": []\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"isWrite\": false,\r\n\t\t\t\t\t\"isArray\": true,\r\n\t\t\t\t\t\"count\": 20,\r\n\t\t\t\t\t\"name\": \"::Vis6D:Vis6DComData.Cyclic.Sh.RotZ\",\r\n\t\t\t\t\t\"children\": []\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t}\r\n\t]\r\n}");
        }
        else
        {
            tagInfo = (OpcUaTagInfo)GlobalScript.LoadJson<OpcUaTagInfo>("opcua" + Ch.ToString());
        }
        url = $"http://{IpAddress}:{Port}/api/opcua/";

        urls = GetUrls();
    }

    /// <summary>
    /// APIのURLを取得する
    /// </summary>
    /// <returns></returns>
    private List<OpcUaUrls> GetUrls()
    {
        var ret = new List<OpcUaUrls>();
        foreach (var tags in tagInfo.tags)
        {
            var booleans = new OpcUaUrls
            {
                ns = tags.ns
            };
            var int32s = new OpcUaUrls
            {
                ns = tags.ns
            };
            var floats = new OpcUaUrls
            {
                ns = tags.ns
            };
            foreach (var tag in tags.booleanTag)
            {
                booleans.urls.AddRange(GetUrls("Boolean", tag));
            }
            foreach (var tag in tags.int32Tag)
            {
                int32s.urls.AddRange(GetUrls("Int32", tag));
            }
            foreach (var tag in tags.floatTag)
            {
                floats.urls.AddRange(GetUrls("Float", tag));
            }
            ret.Add(booleans);
            ret.Add(int32s);
            ret.Add(floats);
        }
        return ret;
    }

    /// <summary>
    /// APIのURLを取得する
    /// </summary>
    /// <param name="tag"></param>
    /// <returns></returns>
    private List<OpcUaUrl> GetUrls(string datatype, OpcUaTag tag, string parent = "")
    {
        var ret = new List<OpcUaUrl>();
        if (tag.isArray)
        {
            if (tag.children.Count > 0)
            {
                // 子供持ち
                for (var i = 0; i < tag.count; i++)
                {
                    for (var j = 0; j < tag.children.Count; j++)
                    {
                        var tmpParent = parent == "" ? tag.name + "[" + i.ToString() + "]" : parent + "." + tag.name + "[" + i.ToString() + "]";
                        ret.AddRange(GetUrls(datatype, tag.children[j], tmpParent));
                    }
                }
            }
            else
            {
                // 配列
                if (tag.isArray)
                {
                    var url = new OpcUaUrl
                    {
                        isWrite = tag.isWrite,
                        tag = parent == "" ? tag.name : parent + "." + tag.name,
                        isArray = true,
                        datatype = datatype
                    };
                    ret.Add(url);
                    for (var i = 0; i < tag.count; i++)
                    {
                        var dev = url.tag + "[" + i + "]";
                        if (!GlobalScript.tagDatas[Name][MechId].ContainsKey(dev))
                        {
                            GlobalScript.tagDatas[Name][MechId].Add(dev, ScriptableObject.CreateInstance<TagInfo>());
                        }
                        else if (GlobalScript.tagDatas[Name][MechId][dev] == null)
                        {
                            GlobalScript.tagDatas[Name][MechId].Remove(dev);
                            GlobalScript.tagDatas[Name][MechId].Add(dev, ScriptableObject.CreateInstance<TagInfo>());
                        }
                        GlobalScript.tagDatas[Name][MechId][dev].name = dev;
                        GlobalScript.tagDatas[Name][MechId][dev].Database = Name;
                        GlobalScript.tagDatas[Name][MechId][dev].MechId = MechId;
                        GlobalScript.tagDatas[Name][MechId][dev].Tag = dev;
                    }
                }
                else
                {
                    for (var i = 0; i < tag.count; i++)
                    {
                        var url = new OpcUaUrl
                        {
                            isWrite = tag.isWrite,
                            tag = parent == "" ? tag.name + "[" + i.ToString() + "]" : parent + "." + tag.name + "[" + i.ToString() + "]",
                            isArray = false,
                            datatype = datatype
                        };
                        ret.Add(url);
                        if (!GlobalScript.tagDatas[Name][MechId].ContainsKey(url.tag))
                        {
                            GlobalScript.tagDatas[Name][MechId].Add(url.tag, ScriptableObject.CreateInstance<TagInfo>());
                        }
                        else if (GlobalScript.tagDatas[Name][MechId][url.tag] == null)
                        {
                            GlobalScript.tagDatas[Name][MechId].Remove(url.tag);
                            GlobalScript.tagDatas[Name][MechId].Add(url.tag, ScriptableObject.CreateInstance<TagInfo>());
                        }
                        GlobalScript.tagDatas[Name][MechId][url.tag].name = url.tag;
                        GlobalScript.tagDatas[Name][MechId][url.tag].Database = Name;
                        GlobalScript.tagDatas[Name][MechId][url.tag].MechId = MechId;
                        GlobalScript.tagDatas[Name][MechId][url.tag].Tag = url.tag;
                    }
                }
            }
        }
        else
        {
            var url = new OpcUaUrl
            {
                isWrite = tag.isWrite,
                tag = parent == "" ? tag.name : parent + "." + tag.name,
                isArray = false,
                datatype = datatype
            };
            if (!GlobalScript.tagDatas[Name][MechId].ContainsKey(url.tag))
            {
                GlobalScript.tagDatas[Name][MechId].Add(url.tag, ScriptableObject.CreateInstance<TagInfo>());
            }
            else if (GlobalScript.tagDatas[Name][MechId][url.tag] == null)
            {
                GlobalScript.tagDatas[Name][MechId].Remove(url.tag);
                GlobalScript.tagDatas[Name][MechId].Add(url.tag, ScriptableObject.CreateInstance<TagInfo>());
            }
            GlobalScript.tagDatas[Name][MechId][url.tag].name = url.tag;
            GlobalScript.tagDatas[Name][MechId][url.tag].Database = Name;
            GlobalScript.tagDatas[Name][MechId][url.tag].MechId = MechId;
            GlobalScript.tagDatas[Name][MechId][url.tag].Tag = url.tag;
            ret.Add(url);

        }
        return ret;
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
        IpAddress = GetStringFromPrm(root, "IpAddress");
        Port = GetInt32FromPrm(root, "Port");
        Ch = GetInt32FromPrm(root, "Ch");
    }
}
