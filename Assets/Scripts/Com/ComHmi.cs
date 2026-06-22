using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Parameters;
using UnityEngine;

/// <summary>
/// hmx-link（PLCデバイス値の WebSocket ハブ）と接続するデジタルツイン通信クライアント。
/// ComMcProtocol 等（実PLC接続）の WebGL 版＝実機ミラー。ComInner(設計値)とは排他で使う。
/// 仕様: Doc/Unity連携仕様.md
///
/// データ駆動（Inspector設定なし。ParameterLoader ファクトリから Setup される）:
///  - dev↔tag は UseDeviceList.json (GlobalScript.useDeviceDatas) から構築。
///  - タグは tagDatas[database][mechId][tag] に登録（デバイススクリプトが読む先）。
///  - 購読は「実行中に読まれたタグ(TagInfo.wasRead)」に対応する dev のみ（動的・debounce再subscribe）。
///  - vals 受信で dev→tag を引いて値を反映 → 3D が実PLC値で動く。
///  - size:2 のデバイスは2レジスタで32bit表現（dev=下位16bit, dev+1=上位16bit）。両方購読し合成する。
/// </summary>
public class ComHmi : MonoBehaviour
{
    /// <summary>1デバイス(アドレス)→タグの紐づけ。size:2 は下位(shift=0)/上位(shift=16)に分かれる。</summary>
    private struct DevBinding
    {
        public string mechId;
        public string tag;
        public int shift;   // 0=下位16bit/単一, 16=上位16bit
        public int size;
    }

    private string database;                 // タグ登録先(=postgresエントリのName)
    private string wsUrl = "ws://localhost:8765";
    private int interval = 200;

    private KmxWebSocket ws;

    // dev(アドレス) -> 紐づけ
    private readonly Dictionary<string, DevBinding> devToBinding = new();
    // tagKey("mechId\ttag") -> 購読すべき dev 群（size:2は2件）
    private readonly Dictionary<string, List<string>> tagToDevs = new();
    // 現在購読中の dev
    private readonly HashSet<string> subscribedDevs = new();

    private bool needSubscribe;
    private float subscribeTimer;
    private float collectTimer;
    private float pingTimer;
    private float reconnectTimer;
    private float reconnectDelay = 2f;
    private bool wantConnected = true;

    [Header("状態（読み取り専用）")]
    [SerializeField] private string state = "Closed";
    [SerializeField] private int registeredTags = 0;
    [SerializeField] private int subscribedCount = 0;
    [SerializeField] private int valsMsgCount = 0;
    [SerializeField] private int lastValsApplied = 0;   // 直近 vals で反映できたデバイス数
    [SerializeField] private int lastValsTotal = 0;     // 直近 vals に含まれたデバイス数

    [Header("診断")]
    [Tooltip("subscribe/vals の中身をConsoleに出力（受信不具合の切り分け用）")]
    [SerializeField] private bool verboseLog = true;

    /// <summary>ファクトリからの初期化</summary>
    public void Setup(string database, string wsUrl, int interval)
    {
        // フォーカスを外しても WS 受信/反映・ping を止めない（デジタルツインは常時ミラー）
        Application.runInBackground = true;
        this.database = database;
        this.wsUrl = string.IsNullOrEmpty(wsUrl) ? "ws://localhost:8765" : wsUrl;
        this.interval = interval > 0 ? interval : 200;
        BuildMapAndRegisterTags();
        Connect();
    }

