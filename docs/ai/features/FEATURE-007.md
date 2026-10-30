# FEATURE-007 — Visual Lesson Engine: real-time 2D/3D teaching visuals (strategy + roadmap)

- **Date:** 2026-10-28 · claude-opus-5-5
- **Status:** in-progress — Phase 1 (VisualPlan contract v1) BUILT 2026-10-28 · claude-opus-5-5 on branch
  `feat/visualplan-contract-v1` (merged to main), LS-1..LS-7 green offline (DEC-023). Phase 0 DONE 2026-10-28
  (DEC-020/021/022; spike 0a GO, 0b deferred, 0c blocked on a key). Phase 2 (Lesson Runtime) BUILT 2026-10-28
  on `feat/lesson-runtime-v1`, LS-8..LS-10 green offline; DEC-024 keeps the planner multimodal and multi-model.
  Phase 3 (Lesson Surface, 2D cards) BUILT 2026-10-28 on `feat/lesson-surface-v1` (DEC-025): LS-11 + LSU-1..LSU-5
  green on the real desktop, LSU-6 demo runs; KaTeX/Rough.js deferred. Phase 4 (Lesson Planner) BUILT offline 2026-10-28 on
  `feat/lesson-planner-v1` (DEC-026): LS-12 + LP-1..LP-7 green; LP-8 live row, streaming and the eval harness wait for a key. Eval harness v0 step 7a BUILT 2026-10-30 (DEC-043): `tools/PlannerEval`, 30 cases + checks K-1..K-11, PE-1/PE-2 green offline; real scores wait for 7b and a key. Schematic 3D contract (step 10a, DEC-048): VisualPlan 1.2 lets a lesson define one tagged, simplified model for an object the library lacks (LS-14, PL-6); planner, scene and router follow in 10b–10d.
  Phase 4.5 (App Shell wiring) BUILT 2026-10-28 on `feat/lesson-shell-wiring-v1` (DEC-027): `--lesson` runs
  LessonTurn (new HeyGanesh.LessonSession) after a produced verdict; LT-1..LT-6 + SH-18 green offline; SH-19 live-gated.
  Plan: [plans/2026-10-28-lesson-shell-wiring-v1.md](../plans/2026-10-28-lesson-shell-wiring-v1.md).
  Phase 5: spike 0b run 2026-10-29 — GO with three conditions (Phase 0 results below). Step 2 (scene3d contract,
  VisualPlan 1.1, DEC-028) BUILT 2026-10-29 · claude-opus-5-5 offline: LS-13 green, 3D off by default
  ([plan](../plans/2026-10-29-scene3d-contract-v1.md)). Step 3 (part library v1, DEC-029) BUILT 2026-10-29 · claude-opus-5-5 offline: PL-1..PL-4 green,
  three models (engine, DNA, schematic Na+/K+ pump), none Reviewed yet ([plan](../plans/2026-10-29-part-library-v1.md)).
  Step 4 (3D renderer, DEC-030) BUILT 2026-10-29 · claude-opus-5-5: scene window with vendored Babylon.js 9.30.0,
  LSU-7..LSU-12 green (cold start ~2 s, warm model beat ~115 ms, orientation pixel-checked, no-WebGL speaks on)
  ([plan](../plans/2026-10-29-scene-renderer-v1.md)).
  Step 5 (click a 3D part to ask, DEC-031) BUILT 2026-10-29 · claude-opus-5-5: picking after playback, ids checked against
  what was drawn, library-written question, follow-up on the same cards; LT-8 12 PASS, LSU-15 9 PASS (real mouse)
  ([plan](../plans/2026-10-29-part-pick-v1.md)). Step 6 split in two ([plan](../plans/2026-10-29-planner-3d-v1.md)): 6a (planner prompt learns 3D, DEC-032) BUILT 2026-10-29 · claude-opus-5-5,
  LP-10 10 PASS, 2D prompt byte-identical; 6b (App Shell passes the catalogue + click-to-ask loop, DEC-033) BUILT 2026-10-29 · claude-opus-5-5: offline shell rows unchanged,
  live 3D turn SH-20 human-gated. Phase 5 code complete; live verification waits for keys.
  Phases 6–8 not started. Phase 1 plan:
  [plans/2026-10-28-visualplan-contract-v1.md](../plans/2026-10-28-visualplan-contract-v1.md).
