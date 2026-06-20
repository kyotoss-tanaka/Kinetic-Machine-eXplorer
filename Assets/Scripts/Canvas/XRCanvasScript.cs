
using NUnit.Framework;
using NUnit.Framework.Internal;
using Oculus.Interaction.Input.Visuals;
using Oculus.Interaction.Locomotion;
using Parameters;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static KssBaseScript;

public class XRCanvasScript : CanvasBaseScript
{
    private class PrefabButtonInfo
    {
        public string name;
        public Button button;
        public TextMeshProUGUI text;
        public GameObject prefab;
        public bool visible;
        public bool all;
    }

    public Transform xrCamera;
    public float distance = 0.75f;

    private bool isLeftDown = false;
    private bool isRightDown = false;

    /// <summary>
    /// ロコモーター
    /// </summary>
    private FirstPersonLocomotor locomotor;

    /// <summary>
    /// サブメニューレイキャンバス
    /// </summary>
    private GameObject rayCanvasInteraction;

    /// <summary>
    /// 各種サブメニュー
    /// </summary>
    private GameObject subMenuSystem;
    private GameObject subMenuBody;
    private GameObject subMenuTool;
    private GameObject subMenuPrefab;
    private GameObject subMenuSlice;
    private GameObject subMenuAssembly;
    private List<GameObject> subMenus = new();

    /// <summary>
    /// 各種ボタン
    /// </summary>
    private Button btnClose;
    private Button btnMainSystem;
    private Button btnMainBody;
    private Button btnMainTool;
    private Button btnMainPrefab;
    private Button btnMainSlice;
    private Button btnMainAssembly;
    private Button btnSystemOutlline;
    private Button btnBodyNormal;
    private Button btnBodyUp;
    private Button btnBodyDown;
    private Button btnToolHand;
    private Button btnToolPlus;
    private Button btnToolMinus;
    private Button btnToolWrench;
    private Button btnSliceX;
    private Button btnSliceY;
    private Button btnSliceZ;
    private Button btnSliceRvs;
    private List<Button> mainButtons = new();

    /// <summary>
    /// 断面表示用
    /// </summary>
    private GlobalScript.ClipInfo.SlideMode sliceMode = GlobalScript.ClipInfo.SlideMode.X;
    private Slider sliderSlice;

    /// <summary>
    /// アセンブリ表示用テキスト
    /// </summary>
    private TextMeshProUGUI txtAssembly;

    /// <summary>
    /// 手オブジェクト
    /// </summary>
    private Transform leftHand;
    private Transform rightHand;

    /// <summary>
    /// コントローラ
    /// </summary>
    private ControllerVisual leftController;
    private ControllerVisual rightController;

    /// <summary>
    /// ツールオブジェクト
    /// </summary>
    private GameObject ToolLeft;
    private GameObject ToolRight;

    /// <summary>
    /// 各種ツール
    /// </summary>
    private GameObject toolPlus;
    private GameObject toolMinus;
    private GameObject toolWrench;

    /// <summary>
    /// ツール衝突スクリプト
    /// </summary>
    private ToolCollisionScript toolCollisionPlus;
    private ToolCollisionScript toolCollisionMinus;
    private ToolCollisionScript toolCollisionWrench;

    /// <summary>
    /// プレハブ関連
    /// </summary>
    private GameObject allPrefab;
    private Button btnPrefab;
    private List<GameObject> prefabs = new();
    private List<PrefabButtonInfo> btnPrefabs = new();

    /// <summary>
    /// 非表示オブジェクトリスト
    /// </summary>
    private List<GameObject> hideObjects = new();

    /// <summary>
    /// 選択オブジェクトリスト
    /// </summary>
    private List<GameObject> selectObjects = new();

    /// <summary>
    /// 選択オブジェクト
    /// </summary>
    private GameObject selectObject;

