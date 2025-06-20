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
    /// �@�\�ʒu
    /// </summary>
    public class MechPos
    {
        public int x;
        public int y;
        public int z;
    }

    /// <summary>
    /// �@�\���
    /// </summary>
    public class MechInfo
    {
        public int no;
        public int mechType;
        public MechPos pos;
    }

    /// <summary>
    /// �ύX���}�e���A��
    /// </summary>
    public class ChangeMaterial
    {
        /// <summary>
        /// �ύX��
        /// </summary>
        public bool isChange;
        /// <summary>
        /// ���̃}�e���A��
        /// </summary>
        public Material material;
    }

    /// <summary>
    /// �R�[���o�b�N�^�O�f�[�^
    /// </summary>
    public class CallbackTag
    {
        public string database { get; set; }
        /// <summary>
        /// �܂�Ԃ��p����
        /// </summary>
        public TagInfo input;
        /// <summary>
        /// �܂�Ԃ��p�o��
        /// </summary>
        public TagInfo output;
        /// <summary>
        /// �J�E���^�p����
        /// </summary>
        public TagInfo cntIn;
        /// <summary>
        /// �J�E���^�p�o��
        /// </summary>
        public TagInfo cntOut;
        /// <summary>
        /// ���݂̒l
        /// </summary>
        public bool value;
        /// <summary>
        /// ���݂̒l
        /// </summary>
        public int count;
        /// <summary>
        /// �X�g�b�v�E�H�b�`
        /// </summary>
        public System.Diagnostics.Stopwatch stopwatch;
        /// <summary>
        /// ����
        /// </summary>
        public long laps { get; set; }
    }

    /// <summary>
    /// MICKS���@�\�̈ʒu���
    /// </summary>
    public static Dictionary<string, List<MechInfo>> micksMechs = new Dictionary<string, List<MechInfo>>();

    /// <summary>
    /// �^�O�f�[�^
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
    /// �����ʐM
    /// </summary>
    public static Dictionary<string, ComInner> inners = new Dictionary<string, ComInner>();

    /// <summary>
    /// ���[�N
    /// </summary>
    public static Dictionary<string, GameObject> works = new Dictionary<string, GameObject>();

    /// <summary>
    /// �@�\�o�^���
    /// </summary>
    public static Dictionary<string, List<GameObject>> regObjects = new Dictionary<string, List<GameObject>>();

    /// <summary>
    /// �X�N���v�g�o�^���
    /// </summary>
    public static Dictionary<string, List<GameObject>> regScripts = new Dictionary<string, List<GameObject>>();

    /// <summary>
    /// ���b�N�p�I�u�W�F�N�g
    /// </summary>
    public static object objLock = new object();

    /// <summary>
    /// ���[�h���t���O
    /// </summary>
    public static bool isLoading = false;

    /// <summary>
    /// �Փ˕\�����[�h
    /// </summary>
    public static bool isCollision = false;

    /// <summary>
    /// �f�o�b�O�o�͗p���[�h
    /// </summary>
    public static bool isDebug = false;

    /// <summary>
    /// �R�[���o�b�N�p�^�O�f�[�^
    /// </summary>
    public static List<CallbackTag> callbackTags { get; set; } = new List<CallbackTag>();

    /// <summary>
    /// �Փˎ����b�N�p
    /// </summary>
    public static object objColLock = new object();

    /// <summary>
    /// �Փ˃I�u�W�F�N�g
    /// </summary>
    public static Dictionary<MeshRenderer, ChangeMaterial> dctMaterial = new Dictionary<MeshRenderer, ChangeMaterial>();

    /// <summary>
    /// ���[�NID
    /// </summary>
    public static int _workId = 0;

    /// <summary>
    /// ���[�NID
    /// </summary>
    public static int workId
    {
        get
        {
            return ++_workId;
        }
    }

    /// <summary>
    /// �f�o�b�O�o�͗p�J�E���^
    /// </summary>
    private static int debugCount = 0;

    /// <summary>
    /// �f�o�b�O�o�͗p�J�E���^�ő�l
    /// </summary>
    private static int debugCountMax = 10;

    /// <summary>
    /// ���������ׂč폜
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
    /// API������32�r�b�g�f�[�^�ɕϊ�
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
    /// ���ݍ��W�擾
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
    /// �^�O���擾
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
    /// �^�O���擾
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
    /// �^�O���擾
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
    /// �^�O���擾
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
    /// �^�O�ɒl���Z�b�g
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
    /// �^�O�ɒl���Z�b�g
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
    /// �^�O���g�p���Ă���I�u�W�F�N�g�擾
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
    /// DB���X�V
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
    /// DB���X�V
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
    /// DB���X�V
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
    /// JSON��ǂݍ���
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
    /// JSON��ǂݍ���
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
    /// JSON��ǂݍ���
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
    /// �C���X�^���XID����I�u�W�F�N�g���擾����
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
    /// �p�X��������擾����
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
    /// ���f���쐬
    /// </summary>
    /// <returns></returns>
    public static List<GameObject> CreateInitialModel()
    {
        return Resources.LoadAll<GameObject>("Pixyz").ToList();
    }

    /// <summary>
    /// �X�C�b�`���f������
    /// </summary>
    /// <returns></returns>
    public static List<GameObject> CreateSwitchModel()
    {
        return Resources.LoadAll<GameObject>("Prefabs/Device").ToList().FindAll(d => d.name.Contains("Switch"));
    }

    /// <summary>
    /// �V�O�i���^���[���f������
    /// </summary>
    /// <returns></returns>
    public static List<GameObject> CreateSignalTowerModel()
    {
        return Resources.LoadAll<GameObject>("Prefabs/Device").ToList().FindAll(d => d.name.Contains("SignalTower"));
    }

    /// <summary>
    /// ���[�N�쐬
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
    /// �Z���T�[�쐬
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
    /// �f�o�b�O�o��
    /// </summary>
    public static void DebugOut()
    {
        // �f�o�b�O�ݒ�
        if (Keyboard.current.dKey.wasPressedThisFrame)
        {
            isDebug = !isDebug;
        }

        // �f�o�b�O�o��
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
