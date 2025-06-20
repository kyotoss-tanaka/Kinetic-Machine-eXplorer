using Parameters;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using TMPro;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Networking;

public static class GlobalScript
{
    /// <summary>
    /// 機構位置
    /// </summary>
    public class MechPos
    {
        public int x;
        public int y;
        public int z;
    }

    /// <summary>
    /// 機構情報
    /// </summary>
    public class MechInfo
    {
        public int no;
        public int mechType;
        public MechPos pos;
    }

    /// <summary>
    /// 変更中マテリアル
    /// </summary>
    public class ChangeMaterial
    {
        /// <summary>
        /// 変更中
        /// </summary>
        public bool isChange;
        /// <summary>
        /// 元のマテリアル
        /// </summary>
        public Material material;
    }

    /// <summary>
    /// コールバックタグデータ
    /// </summary>
    public class CallbackTag
    {
        public string database { get; set; }
        /// <summary>
        /// 折り返し用入力
        /// </summary>
        public TagInfo input;
        /// <summary>
        /// 折り返し用出力
        /// </summary>
        public TagInfo output;
        /// <summary>
        /// カウンタ用入力
        /// </summary>
        public TagInfo cntIn;
        /// <summary>
        /// カウンタ用出力
        /// </summary>
        public TagInfo cntOut;
        /// <summary>
        /// 現在の値
        /// </summary>
        public bool value;
        /// <summary>
        /// 現在の値
        /// </summary>
        public int count;
        /// <summary>
        /// ストップウォッチ
        /// </summary>
        public System.Diagnostics.Stopwatch stopwatch;
        /// <summary>
        /// 時間
        /// </summary>
        public long laps { get; set; }
    }

    /// <summary>
    /// MICKS内機構の位置情報
    /// </summary>
    public static Dictionary<string, List<MechInfo>> micksMechs = new Dictionary<string, List<MechInfo>>();

    /// <summary>
    /// タグデータ
    /// </summary>
    public static Dictionary<string, Dictionary<string, Dictionary<string, TagInfo>>> tagDatas = new Dictionary<string, Dictionary<string, Dictionary<string, TagInfo>>>();

    /// <summary>
    /// Postgres
    /// </summary>
    public static Dictionary<string, ComPostgres> postgreses = new Dictionary<string, ComPostgres>();

    /// <summary>
    /// MongoDB
    /// </summary>
    public static Dictionary<string, ComMongo> mongos = new Dictionary<string, ComMongo>();

    /// <summary>
    /// OPC UA
    /// </summary>
    public static Dictionary<string, ComOpcUA> opcuas = new Dictionary<string, ComOpcUA>();

    /// <summary>
    /// MQTT
    /// </summary>
    public static Dictionary<string, ComMqtt> mqtts = new Dictionary<string, ComMqtt>();

    /// <summary>
    /// 内部通信
    /// </summary>
    public static Dictionary<string, ComInner> inners = new Dictionary<string, ComInner>();

    /// <summary>
    /// ワーク
    /// </summary>
    public static Dictionary<string, GameObject> works = new Dictionary<string, GameObject>();

    /// <summary>
    /// 機構登録情報
    /// </summary>
    public static Dictionary<string, List<GameObject>> regObjects = new Dictionary<string, List<GameObject>>();

    /// <summary>
    /// スクリプト登録情報
    /// </summary>
    public static Dictionary<string, List<GameObject>> regScripts = new Dictionary<string, List<GameObject>>();

    /// <summary>
    /// ロック用オブジェクト
    /// </summary>
    public static object objLock = new object();

    /// <summary>
    /// ロード中フラグ
    /// </summary>
    public static bool isLoading = false;

    /// <summary>
    /// 衝突表示モード
    /// </summary>
    public static bool isCollision = false;

    /// <summary>
    /// デバッグ出力用モード
    /// </summary>
    public static bool isDebug = false;

    /// <summary>
    /// コールバック用タグデータ
    /// </summary>
    public static List<CallbackTag> callbackTags { get; set; } = new List<CallbackTag>();

    /// <summary>
    /// 衝突時ロック用
    /// </summary>
    public static object objColLock = new object();

    /// <summary>
    /// 衝突オブジェクト
    /// </summary>
    public static Dictionary<MeshRenderer, ChangeMaterial> dctMaterial = new Dictionary<MeshRenderer, ChangeMaterial>();

    /// <summary>
    /// ワークID
    /// </summary>
    public static int _workId = 0;

    /// <summary>
    /// ワークID
    /// </summary>
    public static int workId
    {
        get
        {
            return ++_workId;
        }
    }

    /// <summary>
    /// デバッグ出力用カウンタ
    /// </summary>
    private static int debugCount = 0;

