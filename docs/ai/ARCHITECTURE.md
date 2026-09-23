# ARCHITECTURE — Module Map

> Skeleton only. One line per module, no implementation detail.
> These are the **canonical module names** — reference them verbatim in bug tickets,
> feature specs, DECISIONS entries and handovers.
> Implementation detail belongs in a per-module doc once one exists.

## Modules

- **Context Orchestrator** — top-level state machine driving session lifecycle.
- **Screen Overlay** — per-monitor click-through overlay window.
- **Global Hotkey Monitor** — system-wide hotkey registration and dispatch.
- **Screen Capture** — frame acquisition via `Windows.Graphics.Capture`.
- **Element Selection** — PRIMARY coordinate-detection path (UIA enumeration + Jev choice); Vision is the fallback.
- **Vision / Coordinate Detection** — UI-element and coordinate detection (DeepSeek V4.1 Flash) — FALLBACK to Element Selection.
- **Jev** — classification / intent-routing engine; placement TBD, sits before the Tutor Engine call.
- **Tutor Engine** — lesson generation.
- **Visual Lesson Engine** — structured diagram rendering.
- **Voice pipeline** — STT / TTS.
- **Notes persistence** — SQLite-backed notes store.
- **API key security proxy** — isolates LLM API keys from the rest of the application.

## Open placement questions

- **Jev** — its final position on the call path is undecided; currently specified only as
  "before the Tutor Engine call". Record the outcome in [DECISIONS.md](./DECISIONS.md) once decided.

## Data flow at a glance

_(Not yet defined — see [FLOW.md](./FLOW.md).)_

## Module details

### Screen Capture (implemented)
- **Assembly:** `src/HeyGanesh.ScreenCapture/`, `net8.0-windows10.0.22621.0`, zero NuGet.
- **Public surface:** `ScreenCaptureService` (`EnumerateMonitors`, `GetCursorMonitor`, `IsCaptureSupported`, `CaptureMonitorAsync`, `CaptureAllMonitorsAsync`, `Dispose`); `CaptureExclusion` (`TryExcludeFromCapture`, `TryIncludeInCapture`); `ScreenCaptureOptions` / `MonitorCapture`. See [DEC-004](./DECISIONS.md#dec-004--2026-09-21--glm-53-flash).
- **Boundary:** owns everything from HMONITOR to a JPEG `byte[]`. It does not render, does not move the cursor, does not touch the clipboard, and returns nothing for Vision to interpret. DPI awareness is deliberately the host's decision.

### Element Selection (implemented — the PRIMARY coordinate-detection path)
- **Assembly:** `src/HeyGanesh.ElementSelection/`, `net8.0-windows10.0.22621.0`, zero NuGet (project-ref to ScreenCapture only).
- **Public surface:** `ElementSelectionService.SelectAsync(string question, SelectionTarget? target = null, CancellationToken ct = default)` → `SelectionResult`. **The entry point is `SelectAsync`, not `LocateAsync`** — `LocateAsync` is Vision's; the two modules are siblings, not one API. `SelectionTarget`: `CursorWindow` (default — where the user is looking) / `ForegroundWindow`. `IJevClient` is the transport seam (`TypeSafeJevClient` today; bearer key read from `TYPESAFE_API_KEY` at call time, reported as presence/length only); `SelectionOptions` (`MaxCandidates` 120, `ModelId` `jev-1.13.0`, `BaseUrl` `api.typesafe.ai`, `IncludeNoneOption` true, `PerCallTimeout` 30 s). `CandidateEnumerator.Enumerate` → `CandidateSet`; `CandidateRanker.RankAndCap`; `UiaCandidate` / `TargetWindowInfo`. See [DEC-008](./DECISIONS.md#dec-008--2026-09-22--cline-agent-model-id-not-exposed-to-the-agent) and [DEC-009](./DECISIONS.md#dec-009--2026-09-22--cline-agent-model-id-not-exposed-to-the-agent).
- **Pipeline:** UIA enumeration of the target window (`Enumerate` — one cross-process cache-request pass over the control view) → interactive filter (nameless / offscreen / disabled dropped, and Text/Pane/Group/Custom excluded because dense text is Vision's job) → `RankAndCap` by lexical overlap with the question, capped at 120 and re-idded `c001..` → Jev answers the "choice" question over that manifest plus the synthetic `none` escape → the chosen candidate's **UIA `BoundingRectangle` is the coordinate answer**. Jev only ever selects among offered ids: it cannot emit coordinates or free text, and a choice id that was never offered is refused rather than mapped (hallucination guard).
- **Confidence:** `blended = 0.5 * JevConfidence + 0.5 * WinnerProbability`, tiered `>= 0.8` High / `>= 0.5` Medium / else Low (`ElementSelectionService.Mapping.cs`) — deliberately the same 0.8/0.5 boundaries as Vision so a consumer can compare the two paths. Threshold-free by contract: the raw signals (`JevConfidence`, `WinnerProbability`, `NoneProbability`, `PlausibleProbability`, the per-option distribution) are surfaced, never gated here.
- **Composition:** Element Selection is the **PRIMARY** path; **Vision is the FALLBACK**, invoked only on a failed result (`FailureReason != null`: no resolvable window, empty interactive tree, UIA access denied by UIPI, Jev returned no choice, or an unmapped choice id). `SelectionResult` mirrors `VisionResult`'s shape (Found, Description, `BoxOnScreen`/`CenterOnScreen` in global physical pixels, three-tier `Confidence`) but the two modules **must not reference each other** — `ConfidenceLevel` is intentionally duplicated, and unification is deferred to the Context Orchestrator (DEC-009).
- **Boundary:** guide-only — it resolves a rectangle and never injects input; the `SetFocus` slot in `UiaInterop.Interfaces.cs` exists for COM vtable alignment only and is never called. Geometry reuses `PixelRect` in global physical screen pixels; DPI awareness stays the host's decision. No capturing, no rendering. The direct `TYPESAFE_API_KEY` path is temporary, mirroring DEC-007.

### Vision / Coordinate Detection (implemented — FALLBACK to Element Selection)
- **Assembly:** `src/HeyGanesh.Vision/`, `net8.0-windows10.0.22621.0`, zero NuGet (project-ref to ScreenCapture only).
- **Public surface:** `VisionService.LocateAsync(MonitorCapture, question)` → `VisionResult` (`Found`, description, normalized/image/source/screen boxes, `Confidence` + `RawConfidence`/`Consistency`, per-sample diagnostics); `IVisionClient` seam; `VisionOptions`. See [DEC-006](./DECISIONS.md#dec-006--2026-09-22--laguna-s-21) and [DEC-007](./DECISIONS.md#dec-007--2026-09-22--laguna-s-21).
- **Boundary:** consumes one `MonitorCapture` per call (no capturing, no multi-monitor fan-out); screen-space boxes reuse `PixelRect`, the same physical-px contract as `MonitorInfo.Bounds`. No rendering, no clicking, no proxy deployment — the direct-key path is temporary pending the API key security proxy module.

### Screen Overlay (contract inherited)
- Before drawing anything, call `CaptureExclusion.TryExcludeFromCapture(hwnd)` on the real top-level HWND so the capture module cannot photograph its own output. Affinity is per window and must be re-applied on every window recreation.

