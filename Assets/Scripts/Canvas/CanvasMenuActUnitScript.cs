using Meta.XR.InputActions;
using Parameters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CanvasMenuActUnitScript : CanvasMenuBaseScript
{

    private class ActUnitInfo
    {
        public GameObject actObject;
        public TextMeshProUGUI txtTarget;
        public TextMeshProUGUI txtStart;
        public TextMeshProUGUI txtEnd;
        public TagInfo tagStart;
        public TagInfo tagEnd;
        public string devStart;
        public string devEnd;
    }

    private CanvasMenuInfoScript menuInfoScript = null;

    /// <summary>
    /// ドロップダウン
    /// </summary>
    private TMP_Dropdown dropDown;

    /// <summary>
    /// コンテンツベース
    /// </summary>
    private GameObject actUnitContents;

    /// <summary>
    /// コンテンツベース
    /// </summary>
    private GameObject actUnitContentsActList;

    /// <summary>
    /// 位置X
    /// </summary>
    private TextMeshProUGUI txtPosX;

    /// <summary>
    /// 位置Y
    /// </summary>
    private TextMeshProUGUI txtPosY;

    /// <summary>
    /// 位置Z
    /// </summary>
    private TextMeshProUGUI txtPosZ;

    /// <summary>
    /// 角度X
    /// </summary>
    private TextMeshProUGUI txtAngX;

    /// <summary>
    /// 角度Y
    /// </summary>
    private TextMeshProUGUI txtAngY;

    /// <summary>
    /// 角度Z
    /// </summary>
    private TextMeshProUGUI txtAngZ;

    /// <summary>
    /// 動作ユニット
    /// </summary>
    private List<ActUnitInfo> actUnitInfos = new();

    /// <summary>
    /// ユニット設定
    /// </summary>
    private List<UnitSetting> unitSettings = new();

    /// <summary>
    /// 選択ユニット
    /// </summary>
    private UnitSetting? prvSelectedUnit = null;

    /// <summary>
    /// ユニット選択処理
    /// </summary>
    private bool isSelectProcess = false;

    /// <summary>
    /// 拡張機構スクリプト
    /// </summary>
    private ExMechScript exScript;

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        // オブジェクト取得
        dropDown = GetComponentsInChildren<TMP_Dropdown>().ToList().Find(d => d.name == "DropUnitName");
        actUnitContents = GetComponentsInChildren<Transform>(true).ToList().Find(d => d.name == "ActUnitContents").gameObject;
        actUnitContentsActList= GetComponentsInChildren<Transform>(true).ToList().Find(d => d.name == "ActUnitContentsActList").gameObject;

        txtPosX = GetComponentsInChildren<TextMeshProUGUI>(true).ToList().Find(d => d.name == "TxtPosX");
        txtPosY = GetComponentsInChildren<TextMeshProUGUI>(true).ToList().Find(d => d.name == "TxtPosY");
        txtPosZ = GetComponentsInChildren<TextMeshProUGUI>(true).ToList().Find(d => d.name == "TxtPosZ");
        txtAngX = GetComponentsInChildren<TextMeshProUGUI>(true).ToList().Find(d => d.name == "TxtAngX");
        txtAngY = GetComponentsInChildren<TextMeshProUGUI>(true).ToList().Find(d => d.name == "TxtAngY");
        txtAngZ = GetComponentsInChildren<TextMeshProUGUI>(true).ToList().Find(d => d.name == "TxtAngZ");

        menuInfoScript = FindObjectsByType<CanvasMenuInfoScript>(FindObjectsSortMode.None).ToList()[0];
    }

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Start()
    {
        base.Start();
    }

    /// <summary>
    /// 有効時
    /// </summary>
    protected override void OnEnable()
    {
        base.OnEnable();
    }

    /// <summary>
    /// 無効時
    /// </summary>
    protected override void OnDisable()
    {
        base.OnDisable();
        dropDown.value = 0;
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void Update()
    {
        base.Update();
        if (unitSetting != null)
        {
            if (unitSetting.actionSetting.isInternal)
            {
                foreach (var act in actUnitInfos)
                {
                    act.txtStart.color = GetTagValue(act.devStart, ref act.tagStart) == 1 ? Color.blue : Color.red;
                    act.txtEnd.color = GetTagValue(act.devEnd, ref act.tagEnd) == 1 ? Color.blue : Color.red;
                }
            }
            else if (unitSetting.actionSetting.isExternal)
            {
                foreach (var act in actUnitInfos)
                {
                    act.txtEnd.text = GetTagValue(act.devStart, ref act.tagStart).ToString();
                }
            }
            if (unitSetting.moveObject != null)
            {
                if (exScript != null)
                {
                    txtPosX.text = exScript.NowPos.x.ToString("0.000");
                    txtPosY.text = exScript.NowPos.y.ToString("0.000");
                    txtPosZ.text = exScript.NowPos.z.ToString("0.000");
                    txtAngX.text = exScript.NowAngle.x.ToString("0.000");
                    txtAngY.text = exScript.NowAngle.y.ToString("0.000");
                    txtAngZ.text = exScript.NowAngle.z.ToString("0.000");
                }
                else
                {
                    txtPosX.text = unitSetting.moveObject.transform.localPosition.x.ToString("0.000");
                    txtPosY.text = unitSetting.moveObject.transform.localPosition.y.ToString("0.000");
                    txtPosZ.text = unitSetting.moveObject.transform.localPosition.z.ToString("0.000");
                    txtAngX.text = unitSetting.moveObject.transform.localEulerAngles.x.ToString("0.000");
                    txtAngY.text = unitSetting.moveObject.transform.localEulerAngles.y.ToString("0.000");
                    txtAngZ.text = unitSetting.moveObject.transform.localEulerAngles.z.ToString("0.000");
                }
            }
            else
            {
                txtPosX.text = "---";
                txtPosY.text = "---";
                txtPosZ.text = "---";
                txtAngX.text = "---";
                txtAngY.text = "---";
                txtAngZ.text = "---";
            }
        }
        else
        {
            txtPosX.text = "---";
            txtPosY.text = "---";
            txtPosZ.text = "---";
            txtAngX.text = "---";
            txtAngY.text = "---";
            txtAngZ.text = "---";
        }
    }

    /// <summary>
    /// イベントセット
    /// </summary>
    public void SetEvents(List<UnitSetting> unitSettings)
    {
        // キャンパス削除
        foreach (var info in actUnitInfos)
        {
            Destroy(info.actObject);
        }
        actUnitInfos.Clear();
        this.unitSettings = null;
        base.SetEvents();

        this.unitSettings = unitSettings;

        // ドロップダウン初期化
        SetOptions();

        dropDown.onValueChanged.AddListener(OnValueChanged);

        // 選択クリア
        dropDown.value = 0;
    }

    /// <summary>
    /// ドロップダウン更新
    /// </summary>
    private void SetOptions()
    {
        var list = new List<string>();
        dropDown.ClearOptions();
        list.Add("ユニット名");
        foreach (var unitSetting in unitSettings.FindAll(d => d.actionSetting != null))
        {
            if (unitSetting.actionSetting.isInternal)
            {
            }
            else if (unitSetting.actionSetting.isExternal)
            {
            }
            else if (unitSetting.actionSetting.isRobo)
            {
            }
            else if (unitSetting.actionSetting.isActionTable)
            {
            }
            else
            {
                continue;
            }
            list.Add(unitSetting.name);
        }
        dropDown.AddOptions(list);
    }

    /// <summary>
    /// イベントリセット
    /// </summary>
    public override void ResetEvents()
    {
        base.ResetEvents();
        dropDown.onValueChanged.RemoveAllListeners();
    }

    /// <summary>
    /// ユニット選択
    /// </summary>
    /// <param name="target"></param>
    public void SelectUnit(string target)
    {
        int index = dropDown.options.FindIndex(o => o.text == target);
        if (index >= 0)
        {
            isSelectProcess = true;
            dropDown.value = index;
            dropDown.RefreshShownValue();
            isSelectProcess = false;
        }
    }

    /// <summary>
    /// パーツ選択
    /// </summary>
    /// <param name="parts"></param>
    public void SelectParts(GameObject parts)
    {
        // 選択クリア
        dropDown.value = 0;
        // 位置更新
        txtPosX.text = parts.transform.localPosition.x.ToString("0.000");
        txtPosY.text = parts.transform.localPosition.y.ToString("0.000");
        txtPosZ.text = parts.transform.localPosition.z.ToString("0.000");
        txtAngX.text = parts.transform.localEulerAngles.x.ToString("0.000");
        txtAngY.text = parts.transform.localEulerAngles.y.ToString("0.000");
        txtAngZ.text = parts.transform.localEulerAngles.z.ToString("0.000");
        actUnitContentsActList.GetComponent<RectTransform>().sizeDelta = new Vector2(600, 30 * actUnitInfos.Count);
    }

    /// <summary>
    /// 値変更イベント
    /// </summary>
    /// <param name="index"></param>
    void OnValueChanged(int index)
    {
        // キャンパス削除
        foreach (var info in actUnitInfos)
        {
            Destroy(info.actObject);
        }
        actUnitInfos.Clear();
        prvSelectedUnit = unitSetting;
        exScript = null;
        if (index < 0)
        {
            unitSetting = null;
        }
        else
        {
            unitSetting = unitSettings.Find(d => d.name == dropDown.options[index].text);
        }
        if ((unitSetting != null) && (unitSetting != prvSelectedUnit))
        {
            var mi = unitSetting.unitObject.GetComponent<AxisMotionBase>();
            exScript = mi == null ? null : mi.exScript;
            if (unitSetting.actionSetting.isInternal)
            {
                // 出力モード切替チェック
                var i = 0;
                foreach (var act in unitSetting.actionSetting.actions)
                {
                    // 動作テーブル
                    if (act.start != "")// && (act.end != ""))
                    {
                        i++;
                        var actUnit = Instantiate(actUnitContents);
                        actUnit.transform.SetParent(actUnitContentsActList.transform);
                        ((RectTransform)actUnit.transform).anchoredPosition = new Vector3(0, - 30 * actUnitInfos.Count, 0);
                        actUnit.SetActive(true);
                        var txtTarget = actUnit.GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "TxtTarget");
                        var txtStart = actUnit.GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "TxtStartTag");
                        var txtEnd = actUnit.GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "TxtEndTag");
                        var actInfo = new ActUnitInfo
                        {
                            actObject = actUnit,
                            txtTarget = txtTarget,
                            txtStart = txtStart,
                            txtEnd = txtEnd,
                            devStart = act.start,
                            devEnd = act.end
                        };
                        GetTagValue(actInfo.devStart, ref actInfo.tagStart);
                        GetTagValue(actInfo.devEnd, ref actInfo.tagEnd);
                        var startDev = actInfo.tagStart == null ? "none" : actInfo.tagStart.Device;
                        var endDev = actInfo.tagEnd == null ? "none" : actInfo.tagEnd.Device;
                        var starText = startDev + " / " + act.start;
                        var endText = endDev + " / " + act.end;
                        starText = starText.Length > 20 ? starText.Substring(0, 18) + ".." : starText;
                        endText = endText.Length > 20 ? endText.Substring(0, 18) + ".." : endText;
                        txtTarget.text = act.endName == "" ? "Pos" + i : act.endName;
                        txtStart.text = startDev + " / " + starText;
                        txtEnd.text = endDev + " / " + endText;
                        actUnitInfos.Add(actInfo);
                    }
                }
            }
            else if (unitSetting.actionSetting.isExternal)
            {
                var actUnit = Instantiate(actUnitContents);
                actUnit.transform.parent = actUnitContentsActList.transform;
                ((RectTransform)actUnit.transform).anchoredPosition = new Vector3(0, -30 * actUnitInfos.Count, 0);
                actUnit.SetActive(true);
                var txtTarget = actUnit.GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "TxtTarget");
                var txtStart = actUnit.GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "TxtStartTag");
                var txtEnd = actUnit.GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "TxtEndTag");
                var startTag = unitSetting.actionSetting.tag;
                var actInfo = new ActUnitInfo
                {
                    actObject = actUnit,
                    txtTarget = txtTarget,
                    txtStart = txtStart,
                    txtEnd = txtEnd,
                    devStart = startTag
                };
                GetTagValue(actInfo.devStart, ref actInfo.tagStart);
                var startDev = actInfo.tagStart == null ? "none" : actInfo.tagStart.Device;
                txtTarget.text = "external";
                txtStart.text = startDev + " / " + startTag;
                txtEnd.text = "0";
                actUnitInfos.Add(actInfo);
            }
        }
        actUnitContentsActList.GetComponent<RectTransform>().sizeDelta = new Vector2(600, 30 * actUnitInfos.Count);
        if ((unitSetting != null) && (actUnitInfos.Count > 0) && !isSelectProcess)
        {
            //オブジェクト選択
            var obj = GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(d => d.name == unitSetting.name).First();
            if (obj != null)
            {
                menuInfoScript.SetAssemblyObject(((Transform)obj).gameObject);
            }
        }
    }
}