    /// <summary>
    /// デバッグ出力用カウンタ最大値
    /// </summary>
    private static int debugCountMax = 10;

    /// <summary>
    /// 辞書をすべて削除
    /// </summary>
    public static void ClearDictionary()
    {
        micksMechs = new Dictionary<string, List<MechInfo>>();
        tagDatas = new Dictionary<string, Dictionary<string, Dictionary<string, TagInfo>>>();
        postgreses = new Dictionary<string, ComPostgres>();
        mongos = new Dictionary<string, ComMongo>();
        mqtts = new Dictionary<string, ComMqtt>();
        inners = new Dictionary<string, ComInner>();
        opcuas = new Dictionary<string, ComOpcUA>();
        regObjects = new Dictionary<string, List<GameObject>>();
        regScripts = new Dictionary<string, List<GameObject>>();
    }

    /// <summary>
    /// API応答を32ビットデータに変換
    /// </summary>
    /// <param name="response"></param>
    /// <returns></returns>
    public static List<int> ApiGetValue32(string response)
    {
        var datas = new List<int>();
        var count = response.Length / 8;
        for (var i = 0; i < count; i++)
        {
            var strValue = response.Substring(i * 8, 8);
            int value = Convert.ToInt32(strValue, 16);
            datas.Add(value);
        }
        return datas;
    }

    /// <summary>
    /// 現在座標取得
    /// </summary>
    /// <param name="no"></param>
    /// <returns></returns>
    public static MechPos GetMicksMechPos(string ip, int no)
    {
        no--;
        if (!micksMechs.ContainsKey(ip))
        {
            return null;
        }
        if ((no < 0) || (no >= micksMechs[ip].Count))
        {
            return null;
        }

        return micksMechs[ip][no].pos;
    }

    /// <summary>
    /// タグ情報取得
    /// </summary>
    /// <param name="name"></param>
    /// <param name="mechid"></param>
    /// <param name="tag"></param>
    /// <returns></returns>
    public static TagInfo GetTagInfo(string name, string mechid, string tag)
    {
        if (tagDatas.ContainsKey(name) && tagDatas[name].ContainsKey(mechid) && tagDatas[name][mechid].ContainsKey(tag))
        {
            return tagDatas[name][mechid][tag];
        }
        return null;
    }

    /// <summary>
    /// タグ情報取得
    /// </summary>
    /// <param name="tag"></param>
    /// <returns></returns>
    public static int GetTagData(TagInfo tag)
    {
        if (tag == null)
        {
            return 0;
        }
        return GetTagData(tag.Database, tag.MechId, tag.Tag);
    }

    /// <summary>
    /// タグ情報取得
    /// </summary>
    /// <param name="name"></param>
    /// <param name="tag"></param>
    /// <returns></returns>
    public static int GetTagData(string name, string mechid, string tag)
    {
        if ((name != null) && (mechid != null) && (tag != null))
        {
            if (tagDatas.ContainsKey(name) && tagDatas[name].ContainsKey(mechid) && tagDatas[name][mechid].ContainsKey(tag))
            {
                return tagDatas[name][mechid][tag].Value;
            }
        }
        return 0;
    }

    /// <summary>
    /// タグ情報取得
    /// </summary>
    /// <param name="name"></param>
    /// <param name="tag"></param>
    /// <returns></returns>
    public static float GetTagFData(string name, string mechid, string tag)
    {
        if ((name != null) && (mechid != null) && (tag != null))
        {
            if (tagDatas.ContainsKey(name) && tagDatas[name].ContainsKey(mechid) && tagDatas[name][mechid].ContainsKey(tag))
            {
                return tagDatas[name][mechid][tag].fValue;
            }
        }
        return 0;
    }

    /// <summary>
    /// タグに値をセット
    /// </summary>
    /// <param name="tag"></param>
    /// <param name="value"></param>
    public static void SetTagData(TagInfo tag, int value)
    {
        if (tag != null)
        {
            tag.Value = value;
            SetTagDatas(new List<TagInfo> { tag });
        }
    }

    /// <summary>
    /// タグに値をセット
    /// </summary>
    /// <param name="tag"></param>
    /// <param name="value"></param>
    public static void SetTagDatas(List<TagInfo> tags)
    {
        var dctTagInfo = new Dictionary<string, List<TagInfo>>();
        foreach (var tag in tags)
        {
            if (tag.Database != null)
            {
                if (!dctTagInfo.ContainsKey(tag.Database))
                {
                    dctTagInfo.Add(tag.Database, new List<TagInfo>());
                }
                dctTagInfo[tag.Database].Add(tag);
            }
        }
        foreach (var tag in dctTagInfo)
        {
            if (postgreses.ContainsKey(tag.Key))
            {
                postgreses[tag.Key].SetDatas(tag.Value);
            }
            else if (mongos.ContainsKey(tag.Key))
            {
                mongos[tag.Key].SetDatas(tag.Value);
            }
            else if (opcuas.ContainsKey(tag.Key))
            {
                opcuas[tag.Key].SetDatas(tag.Value);
            }
            else if (mqtts.ContainsKey(tag.Key))
            {
                mqtts[tag.Key].SetDatas(tag.Value);
            }
            else if (inners.ContainsKey(tag.Key))
            {
                inners[tag.Key].SetDatas(tag.Value);
            }
        }
    }

