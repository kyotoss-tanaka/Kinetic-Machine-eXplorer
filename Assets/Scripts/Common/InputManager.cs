using Oculus.Interaction;
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

    /// <summary>
    /// 左タッチ
    /// </summary>
    private RayInteractor rayInteractorL = null;
    /// <summary>
    /// 右タッチ
    /// </summary>
    private RayInteractor rayInteractorR = null;

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
    /// マウス位置
    /// </summary>
    private Vector2 mousePos, prvMousePos;

    /// <summary>
    /// 各種ボタン状態
    /// </summary>
    private bool isMouseLeft, isMouseRight, isMouseMiddle;

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

        Vector2 scrollDelta = Mouse.current.scroll.ReadValue();
        if ((scrollDelta.x != 0) || (scrollDelta.y != 0))
        {
            mouseWheelEvents?.Invoke(scrollDelta);
        }
        Vector2 mouseDelta = mousePos - prvMousePos;
        if ((mouseDelta.x != 0) || (mouseDelta.y != 0))
        {
            mouseMoveEvents?.Invoke(mousePos, mouseDelta);
        }
    }

    /// <summary>
    /// タッチアップデート
    /// </summary>
    private void TouchUpdate()
    {
        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch))
        {
            touchDownEvents?.Invoke(TouchButton.LTouch, rayInteractorL.Interactable == null ? null : rayInteractorL.Interactable.gameObject);
        }
        else if (OVRInput.GetUp(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch))
        {
            touchUpEvents?.Invoke(TouchButton.LTouch, rayInteractorL.Interactable == null ? null : rayInteractorL.Interactable.gameObject);
        }
        else if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
        {
            touchDownEvents?.Invoke(TouchButton.RTouch, rayInteractorR.Interactable == null ? null : rayInteractorR.Interactable.gameObject);
        }
        else if (OVRInput.GetUp(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
        {
            touchUpEvents?.Invoke(TouchButton.RTouch, rayInteractorR.Interactable == null ? null : rayInteractorR.Interactable.gameObject);
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
}
