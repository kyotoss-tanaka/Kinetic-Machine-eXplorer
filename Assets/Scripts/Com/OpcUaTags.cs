using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class OpcUaTagInfo
{
    [Serializable]
    public class OpcUaTag
    {
        public bool isWrite = false;
        public bool isArray = false;
        public int count = 1;
        public string name = "";
        public List<OpcUaTag> children;
    }

    [Serializable]
    public class OpcUaTags
    {
        public int ns;
        public List<OpcUaTag> booleanTag;
        public List<OpcUaTag> int32Tag;
        public List<OpcUaTag> floatTag;
    }

    public List<OpcUaTags> tags;
}
