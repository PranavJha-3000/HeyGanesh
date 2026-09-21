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
- **Vision / Coordinate Detection** — UI-element and coordinate detection (DeepSeek V4.1 Flash).
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

### Vision / Coordinate Detection (implemented)
- **Assembly:** `src/HeyGanesh.Vision/`, `net8.0-windows10.0.22621.0`, zero NuGet (project-ref to ScreenCapture only).
- **Public surface:** `VisionService.LocateAsync(MonitorCapture, question)` → `VisionResult` (`Found`, description, normalized/image/source/screen boxes, `Confidence` + `RawConfidence`/`Consistency`, per-sample diagnostics); `IVisionClient` seam; `VisionOptions`. See [DEC-006](./DECISIONS.md#dec-006--2026-09-21--glm-53-flash) and [DEC-007](./DECISIONS.md#dec-007--2026-09-21--glm-53-flash--laguna-s-21).
- **Boundary:** consumes one `MonitorCapture` per call (no capturing, no multi-monitor fan-out); screen-space boxes reuse `PixelRect`, the same physical-px contract as `MonitorInfo.Bounds`. No rendering, no clicking, no proxy deployment — the direct-key path is temporary pending the API key security proxy module.

### Screen Overlay (contract inherited)
- Before drawing anything, call `CaptureExclusion.TryExcludeFromCapture(hwnd)` on the real top-level HWND so the capture module cannot photograph its own output. Affinity is per window and must be re-applied on every window recreation.