    protected override void Awake()
    {
        base.Awake();

        // カメラ取得
        xrCamera = Camera.main.transform;

        // ロコモーター取得
        locomotor = FindObjectsByType<FirstPersonLocomotor>(FindObjectsSortMode.None).ToList()[0];

        // サブメニューレイキャンバス取得
        rayCanvasInteraction = GetComponentsInChildren<Transform>(true).Where(d => d.name == "ISDK_RayCanvasInteraction").ToList()[0].gameObject;

        // 手を取得
        var hands = FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(d => d.name == "OVRControllerPrefab").ToList();
        leftHand = hands.Find(d => d.parent.name.Contains("Left"));
        rightHand = hands.Find(d => d.parent.name.Contains("Right"));

        // ツール作成用オブジェクト作成
        ToolLeft = new GameObject("LeftTools");
        ToolLeft.transform.SetParent(leftHand.parent);
        ToolRight = new GameObject("RightTools");
        ToolRight.transform.SetParent(rightHand.parent);

        // コントローラ取得
        leftController = leftHand.parent.GetComponent<ControllerVisual>();
        rightController = rightHand.parent.GetComponent<ControllerVisual>();

        // サブメニュー取得
        subMenuSystem = GetComponentsInChildren<Transform>(true).Where(d => d.name == "SubMenu_System").ToList()[0].gameObject;
        subMenuBody = GetComponentsInChildren<Transform>(true).Where(d => d.name == "SubMenu_Body").ToList()[0].gameObject;
        subMenuTool = GetComponentsInChildren<Transform>(true).Where(d => d.name == "SubMenu_Tool").ToList()[0].gameObject;
        subMenuPrefab = GetComponentsInChildren<Transform>(true).Where(d => d.name == "SubMenu_Prefab").ToList()[0].gameObject;
        subMenuSlice = GetComponentsInChildren<Transform>(true).Where(d => d.name == "SubMenu_Slice").ToList()[0].gameObject;
        subMenuAssembly = GetComponentsInChildren<Transform>(true).Where(d => d.name == "SubMenu_Assembly").ToList()[0].gameObject;
        subMenus.Add(subMenuSystem);
        subMenus.Add(subMenuBody);
        subMenus.Add(subMenuTool);
        subMenus.Add(subMenuPrefab);
        subMenus.Add(subMenuSlice);
        subMenus.Add(subMenuAssembly);
        SetMainButtonClick(subMenuSystem);

        // ツールボタン取得
        btnClose = GetComponentsInChildren<Button>().Where(d => d.name == "BtnClose").ToList()[0];

        btnMainSystem = GetComponentsInChildren<Button>().Where(d => d.name == "BtnMainSystem").ToList()[0];
        btnMainBody = GetComponentsInChildren<Button>().Where(d => d.name == "BtnMainBody").ToList()[0];
        btnMainTool = GetComponentsInChildren<Button>().Where(d => d.name == "BtnMainTool").ToList()[0];
        btnMainPrefab = GetComponentsInChildren<Button>().Where(d => d.name == "BtnMainPrefab").ToList()[0];
        btnMainSlice = GetComponentsInChildren<Button>().Where(d => d.name == "BtnMainSlice").ToList()[0];
        btnMainAssembly = GetComponentsInChildren<Button>().Where(d => d.name == "BtnMainAssembly").ToList()[0];
        mainButtons.Add(btnMainSystem);
        mainButtons.Add(btnMainBody);
        mainButtons.Add(btnMainTool);
        mainButtons.Add(btnMainPrefab);
        mainButtons.Add(btnMainSlice);
        mainButtons.Add(btnMainAssembly);

        btnSystemOutlline = GetComponentsInChildren<Button>(true).Where(d => d.name == "BtnSystemOutline").ToList()[0];

        btnBodyNormal = GetComponentsInChildren<Button>(true).Where(d => d.name == "BtnBodyNormal").ToList()[0];
        btnBodyUp = GetComponentsInChildren<Button>(true).Where(d => d.name == "BtnBodyUp").ToList()[0];
        btnBodyDown = GetComponentsInChildren<Button>(true).Where(d => d.name == "BtnBodyDown").ToList()[0];

        btnToolHand = GetComponentsInChildren<Button>(true).Where(d => d.name == "BtnToolHand").ToList()[0];
        btnToolPlus = GetComponentsInChildren<Button>(true).Where(d => d.name == "BtnToolPlus").ToList()[0];
        btnToolMinus = GetComponentsInChildren<Button>(true).Where(d => d.name == "BtnToolMinus").ToList()[0];
        btnToolWrench = GetComponentsInChildren<Button>(true).Where(d => d.name == "BtnToolWrench").ToList()[0];

        btnSliceX = GetComponentsInChildren<Button>(true).Where(d => d.name == "BtnSliceX").ToList()[0];
        btnSliceY = GetComponentsInChildren<Button>(true).Where(d => d.name == "BtnSliceY").ToList()[0];
        btnSliceZ = GetComponentsInChildren<Button>(true).Where(d => d.name == "BtnSliceZ").ToList()[0];
        btnSliceRvs = GetComponentsInChildren<Button>(true).Where(d => d.name == "BtnSliceRvs").ToList()[0];

        sliderSlice = GetComponentsInChildren<Slider>(true).Where(d => d.name == "SliderSlice").ToList()[0];

        txtAssembly = GetComponentsInChildren<TextMeshProUGUI>(true).Where(d => d.name == "TxtAssembly").ToList()[0];

        // 各種ツールロード
        toolPlus = Instantiate(GlobalScript.LoadPrefabObject("Prefabs/Tools", "Screwdriver_Cross")[0]);
        toolMinus = Instantiate(GlobalScript.LoadPrefabObject("Prefabs/Tools", "Screwdriver_Single")[0]);
        toolWrench = Instantiate(GlobalScript.LoadPrefabObject("Prefabs/Tools", "Wrench_Open")[0]);
        toolPlus.SetActive(false);
        toolMinus.SetActive(false);
        toolWrench.SetActive(false);

        // ツール衝突スクリプト追加
        toolCollisionPlus = toolPlus.AddComponent<ToolCollisionScript>();
        toolCollisionMinus = toolMinus.AddComponent<ToolCollisionScript>();
        toolCollisionWrench = toolWrench.AddComponent<ToolCollisionScript>();

        // イベント登録
        InputManager.Instance.RegisterButtonDown(ButtonDownEvent);
        InputManager.Instance.RegisterTouchDown(TouchDownEvent);
    }

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Start()
    {
        base.Start();

        // プレハブ関連
        allPrefab = GameObject.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(d => d.name == "PrefabObjects").ToList()[0];
        btnPrefab = GetComponentsInChildren<Button>(true).ToList().Find(d => d.name == "BtnPrefab");
        for (var i = 0; i < allPrefab.transform.childCount; i++)
        {
            prefabs.Add(allPrefab.transform.GetChild(i).gameObject);
        }
        CreateButton(null);
        var dctName = new Dictionary<string, List<PrefabButtonInfo>>();
        foreach (var prefab in prefabs)
        {
            var info = CreateButton(prefab);
            info.visible = prefab.activeSelf;
            if (!dctName.ContainsKey(info.name))
            {
                dctName.Add(info.name, new());
            }
            dctName[info.name].Add(info);
        }
        // 同一名称チェック
        foreach (var info in dctName.Where(d => d.Value.Count > 1).ToList())
        {
            for (var i = 0; i < info.Value.Count; i++)
            {
                var name = info.Key + "-" + (i + 1);
                info.Value[i].name = name;
                info.Value[i].text.text = name;
            }
        }
        dctName.Clear();

        // イベント有効
        foreach (var btn in btnPrefabs)
        {
            btn.button.onClick.AddListener(() => btnPrefab_onClick(btn));
        }
        RenewPrefabButtonColor();
    }

