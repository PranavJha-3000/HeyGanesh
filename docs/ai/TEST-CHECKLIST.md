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
| EX-5 | The future Overlay applies the contract | code review asserts the overlay HWND is passed to `CaptureExclusion.TryExcludeFromCapture` on creation and on recreation | ✅ automated (verified 2026-09-24: `OverlaySmoke --assert-clickthrough` checks `WDA_EXCLUDEFROMCAPTURE` on every live HWND from the OS side, exit 0 — SO-7) |

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

## Context Orchestrator checks

| # | Check | How to verify | Status |
|---|-------|---------------|--------|
| CO-1 | Orchestrator library and its verification host build with zero warnings | `dotnet build src\HeyGanesh.ContextOrchestrator -c Debug` and `dotnet build tools\ContextOrchestratorSmoke -c Debug` | ✅ automated — `Build succeeded. 0 Warning(s) 0 Error(s)` for both (verified 2026-09-23) |
| CO-2 | No package dependencies were sneaked in | inspect `src/HeyGanesh.ContextOrchestrator/*.csproj` and `tools/ContextOrchestratorSmoke/*.csproj` for `<PackageReference` | ✅ automated (none — orchestrator has 3 project-refs: ScreenCapture, ElementSelection, Vision; deliberately no GlobalHotkeyMonitor reference) |
| CO-3 | The primary leg answers and Vision is never called | `ContextOrchestratorSmoke --question "Save" --fake --max-candidates 40` → `source: ElementSelection`, box + center printed in global physical px, `vision calls: 0`, `PASS primary leg answered and Vision was never called`, exit **0** | ✅ automated (verified 2026-09-23: `choice=c001` `'Restore'`, `boxOnScreen 70x51+2423+0`, `center (2458,25)`, `confidence High`, `phase Resolved`, exit 0) |
| CO-4 | The honest no-match (`Choice == "none"`) must NOT fall back | `--question "Save" --fake-none` → `source: ElementSelection`, `found: False`, `choice: none`, **no** FAILURE line, `vision calls: 0`, exit **0** | ✅ automated (verified: `found=False choice=none confidence=Low`, `none=0.62 plausible=0.12`, 0 Vision calls, exit 0 — the load-bearing composition row) |
| CO-5 | A genuinely failed primary leg triggers exactly one Vision fallback | `--question "Save" --fake-unmapped` → `primary why:` carries the ES failure, `source: Vision`, `vision calls: 1`, `found: True` with a screen-space box, exit **0** | ✅ automated (verified: primary `Jev chose option id 'c999' which is not in the offered manifest (40 candidates) -- refusing to fabricate coordinates.`, then `source=Vision found=True box 306x160+614+192 center (767,272)`, tokens summed 138/44, exit 0) |
| CO-6 | A fallback not-found is carried verbatim, and no box is invented | `--question "Save" --fake-vision-none` → `source: Vision`, `found: False`, Failed with Vision's own reason, no box line, `vision calls: 1`, exit **0** | ✅ automated (verified: `FAILURE: the model inspected the image but could not locate the requested element.`, `confidence Low`, no box, exit 0) — see the note below |
| CO-7 | Cancellation propagates and never reaches the fallback | `--question "Save" --fake --cancel-ms 150` → `CANCELLED`, `phase: Idle`, `vision calls: 0`, exit **13** | ✅ automated (verified: fake ES delay auto-raised to 450 ms, token fired at 150 ms, no verdict, exit 13) |
| CO-8 | Provider/HTTP failures propagate instead of being swallowed into a verdict | `--question "Save" --fake-error` → `API ERROR (Element Selection, propagated not swallowed)`, exit **3** | ✅ automated (verified: fake threw HTTP 500 at the client seam; the orchestrator re-threw it, no GuidanceResult printed) |
| CO-9 | Live mode fails fast with no key, and never echoes it | run with both keys unset → `KEY ERROR (Element Selection)`, exit **12** | ✅ automated (verified: `TypeSafe API key not found...`, exit 12, no value echoed; header prints presence only). **Live happy path (both keys) is NOT verified** — the ES live Jev leg is still unverified, see the ES note above |
| CO-10 | One hotkey trigger = one session (overlap refused, not queued) | `--question "Save" --fake --burst` → second trigger prints `REFUSED -- A guidance session is already in flight...`, `PASS one hotkey trigger maps to one session`, exit **0** | ✅ automated (verified: session 1 returned a primary verdict, second call threw `InvalidOperationException`, `vision calls: 0`, exit 0) |
| CO-11 | Frames are captured once per session and retained for the overlay/diagnostics | any offline run → `frames: 1 retained (first = <monitor>)`; `--no-retain-captures` → `frames: 0 retained` with a `PASS` line | ✅ automated for the retention paths (verified 2026-09-23, exit 0 both ways). 🚧 **cursor-monitor-first ordering is NOT exercised** — this machine reports a single display, so ordering across monitors is untested |

