using Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.Windows;
using static System.Net.Mime.MediaTypeNames;

public class CanvasPrefabInfoScript : KssBaseScript
{
    private class PrefabButtonInfo
    {
        public string name;
        public Button button;
        public TextMeshProUGUI text;
        public GameObject prefab;
        public bool visible;
        public bool all;
    }

    // グローバル設定
    private GameObject globalSetting;

    // プレハブ
    private GameObject allPrefab;

    /// <summary>
    /// プレハブ
    /// </summary>
    private Button btnPrefab;

    /// <summary>
    /// プレハブ
    /// </summary>
    private List<GameObject> prefabs = new();

    /// <summary>
    /// 各種ボタン
    /// </summary>
    private List<PrefabButtonInfo> btnPrefabs = new();

    /// <summary>
    /// 各種表示
    /// </summary>
    private List<bool> visibles = new();

    #region 初期化処理
    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        // キャンバス作成
        CreateCanvas();
    }

    /// <summary>
    /// 初期化処理
    /// </summary>
    private void Initialize()
    {
        // 設定
        globalSetting = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(d => d.name == "GlobalSetting").ToList()[0];
        allPrefab = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(d => d.name == "PrefabObjects").ToList()[0];
        btnPrefab = GetComponentsInChildren<Button>(true).ToList().Find(d => d.name == "BtnPrefab");

        // イベント削除
        foreach (var btn in GetComponentsInChildren<Button>())
        {
            btn.onClick.RemoveAllListeners();
            Destroy(btn.gameObject);
        }
        var prvPrefabs = new List<PrefabButtonInfo>();
        prvPrefabs.AddRange(btnPrefabs);
        btnPrefabs.Clear();

        // プレハブ取得
        prefabs.Clear();
        for (var i = 0; i < allPrefab.transform.childCount; i++)
        {
            prefabs.Add(allPrefab.transform.GetChild(i).gameObject);
        }

        //　各種ボタン作成
        CreateButton(null);
        var dctName = new Dictionary<string, List<PrefabButtonInfo>>();
        foreach (var prefab in prefabs)
        {
            var info = CreateButton(prefab);
            if (!dctName.ContainsKey(info.name))
            {
                dctName.Add(info.name, new());
            }
            dctName[info.name].Add(info);
        }
        // 同一名称チェック
        foreach (var info in dctName.Where(d => d.Value.Count > 1).ToList())
        {
            for (var i = 0; i < info.Value.Count; i++)
            {
                var name = info.Key + "-" + (i + 1);
                info.Value[i].name = name;
                info.Value[i].text.text = name;
            }
        }
        dctName.Clear();

        // 前回の状態復帰
        foreach (var prv in prvPrefabs.Where(d => !d.visible))
        {
            var info = btnPrefabs.Find(d => d.name == prv.name);
            if (info != null)
            {
                // 前回非表示
                btnPrefab_onClick(info);
            }
        }
        prvPrefabs.Clear();
    }

    /// <summary>
    /// イベントセット
    /// </summary>
    public void SetEvents()
    {
        Initialize();

        ResetEvents();
        foreach (var btn in btnPrefabs)
        {
            btn.button.onClick.AddListener(() => btnPrefab_onClick(btn));
        }
    }

    /// <summary>
    /// イベントリセット
    /// </summary>
    public void ResetEvents()
    {
        foreach (var btn in btnPrefabs)
        {
            btn.button.onClick.RemoveAllListeners();
        }
    }
    #endregion 初期化処理

    #region イベント
    /// <summary>
    /// ボタンクリックイベント
    /// </summary>
    private void btnPrefab_onClick(PrefabButtonInfo info)
    {
        info.visible = !info.visible;
        SetButtonColor(info.button, info.visible);
        info.prefab.SetActive(info.visible);
    }
    #endregion イベント

    #region メソッド
    /// <summary>
    /// ボタンの色セット
    /// </summary>
    /// <param name="button"></param>
    /// <param name="value"></param>
    private void SetButtonColor(Button button, bool value)
    {
        SetButtonColor(button, value ? Color.white : new Color(0.6f, 0.6f, 0.6f));
    }

    /// <summary>
    /// ボタンの色セット
    /// </summary>
    /// <param name="button"></param>
    /// <param name="color"></param>
    private void SetButtonColor(Button button, Color color)
    {
        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = color;
        button.colors = colors;
        button.targetGraphic.color = colors.normalColor;
    }

    /// <summary>
    /// ボタン作成
    /// </summary>
    /// <param name=""></param>
    private PrefabButtonInfo CreateButton(GameObject prefab)
    {
        var name = "ALL";
        var all = prefab == null;
        if (all)
        {
            prefab = allPrefab;
        }
        else
        {
            var names = prefab.name.Split('-');
            if (names.Length > 1)
            {
                name = names[1];
            }
            else if (names.Length == 1)
            {
                name = names[0].Substring(0, 2);
            }
            else
            {
                return null;
            }
            btnPrefabs.Find(d => d.name == name);
        }
        var btn = Instantiate(btnPrefab);
        btn.transform.SetParent(transform, false);
        btn.gameObject.SetActive(true);
        var text = btn.GetComponentInChildren<TextMeshProUGUI>();
        text.text = name;

        var info = new PrefabButtonInfo
        {
            name = name,
            text = text,
            button = btn,
            prefab = prefab,
            visible = true,
            all = all
        };

        btnPrefabs.Add(info);
        return info;
    }

    /// <summary>
    /// キャンバス作成
    /// </summary>
    private void CreateCanvas()
    {
    }
    #endregion メソッド
}
