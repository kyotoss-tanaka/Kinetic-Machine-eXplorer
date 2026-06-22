// hmx-link 接続用 WebSocket ブリッジ（KmxWebSocket.cs から呼ばれる）。
// 受信はキューに溜め、C# 側が KmxWsReceive でポーリング取得する方式（関数ポインタ非依存で堅牢）。
var KmxWebSocketLib = {
  $kmxWs: {
    sockets: {},
    nextId: 1,
  },

  // 接続。戻り値=インスタンスID
  KmxWsConnect: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    var id = kmxWs.nextId++;
    var entry = { ws: null, state: 1 /*connecting*/, queue: [] };
    try {
      var ws = new WebSocket(url);
      entry.ws = ws;
      ws.onopen = function () { entry.state = 2; };       // open
      ws.onmessage = function (e) {
        if (typeof e.data === "string") {
          entry.queue.push(e.data);
        }
      };
      ws.onerror = function () { /* 状態は onclose で closed に */ };
      ws.onclose = function () { entry.state = 0; };      // closed
    } catch (err) {
      entry.state = 0;
    }
    kmxWs.sockets[id] = entry;
    return id;
  },

  // 送信
  KmxWsSend: function (id, dataPtr) {
    var e = kmxWs.sockets[id];
    if (e && e.ws && e.state === 2) {
      try { e.ws.send(UTF8ToString(dataPtr)); } catch (err) {}
    }
  },

  // 切断
  KmxWsClose: function (id) {
    var e = kmxWs.sockets[id];
    if (e && e.ws) {
      e.state = 3; // closing
      try { e.ws.close(); } catch (err) {}
    }
  },

  // 状態取得 (0=closed,1=connecting,2=open,3=closing)
  KmxWsGetState: function (id) {
    var e = kmxWs.sockets[id];
    return e ? e.state : 0;
  },

  // 受信メッセージを1件取り出す。無ければ 0。戻り値は呼び出し側が KmxWsFree で解放する。
  KmxWsReceive: function (id) {
    var e = kmxWs.sockets[id];
    if (!e || e.queue.length === 0) {
      return 0;
    }
    var msg = e.queue.shift();
    var len = lengthBytesUTF8(msg) + 1;
    var ptr = _malloc(len);
    stringToUTF8(msg, ptr, len);
    return ptr;
  },

  // KmxWsReceive が返したバッファを解放
  KmxWsFree: function (ptr) {
    _free(ptr);
  },
};

autoAddDeps(KmxWebSocketLib, '$kmxWs');
mergeInto(LibraryManager.library, KmxWebSocketLib);