> Note on CO-6 — the fallback leg currently reports a **model-side not-found as a failure**: `VisionService.Reconcile` sets `FailureReason = "the model inspected the image but could not locate the requested element."`, so `VisionResult.Failed == true` even though the module's own `FailureReason` XML doc says a valid "element not found" answer should have no failure reason. The orchestrator carries the leg's verdict verbatim (Found = false, Failed = true, Low) instead of re-classifying it, because re-classifying would be inventing policy the leg owns. Recorded in [HANDOVER.md](./HANDOVER.md) as an open item for the Vision module — **not** fixed in this change.
>
> Not yet covered for the orchestrator: multi-monitor ordering (CO-11, needs a second display), a live end-to-end session with both keys, and any downstream consumption — the Screen Overlay now consumes `GuidanceResult`, see the overlay rows below.

## Screen Overlay checks

| # | Check | How to verify | Status |
|---|-------|---------------|--------|
| SO-1 | Overlay library and verification host build with zero warnings | `dotnet build src\HeyGanesh.ScreenOverlay -c Debug` and `dotnet build tools\OverlaySmoke -c Debug` | ✅ automated (verified 2026-09-24, 0 warnings, 0 errors) |
| SO-2 | No package dependencies were sneaked in | `Select-String` both new csproj for `<PackageReference` | ✅ automated (verified 2026-09-24: no matches — WinUI 3 stays a shell direction per DEC-011) |
| SO-3 | A found verdict shows for the full duration, then removes itself | `OverlaySmoke --found --duration-ms 1500` → `displayed=True`, `windows=1`, `ShownFor≈duration`, no window left, exit **0** | ✅ automated (verified 2026-09-24: `displayed True`, 1 window, ShownFor ~1.44 s, exit 0) |
| SO-4 | The honest no-match draws nothing and creates no window | `OverlaySmoke --no-match` → `displayed=False`, `reason=no-match`, `windows=0`, exit **0** | ✅ automated (verified 2026-09-24, exit 0 — the overlay analogue of CO-4) |
| SO-5 | A failed verdict (Found=false WITH FailureReason — the CO-6 shape) classifies as `failure`, draws nothing | `OverlaySmoke --failed` → `displayed=False`, `reason=failure`, `windows=0`, exit **0** | ✅ automated (verified 2026-09-24, exit 0) |
| SO-6 | Cancellation removes the overlay cleanly | `OverlaySmoke --cancel-ms 300 --duration-ms 5000` → `displayed=False`, `reason=cancelled`, no window left, exit **13** | ✅ automated (verified 2026-09-24, `PASS`, exit 13) |
| SO-7 | Click-through + no-activate + capture-excluded, from the OS's side | `OverlaySmoke --assert-clickthrough` → every live HWND has the full extended-style bits, `WM_NCHITTEST → HTTRANSPARENT`, affinity `WDA_EXCLUDEFROMCAPTURE`, never foreground; removed on hide, exit **0** | ✅ automated (verified 2026-09-24, exit 0 — flips EX-5 ✅) |
| SO-8 | One show at a time (overlap refused, not queued) | `OverlaySmoke --burst` → second `ShowAsync` throws `InvalidOperationException`, first still completes, exit **0** | ✅ automated (verified 2026-09-24: `REFUSED`, first show `Displayed`, exit 0) |
| SO-9 | No leaks across repeated shows | `OverlaySmoke --repeats 25` → GDI and handle deltas within tolerance, no window left, exit **0** | ✅ automated (verified 2026-09-24: GDI delta 0, handles delta ≤ 8, exit 0) |

