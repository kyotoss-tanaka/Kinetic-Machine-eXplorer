using System;
using System.Collections.Generic;
using System.Linq;
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
    /// 起床イベント
    /// </summary>
    protected override void Awake()
    {
        base.Awake();
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

        // マウスアップデート
        MouseUpdate();

        // タッチアップデート
        TouchUpdate();
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
    /// タッチアップデート
    /// ボタン
    ///    A Button.One
    ///    B Button.Two
    ///    X Button.Three
    ///    Y Button.Four
    /// </summary>
    private void TouchUpdate()
    {
        if (GlobalScript.isXRMode)
        {
            if (OVRInput.GetDown(OVRInput.Button.Start))
            {
                buttonDownEvents?.Invoke(ControllerButton.Menu);
            }
            else if (OVRInput.GetUp(OVRInput.Button.Start))
            {
                buttonUpEvents?.Invoke(ControllerButton.Menu);
            }
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
    }

    /// <summary>
    /// 画面内にマウスがあるかチェック
    /// </summary>
    /// <returns></returns>
    private bool IsMouseInsideScreen()
    {
        prvMousePos = mousePos;
        mousePos = Mouse.current.position.ReadValue();
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
}
