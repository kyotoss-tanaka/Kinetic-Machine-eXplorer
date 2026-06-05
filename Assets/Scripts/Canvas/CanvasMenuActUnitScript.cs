using Parameters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using static UnityEngine.Analytics.IAnalytic;

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

    private class LinearMoverInfo
    {
        public GameObject moverObject;
        public TextMeshProUGUI txtId;
        public TextMeshProUGUI txtPos;
        public TextMeshProUGUI txtStat;
        public TextMeshProUGUI txtProcess;
        public MotionLinear.MoverInfo mover;
    }

    private class LinearPointInfo
    {
        public GameObject pointObject;
        public TextMeshProUGUI txtTarget;
        public TextMeshProUGUI txtStart;
        public TextMeshProUGUI txtProcess;
        public TextMeshProUGUI txtEnd;
        public TextMeshProUGUI txtCycle;
        public TagInfo tagStart;
        public TagInfo tagProcess;
        public TagInfo tagEnd;
        public string devStart;
        public string devProcess;
        public string devEnd;
        public MotionLinear.PointInfo point;
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
    /// 動作ユニット
    /// </summary>
    private List<LinearMoverInfo> moverInfos = new();

    /// <summary>
    /// 動作ユニット
    /// </summary>
    private List<LinearPointInfo> pointsInfos = new();

    /// <summary>
    /// ユニット設定
    /// </summary>
    private List<UnitSetting> unitSettings = new();

#nullable enable
    /// <summary>
    /// 選択ユニット
    /// </summary>
    private UnitSetting? prvSelectedUnit = null;
#nullable disable

    /// <summary>
    /// ユニット選択処理
    /// </summary>
    private bool isSelectProcess = false;

    /// <summary>
    /// 拡張機構スクリプト
    /// </summary>
    private ExMechScript exScript;

    /// <summary>
    /// 動作ユニット情報
    /// </summary>
    private GameObject uiLinearInfo;

    /// <summary>
    /// ポイントコンテンツ
    /// </summary>
    private GameObject linearContentsPoint;

    /// <summary>
    /// ポイントコンテンツリスト
    /// </summary>
    private GameObject linearContentsPointList;

    /// <summary>
    /// ポイントコンテンツリスト
    /// </summary>
    private GameObject linearScrollPoint;

    /// <summary>
    /// ポイントコンテンツリスト
    /// </summary>
    private ScrollRect linearScrollRectPoint;

    /// <summary>
    /// ムーバーコンテンツタイトル
    /// </summary>
    private GameObject linearContentsMoverTitle;

    /// <summary>
    /// ムーバーコンテンツリスト
    /// </summary>
    private GameObject linearContentsMoverList;

    /// <summary>
    /// ムーバーコンテンツリスト
    /// </summary>
    private GameObject linearScrollMover;

    /// <summary>
    /// ムーバーコンテンツリスト
    /// </summary>
    private ScrollRect linearScrollRectMover;

    /// <summary>
    /// ムーバーコンテンツ
    /// </summary>
    private GameObject linearContentsMover;

    /// <summary>
    /// リニアスクリプト
    /// </summary>
    private MotionLinear motionLinear;

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

        // リニアメニュー
        var linearUnit = GlobalScript.LoadPrefabObject("Prefabs/Canvas", "LinearInfo");
        if (linearUnit.Count > 0)
        {
            uiLinearInfo = Instantiate(linearUnit[0]);
            uiLinearInfo.transform.SetParent(transform.parent, false);
            uiLinearInfo.AddComponent<CanvasMenuBaseScript>();
            uiLinearInfo.SetActive(false);
            linearContentsMoverTitle = uiLinearInfo.GetComponentsInChildren<Transform>(true).ToList().Find(d => d.name == "LinearContentsMoverTitle").gameObject;
            linearContentsMoverList = uiLinearInfo.GetComponentsInChildren<Transform>(true).ToList().Find(d => d.name == "LinearScrallContentMover").gameObject;
            linearScrollMover = uiLinearInfo.GetComponentsInChildren<Transform>(true).ToList().Find(d => d.name == "LinearScrallMover").gameObject;
            linearContentsMover = uiLinearInfo.GetComponentsInChildren<Transform>(true).ToList().Find(d => d.name == "LinearContentsMover").gameObject;
            linearContentsPoint = uiLinearInfo.GetComponentsInChildren<Transform>(true).ToList().Find(d => d.name == "LinearContentsPoint").gameObject;
            linearContentsPointList = uiLinearInfo.GetComponentsInChildren<Transform>(true).ToList().Find(d => d.name == "LinearScrallContentPoint").gameObject;
            linearScrollPoint = uiLinearInfo.GetComponentsInChildren<Transform>(true).ToList().Find(d => d.name == "LinearScrallPoint").gameObject;
            linearContentsMover.SetActive(false);
            linearContentsPoint.SetActive(false);
            linearScrollRectPoint = linearScrollPoint.GetComponent<ScrollRect>();
            linearScrollRectMover = linearScrollMover.GetComponent<ScrollRect>();
        }
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

        // ドロップダウン初期化
        SetOptions();
    }

    /// <summary>
    /// 無効時
    /// </summary>
    protected override void OnDisable()
    {
        base.OnDisable();
        dropDown.value = 0;
        if (uiLinearInfo != null)
        {
            uiLinearInfo.SetActive(false);
        }
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void Update()
    {
        base.Update();
        if (unitSetting != null)
        {
            if (unitSetting.actionSetting.isLinear)
            {
                for (var i = 0; i < moverInfos.Count; i++)
                {
                    moverInfos[i].txtPos.text = motionLinear.movers[i].txtPos;
                    moverInfos[i].txtStat.text = motionLinear.movers[i].txtStatus;
                    moverInfos[i].txtProcess.text = motionLinear.movers[i].txtProcessTime;
                }
                for (var i = 0; i < pointsInfos.Count; i++)
                {
                    pointsInfos[i].txtStart.color = GetTagValue(pointsInfos[i].devStart, ref pointsInfos[i].tagStart) == 1 ? Color.blue : Color.red;
                    pointsInfos[i].txtProcess.color = GetTagValue(pointsInfos[i].devProcess, ref pointsInfos[i].tagProcess) == 1 ? Color.blue : Color.red;
                    pointsInfos[i].txtEnd.color = GetTagValue(pointsInfos[i].devEnd, ref pointsInfos[i].tagEnd) == 1 ? Color.blue : Color.red;
                    pointsInfos[i].txtCycle.text = motionLinear.points[i].txtCycle;
                }
            }
            else
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
        foreach (var mover in moverInfos)
        {
            Destroy(mover.moverObject);
        }
        moverInfos.Clear();
        foreach (var mover in pointsInfos)
        {
            Destroy(mover.pointObject);
        }
        pointsInfos.Clear();

        this.unitSettings = new();
        base.SetEvents();

        this.unitSettings = unitSettings;

        // ドロップダウン初期化
        SetOptions();

        dropDown.onValueChanged.AddListener(OnValueChanged);

        // 選択クリア
        dropDown.value = 0;

        // イベントセット
        linearScrollRectPoint.onValueChanged.AddListener(OnScrollPoint);
        linearScrollRectMover.onValueChanged.AddListener(OnScrollMover);
        UpdateItemsPoint(0);
        UpdateItemsMover(0);
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
            else if (unitSetting.actionSetting.isLinear)
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
        if (uiLinearInfo != null)
        {
            uiLinearInfo.SetActive(false);
        }
        // イベントリセット
        linearScrollRectPoint.onValueChanged.RemoveAllListeners();
        linearScrollRectMover.onValueChanged.RemoveAllListeners();
    }


    /// <summary>
    /// 位置スクロール
    /// </summary>
    /// <param name="scroll"></param>
    private void OnScrollPoint(Vector2 value)
    {
        float scrollY = linearScrollRectPoint.content.anchoredPosition.y;
        int startIndex = Mathf.FloorToInt(scrollY / 30);
        startIndex = Mathf.Clamp(startIndex, 0, pointsInfos.Count - 6);
        UpdateItemsPoint(startIndex);
    }

    /// <summary>
    /// 位置更新
    /// </summary>
    /// <param name="startIndex"></param>
    void UpdateItemsPoint(int startIndex)
    {
        for (int i = 0; i < pointsInfos.Count; i++)
        {
            if ((i > 6 + startIndex) || (i < startIndex))
            {
                pointsInfos[i].pointObject.SetActive(false);
                continue;
            }
            pointsInfos[i].pointObject.SetActive(true);
        }
    }

    /// <summary>
    /// ムーバースクロール
    /// </summary>
    /// <param name="scroll"></param>
    private void OnScrollMover(Vector2 value)
    {
        float scrollY = linearScrollRectMover.content.anchoredPosition.y;
        int startIndex = Mathf.FloorToInt(scrollY / 30);
        startIndex = Mathf.Clamp(startIndex, 0, moverInfos.Count - 6);
        UpdateItemsMover(startIndex);
    }

    /// <summary>
    /// ムーバー更新
    /// </summary>
    /// <param name="startIndex"></param>
    void UpdateItemsMover(int startIndex)
    {
        for (int i = 0; i < moverInfos.Count; i++)
        {
            if ((i > 6 + startIndex) || (i < startIndex))
            {
                moverInfos[i].moverObject.SetActive(false);
                continue;
            }
            moverInfos[i].moverObject.SetActive(true);
        }
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
        foreach (var mover in moverInfos)
        {
            Destroy(mover.moverObject);
        }
        moverInfos.Clear();
        foreach (var mover in pointsInfos)
        {
            Destroy(mover.pointObject);
        }
        pointsInfos.Clear();
        // 前回選択にセット
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
                        ((RectTransform)actUnit.transform).anchoredPosition = new Vector3(0, -30 * actUnitInfos.Count, 0);
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
                        txtStart.text = starText;
                        txtEnd.text = endText;
                        actUnitInfos.Add(actInfo);
                    }
                }
            }
            else if (unitSetting.actionSetting.isExternal)
            {
                // 外部デバイス
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
            else if (unitSetting.actionSetting.isLinear)
            {
                // リニア
                motionLinear = unitSetting.unitObject.GetComponentInChildren<MotionLinear>();
                if (motionLinear != null)
                {
                    var i = 0;
                    var offset = 0;
                    var maxHeight = 180;
                    foreach (var point in motionLinear.points)
                    {
                        i = motionLinear.points.IndexOf(point);
                        var p = Instantiate(linearContentsPoint);
                        p.transform.parent = linearContentsPointList.transform;
                        p.transform.localEulerAngles = linearContentsPoint.transform.localEulerAngles;
                        p.transform.localPosition = new Vector3(0, offset, 0);
                        var pi = new LinearPointInfo
                        {
                            txtTarget = p.GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "TxtTarget"),
                            pointObject = p,
                            txtStart = p.GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "TxtStartTag"),
                            txtProcess = p.GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "TxtProcessTag"),
                            txtEnd = p.GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "TxtEndTag"),
                            txtCycle = p.GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "TxtCycle"),
                            devStart = point.actTag,
                            devProcess = point.processTag,
                            devEnd = point.finTag,
                        };
                        GetTagValue(pi.devStart, ref pi.tagStart);
                        GetTagValue(pi.devProcess, ref pi.tagProcess);
                        GetTagValue(pi.devEnd, ref pi.tagEnd);
                        var startDev = pi.tagStart == null ? "none" : pi.tagStart.Device;
                        var processDev = pi.tagProcess == null ? "none" : pi.tagProcess.Device;
                        var endDev = pi.tagEnd == null ? "none" : pi.tagEnd.Device;
                        var starText = startDev + " / " + point.actTag;
                        var processText = processDev + " / " + point.processTag;
                        var endText = endDev + " / " + point.finTag;
                        pi.txtTarget.text = point.name.Length > 6 ? point.name.Substring(0, 5) + ".." : point.name;
                        pi.txtStart.text = starText.Length > 14 ? starText.Substring(0, 10) + ".." : starText;
                        pi.txtProcess.text = processText.Length > 14 ? processText.Substring(0, 10) + ".." : processText;
                        pi.txtEnd.text = endText.Length > 14 ? endText.Substring(0, 10) + ".." : endText;
                        pointsInfos.Add(pi);
                        p.SetActive(true);
                        offset -= 30;
                    }
                    if (-offset < maxHeight)
                    {
                        ((RectTransform)linearScrollPoint.transform).sizeDelta = new Vector2(800, -offset);
                        linearContentsMoverTitle.transform.localPosition = new Vector3(0, -60 + offset, 0);
                        linearScrollMover.transform.localPosition = new Vector2(0, -90 + offset);
                    }
                    else
                    {
                        ((RectTransform)linearScrollPoint.transform).sizeDelta = new Vector2(800, maxHeight);
                        linearContentsMoverTitle.transform.localPosition = new Vector3(0, -60 - maxHeight, 0);
                        linearScrollMover.transform.localPosition = new Vector2(0, -90 - maxHeight);
                    }
                    ((RectTransform)linearContentsPointList.transform).sizeDelta = new Vector2(800, -offset);
                    offset = 0;
                    foreach (var mover in motionLinear.movers)
                    {
                        i = motionLinear.movers.IndexOf(mover);
                        var m = Instantiate(linearContentsMover);
                        m.transform.parent = linearContentsMoverList.transform;
                        m.transform.localEulerAngles = linearContentsMover.transform.localEulerAngles;
                        m.transform.localPosition = new Vector3(0, offset, 0);
                        var txtId = m.GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "TxtId");
                        txtId.text = (i + 1).ToString();
                        moverInfos.Add(new LinearMoverInfo
                        {
                            mover = mover,
                            moverObject = m,
                            txtId = txtId,
                            txtPos = m.GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "TxtPos"),
                            txtStat = m.GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "TxtStat"),
                            txtProcess = m.GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "TxtProcess")
                        });
                        m.SetActive(true);
                        offset -= 30;
                    }
                    if (-offset < maxHeight)
                    {
                        ((RectTransform)linearScrollMover.transform).sizeDelta = new Vector2(800, -offset);
                    }
                    else
                    {
                        ((RectTransform)linearScrollMover.transform).sizeDelta = new Vector2(800, maxHeight);
                    }
                    ((RectTransform)linearContentsMoverList.transform).sizeDelta = new Vector2(800, -offset);
                }
            }
            uiLinearInfo.SetActive(unitSetting.actionSetting.isLinear);
        }
        else
        {
            uiLinearInfo.SetActive(false);
        }
        actUnitContentsActList.GetComponent<RectTransform>().sizeDelta = new Vector2(600, 30 * actUnitInfos.Count);
        if ((unitSetting != null) && (actUnitInfos.Count > 0) && (pointsInfos.Count > 0) && !isSelectProcess)
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