using Parameters;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Unity.VisualScripting;
using UnityEngine;

public class KssBaseScript : BaseBehaviour
{
    [Serializable]
    public class DataExchange
    {
        public string InputTag;
        public string OutputTag;
        public int InitValue;
        public TagInfo Input;
        public TagInfo Output;
    }

    public class CanvasValue
    {
        public object value;
        public string unit = "";
        public string format = "";

        public string disp
        {
            get
            {
                var data = "";
                if (value.GetType() == typeof(string))
                {
                    data = ((string)value).ToString();
                }
                else if (value.GetType() == typeof(int))
                {
                    data = ((int)value).ToString(format);
                }
                else if (value.GetType() == typeof(long))
                {
                    data = ((long)value).ToString(format);
                }
                else if (value.GetType() == typeof(float))
                {
                    data = ((float)value).ToString(format);
                }
                else if (value.GetType() == typeof(double))
                {
                    data = ((double)value).ToString(format);
                }
                return data + unit;
            }
        }
    }

    /// <summary>
    /// マニュアル設定
    /// </summary>
    [SerializeField]
    protected bool isManual;

    /// <summary>
    /// キャンバス表示
    /// </summary>
    protected virtual bool isCanvas { get { return false; } }

    /// <summary>
    /// 情報キャンパススクリプト
    /// </summary>
    protected InfoCanvasScript canvasScript;

    /// <summary>
    /// ユニット設定
    /// </summary>
    [SerializeField]
    public UnitSetting unitSetting;

    /// <summary>
    /// 表示データ
    /// </summary>
    protected Dictionary<string, CanvasValue> dctDispValue = new Dictionary<string, CanvasValue>();

    /// <summary>
    /// ルートからの名前
    /// </summary>
    public string pathString
    {
        get
        {
            return GlobalScript.GetPathString(this.transform);
        }
    }

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Start()
    {
        base.Start();

        if (unitSetting != null)
        {
            var script = GetComponent<KssBaseScript>();
            if ((script != null) && script.isCanvas)
            {
                /*
                // キャンバス表示 
                var c = (GameObject)Resources.Load("Canvas/InfoCanvas");
                var canvas = Instantiate(c);
                canvas.transform.parent = transform;
                canvas.transform.localPosition = new Vector3 { x = 0, y = 0.1f, z = 0 };
                canvas.transform.localEulerAngles = new Vector3();
                canvasScript = canvas.transform.GetComponentInChildren<InfoCanvasScript>();
                canvasScript.SetUnitSetting(unitSetting);
                */
            }
        }
    }

    protected override void Update()
    {
        base.Update();

        // チャートスクリプト
        if (canvasScript != null)
        {
            RenewCanvasValues();
            canvasScript.SetValues(dctDispValue);
        }
    }

    public virtual void RenewCanvasValues()
    {
    }

    /// <summary>
    /// パラメータをセットする
    /// </summary>
    /// <param name="components"></param>
    /// <param name="scriptables"></param>
    /// <param name="kssInstanceIds"></param>
    /// <param name="root"></param>
    public virtual void SetParameter(List<Component> components, List<KssPartsBase> scriptables, List<KssInstanceIds> kssInstanceIds, JsonElement root)
    {
    }

    /// <summary>
    /// パラメータをセットする
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="robo"></param>
    public virtual void SetParameter(UnitSetting unitSetting, object obj)
    {
        this.unitSetting = unitSetting;
    }

