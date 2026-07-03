using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.InputSystem;
using Parameters;
using Application = UnityEngine.Application;
using MongoDB.Driver;
using Oculus.Interaction.Locomotion;



#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Experimental.GraphView;
#endif

//[ExecuteInEditMode]
public class MainProcess : KssBaseScript
{
    [SerializeField]
    List<GlobalScript.CbTagInfo> cbTags;

    private CameraController cameraController = null;
    private KssBaseScript selectedScript = null;

    //    private bool isReloading = false;

    private List<RaycastHit> raycastHits = new();

    private bool isControl;

    /// <summary>
    /// 初期化
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        // カメラ設定
        // フレームレート
        if (GlobalScript.isXRMode)
        {
            // アンドロイド
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 120;
            // VR時
            // camera.gameObject.SetActive(false);
#if UNITY_ANDROID && !UNITY_EDITOR
            // Fixed Foveated Rendering: 周辺視野の塗り(フラグメント)を間引いてGPU負荷を下げる（Quest実機のみ）。
            // 動的FFR＝GPU負荷に応じて Off..HighTop を自動調整。重いプレハブ表示時の塗り律速を軽減する狙い。
            OVRManager.fixedFoveatedRenderingLevel = OVRManager.FixedFoveatedRenderingLevel.HighTop;
            OVRManager.useDynamicFoveatedRendering = true;
#endif
        }
        else
        {
            // Windows
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 120;
            // ovr.gameObject.SetActive(false);
        }

        // データ初期化
        var cameraControllers = FindObjectsByType<CameraController>(FindObjectsSortMode.None).ToList();
        if (cameraControllers.Count > 0)
        {
            cameraController = cameraControllers[0];
        }
        // キャンバスロード
        if (GlobalScript.isXRMode)
        {
            var canvases = GlobalScript.LoadPrefabObject("Prefabs/Canvas", "XRCanvas");
            if (canvases.Count > 0)
            {
                var xrCanvas = Instantiate(canvases[0]);
                xrCanvas.SetActive(false);
            }
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        InputManager.Instance.RegisterKey(Key.F, HandleKey);
        InputManager.Instance.RegisterKey(Key.LeftCtrl, HandleKey);
        InputManager.Instance.RegisterKey(Key.RightCtrl, HandleKey);
        InputManager.Instance.RegisterMouseDown(MouseDownEvent);
        InputManager.Instance.RegisterMouseUp(MouseUpEvent);
        InputManager.Instance.RegisterButtonDown(ButtonDownEvent);
        InputManager.Instance.RegisterButtonUp(ButtonUpEvent);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        InputManager.Instance.UnregisterKey(Key.F, HandleKey);
        InputManager.Instance.UnregisterKey(Key.LeftCtrl, HandleKey);
        InputManager.Instance.UnregisterKey(Key.RightCtrl, HandleKey);
        InputManager.Instance.UnregisterMouseDown(MouseDownEvent);
        InputManager.Instance.UnregisterMouseUp(MouseUpEvent);
        InputManager.Instance.UnregisterButtonDown(ButtonDownEvent);
        InputManager.Instance.UnregisterButtonUp(ButtonUpEvent);
    }

    protected override void Start()
    {
        base.Start();

//        InitCallbackData();
    }

