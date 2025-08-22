using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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

[Serializable]
public class ProcessStopWatch
{
    [SerializeField]
    public long laps;
    [SerializeField]
    public long cycle;
    public System.Diagnostics.Stopwatch sw = new();
}

/// <summary> 64bitデータ共用体として使う</summary>
[StructLayout(LayoutKind.Explicit)]
public struct uniLongAllData
{
    /// <summary>double</summary>
    [FieldOffset(0)]
    public double dblData;

    /// <summary>float</summary>
    [FieldOffset(0)]
    public float fData;

    /// <summary>ulong</summary>
    [FieldOffset(0)]
    public ulong ulData;

    /// <summary>long</summary>
    [FieldOffset(0)]
    public long lngData;

    /// <summary>int32</summary>
    [FieldOffset(0)]
    public Int32 int32Data1;
    /// <summary>int32</summary>
    [FieldOffset(4)]
    public Int32 int32Data2;

    /// <summary>uint32</summary>
    [FieldOffset(0)]
    public UInt32 ui32Data1;
    /// <summary>uint32</summary>
    [FieldOffset(4)]
    public UInt32 ui32Data2;

    /// <summary>int16</summary>
    [FieldOffset(0)]
    public Int16 int16Data1;
    /// <summary>int16</summary>
    [FieldOffset(2)]
    public Int16 int16Data2;
    /// <summary>int16</summary>
    [FieldOffset(4)]
    public Int16 int16Data3;
    /// <summary>int16</summary>
    [FieldOffset(6)]
    public Int16 int16Data4;

    /// <summary>uint16</summary>
    [FieldOffset(0)]
    public UInt16 ui16Data1;
    /// <summary>uint16</summary>
    [FieldOffset(2)]
    public UInt16 ui16Data2;
    /// <summary>uint16</summary>
    [FieldOffset(4)]
    public UInt16 ui16Data3;
    /// <summary>uint16</summary>
    [FieldOffset(6)]
    public UInt16 ui16Data4;

    /// <summary>byte</summary>
    [FieldOffset(0)]
    public byte bytData1;
    /// <summary>byte</summary>
    [FieldOffset(1)]
    public byte bytData2;
    /// <summary>byte</summary>
    [FieldOffset(2)]
    public byte bytData3;
    /// <summary>byte</summary>
    [FieldOffset(3)]
    public byte bytData4;
    /// <summary>byte</summary>
    [FieldOffset(4)]
    public byte bytData5;
    /// <summary>byte</summary>
    [FieldOffset(5)]
    public byte bytData6;
    /// <summary>byte</summary>
    [FieldOffset(6)]
    public byte bytData7;
    /// <summary>byte</summary>
    [FieldOffset(7)]
    public byte bytData8;

    /// <summary>sbyte</summary>
    [FieldOffset(0)]
    public sbyte sbData1;
    /// <summary>ubyte</summary>
    [FieldOffset(1)]
    public sbyte sbData2;
    /// <summary>ubyte</summary>
    [FieldOffset(2)]
    public sbyte sbData3;
    /// <summary>ubyte</summary>
    [FieldOffset(3)]
    public sbyte sbData4;
    /// <summary>ubyte</summary>
    [FieldOffset(4)]
    public sbyte sbData5;
    /// <summary>ubyte</summary>
    [FieldOffset(5)]
    public sbyte sbData6;
    /// <summary>ubyte</summary>
    [FieldOffset(6)]
    public sbyte sbData7;
    /// <summary>ubyte</summary>
    [FieldOffset(7)]
    public sbyte sbData8;
}
