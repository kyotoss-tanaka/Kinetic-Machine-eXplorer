using System;
using UnityEngine;

[Serializable]
public class KssPartsBase : ScriptableObject
{
    public virtual string pathString
    {
        get
        {
            return "";
        }
    }
}