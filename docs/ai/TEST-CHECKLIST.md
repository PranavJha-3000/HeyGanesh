# TEST-CHECKLIST — Verification Checklist

> Manual and automated checks that feature work and bug fixes must satisfy before being
> called done. Feature specs and bug tickets link to the relevant items here.

## Standing checks

| # | Check | How to verify | Status |
|---|-------|---------------|--------|
| S-1 | Screen Capture library and verification host build with zero warnings | `dotnet build src\HeyGanesh.ScreenCapture -c Debug` and `dotnet build tools\ScreenCaptureSmoke -c Debug` | ✅ automated |
| S-2 | No package dependencies were sneaked in | inspect `src/HeyGanesh.ScreenCapture/*.csproj` and `tools/ScreenCaptureSmoke/*.csproj` for `PackageReference` | ✅ automated (none) |
| S-3 | Capture runs and the interop call succeeds on the developer machine | `ScreenCaptureSmoke` prints "capture is supported" and a frame with a non-zero `NonBlack` ratio | ✅ automated |

## Screen Capture feature checks

| # | Check | How to verify | Status |
|---|-------|---------------|--------|
| SC-1 | All monitors captured; count matches enumeration | `ScreenCaptureSmoke` asserts `captures.Count == monitors.Count` for every run | ✅ automated |
| SC-2 | Cursor's monitor comes first | `CaptureAllMonitorsAsync` result `[0].Monitor == service.GetCursorMonitor()` | ✅ automated |
| SC-3 | Output is a real JPEG, no upscale, aspect preserved | SOI/EOI magic bytes; `OutputWidth/Height <= SourceWidth/Height`; aspect within 0.02 | ✅ automated |
| SC-4 | Frame is not a blank/black capture | `NonBlackPixelRatio > 0.01` (>0.05 is warned, not failed) | ✅ automated |
| SC-5 | Settle frames are a preference, not a requirement | a single frame is accepted within `SettleTimeout`; a second frame is used if it arrives | ✅ automated |
| SC-6 | Latency is acceptable, not ~2.5 s per monitor | run prints "all monitors in N ms" (~0.7 s at 2560x1600) | ✅ automated |
| SC-7 | No leaks across repeated captures | `--repeats N` soak: small handle delta, flat managed heap | ✅ automated (25x: delta 1) |
| SC-8 | A captured JPEG decodes at the expected resolution | inspect a saved `monitor-1-*.jpg` dimensions vs the monitor's physical size | ✅ manual (host prints sizes) |

## Self-exclusion checks (cross-cutting into the Overlay)

| # | Check | How to verify | Status |
|---|-------|---------------|--------|
| EX-1 | The OS enforces the per-process scope | host calls `TryExcludeFromCapture` on a *foreign* HWND and asserts `ERROR_ACCESS_DENIED` (5) | ✅ automated (console HWND refused) |
| EX-2 | Our own window is accepted | host creates a topmost white probe window and asserts the call returns 0 | ✅ automated |
| EX-3 | Exclusion removes the window from the capture | A/B pixel diff: probe rect is 100% white when included, 100% of its pixels change when excluded, vs a 0% same-rect noise floor and a low control-region change | ✅ automated (measured: 100%/0%/0.6%) |
| EX-4 | Affinity can be cleared again | `TryIncludeInCapture` returns true after a successful exclude | ✅ automated |
| EX-5 | The future Overlay applies the contract | code review asserts the overlay HWND is passed to `CaptureExclusion.TryExcludeFromCapture` on creation and on recreation | 🚧 pending Overlay work

> Note on EX-3: the captured probe is a white rectangle, so the check is appearance-based. Hit-testing `WindowFromPoint` cannot be used for a `STATIC` window — it answers `HTTRANSPARENT` and reports the window beneath.

## Vision / Coordinate Detection checks

| # | Check | How to verify | Status |
|---|-------|---------------|--------|
| V-1 | Vision library and host build with zero warnings | `dotnet build src\HeyGanesh.Vision -c Debug` and `dotnet build tools\VisionSmoke -c Debug` | ✅ automated |
| V-2 | No package dependencies were sneaked in | inspect `src/HeyGanesh.Vision/*.csproj` and `tools/VisionSmoke/*.csproj` for `PackageReference` | ✅ automated (none) |
| V-3 | Live mode fails fast without a key | unset `DEEPSEEK_API_KEY` → `KEY ERROR`, exit **12**, no echo of the key | ✅ automated |
| V-4 | Offline parse→map→annotate path works without network or key | `--fake --image <prior capture>` → `found=True`, fixed box, `annotated.jpg` written, exit **0** | ✅ automated |
| V-5 | Coordinate mapping is exact end-to-end | fake box (240,120,360,220) on a 1280-wide JPEG → `normalized {0.240,0.120 0.120x0.100}` and matching source/global boxes | ✅ automated |
| V-6 | **Live visual hit-check: the box lands on the named element** | capture → ask 3 known targets (Start, clock, a title bar) → open `annotated.jpg`; record per-target px error | 🚧 needs `DEEPSEEK_API_KEY` + human eyes |
| V-7 | Not-found returns cleanly, no fabricated coords | ask for something absent → `found=false`, no box, exit 0 | 🚧 needs live call |
| V-8 | Consistency moves with sample spread | repeat one target with `--samples 4` → wider spread lowers `Consistency` | 🚧 needs live call |
