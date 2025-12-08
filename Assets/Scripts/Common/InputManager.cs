using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

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
    private Dictionary<Key, Action<Key, bool, bool, bool>> keyActions = new Dictionary<Key, Action<Key, bool, bool, bool>>();

    /// <summary>
    /// çXêVèàóù
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
    /// ÉLÅ[Ç…ëŒÇµÇƒÉCÉxÉìÉgìoò^
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
    /// ìoò^âèú
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