    /// <summary>
    /// 有効時
    /// </summary>
    protected override void OnEnable()
    {
        base.OnEnable();

        btnClose.onClick.AddListener(btnClose_onClick);

        btnMainSystem.onClick.AddListener(btnMainSystem_onClick);
        btnMainBody.onClick.AddListener(btnMainBody_onClick);
        btnMainTool.onClick.AddListener(btnMainTool_onClick);
        btnMainPrefab.onClick.AddListener(btnMainPrefab_onClick);
        btnMainSlice.onClick.AddListener(btnMainSlice_onClick);
        btnMainAssembly.onClick.AddListener(btnMainAssembly_onClick);

        btnSystemOutlline.onClick.AddListener(btnSystemOutlline_onClick);

        btnBodyNormal.onClick.AddListener(btnBodyNormal_onClick);
        btnBodyUp.onClick.AddListener(btnBodyUp_onClick);
        btnBodyDown.onClick.AddListener(btnBodyDown_onClick);

        btnToolHand.onClick.AddListener(btnToolHand_onClick);
        btnToolPlus.onClick.AddListener(btnToolPlus_onClick);
        btnToolMinus.onClick.AddListener(btnToolMinus_onClick);
        btnToolWrench.onClick.AddListener(btnToolWrench_onClick);

        btnSliceX.onClick.AddListener(btnSliceX_onClick);
        btnSliceY.onClick.AddListener(btnSliceY_onClick);
        btnSliceZ.onClick.AddListener(btnSliceZ_onClick);
        btnSliceRvs.onClick.AddListener(btnSliceRvs_onClick);
        sliderSlice.onValueChanged.AddListener(sliderSlice_onValueChanged);

        foreach (var btn in btnPrefabs)
        {
            btn.button.onClick.RemoveAllListeners();
            btn.button.onClick.AddListener(() => btnPrefab_onClick(btn));
        }
    }