    /// <summary>UseDeviceList から dev↔tag マップを構築し、タグを登録する</summary>
    private void BuildMapAndRegisterTags()
    {
        if (!GlobalScript.tagDatas.ContainsKey(database))
        {
            GlobalScript.tagDatas.Add(database, new Dictionary<string, Dictionary<string, TagInfo>>());
        }
        foreach (var mech in GlobalScript.useDeviceDatas)
        {
            if (!GlobalScript.tagDatas[database].ContainsKey(mech.mechId))
            {
                GlobalScript.tagDatas[database].Add(mech.mechId, new Dictionary<string, TagInfo>());
            }
            var dct = GlobalScript.tagDatas[database][mech.mechId];
            foreach (var d in mech.devices)
            {
                if (string.IsNullOrEmpty(d.dev) || string.IsNullOrEmpty(d.tag))
                {
                    continue;
                }
                // タグ登録（デバイススクリプトが読む先）
                if (!dct.ContainsKey(d.tag) || dct[d.tag] == null)
                {
                    var ti = ScriptableObject.CreateInstance<TagInfo>();
                    ti.name = d.tag;
                    ti.Database = database;
                    ti.MechId = mech.mechId;
                    ti.Tag = d.tag;
                    ti.Device = d.dev;
                    ti.Size = d.size;
                    ti.Value = 0;
                    dct[d.tag] = ti;
                }

                string tagKey = mech.mechId + "\t" + d.tag;
                var devs = new List<string>();

                // 下位(または単一)
                devToBinding[d.dev] = new DevBinding { mechId = mech.mechId, tag = d.tag, shift = 0, size = d.size };
                devs.Add(d.dev);

                // size:2 → 上位ワード(番号+1)も
                if (d.size >= 2)
                {
                    string highDev = HighDev(d);
                    devToBinding[highDev] = new DevBinding { mechId = mech.mechId, tag = d.tag, shift = 16, size = d.size };
                    devs.Add(highDev);
                }

                tagToDevs[tagKey] = devs;
            }
        }
        registeredTags = tagToDevs.Count;
        Debug.Log($"[ComHmi] tags registered: {registeredTags} (devices={devToBinding.Count}, db={database})");
    }

    /// <summary>size:2 の上位ワードのデバイスアドレス（番号+1）。dev文字列の基数(10進/16進)に合わせて生成。</summary>
    private static string HighDev(DeciceArea d)
    {
        string hex = d.name + d.no.ToString("X");
        if (d.dev == hex && d.dev != d.name + d.no.ToString())
        {
            return d.name + (d.no + 1).ToString("X");   // 16進アドレス(X/Y/B/W等)
        }
        return d.name + (d.no + 1).ToString();           // 10進アドレス(D等。例: D12244→D12245)
    }

    private void Connect()
    {
        ws = new KmxWebSocket(wsUrl);
        ws.OnOpen += HandleOpen;
        ws.OnMessage += OnMessage;
        ws.OnError += (e) => Debug.LogWarning($"[ComHmi] error: {e}");
        ws.OnClose += () => { state = "Closed"; };
        state = "Connecting";
        ws.Connect();
    }

    private void HandleOpen()
    {
        state = "Open";
        reconnectDelay = 2f;
        subscribedDevs.Clear();   // 再接続時は購読をやり直す
        Debug.Log($"[ComHmi] connected: {wsUrl}");
    }

    private void Update()
    {
        ws?.DispatchMessageQueue();

        if (ws != null && ws.State == KmxWebSocket.WsState.Open)
        {
            // 読まれたタグ→dev を購読集合へ（0.5秒ごと）
            collectTimer += Time.unscaledDeltaTime;
            if (collectTimer >= 0.5f)
            {
                collectTimer = 0f;
                CollectReadDevices();
            }

            // 動的 subscribe（debounce: 1秒に1回まで）
            subscribeTimer += Time.unscaledDeltaTime;
            if (needSubscribe && subscribeTimer >= 1f)
            {
                subscribeTimer = 0f;
                needSubscribe = false;
                SendSubscribe();
            }

            // 死活監視: 2秒ごとに ping
            pingTimer += Time.unscaledDeltaTime;
            if (pingTimer >= 2f)
            {
                pingTimer = 0f;
                ws.Send("{\"type\":\"ping\"}");
            }
        }
        else if (wantConnected && (ws == null || ws.State == KmxWebSocket.WsState.Closed))
        {
            // 自動再接続（指数バックオフ）
            reconnectTimer += Time.unscaledDeltaTime;
            if (reconnectTimer >= reconnectDelay)
            {
                reconnectTimer = 0f;
                reconnectDelay = Mathf.Min(reconnectDelay * 1.5f, 30f);
                Connect();
            }
        }
    }

    /// <summary>実行中に読まれたタグ(wasRead)の dev（size:2は上位も）を購読集合に追加</summary>
    private void CollectReadDevices()
    {
        if (!GlobalScript.tagDatas.ContainsKey(database))
        {
            return;
        }
        foreach (var mech in GlobalScript.tagDatas[database])
        {
            foreach (var kv in mech.Value)
            {
                var ti = kv.Value;
                if (ti == null || !ti.wasRead)
                {
                    continue;
                }
                string tagKey = ti.MechId + "\t" + ti.Tag;
                if (tagToDevs.TryGetValue(tagKey, out var devs))
                {
                    foreach (var dev in devs)
                    {
                        if (subscribedDevs.Add(dev))
                        {
                            needSubscribe = true;
                        }
                    }
                }
            }
        }
    }

