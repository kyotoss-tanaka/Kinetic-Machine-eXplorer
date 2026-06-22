using Oculus.Interaction;
using Oculus.Interaction.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class InputManager : BaseBehaviour
{
    private static InputManager _Instance;
    public static InputManager Instance
    {
        get
        {
            if (_Instance == null)
            {
                var mng = GameObject.FindObjectsByType<InputManager>(FindObjectsSortMode.None).ToList();
                if (mng.Count > 0)
                {
                    _Instance = mng[0];
                }
            }
            return _Instance;
        }
    }

    /// <summary>
    /// マウスのボタン
    /// </summary>
    public enum MouseButton : int
    {
        LeftButton = 0,
        RightButton = 1,
        MiddleButton = 2,
    }

    /// <summary>
    /// タッチのボタン
    /// </summary>
    public enum TouchButton : int
    {
        LTouch = 0,
        RTouch = 1,
    }

    public enum ControllerButton : int
    {
        None = 0,
        A,
        B,
        X,
        Y,
        HandTriggerL, 
        HandTriggerR,
        IndexTriggerL, 
        IndexTriggerR,
        StickUpL,
        StickDownL,
        StickLeftL,
        StickRightL,
        StickUpR,
        StickDownR,
        StickLeftR,
        StickRightR,
        Menu
    }

    /// <summary>
    /// キーボードイベント
    /// </summary>
    private Dictionary<Key, Action<Key, bool, bool, bool>> keyActions = new Dictionary<Key, Action<Key, bool, bool, bool>>();
    /// <summary>
    /// キーボード状態
    /// </summary>
    private Dictionary<Key, bool> keyValues = new Dictionary<Key, bool>();
    /// <summary>
    /// マウスダウンイベント
    /// </summary>
    private Action<MouseButton, Vector2> mouseDownEvents;
    /// <summary>
    /// マウスアップイベント
    /// </summary>
    private Action<MouseButton, Vector2> mouseUpEvents;
    /// <summary>
    /// マウスホイールイベント
    /// </summary>
    private Action<Vector2> mouseWheelEvents;
    /// <summary>
    /// マウスムーブイベント
    /// </summary>
    private Action<Vector2, Vector2> mouseMoveEvents;
    /// <summary>
    /// タッチダウンイベント
    /// </summary>
    private Action<TouchButton, GameObject> touchDownEvents;
    /// <summary>
    /// タッチアップイベント
    /// </summary>
    private Action<TouchButton, GameObject> touchUpEvents;
    /// <summary>
    /// ボタンダウンイベント
    /// </summary>
    private Action<ControllerButton> buttonDownEvents;
    /// <summary>
    /// アップイベント
    /// </summary>
    private Action<ControllerButton> buttonUpEvents;

    /// <summary>
    /// マウス位置
    /// </summary>
    private Vector2 mousePos, prvMousePos;

    /// <summary>
    /// 各種ボタン状態
    /// </summary>
    private bool isMouseLeft, isMouseRight, isMouseMiddle, isKeyCtrl, isKeyShift;

    /// <summary>
    /// スクリーン内フラグ
    /// </summary>
    private bool isInsideScreen;

    /// <summary>
    /// 画面タッチ（タブレット/WebGL）のジェスチャ状態
    /// </summary>
    private enum TouchGesture { None, Press, Orbit, PanZoom }
    private TouchGesture touchGesture = TouchGesture.None;
    private Vector2 touchStartPos;
    private float touchStartTime;
    private Vector2 prvTouchPos;
    private float prvPinchDist;
    [SerializeField] private float pinchZoomScale = 0.02f;

    // タッチ感度倍率（HmxLink.json の "touch" で起動時設定。1.0=既定、小さいほど鈍い）
    private static float TouchOrbitSens => GlobalScript.hmxLink?.touch?.orbit ?? 1f;
    private static float TouchPanSens => GlobalScript.hmxLink?.touch?.pan ?? 1f;
    private static float TouchPinchSens => GlobalScript.hmxLink?.touch?.pinch ?? 1f;

    private const float TouchDragThreshold = 12f;
    private const float TapMaxTime = 0.4f;
    private float lastTapTime;
    private Vector2 lastTapPos;
    private bool panZoomMoved;
    private float panZoomStartTime;
    private const float DoubleTapTime = 0.35f;
    private const float DoubleTapDist = 40f;

    /// <summary>
    /// RayInteractor
    /// </summary>
    private RayInteractor rayHandL;
    private RayInteractor rayHandR;
    private RayInteractor rayControllerL;
    private RayInteractor rayControllerR;

    /// <summary>
    /// 起床イベント
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        // タッチ操作（タブレット/WebGL）対応を有効化
        UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Enable();

        // 各種RayInteractor取得
        var handRays = FindObjectsByType<RayInteractor>(FindObjectsSortMode.None).Where(d => d.name == "HandRayInteractor");
        foreach (var handRay in handRays)
        {
            if (handRay.gameObject.GetComponentsInParent<Transform>().Where(d => d.name == "LeftInteractions").Count() > 0)
            {
                rayHandL = handRay;
                rayHandL.WhenStateChanged += rayHandL_WhenStateChanged;
            }
            else if (handRay.gameObject.GetComponentsInParent<Transform>().Where(d => d.name == "RightInteractions").Count() > 0)
            {
                rayHandR = handRay;
                rayHandR.WhenStateChanged += rayHandR_WhenStateChanged;
            }
        }
        var controllerRays = FindObjectsByType<RayInteractor>(FindObjectsSortMode.None).Where(d => d.name == "ControllerRayInteractor");
        foreach (var controllerRay in controllerRays)
        {
            if (controllerRay.gameObject.GetComponentsInParent<Transform>().Where(d => d.name == "LeftInteractions").Count() > 0)
            {
                rayControllerL = controllerRay;
                rayControllerL.WhenStateChanged += RayControllerL_WhenStateChanged;
            }
            else if (controllerRay.gameObject.GetComponentsInParent<Transform>().Where(d => d.name == "RightInteractions").Count() > 0)
            {
                rayControllerR = controllerRay;
                rayControllerR.WhenStateChanged += RayControllerR_WhenStateChanged;
            }
        }
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void Update()
    {
        // スクリーン内かチェック
        isInsideScreen = IsMouseInsideScreen();

        // キーアップデート
        KeyUpdate();

        if (!GlobalScript.IsInTimeChart)
        {
            // マウスアップデート
            MouseUpdate();

            if (GlobalScript.isXRMode)
            {
                // ボタンアップデート
                ButtonUpdate();

                // タッチアップデート
                TouchUpdate();
            }
            else
            {
                // 画面タッチ（タブレット/WebGL）→ マウス操作へ変換
                ScreenTouchUpdate();
            }
        }
    }

    /// <summary>
    /// キーアップデート
    /// </summary>
    private void KeyUpdate()
    {
        foreach (var kvp in keyActions)
        {
            var key = kvp.Key;
            var action = kvp.Value;
            if (Keyboard.current[key].wasPressedThisFrame)
            {
                action?.Invoke(key, true, Keyboard.current.ctrlKey.isPressed, Keyboard.current.shiftKey.isPressed);
                keyValues[key] = true;
            }
            else if (Keyboard.current[key].wasReleasedThisFrame || (keyValues[key] && !isInsideScreen))
            {
                action?.Invoke(key, false, Keyboard.current.ctrlKey.isPressed, Keyboard.current.shiftKey.isPressed);
                keyValues[key] = false;
            }
        }
    }

    /// <summary>
    /// マウスアップデート
    /// </summary>
    private void MouseUpdate()
    {
        if (Mouse.current == null)
        {
            // マウス非搭載（タブレット/タッチ専用）はスキップ。タッチは ScreenTouchUpdate で処理。
            return;
        }
        if (HasActiveTouches())
        {
            // タッチ操作中はマウス経路を走らせない。
            // mousePos が「マウス位置(IsMouseInsideScreen)」と「タッチ点(ScreenTouchUpdate)」で
            // 交互に書き換わり、mouseDelta が巨大化して回転/パンが暴走するのを防ぐ。
            return;
        }
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            mouseDownEvents?.Invoke(MouseButton.LeftButton, mousePos);
            isMouseLeft = true;
        }
        else if (Mouse.current.leftButton.wasReleasedThisFrame || (isMouseLeft && !isInsideScreen))
        {
            mouseUpEvents?.Invoke(MouseButton.LeftButton, mousePos);
            isMouseLeft = false;
        }
        else if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            mouseDownEvents?.Invoke(MouseButton.RightButton, mousePos);
            isMouseRight = true;
        }
        else if (Mouse.current.rightButton.wasReleasedThisFrame || (isMouseRight && !isInsideScreen))
        {
            mouseUpEvents?.Invoke(MouseButton.RightButton, mousePos);
            isMouseRight = false;
        }
        else if (Mouse.current.middleButton.wasPressedThisFrame)
        {
            mouseDownEvents?.Invoke(MouseButton.MiddleButton, mousePos);
            isMouseMiddle = true;
        }
        else if (Mouse.current.middleButton.wasReleasedThisFrame || (isMouseMiddle && !isInsideScreen))
        {
            mouseUpEvents?.Invoke(MouseButton.MiddleButton, mousePos);
            isMouseMiddle = false;
        }
        if (isInsideScreen)
        {
            Vector2 scrollDelta = Mouse.current.scroll.ReadValue();
            if ((scrollDelta.x != 0) || (scrollDelta.y != 0))
            {
                mouseWheelEvents?.Invoke(scrollDelta);
            }
        }
        Vector2 mouseDelta = mousePos - prvMousePos;
        if ((mouseDelta.x != 0) || (mouseDelta.y != 0))
        {
            mouseMoveEvents?.Invoke(mousePos, mouseDelta);
        }
    }

    /// <summary>
    /// ボタンアップデート
    ///    A Button.One
    ///    B Button.Two
    ///    X Button.Three
    ///    Y Button.Four
    /// </summary>
    private void ButtonUpdate()
    {
        if (OVRInput.GetDown(OVRInput.Button.Start))
        {
            buttonDownEvents?.Invoke(ControllerButton.Menu);
        }
        else if (OVRInput.GetUp(OVRInput.Button.Start))
        {
            buttonUpEvents?.Invoke(ControllerButton.Menu);
        }
        if (OVRInput.GetDown(OVRInput.Button.One))
        {
            buttonDownEvents?.Invoke(ControllerButton.A);
        }
        else if (OVRInput.GetUp(OVRInput.Button.One))
        {
            buttonUpEvents?.Invoke(ControllerButton.A);
        }
        if (OVRInput.GetDown(OVRInput.Button.Two))
        {
            buttonDownEvents?.Invoke(ControllerButton.B);
        }
        else if (OVRInput.GetUp(OVRInput.Button.Two))
        {
            buttonUpEvents?.Invoke(ControllerButton.B);
        }
        if (OVRInput.GetDown(OVRInput.Button.Three))
        {
            buttonDownEvents?.Invoke(ControllerButton.X);
        }
        else if (OVRInput.GetUp(OVRInput.Button.Three))
        {
            buttonUpEvents?.Invoke(ControllerButton.X);
        }
        if (OVRInput.GetDown(OVRInput.Button.Four))
        {
            buttonDownEvents?.Invoke(ControllerButton.Y);
        }
        else if (OVRInput.GetUp(OVRInput.Button.Four))
        {
            buttonUpEvents?.Invoke(ControllerButton.Y);
        }
        if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger))
        {
            buttonDownEvents?.Invoke(ControllerButton.HandTriggerL);
        }
        else if (OVRInput.GetUp(OVRInput.Button.PrimaryHandTrigger))
        {
            buttonUpEvents?.Invoke(ControllerButton.HandTriggerL);
        }
        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger))
        {
            buttonDownEvents?.Invoke(ControllerButton.IndexTriggerL);
        }
        else if (OVRInput.GetUp(OVRInput.Button.PrimaryIndexTrigger))
        {
            buttonUpEvents?.Invoke(ControllerButton.IndexTriggerL);
        }
        if (OVRInput.GetDown(OVRInput.Button.SecondaryHandTrigger))
        {
            buttonDownEvents?.Invoke(ControllerButton.HandTriggerR);
        }
        else if (OVRInput.GetUp(OVRInput.Button.SecondaryHandTrigger))
        {
            buttonUpEvents?.Invoke(ControllerButton.HandTriggerR);
        }
        if (OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger))
        {
            buttonDownEvents?.Invoke(ControllerButton.IndexTriggerR);
        }
        else if (OVRInput.GetUp(OVRInput.Button.SecondaryIndexTrigger))
        {
            buttonUpEvents?.Invoke(ControllerButton.IndexTriggerR);
        }
        if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickUp))
        {
            buttonDownEvents?.Invoke(ControllerButton.StickUpL);
        }
        else if (OVRInput.GetUp(OVRInput.Button.PrimaryThumbstickUp))
        {
            buttonUpEvents?.Invoke(ControllerButton.StickUpL);
        }
        else if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickDown))
        {
            buttonDownEvents?.Invoke(ControllerButton.StickDownL);
        }
        else if (OVRInput.GetUp(OVRInput.Button.PrimaryThumbstickDown))
        {
            buttonUpEvents?.Invoke(ControllerButton.StickDownL);
        }
        else if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickLeft))
        {
            buttonDownEvents?.Invoke(ControllerButton.StickLeftL);
        }
        else if (OVRInput.GetUp(OVRInput.Button.PrimaryThumbstickLeft))
        {
            buttonUpEvents?.Invoke(ControllerButton.StickLeftL);
        }
        else if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickRight))
        {
            buttonDownEvents?.Invoke(ControllerButton.StickRightL);
        }
        else if (OVRInput.GetUp(OVRInput.Button.PrimaryThumbstickRight))
        {
            buttonUpEvents?.Invoke(ControllerButton.StickRightL);
        }
        if (OVRInput.GetDown(OVRInput.Button.SecondaryThumbstickUp))
        {
            buttonDownEvents?.Invoke(ControllerButton.StickUpR);
        }
        else if (OVRInput.GetUp(OVRInput.Button.SecondaryThumbstickUp))
        {
            buttonUpEvents?.Invoke(ControllerButton.StickUpR);
        }
        else if (OVRInput.GetDown(OVRInput.Button.SecondaryThumbstickDown))
        {
            buttonDownEvents?.Invoke(ControllerButton.StickDownR);
        }
        else if (OVRInput.GetUp(OVRInput.Button.SecondaryThumbstickDown))
        {
            buttonUpEvents?.Invoke(ControllerButton.StickDownR);
        }
        else if (OVRInput.GetDown(OVRInput.Button.SecondaryThumbstickLeft))
        {
            buttonDownEvents?.Invoke(ControllerButton.StickLeftR);
        }
        else if (OVRInput.GetUp(OVRInput.Button.SecondaryThumbstickLeft))
        {
            buttonUpEvents?.Invoke(ControllerButton.StickLeftR);
        }
        else if (OVRInput.GetDown(OVRInput.Button.SecondaryThumbstickRight))
        {
            buttonDownEvents?.Invoke(ControllerButton.StickRightR);
        }
        else if (OVRInput.GetUp(OVRInput.Button.SecondaryThumbstickRight))
        {
            buttonUpEvents?.Invoke(ControllerButton.StickRightR);
        }
    }

    /// <summary>
    /// タッチアップデート
    /// </summary>
    private void TouchUpdate()
    {
        rayHandL.enabled = rayHandL.gameObject.activeSelf;
        rayHandR.enabled = rayHandR.gameObject.activeSelf;
        rayControllerL.enabled = rayControllerL.gameObject.activeSelf;
        rayControllerR.enabled = rayControllerR.gameObject.activeSelf;
        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch))
        {
            touchDownEvents?.Invoke(TouchButton.LTouch, GlobalScript.rayLObject);
        }
        else if (OVRInput.GetUp(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch))
        {
            touchUpEvents?.Invoke(TouchButton.LTouch, GlobalScript.rayLObject);
        }
        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
        {
            touchDownEvents?.Invoke(TouchButton.RTouch, GlobalScript.rayRObject);
        }
        else if (OVRInput.GetUp(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
        {
            touchUpEvents?.Invoke(TouchButton.RTouch, GlobalScript.rayRObject);
        }
    }

    /// <summary>アクティブなタッチがあるか（指が画面に触れている間 true。終了フレームも含む）</summary>
    private static bool HasActiveTouches()
    {
        return UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0;
    }

    /// <summary>
    /// 画面タッチ（タブレット/WebGL）→ マウス操作へ変換。
    /// 1本指ドラッグ=回転(右ドラッグ相当) / 2本指ドラッグ=パン(中ドラッグ相当) /
    /// ピンチ=ズーム(ホイール相当) / 1本指タップ=選択(左クリック) /
    /// 1本指ダブルタップ=フォーカス(Fキー・再度でトグル) / 2本指タップ=初期位置(Rキー)。
    /// </summary>
    private void ScreenTouchUpdate()
    {
        var touches = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches;
        int count = 0;
        Vector2 p0 = Vector2.zero, p1 = Vector2.zero;
        foreach (var t in touches)
        {
            var ph = t.phase;
            if (ph == UnityEngine.InputSystem.TouchPhase.Began
                || ph == UnityEngine.InputSystem.TouchPhase.Moved
                || ph == UnityEngine.InputSystem.TouchPhase.Stationary)
            {
                if (count == 0) p0 = t.screenPosition;
                else if (count == 1) p1 = t.screenPosition;
                count++;
            }
        }

        if (count >= 2)
        {
            Vector2 center = (p0 + p1) * 0.5f;
            float dist = Vector2.Distance(p0, p1);
            mousePos = center;
            if (touchGesture != TouchGesture.PanZoom)
            {
                EndCurrentTouchGesture(center, false);
                touchGesture = TouchGesture.PanZoom;
                prvTouchPos = center;
                prvPinchDist = dist;
                panZoomMoved = false;
                panZoomStartTime = Time.unscaledTime;
                mouseDownEvents?.Invoke(MouseButton.MiddleButton, center);
            }
            else
            {
                Vector2 panDelta = center - prvTouchPos;
                prvTouchPos = center;
                float pinch = dist - prvPinchDist;
                prvPinchDist = dist;
                if (panDelta.magnitude > 1f || Mathf.Abs(pinch) > 1f)
                {
                    panZoomMoved = true;
                }
                if (panDelta.sqrMagnitude > 0f)
                {
                    mouseMoveEvents?.Invoke(center, panDelta * TouchPanSens);
                }
                if (Mathf.Abs(pinch) > 0.01f)
                {
                    mouseWheelEvents?.Invoke(new Vector2(0f, pinch * pinchZoomScale * TouchPinchSens));
                }
            }
        }
        else if (count == 1)
        {
            mousePos = p0;
            if (touchGesture == TouchGesture.PanZoom)
            {
                // 2本指→1本指：パン/ズーム終了（Rタップ判定はしない）
                EndCurrentTouchGesture(p0, false);
            }
            if (touchGesture == TouchGesture.None)
            {
                touchGesture = TouchGesture.Press;
                touchStartPos = p0;
                prvTouchPos = p0;
                touchStartTime = Time.unscaledTime;
            }
            else if (touchGesture == TouchGesture.Press)
            {
                if ((p0 - touchStartPos).magnitude > TouchDragThreshold)
                {
                    touchGesture = TouchGesture.Orbit;
                    prvTouchPos = p0;
                    mouseDownEvents?.Invoke(MouseButton.RightButton, p0);
                }
            }
            else if (touchGesture == TouchGesture.Orbit)
            {
                Vector2 delta = p0 - prvTouchPos;
                prvTouchPos = p0;
                if (delta.sqrMagnitude > 0f)
                {
                    mouseMoveEvents?.Invoke(p0, delta * TouchOrbitSens);
                }
            }
        }
        else
        {
            EndCurrentTouchGesture(mousePos, true);
        }
    }

    /// <summary>
    /// タッチジェスチャ終了（指を離した/本数変化時）。allowTap=true のときタップ系操作を発火。
    /// </summary>
    private void EndCurrentTouchGesture(Vector2 pos, bool allowTap)
    {
        switch (touchGesture)
        {
            case TouchGesture.Orbit:
                mouseUpEvents?.Invoke(MouseButton.RightButton, pos);
                break;
            case TouchGesture.PanZoom:
                mouseUpEvents?.Invoke(MouseButton.MiddleButton, pos);
                // 2本指タップ（移動なし・短時間）＝カメラ初期位置(Rキー相当)
                if (allowTap && !panZoomMoved && (Time.unscaledTime - panZoomStartTime < TapMaxTime))
                {
                    InvokeKey(Key.R);
                }
                break;
            case TouchGesture.Press:
                if (allowTap && (Time.unscaledTime - touchStartTime < TapMaxTime))
                {
                    // 1本指タップ＝選択（左クリック相当）
                    mouseDownEvents?.Invoke(MouseButton.LeftButton, touchStartPos);
                    mouseUpEvents?.Invoke(MouseButton.LeftButton, touchStartPos);
                    // ダブルタップ＝フォーカス(Fキー相当・再度でトグル)
                    if ((Time.unscaledTime - lastTapTime < DoubleTapTime)
                        && ((touchStartPos - lastTapPos).magnitude < DoubleTapDist))
                    {
                        InvokeKey(Key.F);
                        lastTapTime = 0f;
                    }
                    else
                    {
                        lastTapTime = Time.unscaledTime;
                        lastTapPos = touchStartPos;
                    }
                }
                break;
        }
        touchGesture = TouchGesture.None;
    }

    /// <summary>
    /// 登録済みキーアクションをコード側から発火（タッチ操作→既存のキー機能呼び出し用）
    /// </summary>
    private void InvokeKey(Key key)
    {
        if (keyActions.TryGetValue(key, out var act))
        {
            act?.Invoke(key, true, false, false);
        }
    }

    /// <summary>
    /// 画面内にマウスがあるかチェック
    /// </summary>
    /// <returns></returns>
    private bool IsMouseInsideScreen()
    {
        prvMousePos = mousePos;
        if (Mouse.current != null)
        {
            mousePos = Mouse.current.position.ReadValue();
        }
        // マウスが無い(タッチ)場合は ScreenTouchUpdate が mousePos を更新する
        return mousePos.x >= 0 && mousePos.x <= Screen.width && mousePos.y >= 0 && mousePos.y <= Screen.height;
    }

    /// <summary>
    /// キーごとのイベント登録
    /// </summary>
    /// <param name="key"></param>
    /// <param name="action"></param>
    public void RegisterKey(Key key, Action<Key, bool, bool, bool> action)
    {
        if (keyActions.ContainsKey(key))
        {
            keyActions[key] += action;
        }
        else
        {
            keyActions[key] = action;
        }
        keyValues[key] = false;
    }

    /// <summary>
    /// キーごとのベント登録解除
    /// </summary>
    /// <param name="key"></param>
    /// <param name="action"></param>
    public void UnregisterKey(Key key, Action<Key, bool, bool, bool> action)
    {
        if (keyActions.ContainsKey(key))
        {
            keyActions[key] -= action;
            if (keyActions[key] == null)
                keyActions.Remove(key);
        }
    }

    /// <summary>
    /// マウスダウンイベント登録
    /// </summary>
    /// <param name="action"></param>
    public void RegisterMouseDown(Action<MouseButton, Vector2> action)
    {
        mouseDownEvents += action;
    }

    /// <summary>
    /// マウスダウンイベント登録解除
    /// </summary>
    public void UnregisterMouseDown(Action<MouseButton, Vector2> action)
    {
        mouseDownEvents -= action;
    }

    /// <summary>
    /// マウスアップイベント登録
    /// </summary>
    public void RegisterMouseUp(Action<MouseButton, Vector2> action)
    {
        mouseUpEvents += action;
    }

    /// <summary>
    /// マウスダウンイベント登録解除
    /// </summary>
    public void UnregisterMouseUp(Action<MouseButton, Vector2> action)
    {
        mouseUpEvents -= action;
    }

    /// <summary>
    /// マウスホイールイベント登録
    /// </summary>
    public void RegisterMouseWheel(Action<Vector2> action)
    {
        mouseWheelEvents += action;
    }

    /// <summary>
    /// マウスホイールイベント登録解除
    /// </summary>
    public void UnregisterMouseWheel(Action<Vector2> action)
    {
        mouseWheelEvents -= action;
    }

    /// <summary>
    /// マウスホイールイベント登録
    /// </summary>
    public void RegisterMouseMove(Action<Vector2, Vector2> action)
    {
        mouseMoveEvents += action;
    }

    /// <summary>
    /// マウスホイールイベント登録解除
    /// </summary>
    public void UnregisterMouseMove(Action<Vector2, Vector2> action)
    {
        mouseMoveEvents -= action;
    }

    /// <summary>
    /// タッチダウンイベント登録
    /// </summary>
    public void RegisterTouchDown(Action<TouchButton, GameObject> action)
    {
        touchDownEvents += action;
    }

    /// <summary>
    /// タッチダウンイベント登録解除
    /// </summary>
    public void UnregisterTouchDown(Action<TouchButton, GameObject> action)
    {
        touchDownEvents -= action;
    }

    /// <summary>
    /// タッチアップイベント登録
    /// </summary>
    public void RegisterTouchUp(Action<TouchButton, GameObject> action)
    {
        touchUpEvents += action;
    }

    /// <summary>
    /// タッチアップイベント登録解除
    /// </summary>
    public void UnregisterTouchUp(Action<TouchButton, GameObject> action)
    {
        touchUpEvents -= action;
    }

    /// <summary>
    /// ボタンダウンイベント登録
    /// </summary>
    public void RegisterButtonDown(Action<ControllerButton> action)
    {
        buttonDownEvents += action;
    }

    /// <summary>
    /// ボタンダウンイベント登録解除
    /// </summary>
    public void UnregisterButtonDown(Action<ControllerButton> action)
    {
        buttonDownEvents -= action;
    }

    /// <summary>
    /// ボタンアップイベント登録
    /// </summary>
    public void RegisterButtonUp(Action<ControllerButton> action)
    {
        buttonUpEvents += action;
    }

    /// <summary>
    /// ボタンアップイベント登録解除
    /// </summary>
    public void UnregisterButtonUp(Action<ControllerButton> action)
    {
        buttonUpEvents -= action;
    }


    /// <summary>
    /// 左手レイの状態変更イベント
    /// </summary>
    /// <param name="obj"></param>
    private void rayHandL_WhenStateChanged(InteractorStateChangeArgs obj)
    {
        if (rayHandL.HasCandidate)
        {
            GlobalScript.rayLObject = rayHandL.Candidate.gameObject;
        }
        else
        {
            GlobalScript.rayLObject = null;
        }
    }

    /// <summary>
    /// 右手レイの状態変更イベント
    /// </summary>
    /// <param name="obj"></param>
    private void rayHandR_WhenStateChanged(InteractorStateChangeArgs obj)
    {
        if (rayHandR.HasCandidate)
        {
            GlobalScript.rayRObject = rayHandR.Candidate.gameObject;
        }
        else
        {
            GlobalScript.rayRObject = null;
        }
    }

    /// <summary>
    /// 左コントローラレイの状態変更イベント
    /// </summary>
    /// <param name="obj"></param>
    private void RayControllerL_WhenStateChanged(InteractorStateChangeArgs obj)
    {
        if (rayControllerL.HasCandidate)
        {
            GlobalScript.rayLObject = rayControllerL.Candidate.gameObject;
        }
        else
        {
            GlobalScript.rayLObject = null;
        }
    }

    /// <summary>
    /// 右コントローラレイの状態変更イベント
    /// </summary>
    /// <param name="obj"></param>
    private void RayControllerR_WhenStateChanged(InteractorStateChangeArgs obj)
    {
        if (rayControllerR.HasCandidate)
        {
            GlobalScript.rayRObject = rayControllerR.Candidate.gameObject;
        }
        else
        {
            GlobalScript.rayRObject = null;
        }
    }

}
