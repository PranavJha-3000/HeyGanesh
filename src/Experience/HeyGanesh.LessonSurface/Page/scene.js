// 3D lesson scene renderer (FEATURE-007 Phase 5 step 4, DEC-030).
//
// The host (CardSurface + SceneMessages) posts validated scene ops with Part Library geometry already
// resolved: every rotation arrives as a quaternion computed in C#, so this page never interprets Euler
// angles. Model text (labels, titles) only ever reaches the screen through textContent or Babylon GUI
// text drawn on a canvas — never as markup. Nothing from the model is evaluated here.
// Frames are drawn only while something changes (render on demand, spike 0b condition 2).
// After a lesson the host may turn picking on (DEC-031): a click on a part posts only two ids, never
// text; the host checks them against what it drew before anything is asked about them.
(function () {
  "use strict";

  var canvas = document.getElementById("view");
  var titleEl = document.getElementById("title");
  var tagsEl = document.getElementById("tags");
  var noticeEl = document.getElementById("notice");
  var hintEl = document.getElementById("hint");
  var cspViolations = 0;
  document.addEventListener("securitypolicyviolation", function () { cspViolations++; });

  function post(msg) { window.chrome.webview.postMessage(msg); }

  // ---------- engine (or none: the lesson then continues in speech) ----------
  var engine = null, scene = null, camera = null, glow = null, ui = null, webgl = 0;
  try {
    engine = new BABYLON.Engine(canvas, true, { stencil: true, preserveDrawingBuffer: true, disableWebGL2Support: false }, true);
    webgl = engine.webGLVersion || 0;
  } catch (e) {
    engine = null;
    webgl = 0;
  }

  // The library's finishes (HeyGanesh.PartLibrary.Finish) — a closed palette, owned here.
  var PALETTE = {
    Metal: ["#b9bec7", 1], DarkMetal: ["#5d636e", 1], Brass: ["#c9a227", 1], Glass: ["#bfe3f2", 0.22],
    Membrane: ["#e9c46a", 0.45], Protein: ["#7aa6c2", 1], IonSodium: ["#8e44ad", 1], IonPotassium: ["#2a9d8f", 1],
    Atp: ["#e76f51", 1], BaseAdenine: ["#d1495b", 1], BaseThymine: ["#edae49", 1], BaseGuanine: ["#00798c", 1],
    BaseCytosine: ["#66a182", 1], Backbone: ["#3d405b", 1]
  };
  var SHAPES = ["Box", "Cylinder", "Sphere", "Torus", "Capsule"];
  var VIEWS = {
    front: [Math.PI / 2, Math.PI / 2], side: [0, Math.PI / 2], top: [Math.PI / 2, 0.0001], iso: [Math.PI / 4, Math.PI / 3]
  };
  var materials = {};

  var instances = {};   // id -> { model, root, parts: { pid: { explode, pose, meshes, anchor, offset:[x,y,z] } }, anchor }
  var labels = {};      // id -> { rect, line, text, target }
  var highlights = [];  // targets, in order
  var explodeAmount = {};
  var playing = {};     // instance id -> { group, name }
  var tweens = [];      // { start, dur, apply(f) }
  var cameraView = "", cameraFocus = "";
  var frames = 0, pending = 0, looping = false, lastBuildMs = 0;
  var tags = [];
  var picking = false;  // on only between the host's picking:on and picking:off (or a click / reset)

  if (engine) {
    scene = new BABYLON.Scene(engine);
    scene.useRightHandedSystem = true;            // the Part Library frame: Y up, right-handed
    scene.clearColor = new BABYLON.Color4(0.965, 0.945, 0.89, 1);
    camera = new BABYLON.ArcRotateCamera("camera", VIEWS.iso[0], VIEWS.iso[1], 7, BABYLON.Vector3.Zero(), scene);
    camera.minZ = 0.05;
    var sky = new BABYLON.HemisphericLight("sky", new BABYLON.Vector3(0.3, 1, 0.4), scene);
    sky.intensity = 0.85;
    sky.groundColor = new BABYLON.Color3(0.55, 0.52, 0.47);   // undersides stay readable, not black
    var sun = new BABYLON.DirectionalLight("sun", new BABYLON.Vector3(-0.4, -1, -0.6), scene);
    sun.intensity = 0.55;
    glow = new BABYLON.HighlightLayer("highlight", scene);
    ui = BABYLON.GUI.AdvancedDynamicTexture.CreateFullscreenUI("labels", true, scene);
    Object.keys(PALETTE).forEach(function (k) {
      var m = new BABYLON.StandardMaterial("finish-" + k, scene);
      m.diffuseColor = BABYLON.Color3.FromHexString(PALETTE[k][0]);
      m.specularColor = new BABYLON.Color3(0.25, 0.25, 0.25);
      m.alpha = PALETTE[k][1];
      // Translucent finishes keep back-face culling: drawing a slab's inner faces through its outer ones reads as a dark block.
      materials[k] = m;
    });
  } else {
    noticeEl.textContent = "3D view isn't available on this PC — the lesson continues in speech.";
    noticeEl.hidden = false;
  }

  // ---------- render on demand ----------
  function anyPlaying() {
    for (var k in playing) if (Object.prototype.hasOwnProperty.call(playing, k) && playing[k].group.isPlaying) return true;
    return false;
  }

  function tick() {
    var now = performance.now();
    tweens = tweens.filter(function (t) {
      var f = Math.min(1, (now - t.start) / t.dur);
      t.apply(f < 1 ? 1 - Math.pow(1 - f, 3) : 1);
      return f < 1;
    });
    scene.render();
    frames++;
    if (pending > 0) pending--;
    if (pending === 0 && tweens.length === 0 && !anyPlaying()) {
      engine.stopRenderLoop(tick);
      looping = false;
    }
  }

  function kick() {
    if (!engine) return;
    pending = 2;
    if (!looping) { looping = true; engine.runRenderLoop(tick); }
  }

  function tween(dur, apply) {
    tweens.push({ start: performance.now(), dur: dur, apply: apply });
    kick();
  }

  // ---------- helpers ----------
  function v3(a) { return new BABYLON.Vector3(a[0], a[1], a[2]); }
  function q4(a) { return new BABYLON.Quaternion(a[0], a[1], a[2], a[3]); }

  function buildPrimitive(p, name) {
    var s = p.size, mesh;
    switch (p.shape) {
      case "Box": mesh = BABYLON.MeshBuilder.CreateBox(name, { width: s[0], height: s[1], depth: s[2] }, scene); break;
      case "Cylinder": mesh = BABYLON.MeshBuilder.CreateCylinder(name, { diameterTop: s[0], height: s[1], diameterBottom: s[2], tessellation: 28 }, scene); break;
      case "Sphere": mesh = BABYLON.MeshBuilder.CreateSphere(name, { diameter: s[0], segments: 16 }, scene); break;
      case "Torus": mesh = BABYLON.MeshBuilder.CreateTorus(name, { diameter: s[0], thickness: s[1], tessellation: 28 }, scene); break;
      case "Capsule": mesh = BABYLON.MeshBuilder.CreateCapsule(name, { radius: s[0] / 2, height: s[1], tessellation: 16 }, scene); break;
      default: throw new Error("unknown shape " + p.shape);
    }
    mesh.material = materials[p.finish] || materials.Metal;
    mesh.isPickable = false;
    return mesh;
  }

  // "instance" or "instance.part" -> the meshes it covers, its anchor node, and a readable key.
  function resolve(target) {
    var dot = target.indexOf(".");
    var inst = instances[dot < 0 ? target : target.slice(0, dot)];
    if (!inst) return null;
    if (dot < 0) {
      var all = [];
      Object.keys(inst.parts).forEach(function (k) { all = all.concat(inst.parts[k].meshes); });
      return { meshes: all, anchor: inst.anchor, instance: inst };
    }
    var part = inst.parts[target.slice(dot + 1)];
    return part ? { meshes: part.meshes, anchor: part.anchor, instance: inst, part: part } : null;
  }

  function bounds(meshes) {
    var min = new BABYLON.Vector3(Infinity, Infinity, Infinity), max = new BABYLON.Vector3(-Infinity, -Infinity, -Infinity);
    meshes.forEach(function (m) {
      m.computeWorldMatrix(true);
      var bb = m.getBoundingInfo().boundingBox;
      min = BABYLON.Vector3.Minimize(min, bb.minimumWorld);
      max = BABYLON.Vector3.Maximize(max, bb.maximumWorld);
    });
    return { center: min.add(max).scale(0.5), size: max.subtract(min).length() };
  }

  function frame(meshes, view, animate) {
    var b = bounds(meshes);
    var ab = VIEWS[view] || VIEWS.iso;
    var radius = Math.max(2.2, b.size * 1.35);
    var from = { a: camera.alpha, b: camera.beta, r: camera.radius, t: camera.target.clone() };
    if (!animate) {
      camera.alpha = ab[0]; camera.beta = ab[1]; camera.radius = radius; camera.setTarget(b.center);
      kick();
      return;
    }
    tween(300, function (f) {
      camera.alpha = from.a + (ab[0] - from.a) * f;
      camera.beta = from.b + (ab[1] - from.b) * f;
      camera.radius = from.r + (radius - from.r) * f;
      camera.setTarget(BABYLON.Vector3.Lerp(from.t, b.center, f));
    });
  }

  function setTags() {
    tagsEl.replaceChildren();
    tags.forEach(function (t) {
      var s = document.createElement("span");
      s.className = "tag";
      s.textContent = t;
      tagsEl.appendChild(s);
    });
  }

  function clearScene() {
    Object.keys(playing).forEach(function (k) { playing[k].group.stop(); playing[k].group.dispose(); });
    playing = {};
    Object.keys(labels).forEach(function (k) { labels[k].rect.dispose(); labels[k].line.dispose(); });
    labels = {};
    if (glow) glow.removeAllMeshes();
    Object.keys(instances).forEach(function (k) { instances[k].root.dispose(false, false); });
    instances = {};
    highlights = [];
    explodeAmount = {};
    tweens = [];
    tags = [];
    setTags();
    cameraView = ""; cameraFocus = "";
    setPicking(false);
  }

  function setPicking(on) {
    picking = on;
    hintEl.hidden = !on;
  }

  function isPart(mesh) { return !!(mesh.metadata && mesh.metadata.part); }
  function isSolidPart(mesh) { return isPart(mesh) && !(mesh.material && mesh.material.alpha < 0.5); }

  // What the student sees is what they click: parts seen THROUGH glass or a membrane win over the
  // see-through surface in front; a see-through part is picked only where nothing solid is behind it.
  function pickPart(x, y) {
    var hit = scene.pick(x, y, isSolidPart);
    return hit && hit.hit ? hit : scene.pick(x, y, isPart);
  }

  // Viewport CSS pixels of a point on each part that really picks that part (a mesh centre hidden
  // behind another part is skipped), so a verification row can click it with a real mouse. The point
  // and the four 2 px around it must all pick the part, so rounding to physical pixels cannot tip a
  // click onto a neighbour.
  function pickPoints() {
    var out = {};
    var rect = canvas.getBoundingClientRect();
    var scale = engine.getHardwareScalingLevel();
    var vp = camera.viewport.toGlobal(engine.getRenderWidth(), engine.getRenderHeight());
    Object.keys(instances).forEach(function (id) {
      Object.keys(instances[id].parts).forEach(function (pid) {
        instances[id].parts[pid].meshes.some(function (m) {
          m.computeWorldMatrix(true);
          var c = m.getBoundingInfo().boundingSphere.centerWorld;
          var s = BABYLON.Vector3.Project(c, BABYLON.Matrix.IdentityReadOnly, scene.getTransformMatrix(), vp);
          var x = s.x * scale, y = s.y * scale;
          var steady = [[0, 0], [2, 0], [-2, 0], [0, 2], [0, -2]].every(function (d) {
            var hit = pickPart(x + d[0], y + d[1]);
            return hit && hit.hit && hit.pickedMesh.metadata.instance === id && hit.pickedMesh.metadata.part === pid;
          });
          if (steady) {
            out[id + "." + pid] = [rect.left + x, rect.top + y];
            return true;
          }
          return false;
        });
      });
    });
    return out;
  }

  // ---------- ops ----------
  function addModel(op) {
    var root = new BABYLON.TransformNode(op.id, scene);
    var inst = { model: op.model, root: root, parts: {}, anchor: null };
    inst.anchor = new BABYLON.TransformNode(op.id + "#anchor", scene);
    inst.anchor.parent = root;
    inst.anchor.position = v3(op.anchor);
    op.parts.forEach(function (p) {
      // explode node (offset) -> pose node (rest pose, or the animation's values) -> meshes
      var explode = new BABYLON.TransformNode(op.id + "." + p.id + "#explode", scene);
      explode.parent = root;
      var pose = new BABYLON.TransformNode(op.id + "." + p.id, scene);
      pose.parent = explode;
      pose.position = v3(p.position);
      pose.rotationQuaternion = q4(p.rotation);
      var meshes = p.primitives.map(function (prim, i) {
        var mesh = buildPrimitive(prim, op.id + "." + p.id + "#" + i);
        // Lesson meshes are pickable and say which part they belong to; warm-up and orientation meshes are not.
        mesh.isPickable = true;
        mesh.metadata = { instance: op.id, part: p.id };
        mesh.parent = pose;
        mesh.position = v3(prim.position);
        mesh.rotationQuaternion = q4(prim.rotation);
        return mesh;
      });
      var anchor = new BABYLON.TransformNode(op.id + "." + p.id + "#anchor", scene);
      anchor.parent = pose;
      anchor.position = v3(p.anchor);
      inst.parts[p.id] = { explode: explode, pose: pose, meshes: meshes, anchor: anchor, offset: v3(p.explode), now: [0, 0, 0] };
    });
    instances[op.id] = inst;
    if (op.schematic && tags.indexOf("schematic") < 0) tags.push("schematic");
    if (!op.reviewed && tags.indexOf("not yet reviewed") < 0) tags.push("not yet reviewed");
    setTags();
    if (!titleEl.textContent) titleEl.textContent = op.title || "";
    frame(resolve(op.id).meshes, "iso", false);
    cameraView = "iso"; cameraFocus = op.id;
  }

  function addLabel(op) {
    var r = resolve(op.target);
    if (!r) throw new Error("label target not in scene");
    var rect = new BABYLON.GUI.Rectangle(op.id);
    rect.adaptWidthToChildren = true;
    rect.height = "30px";
    rect.cornerRadius = 3;
    rect.thickness = 1;
    rect.color = "#7a6a3a";
    rect.background = "#fff6a8";
    var text = new BABYLON.GUI.TextBlock(op.id + "#text", op.text);  // canvas text, never markup
    text.fontSize = 15;
    text.color = "#2b2b2b";
    text.resizeToFit = true;
    text.paddingLeft = "8px"; text.paddingRight = "8px";
    rect.addControl(text);
    ui.addControl(rect);
    rect.linkWithMesh(r.anchor);
    rect.linkOffsetY = -70;
    var line = new BABYLON.GUI.Line(op.id + "#leader");
    line.lineWidth = 2;
    line.color = "#7a6a3a";
    line.y2 = 15;
    ui.addControl(line);
    line.linkWithMesh(r.anchor);
    line.connectedControl = rect;
    labels[op.id] = { rect: rect, line: line, text: op.text, target: op.target };
  }

  function highlight(op) {
    var label = labels[op.target];
    if (label) {
      label.rect.thickness = 3;
      label.rect.color = "#d0021b";
    } else {
      var r = resolve(op.target);
      if (!r) throw new Error("highlight target not in scene");
      r.meshes.forEach(function (m) { glow.addMesh(m, BABYLON.Color3.FromHexString("#ff7a00")); });
    }
    highlights.push(op.target);
  }

  function explode(op) {
    var inst = instances[op.target];
    if (!inst) throw new Error("explode target not in scene");
    explodeAmount[op.target] = op.amount;
    Object.keys(inst.parts).forEach(function (k) {
      var part = inst.parts[k];
      var from = part.explode.position.clone();
      var to = part.offset.scale(op.amount);
      part.now = [to.x, to.y, to.z];
      tween(400, function (f) { part.explode.position = BABYLON.Vector3.Lerp(from, to, f); });
    });
  }

  function animate(op) {
    var inst = instances[op.target];
    if (!inst) throw new Error("animate target not in scene");
    if (playing[op.target]) { playing[op.target].group.stop(); playing[op.target].group.dispose(); }
    var fps = 60, total = op.duration * fps;
    var group = new BABYLON.AnimationGroup(op.target + ":" + op.animation, scene);
    var mode = op.loop ? BABYLON.Animation.ANIMATIONLOOPMODE_CYCLE : BABYLON.Animation.ANIMATIONLOOPMODE_CONSTANT;
    op.tracks.forEach(function (t, i) {
      var part = inst.parts[t.part];
      if (!part) throw new Error("track part not in model");
      var position = t.property === "position";
      var anim = new BABYLON.Animation(op.target + ":" + i, position ? "position" : "rotationQuaternion", fps,
        position ? BABYLON.Animation.ANIMATIONTYPE_VECTOR3 : BABYLON.Animation.ANIMATIONTYPE_QUATERNION, mode);
      anim.setKeys(t.keys.map(function (k) { return { frame: k.t * total, value: position ? v3(k.v) : q4(k.v) }; }));
      group.addTargetedAnimation(anim, part.pose);
    });
    group.onAnimationGroupEndObservable.add(function () { kick(); });
    playing[op.target] = { group: group, name: op.animation };
    group.play(op.loop);
    kick();
  }

  function cameraOp(op) {
    var r = resolve(op.focus);
    if (!r) throw new Error("camera focus not in scene");
    frame(r.meshes, op.view, true);
    cameraView = op.view; cameraFocus = op.focus;
  }

  function apply(op) {
    switch (op.op) {
      case "model": addModel(op); break;
      case "label": addLabel(op); break;
      case "highlight": highlight(op); break;
      case "explode": explode(op); break;
      case "animate": animate(op); break;
      case "camera": cameraOp(op); break;
      case "clear": clearScene(); break;
      default: throw new Error("unknown scene op");
    }
  }

  // One hidden mesh per shape x finish, drawn once and dropped: compiles every shader a library model
  // can need, so the lesson's real model builds warm (spike 0b condition 1).
  function warmup() {
    var made = [];
    SHAPES.forEach(function (shape) {
      Object.keys(PALETTE).forEach(function (finish) {
        var m = buildPrimitive({ shape: shape, size: [0.2, 0.2, 0.2], finish: finish }, "warm-" + shape + "-" + finish);
        m.position = new BABYLON.Vector3(0, -50, 0);
        made.push(m);
        glow.addMesh(m, BABYLON.Color3.White());
      });
    });
    return scene.whenReadyAsync().then(function () {
      scene.render();
      made.forEach(function (m) { glow.removeMesh(m); m.dispose(); });
      scene.render();
    });
  }

  // Orientation self-test (DEC-030): red at +X, green at +Y, blue on a parent turned by the host's
  // quaternion (90 deg about Z), so the blue marker must appear at -X. Unlit, so colours are exact.
  function orient(turn) {
    clearScene();
    var made = [];
    function marker(color, pos, parent) {
      var m = BABYLON.MeshBuilder.CreateBox("orient-" + color, { size: 0.6 }, scene);
      var mat = new BABYLON.StandardMaterial("orient-" + color + "-mat", scene);
      mat.disableLighting = true;
      mat.emissiveColor = BABYLON.Color3.FromHexString(color);
      m.material = mat;
      m.position = pos;
      if (parent) m.parent = parent;
      made.push(m, mat);
      return m;
    }
    marker("#ff0000", new BABYLON.Vector3(1.6, 0, 0));
    marker("#00ff00", new BABYLON.Vector3(0, 1.6, 0));
    var parent = new BABYLON.TransformNode("orient-turn", scene);
    parent.rotationQuaternion = q4(turn);
    made.push(parent);
    marker("#0000ff", new BABYLON.Vector3(0, 1.6, 0), parent);
    camera.alpha = VIEWS.front[0]; camera.beta = VIEWS.front[1]; camera.radius = 7; camera.setTarget(BABYLON.Vector3.Zero());
    return scene.whenReadyAsync().then(function () { scene.render(); frames++; return made; });
  }
  var orientMade = [];

  // ---------- read-only description for the host's verification rows ----------
  window.__describe = function () {
    var inst = [], offsets = {};
    Object.keys(instances).forEach(function (id) {
      var parts = instances[id].parts, meshes = 0;
      Object.keys(parts).forEach(function (pid) { meshes += parts[pid].meshes.length; offsets[id + "." + pid] = parts[pid].now; });
      inst.push({ id: id, model: instances[id].model, parts: Object.keys(parts).length, meshes: meshes });
    });
    var playingNow = [];
    Object.keys(playing).forEach(function (k) { if (playing[k].group.isPlaying) playingNow.push(k + ":" + playing[k].name); });
    return {
      webgl: webgl,
      title: titleEl.textContent,
      tags: tags.slice(),
      notice: noticeEl.hidden ? "" : noticeEl.textContent,
      instances: inst,
      labels: Object.keys(labels).map(function (k) { return { id: k, target: labels[k].target, text: labels[k].text }; }),
      highlights: highlights.slice(),
      explode: explodeAmount,
      offsets: offsets,
      playing: playingNow,
      cameraView: cameraView,
      cameraFocus: cameraFocus,
      frames: frames,
      csp: cspViolations,
      lastBuildMs: lastBuildMs,
      picking: picking,
      pickPoints: engine ? pickPoints() : {},
      closeAt: (function () { var r = document.getElementById("close").getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; })(),
      script: document.querySelectorAll("script").length
    };
  };

  // ---------- messages from the host ----------
  function done(m, extra) {
    var reply = { kind: "done", seq: m.seq };
    if (extra) for (var k in extra) if (Object.prototype.hasOwnProperty.call(extra, k)) reply[k] = extra[k];
    post(reply);
  }

  window.chrome.webview.addEventListener("message", function (e) {
    var m = e.data || {};
    try {
      if (m.kind === "probe") {
        fetch("https://example.com/").then(
          function () { post({ kind: "probe", seq: m.seq, fetch: "allowed" }); },
          function () { post({ kind: "probe", seq: m.seq, fetch: "blocked" }); });
        return;
      }
      if (!engine) {
        // No WebGL: acknowledge everything so the beat completes and the speech plays.
        if (m.kind === "reset") titleEl.textContent = m.title || "";
        done(m, { skipped: true });
        return;
      }
      switch (m.kind) {
        case "reset":
          orientMade.forEach(function (x) { x.dispose(); });
          orientMade = [];
          clearScene();
          titleEl.textContent = m.title || "";
          kick();
          done(m);
          return;
        case "warmup":
          warmup().then(function () { done(m); }, function (err) { done(m, { error: String(err && err.message || err) }); });
          return;
        case "picking":
          setPicking(!!m.on);
          done(m);
          return;
        case "orient":
          orient(m.turn).then(function (made) { orientMade = made; done(m); }, function (err) { done(m, { error: String(err && err.message || err) }); });
          return;
        case "beat":
          // A model beat is timed end to end — meshes built, shaders ready, first frame drawn — the
          // same span spike 0b measured against FEATURE-007's 300 ms budget.
          var beatStart = performance.now();
          var hasModel = (m.ops || []).some(function (op) { return op.op === "model"; });
          (m.ops || []).forEach(apply);
          kick();
          scene.whenReadyAsync().then(function () {
            scene.render();
            frames++;
            if (hasModel) lastBuildMs = performance.now() - beatStart;
            done(m, { buildMs: lastBuildMs });
          },
            function (err) { done(m, { error: String(err && err.message || err) }); });
          return;
        default:
          return;
      }
    } catch (err) {
      done(m, { error: String(err && err.message || err) });
    }
  });

  document.getElementById("tab").addEventListener("mousedown", function (e) {
    if (e.button === 0 && e.target.id !== "close") post({ kind: "drag" });
  });
  document.getElementById("close").addEventListener("click", function () { post({ kind: "close" }); });
  // One click, one pick: the part glows, picking turns itself off, and only the two ids go to the host.
  canvas.addEventListener("click", function (e) {
    if (!picking || !scene) return;
    var hit = pickPart(e.offsetX, e.offsetY);
    if (!hit || !hit.hit) return;
    var meta = hit.pickedMesh.metadata;
    var r = resolve(meta.instance + "." + meta.part);
    if (r) r.meshes.forEach(function (m) { glow.addMesh(m, BABYLON.Color3.FromHexString("#2a9d8f")); });
    setPicking(false);
    kick();
    post({ kind: "part-clicked", instance: meta.instance, part: meta.part });
  });
  window.addEventListener("resize", function () { if (engine) { engine.resize(); kick(); } });

  post({ kind: "ready", webgl: webgl });
})();
