# 3D Scene Renderer v1 Implementation Plan

> 2026-10-29 · claude-opus-5-5 · Status: **BUILT 2026-10-29** (owner: "Yes, build it"): LSU-7..LSU-12 green, LSU-13/14 run. As-built notes: the model-beat timing covers the whole beat (meshes + shaders ready + first frame: 114-120 ms) and is read after the lesson, because LessonPlayer speaks while a beat draws; `__describe` reports explode *target* offsets (the 400 ms tween may still be running); the membrane finish was lightened and back faces kept culled after the snapshot review; `explode` does not re-frame the camera; NOTICE.md (Apache-2.0 §4(d)) is not vendored yet; memory was not re-measured (spike 0b's numbers stand). FEATURE-007 Phase 5, step 4 of 6.

**Goal:** Draw a validated scene3d lesson on screen. `CardSurface` (the real `ILessonCanvas`) gains a capture-excluded, no-activate **scene window** that builds Part Library models with vendored Babylon.js 9.30.0 and plays the six 3D ops beat by beat in sync with speech:
- `model`, place a library model;
- `label`, a sticky note on a leader line;
- `highlight`, mark a part;
- `explode`, pull the parts apart;
- `animate`, play the model's own keyframes;
- `camera`, frame a part from front, side, top or iso.

It meets spike 0b's three conditions (warm shaders, render on demand, a recorded memory decision), and degrades to speech when WebGL is missing. It's only exercised by its verification host. The App, `LessonTurn` and the planner stay 2D-only until step 6 passes the catalogue.

**Spec:** [FEATURE-007](../features/FEATURE-007.md) §1.3 (Babylon: `HighlightLayer`, GUI `linkWithMesh`, animation groups), §1.5 (vendored, pinned, offline, integrity-checked), §2.2 (the page receives only validated data; no `eval`, text never as markup), §2.5 (≤ 300 ms 3D scene build), Phase 0 spike 0b results and conditions; DEC-020 (opaque WebView2 windows, capture exclusion, Babylon deferred to Phase 5); DEC-025 (CardSurface); DEC-028 (scene3d contract); DEC-029 (part library, `Geometry.Rotate` convention, "step 4 confirms it with a snapshot").

## The problem, restated

A lesson can now describe "place the engine, label the connecting rod, run the stroke cycle", and the part library has the engine, but nothing draws it. The renderer has to:
- turn library geometry into a scene without reinterpreting it: the rotation convention must survive the hop into the browser exactly;
- stay as locked down as the 2D card: no network, no code from the model, text never as markup;
- not take the student's keyboard focus or appear in screen captures;
- meet the latency and idle-CPU conditions the spike set;
- never lose the lesson's speech, even on a PC that can't run WebGL.

## Execution path (FLOW.md "Lesson playback path")

`LessonPlayer.PlayAsync` → `ILessonCanvas.ResetAsync(surfaces)` → per beat `ApplyBeatAsync(beat)` + speak. Inside `CardSurface` only:

- **ResetAsync** partitions surfaces by kind:
  - Card surfaces get cards, exactly as today.
  - A `Scene3d` surface gets the **scene window**. It's created on first use, together with a shader warm-up, then kept for the surface's lifetime and hidden when a lesson has no scene. It's laid out to the left of the card column.
- **ApplyBeatAsync** routes each op:
  - Card ops go to their card, as today.
  - `model`/`label`/`explode`/`animate`/`camera`, `clear` on the scene, and a `highlight` whose target is a scene element or part ref go to the scene window. These are built by a new `SceneMessages` from `ModelLibrary` data.
  - The beat completes when every window has acknowledged it. For the scene, that's after the change is applied and one frame is drawn, not after an animation ends.

No other assembly's path changes. `ValidationContext.Scene3d` is still null in VisualPlanner, LessonTurn and the App, so a live plan never carries a scene yet.

## Design

### Vendored files (needs owner approval; see "Asks")

`src/Experience/HeyGanesh.LessonSurface/Page/vendor/babylonjs-9.30.0/`:

| File | Size | Source |
|---|---|---|
| `babylon.js` | 8,619,713 B | npm `babylonjs@9.30.0` (via cdn.jsdelivr.net) |
| `babylon.gui.min.js` | 702,021 B | npm `babylonjs-gui@9.30.0` (via cdn.jsdelivr.net) |
| `LICENSE` | — | Apache-2.0 text from the same package (Apache-2.0 requires shipping it) |

- **Hashes:** both scripts' SHA-256 values match jsdelivr's published hashes, checked in spike 0b:
  - `ff3311…bbff` for `babylon.js`
  - `4f15fb…a45c` for `babylon.gui.min.js`
- **Pins file:** `Page/vendor/VENDOR.md` lists each file's version, source URL, SHA-256 and licence. The source audit re-hashes the files against it.
- **Line endings:** `.gitattributes` gets `src/Experience/HeyGanesh.LessonSurface/Page/vendor/** -text`. Otherwise `text=auto` would rewrite line endings on checkout and break the hashes.

The repo grows by about 9.3 MB. A tree-shaken ES-module build would be smaller but needs npm and a bundler at build time, which DEC-020 and FEATURE-007 §1.5 rule out.

### The scene page (`Page/scene.html`, `scene.css`, `scene.js`)

- **Lockdown:** the same CSP as `lesson.html`: `default-src 'self'; script-src 'self'; connect-src 'none'; …`, no `unsafe-eval`, no `unsafe-inline`.
  - A `securitypolicyviolation` counter is reported to the host.
  - If Babylon turns out to need an inline style, I'll stop and ask rather than weaken the CSP silently.
- **Same window rules:** same virtual host, navigation and new-window blocking, and no dev tools, context menu or accelerator keys (`CardWindow` already applies these; it gains a page parameter).
- **Coordinate frame:** `scene.useRightHandedSystem = true`, Y up, matching the library frame.
- **Rotations arrive as quaternions computed in C#,** never as Euler angles, so the page doesn't interpret `Geometry.Rotate`'s Z-X-Y convention. That's the one place it could drift.
- **Look:** finishes map to a closed palette in `scene.js`; `Glass` is translucent.
- **Header** (DOM, `textContent`): the model title, plus visible tags **"schematic"** (`Schematic = true`) and **"not yet reviewed"** (`Reviewed = false`), per FEATURE-007 §3.2 and DEC-029.
- **Ops:**

  | Op | Rendering |
  |---|---|
  | `model` | one `TransformNode` per part, meshes from `MeshBuilder` per primitive |
  | `label` | GUI `Rectangle`+`TextBlock` with `linkWithMesh` and a GUI `Line` leader. Text is drawn on a canvas, never HTML |
  | `highlight` | `HighlightLayer` on the part's meshes, or the label's border for a label id |
  | `explode` | 400 ms tween of each part to `rest + amount × ExplodeOffset` |
  | `animate` | one Babylon `AnimationGroup` from the sent keys: position linear, rotation as quaternion slerp; plays once or loops per the library |
  | `camera` | `ArcRotateCamera` framing the focus's bounding box from the named view (a 300 ms tween) |
  | `clear` | dispose everything in the scene |

- **Render on demand:** frames are drawn only while something is changing: a tween, a playing animation, or a pending op. Otherwise the render loop stops. `__describe()` reports a frame counter so the host can check zero frames while idle.
- **Warm-up:** on the scene window's first reset, build one hidden mesh per shape × finish, render once, then dispose them. That compiles the shaders, so the lesson's real model builds warm (spike: ~200 ms instead of ~370 ms).
- **No WebGL:** if `new BABYLON.Engine` fails, the page reports `webgl: false` and shows "3D view isn't available on this PC — the lesson continues in speech". Every scene op is then acknowledged as skipped, so the beat completes and the speech plays.
- **`__describe()` (read-only, for rows):** webgl version, instances (model id, part count, mesh count), labels (id, target, text), highlighted targets, explode amounts and each part's current offset, playing animations, camera (view, focus), frames rendered, CSP violations, header tags.

### C# side

- **`SceneMessages.cs` (new).** Turns validated scene ops into page messages, taking geometry from `ModelLibrary` by model id:
  - **Rotations:** `ToQuaternion(rotationDeg)` = q(Y)·q(X)·q(Z), so Z is applied first, matching `Geometry.Rotate`.
  - **Rotation keys are densified** to ≤ 45° apart before sending. The DNA `spin` keys at 0/180/360° would otherwise slerp ambiguously, since two shortest paths are equally short at 180°.
  - Positions are sent as authored.
- **`CardSurface`:** the scene window lifecycle, op routing, placement, and `DescribeSceneAsync(surfaceId)` → `SceneSnapshot`. It also gets:
  - `CheckOrientationAsync()` → `OrientationProbe`. A self-test usable as a health check, like `ProbeIsolationAsync`. It shows three coloured test boxes at known C#-computed poses, captures the page (`CapturePreviewAsync`), and reports whether +X renders right, +Y up, and a 90° Z rotation turns +Y into −X. This is the snapshot confirmation DEC-029 asked for, automated.
  - `public static ModelCatalog Scene3dCatalog => ModelLibrary.ToCatalog()`, the catalogue a caller passes as `ValidationContext.Scene3d` when it uses this canvas (step 6 wires it).
- **`CardWindow`:** a page parameter (`lesson.html` | `scene.html`); nothing else changes.
- **`CardSurfaceOptions`:**
  - `SceneWidthDip` = 640 and `SceneHeightDip` = 480;
  - `AdditionalBrowserArguments`, for the verification host's no-WebGL row only (`--disable-webgl`);
  - new records `SceneSnapshot` and `OrientationProbe`.
- **`HeyGanesh.LessonSurface.csproj`:** + ProjectReference `HeyGanesh.PartLibrary`. The vendored files ship via the existing `Page\**\*` content item.

### Memory and latency (DEC-030)

- **The scene window is created on the first 3D lesson, not at startup.** The 360–500 MB is only paid when a 3D lesson runs.
  - It's kept while that `CardSurface` lives, hidden between scenes, and disposed with it.
  - Today `CardSurfaceHost` makes a fresh `CardSurface` per turn, so each 3D turn pays the cold start: environment, about 0.4 s to parse `babylon.js`, and the warm-up. I expect about 1.5–2 s inside `ResetAsync`, before the first beat. The rows measure and print it, and DEC-030 records the measured number.
  - Keeping one surface across turns, or pre-warming at app start, is a separate change for later, recorded as such.
- **Model beats must ack within the existing `ApplyTimeout`** (3 s). The row prints the measured warm build next to the 300 ms budget.

## Files

| File | Change |
|---|---|
| `src/Experience/HeyGanesh.LessonSurface/CardSurface.cs` | scene window lifecycle, op routing, placement, `DescribeSceneAsync`, `CheckOrientationAsync`, `Scene3dCatalog` |
| `.../CardWindow.cs` | page parameter |
| `.../CardSurfaceOptions.cs` | scene size, `AdditionalBrowserArguments`, `SceneSnapshot`, `OrientationProbe` |
| `.../SceneMessages.cs` (new) | ops + library geometry → page messages; quaternions; key densification |
| `.../Page/scene.html`, `scene.css`, `scene.js` (new) | the scene page |
| `.../Page/vendor/babylonjs-9.30.0/*` + `Page/vendor/VENDOR.md` (new) | vendored Babylon + pins |
| `.../HeyGanesh.LessonSurface.csproj` | + PartLibrary reference; header comment |
| `.gitattributes` | vendor files `-text` |
| `tools/LessonSurfaceSmoke/LessonSurfaceSmokeHost.Scene.cs` (new), `.cs`, `.Source.cs` | new modes; source audit covers the scene page, vendor hashes and the new reference |
| `.github/workflows/ci.yml`, `docs/ai/CI.md` | **G-17** Lesson Surface offline (`--assert-source`, `--assert-scene-math`); the window rows stay local-only (they need a desktop) |
| `docs/ai/DECISIONS.md` | DEC-030 |
| `docs/ai/FLOW.md`, `ARCHITECTURE.md`, `AGENTS.md`, `TEST-CHECKLIST.md` (LSU-7..LSU-14), `features/FEATURE-007.md`, `HANDOVER.md`, `ROLLBACK.md` (written **before** the code) | docs |

No change to VisualLessonEngine, PartLibrary, LessonPlanner, LessonSession or the App.

## Rows (LessonSurfaceSmoke)

| Row | Mode | Needs desktop | Asserts |
|---|---|---|---|
| LSU-7 | `--assert-source` (extended) | no | refs = Abstractions, PartLibrary, ScreenCapture, ScreenOverlay, VisualLessonEngine; still exactly one PackageReference; `scene.html` CSP without `unsafe-eval`/`unsafe-inline`; `scene.js` has no HTML/code sinks; each vendor file's SHA-256 equals `VENDOR.md`; LICENSE present; `.gitattributes` marks vendor `-text` |
| LSU-8 | `--assert-scene-math` | no | `ToQuaternion` rotates 500 seeded random vectors exactly as `Geometry.Rotate` (≤ 1e-12); every sent rotation key gap ≤ 45°; each V1 model's message carries exactly the library's parts and primitives |
| LSU-9 | `--assert-scene` | yes | `engine-3d.json` (on `four-stroke-engine`, validated with the real catalogue) plays through `CardSurface` with a fake speaker: every beat acked within `ApplyTimeout`; the page reports WebGL 2, instance `engine` with 10 parts / 17 meshes, 2 labels with exact texts, highlights on `engine.piston` → `engine.connecting-rod` → `l-rod`, explode 0.6 then 0 with part offsets = amount × offset (±1e-6), `stroke-cycle` playing after b4, camera `side` on `engine.crankshaft`; 0 CSP violations; header shows "not yet reviewed"; scene window NOACTIVATE + TOOLWINDOW + TOPMOST + `WDA_EXCLUDEFROMCAPTURE`, never foreground; prints cold scene-window time and warm model build time |
| LSU-10 | `--assert-scene-idle` | yes | frames > 0 while `stroke-cycle` plays; 0 frames over 2 s once the pump's non-looping `pump-cycle` has ended (render on demand) |
| LSU-11 | `--assert-orientation` | yes | `CheckOrientationAsync`: +X right, +Y up, Z+90° maps +Y → −X, read from captured pixels |
| LSU-12 | `--assert-scene-nowebgl` | yes | with `--disable-webgl`: the scene shows the unavailable notice, every beat acks (skipped), the fake speaker hears every sentence, no exception |
| LSU-13 | `--snapshot-3d <dir>` | yes | PNG of each V1 model, rest + exploded, iso view (manual: the owner checks the look) |
| LSU-14 | `--demo-3d [--voice sapi\|timed]` | yes | the engine lesson drawn and spoken (manual) |

Plus every existing row stays green: LSU-2..LSU-6, LessonTurnSmoke LT-6 (2D surface rows), and G-1..G-16.

## Alternatives considered

- **Send Euler angles and let Babylon apply them.** Rejected: Babylon's Euler order and handedness would have to match `Geometry.Rotate` exactly, and a mismatch would draw every model subtly wrong while every C# check still passes. Quaternions computed in C#, plus an orientation self-test, remove that class of bug.
- **One transparent full-screen 3D surface over the desktop.** Rejected again (DEC-020): the riskiest hosting mode, with selective click-through, and nothing in the demo needs the scene to float.
- **Draw the 3D inside an existing card page.** Rejected: it puts 8.6 MB of Babylon and its GPU memory into every 2D card. A separate scene page keeps 2D lessons at today's ~143 MB.
- **three.js instead of Babylon.** Already decided (FEATURE-007 §1.3, spike 0b measured Babylon).
- **Pre-warm the 3D page at app start.** Deferred: it costs 360–500 MB on every run for a lesson type the planner can't produce until step 6. DEC-030 records the trade.

## What could break, and how we'd know

- **CSP blocks something Babylon needs** (inline style, blob worker). LSU-9's violation counter and a failed build would show it. I'd then stop and ask rather than relax the CSP.
- **Orientation or handedness wrong.** LSU-11 reads real pixels; LSU-13 snapshots go to the owner.
- **2D regressions** from the routing change. LSU-2..LSU-5, LT-6 and the 2D demo rows re-run unchanged.
- **Focus theft or capture leaks from the new window.** LSU-9 checks styles, affinity and the foreground window, as LSU-2 does for cards.
- **Idle CPU/GPU burn.** LSU-10.
- **A machine without WebGL** loses the drawing but not the lesson. LSU-12.
- **The hashes break on checkout.** LSU-7 plus the `.gitattributes` rule, and the fresh-checkout build runs LSU-7.
- **Timing on slow GPUs.** Only measured on this RTX 3060 / WARP machine; integrated-GPU numbers are still unmeasured (spike 0b).

## Verification

```
dotnet build src/Experience/HeyGanesh.LessonSurface -c Debug -warnaserror
dotnet build tools/LessonSurfaceSmoke -c Debug -warnaserror
...LessonSurfaceSmoke.exe --assert-source | --assert-scene-math                      -> PASS, exit 0 (CI G-17)
...LessonSurfaceSmoke.exe --assert-scene | --assert-scene-idle | --assert-orientation | --assert-scene-nowebgl   -> PASS, exit 0 (this desktop)
...LessonSurfaceSmoke.exe --assert-window | --assert-render | --assert-security | --assert-marks                 -> unchanged PASS counts
...LessonTurnSmoke.exe --assert-turn-surface                                                                    -> 6 PASS
...LessonSurfaceSmoke.exe --snapshot-3d <dir>; --demo-3d --voice sapi                                           -> manual
dotnet build HeyGanesh.sln -c Debug -warnaserror; local CI runner (13 gates); fresh-checkout build + LSU-7       -> 0/0, all exit 0
```

## Asks (before building)

1. **Commit the vendored Babylon files** (~9.3 MB) into the repo, as above.
2. **Fetch one more file:** the Apache-2.0 `LICENSE` from `babylonjs@9.30.0` on cdn.jsdelivr.net (a few KB). The two scripts are reused from the spike's scratchpad copies, re-hashed before copying.
