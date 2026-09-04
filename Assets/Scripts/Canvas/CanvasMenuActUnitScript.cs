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
    /// 段ボールの状態行を出しているか（毎フレームの更新対象を切り替える）
    /// </summary>
    private bool isCardboardRows = false;

    /// <summary>
    /// クリックで選ばれた段ボールの個体。
    /// 同時に複数の段ボールが流れるため、クリックした個体の時間を表示する
    /// </summary>
    private CardboardScript selectedCardboard;

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
    /// コンベアスクリプト
    /// </summary>
    private ConveyorScript conveyorScript;

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
    /// クリックされた段ボールの個体を表示対象にする。
    /// 同時に複数流れるため、稼働中の先頭ではなくクリックした個体を優先して表示する
    /// </summary>
    public void SelectCardboard(CardboardScript cbs)
    {
        selectedCardboard = cbs;
    }

    /// <summary>
    /// 段ボールの製函状態を表示行へ反映する。ワーク未生成のときは待機表示にする
    /// </summary>
    private void UpdateCardboardRows()
    {
        // クリックで指定された個体を優先。破棄／非稼働になったら稼働中の個体へ戻す
        var cbs = ((selectedCardboard != null) && selectedCardboard.gameObject.activeInHierarchy)
            ? selectedCardboard
            : CardboardScript.FindActive(unitSetting);
        if (cbs == null)
        {
            actUnitInfos[0].txtStart.text = Lang.T("ワーク未生成");
            actUnitInfos[0].txtStart.color = Color.gray;
            actUnitInfos[0].txtEnd.text = "-";
            actUnitInfos[1].txtStart.text = "-";
            actUnitInfos[1].txtEnd.text = "-";
            return;
        }
        actUnitInfos[0].txtStart.text = cbs.PlayHead.ToString("0") + " ms";
        actUnitInfos[0].txtStart.color = cbs.IsWaiting ? Color.red : Color.blue;
        actUnitInfos[0].txtEnd.text = cbs.IsWaiting
            ? Lang.T("待機") + " " + cbs.WaitTag + " @" + cbs.WaitTime.ToString("0")
            : Lang.T("進行中");
        actUnitInfos[1].txtStart.text = cbs.CheckPointCount == 0
            ? Lang.T("なし（従来動作）")
            : cbs.CheckPointIndex + " / " + cbs.CheckPointCount;
        actUnitInfos[1].txtEnd.text = cbs.CheckPointCount == 0 ? "-" : Lang.T("通過数");
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void Update()
    {
        base.Update();
        if (isCardboardRows && (unitSetting != null) && (actUnitInfos.Count >= 2))
        {
            // 段ボールの製函状態。unitSetting.actionSetting が null なので、
            // それを参照する下の処理より前に独立して扱う
            UpdateCardboardRows();
        }
        else if (unitSetting != null)
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
                if (unitSetting.actionSetting.isInternal || unitSetting.actionSetting.isActionTable)
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
                else if (unitSetting.actionSetting.isConveyer && (conveyorScript != null))
                {
                    for (var i = 0; i < actUnitInfos.Count; i++)
                    {
                        var act = actUnitInfos[i];
                        if (i == 0)
                        {
                            // 現在速度行
                            act.txtStart.text = conveyorScript.IsMoving ? "Run" : "Stop";
                            act.txtStart.color = conveyorScript.IsMoving ? Color.blue : Color.red;
                            act.txtEnd.text = conveyorScript.CurrentSpeedMmSec.ToString("0.0");
                        }
                        else
                        {
                            // 速度行（タグなし=常時ON）
                            var on = string.IsNullOrEmpty(act.devStart) || (GetTagValue(act.devStart, ref act.tagStart) == 1);
                            act.txtStart.color = on ? Color.blue : Color.red;
                        }
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
        // 行の意味づけもリセットする（別ユニットを選び直したときに前の表示が残らないように）
        isCardboardRows = false;
        selectedCardboard = null;
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
    /// 動作行を生成する
    /// </summary>
    private ActUnitInfo CreateActRow()
    {
        var actUnit = Instantiate(actUnitContents);
        actUnit.transform.SetParent(actUnitContentsActList.transform);
        ((RectTransform)actUnit.transform).anchoredPosition = new Vector3(0, -30 * actUnitInfos.Count, 0);
        actUnit.SetActive(true);
        return new ActUnitInfo
        {
            actObject = actUnit,
            txtTarget = actUnit.GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "TxtTarget"),
            txtStart = actUnit.GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "TxtStartTag"),
            txtEnd = actUnit.GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "TxtEndTag"),
        };
    }

    /// <summary>
    /// ドロップダウン更新
    /// </summary>
    private void SetOptions()
    {
        var list = new List<string>();
        dropDown.ClearOptions();
        list.Add("ユニット名");
        // 段ボールは actionSetting を持たず、さらに ParameterLoader.CreateUnitObject で
        // unitSettings から除去される（ユニットオブジェクトを作らないため）。
        // 退避しておいたユニット定義を足して走査する
        var candidates = unitSettings.FindAll(d => (d.actionSetting != null) || CardboardScript.HasUnit(d));
        foreach (var cb in CardboardScript.UnitDefs)
        {
            if ((cb != null) && !candidates.Contains(cb))
            {
                candidates.Add(cb);
            }
        }
        foreach (var unitSetting in candidates)
        {
            if (CardboardScript.HasUnit(unitSetting))
            {
                // 段ボール（製函の再生時間とチェックポイントを見るため一覧に出す）
                // ※actionSetting が null のことがあるので、他の判定より先に見る
            }
            else if (unitSetting.actionSetting.isInternal)
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
            else if (unitSetting.actionSetting.isConveyer)
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
        // ※ドロップダウンでユニットを選んだ結果としてモデルが選択された場合はクリアしない。
        //   クリアすると OnValueChanged(0) が再入し、直前に作った表示行を全部消してしまう
        //   （ドロップダウン選択 → モデル選択 → SelectParts → 選択クリア の循環）
        if (!isSelectProcess)
        {
            dropDown.value = 0;
        }
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
        // 行の意味づけもリセットする（別ユニットを選び直したときに前の表示が残らないように）
        isCardboardRows = false;
        selectedCardboard = null;
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
        conveyorScript = null;
        if (index < 0)
        {
            unitSetting = null;
        }
        else
        {
            var selectedName = dropDown.options[index].text;
            // 段ボールは unitSettings から除去されているので退避リストからも探す
            unitSetting = unitSettings.Find(d => d.name == selectedName)
                ?? CardboardScript.UnitDefs.Find(d => d.name == selectedName);
        }
        if ((unitSetting != null) && CardboardScript.HasUnit(unitSetting))
        {
            // 段ボール（製函の再生時間とチェックポイントの消化状況）。
            // ※段ボールは unitObject も actionSetting も null なので、
            //   それらを参照する下の処理より前に、独立した分岐として扱う
            var now = CreateActRow();
            now.txtTarget.text = Lang.T("現在時間");
            now.txtStart.text = "-";
            now.txtEnd.text = "-";
            actUnitInfos.Add(now);
            var cp = CreateActRow();
            cp.txtTarget.text = Lang.T("チェックポイント");
            cp.txtStart.text = "-";
            cp.txtEnd.text = "-";
            actUnitInfos.Add(cp);
            isCardboardRows = true;
        }
        else if ((unitSetting != null) && (unitSetting != prvSelectedUnit))
        {
            var mi = unitSetting.unitObject.GetComponent<AxisMotionBase>();
            exScript = mi == null ? null : mi.exScript;
            if (unitSetting.actionSetting.isInternal || unitSetting.actionSetting.isActionTable)
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
            else if (unitSetting.actionSetting.isConveyer)
            {
                // コンベア（先頭行=現在速度、以降=速度テーブルのタグ＋設定速度[mm/sec]）
                conveyorScript = unitSetting.moveObject == null ? null : unitSetting.moveObject.GetComponent<ConveyorScript>();
                if ((conveyorScript != null) && (conveyorScript.Setting != null))
                {
                    var now = CreateActRow();
                    now.txtTarget.text = Lang.T("現在速度");
                    now.txtStart.text = "Stop";
                    now.txtEnd.text = "0.0";
                    actUnitInfos.Add(now);
                    var i = 0;
                    foreach (var spd in conveyorScript.Setting.speeds)
                    {
                        i++;
                        var row = CreateActRow();
                        row.devStart = spd.tag;
                        var startText = Lang.T("常時ON");
                        if (!string.IsNullOrEmpty(spd.tag))
                        {
                            GetTagValue(row.devStart, ref row.tagStart);
                            var startDev = row.tagStart == null ? "none" : row.tagStart.Device;
                            startText = startDev + " / " + spd.tag;
                            startText = startText.Length > 20 ? startText.Substring(0, 18) + ".." : startText;
                        }
                        row.txtTarget.text = Lang.T("速度") + i;
                        row.txtStart.text = startText;
                        row.txtEnd.text = (spd.spd * 1000f).ToString("0.0");
                        actUnitInfos.Add(row);
                    }
                }
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
        // モデル選択の条件は「ユニットが選ばれている」ことだけ。
        // ※以前は actUnitInfos / pointsInfos の件数も条件だったが、pointsInfos はリニア機構の
        //   点情報でしか埋まらないため、リニア以外のユニットでは選択処理ごとスキップされていた。
        // ※isSelectProcess は「モデルクリック→ドロップダウン設定→再選択」のループ防止なので残す。
        if ((unitSetting != null) && !isSelectProcess)
        {
            // モデル選択が SelectParts 経由でドロップダウンを戻すのを防ぐ。
            // 例外が出ても必ず戻す（立てたままだと次回の選択が無反応になる）
            isSelectProcess = true;
            try
            {
                //オブジェクト選択
                // First() は該当なしで例外を投げる（直後のnullチェックが効かず、以降の処理が中断していた）
                var obj = GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None).FirstOrDefault(d => d.name == unitSetting.name);
                var target = obj != null ? obj.gameObject : unitSetting.unitObject;
                if (target != null)
                {
                    menuInfoScript.SetAssemblyObject(target);
                }
                else
                {
                    Debug.LogWarning($"[ActUnit] ユニット名 '{unitSetting.name}' に一致するオブジェクトが見つかりません（モデル選択をスキップ）");
                }
            }
            finally
            {
                isSelectProcess = false;
            }
        }
    }
}