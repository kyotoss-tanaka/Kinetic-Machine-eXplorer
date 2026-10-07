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
    /// IP アドレスの欄にポートも出すので広げる幅
    /// </summary>
    private const float IpWidenWidth = 50f;

    /// <summary>
    /// パネルの幅（Prefab の 500 ＋ IP の欄を広げた分）
    /// </summary>
    private const float PanelWidth = 500f + IpWidenWidth;

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Awake()
    {
        // IP アドレスの欄を広げて右の列を寄せる（Prefab は変えない。枠の組み立て前に直し、余白や背景を合わせる）
        WidenIpColumn();

        base.Awake();

        // 設定
        globalSetting = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(d => d.name == "GlobalSetting").ToList()[0];

        // コンポネント取得
        directComContents = GetComponentsInChildren<Transform>(true).ToList().Find(d => d.name == "DirectComContents").gameObject;
        directComContentsBase = GetComponentsInChildren<Transform>(true).ToList().Find(d => d.name == "DirectComContentsBase").gameObject;
    }

    /// <summary>
    /// IP アドレスの欄を「IP:ポート」が収まる幅に広げ、右の列（Ping・Connected・Cycle）をその分右へ寄せる
    /// </summary>
    private void WidenIpColumn()
    {
        var widen = new HashSet<string> { "TxtIpAddressTitle", "TxtIpAddress", "DirectComContentsTitle", "DirectComContentsBase", "DirectComContents" };
        var shift = new HashSet<string> { "TxtPingTitle", "TxtPing", "TxtConnectiionTitle", "TxtConnection", "TxtCycleTitle", "TxtCycle" };
        foreach (var rt in GetComponentsInChildren<RectTransform>(true))
        {
            if (widen.Contains(rt.name))
            {
                rt.sizeDelta += new Vector2(IpWidenWidth, 0f);
            }
            else if (shift.Contains(rt.name))
            {
                rt.anchoredPosition += new Vector2(IpWidenWidth, 0f);
            }
        }
        ((RectTransform)transform).sizeDelta += new Vector2(IpWidenWidth, 0f);
        var title = GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(d => d.name == "TxtIpAddressTitle");
        if (title != null)
        {
            title.text = "IP Address : Port";
        }
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
            SetPanelSize(new Vector2(PanelWidth, 60 + 30 * directComInfos.Count));   // 最小化中は展開時の大きさとして控えるだけ
            directComContents.GetComponent<RectTransform>().sizeDelta = new Vector2(PanelWidth, 30 * directComInfos.Count);
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
