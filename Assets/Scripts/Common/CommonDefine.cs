using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class KssSettings
{
    public string name;
    public string path;
    public List<KssSetting> settings;
    public List<KssBoxCollider> boxColliders;
    public List<KssMeshCollider> meshColliders;
}

[Serializable]
public class KssSetting
{
    /// <summary>
    /// 設定JSON
    /// </summary>
    public string json;
    /// <summary>
    /// スクリプトタイプ
    /// </summary>
    public string type;
}

[Serializable]
public class KssBoxCollider
{
    public Vector3 center;
    public Vector3 size;
    public bool isTrigger;
}

[Serializable]
public class KssMeshCollider
{
    public bool convex;
    public bool isTrigger;
}

[Serializable]
public class KssInstanceIds
{
    public int id;
    public string name;
    public string path;
}

[Serializable]
public class KssSaveFormat
{
    public KssSettings[] dctSettings;
    public KssInstanceIds[] dctInstanceId;
}
