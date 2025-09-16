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

    #region �N���X
    #endregion �N���X

    #region �萔
    #endregion �萔

    #region �ϐ�
    #endregion �ϐ�

    /// <summary>
    /// �ꊇ��M�J�E���g
    /// </summary>
    public override int BULK_RCV_COUNT
    {
        get
        {
            return 0;
        }
    }

    /// <summary>
    /// �J�n����
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
    /// ��M����
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
            // ��M�f�[�^�쐬
            foreach (var tags in dctReadSortedTags1)
            {
                foreach (var tag in tags.Value)
                {
                    nodes.Add(new NodeId(tag.NodeId, namespaceIndex));
                }
            }
            // �f�[�^��M
            opcua.session.ReadValues(nodes, out results, out errors);
            // ��M�f�[�^�m�F
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
                // ����̂ݏ������݃f�[�^��M
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
    /// �\�[�g�f�[�^�쐬
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
            // DB�o�^
            SetDbData(tag);
        }
        CreateSorted(dctReadTags1, ref dctReadSortedTags1);
        CreateSorted(dctReadTags2, ref dctReadSortedTags2);
        // �\�[�g���ꂽ�^�O��DB�f�[�^���Z�b�g
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
