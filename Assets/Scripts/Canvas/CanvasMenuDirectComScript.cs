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
    /// 1行の高さ（Prefab と同じ）
    /// </summary>
    private const float RowHeight = 30f;

    /// <summary>
    /// 応答時間の表に出す最大の行数（遅れの大きい順）
    /// </summary>
    private const int ResponseMaxRows = 8;

    /// <summary>
    /// 応答時間の表の列（左端・幅）。ユニット（完了IO）・予定・直近・最大の遅れ・遅れ回数/回数
    /// </summary>
    private static readonly (float x, float w)[] ResponseColumns = { (0f, 250f), (250f, 70f), (320f, 70f), (390f, 80f), (470f, 80f) };

    /// <summary>
    /// 遅れた行の文字の色
    /// </summary>
    private static readonly Color LateColor = new Color(1f, 0.6f, 0.2f, 1f);

    /// <summary>
    /// 応答時間の欄（名前に Contents を含めて、最小化で一緒に隠す）
    /// </summary>
    private RectTransform responseContents;
    private TextMeshProUGUI txtResponseSummary;
    private readonly List<TextMeshProUGUI[]> responseRows = new();
    private int responseShownVersion = -1;
    private int responseShownRows = -1;
    private float responseRefreshTimer;

    /// <summary>
    /// 通信先の行数（応答時間の欄はこの下に置く）
    /// </summary>
    private int comRowCount;

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

        // 応答時間の欄（SetEvents で最小化の対象を集める前に作る）
        BuildResponseContents();
    }

    /// <summary>
    /// 応答時間の欄を作る（Prefab は変えない）。
    /// 1行目：見出し・合計・リセット／2行目：列の見出し／以降：動作ごと（遅れの大きい順に最大 ResponseMaxRows 行）
    /// </summary>
    private void BuildResponseContents()
    {
        var pad = KmxUiStyle.PanelPadding;
        responseContents = new GameObject("ResponseContents", typeof(RectTransform)).GetComponent<RectTransform>();
        responseContents.SetParent(transform, false);
        responseContents.anchorMin = new Vector2(0f, 1f);
        responseContents.anchorMax = new Vector2(0f, 1f);
        responseContents.pivot = new Vector2(0f, 1f);
        responseContents.sizeDelta = new Vector2(PanelWidth, RowHeight * 3);

        // 1行目：見出し（PLC から開始IOを受けてから完了IOを送り終えるまで）・合計・リセット
        KmxUiStyle.AddText(responseContents, "TxtResponseTitle", new Vector2(pad, 0f), new Vector2(200f, RowHeight), "Response Time (ms)", TextAlignmentOptions.MidlineLeft);
        txtResponseSummary = KmxUiStyle.AddText(responseContents, "TxtResponseSummary", new Vector2(200f, 0f), new Vector2(PanelWidth - 200f - 80f, RowHeight), "", TextAlignmentOptions.MidlineRight);
        KmxUiStyle.AddButton(responseContents, "BtnResponseReset", new Vector2(PanelWidth - 70f - pad, -3f), new Vector2(70f, RowHeight - 6f), "Reset", () => ResponseStats.Clear());

        // 2行目：列の見出し（上の表の見出しと同じ帯）
        KmxUiStyle.AddBand(responseContents, "ResponseContentsTitle", -RowHeight, RowHeight, KmxUiStyle.HeaderBand);
        var titles = new[] { "Unit (Done IO)", "Plan", "Last", "Max Late", "Late/All" };
        for (int c = 0; c < titles.Length; c++)
        {
            KmxUiStyle.AddText(responseContents, "TxtResponseHead" + c, ColumnPos(c, -RowHeight), new Vector2(ResponseColumns[c].w - (c == 0 ? pad : 0f), RowHeight), titles[c],
                c == 0 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center);
        }

        // 動作ごとの行（使い回す。表示する行数だけ出す）
        for (int r = 0; r < ResponseMaxRows; r++)
        {
            var y = -RowHeight * (2 + r);
            var cells = new TextMeshProUGUI[ResponseColumns.Length];
            for (int c = 0; c < ResponseColumns.Length; c++)
            {
                cells[c] = KmxUiStyle.AddText(responseContents, $"TxtResponse{r}_{c}", ColumnPos(c, y), new Vector2(ResponseColumns[c].w - (c == 0 ? pad : 0f), RowHeight), "",
                    c == 0 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center);
            }
            responseRows.Add(cells);
        }
    }

    /// <summary>
    /// 応答時間の表の列の位置（1列目は左の余白を空ける）
    /// </summary>
    private static Vector2 ColumnPos(int column, float y)
    {
        return new Vector2(ResponseColumns[column].x + (column == 0 ? KmxUiStyle.PanelPadding : 0f), y);
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
        comRowCount = directComInfos.Count;
        if (directComInfos.Count > 0)
        {
            directComContents.GetComponent<RectTransform>().sizeDelta = new Vector2(PanelWidth, 30 * directComInfos.Count);
        }

        // 応答時間はロードし直したら数え直す（ユニットが作り直されるため）
        ResponseStats.Clear();
        responseContents.anchoredPosition = new Vector2(0f, -(60 + 30 * comRowCount));
        responseShownVersion = -1;
        responseShownRows = -1;
        RenewResponse();
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void Update()
    {
        base.Update();
        // 表示は間引く（通信スレッドから毎周期足されるため）
        responseRefreshTimer += Time.unscaledDeltaTime;
        if (responseRefreshTimer >= 0.5f)
        {
            responseRefreshTimer = 0f;
            RenewResponse();
        }
    }

    /// <summary>
    /// 応答時間の表を書き直す（集計が変わった時だけ）。行数が変わったらパネルの大きさも合わせる
    /// </summary>
    private void RenewResponse()
    {
        if (ResponseStats.Version == responseShownVersion)
        {
            return;
        }
        responseShownVersion = ResponseStats.Version;
        var list = ResponseStats.Snapshot(out var totalCount, out var totalLate);
        // ≥ はフォント（静的アトラス）に無いかもしれないので使わない
        txtResponseSummary.text = $"Late ({ResponseStats.LateThresholdMs:0}ms+) {totalLate} / {totalCount}";
        txtResponseSummary.color = totalLate > 0 ? LateColor : KmxUiStyle.Text;

        // 計測が無い間は1行で案内する（外部通信の開始IOで動くユニットだけ測る）
        var rows = Mathf.Max(1, Mathf.Min(list.Count, ResponseMaxRows));
        for (int r = 0; r < ResponseMaxRows; r++)
        {
            var cells = responseRows[r];
            var visible = r < rows;
            for (int c = 0; c < cells.Length; c++)
            {
                cells[c].gameObject.SetActive(visible);
            }
            if (!visible)
            {
                continue;
            }
            if (list.Count == 0)
            {
                cells[0].text = "No data (measured when a start IO arrives)";
                cells[0].overflowMode = TextOverflowModes.Overflow;
                cells[0].color = KmxUiStyle.Text;
                for (int c = 1; c < cells.Length; c++)
                {
                    cells[c].text = "";
                }
                continue;
            }
            var e = list[r];
            var late = e.maxLateMs >= ResponseStats.LateThresholdMs;
            cells[0].overflowMode = TextOverflowModes.Ellipsis;
            cells[0].text = $"{e.unit} ({e.end})";
            cells[1].text = e.expectedMs.ToString("0");
            cells[2].text = e.lastMs.ToString("0");
            cells[3].text = e.maxLateMs.ToString("+0;-0;0");
            cells[4].text = $"{e.lateCount}/{e.count}";
            foreach (var cell in cells)
            {
                cell.color = late ? LateColor : KmxUiStyle.Text;
            }
        }

        // 行数が変わった時だけ大きさを変える（最小化中は展開時の大きさとして控えるだけ）
        if (rows != responseShownRows)
        {
            responseShownRows = rows;
            var responseHeight = RowHeight * (2 + rows);
            responseContents.sizeDelta = new Vector2(PanelWidth, responseHeight);
            SetPanelSize(new Vector2(PanelWidth, 60 + 30 * comRowCount + responseHeight));
        }
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
