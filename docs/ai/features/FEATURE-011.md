# FEATURE-011 — Adopt openclicky's drawing stage (draw-stage) for lessons

## 2026-11-07 · claude-opus-5-5 — PLAN (awaiting owner approval; no product code written)

Owner's words: "use majority of their drawing and 3d system and scrape ours keep only the best and relevant
parts first plan it and start building when i approve."

Source: https://github.com/prasanthsasikumar/openclicky, MIT, commit `5b838ad`. Read in a scratchpad clone
and never run there.

### 1. The problem, restated

Today a lesson is VisualPlan JSON. The model fills in closed-set ops; we parse, validate and degrade them
(DEC-023/028/048), draw them on opaque WebView2 cards, and show a Babylon scene of hand-built Part Library
models. It is safe and measurable, but it looks stiff. The 3D side only has three catalogue models plus
small schematics.

openclicky's model writes a small JS program in a drawing language (`step`, `say`, `line`, `card`,
`figure`, `f.lathe`, `f.blob`, …). A Web Worker with no network runs it one statement at a time while it
streams. The worker returns marks (plain data). The page draws them as pen strokes, timed to each
`say()` line as it is spoken. 3D is a three.js figure engine that can build any object.

The goal is to make that the only lesson renderer, and to keep only the parts of ours that still add
something.

### 2. What we adopt from openclicky

| Piece | Their file | Notes |
|---|---|---|
| Drawing language + statement scanner + pen pacing timed to speech | `stage.js` | Two clocks: `estimate`, or `speech` (`lineDue` → we speak → `lineStarted`/`lineEnded`). |
| Worker sandbox | `stage-worker.js` | Statements run in a Worker. 2 s per statement (the page terminates the worker). 2000 marks. `random()` is fixed. |
| 3D figure engine | `figure3d.js` | three.js r172 + RoomEnvironment: lathe, extrude, tube/pipe, gear, spring, metaball `blob`, materials, `cut`, `copies`/`around`/`mirror`, motions, labels, drag to turn. |
| Page + style | `index.html`, `stage.css` | CSP `connect-src 'none'`. |
| Prompts | `prompt/drawing-language.md`, `question.md`, `follow-up.md` | Includes the `NONE` way out and the follow-up `keep()`. |
| Samples | `samples/*.js` | Become smoke and eval fixtures. |
| Click on a part | `figure3d.js` posts `figurePart {text}` | The part name comes from the program's const (`liftFan` → "lift fan"). |
| Host contract | `DrawStageMessages.cs`, `DrawStageController.cs`, `DrawingAnswer.cs`, `DrawStageWindow.cs` | Ported into our style. Not copied whole: recording, saving, photos and pieces are left out. |

**Which copy.** The page files under `windows/src/OpenClicky.Windows/Assets/DrawStage/` differ from
`draw-stage/` and are newer and stricter. The worker fixes each fenced global with
`defineProperty(writable:false, configurable:false)`. It fences `WebTransport`, `File`, `FileReader`,
`Request` and `Response` too. It runs no program if any fence fails. The host serves the worker script
with its own CSP (`script-src 'unsafe-eval'; worker-src 'none'; connect-src 'none'`), which also blocks
`import()` of a URL. **We vendor the Windows copy.**

Left out on purpose:

- **Picture/Video save and `DrawingRecorder`.** Not asked for.
- **`f.piece()` and PieceMakers.** These are network 3D generators (Hunyuan3D, LLaMA-Mesh).
- **`photo()`.** See §5.
- **Their settings pages and island UI.**

### 3. What we remove, and what survives (candidates; the owner decides each)

