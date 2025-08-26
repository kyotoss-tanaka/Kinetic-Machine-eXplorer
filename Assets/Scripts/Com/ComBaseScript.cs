using Parameters;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Windows;

public class ComBaseScript : KssBaseScript
{
    [SerializeField]
    protected int No = 0;
    [SerializeField]
    protected int Cycle = 50;
    [SerializeField]
    protected bool isClientMode = false;

    [SerializeField]
    protected string Server = "localhost";
    [SerializeField]
    protected int Port = 5432;
    [SerializeField]
    protected string Database = "kcp_db";
    [SerializeField]
    protected string User = "postgres";
    [SerializeField]
    protected string Password = "kyotoss";

    [SerializeField]
    public long nowCycle = 0;
    [SerializeField]
    public long waitTime = 0;
    [SerializeField]
    public long processTime = 0;
    [SerializeField]
    public long maxProcess = 0;
    [SerializeField]
    public long minProcess = 0;
    [SerializeField]
    public double avgProcess = 0;
    [SerializeField]
    public long maxCycle = 0;
    [SerializeField]
    public long minCycle = 0;
    [SerializeField]
    public double avgCycle = 0;
    [SerializeField]
    public int dataCount = 0;

    [SerializeField]
    public bool isCylceClear = false;

    /// <summary>
    /// ���b�N�p�I�u�W�F�N�g
    /// </summary>
    protected object objLock = new object();

    /// <summary>
    /// �������݃f�[�^
    /// </summary>
    protected volatile List<TagInfoCom> writeDatas = new List<TagInfoCom>();

    /// <summary>
    /// ����t���O
    /// </summary>
    protected bool isFirst = true;

    /// <summary>
    /// �����M��������
    /// </summary>
    protected bool isRcvDb = false;

    /// <summary>
    /// �f�[�^�����ݒ�
    /// </summary>
    protected DataExchangeSetting dataExchange;

    /// <summary>
    /// �f�[�^�����l�ݒ�
    /// </summary>
    protected List<DataExchange> initDatas = new List<DataExchange>();

    /// <summary>
    /// �f�[�^�����ݒ�
    /// </summary>
    protected List<DataExchange> dataExchanges = new List<DataExchange>();

    /// <summary>
    /// ���Ԍv���p
    /// </summary>
    private Stopwatch sw = new Stopwatch();

    /// <summary>
    /// �T�C�N������
    /// </summary>
    private List<long> cycleLaps = new List<long>();

    /// <summary>
    /// ��������
    /// </summary>
    private List<long> processLaps = new List<long>();
    /// <summary>
    /// �f�[�^��������
    /// </summary>
    protected virtual void DataExchangeProcess()
    {
        var tags = new List<TagInfo>();
        if (isFirst)
        {
            // ����̂�
            foreach (var data in initDatas)
            {
                data.Output.Value = data.Input.Value;
                tags.Add(data.Output);
            }
        }
        foreach (var data in dataExchanges)
        {
            var input = GlobalScript.GetTagData(data.Input);
            data.Output.Value = input;
            tags.Add(data.Output);
        }
        GlobalScript.SetTagDatas(tags);
        // DB�̃f�[�^�쐬�������Ă��Ȃ��ƃX���[�����
        isFirst = !isRcvDb;
    }

    public virtual void RenewData()
    {
        if (sw.IsRunning)
        {
            if (isCylceClear)
            {
                cycleLaps.Clear();
                processLaps.Clear();
            }
            nowCycle = sw.ElapsedMilliseconds;
            cycleLaps.Add(nowCycle);
            processLaps.Add(processTime);
            maxCycle = cycleLaps.Max();
            minCycle = cycleLaps.Min();
            avgCycle = cycleLaps.Average();
            maxProcess = processLaps.Max();
            minProcess = processLaps.Min();
            avgProcess = processLaps.Average();
            if (cycleLaps.Count > 1000)
            {
                cycleLaps.RemoveAt(0);
                processLaps.RemoveAt(0);
            }
            dataCount = cycleLaps.Count;
            sw.Restart();
        }
        else
        {
            sw.Start();
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
        Cycle = GetInt32FromPrm(root, "Cycle");
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
    public void SetParameter(int No, int Cycle, string Server, int Port, string Database, string User, string Password, bool isClientMode, DataExchangeSetting dataExchange)
    {
        this.No = No;
        this.Cycle = Cycle;
        this.Server = Server;
        this.Port = Port;
        this.Database = Database;
        this.User = User;
        this.Password = Password;
        this.isClientMode = isClientMode;
        this.dataExchange = dataExchange == null ? new DataExchangeSetting() : dataExchange;
        initDatas = new();
        dataExchanges = new();
        if (dataExchange != null)
        {
            foreach (var data in dataExchange.datas)
            {
                DataExchange d = new DataExchange
                {
                    Input = ScriptableObject.CreateInstance<TagInfo>(),
                    Output = ScriptableObject.CreateInstance<TagInfo>()
                };
                d.Input.Database = Server + ":" + Port;
                d.Input.MechId = dataExchange.mechId;
                d.Input.Tag = data.input;
                d.Input.Value = data.initValue;
                d.Output.Database = Server + ":" + Port;
                d.Output.MechId = dataExchange.mechId;
                d.Output.Tag = data.output;
                if (data.isInit)
                {
                    initDatas.Add(d);
                }
                else
                {
                    dataExchanges.Add(d);
                }
            }
        }
        isFirst = true;
    }

    /// <summary>
    /// ���b�N�I�u�W�F�N�g�擾
    /// </summary>
    /// <returns></returns>
    public object GetLockObject()
    {
        return objLock;
    }
}
