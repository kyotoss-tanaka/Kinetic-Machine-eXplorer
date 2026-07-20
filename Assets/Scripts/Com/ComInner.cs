using Npgsql;
using Parameters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ComInner : ComBaseScript, ITagCom
{

    [Serializable]
    public class ActionTiming
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string name;
        /// <summary>
        /// 機番
        /// </summary>
        public int index;
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId;
        /// <summary>
        /// サイクル
        /// </summary>
        public int cycle;
        /// <summary>
        /// 現在サイクル
        /// </summary>
        public int nowCycle;
        /// <summary>
        /// 現在サイクル
        /// </summary>
        public int prvCycle;
        /// <summary>
        /// タイミング番号
        /// </summary>
        public int no;
        /// <summary>
        /// タイミング
        /// </summary>
        public List<ActionTimingData> timings = new();
    }

    [Serializable]
    public class ActionTimingData
    {
        /// <summary>
        /// トリガタイミング
        /// </summary>
        public int trg;
        /// <summary>
        /// 入力タグ
        /// </summary>
        public string input;
        /// <summary>
        /// 出力タグ
        /// </summary>
        public string output;
        /// <summary>
        /// 継続
        /// </summary>
        public bool isContinue;
    }

    private class TimingData
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId;
        /// <summary>
        /// サイクル
        /// </summary>
        public int cycle;
        /// <summary>
        /// ONタイミング
        /// </summary>
        public int on;
        /// <summary>
        /// OFFタイミング
        /// </summary>
        public int off;
        /// <summary>
        /// タグ
        /// </summary>
        public string tag;
    }

    /// <summary>
    /// サーバー名
    /// </summary>
    public string Name { get { return Server + ":" + Port.ToString(); } }

    /// <summary>
    /// 時間停止
    /// </summary>
    [SerializeField]
    public bool isStop = false;

    /// <summary>
    /// 時間比率
    /// </summary>
    [SerializeField]
    public float timeRate = 1;

    /// <summary>
    /// タイミング設定
    /// </summary>
    [SerializeField]
    private List<TimingData> timings = new();

    /// <summary>
    /// 動作設定
    /// </summary>
    [SerializeField]
    public List<ActionTiming> acts = new();

    /// <summary>
    /// 内部デバイス(仮想I/O)の入力キー集合（(mechId, tag)）。
    /// アクション入力がこれに該当する場合、ComInner はサイクル駆動しない（スイッチ等が駆動＝保持）。
    /// </summary>
    private readonly HashSet<(string mechId, string tag)> internalInputKeys = new();

    /// <summary>
    /// タイミング用
    /// </summary>
    System.Diagnostics.Stopwatch swTiming = new();

    /// <summary>
    /// 現在の時間
    /// </summary>
    private long elapsedMilliseconds = 0;

    /// <summary>
    /// 前回の時間
    /// </summary>
    private long prvElapsedMilliseconds = 0;

    [SerializeField]
    public int actIndex = 0;
    [SerializeField]
    public long actCycle = 0;
    [SerializeField]
    public int no = 0;
    [SerializeField]
    public List<int> inputs = new();
    [SerializeField]
    public List<int> outputs = new();

    /// <summary>
    /// 表示サイクル
    /// </summary>
    public int viewCycle = 1000;

    /// <summary>
    /// コマ送りステップ
    /// </summary>
    public int step = 0;

    /// <summary>
    /// 経過時間
    /// </summary>
    public int time = 0;

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
    /// 更新処理
    /// </summary>
    protected override void FixedUpdate()
    {
        if (GlobalScript.isLoaded)
        {
            base.FixedUpdate();

            // データ交換処理
            DataExchangeProcess();

            // データ更新処理
            lock (objLock)
            {
                RenewData();
            }
        }
        else
        {
            swTiming.Restart();
        }
    }

    /// <summary>
    /// 削除時
    /// </summary>
    protected override void OnDestroy()
    {
        base.OnDestroy();
    }

    /// <summary>
    /// データ更新
    /// </summary>
    public override void RenewData()
    {
        base.RenewData();
        var sw = new System.Diagnostics.Stopwatch();
        sw.Start();

        // 経過時間作成
        var lap = swTiming.ElapsedMilliseconds;
        if (step != 0)
        {
            elapsedMilliseconds += step;
            step = 0;
        }
        else
        {
            elapsedMilliseconds = (long)((lap - prvElapsedMilliseconds) * (isStop ? 0 : timeRate)) + elapsedMilliseconds;
        }
        prvElapsedMilliseconds = lap;
        time = (int)elapsedMilliseconds;
        foreach (var tags in GlobalScript.callbackTags)
        {
            GlobalScript.SetTagData(tags.cycle, time);
        }
        try
        {
            // I/Oタイミングセット
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

            // 動作設定セット
            var onTags = new List<TagInfo>();
            var offTags = new List<TagInfo>();
            foreach (var act in acts)
            {
                var input = act.timings[act.no].input;
                var output = act.timings[act.no].output;
                act.nowCycle = (int)(time % act.cycle);
                if ((input == "") || (output == ""))
                {
                    continue;
                }
                // 入力が内部デバイス(仮想I/O)ならサイクル駆動しない＝スイッチ等が駆動する（内部モードでも保持・上書きしない）。
                if (internalInputKeys.Contains((act.mechId, input)))
                {
                    act.prvCycle = act.nowCycle;
                    continue;
                }
                if (GlobalScript.tagDatas[Name][act.mechId][input].Value == 1)
                {
                    // ON中完了信号待ち
                    if (GlobalScript.tagDatas[Name][act.mechId][output].Value == 1)
                    {
                        offTags.Add(GlobalScript.tagDatas[Name][act.mechId][input]);
                        act.no = (act.no + 1) % act.timings.Count;
                    }
                }
                else
                {
                    // OFF中であれば通過判定
                    bool isContinue = act.timings[act.no].isContinue;
                    if (isContinue)
                    {
                        // 連続動作の場合は次のポイントへ
                        act.no = (act.no + 1) % act.timings.Count;
                    }
                    else
                    {
                        var trg = act.timings[act.no].trg;
                        if (act.prvCycle <= act.nowCycle)
                        {
                            // 通常処理
                            if (trg >= act.prvCycle && trg < act.nowCycle)
                            {
                                onTags.Add(GlobalScript.tagDatas[Name][act.mechId][input]);
                            }
                        }
                        else
                        {
                            // 反転処理
                            if ((trg >= act.prvCycle) || (trg <= act.nowCycle))
                            {
                                onTags.Add(GlobalScript.tagDatas[Name][act.mechId][input]);
                            }
                        }
                    }
                }
                act.prvCycle = act.nowCycle;
            }
            // 一括出力
            foreach (var tag in onTags)
            {
                tag.Value = 1;
            }
            foreach (var tag in offTags)
            {
                tag.Value = 0;
            }
            if (acts.Count > actIndex)
            {
                var act = acts[actIndex];
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
                this.actCycle = act.nowCycle;
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
            UnityEngine.Debug.Log("ComInner : " + ex.Message);
        }
        processTime = sw.ElapsedMilliseconds;
    }

    /// <summary>
    /// タグに値をセットする
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
    /// パラメータをセットする
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
    public void SetParameter(int No, int Cycle, string Server, int Port, string Database, string User, string Password, bool isClientMode, DataExchangeSetting dataExchange, List<InnerProcessSetting> innerSettings, List<UnitActionSetting> actionSettings, List<InternalDeviceSetting> internalDeviceSettings)
    {
        SetParameter(No, Cycle, Server, Port, Database, User, Password, isClientMode, dataExchange);

        // 内部デバイス(仮想I/O)の入力キー集合を構築（アクション入力の照合用）。
        // これらの入力はサイクル駆動せず、スイッチ等に委ねる（内部モードでも保持・上書きしない）。
        internalInputKeys.Clear();
        if (internalDeviceSettings != null)
        {
            foreach (var dev in internalDeviceSettings)
            {
                if (dev == null || dev.tags == null) { continue; }
                foreach (var tag in dev.tags)
                {
                    if (!string.IsNullOrEmpty(tag)) { internalInputKeys.Add((dev.mechId, tag)); }
                }
            }
        }

        // 初期タグ作成
        if (!GlobalScript.tagDatas.ContainsKey(Name))
        {
            GlobalScript.tagDatas.Add(Name, new Dictionary<string, Dictionary<string, TagInfo>>());
        }
        foreach (var inner in innerSettings)
        {
            if (!GlobalScript.tagDatas[Name].ContainsKey(inner.mechId))
            {
                GlobalScript.tagDatas[Name].Add(inner.mechId, new Dictionary<string, TagInfo>());
            }
            if (!GlobalScript.tagDatas[Name][inner.mechId].ContainsKey(inner.tag))
            {
                GlobalScript.tagDatas[Name][inner.mechId].Add(inner.tag, ScriptableObject.CreateInstance<TagInfo>());
            }
        }
        foreach (var tags in GlobalScript.callbackTags)
        {
            if (!GlobalScript.tagDatas[Name].ContainsKey(tags.cycle.MechId))
            {
                GlobalScript.tagDatas[Name].Add(tags.cycle.MechId, new Dictionary<string, TagInfo>());
            }
            if (!GlobalScript.tagDatas[Name][tags.cycle.MechId].ContainsKey(tags.cycle.Tag))
            {
                GlobalScript.tagDatas[Name][tags.cycle.MechId].Add(tags.cycle.Tag, ScriptableObject.CreateInstance<TagInfo>());
            }
        }

        // I/Oタイミングセット
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

        // 動作設定セット
        acts = new();
        foreach (var actionSetting in actionSettings)
        {
            var act = new ActionTiming
            {
                name = actionSetting.name,
                index = actionSettings.IndexOf(actionSetting),
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
                // タグ作成
                if (!GlobalScript.tagDatas[Name].ContainsKey(actionSetting.mechId))
                {
                    GlobalScript.tagDatas[Name].Add(actionSetting.mechId, new Dictionary<string, TagInfo>());
                }
                if (!GlobalScript.tagDatas[Name][actionSetting.mechId].ContainsKey(action.start))
                {
                    GlobalScript.tagDatas[Name][actionSetting.mechId].Add(action.start, ScriptableObject.CreateInstance<TagInfo>());
                }
                if (!GlobalScript.tagDatas[Name][actionSetting.mechId].ContainsKey(action.end))
                {
                    GlobalScript.tagDatas[Name][actionSetting.mechId].Add(action.end, ScriptableObject.CreateInstance<TagInfo>());
                }
                act.timings.Add(timing);
            }
            if (act.timings.Count > 0)
            {
                acts.Add(act);
            }
        }
    }
}