    protected override void Update()
    {
        base.Update();

        // マウス処理
//        MouseUpdate();
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
            if ((key == Key.LeftCtrl) || (key == Key.RightCtrl))
            {
                isControl = true;
            }
        }
        else
        {
            // OFF処理
            if ((key == Key.LeftCtrl) || (key == Key.RightCtrl))
            {
                isControl = false;
            }
        }
    }

    /// <summary>
    /// マウスダウンイベント
    /// </summary>
    /// <param name="button"></param>
    private void MouseDownEvent(InputManager.MouseButton button, Vector2 mousePos)
    {
        if (button == InputManager.MouseButton.LeftButton)
        {
            // 左クリック
            GameObject clickedGameObject = null;
            Vector3 rotateCenter = Vector3.zero;
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
                clickedGameObject = ((GlobalScript.selectedObject == null) || (hits.FindIndex(d => d.collider.gameObject == GlobalScript.selectedObject) < 0)) ? hits[0].collider.gameObject : hits[(hits.FindIndex(d => d.collider.gameObject == GlobalScript.selectedObject) + 1) % hits.Count].collider.gameObject;
                if (clickedGameObject.name == "Floor")
                {
                    // 床なら床の上でクリックされたところを検索
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
                selectedScript = clickedGameObject.GetComponentInChildren<KssBaseScript>();
                if (selectedScript != null)
                {
                    //　マウスダウン
                    selectedScript.OnMouseDown();
                    if (isControl)
                    {
                        // 選択中のマテリアルを解除
                        EventManager.Instance.ProcessObjectSelect(null);
                    }
                    // ゲームオブジェクトの名前を出力
                    Debug.Log(clickedGameObject.name);
                }
                else
                {
                    // 非ユニット（親に KssBaseScript を持たない背景/フレーム/床等）の判定
                    bool clickedIsUnitPart = clickedGameObject.GetComponentInParent<KssBaseScript>() != null;
                    bool unitOpMode = GlobalScript.touchSelectOverride || UnitOperationView.IsActive;   // WebGL/エディタWebGLテスト or タッチ
                    // ユニット選択中に非ユニットをクリックしてもフォーカス(回転中心)を選択ユニットに留める。
                    // ※Ctrlなしの左ドラッグ回転でも効くよう、選択判定(isControl/touch)の外で上書きする。
                    if (unitOpMode && !clickedIsUnitPart && GlobalScript.selectedObject != null)
                    {
                        rotateCenter = GlobalScript.selectedObject.transform.position;
                    }
                    // 選択処理（Ctrl または タッチ時のみ）。ユニット操作モードでは非ユニットは選択しない。
                    if (isControl || GlobalScript.touchSelectOverride)
                    {
                        if (unitOpMode && !clickedIsUnitPart)
                        {
                            // 非ユニットは選択しない（現在の選択を維持・フォーカスは上で維持済み）
                        }
                        else if (GlobalScript.selectedObject == clickedGameObject)
                        {
                            // 既に選択済みなのでマテリアルを解除
                            EventManager.Instance.ProcessObjectSelect(null);
                            Debug.Log($"選択解除: {clickedGameObject.name}");
                        }
                        else
                        {
                            EventManager.Instance.ProcessObjectSelect(clickedGameObject);
                            Debug.Log($"選択: {clickedGameObject.name}");
                        }
                    }
                }
            }
            raycastHits.Clear();
            raycastHits.AddRange(hits);
            // 回転中心セット
            if (cameraController != null)
            {
                cameraController.SetTargetPosition(rotateCenter);
            }
        }
        else if (button == InputManager.MouseButton.RightButton)
        {
            // 右クリック
            if (isControl)
            {
                // 選択中のマテリアルを解除
                selectedScript = null;
                EventManager.Instance.ProcessObjectSelect(null);
            }
        }
    }

    /// <summary>
    /// マウスアップイベント
    /// </summary>
    /// <param name="button"></param>
    private void MouseUpEvent(InputManager.MouseButton button, Vector2 mousePos)
    {
        if(button == InputManager.MouseButton.LeftButton)
        {
            if (selectedScript != null)
            {
                //　マウスアップ
                selectedScript.OnMouseUp();
                selectedScript = null;
            }
        }
    }

    /// <summary>
    /// ボタンダウンイベント
    /// </summary>
    /// <param name="button"></param>
    private void ButtonDownEvent(InputManager.ControllerButton button)
    {
    }

    /// <summary>
    /// ボタンアップイベント
    /// </summary>
    /// <param name="button"></param>
    private void ButtonUpEvent(InputManager.ControllerButton button)
    {
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
