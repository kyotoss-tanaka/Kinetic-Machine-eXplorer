using System;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#else
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#endif

/// <summary>
/// hmx-link 接続用の軽量 WebSocket クライアント（外部パッケージ非依存・同梱）。
/// PC/Editor: System.Net.WebSockets.ClientWebSocket
/// WebGL    : ブラウザ WebSocket（Assets/Plugins/WebGL/KmxWebSocket.jslib 経由・ポーリング受信）
/// イベントは <see cref="DispatchMessageQueue"/> 呼び出し時（メインスレッド）に発火する。
/// テキスト(JSON)メッセージ専用。
/// </summary>
public class KmxWebSocket
{
    public enum WsState { Closed = 0, Connecting = 1, Open = 2, Closing = 3 }

    public event Action OnOpen;
    public event Action<string> OnMessage;
    public event Action<string> OnError;
    public event Action OnClose;

    private readonly string url;
    public WsState State { get; private set; } = WsState.Closed;

    public KmxWebSocket(string url)
    {
        this.url = url;
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    // ===== WebGL: jslib ブリッジ（ポーリング方式） =====
    [DllImport("__Internal")] private static extern int KmxWsConnect(string url);
    [DllImport("__Internal")] private static extern void KmxWsSend(int id, string data);
    [DllImport("__Internal")] private static extern void KmxWsClose(int id);
    [DllImport("__Internal")] private static extern int KmxWsGetState(int id);   // 0=closed,1=connecting,2=open,3=closing
    [DllImport("__Internal")] private static extern IntPtr KmxWsReceive(int id); // 次の受信メッセージ(無ければ 0)
    [DllImport("__Internal")] private static extern void KmxWsFree(IntPtr ptr);

    private int wsId = -1;
    private WsState prvState = WsState.Closed;

    public void Connect()
    {
        State = WsState.Connecting;
        prvState = WsState.Closed;
        wsId = KmxWsConnect(url);
    }

    public void Send(string data)
    {
        if (wsId >= 0)
        {
            KmxWsSend(wsId, data);
        }
    }

    public void Close()
    {
        if (wsId >= 0)
        {
            KmxWsClose(wsId);
            State = WsState.Closing;
        }
    }

    public void DispatchMessageQueue()
    {
        if (wsId < 0)
        {
            return;
        }
        // 受信メッセージを全て取り出す
        while (true)
        {
            IntPtr p = KmxWsReceive(wsId);
            if (p == IntPtr.Zero)
            {
                break;
            }
            string msg = Marshal.PtrToStringUTF8(p);
            KmxWsFree(p);
            if (msg != null)
            {
                OnMessage?.Invoke(msg);
            }
        }
        // 状態遷移を検出してイベント発火
        var s = (WsState)KmxWsGetState(wsId);
        if (s != prvState)
        {
            if (s == WsState.Open)
            {
                OnOpen?.Invoke();
            }
            else if (s == WsState.Closed)
            {
                OnClose?.Invoke();
            }
            prvState = s;
            State = s;
        }
    }
#else
    // ===== PC / Editor: System.Net.WebSockets =====
    private ClientWebSocket socket;
    private CancellationTokenSource cts;
    private readonly object queueLock = new object();
    private readonly Queue<Action> mainThreadQueue = new Queue<Action>();

    public async void Connect()
    {
        State = WsState.Connecting;
        socket = new ClientWebSocket();
        cts = new CancellationTokenSource();
        try
        {
            await socket.ConnectAsync(new Uri(url), cts.Token);
            State = WsState.Open;
            Enqueue(() => OnOpen?.Invoke());
            _ = ReceiveLoop();
        }
        catch (Exception e)
        {
            string m = e.Message;
            Enqueue(() => OnError?.Invoke(m));
            State = WsState.Closed;
            Enqueue(() => OnClose?.Invoke());
        }
    }

    private async Task ReceiveLoop()
    {
        var buffer = new byte[8192];
        var sb = new StringBuilder();
        try
        {
            while (socket != null && socket.State == WebSocketState.Open)
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        State = WsState.Closed;
                        Enqueue(() => OnClose?.Invoke());
                        return;
                    }
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);
                string msg = sb.ToString();
                Enqueue(() => OnMessage?.Invoke(msg));
            }
        }
        catch (Exception e)
        {
            string m = e.Message;
            Enqueue(() => OnError?.Invoke(m));
        }
        State = WsState.Closed;
        Enqueue(() => OnClose?.Invoke());
    }

    public void Send(string data)
    {
        if (socket != null && socket.State == WebSocketState.Open)
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            _ = socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
        }
    }

    public async void Close()
    {
        State = WsState.Closing;
        try
        {
            if (socket != null && socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
        }
        catch { }
        cts?.Cancel();
        State = WsState.Closed;
    }

    private void Enqueue(Action a)
    {
        lock (queueLock)
        {
            mainThreadQueue.Enqueue(a);
        }
    }

    public void DispatchMessageQueue()
    {
        lock (queueLock)
        {
            while (mainThreadQueue.Count > 0)
            {
                mainThreadQueue.Dequeue()?.Invoke();
            }
        }
    }
#endif
}