    /// <summary>
    /// タグを使用しているオブジェクト取得
    /// </summary>
    /// <param name="tag"></param>
    /// <returns></returns>
    public static List<GameObject> GetUseTagObjects(TagInfo tag)
    {
        var ret = new List<GameObject>();
        foreach (var mech in regObjects)
        {
            foreach (var obj in mech.Value)
            {
                var scripts = obj.GetComponentsInChildren<UseTagBaseScript>();
                foreach (var script in scripts)
                {
                    if (script.GetUseTags().Find(d => (d != null) && (d.MechId == tag.MechId) && (d.Tag == tag.Tag)) != null)
                    {
                        ret.Add(obj);
                    }
                }
            }
        }
        return ret;
    }

    /// <summary>
    /// DB情報更新
    /// </summary>
    /// <param name="dbs"></param>
    public static void RenewDatabase(List<ComPostgres> dbs)
    {
        lock (objLock)
        {
            var keys = new List<string>();
            foreach (var key in tagDatas.Keys)
            {
                if (!tagDatas[key].ContainsKey("OpcUA"))
                {
                    keys.Add(key);
                }
            }
            foreach (var postgres in dbs)
            {
                postgres.RenewData();
                keys.Remove(postgres.Name);
            }
            foreach (var key in keys)
            {
//                tagDatas.Remove(key);
            }
        }
    }

    /// <summary>
    /// DB情報更新
    /// </summary>
    /// <param name="dbs"></param>
    public static void RenewDatabase(List<ComMongo> dbs)
    {
        lock (objLock)
        {
            var keys = new List<string>();
            foreach (var key in tagDatas.Keys)
            {
                if (!tagDatas[key].ContainsKey("OpcUA"))
                {
                    keys.Add(key);
                }
            }
            foreach (var mongo in dbs)
            {
                mongo.RenewData();
                keys.Remove(mongo.Name);
            }
            foreach (var key in keys)
            {
//                tagDatas.Remove(key);
            }
        }
    }

    /// <summary>
    /// DB情報更新
    /// </summary>
    /// <param name="dbs"></param>
    public static void RenewDatabase(List<ComOpcUA> dbs)
    {
        lock (objLock)
        {
            var keys = new List<string>();
            foreach (var key in tagDatas.Keys)
            {
                if (tagDatas[key].ContainsKey("OpcUA"))
                {
                    keys.Add(key);
                }
            }
            foreach (var opcua in dbs)
            {
                opcua.RenewData();
                keys.Remove(opcua.Name);
            }
            foreach (var key in keys)
            {
//                tagDatas.Remove(key);
            }
        }
    }

    /// <summary>
    /// JSONを読み込み
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="name"></param>
    /// <returns></returns>
    public static object LoadJson<T>(string name)
    {
        lock (objLock)
        {
            string fullPath = Path.Combine(Application.streamingAssetsPath, $"Datas/{name}.json");
            StreamReader reader = new StreamReader(fullPath);
            string datastr = reader.ReadToEnd();
            reader.Close();
            return JsonUtility.FromJson<T>(datastr);
        }
    }

    /// <summary>
    /// JSONを読み込み
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="name"></param>
    /// <returns></returns>
    public static async Task<object> LoadListJson<T>(string name)
    {
        string datastr = await LoadJsonFromStreamingAssetsAsync($"Datas/{name}.json");
        return JsonSerializer.Deserialize<T>(datastr);
    }

