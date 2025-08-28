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

public class ComInner : ComBaseScript
{

    [Serializable]
    private class ActionTiming
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
    private class ActionTimingData
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
    private bool isStop = false;

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
    private List<ActionTiming> acts = new();

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
    /// 表示用
    /// </summary>
    private GameObject canvaObj;
    private GameObject? uiObj;
    private CanvasMenuTimeScript timeScript;
    private Toggle toggle;
    private Slider slider;
    private TextMeshProUGUI text;
    private Button button;
    private TMP_InputField input;
    private TextMeshProUGUI cycle;
    private TMP_InputField inputStep;
    private Button buttonNext;
    private Button buttonPrev;

    /// <summary>
    /// 表示サイクル
    /// </summary>
    private int viewCycle = 1000;

    /// <summary>
    /// コマ送りステップ
    /// </summary>
    private int step = 0;

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

        // 表示を削除しておく
        toggle.onValueChanged.RemoveAllListeners();
        slider.onValueChanged.RemoveAllListeners();
        button.onClick.RemoveAllListeners();
        input.onValueChanged.RemoveAllListeners();
        Destroy(uiObj);
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
        var time = (int)elapsedMilliseconds;
        foreach (var tags in GlobalScript.callbackTags)
        {
            GlobalScript.SetTagData(tags.cycle, (int)time);
        }

        // 表示セット
        cycle.text = $"Cycle Time : {(time % viewCycle)} msec";

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
    public void SetParameter(int No, int Cycle, string Server, int Port, string Database, string User, string Password, bool isClientMode, DataExchangeSetting dataExchange, List<InnerProcessSetting> innerSettings, List<UnitActionSetting> actionSettings)
    {
        SetParameter(No, Cycle, Server, Port, Database, User, Password, isClientMode, dataExchange);

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
        // 表示追加
        CreateCanvas();
    }

    /// <summary>
    /// キャンバス追加
    /// </summary>
    private void CreateCanvas()
    {
        if (uiObj == null)
        {
            // キャンバス取得
            var canvasObjs = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(d => d.name == "Canvas").ToList();
            canvaObj = canvasObjs.Count == 0 ? new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)) : canvasObjs[0];
            var prefabs = GlobalScript.LoadPrefabObject("Prefabs/Canvas", "ComInner");
            if (prefabs.Count > 0)
            {
                uiObj = Instantiate(prefabs[0]);
                uiObj.transform.SetParent(canvaObj.transform, false);
                timeScript = uiObj.AddComponent<CanvasMenuTimeScript>();
                timeScript.SetEvents();

                // コンポネント取得
                toggle = uiObj.GetComponentInChildren<Toggle>();
                slider = uiObj.GetComponentInChildren<Slider>();
                text = uiObj.GetComponentsInChildren<TextMeshProUGUI>().Where(d => d.name == "ComInnerText").ToList()[0];
                button = uiObj.GetComponentsInChildren<Button>().Where(d => d.name == "ComInnerButton").ToList()[0];
                input = uiObj.GetComponentsInChildren<TMP_InputField>().Where(d => d.name == "ComInnerInput").ToList()[0];
                cycle = uiObj.GetComponentsInChildren<TextMeshProUGUI>().Where(d => d.name == "ComInnerCycle").ToList()[0]; ;
                buttonPrev = uiObj.GetComponentsInChildren<Button>().Where(d => d.name == "ComInnerPrevButton").ToList()[0];
                buttonNext = uiObj.GetComponentsInChildren<Button>().Where(d => d.name == "ComInnerNextButton").ToList()[0];
                inputStep = uiObj.GetComponentsInChildren<TMP_InputField>().Where(d => d.name == "ComInnerStep").ToList()[0];

                // イベント登録
                toggle.onValueChanged.AddListener(toggle_onValueChanged);
                slider.onValueChanged.AddListener(slider_onValueChanged);
                button.onClick.AddListener(button_onClick);
                input.onValueChanged.AddListener(input_onValueChanged);
                buttonPrev.onClick.AddListener(buttonPrev_onClick);
                buttonNext.onClick.AddListener(buttonNext_onClick);
                inputStep.onValueChanged.AddListener(inputStep_onValueChanged);

                // 初期値セット
                slider.value = 1;
                slider.maxValue = 5;
                slider.minValue = 0;
                input.text = acts.Count > 0 ? acts[0].cycle.ToString() : "1000";
                inputStep.text = "10";
            }
        }
    }

    /// <summary>
    /// トグル変更イベント
    /// </summary>
    /// <param name="value"></param>
    private void toggle_onValueChanged(bool value)
    {
        isStop = value;
    }

    /// <summary>
    /// スライダー値変更イベント
    /// </summary>
    /// <param name="value"></param>
    private void slider_onValueChanged(float value)
    {
        timeRate = value;
        text.text = value.ToString("0.00");
    }

    /// <summary>
    /// ボタンクリックイベント
    /// </summary>
    private void button_onClick()
    {
        slider.value = 1;
    }

    /// <summary>
    /// 値取得
    /// </summary>
    /// <param name="text"></param>
    private void input_onValueChanged(string text)
    {
        int value = 0;
        if (int.TryParse(text, out value))
        {
            if (value <= 0)
            {
                input.text = acts.Count > 0 ? acts[0].cycle.ToString() : "1000";
            }
            else
            {
                viewCycle = value;
            }
        }
    }

    /// <summary>
    /// ボタンクリックイベント
    /// </summary>
    private void buttonPrev_onClick()
    {
        toggle.isOn = true;
        int value = 0;
        if (int.TryParse(inputStep.text, out value))
        {
            step = -value;
        }
    }

    /// <summary>
    /// ボタンクリックイベント
    /// </summary>
    private void buttonNext_onClick()
    {
        toggle.isOn = true;
        int value = 0;
        if (int.TryParse(inputStep.text, out value))
        {
            step = value;
        }
    }

    /// <summary>
    /// 値取得
    /// </summary>
    /// <param name="text"></param>
    private void inputStep_onValueChanged(string text)
    {
        int value = 0;
        if (int.TryParse(text, out value))
        {
            if (value <= 0)
            {
                inputStep.text = "1";
            }
        }
    }
}