- **Module(s):** Visual Lesson Engine (new), Tutor Engine, Screen Overlay, Voice Pipeline, App Shell.
- **Decisions this document asks the owner for:** DEC-020 (rendering stack), DEC-021 (planner
  provider path), DEC-022 (tutor session state). None is taken by this document.

## Description

A student holds Ctrl+Alt, asks about what is on screen ("how does the chain rule give 6x/(3x²−1)?",
"show me a sodium–potassium pump in 3D"), and HeyGanesh answers by **speaking and drawing at the same
time**: paper-style cards fill in step by step, marks land on the student's own screen, and — when the
subject is a physical thing — a labelled 3D figure appears that the student can rotate and click a
part of to ask about it. It can also stop and ask the student a question, check the answer, and give
graded hints instead of the solution.

Reference product: HeyClicky v1.1.1 "real-time visuals" (2026-10-23). From their launch stream and
screenshots: lessons are a stream of short *beats* (one spoken sentence + the drawing that goes with
it); cards are cream graph-paper sheets with a handwritten face; 3D figures are assembled from simple
primitives with named parts, sticky-note labels on leader lines, highlight / exploded-view / part
animation per beat, drawn transparently over the screen; drawings are replayable from history. Their
3D engine is in-house. HeyGanesh copies the *teaching* half only — agents, "Drive" (screen
actuation), browser automation and routines are out of scope, and the Tutor Engine's no-actuation
rule (FEATURE-006) stays absolute.

## Out of scope
- Any screen actuation (clicking, typing, driving apps) — Tutor Engine contract.
- General-assistant features: agents, web shopping, email, routines.
- Image-to-3D generative models (minutes of latency, unverifiable geometry).
- AI-written code of any kind executing in the renderer (the model emits data, never JavaScript).

---

## 1. Build vs. leverage

### 1.1 The point that decides it

**Pedagogical accuracy is not produced by the renderer.** Whether the student sees a correct
derivative, a correctly-labelled crankshaft or a correct DNA base pairing is decided by three layers
*above* rasterization: the lesson contract (what can be expressed), the verification layer (what is
checked before it is shown), and the content sources (part library, molecule geometry, computed
plots). A from-scratch engine buys control over pixels — the one layer that does not move the accuracy
needle — at the price of months that should go into the layers that do. So: **build everything that
carries meaning; leverage everything that only draws.** This agrees with the earlier suggestion
(own runtime on a mature renderer) and sharpens it: the line is *meaning vs. pixels*, not 2D vs. 3D.

### 1.2 Options compared

Scores 1 (poor) – 5 (best) for this product. "Accuracy" = how much the option helps or hurts showing
correct content; "Scale" = covering new subjects/models without engine work.

| Option | Control | Accuracy | Scale | Time to first 3D lesson | Dependency cost | Verdict |
|---|---|---|---|---|---|---|
| A. From-scratch native: D3D11 via P/Invoke, own scene graph, text, picking, AA, glTF | 5 | 3 | 2 | 3–5 months | none (zero NuGet holds) | **Rejected** — every month here is a month not spent on verification; text/label rendering and picking alone are weeks |
| B. Native .NET 3D lib (Helix Toolkit, Silk.NET + own scene, Stride) | 3 | 3 | 3 | 6–10 weeks | NuGet + smaller ecosystem; Helix's SharpDX lineage is aging; no math typesetting | Rejected — NuGet cost of B with less capability than C |
| C. WebView2 surface + Babylon.js (3D) + SVG (2D) + KaTeX (math), vendored | 4 | 4 | 5 | 3–4 weeks | one NuGet (`Microsoft.Web.WebView2`) in one Experience assembly; JS libs vendored as pinned static files | **Recommended** |
| C′. Same with three.js instead of Babylon.js | 4 | 4 | 5 | 3–4 weeks | same | Close second — see 1.3 |
| D. WebView2 without the NuGet (hand-declared COM against the runtime) | 4 | 4 | 5 | +2–3 weeks over C | none | Fallback if the owner refuses the NuGet: unsupported loading path, more interop surface to own |

### 1.3 Babylon.js vs. three.js (inside option C)

