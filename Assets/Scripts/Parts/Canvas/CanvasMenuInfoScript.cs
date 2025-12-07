using Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.Windows;

public class CanvasMenuInfoScript : KssBaseScript
{
    // グローバル設定
    private GameObject globalSetting;

    // カメラ
    private CameraController cameraController = null;

    // 設定
    private List<UnitSetting> unitSettings = new();

    /// <summary>
    /// キャンバス
    /// </summary>
    private GameObject canvaObj;

    /// <summary>
    /// 各種ボタン
    /// </summary>
    private Button btnInner;
    private Button btnDirect;
    private Button btnMotion;
    private Button btnAsm;

    /// <summary>
    /// 内部タイマー
    /// </summary>
    private GameObject? uiInner;
    private CanvasMenuTimeScript timeScript;

    /// <summary>
    /// 直接通信
    /// </summary>
    private GameObject uiDirectCom;
    private GameObject directComContents;
    private List<GameObject> directComInfos = new();
    private CanvasMenuBaseScript directComScript;

    /// <summary>
    /// アセンブリ選択
    /// </summary>
    private CanvasMenuAssemblyScript assemblyScript;
    private GameObject uiAssembly;

    /// <summary>
    /// 動作ユニット情報
    /// </summary>
    private GameObject uiActUnitInfo;
//    private GameObject actUnitContents;
//    private List<GameObject> actUnitInfos = new();
    private CanvasMenuActUnitScript actUnitScript;

    /// <summary>
    /// 各種表示
    /// </summary>
    private bool visibleInner = false;
    private bool visibleDirect = false;
    private bool visibleMotion = false;
    private bool visibleAsm = false;

    /// <summary>
    /// 軸表示用
    /// </summary>
    private GameObject selectedObject;
    private GameObject axis;
    private bool isAxisVisible = true;

