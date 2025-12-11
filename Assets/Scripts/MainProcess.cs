using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.InputSystem;
using Parameters;
using Application =UnityEngine.Application;
using Oculus.Interaction;


#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Experimental.GraphView;
#endif

//[ExecuteInEditMode]
public class MainProcess : KssBaseScript
{
    [SerializeField]
    List<GlobalScript.CbTagInfo> cbTags;

    private bool isVR { get { return (Application.platform == RuntimePlatform.Android) || (Application.platform == RuntimePlatform.IPhonePlayer); } }
    private CameraController cameraController = null;
    private RayInteractor rayInteractorL = null;
    private RayInteractor rayInteractorR = null;
    private KssBaseScript selectedScript = null;
    private CanvasMenuInfoScript menuInfoScript = null;

//    private bool isReloading = false;

    private List<RaycastHit> raycastHits = new();
    private GameObject? selectedObject = null;

    private bool isControl;

    /// <summary>
    /// 初期化
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        // カメラ設定
        // var ovr = transform.parent.gameObject.GetComponentInChildren<OVRPlayerController>();
        // var camera = transform.parent.gameObject.GetComponentInChildren<Camera>();

        // フレームレート
        if (isVR)
        {
            // アンドロイド
            Application.targetFrameRate = 120;
            // VR時
            // camera.gameObject.SetActive(false);
        }
        else
        {
            // Windows
            Application.targetFrameRate = 120;
            // ovr.gameObject.SetActive(false);
        }

        // データ初期化
        var cameraControllers = FindObjectsByType<CameraController>(FindObjectsSortMode.None).ToList();
        if (cameraControllers.Count > 0)
        {
            cameraController = cameraControllers[0];
        }
        var rayInteractors = FindObjectsByType<RayInteractor>(FindObjectsSortMode.None).Where(d => d.transform.parent.parent.name == "LeftController").ToList();
        if (rayInteractors.Count > 0)
        {
            rayInteractorL = rayInteractors[0];
        }
        rayInteractors = FindObjectsByType<RayInteractor>(FindObjectsSortMode.None).Where(d => d.transform.parent.parent.name == "RightController").ToList();
        if (rayInteractors.Count > 0)
        {
            rayInteractorR = rayInteractors[0];
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        InputManager.Instance.RegisterKey(Key.R, HandleKey);
        InputManager.Instance.RegisterKey(Key.M, HandleKey);
        InputManager.Instance.RegisterKey(Key.O, HandleKey);
        InputManager.Instance.RegisterKey(Key.F, HandleKey);
        InputManager.Instance.RegisterKey(Key.LeftCtrl, HandleKey);
        InputManager.Instance.RegisterKey(Key.RightCtrl, HandleKey);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        InputManager.Instance.UnregisterKey(Key.R, HandleKey);
        InputManager.Instance.UnregisterKey(Key.M, HandleKey);
        InputManager.Instance.UnregisterKey(Key.O, HandleKey);
        InputManager.Instance.UnregisterKey(Key.F, HandleKey);
        InputManager.Instance.UnregisterKey(Key.LeftCtrl, HandleKey);
        InputManager.Instance.UnregisterKey(Key.RightCtrl, HandleKey);
    }

    protected override void Start()
    {
        base.Start();

        var menuInfoScripts = FindObjectsByType<CanvasMenuInfoScript>(FindObjectsSortMode.None).ToList();
        if (menuInfoScripts.Count > 0)
        {
            menuInfoScript = menuInfoScripts[0];
        }
//        InitCallbackData();
    }

    protected override void Update()
    {
        base.Update();

        // マウス処理
        MouseUpdate();
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate();

        // 折り返しテスト
//        CallbackTest();
    }