> Scope notes for the overlay rows: window-ownership assertions are PID-filtered (`FindOwnWindows`) so a lingering window from a crashed concurrent run cannot fail an unrelated run; a foreign window is logged, never asserted. `Displayed=true` requires the full requested duration — an early end reports `displayed=False` even when a window appeared briefly. Multi-monitor placement math is coded but single-display-verified only (this machine: one 2560x1600 display); the smoke box touches exactly one monitor so `WindowCount == 1` stays assertable on any topology.
>
> Not yet covered for the overlay: off-screen boxes and zero-size boxes on real hardware (coded `off-screen` / `no-box` paths, unexercised), the `RequirePerMonitorDpi` refusal path (hosts are always aware in practice), multi-monitor clipping, and any Jev/Tutor-gated display policy.

## App Shell checks

| # | Check | How to verify | Status |
|---|-------|---------------|--------|
| SH-1 | App Shell builds with zero warnings | `dotnet build src\HeyGanesh.App -c Debug` | ✅ automated (verified 2026-09-29: `Build succeeded. 0 Warning(s) 0 Error(s)`) |
| SH-2 | No package dependencies were sneaked in | `Select-String src\HeyGanesh.App\*.csproj -Pattern '<PackageReference'` | ✅ automated (verified 2026-09-29: no matches — WinUI 3 deferred again, see DEC-012) |
| SH-3 | `--help` prints the flags and the exit-code contract, and exits 0 | `HeyGanesh.App.exe --help` | ✅ automated (verified 2026-09-29: usage plus `Exit codes: 0 … 13 cancelled.`, exit **0**) |
| SH-4 | The shell composes the pipeline, drives it, and fails fast on a missing primary key | `--once --question "where is the Save button"` with both keys unset → header (key presence only, `per-monitor-v2 aware`), a real capture, then `KEY ERROR (Element Selection)`, exit **12** | ✅ automated (verified 2026-09-29, exit 12, no key value echoed). **Separately observed the same day:** with the cursor over a window whose UIA tree yielded no interactive candidates, ES returned a failed result, Vision ran exactly once and reported `KEY ERROR (Vision)`, exit 12 — the fallback leg driven end-to-end through the shell |
| SH-5 | Hooked mode is the default: installs the hook, stays alive, releases it on stop | `--question "x" --seconds 6` → `hook live: IsRunning=True (installed), chord state=Idle (nothing held)`, health block, `still running: False`, exit **0** | ✅ automated (verified 2026-09-29, exit 0; `hook callbacks: 0` because no chord was pressed during that run) |
| SH-6 | ~~A missing question refuses to start rather than defaulting~~ **SUPERSEDED 2026-09-30 by SH-12c (DEC-014):** the question now comes from the microphone, so a no-flag run installs the hook instead of exiting 2. Kept for the record: the old expectation was `no --question was given, and the Voice pipeline that would supply one does not exist yet.` + exit **2** (verified 2026-09-29, exit 2) | `HeyGanesh.App.exe` with no flags now hooks (see SH-12a); exit 2 remains reserved for "the shell could not start" | ⚠️ superseded |
| SH-7 | Cancellation propagates and no verdict is fabricated | `--once --cancel-ms N --question "x"` for N = 1, 10, 60, 300 → `CANCELLED` + `phase: Idle`, exit **13** | ✅ automated (verified 2026-09-29: all four exit 13). Note the observed abort time: 13 ms for N=1 but ~570–700 ms for N=10/60/300 — the token is only *seen* at an await boundary, and UIA enumeration is not cancellable mid-flight |
| SH-8 | `--no-dpi` is accepted and the header stops claiming awareness | `--once --no-dpi --question "x"` → `dpi: awareness not claimed (--no-dpi) - the overlay must refuse to draw` | ✅ automated for the flag and the header line (verified 2026-09-29, exit 12 at the key gate). ⚠️ **The overlay's `not-per-monitor-dpi-aware` refusal is still not exercised** — reaching it needs a verdict, which needs a key |
| SH-9 | A real chord produces a real verdict, a real highlight, and leaves no window behind; a second press during one is refused | run with both keys set, hold Ctrl+Alt, watch the highlight, press twice in quick succession for `REFUSED`, then confirm no overlay window survives | 🚧 needs both keys and a human hand |
| SH-10 | The shell now takes the Voice project-ref and still has zero NuGet | `dotnet build src\HeyGanesh.App -c Debug`; `Select-String src\HeyGanesh.App\*.csproj -Pattern '<PackageReference'` (expect no matches); the csproj lists 4 `ProjectReference`s | ✅ automated (verified 2026-09-30: `Build succeeded. 0 Warning(s) 0 Error(s)`, 0 `PackageReference`, refs = GlobalHotkeyMonitor + Voice + ContextOrchestrator + ScreenOverlay) |
| SH-11 | `--help` documents the voice trigger, the legacy flag and the new exit meanings | `HeyGanesh.App.exe --help` → `hold Ctrl+Alt to speak`, `--question LEGACY / verification`, exit-code line mentioning "no transcript was produced", exit **0** | ✅ automated (verified 2026-09-30, exit 0) |
| SH-12a | Hooked mode needs no flag: the microphone is the question source | `HeyGanesh.App.exe --seconds 4` → `question: <voice: hold Ctrl+Alt to speak>`, `hook live: IsRunning=True`, `Hold Ctrl+Alt to speak, release to submit`, health block, `still running: False`, exit **0** | ✅ automated (verified 2026-09-30, exit 0, 25 hook callbacks, max latency 0.91 ms) |
| SH-12b | The legacy `--once --question` path is byte-for-byte unchanged | `HeyGanesh.App.exe --once --question "where is the Save button"` → same header shape + `KEY ERROR (Element Selection)`, exit **12** with keys unset | ✅ automated (verified 2026-09-30, exit 12; SH-4/SH-7/SH-8 re-confirmed on the same build) |
| SH-12c | A flagged question in hooked mode is refused out loud, not silently defaulted | `HeyGanesh.App.exe --question "x" --seconds 4` → `note: --question is legacy and is IGNORED in hooked mode` + header `x (ignored: hooked mode listens to the microphone)`, exit **0** | ✅ automated (verified 2026-09-30, exit 0) |
| SH-13 | Push-to-talk turn: press listens, release ends the utterance, a committed transcript resolves and draws, and nothing outlives the turn | Gateway running + `TYPESAFE_API_KEY`/`DEEPSEEK_API_KEY` + mic: hold Ctrl+Alt, speak, release → `utterance: UtteranceResult: "…"`, then the SH-9 verdict/highlight; repeat releasing early → `released before the transcript committed`, exit **0**, **no capture line**; press twice → `REFUSED`; Ctrl+C mid-listen → `CANCELLED`, exit **13**, no overlay window left | 🚧 **needs a live Gateway, keys, a microphone and a human** — first live run also confirms the two protocol details DEC-013 flagged as unverified (`input_audio_buffer.append` field names, `session.ready` version field) |
| SH-14 | Silence costs nothing: `IsNoSpeech` never reaches the orchestrator | `--once` with the mic silent (or a live Gateway that returns no speech) → `no speech heard: the microphone opened and nothing was said`, exit **0**, and **no `verdict`/capture line at all**. Offline proxy for the same mapping: `--once` with `HEYGANESH_QWEN_GATEWAY_URL` unset → `VOICE FAILURE: no transcript was produced (No qwen-audio-agent Gateway endpoint is configured…)`, exit **1**, no capture, no crash | 🚧 the silence half needs a microphone; ✅ automated for the `Failed` half (verified 2026-09-30, exit 1) |