    /// <summary>
    /// 無効時
    /// </summary>
    protected override void OnDisable()
    {
        base.OnDisable();

        btnClose.onClick.RemoveAllListeners();

        btnMainSystem.onClick.RemoveAllListeners();
        btnMainBody.onClick.RemoveAllListeners();
        btnMainTool.onClick.RemoveAllListeners();
        btnMainPrefab.onClick.RemoveAllListeners();
        btnMainSlice.onClick.RemoveAllListeners();

        btnSystemOutlline.onClick.RemoveAllListeners();

        btnBodyNormal.onClick.RemoveAllListeners();
        btnBodyUp.onClick.RemoveAllListeners();
        btnBodyDown.onClick.RemoveAllListeners();

        btnToolHand.onClick.RemoveAllListeners();
        btnToolPlus.onClick.RemoveAllListeners();
        btnToolMinus.onClick.RemoveAllListeners();
        btnToolWrench.onClick.RemoveAllListeners();

        btnSliceX.onClick.RemoveAllListeners();
        btnSliceY.onClick.RemoveAllListeners();
        btnSliceZ.onClick.RemoveAllListeners();
        btnSliceRvs.onClick.RemoveAllListeners();
        sliderSlice.onValueChanged.RemoveAllListeners();

        foreach (var btn in btnPrefabs)
        {
            btn.button.onClick.RemoveAllListeners();
        }
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void LateUpdate()
    {
        // カメラの前に表示
        transform.position = xrCamera.position + xrCamera.forward * distance;
        transform.position = new Vector3(transform.position.x, transform.position.y - 0.2f, transform.position.z);
        transform.rotation = Quaternion.LookRotation(transform.position - xrCamera.position);
    }

    #region イベント
    /// <summary>
    /// ボタンダウンイベント
    /// </summary>
    /// <param name="button"></param>
    private void ButtonDownEvent(InputManager.ControllerButton button)
    {
        var clipDistance = 0f;
        if (button == InputManager.ControllerButton.Menu)
        {
            gameObject.SetActive(!gameObject.activeSelf);
        }
        else if (button == InputManager.ControllerButton.X)
        {
            // 低いところへ
            if (locomotor.IsCrouching)
            {
                SetBody(false, false);
            }
            else
            {
                SetBody(false, true);
            }
        }
        else if (button == InputManager.ControllerButton.Y)
        {
            // 高いところへ
            if (locomotor.IsCrouching)
            {
                SetBody(false, false);
            }
            else
            {
                SetBody(true, false);
            }
        }
        else if (button == InputManager.ControllerButton.B)
        {
            // 表示/非表示
            if (selectObject != null)
            {
                selectObject.SetActive(!selectObject.activeSelf);
                if (!selectObject.activeSelf)
                {
                    hideObjects.Add(selectObject);
                }
                else
                {
                    hideObjects.Remove(selectObject);
                }
            }
            else
            {
                if(hideObjects.Count > 0)
                {
                    hideObjects[hideObjects.Count - 1].SetActive(true);
                    hideObjects.Remove(hideObjects[hideObjects.Count - 1]);
                }
            }
        }
        else if (button == InputManager.ControllerButton.HandTriggerL)
        {
            if (GlobalScript.clipInfo.isOn)
            {
                clipDistance = -0.25f;
            }
            else if (selectObjects.Count > 0)
            {
                var index = selectObjects.IndexOf(selectObject) + 1;
                if (index < selectObjects.Count)
                {
                    selectObject = selectObjects[index];
                    txtAssembly.text = selectObject.name;
                    EventManager.Instance.ProcessObjectSelect(selectObject);
                }
            }
        }
        else if (button == InputManager.ControllerButton.HandTriggerR)
        {
            if (GlobalScript.clipInfo.isOn)
            {
                clipDistance = 0.25f;
            }
            else if (selectObjects.Count > 0)
            {
                var index = selectObjects.IndexOf(selectObject) - 1;
                if (index >= 0)
                {
                    selectObject = selectObjects[index];
                    txtAssembly.text = selectObject.name;
                    EventManager.Instance.ProcessObjectSelect(selectObject);
                }
            }
        }
        else if (button == InputManager.ControllerButton.IndexTriggerL)
        {
            if (GlobalScript.clipInfo.isOn)
            {
                clipDistance = -0.01f;
            }
        }
        else if (button == InputManager.ControllerButton.IndexTriggerR)
        {
            if (GlobalScript.clipInfo.isOn)
            {
                if (GlobalScript.clipInfo.isOn)
                {
                    clipDistance = 0.01f;
                }
            }
        }

        if (clipDistance != 0f)
        {
            GlobalScript.clipInfo.x += GlobalScript.clipInfo.mode == GlobalScript.ClipInfo.SlideMode.X ? clipDistance : 0;
            GlobalScript.clipInfo.y += GlobalScript.clipInfo.mode == GlobalScript.ClipInfo.SlideMode.Y ? clipDistance : 0;
            GlobalScript.clipInfo.z += GlobalScript.clipInfo.mode == GlobalScript.ClipInfo.SlideMode.Z ? clipDistance : 0;
            GlobalScript.clipInfo.value += clipDistance;
        }
    }

    /// <summary>
    /// トリガボタンダウンイベント
    /// </summary>
    /// <param name="button"></param>
    /// <param name="gameObject"></param>
    private void TouchDownEvent(InputManager.TouchButton button, GameObject gameObject)
    {
        selectObject = null;
        txtAssembly.text = "";
        selectObjects.Clear();
        isLeftDown = button == InputManager.TouchButton.LTouch;
        isRightDown = button == InputManager.TouchButton.RTouch;
        if (isRightDown)
        {
            if (gameObject != null)
            {
                var isMotion = gameObject.GetComponentInParent<AxisMotionBase>() != null;
                for (var obj = gameObject.transform; obj != null; obj = obj.transform.parent)
                {
                    if (isMotion)
                    {
                        if (obj.GetComponent<AxisMotionBase>() != null)
                        {
                            selectObjects.Add(obj.gameObject);
                        }
                    }
                    else
                    {
                        if (obj.GetComponent<UnityEngine.Pixyz.UnitySDK.Components.Metadata>() != null)
                        {
                            selectObjects.Add(obj.gameObject);
                        }
                    }
                }
                if (selectObjects.Count > 0)
                {
                    selectObject = selectObjects[0];
                    txtAssembly.text = selectObject.name;
                }
            }
        }
        EventManager.Instance.ProcessObjectSelect(selectObject);
    }

    /// <summary>
    /// 閉じるクリック
    /// </summary>
    private void btnClose_onClick()
    {
        this.gameObject.SetActive(false);
    }
    #region メインメニュー
    /// <summary>
    /// ボディクリック
    /// </summary>
    private void btnMainSystem_onClick()
    {
        SetMainButtonColor(btnMainSystem);
        SetMainButtonClick(subMenuSystem);
    }

    /// <summary>
    /// ボディクリック
    /// </summary>
    private void btnMainBody_onClick()
    {
        SetMainButtonColor(btnMainBody);
        SetMainButtonClick(subMenuBody);
    }

    /// <summary>
    /// ツールクリック
    /// </summary>
    private void btnMainTool_onClick()
    {
        SetMainButtonColor(btnMainTool);
        SetMainButtonClick(subMenuTool);
    }

    /// <summary>
    /// プレハブクリック
    /// </summary>
    private void btnMainPrefab_onClick()
    {
        SetMainButtonColor(btnMainPrefab);
        SetMainButtonClick(subMenuPrefab);
    }

    /// <summary>
    /// 断面表示クリック
    /// </summary>
    private void btnMainSlice_onClick()
    {
        SetMainButtonColor(btnMainSlice);
        SetMainButtonClick(subMenuSlice);
        if (GlobalScript.clipInfo.isOn)
        {
            GlobalScript.clipInfo.mode = sliceMode;
            if (sliceMode == GlobalScript.ClipInfo.SlideMode.X)
            {
                btnSliceX_onClick();
            }
            else if (sliceMode == GlobalScript.ClipInfo.SlideMode.Y)
            {
                btnSliceY_onClick();
            }
            else if (sliceMode == GlobalScript.ClipInfo.SlideMode.Z)
            {
                btnSliceZ_onClick();
            }
        }
    }

    /// <summary>
    /// 断面表示クリック
    /// </summary>
    private void btnMainAssembly_onClick()
    {
        SetMainButtonColor(btnMainAssembly);
        SetMainButtonClick(subMenuAssembly);
    }

    /// <summary>
    /// ボタンの色をセットする
    /// </summary>
    /// <param name="on"></param>
    /// <param name="offs"></param>
    private void SetMainButtonColor(Button select)
    {
        GlobalScript.clipInfo.isOn = select == btnMainSlice;
        foreach (var button in mainButtons)
        {
            if (button == select)
            {
                button.GetComponent<Image>().color = new Color(64 / 255f, 64 / 255f, 1f, 1 / 2f);
            }
            else
            {
                button.GetComponent<Image>().color = new Color(1 / 2f, 200 / 255f, 1f, 1 / 2f);
            }
        }
        // レイキャストの範囲変更
        if (select == btnMainAssembly)
        {
            ((RectTransform)rayCanvasInteraction.transform).sizeDelta = new Vector2(120, ((RectTransform)transform).sizeDelta.y);
        }
        else
        {
            ((RectTransform)rayCanvasInteraction.transform).sizeDelta = new Vector2(((RectTransform)transform).sizeDelta.x, ((RectTransform)transform).sizeDelta.y);
            // 選択解除
            EventManager.Instance.ProcessObjectSelect(null);
            txtAssembly.text = "";
        }
    }

    /// <summary>
    /// メインボタンクリック
    /// </summary>
    private void SetMainButtonClick(GameObject select)
    {
        foreach (var subMenu in subMenus)
        {
            subMenu.SetActive(select == subMenu);
        }
    }
    #endregion メインメニュー

    #region サブメニュー：システム
    /// <summary>
    /// システムアウトラインクリック
    /// </summary>
    private void btnSystemOutlline_onClick()
    {
        GlobalScript.isLiens = !GlobalScript.isLiens;
        if (GlobalScript.isLiens)
        {
            SetSubButtonColor(btnSystemOutlline, new Button[] { });
        }
        else
        {
            SetSubButtonColor(null, new Button[] { btnSystemOutlline });
        }
    }
    #endregion サブメニュー：システム

    #region サブメニュー：ボディ
    /// <summary>
    /// ボディノーマルをクリック
    /// </summary>
    private void btnBodyNormal_onClick()
    {
        SetBody(false, false);
    }

    /// <summary>
    /// ボディアップをクリック
    /// </summary>
    private void btnBodyUp_onClick()
    {
        SetBody(true, false);
    }

    /// <summary>
    /// ボディダウンをクリック
    /// </summary>
    private void btnBodyDown_onClick()
    {
        SetBody(false, true);
    }

    /// <summary>
    /// ボディセット
    /// </summary>
    /// <param name="height"></param>
    private void SetBody(bool up, bool down)
    {
        if (up)
        {
            locomotor.CrouchHeightOffset = 2.0f;
            SetSubButtonColor(btnBodyUp, new Button[] { btnBodyNormal, btnBodyDown });
        }
        else if (down)
        {
            locomotor.CrouchHeightOffset = -0.5f;
            SetSubButtonColor(btnBodyDown, new Button[] { btnBodyNormal, btnBodyUp });
        }
        else
        {
            SetSubButtonColor(btnBodyNormal, new Button[] { btnBodyUp, btnBodyDown });
        }
        locomotor.Crouch(up || down);
    }
    #endregion サブメニュー：ボディ

    #region サブメニュー：ツール
    /// <summary>
    /// ツールハンドをクリック
    /// </summary>
    private void btnToolHand_onClick()
    {
        SetTool(null, new List<GameObject> { toolPlus, toolMinus, toolWrench });
        SetSubButtonColor(btnToolHand, new Button[] { btnToolPlus, btnToolMinus, btnToolWrench });
        leftController.InjectRoot(leftHand.gameObject);
        rightController.InjectRoot(rightHand.gameObject);
    }

    /// <summary>
    /// ツールプラスをクリック
    /// </summary>
    private void btnToolPlus_onClick()
    {
        SetTool(toolPlus, toolCollisionPlus, new List<GameObject> { toolMinus, toolWrench });
        SetSubButtonColor(btnToolPlus, new Button[] { btnToolHand, btnToolMinus, btnToolWrench });
    }

    /// <summary>
    /// ツールマイナスをクリック
    /// </summary>
    private void btnToolMinus_onClick()
    {
        SetTool(toolMinus, toolCollisionMinus, new List<GameObject> { toolPlus, toolWrench });
        SetSubButtonColor(btnToolMinus, new Button[] { btnToolHand, btnToolPlus, btnToolWrench });
    }

    /// <summary>
    /// ツールレンチをクリック
    /// </summary>
    private void btnToolWrench_onClick()
    {
        SetTool(toolWrench, toolCollisionWrench, new List<GameObject> { toolPlus, toolMinus });
        SetSubButtonColor(btnToolWrench, new Button[] { btnToolHand, btnToolPlus, btnToolMinus });
    }

    /// <summary>
    /// ツールをセットする
    /// </summary>
    /// <param name="tool"></param>
    private void SetTool(GameObject tool, ToolCollisionScript collision, List<GameObject> notUsed)
    {
        if (isLeftDown)
        {
            tool.transform.SetParent(ToolLeft.transform);
            leftController.InjectRoot(ToolLeft);
            rightController.InjectRoot(rightHand.gameObject);
            leftHand.gameObject.SetActive(false);
            isLeftDown = false;
            collision.controller = OVRInput.Controller.LTouch;
        }
        else
        {
            tool.transform.SetParent(ToolRight.transform);
            leftController.InjectRoot(leftHand.gameObject);
            rightController.InjectRoot(ToolRight);
            rightHand.gameObject.SetActive(false);
            isRightDown = false;
            collision.controller = OVRInput.Controller.RTouch;
        }
        tool.transform.localPosition = Vector3.zero;
        tool.transform.localEulerAngles = new Vector3(-45, 0, 0);
        SetTool(tool, notUsed);
    }

    /// <summary>
    /// ツールをセットする
    /// </summary>
    /// <param name="use"></param>
    /// <param name="notUsed"></param>
    private void SetTool(GameObject use, List<GameObject> notUsed)
    {
        if (use != null)
        {
            use.SetActive(true);
        }
        foreach (var n in notUsed)
        {
            n.SetActive(false);
        }
    }
    #endregion サブメニュー：ツール

    #region サブメニュー：プレハブ
    /// <summary>
    /// ボタンクリックイベント
    /// </summary>
    private void btnPrefab_onClick(PrefabButtonInfo info)
    {
        info.visible = !info.visible;
        info.prefab.SetActive(info.visible);
        RenewPrefabButtonColor();
    }

    /// <summary>
    /// ボタン作成
    /// </summary>
    /// <param name=""></param>
    private PrefabButtonInfo CreateButton(GameObject prefab)
    {
        var name = "ALL";
        var all = prefab == null;
        if (all)
        {
            prefab = allPrefab;
        }
        else
        {
            var names = prefab.name.Split('-');
            if (name.Length > 1)
            {
                name = names[1];
            }
            else if (name.Length == 1)
            {
                name = names[0].Substring(0, 2);
            }
            else
            {
                return null;
            }
            btnPrefabs.Find(d => d.name == name);
        }
        var btn = Instantiate(btnPrefab);
        btn.transform.SetParent(subMenuPrefab.transform, false);
        btn.transform.localPosition = new Vector3((btnPrefabs.Count % 6) * 60, (int)(btnPrefabs.Count / 6) * (-60));
        btn.gameObject.SetActive(true);
        var text = btn.GetComponentInChildren<TextMeshProUGUI>();
        text.text = name;

        var info = new PrefabButtonInfo
        {
            name = name,
            text = text,
            button = btn,
            prefab = prefab,
            visible = true,
            all = all
        };

        btnPrefabs.Add(info);
        return info;
    }

    /// <summary>
    /// プレハブボタンカラー更新
    /// </summary>
    private void RenewPrefabButtonColor()
    {
        foreach (var info in btnPrefabs)
        {
            if (info.visible)
            {
                info.button.GetComponent<Image>().color = new Color(1f, 1f, 1 / 2f, 1 / 2f);
            }
            else
            {
                info.button.GetComponent<Image>().color = new Color(1f, 200 / 255f, 1 / 2f, 1 / 2f);
            }
        }
    }
    #endregion サブメニュー：プレハブ

    #region サブメニュー：断面表示
    /// <summary>
    /// 断面表示X
    /// </summary>
    private void btnSliceX_onClick()
    {
        GlobalScript.clipInfo.mode = GlobalScript.ClipInfo.SlideMode.X;
        sliceMode = GlobalScript.clipInfo.mode;
        sliderSlice.minValue = GlobalScript.clipInfo.bounds.min.x;
        sliderSlice.maxValue = GlobalScript.clipInfo.bounds.max.x;
        sliderSlice.value = GlobalScript.clipInfo.x;
        SetSubButtonColor(btnSliceX, new Button[] { btnSliceY, btnSliceZ });
    }

    /// <summary>
    /// 断面表示Y
    /// </summary>
    private void btnSliceY_onClick()
    {
        GlobalScript.clipInfo.mode = GlobalScript.ClipInfo.SlideMode.Y;
        sliceMode = GlobalScript.clipInfo.mode;
        sliderSlice.minValue = GlobalScript.clipInfo.bounds.min.y;
        sliderSlice.maxValue = GlobalScript.clipInfo.bounds.max.y;
        sliderSlice.value = GlobalScript.clipInfo.y;
        SetSubButtonColor(btnSliceY, new Button[] { btnSliceX, btnSliceZ });
    }

    /// <summary>
    /// 断面表示Z
    /// </summary>
    private void btnSliceZ_onClick()
    {
        GlobalScript.clipInfo.mode = GlobalScript.ClipInfo.SlideMode.Z;
        sliceMode = GlobalScript.clipInfo.mode;
        sliderSlice.minValue = GlobalScript.clipInfo.bounds.min.z;
        sliderSlice.maxValue = GlobalScript.clipInfo.bounds.max.z;
        sliderSlice.value = GlobalScript.clipInfo.z;
        SetSubButtonColor(btnSliceZ, new Button[] { btnSliceX, btnSliceY });
    }

    /// <summary>
    /// 断面表示反転
    /// </summary>
    private void btnSliceRvs_onClick()
    {
        GlobalScript.clipInfo.isRvs = !GlobalScript.clipInfo.isRvs;
        if (GlobalScript.clipInfo.isRvs)
        {
            btnSliceRvs.GetComponent<Image>().color = new Color(1f, 200 / 255f, 1 / 2f, 1 / 2f);
        }
        else
        {
            btnSliceRvs.GetComponent<Image>().color = new Color(1f, 1f, 1 / 2f, 1 / 2f);
        }
    }

    /// <summary>
    /// 断面表示の値セット
    /// </summary>
    private void sliderSlice_onValueChanged(float value)
    {
        if (sliceMode == GlobalScript.ClipInfo.SlideMode.X)
        {
            GlobalScript.clipInfo.x = value;
        }
        else if (sliceMode == GlobalScript.ClipInfo.SlideMode.Y)
        {
            GlobalScript.clipInfo.y = value;
        }
        else if (sliceMode == GlobalScript.ClipInfo.SlideMode.Z)
        {
            GlobalScript.clipInfo.z = value;
        }
        GlobalScript.clipInfo.value = value;
    }
    #endregion サブメニュー：断面表示
    #endregion イベント

    #region メソッド
    /// <summary>
    /// ボタンの色をセットする
    /// </summary>
    /// <param name="on"></param>
    /// <param name="offs"></param>
    private void SetSubButtonColor(Button on, Button[] offs)
    {
        if (on != null)
        {
            on.GetComponent<Image>().color = new Color(1f, 200 / 255f, 1 / 2f, 1 / 2f);
        }
        foreach (var off in offs)
        {
            off.GetComponent<Image>().color = new Color(1f, 1f, 1 / 2f, 1 / 2f);
        }
    }
    #endregion メソッド
}