    /// <summary>
    /// キーイベント
    /// </summary>
    /// <param name="key"></param>
    private void HandleKey(Key key, bool value, bool isCtrl, bool isShift)
    {
        if (value)
        {
            // ON処理
            if (key == Key.R)
            {
                // R
                if (cameraController != null)
                {
                    cameraController.SetInitPosition();
                }
            }
            else if (key == Key.M)
            {
                // M
                if (cameraController != null)
                {
                    cameraController.SetRoomPosition();
                }
            }
            else if (key == Key.O)
            {
                // O
                if (cameraController != null)
                {
                    cameraController.InitCameraPosition();
                }
            }
            else if (key == Key.F)
            {
                // F
                if ((cameraController != null) && (selectedObject != null))
                {
                    cameraController.FocusTo(selectedObject.transform);
                }
            }
            else if ((key == Key.LeftCtrl) || (key == Key.RightCtrl))
            {
                isControl = true;
            }
        }
        else
        {
            if ((key == Key.LeftCtrl) || (key == Key.RightCtrl))
            {
                isControl = false;
            }
        }
    }

    private void MouseUpdate()
    {
        if (Application.isFocused)
        {
            // カメラのマウス情報更新
            if (cameraController != null)
            {
                // マウス操作更新
                cameraController.MouseUpdate();
            }
        }

        var click = Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.leftButton.wasReleasedThisFrame;
        var left = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch) || OVRInput.GetUp(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch);
        var right = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) || OVRInput.GetUp(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
        var leftDown = Mouse.current.leftButton.wasPressedThisFrame || OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch) || OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
        var rightDown = Mouse.current.rightButton.wasPressedThisFrame;
        var middleDown = Mouse.current.middleButton.wasPressedThisFrame;

        // 左クリックでRaycast(オブジェクト選択)
        if (click || left || right)
        {
            GameObject clickedGameObject = null;
            Vector3 rotateCenter = Vector3.zero;
            if (left)
            {
                if (rayInteractorL.Interactable != null)
                {
                    clickedGameObject = rayInteractorL.Interactable.gameObject;
                    rotateCenter = clickedGameObject.transform.position;
                }
            }
            else if (right)
            {
                if (rayInteractorR.Interactable != null)
                {
                    clickedGameObject = rayInteractorR.Interactable.gameObject;
                    rotateCenter = clickedGameObject.transform.position;
                }
            }
            else if (leftDown)
            {
                Vector2 mousePos = Mouse.current.position.ReadValue();
                Ray ray = Camera.main.ScreenPointToRay(mousePos);
                var hits = Physics.RaycastAll(ray, 100, LayerMask.GetMask("Default"), QueryTriggerInteraction.Collide).ToList();
                hits = hits.Where(h => !float.IsNaN(h.distance)).ToList();
                try
                {
                    hits.Sort((a, b) => a.distance > b.distance ? 1 : -1);
                }
                catch
                {
                    hits.Clear();
                }
                if (hits.Count > 0)
                {
                    // 選択あり
                    clickedGameObject = ((selectedObject == null) || (hits.FindIndex(d => d.collider.gameObject == selectedObject) < 0)) ? hits[0].collider.gameObject : hits[(hits.FindIndex(d => d.collider.gameObject == selectedObject) + 1) % hits.Count].collider.gameObject;
                    if (clickedGameObject.name == "Floor")
                    {
                        Plane plane = new Plane(Vector3.up, Vector3.zero);
                        if (plane.Raycast(ray, out float enter))
                        {
                            rotateCenter = ray.GetPoint(enter);
                        }
                    }
                    else
                    {
                        rotateCenter = clickedGameObject.transform.position;
                    }
                }
                raycastHits.Clear();
                raycastHits.AddRange(hits);
            }
            if (cameraController != null)
            {
                if (!Mouse.current.leftButton.wasReleasedThisFrame)
                {
                    cameraController.SetTargetPosition(rotateCenter);
                }
            }
            if (clickedGameObject != null)
            {
                var script = clickedGameObject.GetComponentInChildren<KssBaseScript>();
                if (script != null)
                {
                    if (leftDown)
                    {
                        //　マウスダウン
                        selectedScript = script;
                        selectedScript.OnMouseDown();
                        if (isControl)
                        {
                            // 選択中のマテリアルを解除
                            selectedObject = null;
                            menuInfoScript.SetAssemblyObject(selectedObject);
                        }
                        // ゲームオブジェクトの名前を出力
                        Debug.Log(clickedGameObject.name);
                    }
                    else if (selectedScript != null)
                    {
                        //　マウスアップ
                        selectedScript.OnMouseUp();
                        selectedScript = null;
                    }
                }
                else
                {
                    if (cameraController != null)
                    {
                        cameraController.SetTargetPosition(clickedGameObject.transform.position);
                    }
                    if (leftDown)
                    {
                        // 選択中のマテリアルをセット
                        if (isControl)
                        {
                            if (selectedObject == clickedGameObject)
                            {
                                // 既に選択済みなのでマテリアルを解除
                                selectedObject = null;
                                menuInfoScript.SetAssemblyObject(selectedObject);
                            }
                            else
                            {
                                selectedObject = clickedGameObject;
                                menuInfoScript.SetAssemblyObject(selectedObject);
                            }
                        }
                        // ゲームオブジェクトの名前を出力
                        Debug.Log(clickedGameObject.name);
                    }
                }
            }
        }
        else if (rightDown)
        {
            if (isControl)
            {
                // 選択中のマテリアルを解除
                selectedObject = null;
                menuInfoScript.SetAssemblyObject(selectedObject);
            }
        }
        else
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = Camera.main.ScreenPointToRay(mousePos);
            var script = selectedScript;
            if (Physics.Raycast(ray, out RaycastHit hit, 10, LayerMask.GetMask("Default"), QueryTriggerInteraction.Collide))
            {
                var mouseGameObject = hit.collider.gameObject;
                if (mouseGameObject != null)
                {
                    script = mouseGameObject.GetComponentInChildren<KssBaseScript>();
                }
            }
            if (selectedScript == null)
            {
                if (script != null)
                {
                    // 初回処理
                    selectedScript = script;
                    selectedScript.OnMouseEnter();
                }
            }
            else
            {
                if (script != selectedScript)
                {
                    if (script == null)
                    {
                        selectedScript.OnMouseExit();
                        selectedScript = null;
                    }
                    else
                    {
                        selectedScript.OnMouseExit();
                        selectedScript = script;
                        selectedScript.OnMouseEnter();
                    }
                }
                else
                {
                    selectedScript.OnMouseOver();
                }
            }
        }
    }

    /*
    private void InitCallbackData()
    {
        // コールバックデータ初期化
        cbTags = new();
        foreach (var tag in GlobalScript.callbackTags)
        {
            GlobalScript.SetTagData(tag.output, 0);
            tag.output.Value = 0;
        }
    }

    /// <summary>
    /// コールバックテスト
    /// </summary>
    public void CallbackTest()
    {
        if (!isReloading)
        {
            if (cbTags.Count == 0)
            {
                foreach (var tag in GlobalScript.callbackTags)
                {
                    tag.output.stopwatch = new();
                    tag.cntIn.stopwatch = new();
                    cbTags.Add(tag.output);
                    cbTags.Add(tag.cntIn);
                }
            }
            foreach (var tag in GlobalScript.callbackTags)
            {
                // 折り返し
                if ((tag.input.Tag != "") && (tag.output.Tag != ""))
                {
                    var input = GlobalScript.GetTagData(tag.input);
                    var output = GlobalScript.GetTagData(tag.output);
                    if (input == output)
                    {
                        var next = input == 0 ? 1 : 0;
                        if ((tag.output.Value != next) || (tag.output.stopwatch.ElapsedMilliseconds > 5000))
                        {
                            GlobalScript.SetTagData(tag.output, next);
                            tag.output.SetLaps(tag.output.stopwatch.ElapsedMilliseconds);
                            tag.output.stopwatch.Restart();
                            tag.output.Value = next;
                        }
                    }
                    else
                    {
                    }
                }
                // カウンタ
                if ((tag.cntIn.Tag != "") && (tag.cntOut.Tag != ""))
                {
                    var count = GlobalScript.GetTagData(tag.cntIn);
                    if (tag.cntIn.Value != count)
                    {
                        tag.cntIn.SetLaps(tag.cntIn.stopwatch.ElapsedMilliseconds);
                        tag.cntIn.stopwatch.Restart();
                        tag.cntIn.Value = count;
                    }
                    tag.cntOut.Value = (tag.cntOut.Value + 1) % 10000;
                    GlobalScript.SetTagData(tag.cntOut, tag.cntOut.Value);
                }
            }
        }
    }
    */
}
