using MQTTnet.Server;
using Parameters;
using StackExchange.Redis;
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
using UnityEngine.AddressableAssets.Initialization;
using UnityEngine.AddressableAssets;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;
using System.Collections;
using System.Xml;
using MongoDB.Driver.Linq;

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

    [Serializable]
    public class CbTagInfo : TagInfo
    {
        /// <summary>
        /// ストップウォッチ
        /// </summary>
        public System.Diagnostics.Stopwatch stopwatch = new();
        /// <summary>
        /// 時間
        /// </summary>
        public List<long> laps;
        public void SetLaps(long laps)
        {
            if (this.laps.IsUnityNull())
            {
                this.laps = new();
            }
            this.laps.Add(laps);
            if (this.laps.Count > 100)
            {
                this.laps.RemoveAt(0);
            }
        }
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
        public CbTagInfo input;
        /// <summary>
        /// 折り返し用出力
        /// </summary>
        public CbTagInfo output;
        /// <summary>
        /// カウンタ用入力
        /// </summary>
        public CbTagInfo cntIn;
        /// <summary>
        /// カウンタ用出力
        /// </summary>
        public CbTagInfo cntOut;
        /// <summary>
        /// サイクルタグ
        /// </summary>
        public TagInfo cycle;
    }
    
    /// <summary>
    /// 断面表示情報
    /// </summary>
    public class ClipInfo
    {
        public enum SlideMode : int
        {
            None = 0,
            X = 1,
            Y = 2,
            Z = 3
        }

        /// <summary>
        /// 断面表示機能
        /// </summary>
        public bool isOn;
        /// <summary>
        /// 描画エリア
        /// </summary>
        public Bounds bounds;
        /// <summary>
        /// モード 0:なし 1:X, 2:Y, 3:Z
        /// </summary>
        public SlideMode mode;
        /// <summary>
        /// 反転
        /// </summary>
        public bool isRvs;
        /// <summary>
        /// X値
        /// </summary>
        public float x;
        /// <summary>
        /// Y値
        /// </summary>
        public float y;
        /// <summary>
        /// Z値
        /// </summary>
        public float z;
        /// <summary>
        /// 値
        /// </summary>
        public float value;
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
    /// ロード済みユニット設定（ParameterLoader が設定）。ユニット名→DB/機番 の解決に使用。
    /// </summary>
    public static List<UnitSetting> unitSettings = new List<UnitSetting>();

    /// <summary>
    /// ユニット名から (database, mechId) を解決する。ROS2 連携等の可搬性用。
    /// ※ここで使う名前は「内部の完全一致」だけ（ROS メッセージには載せない）なので、日本語ユニット名でも安全。
    /// </summary>
    public static bool TryResolveUnitDb(string unitName, out string database, out string mechId)
    {
        database = "";
        mechId = "";
        if (string.IsNullOrEmpty(unitName) || unitSettings == null)
        {
            return false;
        }
        var u = unitSettings.Find(d => d != null && d.name == unitName);
        if (u == null)
        {
            return false;
        }
        database = u.Database;
        mechId = u.mechId;
        return true;
    }

    /// <summary>
    /// Postgres
    /// </summary>
    // ↓ 各通信コンポーネントの登録先。Com 具象型ではなく ITagCom で保持し、
    //   GlobalScript(Common) → Com の依存（循環）を切っている。
    public static Dictionary<string, ITagCom> postgreses = new Dictionary<string, ITagCom>();

    /// <summary>
    /// MongoDB
    /// </summary>
    public static Dictionary<string, ITagCom> mongos = new Dictionary<string, ITagCom>();

    /// <summary>
    /// OPC UA
    /// </summary>
    public static Dictionary<string, ITagCom> opcuaapis = new Dictionary<string, ITagCom>();

    /// <summary>
    /// MQTT
    /// </summary>
    public static Dictionary<string, ITagCom> mqtts = new Dictionary<string, ITagCom>();

    /// <summary>
    /// Redis
    /// </summary>
    public static Dictionary<string, ITagCom> redises = new Dictionary<string, ITagCom>();

    /// <summary>
    /// 内部通信
    /// </summary>
    public static Dictionary<string, ITagCom> inners = new Dictionary<string, ITagCom>();

    /// <summary>
    /// MCプロトコル
    /// </summary>
    public static Dictionary<string, ITagCom> mcprotocols = new Dictionary<string, ITagCom>();

    /// <summary>
    /// MICKS通信
    /// </summary>
    public static Dictionary<string, ITagCom> mickses = new Dictionary<string, ITagCom>();

    /// <summary>
    /// OPC UA通信
    /// </summary>
    public static Dictionary<string, ITagCom> opcuas = new Dictionary<string, ITagCom>();

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
    /// ビルド設定
    /// </summary>
    public static BuildConfig buildConfig = new();

    /// <summary>
    /// ロード中フラグ
    /// </summary>
    public static bool isLoading = false;

    /// <summary>
    /// ロード完了
    /// </summary>
    public static bool isLoaded = false;

    /// <summary>
    /// ROS2 連携が有効か（ParameterLoader が判定して設定）。
    /// 実機(OPC UA/Postgres)とROSで関節値の規約が異なる場合の場合分けに使う。
    /// 例: CRX-30iA の3軸目は実機=連成値(J2+J3)だが ROS=純粋な関節角なので arm3 の式を切替える。
    /// </summary>
    public static bool useRos2 = false;

    /// <summary>
    /// ロード進捗(0..1)。ローディング画面(KmxLoadingScreen)が参照。
    /// </summary>
    public static float loadProgress = 0f;

    /// <summary>
    /// ロード中コメント（"Loading Unit : ..." 等）。ローディング画面が参照。
    /// </summary>
    public static string loadLabel = "";

    /// <summary>
    /// イベントロード要求
    /// </summary>
    public static bool isReqLoadEvent = false;

    /// <summary>
    /// 衝突表示モード
    /// </summary>
    public static bool isCollision = false;

    /// <summary>
    /// ライン表示
    /// </summary>
    public static bool isLiens = true;

    /// <summary>
    /// システムレコーダ表示中
    /// </summary>
    public static bool isSystemRecorder = false;

    /// <summary>
    /// システムレコーダ時間
    /// </summary>
    public static long sysRecMilliseconds = 0;
    /// <summary>
    /// XR表示モード
    /// </summary>
    public static bool isXRMode
    {
        get => PlayerPrefs.GetInt("isXRMode", 1) == 1;
        set => PlayerPrefs.SetInt("isXRMode", value ? 1 : 0);
    }

    /// <summary>
    /// VR用プレハブモード
    /// </summary>
    public static bool isXRPrefab
    {
        get => PlayerPrefs.GetInt("isXRPrefab", 1) == 1;
        set => PlayerPrefs.SetInt("isXRPrefab", value ? 1 : 0);
    }

    /// <summary>
    /// 断面表示情報
    /// </summary>
    public static ClipInfo clipInfo = new();

    /// <summary>
    /// デバッグ出力用モード
    /// </summary>
    public static bool isDebug = false;

    /// <summary>
    /// コールバック用タグデータ
    /// </summary>
    public static List<CallbackTag> callbackTags { get; set; } = new List<CallbackTag>();

    /// <summary>
    /// 動作テーブル情報
    /// </summary>
    public static List<ActionTableData> actionTableDatas { get; set; } = new List<ActionTableData>();

    /// <summary>
    /// 使用デバイスリスト
    /// </summary>
    public static List<UseDeviceData> useDeviceDatas { get; set; } = new List<UseDeviceData>();

    /// <summary>
    /// hmx-link（デジタルツイン）設定。HmxLink.json から読み込む（無ければ既定=無効）。
    /// </summary>
    public static HmxLinkSetting hmxLink = new HmxLinkSetting();

    /// <summary>WebGL 専用設定（WebGlSetting.json）。無ければ既定値。</summary>
    public static WebGlSetting webGlSetting = new WebGlSetting();

    /// <summary>
    /// 手動操作(JOG)定義（ManualOpInfo.json）。mechId+name で引く。
    /// </summary>
    public static List<ManualOpData> manualOps = new List<ManualOpData>();

    /// <summary>指定ユニットの手動操作定義を取得（無ければ null）</summary>
    public static ManualOpData GetManualOp(string mechId, string name)
    {
        if (manualOps == null)
        {
            return null;
        }
        foreach (var m in manualOps)
        {
            if (m != null && m.mechId == mechId && m.name == name)
            {
                return m;
            }
        }
        return null;
    }

    /// <summary>タッチtap中だけtrue。MainProcessがCtrl無しでもユニット選択するための一時フラグ（タッチ操作機能用）。</summary>
    public static bool touchSelectOverride = false;

    /// <summary>
    /// タイムチャーチデータリスト
    /// </summary>
    public static List<TimeChartData> timeChartDatas { get; set; } = new List<TimeChartData>();

    /// <summary>
    /// タイムチャート操作中
    /// </summary>
    public static bool IsInTimeChart;

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
    /// 選択オブジェクト
    /// </summary>
    public static GameObject selectedObject;

    /// <summary>
    /// 左オブジェクト
    /// </summary>
    public static GameObject rayLObject;

    /// <summary>
    /// 右オブジェクト
    /// </summary>
    public static GameObject rayRObject;

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

    /*
    /// <summary>
    /// デバッグ出力用カウンタ
    /// </summary>
    private static int debugCount = 0;

    /// <summary>
    /// デバッグ出力用カウンタ最大値
    /// </summary>
    private static int debugCountMax = 10;
    */

    /// <summary>
    /// 内部時間
    /// </summary>
    public static long innerCycle = 0;

    // static変数を初期化する処理  
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Clear()
    {
        ClearDictionary();

        objLock = new object();
        buildConfig = new();
        isLoading = false;
        isLoaded = false;
        isReqLoadEvent = false;
        isCollision = false;
        clipInfo = new();
        isDebug = false;
        callbackTags = new List<CallbackTag>();
        actionTableDatas = new List<ActionTableData>();
        objColLock = new object();
        _workId = 0;
    }

    /// <summary>
    /// 辞書をすべて削除
    /// </summary>
    public static void ClearDictionary()
    {
        micksMechs = new Dictionary<string, List<MechInfo>>();
        tagDatas = new Dictionary<string, Dictionary<string, Dictionary<string, TagInfo>>>();
        postgreses = new Dictionary<string, ITagCom>();
        mongos = new Dictionary<string, ITagCom>();
        opcuaapis = new Dictionary<string, ITagCom>();
        mqtts = new Dictionary<string, ITagCom>();
        redises = new Dictionary<string, ITagCom>();
        inners = new Dictionary<string, ITagCom>();
        mcprotocols = new Dictionary<string, ITagCom>();
        mickses = new Dictionary<string, ITagCom>();
        opcuas = new Dictionary<string, ITagCom>();
        works = new Dictionary<string, GameObject>();
        regObjects = new Dictionary<string, List<GameObject>>();
        regScripts = new Dictionary<string, List<GameObject>>();
        dctMaterial = new Dictionary<MeshRenderer, ChangeMaterial>();
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
    /// <param name="database"></param>
    /// <param name="mechid"></param>
    /// <param name="tag"></param>
    /// <returns></returns>
    public static TagInfo GetTagInfo(string database, string mechid, string tag)
    {
        try
        {
            if (tagDatas.ContainsKey(database) && tagDatas[database].ContainsKey(mechid) && tagDatas[database][mechid].ContainsKey(tag))
            {
                var t = tagDatas[database][mechid][tag];
                t.wasRead = true;   // デジタルツイン: 読まれたタグを記録（購読対象の絞り込み）
                return t;
            }
        }
        catch
        {
        }
        return null;
    }

    /// <summary>
    /// デバイス名からタグ情報を取得
    /// </summary>
    /// <param name="database"></param>
    /// <param name="mechid"></param>
    /// <param name="dev"></param>
    /// <returns></returns>
    public static TagInfo GetTagInfoFromDev(string database, string mechid, string dev)
    {
        try
        {
            if (tagDatas.ContainsKey(database) && tagDatas[database].ContainsKey(mechid))
            {
                return tagDatas[database][mechid].Values.Where(d => d.Device == dev).First();
            }
        }
        catch
        {
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
                var t = tagDatas[name][mechid][tag];
                t.wasRead = true;   // デジタルツイン: 読まれたタグを記録（購読対象の絞り込み）
                return t.Value;
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
            else if (opcuaapis.ContainsKey(tag.Key))
            {
                opcuaapis[tag.Key].SetDatas(tag.Value);
            }
            else if (mqtts.ContainsKey(tag.Key))
            {
                mqtts[tag.Key].SetDatas(tag.Value);
            }
            else if (redises.ContainsKey(tag.Key))
            {
                redises[tag.Key].SetDatas(tag.Value);
            }
            else if (inners.ContainsKey(tag.Key))
            {
                inners[tag.Key].SetDatas(tag.Value);
            }
            else if (mcprotocols.ContainsKey(tag.Key))
            {
                mcprotocols[tag.Key].SetDatas(tag.Value);
            }
            else if (mickses.ContainsKey(tag.Key))
            {
                mickses[tag.Key].SetDatas(tag.Value);
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
    /// DB情報更新（Com 具象型に依存しないよう ITagCom で受ける）
    /// </summary>
    /// <param name="dbs">対象の通信コンポーネント</param>
    /// <param name="isOpcUa">OPC UA(タグキーに "OpcUA" を含むもの) を対象にするか</param>
    public static void RenewDatabase(IEnumerable<ITagCom> dbs, bool isOpcUa = false)
    {
        lock (objLock)
        {
            var keys = new List<string>();
            foreach (var key in tagDatas.Keys)
            {
                if (tagDatas[key].ContainsKey("OpcUA") == isOpcUa)
                {
                    keys.Add(key);
                }
            }
            foreach (var db in dbs)
            {
                db.RenewData();
                keys.Remove(db.Name);
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
        // WebGL も StreamingAssets はHTTP配信のため UnityWebRequest が必須（ファイルAPI不可）
        if ((Application.platform == RuntimePlatform.Android) || (Application.platform == RuntimePlatform.IPhonePlayer) || (Application.platform == RuntimePlatform.WebGLPlayer))
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
    public static List<GameObject> LoadSwitchModel()
    {
        return LoadPrefabObject("Prefabs/Device", "Switch", true);
    }

    /// <summary>
    /// シグナルタワーモデル生成
    /// </summary>
    /// <returns></returns>
    public static List<GameObject> LoadSignalTowerModel()
    {
        return LoadPrefabObject("Prefabs/Device", "SignalTower", true);
    }

    /// <summary>
    /// プレハブをロードする
    /// </summary>
    /// <param name="path"></param>
    /// <param name="name"></param>
    /// <returns></returns>
    public static List<GameObject> LoadPrefabObject(string path, string name, bool contains = false)
    {
        return Resources.LoadAll<GameObject>(path).ToList().FindAll(d => !contains ? d.name == name : d.name.Contains(name));
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
        var script = work.GetComponentInChildren<WorkCollisionScript>();
        if (script == null)
        {
            script = work.AddComponent<WorkCollisionScript>();
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
    /// 衝突検知用コライダー作成
    /// </summary>
    /// <returns></returns>
    public static bool CreateCollider(GameObject model)
    {
        var meshColliderBuilder = model.AddComponent<SAMeshColliderBuilder>();
        meshColliderBuilder.reducerProperty.shapeType = SAColliderBuilderCommon.ShapeType.Mesh;
        meshColliderBuilder.reducerProperty.meshType = SAColliderBuilderCommon.MeshType.Raw;
        meshColliderBuilder.splitProperty.splitPrimitiveEnabled = false;
        //                    meshColliderBuilder.splitProperty.splitPolygonNormalEnabled = false;
        //                    meshColliderBuilder.splitProperty.splitPolygonNormalAngle = 60;
        //                    meshColliderBuilder.reducerProperty.meshType = SAColliderBuilderCommon.MeshType.ConvexHull;
        meshColliderBuilder.rigidbodyProperty.isCreate = false;
        meshColliderBuilder.colliderProperty.convex = false;
        meshColliderBuilder.colliderProperty.isTrigger = false;
        KssMeshColliderBuilderInspector.Process(meshColliderBuilder);
        var suctions = model.GetComponentsInChildren<SuctionScript>().ToList();
        foreach (var col in model.GetComponentsInChildren<MeshCollider>())
        {
            try
            {
                if ((col == null) || (col.sharedMesh == null) || (suctions.Find(d => col.transform.IsChildOf(d.transform)) != null))
                {
                    // 吸引は無視
                    continue;
                }
                AddFakeThickness(col.sharedMesh);
                var verts = col.sharedMesh.vertices;
                float minZ = verts.Min(v => v.z);
                float maxZ = verts.Max(v => v.z);
                float thickness = Mathf.Abs(maxZ - minZ);
                int triangleCount = col.sharedMesh.triangles.Length / 3;
                var message = "";
                if (IsMesh3D(col.sharedMesh, ref message) && (triangleCount <= 255))
                {
                    try
                    {
                        col.convex = true;
                        col.isTrigger = true;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"Convex設定に失敗: {col.name}, 理由: {ex.Message}");
                        col.convex = false;
                        col.isTrigger = false;
                    }
                }
                else
                {
                    GameObject.Destroy(col);
                    //                                Debug.Log($"convexスキップ: {col.name}, triangle: {triangleCount}, thickness: {thickness}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Convex設定に失敗: {col.name}, 理由: {ex.Message}");
                col.convex = false;
                col.isTrigger = false;
            }
        }
        return true;
    }

    /// <summary>
    /// メッシュが3Dかチェック
    /// </summary>
    /// <param name="mesh"></param>
    /// <returns></returns>
    private static bool IsMesh3D(UnityEngine.Mesh mesh, ref string message)
    {
        if (mesh == null || mesh.vertexCount < 4) return false;

        var verts = mesh.vertices;
        var min = verts[0];
        var max = verts[0];

        foreach (var v in verts)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }

        float thicknessZ = Mathf.Abs(max.z - min.z);
        float thicknessY = Mathf.Abs(max.y - min.y);
        float thicknessX = Mathf.Abs(max.x - min.x);

        message = (thicknessX * thicknessX * 1000000 + thicknessY * thicknessY * 1000000 + thicknessZ * thicknessZ * 1000000).ToString();

        // 最小でも3方向にある程度の広がりがないと凸包は失敗する可能性
        return (thicknessX > 1e-4f && thicknessY > 1e-4f && thicknessZ > 1e-4f);
    }

    /// <summary>
    /// 厚みを加える
    /// </summary>
    /// <param name="offset"></param>
    /// <param name="mesh"></param>
    private static void AddFakeThickness(UnityEngine.Mesh mesh, float offset = 0.0001f)
    {
        Vector3[] verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++)
        {
            verts[i].z += UnityEngine.Random.Range(-offset, offset); // Z方向に厚み
        }
        mesh.vertices = verts;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
    }
}
