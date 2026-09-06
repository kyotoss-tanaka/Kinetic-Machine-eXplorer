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
    /// ロック用オブジェクト
    /// </summary>
    protected object objLock = new object();

    /// <summary>
    /// 未解決のデータ交換を引き直す間隔(呼び出し回数)。
    /// 実機PLC/MICKS接続時は初回で解決してキャッシュされるのでこの間引きは働かない。
    /// 内部モードのように相手側のタグがまだ存在しない構成で、毎フレームの空振り検索を防ぐ。
    /// </summary>
    private const int ResolveRetryCount = 50;

    /// <summary>
    /// 未解決データ交換の引き直しカウンタ
    /// </summary>
    private int resolveCount = 0;

    /// <summary>
    /// サイクル統計の更新間隔(呼び出し回数)。Inspector表示専用なので毎回でなくてよい
    /// </summary>
    private const int StatsInterval = 30;

    /// <summary>
    /// サイクル統計の更新カウンタ
    /// </summary>
    private int statsCount = 0;

    #region 計測マーカー（負荷調査用）
    /// <summary>データ交換処理の計測</summary>
    private static readonly Unity.Profiling.ProfilerMarker markerDataExchange = new("ComBase.DataExchange");
    /// <summary>サイクル統計(Max/Min/Average)の計測</summary>
    private static readonly Unity.Profiling.ProfilerMarker markerStats = new("ComBase.Stats");
    #endregion 計測マーカー

    /// <summary>
    /// 書き込みデータ
    /// </summary>
    protected volatile List<TagInfoCom> writeDatas = new List<TagInfoCom>();

    /// <summary>
    /// 初回フラグ
    /// </summary>
    protected bool isFirst = true;

    /// <summary>
    /// 初回受信完了処理
    /// </summary>
    protected bool isRcvDb = false;

    /// <summary>
    /// データ交換設定
    /// </summary>
    protected DataExchangeSetting dataExchange;

    /// <summary>
    /// データ初期値設定
    /// </summary>
    protected List<DataExchange> initDatas = new List<DataExchange>();

    /// <summary>
    /// データ交換設定
    /// </summary>
    protected List<DataExchange> dataExchanges = new List<DataExchange>();

    /// <summary>
    /// 時間計測用
    /// </summary>
    private Stopwatch sw = new Stopwatch();

    /// <summary>
    /// サイクル時間
    /// </summary>
    private List<long> cycleLaps = new List<long>();

    /// <summary>
    /// 処理時間
    /// </summary>
    private List<long> processLaps = new List<long>();
    /// <summary>
    /// データ交換処理
    /// </summary>
    protected virtual void DataExchangeProcess()
    {
        using var _ = markerDataExchange.Auto();
        if (isFirst)
        {
            // 初回のみ
            foreach (var data in initDatas)
            {
                GetTagValue(data.OutputTag, ref data.Output);
                if (data.Output != null)
                {
                    data.Output.Value = data.InitValue;
                }
            }
        }
        // 出力タグが未解決の項目は、この回に書き込みが起きないことが確定している。
        // GetTagValue は解決成功時しかキャッシュしないため、毎回引くと
        // 未定義タグの数だけ辞書検索が積み上がる（現案件は511件×2＝1022回/回が全て空振り）。
        // 後からタグが作られる構成に追従できるよう、一定間隔でだけ引き直す。
        var isResolve = (++resolveCount >= ResolveRetryCount);
        if (isResolve)
        {
            resolveCount = 0;
        }
        foreach (var data in dataExchanges)
        {
            if ((data.Output == null) && !isResolve)
            {
                continue;
            }
            var input = GetTagValue(data.InputTag, ref data.Input);
            GetTagValue(data.OutputTag, ref data.Output);
            if (data.Output != null)
            {
                data.Output.Value = input;
            }
        }
        // DBのデータ作成完了していないとスルーされる
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
            // Max/Min/Average は Inspector 表示専用。
            // 最大1000要素のリストを6本ぶん毎回走査する必要はないので間引く
            if (++statsCount >= StatsInterval)
            {
                statsCount = 0;
                using (markerStats.Auto())
                {
                    maxCycle = cycleLaps.Max();
                    minCycle = cycleLaps.Min();
                    avgCycle = cycleLaps.Average();
                    maxProcess = processLaps.Max();
                    minProcess = processLaps.Min();
                    avgProcess = processLaps.Average();
                }
            }
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
    /// パラメータセット
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
        // ダミーデータ作成
        unitSetting = new UnitSetting
        {
            Database = Server + ":" + Port,
            mechId = dataExchange.mechId
        };
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
                    InputTag = data.input,
                    OutputTag = data.output,
                    InitValue = data.initValue,

                };
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
    /// ロックオブジェクト取得
    /// </summary>
    /// <returns></returns>
    public object GetLockObject()
    {
        return objLock;
    }
}
