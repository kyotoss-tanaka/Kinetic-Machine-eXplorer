using Npgsql;
using Parameters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using static ComOpcUA;
using static OVRPlugin;

public class ComPostgres : ComBaseScript
{
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
    /// 処理中
    /// </summary>
    private bool isProcessing = false;
    /// <summary>
    /// DBのデータ
    /// </summary>
    public class LatestData
    {
        public DateTime datetime { get; set; }
        public string mech_id { get; set; }
        public string event_id { get; set; }
        public string device_name { get; set; }
        public int data_value { get; set; }
    }

    /// <summary>
    /// 受信タグ
    /// </summary>
    private List<LatestData> tagInfos;

    /// <summary>
    /// タグ数
    /// </summary>
    private int tagCount = 0;

    [SerializeField]
    int cntTest;
    // Start is called before the first frame update
    protected override void Start()
    {
        tagInfos = new();
        if ((Application.platform == RuntimePlatform.Android) || (Application.platform == RuntimePlatform.IPhonePlayer))
        {
            //端末がAndroidかiOSだった場合の処理
            IsWebApi = true;
        }
        if (!GlobalScript.tagDatas.ContainsKey(Name))
        {
            GlobalScript.tagDatas.Add(Name, new Dictionary<string, Dictionary<string, TagInfo>>());
        }
        if (!GlobalScript.postgreses.ContainsKey(Name))
        {
            GlobalScript.postgreses.Add(Name, this);
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
    /*
    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void Update()
    {
        base.Update();
        if (IsTask)
        {
            if (!isProcessing)
            {
                isProcessing = true;
                _ = DataUpdateTask();
            }
            else
            {
            }
        }
    }

    /// <summary>
    /// 非同期更新
    /// </summary>
    /// <returns></returns>
    private async Task DataUpdateTask()
    {
        if (GlobalScript.isLoaded)
        {
            // データ交換処理
            DataExchangeProcess();

            // データ更新処理
            await RenewDataAsync();
        }
        // 処理完了
        isProcessing = false;
    }

    /// <summary>
    /// 非同期DBアクセス
    /// </summary>
    /// <returns></returns>
    private async Task<bool> RenewDataAsync()
    {
        base.RenewData();
        var sw = new System.Diagnostics.Stopwatch();
        sw.Start();
        var connectionString = $"Server={Server};Port={Port};Database={Database};User ID={User};Password={Password};Max Auto Prepare=1;";
        using (var connection = new NpgsqlConnection(connectionString))
        {
            try
            {
                // 接続の確立
                connection.Open();
                // 書き込み処理
                if (!isClientMode && writeDatas.Count > 0)
                {
                    using (var command = connection.CreateCommand())
                    {
                        // 書き込み実行
                        command.CommandText = "";
                        foreach (var tag in writeDatas)
                        {
                            command.CommandText += $"UPDATE latestdata SET data_value = {tag.Value} where mech_id = '{tag.MechId}' and  event_id = '{tag.Tag}';";
                        }
                        command.Prepare();
                        command.ExecuteNonQuery();
                        command.Dispose();
                        writeDatas.Clear();
                    }
                }
                tagInfos = new();
                // 読み込み処理
                using (var command = connection.CreateCommand())
                {
                    // SELECT文の実行
                    command.CommandText = "SELECT * FROM latestdata;";
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        avgProcess = sw.ElapsedMilliseconds;//★
                        // 1行ずつデータを取得
                        while (reader.Read())
                        {
                            tagInfos.Add(new LatestData
                            {
                                mech_id = reader["mech_id"].ToString(),
                                event_id = reader["event_id"].ToString(),
                                device_name = reader["device_name"].ToString(),
                                data_value = int.Parse(reader["data_value"].ToString())
                            });
                        }
                    }
                }
                foreach (var tagInfo in tagInfos)
                {
                    var mech = tagInfo.mech_id;
                    var tag = tagInfo.event_id;
                    if (tagCount != tagInfos.Count)
                    {
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
                    }
                    var dct = GlobalScript.tagDatas[Name][mech][tag];
                    dct.name = tag;
                    dct.Database = Name;
                    dct.MechId = mech;
                    dct.Tag = tag;
                    dct.Device = tagInfo.device_name;
                    dct.Value = tagInfo.data_value;
                }
                tagCount = tagInfos.Count;
                // DBのデータ作成完了
                isRcvDb = true;
            }
            catch (Exception ex)
            {
                Debug.LogError(ex.InnerException.Message);
            }
        }
        processTime = sw.ElapsedMilliseconds;
        return true;
    }
    */

    /// <summary>
    /// API通信
    /// </summary>
    /// <returns></returns>
    private IEnumerator DataUpdate()
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
                foreach (var tag in tags)
                {
                    if (GlobalScript.tagDatas[Name].ContainsKey(tag.MechId) && GlobalScript.tagDatas[Name][tag.MechId].ContainsKey(tag.Tag))
                    {
                        if (isFirst || (GlobalScript.tagDatas[Name][tag.MechId][tag.Tag].Value != tag.Value))
                        {
                            GlobalScript.tagDatas[Name][tag.MechId][tag.Tag].Value = tag.Value;
                            datas.Add(new TagInfoCom { MechId = tag.MechId, Tag = tag.Tag, Value = tag.Value, fValue = tag.fValue, isFloat = tag.isFloat });
                        }
                    }
                }
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
            if (!isProcessing)
            {
                isProcessing = true;
                if (tagInfos.Count > 0)
                {
                    foreach (var tagInfo in tagInfos)
                    {
                        var mech = tagInfo.mech_id;
                        var tag = tagInfo.event_id;
                        if (tagCount != tagInfos.Count)
                        {
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
                        }
                        var dct = GlobalScript.tagDatas[Name][mech][tag];
                        dct.name = tag;
                        dct.Database = Name;
                        dct.MechId = mech;
                        dct.Tag = tag;
                        dct.Device = tagInfo.device_name;
                        dct.Value = tagInfo.data_value;
                    }
                    tagCount = tagInfos.Count;
                    tagInfos.Clear();
                    // DBのデータ作成完了
                    isRcvDb = true;
                    cntTest = 0;
                }
                else
                {
                    cntTest++;
                }
                // DBへのアクセスを別タスクに
                _ = Task.Run(() =>
                {
                    var sw = new System.Diagnostics.Stopwatch();
                    sw.Start();
                    var connectionString = $"Server={Server};Port={Port};Database={Database};User ID={User};Password={Password};Max Auto Prepare=1;";
                    using (var connection = new NpgsqlConnection(connectionString))
                    {
                        try
                        {
                            // 接続の確立
                            connection.Open();
                            // 書き込み処理
                            lock (objLock)
                            {
                                if (!isClientMode && writeDatas.Count > 0)
                                {
                                    using (var command = connection.CreateCommand())
                                    {
                                        // 書き込み実行
                                        command.CommandText = "";
                                        var sb = new StringBuilder();
                                        foreach (var tag in writeDatas)
                                        {
                                            sb.Append($"UPDATE latestdata SET data_value = {tag.Value} where mech_id = '{tag.MechId}' and  event_id = '{tag.Tag}';");
                                        }
                                        command.CommandText = sb.ToString();
                                        command.Prepare();
                                        command.ExecuteNonQuery();
                                        command.Dispose();
                                        writeDatas.Clear();
                                    }
                                }
                            }
                            // 読み込み処理
                            tagInfos = new();
                            using (var command = connection.CreateCommand())
                            {
                                // SELECT文の実行
                                command.CommandText = "SELECT * FROM latestdata;";
                                command.Prepare();
                                using (var reader = command.ExecuteReader())
                                {
                                    // 1行ずつデータを取得
                                    while (reader.Read())
                                    {
                                        tagInfos.Add(new LatestData
                                        {
                                            mech_id = reader.GetString(1),
                                            event_id = reader.GetString(2),
                                            device_name = reader.GetString(3),
                                            data_value = reader.GetInt32(4)
                                        });
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError(ex.InnerException.Message);
                        }
                    }
                    processTime = sw.ElapsedMilliseconds;
                    isProcessing = false;
                });
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
                    tagInfos = JsonSerializer.Deserialize<List<LatestData>>(req.downloadHandler.text);
                    foreach (var data in tagInfos)
                    {
                        var mech = data.mech_id;
                        var tag = data.event_id;
                        var dev = data.device_name;
                        var val = int.Parse(data.data_value.ToString());
                        if (tagInfos.Count != tagCount)
                        {
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
                        }
                        GlobalScript.tagDatas[Name][mech][tag].name = tag;
                        GlobalScript.tagDatas[Name][mech][tag].Database = Name;
                        GlobalScript.tagDatas[Name][mech][tag].MechId = mech;
                        GlobalScript.tagDatas[Name][mech][tag].Tag = tag;
                        GlobalScript.tagDatas[Name][mech][tag].Device = dev;
                        GlobalScript.tagDatas[Name][mech][tag].Value = val;
                    }
                    tagCount = tagInfos.Count;
                    // DBのデータ作成完了
                    isRcvDb = true;
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