> Scope notes for the shell rows: the shell carries **no `--fake` flags on purpose** (DEC-012) — test doubles belong to verification hosts, so the offline coverage above stops exactly where the real key gates stop, and everything past them (SH-9, the `REFUSED` overlap path, `--no-overlay`, the visible `--duration-ms` effect, `not-per-monitor-dpi-aware`) is unverified rather than unverifiable.
>
> Exit codes must be read from the built `HeyGanesh.App.exe` directly: `dotnet run` obscures non-zero exit codes (the CO-8 lesson). The composition and lifecycle rows (SH-4..SH-8) each needed a **rebuild first** — running against a stale binary in the same batch reported the previous build's behaviour, which briefly made a removed diagnostic look present.

## Voice checks

| # | Check | How to verify | Status |
|---|-------|---------------|--------|
| VO-1 | Voice library and verification host build with zero warnings | `dotnet build src\HeyGanesh.Voice -c Debug` and `dotnet build tools\VoiceSmoke -c Debug` | ✅ automated (verified 2026-09-30: `Build succeeded. 0 Warning(s) 0 Error(s)` for both) |
| VO-2 | No package dependencies were sneaked in, and Voice references no sibling module | `Select-String src\HeyGanesh.Voice\*.csproj, tools\VoiceSmoke\*.csproj -Pattern '<(PackageReference\|ProjectReference)'` → only the smoke host's one `ProjectReference` (Voice's is zero; its csproj *comment* mentions the term, which is why the pattern is anchored to `<`) | ✅ automated (verified 2026-09-30: literal-element matches = 0 in `HeyGanesh.Voice.csproj`, 1 in the smoke host) |
| VO-3 | Committed transcript becomes the verdict | `VoiceSmoke --fake-transcript "where is the Save button"` → `text: "where is the Save button"`, `suppressed: 2`, exit **0** | ✅ automated (verified 2026-09-30, exit 0) |
| VO-4 | Silence is a verdict, not a failure | `--fake-nospeech` → `no speech: True`, no `FAILURE` line, exit **0** (the `Choice=="none"` analogue) | ✅ automated (verified 2026-09-30, exit 0) |
| VO-5 | **Tutor authority: only `transcript.final role=user` can become text** | `--assert-tutor-authority` → replays a recorded stream where the frontend answers anyway; asserts the one question, assistant final/audio counted (not spoken), tool call ignored, suppression ≥ 3, exit **0** | ✅ automated (verified 2026-09-30: all 7 checks PASS, ledger line `assistantText=1+1 audio=2 tools=1 unknown=0 cancels=0 tasks=0`, exit 0) |
| VO-6 | GCP message allowlist + frame classification are exactly what the loop switches on | `--assert-gcp-events` → 7 client messages, `session.hello` shape with `output_enabled=false`, all builders parse, 11 server-frame classifications (incl. role-less final → Unknown, malformed → Unknown), exit **0** | ✅ automated (verified 2026-09-30: 20 checks PASS, exit 0) |
| VO-7 | Backend Agent guard: `task.*` is named and counted, never ignored | `--assert-no-backend` → classified `TaskEvent`, `SawBackendTask=true`, clean ledger unaffected, exit **0** | ✅ automated (verified 2026-09-30, exit 0). 🚧 The *health-endpoint* half (`AGENT_PROTOCOL` reported by a live Gateway) and the listen-loop abort are live-only |
| VO-8 | Fish contract: wav/44100 body, strict WAV validation, key gate before any I/O | `--assert-fish-format` → body `format=wav`+`sample_rate=44100`, non-wav refused at construction, fixture parses to 8820 B @ 44.1 k with final marker, stereo/wrong-magic refused, missing key → `VoiceAuthenticationException` before the (I/O-booby-trapped) handler, exit **0** | ✅ automated (verified 2026-09-30: 8 checks PASS, exit 0) |
| VO-9 | Cancellation propagates and never becomes a verdict; overlap refused not queued | `--cancel-ms 50` → `CANCELLED: … no verdict`, exit **13**; `--burst-fake` → second listen `REFUSED (not queued)`, first completes, exit **0** | ✅ automated (verified 2026-09-30: exits 13 and 0) |
| VO-10 | Key errors fail fast and are never echoed; the real speaker plays | `--fake-auth-error` → `KEY ERROR: …`, exit **12** (presence/length only in the header); `--speak-fake --text "Found the Save button"` → real `WinMmAudioOutput` plays the fake TTS sine, `SpeakResult: 21 chars, 0.35s audio via fish-audio (327 ms)`, exit **0** | ✅ automated (verified 2026-09-30: exits 12 and 0 — this row caught the real `CALLBACK_EVENT 0x00010000`→`0x00050000` bug) |

> Scope notes for the Voice rows: everything offline runs against fakes and recorded frames — no Gateway, no microphone, no Fish call. **Not verified (live-only, human-gated):** a real listen against a running qwen-audio-agent Gateway (`session.hello` payload field names beyond the verified ones, health-endpoint capability/backend reporting, live suppression of a real frontend answer), the mic capture path on real hardware, and a real Fish synthesis. Two implementation facts are known-unverified and marked so: the `input_audio_buffer.append` payload field names (`audio`/`sample_rate`) are the plan's best reading, and `session.ready`'s version field is read tolerantly (`protocolVersion` || `protocol`) — VO-6 pins everything that *is* verified.