| Ours | Proposal | Why |
|---|---|---|
| VisualPlan contract, parser, validator, degrade (`VisualLessonEngine`, ~2.0k lines) | **Remove** | A program replaces the plan. The validator cannot read a program. |
| `LessonPlayer`, `PlotExpression`, `ILessonCanvas` | **Remove** | `stage.js` does the pacing and plotting. |
| `VisualPlanner` / `PlannerPrompt` / repair turn / `PlanStreamScanner` | **Replace** with a `DrawingPlanner` | Same seam: `ILlmClient`, streaming when the client streams. Their prompts are filled from code. |
| `CardSurface`, `lesson.*`, `scene.*`, `scene-svg.*`, Babylon 9.30 and Anatomy vendor (~3.5k lines) | **Remove** | The stage page replaces them. The LSU-18 tail-pick bug goes with the Babylon scene. |
| Part Library (3 vetted models, schematics) | **Remove** | Their figures build any object, and a catalogue would hold the model back. Its physics could come back later as sample programs; that is not in this sequence. |
| `LessonTurn` / `RunWithFollowUpsAsync` (30 s click wait, Ctrl+Alt refused meanwhile) | **Replace** | Their model: a drawing stays up, and the next question asked by voice is a follow-up (`keep()` or redraw or `NONE`). A part click also becomes a follow-up question. |
| **Checking the worker's marks before painting** | **Keep (recommended)**, in a new form | Marks are plain data. A small closed check in `stage.js` sits between the worker and the pen: known `do` kinds, finite numbers inside the canvas (± a margin), known colours, capped text length, known part kinds and materials, and a part cap. A failing mark is skipped and logged. This is the part of DEC-023 that still applies. |
| **Capture exclusion** | **Keep (invariant)** | `CaptureExclusion.TryExcludeFromCapture` on the stage window. Their window does the same. |
| **PerMonitorV2 / physical pixels** | **Keep (invariant)** | Canvas W×H = the cursor monitor's capture in physical pixels, so the page maps 1:1. |
| **Grounded marks on the real screen** | **Keep, as prompt grounding** | Element Selection's box goes into the user prompt as canvas coordinates, marked untrusted. The model circles or points at it itself (their prompt already asks for this). `ScreenMarkOp` and the overlay forwarding go away; the Screen Overlay itself stays for the point leg. |
| **Vetted Part Library descriptions for click-to-ask** | **Drop** | A model-built figure has none. A click sends the part's const name, cleaned, length-capped and fenced as untrusted, as a follow-up question. That is weaker than vetted text, and the owner should know it. |
| **Notes + Tray replay** (DEC-058/060) | **Keep, reworked** | Store the program text in the existing `LessonPlan` column with a `drawing:` marker. Replay feeds it with no model call (pace `speech` with the shell's voice, or `estimate` when silent). An old VisualPlan row says "made by an older version; cannot replay". No schema change. |
| **Router: point / explain / lesson** (DEC-035/036) | **Keep the intent question; drop dimension, model and schematic** | The program decides 2D or 3D. `RouteRequest.Models` and `Schematics` go. |
| **Tutor leg and `NONE`** | **Keep** | A `NONE` reply hands the turn to the Tutor's spoken answer, so nothing is lost. |
| **Eval harness** (PlannerEval, 30 cases, DEC-043) | **Keep, with new checks** | See §6. |
| **Session context** (DEC-061) | **Keep** | Earlier turns are fenced into the drawing prompt as they are into the planner today. |
| FEATURE-009 glass look on cards | **Drop for lessons** | The stage draws its own paper `card()`. Restyling `stage.css` with our tokens can be a later, optional step. |

### 4. The new dependency: decision needed

**three.js r172** (0.172.0, MIT), vendored as files: `three.module.min.js`, `three.core.min.js`,
`RoomEnvironment.js` and LICENSE, about 700 KB. No NuGet, no npm at build time. It is served from the
page folder like Babylon is today. AGENTS.md says to ask before adding a dependency.

- **A (recommended): vendor three.js r172.** `figure3d.js` runs unchanged, and upstream fixes carry
  over. Babylon 9.30 and Anatomy are deleted in step 10, so the total vendor size goes down.
- **B: keep Babylon and port `figure3d.js` to it** (67 KB, ~1,300 lines written against three.js: PMREM
  room lighting, `LatheGeometry`, `ExtrudeGeometry`, `TubeGeometry`, its own metaball mesher, clipping
  planes for `cut`). No new dependency, but it is a rewrite with a high risk of looking worse, and it
  would drift from upstream.

### 5. Safety: model-written code runs locally

What the model writes is code. It is shaped by the question, the screen text and earlier turns, all of
which are untrusted. The fences, in order:

1. **Worker only.** No DOM. Network and code-loading globals are fixed to `undefined` (Windows copy),
   and no program runs if any fence fails.
2. **Two CSPs.** The page CSP: `default-src 'none'; script-src 'self' 'unsafe-eval'; worker-src 'self';
   connect-src 'none'`. The worker CSP is set by the host through `WebResourceRequested`:
   `script-src 'unsafe-eval'; worker-src 'none'; connect-src 'none'`. **New for us:** our pages forbid
   eval today, and this adds `'unsafe-eval'`, which the worker needs.
3. **Host.** A virtual host on `*.heyganesh.invalid` with host resource access `Deny`. Every other
   navigation is cancelled. DevTools off. No host objects. Web messages only.
4. **Limits.** 2 s per statement (then the worker is terminated), 2000 marks, at most 5 errors reported.
   **Proposed addition:** a whole-program wall-clock cap (for example 20 s of worker time), because many
   short statements of 1.9 s each are not limited today.
5. **Mark check before painting** (§3), the survivor of DEC-023.
6. **Messages from the page are untrusted.** `figurePart` text is cleaned and capped before it goes into
   a prompt. No page message can start anything except a follow-up question.

**`photo()`. Proposal: drop it in v1.** Their host fetches Wikimedia, so the page has no network. But
the search query is model text, and the model saw the screen, so a query is a channel that can leak
screen content to a third party. If the owner wants it later, it would be a host-side fetch from a fixed
endpoint, with the query limited to a small allowed vocabulary or confirmed by the user. The prompt line
for `photo` is removed, and the worker's `photo` becomes a no-op that warns.

### 6. Measuring programs (PlannerEval)

K-1..K-12 read VisualPlan JSON. A program gives the same evidence in two layers:

- **Static (offline, CI):** extract the `say()` lines from the program text. K-2 (beat count and words),
  K-3 (mentions), K-4 (misconceptions), K-8 (screen terms), K-9 (injection), K-10 (answer first) and
  K-11 (follow-up is not a repeat) carry over onto the spoken lines.
- **Run (local-only, real WebView2 worker):** a hidden `DrawStageWindow` runs the program with pace
  `estimate` at most speed and returns its marks and errors. New checks: P-1 the program ran without
  stopping, P-2 the marks passed the mark check, P-3 2D vs 3D as the case expects (`figure` present),
  P-4 a grounded case points inside the box, P-5 the parts are within the cap.
- **Retired:** K-5 catalogue model, K-6 invented catalogue names and K-12 schematic says simplified (no
  catalogue). K-1 (degraded drawings) becomes P-1/P-2.

The 30 cases and the recordings stay. New recordings are needed: old VisualPlan recordings cannot be
replayed against program checks.

### 7. Execution path affected (FLOW.md)

The sections "Lesson playback", "Lesson planning", "Lesson leg", "Schematic models", "Turn routing" and
the replay part of "Tray" are rewritten step by step as each change lands. The new path:

```
verdict → TeachAsync → route (point | explain | lesson)
  lesson → DrawingPlanner.StreamAsync(question, W×H of cursor monitor, grounded box?, screen text?, earlier turns)
         → ILlmClient.StreamAsync (or GenerateAsync: Codex)  → ```js block extracted as it grows
         → "NONE" first word → Tutor leg
         → DrawStageController: beginStream{w,h,pace:'speech'} → feed{code,done}
             page: scanner → worker (statement) → marks → mark check → pen
             page → line{k,text} (prefetch) · lineDue{k} → voice.SpeakAsync → lineStarted/lineEnded
             page → finished → note recorded (program text) → drawing stays up
  next question while a drawing is up → follow-up prompt (program + question) → keep() | redraw | NONE
  figurePart{text} → follow-up question "tell me about the <part>" (cleaned, fenced)
```

### 8. Alternatives considered

- **The hybrid from the assessment** (their renderer, our VisualPlan JSON translated into drawing
  calls). Rejected: it keeps the stiffness the owner wants gone, and the 3D side would still be limited
  to the catalogue.
- **Host the stage inside our opaque card window instead of a full-monitor transparent window.** It is
  smaller (the window exists and is verified), but the drawing could not sit on the real screen next to
  what it explains, which is the core of the HeyClicky feel. Rejected as the end state. Step 2 builds the
  transparent window directly.
- **Run the program in C# (a JS engine).** Needs a new dependency and duplicates the worker. Rejected.

### 9. What could break, and how we would know

- **The transparent composition window** (Windows.UI.Composition + `CoreWebView2CompositionController`)
  is new to us. The CsWinRT projections come with the existing TFM, so no package. *Known by:* DS-3
  (styles, exclusion, hit test) and a snapshot.
- **Click-through vs drag-to-turn on figures.** Their window takes the mouse only over the reported
  `figures` rects (`WM_NCHITTEST`). *Known by:* DS-3 and a real-cursor pick row (local-only, moves the
  cursor).
- **The speech clock.** Our `VoiceService.SpeakAsync` synthesises and plays one line per call, with no
  prefetch, so there can be gaps between lines on Fish. *Known by:* line timings logged by the
  controller; prefetch is a later step if needed.
- **Codex is not streamed.** The first stroke waits for the whole reply. That is accepted, and the
  first beat p50 is recorded per provider.
- **Notes rows from before the switch cannot replay.** That is accepted with a clear message.
- **`'unsafe-eval'` + model code.** Fenced as in §5. *Known by:* DS-4 hostile programs.
- **CI gates G-13..G-17 and G-22 go away or change in step 10.** `ci.yml` is updated in the same step.

### 10. Steps (one logical change each; only step 1 is built after approval)

Each step lists its proof rows. The rows are added to TEST-CHECKLIST.md in that step.

0. **ROLLBACK + decisions (docs only).** A ROLLBACK.md entry for the whole sequence: base commit
   `d358484`, a branch per step. DEC-067 (adopt draw-stage, the survivors in §3) and DEC-068 (three.js
   vs Babylon, the owner's choice) are written with the owner's answers.
1. **Vendor the stage page, unchanged.** New `src/Experience/HeyGanesh.DrawStage/Page/` holds the Windows
   copy of `index.html`, `stage.js`, `stage-worker.js`, `figure3d.js`, `stage.css`, `vendor/three/*`,
   LICENSE files, `prompt/*.md` and `samples/*.js`. `VENDOR.md` records the source commit and SHA-256 of
   every file. The project is a class library holding only the content files. A new host
   `tools/DrawStageSmoke` adds **DS-1 `--assert-source`**: every file is present and matches its pinned
   SHA-256; the page CSP has `connect-src 'none'`; no `http(s)://` URL in any page file except comments
   and licences; the worker's fence list includes fetch, Worker, Blob, WebTransport and Request.
   Offline, CI-able. No wiring.
2. **`DrawStageWindow`.** A transparent, click-through, topmost, no-activate popup over one monitor.
   WebView2 in composition mode. Capture-excluded. Virtual host + `Deny`, navigation cancel, worker CSP
   via `WebResourceRequested`, DevTools off. Rows: **DS-2 `--assert-window`** (styles,
   `WM_NCHITTEST` transparent outside figure rects, exclusion applied, never activated),
   **DS-3 `--snapshot <dir>`** (a sample drawn → PNG via `CapturePreviewAsync`, reviewed by eye).
   Local-only.
3. **Sandbox and mark check.** Add the mark check to `stage.js` and the whole-program time cap. This is
   our first edit to vendored code, so it is marked in VENDOR.md. Rows: **DS-4 `--assert-sandbox`**:
   hostile statements (fetch, `import()`, `new Worker`, Blob URL, `while(true)`, 2001 marks, a 25 s
   program) each end as expected and nothing reaches the network. **DS-5 `--assert-marks`**: bad marks
   (NaN, off-canvas, unknown kind or colour, 10 kB text) are skipped and logged, and good ones are drawn.
4. **`DrawStageController` + speech clock.** A C# port: beginStream, continueStream, feed, line /
   lineDue / lineStarted / lineEnded, finishNow, clear, sleep, wake, and the `finished` and `figurePart`
   events. Speech goes through a `Func<string, CancellationToken, Task>`. Rows: **DS-6 `--assert-pace`**
   (fake speaker: lines spoken in order, each timing sent, `finished` after the last; a cut-short voice
   → `finishNow`). **DS-7 `--demo <sample> --voice sapi`** by hand.
5. **`DrawingPlanner`** (in LessonPlanner, next to the old planner until step 10). Prompts filled from
   code (`{{W}}`, `{{H}}`, `{{captionTop}}`, beats, calls, parts); grounded box, screen text and earlier
   turns fenced as untrusted; `NONE` detection; streaming `js` block extraction; a follow-up prompt with
   `keep()`. Works through any `ILlmClient`, including `ClaudeCodeLlmClient` (streamed) and
   `CodexLlmClient` (whole reply). Rows: **LP-14 `--assert-drawing-prompt`**, **LP-15
   `--assert-drawing-stream`**, **LP-16 `--assert-drawing-none`** (recording fake client, offline, CI).
6. **PlannerEval for programs.** Static checks, P-1..P-5 run checks, `--record --drawing` on every
   provider. Rows: **PE-9 `--assert-program-checks`** (offline), **PE-10** live recording of E-01 and
   E-03 on the Claude plan (owner).
7. **App Shell lesson leg switches to the stage.** `--lesson` and routed lessons use DrawingPlanner +
   DrawStageController. A drawing stays up; the next question while it is up is a follow-up; a
   `figurePart` click asks a follow-up. Ctrl+Alt is no longer refused after a lesson. Rows: SH-18 updated
   (the offline key gate is unchanged), **SH-23** live (owner).
8. **Router shrinks** to intent only (dimension, model and schematic dropped), with its rows updated.
9. **Notes + replay** store and replay the program; old rows get the message. RL and NB rows updated.
10. **Delete the old stack**: VisualLessonEngine, VisualPlanner/PlannerPrompt, CardSurface pages +
    Babylon + Anatomy, PartLibrary, LessonTurn, and their smoke hosts. `ci.yml` gates updated, plus
    AGENTS.md, ARCHITECTURE.md and FLOW.md. This is large, so it gets its own ROLLBACK entry first.
11. *(optional)* Restyle `stage.css` with FEATURE-009 tokens; restore `photo()` host-side if the owner
    wants it.

### 11. Decisions the owner is asked for

1. Approve the plan, or change it (step order, survivors in §3).
2. **three.js r172 vendored (A, recommended) or port to Babylon (B)?**
3. `photo()`: drop in v1 (recommended), or a host-side fetch later?
4. Click-to-ask on model-built parts with the const name only (no vetted descriptions): acceptable?

### Status

- 2026-11-07 · claude-opus-5-5: plan written; waiting for the owner. Nothing built.
- 2026-11-07 · claude-opus-5-5: **approved** ("Approve, use three.js"). Decisions 3 and 4 were taken as recommended in the plan: `photo()` is dropped in v1, and click-to-ask uses the part's const name. Recorded in DEC-067 and DEC-068.
- 2026-11-07 · claude-opus-5-5: **step 0 done.** ROLLBACK entries for the sequence and for step 1 (rollback point `d358484`), plus DEC-067 and DEC-068.
- 2026-11-07 · claude-opus-5-5: **step 1 done.** `src/Experience/HeyGanesh.DrawStage` (content-only) holds the page, prompts, samples and three.js r172, 23 files pinned in `VENDOR.md`. `tools/DrawStageSmoke --assert-source` (DS-1) gives 13 PASS, and CI gate G-27 runs it. Each of three mutations fails it: a changed byte, an unlisted file, a planted address.
  - Trap found and fixed: a Windows clone (autocrlf) and `git archive` both turn LF into CRLF, so the first copy's hashes matched nothing upstream. The files were re-exported with `git -c core.autocrlf=false -c core.eol=lf archive`. three.js module, core and licence now equal npm's published hashes.
  - Next: step 2 (`DrawStageWindow`).
- 2026-11-07 · claude-opus-5-5: **step 2 done (DEC-069).** `DrawStageWindow` + `StageThread` in `HeyGanesh.DrawStage`: a transparent, click-through, topmost, never-activated, capture-excluded composition window over one monitor, with the page served by a resource handler and the worker's own CSP.
  - Rows: DS-2 `--assert-window` 13 PASS; DS-3 `--snapshot` 2 PASS, with both PNGs reviewed by eye (the 2D rocket, and the three.js jet-engine cutaway with its labels). DS-1 still 13 (its reference check now expects WebView2 + Abstractions + ScreenCapture).
  - Failed attempt, logged: the first DS-2 run failed the clip check. A page box of `1e12` overflowed the `double`→`int` cast and became empty; it is now clipped in double first.
  - One DS-2 run in ten failed one check, and the failing line was not captured. The suspected cause, not confirmed, is a mouse move out of the 80 px test box. The box now covers the whole monitor: 5 of 5 pass since.
  - Mutations caught: no capture exclusion, no worker CSP header, navigation not cancelled. That last mutant really did load example.com once.
  - Found for step 4: the vendored page shows its Picture / Video / PiP / close buttons from the start (openclicky `e411d99`), and they post `save` and similar messages nobody handles yet. Step 4 either handles close and PiP and hides save, or a step 3 CSS edit hides them; the choice is the owner's.
  - Next: step 3, the sandbox rows and the mark check.