    protected bool GetBooleanFromPrm(JsonElement root, string name)
    {
        try
        {
            return root.GetProperty(name).GetBoolean();

        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        return false;
    }

    protected string GetStringFromPrm(JsonElement root, string name)
    {
        try
        {
            return root.GetProperty(name).GetString();

        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        return "";
    }

    protected int GetInt32FromPrm(JsonElement root, string name)
    {
        try
        {
            return root.GetProperty(name).GetInt32();

        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        return 0;
    }

    protected float GetFloatFromPrm(JsonElement root, string name)
    {
        try
        {
            return (float)root.GetProperty(name).GetDouble();

        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        return 0;
    }

    protected Vector3 GetVector3FromPrm(JsonElement root, string name)
    {
        try
        {
            var vec = root.GetProperty(name);
            return new Vector3
            {
                x = (float)vec.GetProperty("x").GetDouble(),
                y = (float)vec.GetProperty("y").GetDouble(),
                z = (float)vec.GetProperty("z").GetDouble()
            };
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        return new Vector3();
    }

    protected GameObject GetGameObjectFromPrm(List<Component> components, List<KssInstanceIds> kssInstanceIds, JsonElement root, string name)
    {
        var id = GetInstanceID(root, name);
        if (id != 0)
        {
            var objname = kssInstanceIds.Find(d => d.id == id);
            if (objname != null)
            {
                var cmps = components.FindAll(d => d.name == objname.name);
                var component = cmps.Find(d => GlobalScript.GetPathString(d.gameObject.transform) == objname.path);
                return component == null ? null : component.gameObject;
            }
        }
        return null;
    }

    protected GameObject[] GetGameObjectsFromPrm(List<Component> components, List<KssInstanceIds> kssInstanceIds, JsonElement root, string name)
    {
        var ret = new List<GameObject>();
        var ids = GetInstanceIDs(root, name);
        foreach (var id in ids)
        {
            if (id != 0)
            {
                var objname = kssInstanceIds.Find(d => d.id == id);
                if (objname != null)
                {
                    var cmps = components.FindAll(d => d.name == objname.name);
                    var component = cmps.Find(d => GlobalScript.GetPathString(d.gameObject.transform) == objname.path);
                    ret.Add(component == null ? null : component.gameObject);
                }
            }
        }
        return ret.ToArray();
    }

    protected TagInfo GetTagInfoFromPrm(List<KssPartsBase> scriptables, List<KssInstanceIds> kssInstanceIds, JsonElement root, string name)
    {
        var id = GetInstanceID(root, name);
        if (id != 0)
        {
            var objname = kssInstanceIds.Find(d => d.id == id);
            if (objname != null)
            {
                return (TagInfo)scriptables.Find(d => d.pathString == objname.path);
            }
        }
        return null;
    }

    protected List<TagInfo> GetTagInfosFromPrm(List<Component> components, List<KssPartsBase> scriptables, List<KssInstanceIds> kssInstanceIds, JsonElement root, string name)
    {
        var ret = new List<TagInfo>();
        try
        {
            JsonElement obj = root.GetProperty(name);
            foreach (var child in obj.EnumerateArray())
            {
                var id = child.GetProperty("instanceID").GetInt32();
                var objname = kssInstanceIds.Find(d => d.id == id);
                if (objname != null)
                {
                    var tag = (TagInfo)scriptables.Find(d => d.pathString == objname.path);
                    if (tag != null)
                    {
                        ret.Add(tag);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        return ret;
    }

    protected T GetObjectFromPrm<T>(List<Component> components, List<KssPartsBase> scriptables, List<KssInstanceIds> kssInstanceIds, JsonElement root, string name)
    {
        var id = GetInstanceID(root, name);
        if (id != 0)
        {
            var objname = kssInstanceIds.Find(d => d.id == id);
            if (objname != null)
            {
                var tmp = components.Find(d => d.name == objname.name);
            }
        }
        return default(T);
    }

    protected List<DataExchange> GetDataExchangeFromPrm(List<Component> components, List<KssPartsBase> scriptables, List<KssInstanceIds> kssInstanceIds, JsonElement root, string name)
    {
        var ret = new List<DataExchange>();
        try
        {
            JsonElement obj = root.GetProperty(name);
            foreach (var child in obj.EnumerateArray())
            {
                ret.Add(new DataExchange
                {
                    Input = GetTagInfoFromPrm(scriptables, kssInstanceIds, child, "Input"),
                    Output = GetTagInfoFromPrm(scriptables, kssInstanceIds, child, "Output")
                });
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        return ret;
    }

    protected int GetInstanceID(JsonElement root, string name)
    {
        try
        {
            var obj = root.GetProperty(name);
            var instanceID = obj.GetProperty("instanceID");
            return instanceID.GetInt32();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        return 0;
    }
    protected List<int> GetInstanceIDs(JsonElement root, string name)
    {
        var ret = new List<int>();
        try
        {
            JsonElement obj = root.GetProperty(name);
            foreach (var child in obj.EnumerateArray())
            {
                ret.Add(child.GetProperty("instanceID").GetInt32());
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        return ret;
    }

    /// <summary>
    /// タグの値取得
    /// </summary>
    /// <param name="tag"></param>
    /// <param name="tagInfo"></param>
    /// <returns></returns>
    protected int GetTagValue(string tag, ref TagInfo tagInfo, int index = -1)
    {
        return GetTagValue(unitSetting.Database, unitSetting.mechId, tag, ref tagInfo, index);
    }
    /// <summary>
    /// "name:bit" 形式のタグを正規表現なしで解析する。bit指定があれば tag を基底名に書き換え true を返す。
    /// （毎フレーム多数呼ばれるため Regex を排除し、通常ケースのアロケーションをゼロにする）
    /// </summary>
    private static bool TryParseBitTag(ref string tag, out int bitNo)
    {
        bitNo = 0;
        int colon = tag.LastIndexOf(':');
        if (colon < 0 || colon >= tag.Length - 1)
        {
            return false;
        }
        int n = 0;
        for (int i = colon + 1; i < tag.Length; i++)
        {
            char c = tag[i];
            if (c < '0' || c > '9')
            {
                return false;
            }
            n = n * 10 + (c - '0');
        }
        bitNo = n;
        tag = tag.Substring(0, colon);
        return true;
    }

    protected int GetTagValue(string dbName, string mechId, string tag, ref TagInfo tagInfo, int index = -1)
    {
        bool hasBit = TryParseBitTag(ref tag, out int bitNo);
        if (tagInfo != null)
        {
            if (tagInfo.IsDestroyed())
            {
                tagInfo = null;
            }
            else
            {
                if (hasBit)
                {
                    return (tagInfo.Value >> bitNo) & 1;
                }
                else
                {
                    return tagInfo.isFloat ? (int)(tagInfo.fValue * 1000000f) : tagInfo.Value;
                }
            }
        }
        int value = 0;
        if ((dbName == null) || (tag == ""))
        {
            return 0;
        }
        else if (int.TryParse(tag, out value))
        {
            // 数値
            return value;
        }
        else if (index >= 0)
        {
            tag += $"[{index}]";
        }
        tagInfo = GlobalScript.GetTagInfo(dbName, mechId, tag);
        if (tagInfo == null)
        {
            return 0;
        }
        else
        {
            if (hasBit)
            {
                return (tagInfo.Value >> bitNo) & 1;
            }
            else
            {
                return tagInfo.isFloat ? (int)(tagInfo.fValue * 1000000f) : tagInfo.Value;
            }
        }
    }

    /// <summary>
    /// タグの値取得
    /// </summary>
    /// <param name="tag"></param>
    /// <param name="tagInfo"></param>
    /// <returns></returns>
    protected float GetTagValueF(string tag, ref TagInfo tagInfo, int index = -1)
    {
        return GetTagValueF(unitSetting.Database, unitSetting.mechId, tag, ref tagInfo, index);
    }
    protected float GetTagValueF(string dbName, string mechId, string tag, ref TagInfo tagInfo, int index = -1)
    {
        if (tagInfo != null)
        {
            if (tagInfo.IsDestroyed())
            {
                tagInfo = null;
            }
            else
            {
                return tagInfo.isFloat ? tagInfo.fValue : tagInfo.Value;
            }
        }
        int value = 0;
        if ((dbName == null) || (tag == ""))
        {
            return 0;
        }
        else if (int.TryParse(tag, out value))
        {
            // 数値
            return value;
        }
        else if (index >= 0)
        {
            tag += $"[{index}]";
        }
        tagInfo = GlobalScript.GetTagInfo(dbName, mechId, tag);
        return tagInfo == null ? 0 : (tagInfo.isFloat ? tagInfo.fValue : tagInfo.Value);
    }

    /// <summary>
    /// タグの値セット
    /// </summary>
    /// <param name="tag"></param>
    /// <param name="tagInfo"></param>
    /// <returns></returns>
    protected void SetTagValue(string tag, ref TagInfo tagInfo, int value, int index = -1)
    {
        SetTagValue(unitSetting.Database, unitSetting.mechId, tag, ref tagInfo, value, index);
    }
    protected void SetTagValue(string dbName, string mechId, string tag, ref TagInfo tagInfo, int value, int index = -1)
    {
        bool hasBit = TryParseBitTag(ref tag, out int bitNo);
        if (tagInfo != null)
        {
            if (hasBit)
            {
                if (value == 1)
                {
                    tagInfo.Value |= (1 << bitNo);
                }
                else
                {
                    tagInfo.Value &= ~(1 << bitNo);
                }
            }
            else
            {
                tagInfo.Value = value;
            }
            return;
        }
        if (tag == "")
        {
            return;
        }
        else if (index >= 0)
        {
            tag += $"[{index}]";
        }
        tagInfo = GlobalScript.GetTagInfo(dbName, mechId, tag);
        if (tagInfo != null)
        {
            if (hasBit)
            {
                if (value == 1)
                {
                    tagInfo.Value |= (1 << bitNo);
                }
                else
                {
                    tagInfo.Value &= ~(1 << bitNo);
                }
            }
            else
            {
                tagInfo.Value = value;
            }
        }
    }
}
