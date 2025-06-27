using Npgsql;
using Parameters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Windows;

public class ComInner : ComBaseScript
{

    [Serializable]
    private class ActionTiming
    {
        /// <summary>
        /// �@��
        /// </summary>
        public string mechId;
        /// <summary>
        /// �T�C�N��
        /// </summary>
        public int cycle;
        /// <summary>
        /// ���݃T�C�N��
        /// </summary>
        public int nowCycle;
        /// <summary>
        /// ���݃T�C�N��
        /// </summary>
        public int prvCycle;
        /// <summary>
        /// �^�C�~���O�ԍ�
        /// </summary>
        public int no;
        /// <summary>
        /// �^�C�~���O
        /// </summary>
        public List<ActionTimingData> timings = new();
    }

    [Serializable]
    private class ActionTimingData
    {
        /// <summary>
        /// �g���K�^�C�~���O
        /// </summary>
        public int trg;
        /// <summary>
        /// ���̓^�O
        /// </summary>
        public string input;
        /// <summary>
        /// �o�̓^�O
        /// </summary>
        public string output;
        /// <summary>
        /// �p��
        /// </summary>
        public bool isContinue;
    }

    private class TimingData
    {
        /// <summary>
        /// �@��
        /// </summary>
        public string mechId;
        /// <summary>
        /// �T�C�N��
        /// </summary>
        public int cycle;
        /// <summary>
        /// ON�^�C�~���O
        /// </summary>
        public int on;
        /// <summary>
        /// OFF�^�C�~���O
        /// </summary>
        public int off;
        /// <summary>
        /// �^�O
        /// </summary>
        public string tag;
    }

    /// <summary>
    /// �T�[�o�[��
    /// </summary>
    public string Name { get { return Server + ":" + Port.ToString(); } }

    /// <summary>
    /// �^�C�~���O�ݒ�
    /// </summary>
    [SerializeField]
    private List<TimingData> timings = new();

    /// <summary>
    /// ����ݒ�
    /// </summary>
    [SerializeField]
    private List<ActionTiming> acts = new();

    /// <summary>
    /// �^�C�~���O�p
    /// </summary>
    System.Diagnostics.Stopwatch swTiming = new();

    [SerializeField]
    public int timeRate = 1;

    [SerializeField]
    public int actNo = 0;
    [SerializeField]
    public long time = 0;
    [SerializeField]
    public int no = 0;
    [SerializeField]
    public List<int> inputs = new();
    [SerializeField]
    public List<int> outputs = new();


    // Start is called before the first frame update
    protected override void Start()
    {
        if (!GlobalScript.tagDatas.ContainsKey(Name))
        {
            GlobalScript.tagDatas.Add(Name, new Dictionary<string, Dictionary<string, TagInfo>>());
        }
        if (!GlobalScript.inners.ContainsKey(Name))
        {
            GlobalScript.inners.Add(Name, this);
        }
        swTiming.Start();
    }

    /// <summary>
    /// �X�V����
    /// </summary>
    protected override void FixedUpdate()
    {
        base.FixedUpdate();

        // �f�[�^��������
        DataExchangeProcess();

        // �f�[�^�X�V����
        lock (objLock)
        {
            RenewData();
        }
    }

    /// <summary>
    /// �f�[�^�X�V
    /// </summary>
    public override void RenewData()
    {
        base.RenewData();
        var sw = new System.Diagnostics.Stopwatch();
        sw.Start();

        var timeRate = this.timeRate < 1 ? 1 : this.timeRate;
        var time = swTiming.ElapsedMilliseconds / timeRate;
        try
        {
            // I/O�^�C�~���O�Z�b�g
            foreach (var timing in timings)
            {
                var now = time % timing.cycle;
                var value = 0;
                if (timing.on < timing.off)
                {
                    if ((now >= timing.on) && (now < timing.off))
                    {
                        value = 1;
                    }
                }
                else
                {
                    if ((now >= timing.on) || (now < timing.off))
                    {
                        value = 1;
                    }
                }
                GlobalScript.tagDatas[Name][timing.mechId][timing.tag].Value = value;
            }

            // ����ݒ�Z�b�g
            foreach (var act in acts)
            {
                var input = act.timings[act.no].input;
                var output = act.timings[act.no].output;
                act.nowCycle = (int)(time % act.cycle);
                if (GlobalScript.tagDatas[Name][act.mechId][input].Value == 1)
                {
                    // ON�������M���҂�
                    if (GlobalScript.tagDatas[Name][act.mechId][output].Value == 1)
                    {
                        GlobalScript.tagDatas[Name][act.mechId][input].Value = 0;
                        act.no = (act.no + 1) % act.timings.Count;
                    }
                }
                else
                {
                    // OFF���ł���Βʉߔ���
                    bool isContinue = act.timings[act.no].isContinue;
                    if (isContinue)
                    {
                        // �A������̏ꍇ�͎��̃|�C���g��
                        act.no = (act.no + 1) % act.timings.Count;
                    }
                    else
                    {
                        var trg = act.timings[act.no].trg;
                        if (act.prvCycle <= act.nowCycle)
                        {
                            // �ʏ폈��
                            if (trg >= act.prvCycle && trg < act.nowCycle)
                            {
                                GlobalScript.tagDatas[Name][act.mechId][input].Value = 1;
                            }
                        }
                        else
                        {
                            // ���]����
                            if ((trg >= act.prvCycle) || (trg <= act.nowCycle))
                            {
                                GlobalScript.tagDatas[Name][act.mechId][input].Value = 1;
                            }
                        }
                    }
                }
                act.prvCycle = act.nowCycle;
            }
            if (acts.Count > actNo)
            {
                var act = acts[actNo];
                if (inputs.Count != act.timings.Count)
                {
                    outputs.Clear();
                    inputs.Clear();
                    foreach (var timing in act.timings)
                    {
                        inputs.Add(0);
                        outputs.Add(0);
                    }
                }
                this.time = act.nowCycle;
                no = act.no;
                for (var i = 0; i < inputs.Count; i++)
                {
                    inputs[i] = GlobalScript.tagDatas[Name][act.mechId][act.timings[i].input].Value;
                    outputs[i] = GlobalScript.tagDatas[Name][act.mechId][act.timings[i].output].Value;
                }
            }
        }
        catch (Exception ex)
        {
            
        }
        processTime = sw.ElapsedMilliseconds;
    }

