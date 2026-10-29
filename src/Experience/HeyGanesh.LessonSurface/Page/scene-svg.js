// The 3D lesson drawn as an isometric SVG when WebGL is missing (DEC-034, after spike 0d).
//
// scene.js keeps the scene state (placed models, labels, highlights, explode amounts, focus) and calls
// SceneSvg.draw(state) after every beat. Geometry is the same Part Library message the Babylon path gets
// (SceneMessages.Model); Anatomy's MIT kit (vendored, SHA-pinned in vendor/VENDOR.md) turns each
// primitive into shaded SVG paths. Kit output carries only numbers and fixed class names; lesson text
// (labels) is added afterwards as DOM text nodes, never inside an SVG string.
import * as K from "./vendor/anatomy-1482e846/iso-kit.mjs";
import * as G from "./vendor/anatomy-1482e846/lathe.mjs";

const NS = "http://www.w3.org/2000/svg";
const WIDTH = 640;
const HEIGHT = 420;

// A cylinder longer than this many diameters is drawn as pieces about one diameter long, each sorted
// on its own: a part that passes through another (a valve stem through the cylinder head) then shows
// where it really sticks out instead of being painted wholly behind or in front (spike 0d's defect).
// Inner pieces are open bands, so the cuts draw no rings; only the true ends get caps.
const PIECE_RATIO = 2.5;

