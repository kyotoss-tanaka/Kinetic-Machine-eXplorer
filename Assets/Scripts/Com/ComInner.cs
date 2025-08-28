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
        /// �@��
        /// </summary>
        public string name;
        /// <summary>
        /// �@��
        /// </summary>
        public int index;
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
    /// ���Ԓ�~
    /// </summary>
    [SerializeField]
    private bool isStop = false;

    /// <summary>
    /// ���Ԕ䗦
    /// </summary>
    [SerializeField]
    public float timeRate = 1;

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

    /// <summary>
    /// ���݂̎���
    /// </summary>
    private long elapsedMilliseconds = 0;

    /// <summary>
    /// �O��̎���
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
    /// �\���p
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
    /// �\���T�C�N��
    /// </summary>
    private int viewCycle = 1000;

    /// <summary>
    /// �R�}����X�e�b�v
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
    /// �X�V����
    /// </summary>
    protected override void FixedUpdate()
    {
        if (GlobalScript.isLoaded)
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
        else
        {
            swTiming.Restart();
        }
    }

    /// <summary>
    /// �폜��
    /// </summary>
    protected override void OnDestroy()
    {
        base.OnDestroy();

        // �\�����폜���Ă���
        toggle.onValueChanged.RemoveAllListeners();
        slider.onValueChanged.RemoveAllListeners();
        button.onClick.RemoveAllListeners();
        input.onValueChanged.RemoveAllListeners();
        Destroy(uiObj);
    }

    /// <summary>
    /// �f�[�^�X�V
    /// </summary>
    public override void RenewData()
    {
        base.RenewData();
        var sw = new System.Diagnostics.Stopwatch();
        sw.Start();

        // �o�ߎ��ԍ쐬
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

        // �\���Z�b�g
        cycle.text = $"Cycle Time : {(time % viewCycle)} msec";

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
                    // ON�������M���҂�
                    if (GlobalScript.tagDatas[Name][act.mechId][output].Value == 1)
                    {
                        offTags.Add(GlobalScript.tagDatas[Name][act.mechId][input]);
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
                                onTags.Add(GlobalScript.tagDatas[Name][act.mechId][input]);
                            }
                        }
                        else
                        {
                            // ���]����
                            if ((trg >= act.prvCycle) || (trg <= act.nowCycle))
                            {
                                onTags.Add(GlobalScript.tagDatas[Name][act.mechId][input]);
                            }
                        }
                    }
                }
                act.prvCycle = act.nowCycle;
            }
            // �ꊇ�o��
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
                // �^�O�쐬
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
        // �\���ǉ�
        CreateCanvas();
    }

    /// <summary>
    /// �L�����o�X�ǉ�
    /// </summary>
    private void CreateCanvas()
    {
        if (uiObj == null)
        {
            // �L�����o�X�擾
            var canvasObjs = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(d => d.name == "Canvas").ToList();
            canvaObj = canvasObjs.Count == 0 ? new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)) : canvasObjs[0];
            var prefabs = GlobalScript.LoadPrefabObject("Prefabs/Canvas", "ComInner");
            if (prefabs.Count > 0)
            {
                uiObj = Instantiate(prefabs[0]);
                uiObj.transform.SetParent(canvaObj.transform, false);
                timeScript = uiObj.AddComponent<CanvasMenuTimeScript>();
                timeScript.SetEvents();

                // �R���|�l���g�擾
                toggle = uiObj.GetComponentInChildren<Toggle>();
                slider = uiObj.GetComponentInChildren<Slider>();
                text = uiObj.GetComponentsInChildren<TextMeshProUGUI>().Where(d => d.name == "ComInnerText").ToList()[0];
                button = uiObj.GetComponentsInChildren<Button>().Where(d => d.name == "ComInnerButton").ToList()[0];
                input = uiObj.GetComponentsInChildren<TMP_InputField>().Where(d => d.name == "ComInnerInput").ToList()[0];
                cycle = uiObj.GetComponentsInChildren<TextMeshProUGUI>().Where(d => d.name == "ComInnerCycle").ToList()[0]; ;
                buttonPrev = uiObj.GetComponentsInChildren<Button>().Where(d => d.name == "ComInnerPrevButton").ToList()[0];
                buttonNext = uiObj.GetComponentsInChildren<Button>().Where(d => d.name == "ComInnerNextButton").ToList()[0];
                inputStep = uiObj.GetComponentsInChildren<TMP_InputField>().Where(d => d.name == "ComInnerStep").ToList()[0];

                // �C�x���g�o�^
                toggle.onValueChanged.AddListener(toggle_onValueChanged);
                slider.onValueChanged.AddListener(slider_onValueChanged);
                button.onClick.AddListener(button_onClick);
                input.onValueChanged.AddListener(input_onValueChanged);
                buttonPrev.onClick.AddListener(buttonPrev_onClick);
                buttonNext.onClick.AddListener(buttonNext_onClick);
                inputStep.onValueChanged.AddListener(inputStep_onValueChanged);

                // �����l�Z�b�g
                slider.value = 1;
                slider.maxValue = 5;
                slider.minValue = 0;
                input.text = acts.Count > 0 ? acts[0].cycle.ToString() : "1000";
                inputStep.text = "10";
            }
        }
    }

    /// <summary>
    /// �g�O���ύX�C�x���g
    /// </summary>
    /// <param name="value"></param>
    private void toggle_onValueChanged(bool value)
    {
        isStop = value;
    }

    /// <summary>
    /// �X���C�_�[�l�ύX�C�x���g
    /// </summary>
    /// <param name="value"></param>
    private void slider_onValueChanged(float value)
    {
        timeRate = value;
        text.text = value.ToString("0.00");
    }

    /// <summary>
    /// �{�^���N���b�N�C�x���g
    /// </summary>
    private void button_onClick()
    {
        slider.value = 1;
    }

    /// <summary>
    /// �l�擾
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
    /// �{�^���N���b�N�C�x���g
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
    /// �{�^���N���b�N�C�x���g
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
    /// �l�擾
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
