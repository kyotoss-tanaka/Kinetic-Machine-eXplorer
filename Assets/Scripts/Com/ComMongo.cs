using Npgsql;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using MongoDB.Bson;
using Unity.VisualScripting;
using MongoDB.Bson.Serialization.Attributes;
using UnityEngine.PlayerLoop;
using Parameters;
using static KssBaseScript;

public class ComMongo : ComBaseScript
{
    [SerializeField]
    private string collectionName = "UnityData";

    /// <summary>
    /// MongoDBアクセス用クライアント
    /// </summary>
    private MongoClient m_mongoClient;

    /// <summary>
    /// アクセスするDB
    /// </summary>
    private IMongoDatabase m_database;

    /// <summary>
    /// データベースのアドレス
    /// </summary>
    private string m_connectAddress = "mongodb://localhost";

    /// <summary>
    /// コレクション
    /// </summary>
    private IMongoCollection<UnitUniversalRwData> collection;

    /// <summary>
    /// サーバー名
    /// </summary>
    public string Name { get { return Server + ":" + Port.ToString(); } }

    /// <summary>
    /// WebAPIアクセス
    /// </summary>
    private bool IsWebApi = false;

    /// <summary>
    /// WebAPIアクセス用URL
    /// </summary>
    private string url { get { return "http://" + Server + ":1880/api/db/"; } }

    /// <summary>
    /// DBのデータ
    /// </summary>
    [Serializable]
    [BsonIgnoreExtraElements]
    public class UnitUniversalRwData
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string MechID { get; set; }

        /// <summary>
        /// 品種番号
        /// </summary>
        public int KindNo { get; set; }

        /// <summary>
        /// 品種名
        /// </summary>
        public string KindName { get; set; }

        /// <summary>
        /// ユニットタイプ
        /// </summary>
        public string UnitType { get; set; }

        /// <summary>
        /// 保存日時
        /// </summary>
        public DateTime DateTime { get; set; }

        /// <summary>
        /// 履歴データ
        /// </summary>
        public List<ClsUniversalRegData> Datas { get; set; }

        /// <summary>
        /// 最新データフラグ
        /// </summary>
        public bool IsLatest { get; set; }