    /// <summary>
    /// �^�O�ɒl���Z�b�g����
    /// </summary>
    /// <param name="tag"></param>
    /// <param name=""></param>
    public void SetDatas(List<TagInfo> tags)
    {
        foreach (var tag in tags)
        {
            if (GlobalScript.tagDatas[tag.Database].ContainsKey(tag.MechId))
            {
                if (GlobalScript.tagDatas[tag.Database][tag.MechId].ContainsKey(tag.Tag))
                {
                    GlobalScript.tagDatas[tag.Database][tag.MechId][tag.Tag].Value = tag.Value;
                }
            }
        }
    }

    /// <summary>
    /// �p�����[�^���Z�b�g����
    /// </summary>
    /// <param name="components"></param>
    /// <param name="scriptables"></param>
    /// <param name="kssInstanceIds"></param>
    /// <param name="root"></param>
    public override void SetParameter(List<Component> components, List<KssPartsBase> scriptables, List<KssInstanceIds> kssInstanceIds, JsonElement root)
    {
        base.SetParameter(components, scriptables, kssInstanceIds, root);
        Server = GetStringFromPrm(root, "Server");
        Port = GetInt32FromPrm(root, "Port");
        Database = GetStringFromPrm(root, "Database");
        User = GetStringFromPrm(root, "User");
        Password = GetStringFromPrm(root, "Password");
    }

    /// <summary>
    /// �p�����[�^�Z�b�g
    /// </summary>
    /// <param name="No"></param>
    /// <param name="Cycle"></param>
    /// <param name="Server"></param>
    /// <param name="Port"></param>
    /// <param name="Database"></param>
    /// <param name="User"></param>
    /// <param name="Password"></param>
    /// <param name="isClientMode"></param>
    public void SetParameter(int No, int Cycle, string Server, int Port, string Database, string User, string Password, bool isClientMode, DataExchangeSetting dataExchange, List<InnerProcessSetting> innerSettings, List<UnitActionSetting> actionSettings)
    {
        SetParameter(No, Cycle, Server, Port, Database, User, Password, isClientMode, dataExchange);

        // �����^�O�쐬
        foreach (var inner in innerSettings)
        {
            if (!GlobalScript.tagDatas.ContainsKey(Name))
            {
                GlobalScript.tagDatas.Add(Name, new Dictionary<string, Dictionary<string, TagInfo>>());
            }
            if (!GlobalScript.tagDatas[Name].ContainsKey(inner.mechId))
            {
                GlobalScript.tagDatas[Name].Add(inner.mechId, new Dictionary<string, TagInfo>());
            }
            if (!GlobalScript.tagDatas[Name][inner.mechId].ContainsKey(inner.tag))
            {
                GlobalScript.tagDatas[Name][inner.mechId].Add(inner.tag, ScriptableObject.CreateInstance<TagInfo>());
            }
        }

        // I/O�^�C�~���O�Z�b�g
        timings = new();
        foreach (var inner in innerSettings.FindAll(d => d.cycle != 0))
        {
            var timing = new TimingData
            {
                mechId = inner.mechId,
                cycle = (int)inner.cycle,
                on = (int)inner.onTiming,
                off = (int)inner.offTiming,
                tag = inner.tag
            };
            timings.Add(timing);
        }

        // ����ݒ�Z�b�g
        acts = new();
        foreach (var actionSetting in actionSettings)
        {
            var act = new ActionTiming
            {
                mechId = actionSetting.mechId,
                cycle = actionSetting.cycle,
                timings = new()
            };
            foreach (var action in actionSetting.actions)
            {
                var timing = new ActionTimingData
                {
                    trg = action.trg,
                    isContinue = action.isContinue,
                    input = action.start,
                    output = action.end
                };
                act.timings.Add(timing);
            }
            acts.Add(act);
        }
    }
}