// Quaternions arrive as [x, y, z, w] (SceneMessages.ToQuaternion).
const qmul = ([ax, ay, az, aw], [bx, by, bz, bw]) => [
  aw * bx + ax * bw + ay * bz - az * by,
  aw * by - ax * bz + ay * bw + az * bx,
  aw * bz + ax * by - ay * bx + az * bw,
  aw * bw - ax * bx - ay * by - az * bz,
];
const qrot = (q, v) => {
  const p = qmul(qmul(q, [v[0], v[1], v[2], 0]), [-q[0], -q[1], -q[2], q[3]]);
  return [p[0], p[1], p[2]];
};
const add = (a, b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
const mul = (a, k) => [a[0] * k, a[1] * k, a[2] * k];

// Part Library space is right-handed with Y up. The kit's projection is a reflection of a right-handed
// screen frame (determinant -1), so the map into kit space must reflect too or every model is drawn
// mirrored: (x, y, z) -> (x, z, y), kit z up. Spike 0d; LSU-16 checks it from real pixels.
const toKit = ([x, y, z]) => [x, z, y];

/** Every primitive of every placed model, in kit space, with what the beat state says about its part. */
function primitivesOf(state) {
  const out = [];
  for (const [id, op] of Object.entries(state.instances)) {
    const amount = state.explode[id] || 0;
    for (const part of op.parts) {
      const at = add(part.position, mul(part.explode, amount));
      const key = `${id}.${part.id}`;
      const lit = state.highlights.includes(id) || state.highlights.includes(key);
      for (const prim of part.primitives) {
        const q = qmul(part.rotation, prim.rotation);
        out.push({
          key,
          lit,
          shape: prim.shape,
          size: prim.size,
          finish: prim.finish,
          c: toKit(add(qrot(part.rotation, prim.position), at)),
          ex: toKit(qrot(q, [1, 0, 0])),
          ey: toKit(qrot(q, [0, 1, 0])),
          ez: toKit(qrot(q, [0, 0, 1])),
        });
      }
    }
  }
  return out;
}

/** The eight corners of a primitive's bounding box, for framing the camera. */
function cornersOf(p) {
  const [a, b, c] = p.size;
  const half = {
    Box: [a / 2, b / 2, c / 2],
    Cylinder: [Math.max(a, c) / 2, b / 2, Math.max(a, c) / 2],
    Sphere: [a / 2, a / 2, a / 2],
    Torus: [(a + b) / 2, b / 2, (a + b) / 2],
    Capsule: [a / 2, b / 2, a / 2],
  }[p.shape] || [0.1, 0.1, 0.1];
  const pts = [];
  for (const sx of [-1, 1]) for (const sy of [-1, 1]) for (const sz of [-1, 1]) {
    pts.push(add(add(add(p.c, mul(p.ex, sx * half[0])), mul(p.ey, sy * half[1])), mul(p.ez, sz * half[2])));
  }
  return pts;
}

function capsuleProfile(d, h, n = 8) {
  const r = d / 2;
  const half = h / 2;
  const prof = [];
  for (let i = 1; i <= n; i++) {
    const t = (i / n) * (Math.PI / 2);
    prof.push([-half + r - r * Math.cos(t), r * Math.sin(t), 1]);
  }
  for (let i = n - 1; i >= 1; i--) {
    const t = (i / n) * (Math.PI / 2);
    prof.push([half - r + r * Math.cos(t), r * Math.sin(t), 1]);
  }
  return prof;
}

/**
 * Lines for one piece of a cut box, so the cuts don't show: the silhouette without the edges that lie on
 * an inner cut, the long edges between two visible side faces, and the edges of a true end cap when it
 * faces the viewer. Replaces the piece's own outline and creases from prismOf.
 */
function pieceLines(F, rect, s0, s1, lowIsEnd, highIsEnd, P) {
  const V = G.viewOf(P);
  const at = (s, [x, y]) => add(add(add(F.o, mul(F.a, s)), mul(F.u, x)), mul(F.v, y));
  const ring = (s) => rect.map((q) => at(s, q));
  const low = ring(s0), high = ring(s1);
  const flat = [...low, ...high].map((q) => K.iso(q, P));
  const tag = (pt) => flat.findIndex((f) => Math.abs(f[0] - pt[0]) < 1e-6 && Math.abs(f[1] - pt[1]) < 1e-6);
  const hull = G.hullOf(flat);
  const edges = [];
  for (let i = 0; i < hull.length; i++) {
    const a = tag(hull[i]), b = tag(hull[(i + 1) % hull.length]);
    const onLowCut = !lowIsEnd && a < 4 && b < 4;
    const onHighCut = !highIsEnd && a >= 4 && b >= 4;
    if (!onLowCut && !onHighCut) edges.push(K.pathOf([hull[i], hull[(i + 1) % hull.length]]));
  }
  const n = rect.length;
  const seen = rect.map(([x0, y0], j) => {
    const [x1, y1] = rect[(j + 1) % n];
    return G.dot3(add(mul(F.u, y1 - y0), mul(F.v, -(x1 - x0))), V) > 1e-6;
  });
  const creases = [];
  for (let j = 0; j < n; j++) if (seen[j] && seen[(j - 1 + n) % n]) creases.push(K.pathOf([K.iso(low[j], P), K.iso(high[j], P)]));
  for (const [isEnd, points, normal] of [[lowIsEnd, low, mul(F.a, -1)], [highIsEnd, high, F.a]]) {
    if (isEnd && G.dot3(normal, V) > 1e-6) creases.push(K.pathOf(points.map((q) => K.iso(q, P)), true));
  }
  return { outline: edges.join(""), crease: creases.join("") };
}

/** One primitive as depth-keyed SVG strings (a torus or a long cylinder becomes several). */
function itemsOf(p, P) {
  const cls = `f-${p.finish}${p.lit ? " hl" : ""}`;
  const [a, b, c] = p.size;
  const item = (paths, at) => ({ key: p.key, lit: p.lit, at, prim: p, depth: K.depthOf(at, P), svg: K.solidSvg(paths, { className: cls }) });
  switch (p.shape) {
    case "Box": {
      // Extruded along its longest side; a long bar (the connecting rod) is cut into pieces like a long
      // cylinder, so the end buried in the piston is hidden and the rest is drawn where it really is.
      const sides = [[a, p.ex], [b, p.ey], [c, p.ez]].sort((x, y) => y[0] - x[0]);
      const [[len, axis], [w, u], [d]] = sides;
      const F = G.frameOf(p.c, G.unit3(axis), G.unit3(u), G.unit3(G.cross3(axis, u)));
      const rect = [[-w / 2, -d / 2], [w / 2, -d / 2], [w / 2, d / 2], [-w / 2, d / 2]];
      const pieces = len > PIECE_RATIO * w ? Math.ceil(len / w) : 1;
      if (pieces === 1) return [item(G.prismOf(F, rect, -len / 2, len / 2, P), p.c)];
      const out = [];
      for (let i = 0; i < pieces; i++) {
        const s0 = -len / 2 + (len * i) / pieces;
        const s1 = -len / 2 + (len * (i + 1)) / pieces;
        const paths = G.prismOf(F, rect, s0, s1, P);
        Object.assign(paths, pieceLines(F, rect, s0, s1, i === 0, i === pieces - 1, P));
        out.push(item(paths, add(p.c, mul(axis, (s0 + s1) / 2))));
      }
      return out;
    }
    case "Cylinder": {
      const rTop = a / 2;
      const rBottom = c / 2;
      const pieces = b > PIECE_RATIO * Math.max(a, c) ? Math.ceil(b / Math.max(a, c)) : 1;
      const F = G.frameAlong(p.c, p.ey);
      const r = (s) => rBottom + (rTop - rBottom) * ((s + b / 2) / b);
      if (pieces === 1) return [item(G.solidOf([[-b / 2, rBottom], [b / 2, rTop]], F, P), p.c)];
      const steps = G.stepsFor(Math.max(rTop, rBottom), 32, 160, G.scaleOf(P));
      const out = [];
      for (let i = 0; i < pieces; i++) {
        const s0 = -b / 2 + (b * i) / pieces;
        const s1 = -b / 2 + (b * (i + 1)) / pieces;
        const side = [[s0, r(s0)], [s1, r(s1)]];
        const meridian = i === 0 ? [[s0, 0], ...side] : i === pieces - 1 ? [...side, [s1, 0]] : side;
        const rims = i === 0 ? "first" : i === pieces - 1 ? "last" : "none";
        out.push(item(G.lathe(meridian, F, P, { steps, rims }), add(p.c, mul(p.ey, (s0 + s1) / 2)), p));
      }
      return out;
    }
    case "Sphere":
      return [item(G.sphereOf(p.c, a / 2, P), p.c)];
    case "Capsule":
      return [item(G.solidOf(capsuleProfile(a, b), G.frameAlong(p.c, p.ey), P), p.c)];
    case "Torus": {
      // No turned-circle solid in the kit: a ring of short tubes, each sorted on its own (spike 0d).
      const R = a / 2;
      const n = 20;
      const out = [];
      for (let i = 0; i < n; i++) {
        const th = (i / n) * Math.PI * 2;
        const centre = add(p.c, add(mul(p.ex, R * Math.cos(th)), mul(p.ez, R * Math.sin(th))));
        const tangent = add(mul(p.ex, -Math.sin(th)), mul(p.ez, Math.cos(th)));
        const len = ((2 * Math.PI * R) / n) * 0.55;
        out.push(item(G.disc(-len, len, b / 2, G.frameAlong(centre, tangent), P), centre));
      }
      return out;
    }
    default:
      throw new Error("unknown shape");
  }
}

const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

/** True when kit-space point `q` lies inside primitive `c` (shapes treated as their solid volumes). */
function inside(q, c) {
  const d = [q[0] - c.c[0], q[1] - c.c[1], q[2] - c.c[2]];
  const x = dot(d, c.ex), y = dot(d, c.ey), z = dot(d, c.ez);
  const [a, b, w] = c.size;
  switch (c.shape) {
    case "Box": return Math.abs(x) < a / 2 && Math.abs(y) < b / 2 && Math.abs(z) < w / 2;
    case "Cylinder": return Math.abs(y) < b / 2 && Math.hypot(x, z) < Math.max(a, w) / 2;
    case "Capsule": return Math.abs(y) < b / 2 && Math.hypot(x, z) < a / 2;
    case "Sphere": return Math.hypot(x, y, z) < a / 2;
    default: return false;
  }
}

/**
 * Painter's order with one correction: a piece whose centre lies inside another solid is painted just
 * before that solid, so what is buried (the piston pin, the stem inside the head) stays hidden and only
 * what really sticks out is drawn over its neighbour. Containers nest (pin in piston in glass cylinder),
 * so each primitive's own order is settled first, outermost inward, and a buried piece goes just before
 * its container's settled order. Far first; depthOf grows toward the viewer.
 */
function paintOrder(items, prims, P) {
  const EPS = 1e-4;
  const holders = (q, self) => prims.filter((c) => c !== self && c.shape !== "Torus" && inside(q, c));
  const order = new Map(prims.map((c) => [c, K.depthOf(c.c, P)]));
  for (let pass = 0; pass < 4; pass++) {
    for (const c of prims) {
      for (const h of holders(c.c, c)) order.set(c, Math.min(order.get(c), order.get(h) - EPS));
    }
  }
  for (const it of items) {
    let key = it.depth;
    for (const h of holders(it.at, it.prim)) key = Math.min(key, order.get(h) - EPS);
    it.order = key;
  }
  // Ties (two things moved just before the same container) fall back to their own depth.
  return items.sort((x, y) => x.order - y.order || x.depth - y.depth);
}

/** Where a label points: the part's anchor (or the instance's), in kit space. */
function anchorOf(state, target) {
  const dot = target.indexOf(".");
  const op = state.instances[dot < 0 ? target : target.slice(0, dot)];
  if (!op) return null;
  if (dot < 0) return toKit(op.anchor);
  const part = op.parts.find((x) => x.id === target.slice(dot + 1));
  if (!part) return null;
  const amount = state.explode[target.slice(0, dot)] || 0;
  return toKit(add(add(qrot(part.rotation, part.anchor), part.position), mul(part.explode, amount)));
}

function el(name, attrs) {
  const node = document.createElementNS(NS, name);
  for (const [k, v] of Object.entries(attrs)) node.setAttribute(k, String(v));
  return node;
}

/** Sticky-note labels on leader lines; text only through textContent. */
function addLabels(svg, state, P) {
  for (const [id, label] of Object.entries(state.labels)) {
    const at = anchorOf(state, label.target);
    if (!at) continue;
    const [x, y] = K.iso(at, P);
    const g = el("g", { class: `lbl${state.highlights.includes(id) ? " hl" : ""}` });
    const line = el("line", { x1: x, y1: y, x2: x, y2: y - 34 });
    const rect = el("rect", { y: y - 56, height: 22, rx: 3 });
    const text = el("text", { x, y: y - 40, "text-anchor": "middle" });
    text.textContent = label.text;
    g.append(line, rect, text);
    svg.appendChild(g);
    const w = text.getComputedTextLength() + 16;
    rect.setAttribute("x", String(x - w / 2));
    rect.setAttribute("width", String(w));
  }
}

let last = { solids: 0, ms: 0, order: [], lit: 0, hash: 0 };
let lastHost = null;

// A cheap fingerprint of the drawn markup, so a verification row can tell that an op changed the picture.
const hashOf = (text) => {
  let h = 5381;
  for (let i = 0; i < text.length; i++) h = ((h * 33) ^ text.charCodeAt(i)) >>> 0;
  return h;
};

/** Draws the whole scene into `host`. Returns { solids, ms }. */
function draw(state, host) {
  const t0 = performance.now();
  lastHost = host;
  const prims = primitivesOf(state);
  host.replaceChildren();
  if (prims.length === 0) {
    last = { solids: 0, ms: performance.now() - t0, order: [], lit: 0, hash: 0 };
    return last;
  }
  const focus = state.focus && state.focus.indexOf(".") > 0 ? prims.filter((p) => p.key === state.focus) : prims;
  const P = K.fitProjection((focus.length ? focus : prims).flatMap(cornersOf), WIDTH, HEIGHT, { pad: 36, headroom: 30 });
  const items = paintOrder(prims.flatMap((p) => itemsOf(p, P)), prims, P);
  const markup = K.figureSvg({ width: WIDTH, height: HEIGHT, label: "lesson drawing", body: items.map((i) => i.svg) });
  const svg = document.importNode(new DOMParser().parseFromString(markup, "image/svg+xml").documentElement, true);
  host.appendChild(svg);
  addLabels(svg, state, P);
  // Paint order by part, consecutive repeats folded: what LSU-16 reads to pin the depth-order fix.
  const order = items.map((i) => i.key).filter((k, i, all) => i === 0 || all[i - 1] !== k);
  last = { solids: items.length, ms: performance.now() - t0, order, lit: items.filter((i) => i.lit).length, hash: hashOf(markup) };
  return last;
}

/**
 * Orientation self-test on the drawing (DEC-030's probe, DEC-034): flat red at +X, green at +Y, blue at +Y
 * on a parent turned by the host's quaternion, seen from the front (azimuth 90, elevation 0: our +Z
 * toward the viewer, the same view Babylon's probe uses). CardSurface finds them by colour in the pixels.
 */
function orient(turn, host) {
  host.replaceChildren();
  const P = K.fitProjection([[-2.4, -2.4, -2.4], [2.4, 2.4, 2.4]].map(toKit), WIDTH, HEIGHT, { pad: 20, azimuth: 90, elevation: 0 });
  const box = (cls, at) => {
    const F = G.frameOf(toKit(at), [0, 0, 1], [1, 0, 0], [0, 1, 0]);
    return { depth: K.depthOf(toKit(at), P), svg: K.solidSvg(G.prismOf(F, [[-0.3, -0.3], [0.3, -0.3], [0.3, 0.3], [-0.3, 0.3]], -0.3, 0.3, P), { className: cls, crease: "none" }) };
  };
  const items = [box("o-red", [1.6, 0, 0]), box("o-green", [0, 1.6, 0]), box("o-blue", qrot(turn, [0, 1.6, 0]))];
  items.sort((x, y) => x.depth - y.depth);
  const markup = K.figureSvg({ width: WIDTH, height: HEIGHT, label: "orientation probe", body: items.map((i) => i.svg) });
  host.appendChild(document.importNode(new DOMParser().parseFromString(markup, "image/svg+xml").documentElement, true));
}

window.SceneSvg = {
  kit: true,
  draw,
  orient,
  // Read-only, for verification rows: label texts as the DOM holds them, and how many elements inside the
  // labels are anything but the three this file creates (must stay 0: no lesson text ever becomes markup).
  describe: () => ({
    solids: last.solids,
    buildMs: Math.round(last.ms * 10) / 10,
    order: last.order,
    lit: last.lit,
    hash: last.hash,
    labelTexts: lastHost ? Array.from(lastHost.querySelectorAll(".lbl text"), (t) => t.textContent) : [],
    labelForeign: lastHost ? lastHost.querySelectorAll(".lbl *:not(line):not(rect):not(text)").length : 0,
  }),
};
window.dispatchEvent(new Event("scenesvg-ready"));
