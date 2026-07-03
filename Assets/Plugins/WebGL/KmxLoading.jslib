// Unity(WebGL) → JS 連携。HTMLテンプレート(index.html)の window.KmxLoading へ橋渡しする。
// WebGlLoadingBridge.cs から呼ばれ、ローディング画面の進捗バー・コメント・完了を更新する。
mergeInto(LibraryManager.library, {
  KmxLoadingProgress: function (p, labelPtr) {
    if (typeof window !== "undefined" && window.KmxLoading && window.KmxLoading.progress) {
      window.KmxLoading.progress(p, UTF8ToString(labelPtr));
    }
  },
  KmxLoadingDone: function () {
    if (typeof window !== "undefined" && window.KmxLoading && window.KmxLoading.done) {
      window.KmxLoading.done();
    }
  },
  KmxLoadingShow: function () {
    if (typeof window !== "undefined" && window.KmxLoading && window.KmxLoading.show) {
      window.KmxLoading.show();
    }
  }
});