    /// <summary>
    /// JSONを読み込む
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
    public static async Task<string> LoadJsonFromStreamingAssetsAsync(string fileName)
    {
        string path = Path.Combine(Application.streamingAssetsPath, fileName); ;
        string datastr = "";
        if ((Application.platform == RuntimePlatform.Android) || (Application.platform == RuntimePlatform.IPhonePlayer))
        {
            using (UnityWebRequest request = UnityWebRequest.Get(path))
            {
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }
                Debug.Log($"***** " + request.result + " *****");
                if (request.result == UnityWebRequest.Result.Success)
                {
                    datastr = request.downloadHandler.text;
                }
            }
        }
        else
        {
            lock (objLock)
            {
                StreamReader reader = new StreamReader(path);
                datastr = reader.ReadToEnd();
                reader.Close();
            }
        }
        return datastr;
    }

    /// <summary>
    /// インスタンスIDからオブジェクトを取得する
    /// </summary>
    /// <param name="instanceId"></param>
    /// <returns></returns>
    public static UnityEngine.Object FindObjectFromInstanceID(int instanceId)
    {
        try
        {
            var type = typeof(UnityEngine.Object);
            var flags = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.InvokeMethod;
            var ret = type.InvokeMember("FindObjectFromInstanceID", flags, null, null, new object[] { instanceId });
            return (UnityEngine.Object)ret;
        }
        catch (Exception e)
        {
            Debug.LogError(e.Message);
        }
        return null;
    }

    /// <summary>
    /// パス文字列を取得する
    /// </summary>
    /// <param name="transform"></param>
    /// <returns></returns>
    public static string GetPathString(Transform transform)
    {
        var name = transform.name;
        var obj = transform;
        while (obj.transform.parent != null)
        {
            obj = obj.parent;
            name = obj.name + "/" + name;
        }
        return name;
    }

    /// <summary>
    /// モデル作成
    /// </summary>
    /// <returns></returns>
    public static List<GameObject> CreateInitialModel()
    {
        return Resources.LoadAll<GameObject>("Pixyz").ToList();
    }

    /// <summary>
    /// スイッチモデル生成
    /// </summary>
    /// <returns></returns>
    public static List<GameObject> CreateSwitchModel()
    {
        return Resources.LoadAll<GameObject>("Prefabs/Device").ToList().FindAll(d => d.name.Contains("Switch"));
    }

    /// <summary>
    /// シグナルタワーモデル生成
    /// </summary>
    /// <returns></returns>
    public static List<GameObject> CreateSignalTowerModel()
    {
        return Resources.LoadAll<GameObject>("Prefabs/Device").ToList().FindAll(d => d.name.Contains("SignalTower"));
    }

    /// <summary>
    /// ワーク作成
    /// </summary>
    /// <param name="game"></param>
    /// <param name="name"></param>
    /// <returns></returns>
    public static GameObject CreateWork(GameObject game, string name)
    {
        var work = game != null ? game : (works.ContainsKey(name) ? works[name] : (GameObject)Resources.Load("3DModel/Works/" + name));
        if (work == null)
        {
            work = (GameObject)Resources.Load("3DModel/Works/Cube");
        }
        var rigi = work.GetComponentInChildren<Rigidbody>();
        if (rigi == null)
        {
            rigi = work.AddComponent<Rigidbody>();
        }
        var script = work.GetComponentInChildren<CollisionScript>();
        if (script == null)
        {
            script = work.AddComponent<CollisionScript>();
        }
        var mesh = work.GetComponentInChildren<MeshRenderer>();
        if (mesh != null)
        {
            var col = mesh.GetComponentInChildren<BoxCollider>();
            if (col == null)
            {
                col = mesh.AddComponent<BoxCollider>();
            }
        }
        return work;
    }

    /// <summary>
    /// センサー作成
    /// </summary>
    /// <param name="sensor"></param>
    /// <returns></returns>
    public static GameObject CreateSensor(GameObject parent, SensorSetting sensor, string name)
    {
        var tmp = (GameObject)Resources.Load("3DModel/Sensor/" + name);
        if (tmp == null)
        {
            tmp = (GameObject)Resources.Load("3DModel/Sensor/CvSensor");
        }
        var s = UnityEngine.Object.Instantiate(tmp, new Vector3(), Quaternion.identity);
        s.transform.parent = parent.transform;
        s.transform.localPosition = new Vector3();
        s.transform.localEulerAngles = new Vector3();
        var m = s.GetComponentInChildren<MeshFilter>();
        if (m != null)
        {
            m.transform.localScale = new Vector3
            {
                x = sensor.width,
                y = 0.005f,
                z = 0.005f
            };
        }
        return s;
    }

    /// <summary>
    /// デバッグ出力
    /// </summary>
    public static void DebugOut()
    {
        // デバッグ設定
        if (Keyboard.current.dKey.wasPressedThisFrame)
        {
            isDebug = !isDebug;
        }

        // デバッグ出力
        if (isDebug)
        {
            if (debugCount == 0)
            {
                foreach (var postgres in postgreses)
                {
                    Debug.Log(postgres.Key + " : " + postgres.Value.nowCycle + "msec");
                }
                foreach (var mqtt in mqtts)
                {
                    Debug.Log(mqtt.Key + " : " + mqtt.Value.nowCycle + "msec");
                }
            }
            debugCount = (debugCount + 1) % debugCountMax;
        }
    }
}
