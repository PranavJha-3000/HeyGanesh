# Lesson Surface v1 (2D cards on screen) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** A real `ILessonCanvas`: validated lessons drawn on paper-style WebView2 card windows (DEC-020), marks on the student's own screen through the native ScreenOverlay, and an offline demo that plays the golden fixtures with a real Windows voice — no API key.

**Architecture:** New Experience assembly `src/Experience/HeyGanesh.LessonSurface/` (the only assembly allowed a PackageReference: `Microsoft.Web.WebView2` 1.0.4314.35). A dedicated STA UI thread runs a WinForms message loop; one shared WebView2 environment is created at startup (spike 0a: ~0.8 s cold, ~2.5 ms warm). Each declared surface gets one borderless, top-most, no-activate, capture-excluded `CardWindow`. The page (`Page/lesson.html|css|js`, shipped as content files, served from a virtual host, CSP `default-src 'self'`) receives validated beats as JSON messages and builds DOM/SVG with `textContent` only, acknowledging each beat. Plot points are computed in C# from the validated expression (FEATURE-007 §3.1 "compute, don't recall"), never by the page or the model.

**Tech Stack:** .NET 8 WinForms (in-box), WebView2 SDK, inline SVG, Windows fonts (Ink Free / Segoe Print, shipped with Windows). KaTeX and Rough.js are NOT downloaded in this phase: math is rendered by a small TeX-lite formatter in the page, strokes are jittered in-page — both swap for the vendored libraries later without touching the C# side.

**Spec:** FEATURE-007 §2.2–2.4, §3 items 1/3/7, §4 Phase 3; DEC-020/023; Phase 2 seams (`ILessonCanvas`).

## Global Constraints

- Core modules stay zero-NuGet. `HeyGanesh.LessonSurface` has exactly one PackageReference (`Microsoft.Web.WebView2` `1.0.4314.35`), audited.
- Every card window calls `CaptureExclusion.TryExcludeFromCapture` on its top-level HWND before it is first shown; failure is a hard error (overlay invariant).
- Cards never take focus: `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST`, `ShowWithoutActivation`.
- Physical pixels / PerMonitorV2: the host process sets PerMonitorV2 before creating the surface; card sizes are scaled by the card's DPI.
- The page: navigation outside the virtual host is cancelled, new windows refused, no dev tools or context menu by default, no host objects, CSP blocks network; every model string reaches the DOM through `textContent`/`createTextNode` only.
- Plot expressions are parsed by C# (`PlotExpression`), evaluated in C#; an unparsable expression is a validator issue (`value.invalid`) so the beat degrades before rendering.
- Verification follows the house pattern: offline rows in a host, exit codes from the built EXE, library then host built sequentially.

## Seam change (ruling)

`ILessonCanvas.ClearAsync(ct)` becomes `ResetAsync(IReadOnlyList<LessonSurface> surfaces, ct)`: the card surface needs each card's id and title before the first beat, and a beat alone does not carry them. `LessonPlayer` passes `plan.Surfaces`; Phase 2 fakes log `clear` unchanged.

## Review Focus

1. A beat that highlights an element written on a *different* card — highlight lands on the card that holds it.
2. Two screen marks in one lesson while the overlay is still showing the first — the first is hidden, the second shown, no `InvalidOperationException` escapes.
3. Model text containing HTML/script — rendered literally, no element created.
4. The page never acknowledging a beat (renderer wedged) — `ApplyBeatAsync` fails with a timeout instead of hanging the lesson.
5. Disposing the surface mid-lesson — windows close, the UI thread exits, pending applies fail with `ObjectDisposedException`/OCE, no hang.

## Tasks

