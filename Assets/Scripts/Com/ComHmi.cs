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

    [Header("JOG / 手動操作（hmx-link write）")]
    [SerializeField] private bool writerAuthed = false;
    [SerializeField] private int activeJogCount = 0;

    // hmx-link write/JOG（docs/hmx-link_write要求.md §3,§8）
    private static readonly List<ComHmi> instances = new();
    /// <summary>JOG状態変化通知（dev, isOn）。UI(UnitOperationView 等)が購読してハンドル表示を更新。</summary>
    public static event Action<string, bool> JogChanged;

    private readonly HashSet<string> writeAllow = new();
    private bool authSent;
    // ランプ（PLCがボタン認識を返す読取デバイス）。購読して値を保持し、ボタン点灯に使う。
    private readonly HashSet<string> lampDevs = new();
    private readonly Dictionary<string, bool> lampStates = new();
    private class JogState { public int seq; public float lastSend; public float lastAck; }
    private readonly Dictionary<string, JogState> jogs = new();

    private static string WriteToken => GlobalScript.hmxLink != null ? (GlobalScript.hmxLink.writeToken ?? "") : "";
    private static float JogInterval => Mathf.Max(0.02f, (GlobalScript.hmxLink != null ? GlobalScript.hmxLink.jogIntervalMs : 100) / 1000f);
    private static float JogTimeout => Mathf.Max(0.1f, (GlobalScript.hmxLink != null ? GlobalScript.hmxLink.jogTimeoutMs : 300) / 1000f);

    private void Awake()
    {
        if (!instances.Contains(this))
        {
            instances.Add(this);
        }
    }

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
        ws.OnClose += () => { state = "Closed"; OnWsClosed(); };
        state = "Connecting";
        ws.Connect();
    }

    private void HandleOpen()
    {
        state = "Open";
        reconnectDelay = 2f;
        subscribedDevs.Clear();   // 再接続時は購読をやり直す
        ResetWriteState();        // 認証/JOGは接続ごとにやり直し（安全側）
        Debug.Log($"[ComHmi] connected: {wsUrl}");
    }

    private void Update()
    {
        ws?.DispatchMessageQueue();
        ProcessJogs();   // JOGハートビート送信＋ack途絶ウォッチドッグ（毎フレーム）

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
        // ランプ読取デバイスは常に購読対象に含める（再接続後も復帰）
        foreach (var d in lampDevs)
        {
            subscribedDevs.Add(d);
        }
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
                    TrySendAuth();         // writer権限を要求（writeToken設定時のみ）
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
                case "auth_ack":
                    HandleAuthAck(root);
                    break;
                case "jog_ack":
                    HandleJogAck(root);
                    break;
                case "jog_timeout":
                    HandleJogTimeout(root);
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
            // ランプ（PLCのボタン認識返し）。UseDeviceListに無い内部IOなのでここで先に拾う。
            if (lampDevs.Contains(kv.Name))
            {
                double lv = kv.Value.ValueKind == JsonValueKind.Number ? kv.Value.GetDouble() : 0;
                lampStates[kv.Name] = lv != 0;
                matched++;
                continue;
            }
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
        EndAllJogs();
        instances.Remove(this);
        wantConnected = false;
        ws?.Close();
    }

    // ===== JOG / 手動操作（hmx-link write, docs/hmx-link_write要求.md §8） =====

    /// <summary>このユニットの dev を JOG 可能か（認証済み・allow内・接続中・記録再生中でない）</summary>
    public bool CanJog(string dev)
    {
        return writerAuthed
            && !string.IsNullOrEmpty(dev)
            && writeAllow.Contains(dev)
            && ws != null && ws.State == KmxWebSocket.WsState.Open
            && !GlobalScript.isSystemRecorder;
    }

    // --- 静的ルーティング（dev を扱えるインスタンスへ委譲。UI からはこちらを呼ぶ） ---

    /// <summary>いずれかの ComHmi が dev を JOG 可能か</summary>
    public static bool CanJogAny(string dev)
    {
        foreach (var c in instances)
        {
            if (c != null && c.CanJog(dev))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>ランプ（PLCのボタン認識返し）読取デバイスを購読登録（全インスタンス）。</summary>
    public static void RegisterLamp(string lampDev)
    {
        if (string.IsNullOrEmpty(lampDev))
        {
            return;
        }
        foreach (var c in instances)
        {
            if (c != null && c.lampDevs.Add(lampDev))
            {
                c.subscribedDevs.Add(lampDev);
                c.needSubscribe = true;
            }
        }
    }

    /// <summary>ランプが ON か（PLCがボタン認識を返している）。いずれかのインスタンスがONなら true。</summary>
    public static bool IsLampOn(string lampDev)
    {
        if (string.IsNullOrEmpty(lampDev))
        {
            return false;
        }
        foreach (var c in instances)
        {
            if (c != null && c.lampStates.TryGetValue(lampDev, out var on) && on)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>JOG開始（押下）。成功で true。</summary>
    public static bool BeginJog(string dev)
    {
        foreach (var c in instances)
        {
            if (c != null && c.CanJog(dev))
            {
                return c.BeginJogInternal(dev);
            }
        }
        return false;
    }

    /// <summary>JOG終了（離す）。全インスタンスに対し安全側で OFF。</summary>
    public static void EndJog(string dev)
    {
        foreach (var c in instances)
        {
            if (c != null)
            {
                c.EndJogInternal(dev);
            }
        }
    }

    private bool BeginJogInternal(string dev)
    {
        if (!CanJog(dev))
        {
            return false;
        }
        if (jogs.ContainsKey(dev))
        {
            return true;
        }
        float now = Time.unscaledTime;
        jogs[dev] = new JogState { seq = 1, lastSend = now, lastAck = now };
        ws.Send($"{{\"type\":\"jog\",\"dev\":\"{dev}\",\"val\":1,\"hold\":true,\"seq\":1}}");
        Debug.Log($"[ComHmi] JOG ON: {dev}");
        JogChanged?.Invoke(dev, true);
        return true;
    }

    private void EndJogInternal(string dev)
    {
        if (!jogs.TryGetValue(dev, out var st))
        {
            return;
        }
        jogs.Remove(dev);
        // 解除は即OFF送信（接続中のみ）。サーバ側ウォッチドッグもバックアップ。
        if (ws != null && ws.State == KmxWebSocket.WsState.Open)
        {
            ws.Send($"{{\"type\":\"jog\",\"dev\":\"{dev}\",\"val\":0,\"hold\":false,\"seq\":{++st.seq}}}");
        }
        Debug.Log($"[ComHmi] JOG OFF: {dev}");
        JogChanged?.Invoke(dev, false);
    }

    /// <summary>サーバが既にOFF済（jog_timeout等）。送信せずローカル停止＋通知。</summary>
    private void StopJogLocal(string dev)
    {
        if (jogs.Remove(dev))
        {
            JogChanged?.Invoke(dev, false);
        }
    }

    private void EndAllJogs()
    {
        if (jogs.Count == 0)
        {
            return;
        }
        var devs = new List<string>(jogs.Keys);
        foreach (var dev in devs)
        {
            EndJogInternal(dev);
        }
    }

    /// <summary>JOGハートビート(100ms)送信＋ack途絶ウォッチドッグ(Tout)。途絶で即OFF（§8.2 KMX側）。</summary>
    private void ProcessJogs()
    {
        if (jogs.Count == 0)
        {
            activeJogCount = 0;
            return;
        }
        float now = Time.unscaledTime;
        float interval = JogInterval, tout = JogTimeout;
        List<string> toStop = null;
        foreach (var kv in jogs)
        {
            var dev = kv.Key;
            var st = kv.Value;
            if (now - st.lastAck > tout)
            {
                // jog_ack 途絶 → ハートビート停止（サーバ側ウォッチドッグも作動しOFF）
                (toStop ??= new List<string>()).Add(dev);
                continue;
            }
            if (ws != null && ws.State == KmxWebSocket.WsState.Open && now - st.lastSend >= interval)
            {
                st.seq++;
                ws.Send($"{{\"type\":\"jog\",\"dev\":\"{dev}\",\"val\":1,\"hold\":true,\"seq\":{st.seq}}}");
                st.lastSend = now;
            }
        }
        if (toStop != null)
        {
            foreach (var dev in toStop)
            {
                Debug.LogWarning($"[ComHmi] JOG watchdog: ack 途絶 → OFF ({dev})");
                EndJogInternal(dev);
            }
        }
        activeJogCount = jogs.Count;
    }

    // --- 認証（writer role） ---

    private void TrySendAuth()
    {
        if (authSent)
        {
            return;
        }
        if (ws == null || ws.State != KmxWebSocket.WsState.Open)
        {
            return;
        }
        // 接続要求 §2.2: writer 認証は token 空運用でよい（token:"" でも auth を送る）。
        // 送らないと auth_ack(allow) が来ず、常に「writer未認証/allow外」になる。
        string token = WriteToken ?? "";
        ws.Send($"{{\"type\":\"auth\",\"role\":\"writer\",\"token\":\"{token}\"}}");
        authSent = true;
    }

    private void HandleAuthAck(JsonElement root)
    {
        bool ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
        writeAllow.Clear();
        writerAuthed = ok;
        if (ok && root.TryGetProperty("allow", out var allowEl) && allowEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in allowEl.EnumerateArray())
            {
                var s = a.GetString();
                if (!string.IsNullOrEmpty(s))
                {
                    writeAllow.Add(s);
                }
            }
            Debug.Log($"[ComHmi] writer auth OK (allow={writeAllow.Count})");
        }
        else
        {
            string msg = root.TryGetProperty("msg", out var m) ? m.GetString() : "";
            Debug.LogWarning($"[ComHmi] writer auth NG: {msg}");
        }
    }

    private void HandleJogAck(JsonElement root)
    {
        if (!root.TryGetProperty("dev", out var devEl))
        {
            return;
        }
        string dev = devEl.GetString();
        if (dev == null || !jogs.TryGetValue(dev, out var st))
        {
            return;
        }
        bool ok = !root.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.False;
        if (ok)
        {
            st.lastAck = Time.unscaledTime;   // ウォッチドッグ再武装
        }
        else
        {
            string msg = root.TryGetProperty("msg", out var m) ? m.GetString() : "";
            Debug.LogWarning($"[ComHmi] jog denied ({dev}): {msg} → OFF");
            EndJogInternal(dev);
        }
    }

    private void HandleJogTimeout(JsonElement root)
    {
        if (!root.TryGetProperty("dev", out var devEl))
        {
            return;
        }
        string dev = devEl.GetString();
        if (dev != null && jogs.ContainsKey(dev))
        {
            Debug.LogWarning($"[ComHmi] jog_timeout（サーバ自動OFF）: {dev}");
            StopJogLocal(dev);
        }
    }

    private void ResetWriteState()
    {
        EndAllJogs();
        writerAuthed = false;
        authSent = false;
        writeAllow.Clear();
    }

    private void OnWsClosed()
    {
        // 切断時: サーバ側ウォッチドッグがデバイスをOFFにする。ローカルJOGは停止＋通知（送信はしない）。
        if (jogs.Count > 0)
        {
            var devs = new List<string>(jogs.Keys);
            jogs.Clear();
            foreach (var dev in devs)
            {
                JogChanged?.Invoke(dev, false);
            }
        }
        writerAuthed = false;
        authSent = false;
    }

    private void OnApplicationFocus(bool focus)
    {
        if (!focus)
        {
            EndAllJogs();   // §8.3 フォーカス喪失で即OFF（runInBackground=trueでも動き続けるため必須）
        }
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            EndAllJogs();
        }
    }
}