Babylon.js wins on the things an LLM-driven teaching scene needs out of the box: `HighlightLayer`
(signalling a part), GUI labels that track a mesh (`linkWithMesh` — the sticky-note callouts), ray
picking (click-a-part-to-ask), animation groups, glTF loading, WebGPU with WebGL2 fallback, a strong
backward-compatibility policy (we pin a version and our schema maps onto a stable API), Apache-2.0,
TypeScript-native. three.js is smaller and has the bigger ecosystem but churns its API across monthly
releases, and labels/highlight/picking are add-ons we would assemble. Neither choice leaks past the
renderer: the C# side speaks only our schema, so swapping is a renderer rewrite, not a product rewrite.

### 1.4 What we build (owned code, where accuracy lives)

- **VisualPlan contract** — versioned schema, parser, validator, degrade-to-text (Phase 1).
- **Lesson Runtime** — beat timeline, speech sync, state, replay (Phase 2).
- **Verification layer** — numeric spot-checks, reference integrity, vocabulary/part-library binding,
  claim provenance (Phase 4).
- **Primitive kit + curated part library** — parametric STEM models with vetted part names (Phase 5).
- **Pedagogy engine** — mode selection, Socratic ask/grade/hint ladder, session state (Phase 6).
- **Eval harness** — golden questions, rubric scoring, regression gate (Phase 7, started in Phase 4).

### 1.5 What we leverage (vendored, pinned, offline)

