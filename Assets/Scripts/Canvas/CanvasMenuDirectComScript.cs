using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class CanvasMenuDirectComScript : CanvasMenuBaseScript
{
    // グローバル設定
    private GameObject globalSetting;

    private GameObject directComContentsBase;
    private GameObject directComContents;
    private List<GameObject> directComInfos = new();

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        // 設定
        globalSetting = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(d => d.name == "GlobalSetting").ToList()[0];

        // コンポネント取得
        directComContents = GetComponentsInChildren<Transform>(true).ToList().Find(d => d.name == "DirectComContents").gameObject;
        directComContentsBase = GetComponentsInChildren<Transform>(true).ToList().Find(d => d.name == "DirectComContentsBase").gameObject;
    }

    /// <summary>
    /// 初期化処理
    /// </summary>
    private void Initialize()
    {
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
                var directComInfo = Instantiate(directComContentsBase);
                directComInfo.transform.parent = directComContents.transform;
                directComInfo.transform.localPosition = new Vector3(0, - 30 * index, 0);
                directComInfo.SetActive(true);
                protocol.SetDirectCanvas(directComInfo);
                directComInfos.Add(directComInfo);
                index++;
            }
        }
        // キャンバス表示更新
        if (directComInfos.Count > 0)
        {
            GetComponent<RectTransform>().sizeDelta = new Vector2(500, 60 + 30 * directComInfos.Count);
            directComContents.GetComponent<RectTransform>().sizeDelta = new Vector2(500, 30 * directComInfos.Count);
        }
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void Update()
    {
        base.Update();
    }

    /// <summary>
    /// イベント登録
    /// </summary>
    public override void SetEvents()
    {
        base.SetEvents();
        Initialize();
    }

    /// <summary>
    /// イベント解除
    /// </summary>
    public override void ResetEvents()
    {
        base.ResetEvents();
    }

    #region イベント処理
    #endregion イベント処理
}
