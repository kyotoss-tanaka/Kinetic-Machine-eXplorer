using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public class EventManager : BaseBehaviour
{
    private static EventManager _Instance;
    public static EventManager Instance
    {
        get
        {
            if (_Instance == null)
            {
                var mng = GameObject.FindObjectsByType<EventManager>(FindObjectsSortMode.None).ToList();
                if (mng.Count > 0)
                {
                    _Instance = mng[0];
                }
            }
            return _Instance;
        }
    }

    /// <summary>
    /// オブジェクト選択イベント
    /// </summary>
    private Action<GameObject> objectSelectEvents;

    /// <summary>
    /// オブジェクト選択イベント登録
    /// </summary>
    public void RegisterObjectSelect(Action<GameObject> action)
    {
        objectSelectEvents += action;
    }

    /// <summary>
    /// オブジェクト選択イベント登録解除
    /// </summary>
    public void UnregisterObjectSelect(Action<GameObject> action)
    {
        objectSelectEvents -= action;
    }

    /// <summary>
    /// オブジェクト選択イベント実行
    /// </summary>
    /// <param name="gameObject"></param>
    public void ProcessObjectSelect(GameObject gameObject)
    {
        GlobalScript.selectedObject = gameObject;
        objectSelectEvents?.Invoke(gameObject);
    }
}
