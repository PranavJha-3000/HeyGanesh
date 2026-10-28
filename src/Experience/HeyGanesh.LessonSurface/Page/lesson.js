// Lesson card renderer (FEATURE-007 Phase 3, DEC-020).
//
// The host (CardSurface) posts validated beats; this page turns each op into DOM/SVG and posts
// "done" for the beat's seq. Every string here came from a model and may echo screen text, so it
// only ever reaches the document through textContent / createTextNode — never as markup. Plot
// points arrive computed by the host; nothing from the model is evaluated here.
(function () {
  "use strict";

  var SVG = "http://www.w3.org/2000/svg";
  var content = document.getElementById("content");
  var ink = document.getElementById("ink");
  var card = document.getElementById("card");
  var titleEl = document.getElementById("title");

  var arrows = [];          // { key, from, to, label }
  var highlights = [];      // element ids, in order
  var drawn = new Set();    // ink keys already animated once
  var noticeText = "";

  function post(msg) { window.chrome.webview.postMessage(msg); }

  // ---------- small DOM helpers (text only) ----------
  function el(tag, cls, text) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text !== undefined && text !== null) e.textContent = text;
    return e;
  }

  function svg(tag, attrs) {
    var e = document.createElementNS(SVG, tag);
    for (var k in attrs) if (Object.prototype.hasOwnProperty.call(attrs, k)) e.setAttribute(k, String(attrs[k]));
    return e;
  }

  function reveal(node) {
    content.appendChild(node);
    requestAnimationFrame(function () { node.classList.add("in"); });
  }

  function byId(id) {
    var all = content.querySelectorAll("[data-id]");
    for (var i = 0; i < all.length; i++) if (all[i].getAttribute("data-id") === id) return all[i];
    return null;
  }

  // Seeded jitter so a stroke looks hand-drawn but is redrawn identically on relayout.
  function jitter(seed) {
    var s = 0;
    for (var i = 0; i < seed.length; i++) s = (s * 31 + seed.charCodeAt(i)) | 0;
    return function () { s = (s * 1103515245 + 12345) | 0; return ((s >>> 8) & 0xffff) / 0xffff - 0.5; };
  }

  // ---------- TeX-lite: a readable subset until KaTeX is vendored ----------
  var SYMBOLS = {
    cdot: "·", times: "×", pm: "±", to: "→", rightarrow: "→", leftarrow: "←", leq: "≤", geq: "≥",
    neq: "≠", approx: "≈", infty: "∞", partial: "∂", nabla: "∇", sum: "∑", prod: "∏", int: "∫",
    alpha: "α", beta: "β", gamma: "γ", delta: "δ", Delta: "Δ", epsilon: "ε", theta: "θ", lambda: "λ",
    mu: "μ", pi: "π", rho: "ρ", sigma: "σ", Sigma: "Σ", tau: "τ", phi: "φ", omega: "ω", Omega: "Ω",
    ln: "ln", log: "log", sin: "sin", cos: "cos", tan: "tan", exp: "exp", lim: "lim",
    ",": " ", ";": " ", ":": " ", " ": " ", quad: " ", qquad: "  ",
    "{": "{", "}": "}", "%": "%", "$": "$", "#": "#", "&": "&", "_": "_"
  };

  function tokenize(src) {
    var t = [], i = 0;
    while (i < src.length) {
      var c = src[i];
      if (c === "\\") {
        var j = i + 1;
        if (j < src.length && /[A-Za-z]/.test(src[j])) {
          while (j < src.length && /[A-Za-z]/.test(src[j])) j++;
          t.push({ cmd: src.slice(i + 1, j) }); i = j;
        } else { t.push({ cmd: src[j] || "" }); i = j + 1; }
      } else if ("{}^_&".indexOf(c) >= 0) { t.push({ op: c }); i++; }
      else { t.push({ ch: c }); i++; }
    }
    return t;
  }

  function tex(src) {
    var toks = tokenize(src), p = 0;
    function peek() { return toks[p]; }
    function isOp(t, o) { return t && t.op === o; }

    function seq(stopAtBrace) {
      var f = document.createDocumentFragment();
      while (p < toks.length) {
        if (stopAtBrace && isOp(peek(), "}")) break;
        if (isOp(peek(), "&") || (peek().cmd === "\\") || peek().cmd === "end") break;
        f.appendChild(scripted());
      }
      return f;
    }

    function scripted() {
      var base = atom();
      var wrap = el("span");
      wrap.appendChild(base);
      while (isOp(peek(), "^") || isOp(peek(), "_")) {
        var tag = peek().op === "^" ? "sup" : "sub"; p++;
        var s = el(tag); s.appendChild(arg()); wrap.appendChild(s);
      }
      return wrap;
    }

    function arg() {
      if (isOp(peek(), "{")) { p++; var g = seq(true); if (isOp(peek(), "}")) p++; return g; }
      if (p < toks.length) return atom();
      return document.createDocumentFragment();
    }

    function rawGroup() {
      var s = "";
      if (isOp(peek(), "{")) { p++; while (p < toks.length && !isOp(peek(), "}")) { var t = toks[p++]; s += t.ch || t.cmd || t.op || ""; } p++; }
      return s;
    }

    function matrix() {
      var table = el("span", "matrix"), row = el("span", "row"), cell = el("span", "cell");
      while (p < toks.length) {
        var t = peek();
        if (t.cmd === "end") { p++; rawGroup(); break; }
        if (isOp(t, "&")) { p++; row.appendChild(cell); cell = el("span", "cell"); continue; }
        if (t.cmd === "\\") { p++; row.appendChild(cell); table.appendChild(row); row = el("span", "row"); cell = el("span", "cell"); continue; }
        cell.appendChild(scripted());
      }
      row.appendChild(cell); table.appendChild(row);
      return table;
    }

    function atom() {
      var t = toks[p++];
      if (!t) return document.createDocumentFragment();
      if (t.ch !== undefined) return document.createTextNode(t.ch);
      if (t.op === "{") { var g = seq(true); if (isOp(peek(), "}")) p++; return g; }
      if (t.op) return document.createTextNode(t.op);
      switch (t.cmd) {
        case "frac": case "dfrac": case "tfrac": {
          var f = el("span", "frac"), n = el("span"), d = el("span");
          n.appendChild(arg()); d.appendChild(arg()); f.appendChild(n); f.appendChild(d); return f;
        }
        case "sqrt": {
          var w = el("span"); w.appendChild(document.createTextNode("√"));
          var r = el("span", "sqrt"); r.appendChild(arg()); w.appendChild(r); return w;
        }
        case "text": case "mathrm": case "textbf": case "mathbf": case "operatorname": {
          var s = el("span"); s.style.fontStyle = "normal"; s.appendChild(arg()); return s;
        }
        case "xrightarrow": {
          var x = el("span", "xarrow"), lab = el("span"); lab.appendChild(arg());
          x.appendChild(lab); x.appendChild(el("span", null, "⟶")); return x;
        }
        case "begin": rawGroup(); return matrix();
        case "left": case "right": return document.createDocumentFragment();
        default:
          if (Object.prototype.hasOwnProperty.call(SYMBOLS, t.cmd)) return document.createTextNode(SYMBOLS[t.cmd]);
          return document.createTextNode(t.cmd);
      }
    }

    var out = document.createDocumentFragment();
    while (p < toks.length) {
      out.appendChild(seq(false));
      if (p < toks.length) p++; // stray & / \\ / \end outside a matrix: skip it
    }
    return out;
  }

  // ---------- glyphs: ten hand-drawn icons, a closed set matching LessonVocabulary.Glyphs ----------
  var GLYPHS = {
    atom: ["M32 32m-4 0a4 4 0 1 0 8 0a4 4 0 1 0 -8 0", "M8 32c0-8 48-8 48 0s-48 8-48 0", "M18 12c7-4 35 36 28 40s-35-36-28-40", "M46 12c7 4-21 44-28 40s21-44 28-40"],
    bulb: ["M32 8c-11 0-18 8-18 17 0 7 5 11 8 15v6h20v-6c3-4 8-8 8-15 0-9-7-17-18-17", "M24 52h16", "M26 57h12"],
    book: ["M8 14c8-3 16-3 24 2v38c-8-5-16-5-24-2z", "M56 14c-8-3-16-3-24 2v38c8-5 16-5 24-2z"],
    cell: ["M32 8c14 0 24 10 24 24s-10 24-24 24S8 46 8 32 18 8 32 8", "M32 24c5 0 8 4 8 8s-3 8-8 8-8-4-8-8 3-8 8-8", "M18 20l3 2M44 44l3 2M44 18l-2 3"],
    flask: ["M24 8h16", "M27 8v16L12 52c-1 3 1 5 4 5h32c3 0 5-2 4-5L37 24V8", "M18 42h28"],
    gear: ["M32 22a10 10 0 1 0 0.1 0", "M32 6v8M32 50v8M6 32h8M50 32h8M13 13l6 6M45 45l6 6M51 13l-6 6M19 45l-6 6"],
    leaf: ["M12 52C12 24 30 10 54 10c0 26-16 42-42 42z", "M12 52L40 24", "M26 38h-8M34 30v-9"],
    magnet: ["M14 10v22a18 18 0 0 0 36 0V10H40v22a8 8 0 0 1-16 0V10z", "M14 18h10M40 18h10"],
    cycle: ["M50 26A20 20 0 0 0 16 20", "M16 12v8h8", "M14 38a20 20 0 0 0 34 6", "M48 52v-8h-8"],
    scale: ["M32 8v46", "M20 54h24", "M10 18h44", "M10 18l-6 16h12zM54 18l-6 16h12z"]
  };

  function icon(glyph) {
    var s = svg("svg", { viewBox: "0 0 64 64" });
    (GLYPHS[glyph] || []).forEach(function (d) { s.appendChild(svg("path", { d: d, "class": "stroke" })); });
    return s;
  }

  // ---------- ops ----------
  function write(op) {
    var row = el("div", "el " + (op.style || "body"));
    row.setAttribute("data-id", op.id);
    row.setAttribute("data-kind", "write");
    row.appendChild(el("span", "text", op.text));
    if (op.math) { var m = el("span", "math"); m.appendChild(tex(op.math)); row.appendChild(m); }
    reveal(row);
  }

  function step(op) {
    var row = el("div", "el step");
    row.setAttribute("data-id", op.id);
    row.setAttribute("data-kind", "step");
    row.appendChild(el("span", "num", String(op.index)));
    row.appendChild(el("span", "text", op.text));
    reveal(row);
  }

  function iconOp(op) {
    var wrap = el("div", "el icon");
    wrap.setAttribute("data-id", op.id);
    wrap.setAttribute("data-kind", "icon");
    wrap.appendChild(icon(op.glyph));
    reveal(wrap);
  }

  function plot(op) {
    var W = 400, H = 200, M = 28;
    var pts = op.points || [];
    var ys = pts.filter(function (p) { return p; }).map(function (p) { return p[1]; });
    var lo = Math.min.apply(null, ys.length ? ys : [0]), hi = Math.max.apply(null, ys.length ? ys : [1]);
    if (!(hi > lo)) { lo -= 1; hi += 1; }
    var pad = (hi - lo) * 0.1; lo -= pad; hi += pad;
    function X(x) { return M + (x - op.xMin) / (op.xMax - op.xMin) * (W - 2 * M); }
    function Y(y) { return H - M - (y - lo) / (hi - lo) * (H - 2 * M); }

    var s = svg("svg", { viewBox: "0 0 " + W + " " + H, preserveAspectRatio: "none" });
    var axisY = lo <= 0 && hi >= 0 ? Y(0) : H - M;
    var axisX = op.xMin <= 0 && op.xMax >= 0 ? X(0) : M;
    s.appendChild(svg("path", { d: "M" + M + " " + axisY + "H" + (W - M), "class": "axis" }));
    s.appendChild(svg("path", { d: "M" + axisX + " " + M + "V" + (H - M), "class": "axis" }));
    var minLabel = svg("text", { x: M, y: H - 8, "class": "label" }); minLabel.textContent = String(op.xMin);
    var maxLabel = svg("text", { x: W - M - 10, y: H - 8, "class": "label" }); maxLabel.textContent = String(op.xMax);
    s.appendChild(minLabel); s.appendChild(maxLabel);

    var d = "", pen = false, count = 0;
    pts.forEach(function (p) {
      if (!p) { pen = false; return; }
      d += (pen ? "L" : "M") + X(p[0]).toFixed(1) + " " + Y(p[1]).toFixed(1);
      pen = true; count++;
    });
    var curve = svg("path", { d: d, "class": "stroke red draw" });
    s.appendChild(curve);

    var wrap = el("div", "el plot");
    wrap.setAttribute("data-id", op.id);
    wrap.setAttribute("data-kind", "plot");
    wrap.setAttribute("data-points", String(count));
    wrap.appendChild(s);
    reveal(wrap);
    requestAnimationFrame(function () {
      try { curve.style.setProperty("--len", String(Math.ceil(curve.getTotalLength()))); } catch (e) { /* zero-length path */ }
    });
  }

  function clear() {
    content.replaceChildren();
    arrows = []; highlights = []; drawn = new Set(); noticeText = "";
  }

  function apply(op) {
    switch (op.op) {
      case "write": write(op); break;
      case "step": step(op); break;
      case "icon": iconOp(op); break;
      case "plot": plot(op); break;
      case "arrow": arrows.push({ key: "a:" + op.from + ">" + op.to + ":" + arrows.length, from: op.from, to: op.to, label: op.label }); break;
      case "highlight":
        if (highlights.indexOf(op.target) < 0) highlights.push(op.target);
        var t = byId(op.target); if (t) t.classList.add("hl");
        break;
      case "clear": clear(); break;
    }
  }

  // ---------- ink layer: arrows and highlight rings, recomputed from element boxes ----------
  function box(node) {
    var c = card.getBoundingClientRect(), r = node.getBoundingClientRect();
    return { x: r.left - c.left, y: r.top - c.top, w: r.width, h: r.height, cx: r.left - c.left + r.width / 2, cy: r.top - c.top + r.height / 2 };
  }

  function edge(b, tx, ty) {
    var dx = tx - b.cx, dy = ty - b.cy;
    if (dx === 0 && dy === 0) return { x: b.cx, y: b.cy };
    var sx = dx === 0 ? Infinity : (b.w / 2 + 6) / Math.abs(dx);
    var sy = dy === 0 ? Infinity : (b.h / 2 + 6) / Math.abs(dy);
    var s = Math.min(sx, sy);
    return { x: b.cx + dx * s, y: b.cy + dy * s };
  }

  function stroke(d, key, cls) {
    var path = svg("path", { d: d, "class": "stroke " + (cls || "") });
    ink.appendChild(path);
    if (!drawn.has(key)) {
      drawn.add(key);
      try { path.style.setProperty("--len", String(Math.ceil(path.getTotalLength()))); path.classList.add("draw"); } catch (e) { /* ignore */ }
    }
    return path;
  }

  // Shrink the card's content to fit rather than cut a plot or a step off the bottom.
  function fit() {
    content.style.zoom = "1";
    var z = 1;
    while (z > 0.6 && content.scrollHeight > content.clientHeight + 1) {
      z -= 0.05;
      content.style.zoom = String(z);
    }
  }

  function redrawInk() {
    ink.replaceChildren();
    arrows.forEach(function (a) {
      var A = byId(a.from), B = byId(a.to);
      if (!A || !B) return;
      var ba = box(A), bb = box(B), r = jitter(a.key), s, e, cx, cy;
      var overlapX = Math.min(ba.x + ba.w, bb.x + bb.w) - Math.max(ba.x, bb.x);
      if (overlapX > 0) {
        // Stacked items: a straight arrow would be a stub in the gap, so curve round the right side.
        var right = Math.max(ba.x + ba.w, bb.x + bb.w) + 8;
        s = { x: ba.x + ba.w + 6, y: ba.cy };
        e = { x: bb.x + bb.w + 6, y: bb.cy };
        cx = right + 34 + r() * 8; cy = (s.y + e.y) / 2;
      } else {
        s = edge(ba, bb.cx, bb.cy); e = edge(bb, ba.cx, ba.cy);
        var len = Math.hypot(e.x - s.x, e.y - s.y) || 1, bend = len * (0.12 + r() * 0.06);
        cx = (s.x + e.x) / 2 - (e.y - s.y) / len * bend; cy = (s.y + e.y) / 2 + (e.x - s.x) / len * bend;
      }
      var mx = cx, my = cy;
      stroke("M" + s.x + " " + s.y + "Q" + cx + " " + cy + " " + e.x + " " + e.y, a.key);
      var ang = Math.atan2(e.y - cy, e.x - cx);
      var h1 = ang + 2.6, h2 = ang - 2.6;
      stroke("M" + (e.x + 12 * Math.cos(h1)) + " " + (e.y + 12 * Math.sin(h1)) + "L" + e.x + " " + e.y + "L" + (e.x + 12 * Math.cos(h2)) + " " + (e.y + 12 * Math.sin(h2)), a.key + ":head");
      if (a.label) {
        var t = svg("text", { x: mx + 4, y: my - 6, "class": "label", "text-anchor": "start" });
        t.textContent = a.label;
        ink.appendChild(t);
      }
    });
    highlights.forEach(function (id) {
      var n = byId(id);
      if (!n) return;
      var b = box(n), r = jitter("h:" + id), rx = b.w / 2 + 14, ry = b.h / 2 + 8;
      var d = "";
      for (var i = 0; i <= 28; i++) {
        var a = (i / 24) * Math.PI * 2 - 0.4, k = 1 + r() * 0.05;
        d += (i ? "L" : "M") + (b.cx + Math.cos(a) * rx * k).toFixed(1) + " " + (b.cy + Math.sin(a) * ry * k).toFixed(1);
      }
      stroke(d, "h:" + id, "red");
    });
  }

  // ---------- read-only description for the host's verification rows ----------
  window.__describe = function () {
    var ids = [], texts = {}, plotPoints = 0;
    content.querySelectorAll("[data-id]").forEach(function (n) {
      var id = n.getAttribute("data-id");
      ids.push(id);
      var kind = n.getAttribute("data-kind");
      if (kind === "write" || kind === "step") texts[id] = n.querySelector(".text").textContent;
      if (kind === "plot") plotPoints += parseInt(n.getAttribute("data-points"), 10) || 0;
    });
    return {
      title: titleEl.textContent,
      ids: ids,
      texts: texts,
      arrows: arrows.filter(function (a) { return byId(a.from) && byId(a.to); }).length,
      highlights: highlights.filter(function (id) { return byId(id); }),
      plotPoints: plotPoints,
      img: document.querySelectorAll("img").length,
      script: document.querySelectorAll("script").length,
      bold: document.querySelectorAll("b").length,
      notice: noticeText
    };
  };

  // ---------- messages from the host ----------
  window.chrome.webview.addEventListener("message", function (e) {
    var m = e.data || {};
    try {
      switch (m.kind) {
        case "reset": clear(); titleEl.textContent = m.title || ""; ink.replaceChildren(); break;
        case "beat": (m.ops || []).forEach(apply); fit(); redrawInk(); break;
        case "notice": clear(); noticeText = m.text || ""; content.appendChild(el("div", "notice", noticeText)); break;
        case "probe":
          fetch("https://example.com/").then(
            function () { post({ kind: "probe", seq: m.seq, fetch: "allowed" }); },
            function () { post({ kind: "probe", seq: m.seq, fetch: "blocked" }); });
          return;
        default: return;
      }
      post({ kind: "done", seq: m.seq });
    } catch (err) {
      post({ kind: "done", seq: m.seq, error: String(err && err.message || err) });
    }
  });

  document.getElementById("tab").addEventListener("mousedown", function (e) {
    if (e.button === 0 && e.target.id !== "close") post({ kind: "drag" });
  });
  document.getElementById("close").addEventListener("click", function () { post({ kind: "close" }); });
  window.addEventListener("resize", function () { fit(); redrawInk(); });

  post({ kind: "ready" });
})();
