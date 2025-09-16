using Meta.XR.ImmersiveDebugger.UserInterface;
using NUnit;
using Opc.Ua;
using Opc.Ua.Client;
using Parameters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

public class ComOpcUa : ComProtocolBase
{

    #region クラス
    #endregion クラス

    #region 定数
    #endregion 定数

    #region 変数
    #endregion 変数

    /// <summary>
    /// 一括受信カウント
    /// </summary>
    public override int BULK_RCV_COUNT
    {
        get
        {
            return 0;
        }
    }

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Start()
    {
        base.Start();

        if (!GlobalScript.opcuas.ContainsKey(Name))
        {
            GlobalScript.opcuas.Add(Name, this);
        }
    }

    /// <summary>
    /// 受信処理
    /// </summary>
    /// <returns></returns>
    protected override bool Recieve()
    {
        var ret = true;
        try
        {
            var nodes = new List<NodeId>();
            DataValueCollection results;
            IList<ServiceResult> errors;
            // 受信データ作成
            foreach (var tags in dctReadSortedTags1)
            {
                foreach (var tag in tags.Value)
                {
                    nodes.Add(new NodeId(tag.NodeId, namespaceIndex));
                }
            }
            // データ受信
            opcua.session.ReadValues(nodes, out results, out errors);
            // 受信データ確認
            if ((nodes.Count == results.Count) && (nodes.Count == errors.Count))
            {
                foreach (var tags in dctReadSortedTags1)
                {
                    foreach (var tag in tags.Value)
                    {
                        var i = directData.tags.FindIndex(d => d.DataTag == tag.DataTag);
                        if ((results[i].Value is float[] fv))
                        {
                            for (var j = 0; j < directData.tags[i].DataCount; j++)
                            {
                                if (j < fv.Length)
                                {
                                    tag.values[j].Value = (int)(fv[j] * 1000000);
                                }
                            }
                        }
                        else if (results[i].Value is short[] sv)
                        {
                            for (var j = 0; j < directData.tags[i].DataCount; j++)
                            {
                                if (j < sv.Length)
                                {
                                    tag.values[j].Value = (int)(sv[j] * 1000000);
                                }
                            }
                        }
                    }
                }
            }
            if (isFirst)
            {
                // 初回のみ書き込みデータ受信
                foreach (var tags in dctReadSortedTags2)
                {
                    foreach (var tag in tags.Value)
                    {
                        /*
                        int commandId = 0;
                        ret &= Read(tag, ref commandId);
                        if (!IsConnected)
                        {
                            return false;
                        }
                        */
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Disconnect();
            ret= false;
        }
        return ret;
    }

    /// <summary>
    /// ソートデータ作成
    /// </summary>
    protected override void CreateSortedData()
    {
        if (!GlobalScript.tagDatas.ContainsKey(Name))
        {
            GlobalScript.tagDatas.Add(Name, new Dictionary<string, Dictionary<string, TagInfo>>());
        }
        Dictionary<string, List<KMXDBSetting>> dctReadTags1 = new();
        Dictionary<string, List<KMXDBSetting>> dctReadTags2 = new();
        foreach (var tag in directData.tags)
        {
            if (tag.IsWrite)
            {
                if (!dctReadTags2.ContainsKey(tag.RegisterType))
                {
                    dctReadTags2.Add(tag.RegisterType, new List<KMXDBSetting>());
                }
                dctReadTags2[tag.RegisterType].Add((KMXDBSetting)tag.Clone());
                if (!dctWriteSortedTags.ContainsKey(tag.RegisterType))
                {
                    dctWriteSortedTags.Add(tag.RegisterType, new List<KMXDBSetting>());
                }
                dctWriteSortedTags[tag.RegisterType].Add((KMXDBSetting)tag.Clone());
            }
            else
            {
                if (!dctReadTags1.ContainsKey(tag.RegisterType))
                {
                    dctReadTags1.Add(tag.RegisterType, new List<KMXDBSetting>());
                }
                dctReadTags1[tag.RegisterType].Add((KMXDBSetting)tag.Clone());
            }
            // DB登録
            SetDbData(tag);
        }
        CreateSorted(dctReadTags1, ref dctReadSortedTags1);
        CreateSorted(dctReadTags2, ref dctReadSortedTags2);
        // ソートされたタグにDBデータをセット
        foreach (var tags in dctWriteSortedTags)
        {
            foreach (var tag in tags.Value)
            {
                SetDbPointer(tag);
            }
        }
    }

    public override void SetParameter(int No, int Cycle, string Server, int Port, string Database, string User, string Password, bool isClientMode, DataExchangeSetting dataExchange, PostgresSetting.KmxDirectData directData)
    {
        endpointUrl = directData.endpointURL;
        ushort.TryParse(directData.nameSpaceIndex, out namespaceIndex);
        base.SetParameter(No, Cycle, Server, Port, Database, User, Password, isClientMode, dataExchange, directData);
    }
}