        /// <summary>
        /// 書き込みフラグ
        /// </summary>
        public bool IsWrite { get; set; }
    }

    /// <summary>
    /// デバイス登録データクラス
    /// </summary>
    [Serializable]
    [BsonIgnoreExtraElements]
    public class ClsUniversalRegData
    {
        /// <summary>
        /// 名前
        /// </summary>
        public string Name { set; get; }

        /// <summary>
        /// タグ名
        /// </summary>
        public string Tag { set; get; }

        /// <summary>
        /// 値
        /// </summary>
        public long Value { set; get; }
    }

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
        if (!GlobalScript.mongos.ContainsKey(Name))
        {
            GlobalScript.mongos.Add(Name, this);
        }
        if (IsWebApi)
        {
            StartCoroutine(RenewDataApi());
        }
        else
        {
            StartCoroutine(DataUpdate());
        }
    }

    /// <summary>
    /// API通信
    /// </summary>
    /// <returns></returns>
    private IEnumerator DataUpdate()
    {
        while (true)
        {
            lock (objLock)
            {
                RenewData();
            }
            yield return new WaitForSeconds(Cycle / 1000f);
        }
    }

    /// <summary>
    /// タグに値をセットする
    /// </summary>
    /// <param name="tag"></param>
    /// <param name=""></param>
    public void SetDatas(List<TagInfo> tags)
    {
        if (!isClientMode)
        {
            if ((Application.platform != RuntimePlatform.Android) && (Application.platform != RuntimePlatform.IPhonePlayer))
            {
                var datas = new List<TagInfoCom>();
                foreach (var tag in tags)
                {
                    if (GlobalScript.tagDatas[Name].ContainsKey(tag.MechId) && GlobalScript.tagDatas[Name][tag.MechId].ContainsKey(tag.Tag))
                    {
                        GlobalScript.tagDatas[Name][tag.MechId][tag.Tag].Value = tag.Value;
                        datas.Add(new TagInfoCom { MechId = tag.MechId, Tag = tag.Tag, Value = tag.Value, fValue = tag.fValue, isFloat = tag.isFloat });
                    }
                }
                if (datas.Count == 0)
                {
                    return;
                }
                lock (objLock)
                {
                    if (IsWebApi)
                    {
                        /*
                        // WebAPIアクセス
                        UnityWebRequest req = UnityWebRequest.Post(url + $"latestdata/write/multiple", JsonSerializer.Serialize(datas), "application/json");
                        req.SendWebRequest();
                        try
                        {
                            if (req.isNetworkError || req.isHttpError)
                            {
                                Debug.Log(req.error);
                            }
                            else if (req.responseCode == 200)
                            {
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogException(ex);
                        }
                        */
                    }
                    else
                    {
                        // MongoDBとの接続作成
                        if (m_database == null)
                        {
                            m_connectAddress = "mongodb://" + Server;
                            m_mongoClient = new MongoClient(m_connectAddress);
                            // DB名でDB取得（なかったらその名前のDB作成）
                            m_database = m_mongoClient.GetDatabase(Database);
                            collection = getCollection<UnitUniversalRwData>(collectionName);
                        }
                        try
                        {
                            var fb = Builders<UnitUniversalRwData>.Filter;
                            var options = new FindOptions { AllowPartialResults = true };
                            var latests = collection.Find(d => d.IsLatest, options).ToList();
                            if (latests.Count > 0)
                            {
                                var dctDatas = new Dictionary<string, List<TagInfoCom>>();
                                foreach (var data in datas)
                                {
                                    if (!dctDatas.ContainsKey(data.MechId))
                                    {
                                        dctDatas.Add(data.MechId, new List<TagInfoCom>());
                                    }
                                    dctDatas[data.MechId].Add(data);
                                }
                                foreach (var mech in dctDatas)
                                {
                                    var latest = latests.Find(d => d.MechID == mech.Key);
                                    if (latest != null)
                                    {
                                        var filter = fb.And(fb.Eq("MechID", mech.Key), fb.Eq("IsLatest", true));
                                        var update = Builders<UnitUniversalRwData>.Update.Set(d => d.Datas, latest.Datas);
                                        foreach (var data in mech.Value)
                                        {
                                            var tmp = latest.Datas.Find(d => d.Tag == data.Tag);
                                            tmp.Value = data.Value;
                                        }
                                        collection.UpdateOne(filter, update);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError(ex.Message);
                        }
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
            // DB作成
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
            // MongoDBとの接続作成
            if (m_database == null)
            {
                m_connectAddress = "mongodb://" + Server;
                m_mongoClient = new MongoClient(m_connectAddress);
                // DB名でDB取得（なかったらその名前のDB作成）
                m_database = m_mongoClient.GetDatabase(Database);
                collection = getCollection<UnitUniversalRwData>(collectionName);
            }
            try
            {
                var fb = Builders<UnitUniversalRwData>.Filter;
                var options = new FindOptions { AllowPartialResults = true };
                var latests = collection.Find(d => d.IsLatest, options).ToList();
                foreach(var latest in latests)
                {
                    foreach (var data in latest.Datas)
                    {
                        var mech = latest.MechID;
                        if (!GlobalScript.tagDatas[Name].ContainsKey(mech))
                        {
                            // 機番作成
                            GlobalScript.tagDatas[Name].Add(mech, new Dictionary<string, TagInfo>());
                        }
                        var tag = data.Tag;
                        var dev = data.Name;
                        var val = (int)data.Value;
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
                Debug.LogError(ex.Message);
            }
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
                        var dev = data.event_id;
                        var val = int.Parse(data.data_value.ToString());
                        if (!GlobalScript.tagDatas[Name][mech].ContainsKey(dev))
                        {
                            GlobalScript.tagDatas[Name][mech].Add(dev, ScriptableObject.CreateInstance<TagInfo>());
                        }
                        else if (GlobalScript.tagDatas[Name][mech][dev] == null)
                        {
                            GlobalScript.tagDatas[Name][mech].Remove(dev);
                            GlobalScript.tagDatas[Name][mech].Add(dev, ScriptableObject.CreateInstance<TagInfo>());
                        }
                        GlobalScript.tagDatas[Name][mech][dev].name = dev;
                        GlobalScript.tagDatas[Name][mech][dev].Database = Name;
                        GlobalScript.tagDatas[Name][mech][dev].MechId = mech;
                        GlobalScript.tagDatas[Name][mech][dev].Tag = dev;
                        GlobalScript.tagDatas[Name][mech][dev].Value = val;
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
    /// コレクションの取得
    /// </summary>
    /// <typeparam name="T">型</typeparam>
    /// <param name="collectionName">コレクション名</param>
    /// <param name="maxSize">最大サイズ[MB]</param>
    /// <returns>コレクション</returns>
    private IMongoCollection<T> getCollection<T>(string collectionName)
    {
        return m_database.GetCollection<T>(collectionName);
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