| Need | Library | Licence | Why not build |
|---|---|---|---|
| Host surface | WebView2 (Evergreen runtime ships with Windows 11) | MS SDK licence | Chromium-grade compositor, GPU + software fallback (HeyClicky's Windows bug "GPU can't draw 3D" is exactly what a software fallback absorbs) |
| 3D | Babylon.js | Apache-2.0 | see 1.3 |
| Math typesetting | KaTeX | MIT | TeX layout is a decade of work; KaTeX is fast and synchronous |
| Hand-drawn strokes | Rough.js | MIT | the "marker on paper" look for arrows/boxes |
| Handwritten face | an OFL font (e.g. Patrick Hand) bundled locally | OFL-1.1 | — |

All JS/CSS/fonts ship inside the app as static files served through a virtual host mapping — no CDN,
no npm at runtime, works offline, integrity-hashed in a manifest the source audit checks.

---

## 2. Technical architecture

### 2.1 Shape

```
Voice (committed transcript) ─┐
Screen context (ES/Vision box,├─► Tutor Session ──► Pedagogy Policy ──► Lesson Planner (LLM, streamed)
  bounded crop, untrusted)    │   (turn state,       (mode: explain /     │  emits VisualPlan beats
Retrieval (course docs, M8)  ─┘    learner signals)   socratic / worked)  ▼
                                                                  Verification layer (C#, deterministic)
                                                                  schema → refs → vocabulary → numeric
                                                                  spot-check → claim provenance
                                                                         │ verified beat (or degraded)
                                                                         ▼
                                     Lesson Runtime (C#) — timeline, clock, state, replay log
                                       │ speak(beat.say)                     │ render(beat.ops)
                                       ▼                                     ▼
                                Voice TTS (Fish)                   Lesson Surface (WebView2)
                                                                   2D: SVG cards + KaTeX + Rough.js
                                                                   3D: Babylon scene from part library
                                                                   screen marks over the desktop
                                                                         │ events: part-clicked,
                                                                         │ answer-given, dismissed
                                                                         ▼
                                                               back to Tutor Session (follow-up turn)
```

### 2.2 Seams (who owns what)

| Component | Assembly (proposed) | Owns | Never does |
|---|---|---|---|
| VisualPlan contract | `src/Intelligence/HeyGanesh.VisualLessonEngine/` | schema types, parse, validate, degrade | I/O, rendering, provider calls |
| Lesson Planner | same assembly, `Planning/` | prompt contract, streaming beat assembly, verification | rendering, speaking |
| Lesson Runtime | same assembly, `Runtime/` | beat clock, sync, replay log | knows nothing of WebView2 |
| Lesson Surface | `src/Experience/HeyGanesh.LessonSurface/` (only assembly with the WebView2 NuGet) | window, transparency, capture exclusion, typed message bridge, vendored assets | parses model output (it receives only *validated* plans) |
| Tutor Session | Tutor Engine (extended) or Context Orchestrator — DEC-022 | turn history, mode, learner signals | rendering |

The bridge between C# and the page carries **only validated VisualPlan fragments and typed events**,
as JSON messages with a closed op vocabulary. The page has no network access (virtual host only,
navigation blocked), runs no `eval`, and treats every string as text (no `innerHTML`). A compromised
or confused model can, at worst, produce a wrong-but-schema-valid drawing — never code execution.

### 2.3 Window model

One per-monitor, borderless, top-most, **transparent** WebView2 surface (composition hosting,
transparent default background), capture-excluded via `CaptureExclusion.TryExcludeFromCapture`,
PerMonitorV2-aware, coordinates in physical pixels (`PixelRect`). Click-through everywhere except
interactive regions: the page reports its interactive rectangles (cards, 3D canvas) to the host,
which answers `WM_NCHITTEST` with `HTTRANSPARENT` outside them. This keeps the existing overlay
contract (SO rows) true for every pixel the student did not ask to interact with. **This is the
riskiest technical assumption in the plan and is spiked first (Phase 0).**

### 2.4 Tutoring workflows the contract must express

| Workflow | Contract feature | Grading |
|---|---|---|
| Step-by-step explanation | `beats[]`, each one sentence + ≤12 ops (segmenting) | — |
| Worked example → faded example | `mode: worked-example`; later beats leave a step for the student | ask beat |
| Socratic questioning | `ask` beat: prompt, expected answer kind (numeric ± tolerance, choice, short text), 3-level hint ladder, reveal | numeric/choice: deterministic C#; short text: rubric-graded LLM call |
| Check for understanding | 1 `ask` beat at lesson end (retrieval practice) | as above |
| Knowledge retrieval | beat `claims[]` carry provenance `screen` / `retrieved:<doc#chunk>` / `general` | retrieved chunks delimited as untrusted data (existing Tutor pattern) |
| Follow-up ("simpler", "just the combustion part") | session keeps the last plan; planner gets it as context and may `refine` a surface instead of redrawing | — |
| Click a 3D part | surface emits `part-clicked(partId)`; session turns it into a follow-up question bound to that part | — |

### 2.5 Latency budget (target: first stroke ≤ 2.5 s after key release)

| Stage | Budget | How |
|---|---|---|
| STT commit | existing | Gateway |
| Planner first beat | ≤ 1.5 s | stream; outline + beat 1 first; small beats; structured output |
| Verify beat | ≤ 20 ms | pure C#, no network |
| TTS first audio | ≤ 700 ms, overlapped | speak beat *n* while planner streams *n+1* |
| Render beat | ≤ 50 ms 2D, ≤ 300 ms 3D scene build | pre-warmed WebView2 (created hidden at startup) |

---

## 3. Optimization strategy: accuracy and instructional integrity

Ordered by accuracy gained per unit of effort.

1. **Compute, don't recall.** Anything computable is computed by C#, never trusted from the model:
   plot points come from an expression the app evaluates; derivative/integral/identity claims are
   **numerically spot-checked** (evaluate both sides at random points in the stated domain; disagree
   beyond tolerance → beat flagged and re-planned or degraded). Cheap, subject-agnostic, catches the
   most common STEM hallucination class (algebra slips).
2. **Bind to vetted content.** 3D figures reference the curated part library by id + parameters
   (engine, DNA helix, cell membrane pump, circuit board, heart, eye, …) with reviewed part names and
   one-line descriptions. Molecules use real conformer geometry from a bundled set (PubChem 3D SDF),
   never model-invented coordinates. Free-form primitive composition is allowed but rendered with a
   visible **"schematic"** tag.
3. **Closed vocabularies.** Glyphs, part ids, op names, styles are enumerations. The model cannot
   reference what does not exist; the validator rejects it and the beat degrades, visibly.
4. **Referential integrity.** Every label/arrow/highlight must target an element that exists at that
   beat; every part named in `say` should exist in the scene (warn → planner retry once).
5. **Claim provenance + abstention.** Each beat's factual claims carry a basis. `general` claims with
   specific numbers/dates/names are surfaced with a hedge in speech; the prompt contract requires
   "I'm not sure" over a guess, and the eval rubric rewards it.
6. **Screen content is data.** Unchanged from FEATURE-006: on-screen text is untrusted, labelled, and
   never an instruction — a slide that says "ignore your rules" teaches nothing to the planner.
7. **Pedagogy as rules, not vibes.** The renderer enforces multimedia-learning principles: narration
   and drawing in sync (temporal contiguity), labels adjacent to their parts (spatial contiguity),
   highlight what is being said (signalling), one idea per beat (segmenting), no decorative clutter
   (coherence), captions optional rather than duplicated on-card text (redundancy). Socratic mode
   withholds the answer until two hint levels are spent unless the student asks for it.
8. **Strongest model where it matters.** Planner on the most capable reasoning model available
   through the Providers seam, low temperature, structured output; grading/rubric on a cheaper
   model; the planner prompt is versioned and every change runs the eval gate.
9. **Measure it.** Eval harness from Phase 4: ≥ 100 golden questions across math, physics,
   chemistry, biology, CS, engineering; rubric: factual accuracy, step validity, visual–speech
   agreement, pedagogy (asks before telling in socratic mode), abstention correctness. LLM-judge +
   human spot-check of 10%. A prompt or model change that lowers accuracy does not merge.

---

## 4. Roadmap

Each phase is one or more single-logical-change steps (Working Agreement), each with its own plan,
verification host rows, and docs. "Demo" = what the owner can see at the end.

| Phase | Scope | Depends on | Demo / exit |
|---|---|---|---|
| **0. Decide + de-risk** | DEC-020/021/022 by the owner. Throwaway spikes in `%TEMP%` (never committed): (a) transparent, capture-excluded, region-click-through WebView2 window at PerMonitorV2; (b) Babylon scene of 200 primitives + 10 labels at 60 fps on integrated GPU and under software rendering; (c) time-to-first-beat with streamed structured output on the chosen model | — | three numbers and a go/no-go per spike, logged in this file |
| **1. VisualPlan contract v1** | schema, parser, validator, degrade-to-text, golden fixtures, `tools/LessonSmoke` | none (pure C#) | LS-1..LS-7 green offline — [plan](../plans/2026-10-28-visualplan-contract-v1.md) |
| **2. Lesson Runtime** | beat clock, speech-duration sync against fake TTS, pause/skip/replay, replay log | 1 | a fixture lesson "plays" in the host with timestamps |
| **3. Lesson Surface, 2D** | `HeyGanesh.LessonSurface`, bridge, vendored assets, cards (write/step/arrow/icon/plot/math), screen marks, dismiss | 0a, 1, 2 | fixture lessons draw on screen in sync with fake speech |
| **4. Planner + verification** | prompt contract, streaming beats, numeric spot-check, ref checks, retry-once, eval harness v0 (30 questions) | 1, 3; SH-17 shell flip | a live voice question produces a drawn, spoken, verified lesson |
| **5. 3D** | `scene3d` surface, primitive kit, part library v1 (10 models), labels, highlight, explode, animate, picking → click-to-ask | 0b, 3, 4 | "show me a 4-stroke engine in 3D", click the connecting rod |
| **6. Socratic loop + session** | `ask` beats, deterministic + rubric grading, hint ladder, multi-turn follow-ups | 4, DEC-022 | tutor asks, student answers by voice, gets a hint, then credit |
| **7. Quality gate** | eval set to ≥ 100, CI regression gate, accessibility (keyboard, reduced motion, high contrast), perf/leak soak | 4–6 | published eval baseline; soak clean |
| **8. Production** | course-material retrieval (M8), notes + lesson history replay, installer/updates, crash reporting without content | 7 | M9 release checklist |

**Fast path to the first visible win:** Phases 1 → 2 → 3 need no live model and no 3D; with Phase 0a
done they put a fixture lesson on screen. Phase 4 makes it live.


## Phase 0 results (2026-10-28 · claude-opus-5-5)

Decisions DEC-020/021/022 taken by delegation ("quicker to build and demo, cheaper, efficient").

| Spike | Result | Numbers | Verdict |
|---|---|---|---|
| 0a — WebView2 card window | Reshaped by DEC-020 to an **opaque** card (not a transparent full-screen surface): borderless, top-most, no-activate, capture-excluded | env 384–495 ms; first paint 761–872 ms; warm update 2.3–2.5 ms; 143 MB / 6 processes; focus never stolen; capture hidden after affinity — 3/3 runs | **GO** |
| 0b — Babylon.js on integrated GPU / software | Run 2026-10-29 · claude-opus-5-5 (see below): throwaway WinForms host (WebView2 1.0.4314.35, runtime 155.0) + vendored Babylon.js 9.30.0 / babylon.gui 9.30.0, sha256-verified; 200 primitives (box/cylinder/sphere/torus, 10 materials) + 10 sticky-note labels on leader lines (`linkWithMesh`) + `HighlightLayer` on 5 meshes, auto-rotating; 900×650 opaque no-activate card, native resolution (canvas 900×651 at 150 %), 3 runs per variant | GPU (RTX 3060 Laptop, D3D11): **164–165 fps**, p95 6.8 ms. Software, `--disable-gpu` → WARP: **38–39 fps**, p95 ~31 ms (56–61 fps at CSS resolution). SwiftShader (forced): **16–21 fps**. Scene build cold **361–425 ms** (budget 300), **warm rebuild 192–206 ms**. Script parse 355–460 ms per page load. WebView2 private memory 357–506 MB (2D card: 143 MB) | **GO**, with three conditions for Phase 5 |
| 0c — planner time-to-first-beat | Blocked: no provider key on this machine (`DEEPSEEK_API_KEY` absent) | — | runs before Phase 4 is called done |
| 0d — Part Library as isometric SVG (Anatomy's kit) | Run 2026-10-29 · claude-opus-5-5 (see below): the exact Babylon `model` messages drawn by a throwaway adapter over Anatomy's `iso-kit.mjs` + `lathe.mjs` (MIT, commit `1482e846`), under the scene page's CSP | build 9.9–16.2 ms per model (engine 11.6, DNA 9.9, pump 16.2); SVG 80–145 KB; 0 CSP violations, 0 JS errors | **GO with two conditions** |

**Spike 0b conditions (2026-10-29 · claude-opus-5-5)** — what Phase 5 must build in, because the numbers above say so:
1. **Pre-warm the 3D page and its shaders at startup**, like the 2D card pre-warm (DEC-025): build one hidden warm-up scene with the kit's material set, dispose it, keep the engine. Cold build misses the 300 ms budget by 20–40 %; warm builds are ~200 ms. The 8.6 MB `babylon.js` parse (~0.4 s) is paid in the same pre-warm, never on a question.
2. **Render on demand, not every frame.** Lesson scenes are still between beats; render on camera input, beat ops and animations only. That makes software rendering a non-issue for still frames: WARP (the fallback WebView2 takes when the GPU is blocklisted) holds 38 fps at native resolution even while rotating; only a forced SwiftShader (16–21 fps) falls below 30, and it is not the default fallback.
3. **Memory: ~360–500 MB of WebView2 private memory with a 3D scene** vs. DEC-020's ~150 MB budget for cards. Phase 5 records this as a DEC (one shared environment for 2D + 3D, 3D page created on first 3D lesson or at pre-warm — owner's call).

Not measured: an **integrated GPU** — this laptop exposes only the RTX 3060 (`Win32_VideoController` lists one adapter; `--force_low_power_gpu` still chose it). WARP stands in as the worst realistic case. Spike sources (host, page, runner, raw `results*.jsonl`) stayed in the session scratchpad and were not committed, as for 0a.

**Spike 0d results (2026-10-29 · claude-opus-5-5)** — [plan](../plans/2026-10-29-iso-svg-spike.md). Everything ran in the scratchpad; nothing in `src/` changed.
- **Downloaded (owner-approved)** from `raw.githubusercontent.com/wheresryan22/anatomy/1482e8469b0a9e0d8cc18ea2740a01423d29ae80/`:

  | File | Bytes | SHA-256 |
  |---|---|---|
  | `kit/iso-kit.mjs` | 35,175 | `6d3ecc725d30c3b0346da309cf61dfa5a65017c23620bbd4093009d4fb104018` |
  | `kit/lathe.mjs` | 48,028 | `a4e7e974b1a37e603ad2b76363b6f9815cc101dfff0ad277a5d4e8f1dc56fccb` |
  | `kit/iso.css` | 7,698 | `ef19bac5a42ba9be42b56b2c1cf7fc7e21d332a8060765e9ca1d987b8ab23924` |
  | `LICENSE` | 1,061 | `87c078ab9d90a3044f359e6b563d4e6afea7aa060897cf4f21618687b3856796` (MIT) |

- **Input:** `SceneMessages.Model` for each V1 model, exported by a throwaway console project. It's the same JSON the Babylon page receives, so both renderers draw the same numbers.
- **Shape mapping:**
  - Box → `prismOf`;
  - Cylinder and cone → `solidOf`;
  - Sphere → `sphereOf`;
  - Capsule → `solidOf` with smooth hemispherical ends;
  - Torus → 20 short `disc` segments around the ring. `lathe` takes a half-circle meridian, but that gives only an open band, so the segments are what's drawn.
- **Axis mapping, corrected from the plan:** the kit's projection matrix has determinant −1. Its world reads mirrored relative to a right-handed screen frame, so the map from Part Library space must also reflect: (x, y, z) → **(x, z, y)**, not the plan's (x, −z, y). With it, every model's pose matches its Babylon snapshot: the engine's valves and crank, the DNA's pair order and twist direction, the pump's ion sides. Nothing is mirrored.
- **Pictures (reviewed by me):**
  - all three models are recognisable and match `--snapshot-3d` in pose;
  - the DNA's overlaps are drawn in correct depth order throughout;
  - translucent glass and membrane read correctly through `opacity` on the finish class.
- **Defect found:** per-primitive painter's sorting puts the intake-valve stem mostly *behind* the cylinder head it passes through. Its centre lies below the head's top. This is the predicted interpenetration risk.
- **Found on the way:** this machine's registry maps `.js`/`.mjs` to `text/plain`, and a module script with that type is refused. It's worth checking that WebView2's virtual host serves `.mjs` as JavaScript before shipping (not verified).
- **GO conditions for a product step:**
  1. Fix painter's order for parts that pass through others: split long primitives into short segments for sorting, as the torus already is, or sort by the nearest point and check it with a rendering row.
  2. Confirm module MIME under `SetVirtualHostNameToFolderMapping`, or vendor the kit under a `.js` name.

  Vendoring then follows `VENDOR.md` (pinned hashes, the MIT licence shipped, LSU-7 coverage).
- **Shipped 2026-10-29 · claude-opus-5-5 as DEC-034** ([plan](../plans/2026-10-29-svg-fallback-v1.md)). Both conditions are met: the `.mjs` modules load in WebView2, and painter's order is fixed by cutting long parts into pieces and painting buried pieces before their container. LSU-12 (8) and LSU-16 (8) are green.

Effect on §2.3 (window model): cards are opaque windows; only screen marks are click-through, and they stay on the existing native ScreenOverlay. The transparent selective-click-through surface is no longer on the demo path.

## Risks

| Risk | Signal | Mitigation |
|---|---|---|
| Transparent WebView2 + selective click-through does not compose cleanly with capture exclusion | Phase 0a spike fails | fall back to two windows: native click-through overlay (marks) + opaque interactive lesson window (cards/3D) |
| Planner too slow for "real-time" | Phase 0c > 2.5 s first beat | smaller first beat, faster model for beat 1 + strong model for the rest, prefetch outline |
| Schema too narrow → model fights it | many degraded beats in evals | add ops by minor version, driven by eval failures, never speculatively |
| Part library coverage | students ask for unmodelled objects | schematic primitive composition, clearly tagged; library grows from eval misses |
| NuGet refused | DEC-020 = no | option D (hand-declared COM), +2–3 weeks |

## Acceptance criteria
- [x] DEC-020, DEC-021, DEC-022 decided (by owner delegation, 2026-10-28).
- [x] Phase 0 spikes recorded with measured numbers (0a GO; 0b deferred; 0c blocked on a key).
- [x] Phase 1 rows LS-1..LS-7 green offline (2026-10-28).
- [ ] (later phases: each phase's exit row in TEST-CHECKLIST.md)

## Test checklist
Phase 1 rows LS-1..LS-7 are defined in the Phase 1 plan and added to TEST-CHECKLIST.md by its last task.

## Rollback
Documentation only at this stage — delete this file. Phase 1 adds one new assembly and one new host
with no edits to existing code paths; rollback is removing both and their `.sln` entries.