### Task 1: `PlotExpression` + validator check — LessonSmoke `--assert-plot`
Pure C# in `HeyGanesh.VisualLessonEngine/PlotExpression.cs`: `static bool TryParse(string text, out PlotExpression? expr, out string? error)`, `double Evaluate(double x)`, `static IReadOnlyList<PlotPoint?> Sample(PlotOp op, int count = 121)` (null = gap for non-finite y). Grammar: numbers, `x`, `pi`, `e`, `+ - * / ^` (right-assoc), unary minus, parentheses, `sin cos tan exp ln log sqrt abs`, implicit multiplication (`6x`, `3x^2`, `2(x+1)`, `2sin(x)`); nesting depth ≤ 64; length ≤ 280.
Checks: `6*x/(3*x^2-1)` at 1 → 3; `3x^2` at 2 → 12; `-2^2` → −4; `2^3^2` → 512; `sin(pi/2)` → 1; `ln(e)` → 1; `x+`, `x**2`, `import os`, `foo(x)` → parse error; 200-deep parentheses → error, no stack overflow; `1/x` on [−1,1] sampled → a null gap at 0; validator: plot with `x+` → `value.invalid` on its beat; golden fixtures still Valid.

### Task 2: Assembly, UI thread, card window, bridge — LessonSurfaceSmoke `--assert-source`, `--assert-window`
`LessonSurface : ILessonCanvas, IAsyncDisposable` with `static Task<LessonSurface> CreateAsync(LessonSurfaceOptions? options = null, ScreenOverlayService? marks = null, CancellationToken ct = default)`, `Task HideAsync()`, `Task<CardSnapshot> DescribeCardAsync(string surfaceId)` (read-only diagnostics: element ids, texts, arrow/highlight/plot counts, window styles, display affinity). Seam change above. Bridge: host→page `{kind:"reset"|"beat"|"notice", seq, …}`; page→host `{kind:"ready"|"done"|"close"|"drag", seq?}`; each apply awaits its `done` with `LessonSurfaceOptions.ApplyTimeout` (default 3 s) → `TimeoutException`.
Checks: exactly one PackageReference (WebView2 1.0.4314.35); card window has the three ex-styles, display affinity `WDA_EXCLUDEFROMCAPTURE`, foreground never a card; navigation to `https://example.com/` cancelled; dispose mid-apply → no hang.

### Task 3: Page rendering — `--assert-render`, `--assert-security`
`lesson.js` renders write (heading/body/note + TeX-lite math), step (numbered), icon (10 built-in hand-drawn SVG glyphs), arrow (jittered SVG path between element boxes + label), highlight (marker underline + rough ring), plot (axes + polyline from host points, gaps honoured), clear; draw-on animation; card tab with title, close ×, drag handle.
Checks: each golden fixture played through `LessonPlayer` with an instant speaker → per card: every written element id present with exact text; arrow/highlight/plot counts match the plan; cross-card highlight lands on the owning card (Review Focus 1); `<img src=x onerror=…>` text → zero `img` elements, literal text (Review Focus 3); page `fetch` to an outside URL fails (CSP).

### Task 4: Screen marks — `--assert-marks`
`ScreenMarkOp` → `ScreenOverlayService.ShowAsync` (not awaited; previous mark hidden first; faults observed and logged into the snapshot). Checks: mark beat → overlay `IsShowing`; two marks back to back → no exception, second shown (Review Focus 2); renderer wedge → `TimeoutException` (Review Focus 4).

### Task 5: Demo — `--demo [chain-rule|tensor-2d|photosynthesis|all] [--voice sapi|timed]`
Plays fixtures on screen; `sapi` speaks through the Windows SAPI voice via COM late binding in the host only (no package); `timed` waits ~60 ms per character. Manual/visual — recorded as a human-observed row.

### Task 6: Docs + final review
TEST-CHECKLIST LS-11 (plot) and LSU-1..LSU-6, ARCHITECTURE (LessonSurface module), FLOW (canvas implementation), DECISIONS (seam change + TeX-lite/no-download ruling), FEATURE-007 status, HANDOVER, AGENTS commands; final whole-branch review and one fix pass.
