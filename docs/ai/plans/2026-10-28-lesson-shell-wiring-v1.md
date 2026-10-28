# Lesson Shell Wiring v1 Implementation Plan (FEATURE-007 Phase 4.5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Status:** 2026-10-28 · claude-opus-5-5 — approved by the owner ("Yes, build it": D1 and D2 as recommended) and BUILT the same day on `feat/lesson-shell-wiring-v1`. DEC-027 records it. Deviations from the plan: the App also references Providers.OpenAiCompat and gains `EnvironmentSecretStore.cs` (the adapters read keys through `ISecretStore`); `TeachAsync` is the one dispatch at both call sites; LT-6 also counts the process's visible windows during and after the turn.

**Goal:** Connect the finished lesson pieces to the product path. After a produced verdict, `--lesson` turns the question (plus the verdict as grounding) into a drawn, spoken lesson: VisualPlanner, then LessonPlayer over CardSurface, with Voice as the speaker. All of it is verified offline with a fake model, so when a key arrives the first real lesson works with no code change.

## Problem, restated

- Phases 1–4 each work on their own. LessonSmoke, LessonSurfaceSmoke and LessonPlannerSmoke each pass all their rows.
- Nothing in `HeyGanesh.App` calls any of them, so pressing Ctrl+Alt still ends in the Tutor's prose leg (which the shell keeps clientless, per DEC-015/017).
- Two existing rules shape how this is wired, and this plan keeps both:
  - **DEC-012: the shell has no `--fake` flags.** Test doubles live in verification hosts. A fake model therefore cannot be injected through the App EXE, so the code worth testing has to sit somewhere a host can reach.
  - **DEC-015/016: the shell is the only place that maps a verdict to teaching.** `GuidanceResult` must never reach a teaching module. The verdict-to-`ScreenGrounding` mapping therefore stays in the shell, next to `MapGuidanceToContext`.

## Design

### 1. New assembly `src/Experience/HeyGanesh.LessonSession/`

It references LessonPlanner, VisualLessonEngine and LessonSurface, and holds no shell policy. Its contents:

- **`LessonTurn.RunAsync(planner, canvasHost, speaker, PlanRequest, ct)`** returns a `LessonTurnReport`. The report has the PlanResult summary, the PlaybackReport, and an outcome of `Played`, `PlayedFallback`, `Rejected`, `ProviderError`, `AuthError` or `Cancelled`. The steps:
  1. plan;
  2. if Rejected, report it and open no cards;
  3. otherwise create the canvas, play, then dispose the canvas.
- **It never throws** for a documented fault: `ProviderException` subtypes and OCE map to outcomes. Unknown exceptions still propagate, so a defect is never dressed up as a verdict.
- **`DelegateLessonSpeaker : ILessonSpeaker`** wraps `Func<string, CancellationToken, Task>`. The shell passes `voice.SpeakAsync`, so this assembly never references Voice, and Voice stays text-only (DEC-016).
- **`ILessonCanvasHost`:** creates an `ILessonCanvas` that is also `IAsyncDisposable`. The real one wraps `CardSurface.CreateAsync`. The cards are created only after a plan exists, so a rejected plan costs no WebView2 start-up (~300 ms).

### 2. App Shell (`src/Experience/HeyGanesh.App/`)

- **`ShellHost.Args.cs`:**
  - `--lesson` flag;
  - header line `lesson : seam wired, planner <present|none>`;
  - usage text.
- **New `ShellHost.Lesson.cs`:**
  - `MapGuidanceToGrounding(GuidanceResult)` → `ScreenGrounding`:
    - when Found: Description, BoxOnScreen and Captures[0] JPEG;
    - on an honest no-match: no box, so every mark is refused by LS-12 and the planner gets the question alone.
    - The precondition is `!Failed`, enforced at the call sites exactly as the Tutor leg does it.
  - `TeachWithLessonAsync(...)`: maps the report outcome to the existing exit-code alphabet (0 / 3 / 12 / 13 / 4). No new codes.
  - `CreateLessonPlanner()` (see decision D1).
- **`ShellHost.Session.cs`:** at the two existing call sites, `options.Lesson ? TeachWithLessonAsync(...) : TeachAndSpeakAsync(...)`. Everything before the verdict stays byte-identical (SH-16 must still hold).
- **`HeyGanesh.App.csproj`:** add a sixth ProjectReference to LessonSession. This brings the WebView2 package in transitively (see D2).

### 3. Verification host `tools/LessonTurnSmoke/` (new; it is not product code, so fakes are allowed)

| Row | Check |
|---|---|
| LT-1 `--assert-source` | LessonSession: zero `<PackageReference`; refs = LessonPlanner + LessonSurface + VisualLessonEngine; no Voice/ContextOrchestrator reference; no env/file/network |
| LT-2 `--assert-turn` | fake model answers chain-rule → `Played`, every beat drawn, every sentence to the speaker in order (fake canvas) |
| LT-3 `--assert-turn-degraded` | mostly-bad answer → `PlayedFallback`, notice shown, text spoken |
| LT-4 `--assert-turn-rejected` | prose with no JSON on both attempts → `Rejected`, the canvas host is **never called** |
| LT-5 `--assert-turn-errors` | auth fault → `AuthError`; rate limit / 500 → `ProviderError`; cancel mid-play → `Cancelled`; in every case the canvas is disposed |
| LT-6 `--assert-turn-surface` | the same chain-rule turn on the **real** CardSurface with a timed speaker → `Played`, cards capture-excluded and never foreground, no card left after the turn (interactive session) |
| LT-7 `--demo-turn` | manual: fake model + real cards + SAPI voice, the whole turn end to end |
| SH-18 | `HeyGanesh.App.exe --help` lists `--lesson`; `--once --lesson --question "x"` (keys unset) → the unchanged `KEY ERROR (Element Selection)`, exit 12 |
| SH-19 | live-gated: keys + `--lesson` + chord → verdict, highlight, then cards drawn and spoken |

