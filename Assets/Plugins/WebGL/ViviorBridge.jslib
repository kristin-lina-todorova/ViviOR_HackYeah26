// ViviOR <-> website bridge (WebGL only). See website/VIVIOR-simulator-integration.md.
mergeInto(LibraryManager.library, {
  // the page's query string, e.g. "?op=lichtenstein&mode=train&embed=1"
  VB_Query: function () {
    var s = window.location.search || "";
    var n = lengthBytesUTF8(s) + 1, p = _malloc(n);
    stringToUTF8(s, p, n);
    return p;
  },
  // post a JSON message to the website that embeds us (no-op when opened on its own)
  VB_Post: function (json) {
    try {
      if (window.parent && window.parent !== window) window.parent.postMessage(JSON.parse(UTF8ToString(json)), "*");
    } catch (e) { console.warn("ViviOR bridge:", e); }
  }
});
