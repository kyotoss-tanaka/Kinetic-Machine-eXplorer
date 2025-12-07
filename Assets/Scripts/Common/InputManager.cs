using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : BaseBehaviour
{
    public static InputManager Instance { get; set; }
    private Dictionary<Key, Action<Key, bool, bool, bool>> keyActions = new Dictionary<Key, Action<Key, bool, bool, bool>>();

    /// <summary>
    /// 起床処理
    /// </summary>
    protected override void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void Update()
    {
        foreach (var kvp in keyActions)
        {
            var key = kvp.Key;
            var action = kvp.Value;
            if (Keyboard.current[key].wasPressedThisFrame)
            {
                action?.Invoke(key, true, Keyboard.current.ctrlKey.isPressed, Keyboard.current.shiftKey.isPressed);
            }
            else if (Keyboard.current[key].wasReleasedThisFrame)
            {
                action?.Invoke(key, false, Keyboard.current.ctrlKey.isPressed, Keyboard.current.shiftKey.isPressed);
            }
        }
    }

    /// <summary>
    /// キーに対してイベント登録
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
    }

    /// <summary>
    /// 登録解除
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
}