    private void SendSubscribe()
    {
        var sb = new StringBuilder();
        // KMX側実装要求 M4: 必ず readOnly:true を付ける / M5: connection は絶対に送らない
        // （read-only でも実 connection を送ると hmx-link が PLC ドライバを切替＝HMI接続が切れる）
        sb.Append("{\"type\":\"subscribe\",\"readOnly\":true,\"devices\":[");
        bool first = true;
        foreach (var dev in subscribedDevs)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(dev).Append('"');
        }
        sb.Append("],\"interval\":").Append(interval).Append('}');
        string payload = sb.ToString();
        ws.Send(payload);
        subscribedCount = subscribedDevs.Count;
        Debug.Log($"[ComHmi] subscribe sent ({subscribedCount} devices, readOnly)");
        if (verboseLog)
        {
            Debug.Log($"[ComHmi] subscribe payload: {payload}");
        }
    }

    private void OnMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var typeEl))
            {
                return;
            }
            switch (typeEl.GetString())
            {
                case "hello":
                    ws.Send("{\"type\":\"hello_ack\"}");
                    needSubscribe = true;
                    subscribeTimer = 1f;   // 次フレームで即 subscribe
                    break;
                case "subscribed":
                    // M6: subscribed.readOnly===true を確認（read-only受理確認）
                    bool ro = root.TryGetProperty("readOnly", out var roEl) && roEl.ValueKind == JsonValueKind.True;
                    if (!ro)
                    {
                        Debug.LogWarning($"[ComHmi] subscribed に readOnly:true がありません（HMX側read-only未対応/受理されていない可能性）: {json}");
                    }
                    else if (verboseLog)
                    {
                        Debug.Log($"[ComHmi] subscribed (readOnly OK): {json}");
                    }
                    break;
                case "vals":
                    if (root.TryGetProperty("vals", out var vals))
                    {
                        ApplyVals(vals);
                    }
                    break;
                case "pong":
                case "status":
                case "stats":
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ComHmi] parse error: {e.Message}");
        }
    }

    private void ApplyVals(JsonElement vals)
    {
        valsMsgCount++;
        if (!GlobalScript.tagDatas.ContainsKey(database))
        {
            return;
        }
        int total = 0, matched = 0;
        StringBuilder unmatched = null;
        foreach (var kv in vals.EnumerateObject())
        {
            total++;
            if (!devToBinding.TryGetValue(kv.Name, out var b))
            {
                // UseDeviceList に無い／アドレス書式が異なる dev
                if (verboseLog && (unmatched == null || unmatched.Length < 300))
                {
                    (unmatched ??= new StringBuilder()).Append(kv.Name).Append('=').Append(kv.Value).Append(' ');
                }
                continue;
            }
            matched++;
            double dval = kv.Value.ValueKind == JsonValueKind.Number ? kv.Value.GetDouble() : 0;
            if (!GlobalScript.tagDatas[database].TryGetValue(b.mechId, out var dct)
                || !dct.TryGetValue(b.tag, out var ti) || ti == null)
            {
                continue;
            }
            if (b.size >= 2)
            {
                // 32bit を2ワードで構成: 下位/上位それぞれの16bitだけ更新して合成
                int word = (int)dval & 0xFFFF;
                if (b.shift == 0)
                {
                    ti.Value = (ti.Value & ~0xFFFF) | word;          // 下位16bit
                }
                else
                {
                    ti.Value = (ti.Value & 0xFFFF) | (word << 16);   // 上位16bit
                }
                ti.fValue = ti.Value;
            }
            else
            {
                ti.Value = (int)dval;
                ti.fValue = (float)dval;
            }
        }
        lastValsTotal = total;
        lastValsApplied = matched;
        if (verboseLog)
        {
            Debug.Log($"[ComHmi] vals#{valsMsgCount}: {matched}/{total} applied (subscribed={subscribedDevs.Count})");
            if (unmatched != null)
            {
                Debug.LogWarning($"[ComHmi] vals に購読外/書式違いの dev: {unmatched}");
            }
        }
    }

    private void OnDestroy()
    {
        wantConnected = false;
        ws?.Close();
    }
}