LT-6 also replaces the shell-level visual check the "no `--fake`" rule forbids.

## Decisions the owner must make

- **D1. Where does the shell's planner client come from?**
  - **Recommended:** `CreateLessonPlanner()` builds `VisualPlanner.FromRegistry(...)` from the environment when a provider key is present, and returns null otherwise (the leg then prints an honest skip, like the Tutor's).
    - This is what makes "the first lesson works the moment a key is added" true.
    - It is a departure from the Tutor stance, where DEC-017 left the flip deliberately untaken. It would be recorded as DEC-027.
  - **Alternative:** return null and keep a one-line flip, matching DEC-017 exactly.
    - Safer on paper, but it means another code change before the first demo.
- **D2. The App gains a transitive NuGet package (WebView2).**
  - SH-2/SH-10/SH-15 grep only the App csproj, so they still pass, but the csproj comment says "Dependency-free, like every module".
  - DEC-020/025 already accepted WebView2 for the card surface, and the core-module invariant (hotkey, capture, ES, Vision) is untouched.
  - **Recommended:** accept it, and update the csproj comment and SH-2's wording to "no *direct* PackageReference; WebView2 arrives only through LessonSurface (DEC-025)".
- **D3. Prerequisite risk.** LessonPlanner already references `src/Providers/HeyGanesh.Providers.Core`, which is **untracked**. Wiring the App to it means `main` can't build from a clean clone even for the App. Committing `src/Providers` + `src/Foundation` first (deferred earlier as "Not now") becomes more pressing; this plan does not do it.

## Alternatives rejected

- **`--fake-lesson` flag on the App EXE.** It breaks DEC-012, and product code would ship a test double.
- **Putting the lesson leg directly in the App with no new assembly.** No host can reach it, so the whole turn would be untested until a key exists. That is the exact gap SH-15/SH-17 already live with.
- **Making the Tutor call the planner.** It breaks DEC-015 (the Tutor references nothing) and couples prose teaching to drawing.
- **Mapping `GuidanceResult` inside LessonSession.** It breaks DEC-016 (the shell is the only mapper), and it would need a ContextOrchestrator reference.

## Execution path affected (FLOW.md)

Today:

    hotkey → Voice → orchestrator verdict → overlay → [Failed? exit 1] → TeachAndSpeakAsync

With `--lesson`, the last step becomes:

    TeachWithLessonAsync → MapGuidanceToGrounding → LessonTurn (plan → cards → play, with Voice speaking) → exit code

FLOW.md gets a new "Lesson leg" section.

## What could break, and how we'd know

| Risk | Detected by |
|---|---|
| The pre-verdict shell behaviour changes | SH-4/7/12b/16 re-run, byte-compared exits |
| WebView2 or its MSB3277 trap leaks into the App build | SH-1 with `-warnaserror`. The App may need the same `DropWebView2Wpf` target that LessonSurfaceSmoke carries |
| A card outlives the turn, or takes focus | LT-6 |
| A rejected plan still opens windows | LT-4 |
| Exit-code drift | LT-5 + SH-18 |
| CS2012 when the library and host build in parallel | sequential builds, as in every phase |

## Verification commands (on completion)

    dotnet build HeyGanesh.sln -c Debug -warnaserror        → 0 Warning(s) 0 Error(s)
    tools/LessonTurnSmoke/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.Tools.LessonTurnSmoke.exe --assert-source   → exit 0
      (and --assert-turn, --assert-turn-degraded, --assert-turn-rejected, --assert-turn-errors, --assert-turn-surface → exit 0 each)
    src/Experience/HeyGanesh.App/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.App.exe --help                        → lists --lesson, exit 0
    src/Experience/HeyGanesh.App/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.App.exe --once --lesson --question "x" → KEY ERROR (Element Selection), exit 12
    LessonSmoke 11 rows, LessonSurfaceSmoke 5, LessonPlannerSmoke 8 → all still exit 0

## Rollback

The work happens on branch `feat/lesson-shell-wiring-v1`, and ROLLBACK.md gets an entry before the first edit (more than 3 files are touched). To revert, delete the branch. main stays at 4c4be17 until merge.

## Tasks (after go-ahead)

- [x] 1. ROLLBACK.md entry; branch; LessonSession assembly + LT-1
- [x] 2. LessonTurn + DelegateLessonSpeaker + LT-2..LT-5 (fake canvas)
- [x] 3. LT-6 real surface + LT-7 demo
- [x] 4. Shell: `--lesson`, mapping, call sites, CreateLessonPlanner per D1, csproj per D2 + SH-18
- [x] 5. Docs: DEC-027, FLOW, ARCHITECTURE, TEST-CHECKLIST (LT-1..7, SH-18/19), AGENTS.md commands, FEATURE-007, HANDOVER
- [x] 6. Full re-run of every row above; final review; commit