    #region 初期化処理
    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        // キャンバス作成
        CreateCanvas();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        InputManager.Instance.RegisterKey(Key.A, HandleKey);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        InputManager.Instance.UnregisterKey(Key.A, HandleKey);
    }

    /// <summary>
    /// 初期化処理
    /// </summary>
    private void Initialize()
    {
        // 設定
        globalSetting = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(d => d.name == "GlobalSetting").ToList()[0];

        //　各種ボタン
        btnInner = GetComponentsInChildren<Button>().ToList().Find(d => d.name == "BtnTime");
        btnDirect = GetComponentsInChildren<Button>().ToList().Find(d => d.name == "BntCom");
        btnMotion = GetComponentsInChildren<Button>().ToList().Find(d => d.name == "BntMotion");
        btnAsm = GetComponentsInChildren<Button>().ToList().Find(d => d.name == "BntAsm");

        // カメラ表示
        var cameraControllers = FindObjectsByType<CameraController>(FindObjectsSortMode.None).ToList();
        if (cameraControllers.Count > 0)
        {
            cameraController = cameraControllers[0];
        }

        // キャンパス削除
        foreach (var direct in directComInfos)
        {
            Destroy(direct);
        }
        directComInfos.Clear();

        // 直接通信
        var index = 0;
        foreach (var protocol in globalSetting.GetComponents<ComProtocolBase>().Where(d => d.IsDirect))
        {
            if (protocol.IsDirect)
            {
                var directComInfo = Instantiate(directComContents);
                directComInfo.transform.parent = uiDirectCom.transform;
                directComInfo.transform.localPosition = new Vector3(0, -50 - 20 * index, 0);
                directComInfo.SetActive(true);
                protocol.SetDirectCanvas(directComInfo);
                directComInfos.Add(directComInfo);
                index++;
            }
        }
        // キャンバス表示更新
        if (directComInfos.Count > 0)
        {
            uiDirectCom.GetComponent<RectTransform>().sizeDelta = new Vector2(500, 50 + 20 * directComInfos.Count);
        }

        // 動作ユニット表示
        timeScript.SetEvents();
        directComScript.SetEvents();
        actUnitScript.SetEvents(unitSettings);
        assemblyScript.SetEvents(uiActUnitInfo);

        // 有効/無効切り替え
        btnInner.interactable = timeScript.IsEnabled;
        btnDirect.interactable = index > 0;

        uiInner.SetActive(visibleInner && btnInner.interactable);
        uiDirectCom.SetActive(visibleDirect && btnDirect.interactable);
    }

    /// <summary>
    /// イベントセット
    /// </summary>
    public void SetEvents(List<UnitSetting> unitSettings)
    {
        this.unitSettings = unitSettings;
        Initialize();

        ResetEvents();
        btnInner.onClick.AddListener(btnInner_onClick);
        btnDirect.onClick.AddListener(btnDirect_onClick);
        btnMotion.onClick.AddListener(btnMotion_onClick);
        btnAsm.onClick.AddListener(btnAsm_onClick);
    }

    /// <summary>
    /// イベントリセット
    /// </summary>
    public void ResetEvents()
    {
        btnInner.onClick.RemoveAllListeners();
        btnDirect.onClick.RemoveAllListeners();
        btnMotion.onClick.RemoveAllListeners();
        btnAsm.onClick.RemoveAllListeners();
    }
    #endregion 初期化処理

    #region イベント
    /// <summary>
    /// タイマー表示切り替え
    /// </summary>
    private void btnInner_onClick()
    {
        visibleInner = !visibleInner && btnInner.interactable;
        uiInner.SetActive(visibleInner);

        SetButtonColor(btnInner, visibleInner);
    }

    /// <summary>
    /// 直接通信表示切り替え
    /// </summary>
    private void btnDirect_onClick()
    {
        visibleDirect = !visibleDirect && btnDirect.interactable;
        uiDirectCom.SetActive(visibleDirect);

        SetButtonColor(btnDirect, visibleDirect);
    }

    /// <summary>
    /// ユニット動作表示切り替え
    /// </summary>
    private void btnMotion_onClick()
    {
        visibleMotion = !visibleMotion && btnMotion.interactable;
        uiActUnitInfo.SetActive(visibleMotion);

        SetButtonColor(btnMotion, visibleMotion);
    }

    /// <summary>
    /// アセンブリ表示表示切り替え
    /// </summary>
    private void btnAsm_onClick()
    {
        visibleAsm = !visibleAsm && btnAsm.interactable;
        uiAssembly.SetActive(visibleAsm);

        SetButtonColor(btnAsm, visibleAsm);
    }

    /// <summary>
    /// アセンブリ表示切り替え
    /// </summary>
    public void btnAsm_Visible(bool visible)
    {
        if (visibleAsm != visible)
        {
            btnAsm_onClick();
        }
    }
    /// <summary>
    /// キーイベント
    /// </summary>
    /// <param name="key"></param>
    private void HandleKey(Key key, bool isCtrl, bool isShift)
    {
        if (key == Key.A)
        {
            // A 表示/非表示切り替え
            isAxisVisible = !isAxisVisible;
        }
    }
    #endregion イベント

    #region メソッド
    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void Update()
    {
        // 軸更新
        AxisUpdate();
    }

    /// <summary>
    /// ボタンの色セット
    /// </summary>
    /// <param name="button"></param>
    /// <param name="value"></param>
    private void SetButtonColor(Button button, bool value)
    {
        SetButtonColor(button, value ? Color.yellow : Color.white);
    }

    /// <summary>
    /// ボタンの色セット
    /// </summary>
    /// <param name="button"></param>
    /// <param name="color"></param>
    private void SetButtonColor(Button button, Color color)
    {
        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = color;
        button.colors = colors;
        button.targetGraphic.color = colors.normalColor;
    }

    /// <summary>
    /// キャンバス作成
    /// </summary>
    private void CreateCanvas()
    {
        // キャンバス取得
        var canvasObjs = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(d => d.name == "Canvas").ToList();
        canvaObj = canvasObjs.Count == 0 ? new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)) : canvasObjs[0];

        // 内部処理
        var inner = GlobalScript.LoadPrefabObject("Prefabs/Canvas", "ComInner");
        if (inner.Count > 0)
        {
            uiInner = Instantiate(inner[0]);
            uiInner.transform.SetParent(canvaObj.transform, false);
            timeScript = uiInner.AddComponent<CanvasMenuTimeScript>();
        }

        // 直接通信
        var direct = GlobalScript.LoadPrefabObject("Prefabs/Canvas", "DirectComInfo");
        if (direct.Count > 0)
        {
            uiDirectCom = Instantiate(direct[0]);
            uiDirectCom.transform.SetParent(canvaObj.transform, false);
            directComScript = uiDirectCom.AddComponent<CanvasMenuBaseScript>();
            directComContents = uiDirectCom.GetComponentsInChildren<Transform>(true).ToList().Find(d => d.name == "DirectComContents").gameObject;
        }

        // アセンブリ表示
        var asm = GlobalScript.LoadPrefabObject("Prefabs/Canvas", "AssemblySetting");
        if (asm.Count > 0)
        {
            uiAssembly = Instantiate(asm[0]);
            uiAssembly.transform.SetParent(canvaObj.transform, false);
            assemblyScript = uiAssembly.AddComponent<CanvasMenuAssemblyScript>();
        }

        // 動作確認
        var actUnit = GlobalScript.LoadPrefabObject("Prefabs/Canvas", "ActUnitInfo");
        if (actUnit.Count > 0)
        {
            uiActUnitInfo = Instantiate(actUnit[0]);
            uiActUnitInfo.transform.SetParent(canvaObj.transform, false);
            actUnitScript = uiActUnitInfo.AddComponent<CanvasMenuActUnitScript>();
        }

        // 各種表示
        uiInner.SetActive(visibleInner);
        uiDirectCom.SetActive(visibleDirect);
        uiActUnitInfo.SetActive(visibleMotion);
        uiAssembly.SetActive(visibleAsm);
    }

    /// <summary>
    /// アセンブリ選択
    /// </summary>
    public void SetAssemblyObject(GameObject gameObject, bool isSelectOnly = false)
    {
        if (!isSelectOnly)
        {
            assemblyScript.SetAssembly(gameObject);
        }
        selectedObject = gameObject;
    }

    /// <summary>
    /// 軸作成
    /// </summary>
    /// <param name="dir"></param>
    /// <param name="color"></param>
    private void CreateAxis(GameObject parent, Vector3 dir, Color color)
    {
        var go = new GameObject("Axis_" + color);
        go.transform.parent = transform;

        var lr = go.AddComponent<LineRenderer>();
        lr.startWidth = 0.02f;
        lr.endWidth = 0.02f;
        lr.positionCount = 2;
        lr.useWorldSpace = false;
        lr.SetPosition(0, Vector3.zero);
        lr.SetPosition(1, dir * 0.2f);

        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = color;
        lr.endColor = color;
        go.transform.parent = parent.transform;
    }

    /// <summary>
    /// 軸更新処理
    /// </summary>
    private void AxisUpdate()
    {
        if ((axis == null) || axis.IsDestroyed())
        {
            axis = new GameObject("AxisView");
            CreateAxis(axis, Vector3.right, Color.red);
            CreateAxis(axis, Vector3.up, Color.green);
            CreateAxis(axis, Vector3.forward, Color.blue);
        }
        if ((selectedObject == null) || !isAxisVisible)
        {
            if (axis.activeSelf)
            {
                axis.SetActive(false);
            }
        }
        else if (isAxisVisible)
        {
            if (!axis.activeSelf)
            {
                axis.SetActive(true);
            }
            axis.transform.parent = selectedObject.transform;
            axis.transform.localPosition = Vector3.zero;
            axis.transform.localEulerAngles = Vector3.zero;
            axis.transform.localScale = selectedObject.transform.localScale;

            float dist = Vector3.Distance(transform.position, cameraController.transform.position);
            float t = Mathf.InverseLerp(0.5f, 5f, dist);
            float width = Mathf.Lerp(0.0005f, 0.01f, t);
            float length = Mathf.Lerp(0.02f, 0.4f, t);

            // 全軸に適用
            var index = 0;
            foreach (var lr in axis.GetComponentsInChildren<LineRenderer>())
            {
                lr.startWidth = width;
                lr.endWidth = width;
                lr.SetPosition(1, (index == 0 ? Vector3.right : (index == 1 ? Vector3.up : Vector3.forward)) * length);
                index++;
            }
        }
    }
    #endregion メソッド
}
