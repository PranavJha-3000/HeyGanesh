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

## Element Selection checks

| # | Check | How to verify | Status |
|---|-------|---------------|--------|
| ES-1 | Element Selection library builds with zero warnings | `dotnet build src\HeyGanesh.ElementSelection -c Debug` | ✅ automated — `Build succeeded. 0 Warning(s) 0 Error(s)` (verified 2026-09-23; was ⛔ `CS0103 BuildCandidateSet` until that method was restored, see note below) |
| ES-2 | Verification host builds with zero warnings | `dotnet build tools\ElementSelectionSmoke -c Debug` | ✅ automated — `Build succeeded. 0 Warning(s) 0 Error(s)` (verified 2026-09-23) |
| ES-3 | No package dependencies were sneaked in | inspect `src/HeyGanesh.ElementSelection/*.csproj` and `tools/ElementSelectionSmoke/*.csproj` for `<PackageReference` | ✅ automated (none — project-ref to ScreenCapture only) |
| ES-4 | Enumeration and the coordinate cross-check work with no key | `ElementSelectionSmoke --question "x" --list-only` → window title + `enumerated` counts printed, `coordinate cross-check: ... MATCH (same physical-px space)`, exit **0** | ✅ automated (verified 2026-09-23 on a live window: `'(145) YouTube - Brave'`, `control-view elements=44599  interactive after filter=7669`, `GetWindowRect=2582x1550+-11+-11  UIA root=2582x1550+-11+-11 -> MATCH`, exit 0; cursor-target variant too: `elements=401 filter=26`, MATCH, exit 0) |
| ES-5 | Offline fake path selects a real candidate | `ElementSelectionSmoke --question "Save" --fake` → `found` = `True`, `choice` = `c001`, `boxOnScreen` and `center` printed in global physical px, exit **0** | ✅ automated (verified 2026-09-23: `found=True`, `choice=c001`, `selected=Edit 'Enter keywords...' 1350x106+534+-1031`, box + `center (1209,-978)` printed, `confidence=High (jev=0.88, winner=0.91)`, exit 0 — note the negative coords: a monitor left of/above the primary is legitimate) |
| ES-6 | The `none` option is an honest not-found, not a failure | `ElementSelectionSmoke --question "Save" --fake --fake-none` → `found` = `False`, `choice` = `none`, no box printed, exit **0** (`Found=false` with no `FailureReason` is a successful answer) | ✅ automated (verified 2026-09-23: `found=False`, `choice=none`, `confidence=Low`, probabilities `none=0.62`, no box, no `FAILURE` line, exit 0). **Host fix 2026-09-23:** `PrintResult` used to print `choice` only inside `if (r.Found)`, so this path showed no `choice` line; it now prints whenever `r.Choice is not null` |
| ES-7 | Live mode fails fast without a key | unset `TYPESAFE_API_KEY` → `KEY ERROR`, exit **12**, key never echoed | ✅ automated (verified 2026-09-23: `KEY ERROR: TypeSafe API key not found. Set the TYPESAFE_API_KEY environment variable.`, exit 12, value never echoed; no network call made — the live Jev leg is still unverified, see ES note) |

> Note on ES-1/ES-2 — **resolved 2026-09-23**: `CandidateEnumerator.Walk.cs:45` called `BuildCandidateSet(...)` with no definition anywhere in `src/HeyGanesh.ElementSelection/`, which made the whole module fail to compile (`0 warnings, 1 error`: `CandidateEnumerator.Walk.cs(45,20): error CS0103: The name 'BuildCandidateSet' does not exist in the current context`). The two empty files `CandidateEnumerator.Elements.cs` (65 bytes) and `CandidateEnumerator.Reads.cs` (63 bytes) — each holding only two `using` directives and no type body — were the homes of that missing work, and the body was reconstructed there (filter + assembly in `Elements.cs`, cached-property readers + position hint in `Reads.cs`). The full diagnosis, the live failure it exposed (`COMException 0x80040201 UIA_E_ELEMENTNOTAVAILABLE` on a stale node aborting the walk) and the verification run are recorded in [HANDOVER.md](./HANDOVER.md) entries for 2026-09-23. ES-4..ES-7 below are the checks that were re-run to prove it.
>
> Still unverified for Element Selection: the **live Jev leg** (needs `TYPESAFE_API_KEY` — ES-5/ES-6 ran with the offline fake client, so ranking → HTTP → mapping has only been exercised up to the transport), `--annotate`/`annotated.jpg`, whether the Jev *model* picks the right element for a real question (no analogue of V-6's hit-check yet), and whether the 120 cap + interactive filter keep enough recall on dense windows.
