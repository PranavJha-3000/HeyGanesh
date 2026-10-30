# PLATFORM - HeyGanesh at a Glance

> Single-page overview. Every statement is derived from [ARCHITECTURE.md](./ARCHITECTURE.md),
> [FLOW.md](./FLOW.md), [DECISIONS.md](./DECISIONS.md), [TEST-CHECKLIST.md](./TEST-CHECKLIST.md)
> and the source under `src/`. Where the docs and the code disagree, the code wins and the
> conflict is listed in [Discrepancies](#8-discrepancies), not fixed here.

## 1. Purpose

HeyGanesh is a Windows desktop tutor. You ask a question, it resolves what you are looking at on
screen, points at it, and teaches you the concept behind it.

- **Academic / technical teaching.** The question is the teaching task and is answerable with no screen data at all (`TutorRequest.Question` is required; `TutorContext` is optional grounding).
- **Guide-only.** It resolves coordinates and explains. It never acts for the user: no `SendInput`, no `SetForegroundWindow`, no UIA writes, no typing. The user always acts. The one `SetFocus` slot in `UiaInterop.Interfaces.cs` exists only to keep the COM vtable aligned and is never called; the one `SetCursorPos` declaration in Screen Capture's `NativeMethods.cs` is documented as "for verification hosts only; this library never moves the cursor", and no product code calls it (only the Screen Capture smoke host does, to park the pointer).

## 2. Core user flow

```
Ctrl+Alt held -> GlobalHotkeyMonitor.Pressed -> Voice: ListenOnceAsync
  (mic -> qwen-audio-agent Gateway; release ends the utterance; only `transcript.final role=user`
   becomes text) -> Question = committed transcript
        |
        v
  Context Orchestrator - one session per trigger:
     1. Screen Capture: all monitors, cursor's display first, one frame per session
     2. Element Selection (PRIMARY): UIA tree -> filter -> rank/cap 120 -> Jev choice -> UIA rect
     3. Vision (FALLBACK): only if step 2 returned a FAILED result
        |
        v
  GuidanceResult (Found, Description, box/centre in global physical px, tier, raw signals)
        |
        v
  Screen Overlay ShowAsync(guidance): draws nothing for no-match/failure; else one click-through
  window per touched monitor, bounded (~3 s) -> OverlayShowResult (never a position)
        |
        v
  Tutor Engine (question + optional bounded screen context) -> explanation text
        |
        v
  Voice / Fish TTS speaks it verbatim (unless --no-speak); shell reports the turn
```
The overlay draws nothing for an honest no-match or a failure: a pointer for "not found" would
teach the wrong meaning. The teaching leg runs only after a produced non-`Failed` verdict; a
no-match maps to `TutorContext.Empty`, so the general concept is still taught.

## 3. Stack

- **C# / .NET 8** - every assembly targets `net8.0-windows10.0.22621.0` except the Global Hotkey
  Monitor, which targets `net8.0-windows` (the shell that consumes it is on the higher version).
- **Zero NuGet in every core module.** Direct Win32 P/Invoke (user32, gdi32, winmm) plus Windows
  SDK WinRT interop (`Windows.Graphics.Capture`). The Tutor Engine has zero project references too.
- **WinUI 3 / Windows App SDK** - deferred, not present. DEC-011 and DEC-012 both defer it to a
  future packaged shell; the overlay is the UI today and it is Win32.
- **External runtimes are services, never dependencies:** the qwen-audio-agent Gateway (operator-run),
  Fish Audio (TTS), DeepSeek (Vision + Tutor), TypeSafe (Jev).

## 4. Component map

| Component | Assembly | Role |
|---|---|---|
| Global Hotkey Monitor | `src/Platform/HeyGanesh.GlobalHotkeyMonitor/` | System-wide listen-only Ctrl+Alt push-to-talk chord; nothing is swallowed. |
| Screen Capture | `src/Platform/HeyGanesh.ScreenCapture/` | `Windows.Graphics.Capture` frames per monitor (JPEG, <=1280 px) + the `CaptureExclusion` contract. |
| Element Selection (PRIMARY) | `src/Perception/HeyGanesh.ElementSelection/` | UIA enumeration of the target window -> interactive filter -> lexical rank, cap 120 (`c001..`) -> Jev choice -> the chosen candidate's UIA `BoundingRectangle` is the answer. Entry point `SelectAsync`, not `LocateAsync`. |
| Vision / Coordinate Detection (FALLBACK) | `src/Perception/HeyGanesh.Vision/` | DeepSeek over the captured frame; the model returns a box on a 0..1000 integer grid, back-mapped to physical pixels. Entry point `LocateAsync`. |
| Context Orchestrator | `src/Intelligence/HeyGanesh.ContextOrchestrator/` | One session per trigger; composes capture -> ES -> (fallback) Vision; normalises both verdicts into `GuidanceResult`. The only assembly that sees both legs. |
| Screen Overlay | `src/Platform/HeyGanesh.ScreenOverlay/` | Per-monitor click-through layered window; draws the box and the centre glyph, capture-excluded, bounded duration. |
| Voice pipeline | `src/Experience/HeyGanesh.Voice/` | Speech in (Qwen Gateway, 16 kHz mono PCM) and speech out (Fish TTS, wav/44.1 kHz). Answers nothing. |
| Tutor Engine | `src/Intelligence/HeyGanesh.TutorEngine/` | The only component allowed to answer the user: question (+ bounded untrusted grounding) -> explanation. |
| App Shell | `src/Experience/HeyGanesh.App/` | Process host: PerMonitorV2 DPI awareness, the service graph, and the wiring. Draws no window of its own. |
| CursorTarget abstraction | (none) | **Planned.** No such type exists in the tree today. |

**The sibling rule:** Element Selection and Vision must never reference each other. They are
separate assemblies with separate result types (`SelectionResult` / `VisionResult`), a deliberately
duplicated `ConfidenceLevel` with the same 0.8 / 0.5 tier boundaries, and separate entry points.
The ES-to-Vision fallback is decided by the Context Orchestrator on a *failed* primary result, never
by either module; the finer confidence-tier gate stays a future Jev component.

## 5. Status

| Component | Status | Evidence / gap |
|---|---|---|
| Global Hotkey Monitor | DONE-VERIFIED | Builds 0 warnings; hook install/teardown covered by the shell's own row (SH-12a). `HotkeySmoke --seconds 4` could not self-terminate in a non-interactive shell, so that unattended row is unrun. |
| Screen Capture | DONE-VERIFIED | S-1..SC-8 automated, including the blank-frame and leak checks. |
| Element Selection | DONE-UNVERIFIED | Offline rows ES-1..ES-7 green (fake client). **The live Jev leg is NOT VERIFIED** - it needs `TYPESAFE_API_KEY`; whether the Jev model picks the right element is untested. |
| Vision / Coordinate Detection | DONE-UNVERIFIED | Offline parse/map/annotate rows green. **Live calls are NOT VERIFIED** - V-6 (box lands on the named element), V-7 (honest not-found) and V-8 (consistency) all need `DEEPSEEK_API_KEY` plus a human. |
| Context Orchestrator | DONE-VERIFIED | CO-1..CO-10 automated: primary answers, `none` never falls back, a failed primary triggers exactly one Vision call, overlap refused. A live session with both keys is not run; multi-monitor ordering is untested (needs a second display). |
| Screen Overlay | DONE-VERIFIED | SO-1..SO-9 automated: no-match and failure draw nothing, click-through + no-activate + capture exclusion checked from the OS side, 25x soak leaks nothing. Multi-monitor placement is single-display-verified only. |
| Voice pipeline | DONE-UNVERIFIED | VO-1..VO-10 green offline (fakes + recorded frames). **Every live path is NOT VERIFIED**: a real Gateway listen, real microphone capture and a real Fish synthesis have not been run. |
| Tutor Engine | DONE-UNVERIFIED | TU-1..TU-11 green offline, including the provider's wire contract over a loopback stub. **TU-12's live half is NOT VERIFIED** - it needs `DEEPSEEK_API_KEY`. |
| App Shell | DONE-UNVERIFIED | SH-1..SH-16 automated offline; the shell carries no `--fake` flags, so coverage stops at the real key gates. SH-9, SH-13 and SH-14 are human-gated; SH-17 is blocked on the one-line `CreateTutorClient()` flip. |
| Teaching leg (shell -> Tutor -> speaker) | DONE-UNVERIFIED | Wired end to end and mapped (DEC-016); since DEC-047 the shell creates the DeepSeek Tutor whenever the key ring has the deepseek key. A real spoken answer (SH-17) needs the keys. |
| Jev intent/routing component | PLANNED | No `src/HeyGanesh.Jev/` assembly exists. Today Jev is only the Element Selection choice model behind `IJevClient`. The confidence-gate and the intent classifier are both unbuilt. |
| Visual Lesson Engine | PLANNED | No `src/Intelligence/HeyGanesh.VisualLessonEngine/` assembly exists. |
| Notes persistence | PLANNED | No `src/HeyGanesh.Notes/` assembly exists; no lesson is stored anywhere today. |
| API key security proxy | BUILT (DEC-045/DEC-046) | `HeyGanesh.Security.KeyRing`: Credential Manager first, environment fallback, key commands in the App Shell; Element Selection, Vision, Voice (Fish) and the lesson/router clients read through it. The Tutor still reads its environment variable until step 9. |
| WinUI 3 / packaged shell | PLANNED | Deferred twice (DEC-011, DEC-012). |
| CursorTarget abstraction | PLANNED | No such type exists in the tree. |

Live provider checks - DeepSeek (Vision), DeepSeek (Tutor), TypeSafe (Jev), Fish Audio (TTS) and the
qwen-audio-agent Gateway - are **NOT VERIFIED** across the board: no real call has been made from
this tree. What is verified offline is the wire shape we send, never the provider's acceptance.

## 6. Roadmap

Ordered as they stand in the docs; all are named in the architecture or the feature files.

1. **Overlay + Bezier pointing** - extend the highlight from a static box to a curved pointer leading the eye to the resolved centre.
2. **Voice** - the push-to-talk seam (Qwen Gateway in, Fish TTS out) and the shell's release-driven trigger; `--question` retires when the shell is fully voice-driven.
3. **Tutor Engine** - built and wired; what remains is the one-line shell flip to the live provider plus the live-gated rows.
4. **Visual Lesson Engine** - structured JSON lesson documents rendered as native widgets.
5. **Interactive widget runtime** - the runtime that hosts those widgets and lets the learner interact with a lesson.
6. **SQLite notes** - a notes store persisting lessons for later review and for session context.
7. **API key proxy** - one place that holds provider credentials, replacing the per-module environment-variable reads.
8. **Context Orchestrator** - built; what remains is its Jev confidence-gate and the wider session policy it will host.
9. **Long-term memory architecture** - *under consideration*: no design, no decision, no assembly. The Tutor Engine is deliberately stateless today ("no memory, no learner model, no multi-turn state"), so any learner model is a future decision, not an implied feature.

## 7. Where the detail lives

| File | What it holds |
|---|---|
| [ARCHITECTURE.md](./ARCHITECTURE.md) | Canonical module names and per-module public surface, pipeline and boundary. |
| [FLOW.md](./FLOW.md) | The end-to-end interaction flow, leg by leg, with the rules that hold on each path. |
| [DECISIONS.md](./DECISIONS.md) | Every non-obvious choice, dated and model-versioned, with the alternatives rejected. |
| [TEST-CHECKLIST.md](./TEST-CHECKLIST.md) | The verification rows behind the status table above, with the exact commands. |
| [HANDOVER.md](./HANDOVER.md) | The session log: what landed, what is unfinished, what is fragile. |
| [ROLLBACK.md](./ROLLBACK.md) | Known-good states and how to return to them. |
| [TEMPLATES.md](./TEMPLATES.md) | The entry formats for every file under `docs/ai/`. |
| [features/](./features/) | Per-feature scope, acceptance criteria and status (FEATURE-001..006). |
| [bugs/](./bugs/) | Bug traces (empty so far). |
| `AGENTS.md` (repo root) | The onboarding summary and the build/verify commands. |

## 8. Discrepancies

Code wins; nothing here was fixed by this document.

1. **`AGENTS.md` lists four assemblies that do not exist**: `src/HeyGanesh.Jev/`, `src/HeyGanesh.VisualLessonEngine/`, `src/HeyGanesh.Notes/`, `src/HeyGanesh.SecurityProxy/`. The `src/` tree holds only App, ContextOrchestrator, ElementSelection, GlobalHotkeyMonitor, ScreenCapture, ScreenOverlay, TutorEngine, Vision and Voice.
2. **`AGENTS.md` names the Vision model "DeepSeek V4.1 Flash"; the code sends `deepseek-flash`** (`VisionOptions.ModelId`, `DeepSeekTutorOptions`). The provider's own naming is not something the code asserts.
3. **`AGENTS.md` duplicates the Tutor Engine row** with two different descriptions (one "interactive lesson generation and teaching logic", one the accurate academic/technical layer), and describes Visual Lesson Engine and Jev as if both were implemented subsystems.
4. **`AGENTS.md`'s pipeline line places "Jev" between Element Selection and Tutor Engine**, which is true of the *Jev choice model* used by Element Selection but not of the separate intent / confidence-gate component the same table lists.
5. **The Visual Lesson Engine and Notes persistence are named as modules in `ARCHITECTURE.md`** while having no assembly and no code.
6. **`VisionResult.FailureReason`'s XML doc says a valid not-found carries none, but `VisionService.Reconcile` sets one** for "the model inspected the image but could not locate the requested element". The orchestrator carries the verdict verbatim rather than re-classifying it (CO-6); the internal mismatch is untouched.
7. **`CONSTRAINTS.md` is empty** - user-owned and deliberately unpopulated, so the guide-only rule this document states is enforced in code (no actuation anywhere in the tree) and in the decisions, not by a recorded constraint.
8. **This document's "WinUI 3" and "teaching window" wording comes from the product vision, not from the code**: no WinUI 3 code exists (it is deferred to a future packaged shell) and there is no teaching window today - the lesson is printed to the console and spoken, and the overlay is the only window the product draws.
9. **"Saved lesson note by subject" is a roadmap item, not current behaviour**: nothing is stored today, and "by subject" is a product decision that has not been made (no notes module, no subject field anywhere in the tree).

