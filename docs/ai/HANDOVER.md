# HANDOVER — Session Log

> Append-only session log. One entry per work session, **newest entry at the top**.
> Use the HANDOVER entry template in [TEMPLATES.md](./TEMPLATES.md) §1.
> Never delete or rewrite a prior entry — correct it with a new entry instead.

## Index

| Date | Model / version | Headline | Branch / commit |
|------|-----------------|----------|-----------------|
| 2026-09-21 | GLM-5.3-Flash / Laguna S 2.1 | Vision / Coordinate Detection module + verification host | `main` (working tree) |
| 2026-09-21 | GLM-5.3-Flash | Screen Capture module + verification host | `main` (working tree) |
| 2026-09-21 | GLM-5.3-Flash | Global Hotkey Monitor module + verification host (first code in the repo) | `main` @ `fc1be37` |

---

<!-- SESSION ENTRIES BELOW THIS LINE — newest first -->

## 2026-09-21 — GLM-5.3-Flash / Laguna S 2.1 — Vision / Coordinate Detection module + verification host

- **Branch / commit:** `main` — working tree changes, uncommitted.
- **Scope:** implement **Vision / Coordinate Detection** (DeepSeek V4.1 Flash grounding over ScreenCapture output) + `tools/VisionSmoke`, before any orchestrator, overlay, Jev, or proxy. Plan approved with no overrides (0–1000 grid, temp 0.7 / 2 samples, detail high).

### Done
- `src/HeyGanesh.Vision/`, `net8.0-windows10.0.22621.0`, **zero `PackageReference`**, project-ref to ScreenCapture: `VisionService` (`LocateAsync` — sampling, tolerant parse + retry, blank-frame short-circuit, `CoordinateMapper` back-mapping), `VisionResult`/`VisionSample` (`Found`, description, normalized/image/source/screen boxes, `Confidence` + `RawConfidence`/`Consistency`, diagnostics), `IVisionClient` seam, `DeepSeekVisionClient` (temporary direct path, key from `DEEPSEEK_API_KEY`), `VisionPrompt`, `VisionResponseParser`, `CoordinateMapper`, `VisionOptions`.
- `tools/VisionSmoke/`: capture-or-`--image` → locate → console result + `capture.jpg`/`annotated.jpg`; `--samples/--max-dim/--detail/--out/--fake`; DPI-v2 opt-in. `--fake` uses an in-harness fixed-box client; live mode calls DeepSeek directly; harness never echoes the key.
- DEC-006 (grid + confidence design; no resolution matching) and DEC-007 (seam + temporary direct-key risk).

### In progress / next
- **Live visual hit-check is yours:** `dotnet run --project tools\VisionSmoke -c Debug -- --question "where is the Start button" --samples 2` with `DEEPSEEK_API_KEY` set; open `out\annotated.jpg`; per-target screen-px error belongs in the next HANDOVER entry. Also: key-absent fail-fast (exit 12), not-found path (exit 0, `found=false`), `--samples 4` consistency movement.

### Risks & gotchas
- **No live call has been made from this tree yet** — grounding precision on dense desktop UI is unproven; bad numbers are a feasibility finding.
- Direct-key stopgap risk recorded in DEC-007 (env inheritance, direct transit, capture-visible key); dies with the proxy module.
- Screen shots go to a cloud API by design — redaction policy is an undecided CONSTRAINTS item, flagged not solved.
- DPI: screen-space output assumes a PerMonitorV2 host (standing risk, now load-bearing for coordinates).
- Single-monitor machine: multi-monitor fan-out is the future orchestrator's job; untested here.
- UAC Secure Desktop / RDP / elevated: inherited blank-frame path from capture; `WasBlankFrame` surfaces it.

### Files touched
- `src/HeyGanesh.Vision/*` (9 files, incl. `IVisionClient.cs` now holding the consolidated `VisionRequest`/`VisionRawResponse` shape); `tools/VisionSmoke/*` (5 files); `docs/ai/{DECISIONS,HANDOVER,ARCHITECTURE,FLOW,TEST-CHECKLIST}.md` (below).

### Verification run
- `dotnet build src\HeyGanesh.Vision -c Debug` → **0 warnings, 0 errors**; `dotnet build tools\VisionSmoke -c Debug` → **0 warnings, 0 errors**; no `PackageReference` in either csproj (comment mentions only).
- `--help` → usage, exit 0. Live mode with key absent → `KEY ERROR`, exit **12**. `--fake --image <prior capture>` → `found=True`, `normalized {0.240,0.120 0.120x0.100}`, `rectangle 153x80+307+96`, `confidence High (model=0.86, consistency=1, samples=2)`, `annotated.jpg` written, exit **0** — parse→map→annotate path proven without network or key.

## 2026-09-21 — GLM-5.3-Flash — Screen Capture module + verification host

- **Branch / commit:** `main` — working tree changes, uncommitted.
- **Scope:** implement the **Screen Capture** module — on-demand still frames of every monitor via `Windows.Graphics.Capture`, plus a runnable verification host, before any app shell, orchestrator or UI exists.
- **Done:**
  - `src/HeyGanesh.ScreenCapture/`, `net8.0-windows10.0.22621.0`, **zero `PackageReference`**:
    - `NativeMethods.cs` — `EnumDisplayMonitors`, `GetMonitorInfoW`, `GetCursorPos`/`MonitorFromPoint`, `D3D11CreateDevice`, `CreateDirect3D11DeviceFromDXGIDevice`, `SetWindowDisplayAffinity`/`WDA_EXCLUDEFROMCAPTURE`.
    - `Monitors.cs` — `PixelRect`, `MonitorInfo`, `Monitors` (enumerate, cursor monitor, capture order).
    - `CaptureExclusion.cs` — public `TryExcludeFromCapture` / `TryIncludeInCapture` contract for our own windows.
    - `CpuFrameReader.cs` — `SoftwareBitmap.CreateCopyFromSurfaceAsync` + blank-frame detection via the `CryptographicBuffer` byte[]↔IBuffer bridge.
    - `WgcFrameGrabber.cs` — one-shot `CreateFreeThreaded` pool + two-phase settle wait; `IGraphicsCaptureItemInterop` vtable order and GUIDs confirmed from Microsoft's own sample.
    - `JpegEncoder.cs` — `BitmapEncoder` JPEG, quality 0.8, Fant downscale to 1280 px, aspect preserved, never upscales, Linear fallback.
    - `ScreenCaptureService.cs` — public API: `EnumerateMonitors`, `GetCursorMonitor`, `IsCaptureSupported`, `CaptureMonitorAsync`, `CaptureAllMonitorsAsync`, with `ScreenCaptureOptions` / `MonitorCapture`.
  - `tools/ScreenCaptureSmoke/` — verification host: monitor table + cursor marker, capture-all → JPEGs, JPEG magic / dimension / aspect / content checks, A/B exclusion proof (decode JPEGs + pixel diff with a same-rectangle noise floor and a control region), `--move-cursor` cursor sweep, `--repeats` leak soak, DPI opt-in.
  - DEC-004 (capture design, zero deps, **corrected settle window**) and DEC-005 (self-exclusion contract + measured proof).
- **Evidence (this session, 2560x1600 @ 150% DPI, build 26200):**
  - `dotnet build` for the library and the host → **0 warnings, 0 errors**; no `PackageReference`.
  - Every monitor captured with valid JPEGs (e.g. 2560x1600 → 1280x800, ~80 KB, 97 %+ non-black). `CreateForMonitor` returns `S_OK`. Per-capture time ~0.7 s (was ~2.5 s before the settle fix).
  - A 25× capture loop leaves handle delta 1 and a flat managed heap.
  - Exclusion A/B on an owned white-window probe: **100 %** of the probe rectangle is white with affinity off; **100 %** of those pixels change with affinity on; noise floor **0 %**, control region **0.6 %** — i.e. exclusion is measured, not inferred. The console HWND is correctly refused (`ERROR_ACCESS_DENIED`), proving the OS enforces the per-process scope.
- **Risks & gotchas:**
  - **Settle correction vs the plan:** WGC delivers frames on screen *change*, so a still desktop offers one frame. Waiting for frame 2 wasted ~2.5 s and still used frame 1 — now a bounded 250 ms preference window (`SettleTimeout`) with the 2 s `FrameTimeout` kept as the hard deadline (DEC-004).
  - **HDR:** `B8G8R8A8` content is tone-mapped on HD-color displays (washed colours); acceptable for vision input.
  - **Yellow border / borderless:** clearing the capture border needs a packaged app + `graphicsCaptureWithoutBorder` capability + consented `RequestAccessAsync(Borderless)`; a non-packaged host's `IsBorderRequired=false` is silently ignored. Deferred to the packaged shell.
  - **RDP / Secure Desktop:** monitor capture items cannot be created under RDP, and nothing is captured on the UAC Secure Desktop.
  - **`CreateForMonitor` E_INVALIDARG** reported on some 26100 machines; `S_OK` here on 26200. Documented fallback: `TryCreateFromDisplayId` once the Windows App SDK shell exists.
  - **DPI:** the library never sets process DPI awareness (process-global side effect). The host must be PerMonitorV2; the verification host sets it itself. Coordinates and frames are physical pixels.
  - **Exclusion lifecycle:** `WDA_EXCLUDEFROMCAPTURE` is per window and must be re-applied whenever a window is recreated (per-monitor overlays, hot-plug, rebuild). The Screen Overlay must apply it to the real top-level HWND.
- **Files touched:** `src/HeyGanesh.ScreenCapture/*.cs` (+ `.csproj`); `tools/ScreenCaptureSmoke/{Program.cs,ScreenCaptureSmoke.csproj}`; `docs/ai/DECISIONS.md` (DEC-004, DEC-005).
- **Open:** Context Orchestrator still pending; Vision/Coordinate Detection is the next consumer of `MonitorCapture.Jpeg`.

## 2026-09-21 — GLM-5.3-Flash — Global Hotkey Monitor module (first code in the repo)

- **Branch / commit:** `main` @ `fc1be37` — working tree has new `src/`, `tools/`, `.gitignore`, `docs/` (all uncommitted)
- **Scope:** implement the **Global Hotkey Monitor** (system-wide, listen-only, modifier-only push-to-talk) with a runnable verification host, before any app shell, orchestrator or UI exists

### Done
- `src/HeyGanesh.GlobalHotkeyMonitor/` class library, `net8.0-windows`, **zero `PackageReference`**:
  - `NativeMethods.cs` — `WH_KEYBOARD_LL` P/Invoke, `SafeHookHandle`, `KBDLLHOOKSTRUCT`.
  - `HotkeyChord.cs` — `ChordTracker`, the modifier-only chord state machine.
  - `GlobalHotkeyMonitor.cs` — `HotkeyState`, `HotkeyMonitorOptions`, `GlobalHotkeyMonitor`.
- Public surface exactly as specified: `Pressed` / `Released` events, `State` (`Idle`/`Listening`), `Start()`/`Stop()`/`Dispose()`, plus diagnostics `HookCallbackCount`, `DroppedEventCount`, `MaxCallbackLatency`, `LastError`.
- `tools/HotkeySmoke/` — manual verification host: timestamped events to console + `smoke.log`, `--seconds=N` for unattended runs, summary (including max callback latency) on exit.
- `.gitignore` — `bin/`, `obj/`, `.vs/`, `*.user`, `smoke.log`. Added because this change is the first to produce build output in the repo; without it `git status` is drowned in artifacts.
- Environment: installed **.NET SDK 8.0.425** via `winget install Microsoft.DotNet.SDK.8`. The machine had a broken runtime-only `dotnet` (no `sdk\`, no `host\fxr\` → `dotnet` exited 0x80008083).

### In progress / next
- Nothing half-finished. The module is standalone and consumes nothing else. Next candidates: the Context Orchestrator (subscribe to `Pressed`/`Released`), or Screen Capture.

### Risks & gotchas
- **AltGr:** chord requires **left** Alt, so AltGr (LCtrl+RAlt on many layouts) can never complete it. Right Alt is deliberately inert (`RequireLeftAlt` default `true`).
- **UIPI / elevated focus:** no events while an elevated window has focus, or on the UAC Secure Desktop. Presents as "chord does nothing", never as stuck `Listening`.
- **Silent unhook** (Windows' version of macOS "tap disabled by timeout"): the OS removes LL hooks whose callbacks exceed `LowLevelHooksTimeout` (~300 ms) with no notification. Mitigation is the trivial callback + diagnostics; **auto-rehook deferred on purpose** (DEC-003).
- **Do not add work to `HookProc`.** Measured max callback latency 0.63 ms (first-call JIT dominated; `WarmUpHookPath()` mitigates). Budget is 300 ms.
- **Events fire on `HeyGanesh.HotkeyMonitor.Worker`**, not the UI thread — WinUI subscribers must marshal.
- **`GetAsyncKeyState` staleness:** never re-read the modifier belonging to the event currently being delivered; it is not guaranteed to have caught up (this was a real bug — see below).
- `dotnet` SDK is now installed **machine-wide** in `C:\Program Files\dotnet` (winget, no elevation needed); the earlier broken install was overwritten by a working one.

### Files touched
- `src/HeyGanesh.GlobalHotkeyMonitor/{HeyGanesh.GlobalHotkeyMonitor.csproj, NativeMethods.cs, HotkeyChord.cs, GlobalHotkeyMonitor.cs}` — the module.
- `tools/HotkeySmoke/{HotkeySmoke.csproj, Program.cs}` — manual verification host.
- `.gitignore` — build output hygiene.
- `docs/ai/HANDOVER.md`, `docs/ai/DECISIONS.md` — this entry; DEC-001 (hook architecture), DEC-002 (AltGr/chord rules), DEC-003 (rehook deferred).

### Verification run
- `dotnet build src\HeyGanesh.GlobalHotkeyMonitor\HeyGanesh.GlobalHotkeyMonitor.csproj -c Debug` → **Build succeeded, 0 warnings, 0 errors**.
- `dotnet build tools\HotkeySmoke\HotkeySmoke.csproj -c Debug` → **Build succeeded, 0 warnings, 0 errors**.
- `HeyGanesh.Tools.HotkeySmoke.exe --seconds=4` → hook live at `state=Idle`, clean stop, summary printed, `last error: none`, **exit 0** (`hook callbacks seen: 0` — no keys were pressed during the unattended run, as expected).
- **Throwaway SendInput verifier** (built in `%TEMP%\heyganesh-hotkeyverify`, deliberately *not* committed; injects only Ctrl/Alt/F23/F24 so no text can reach a focused window) → **24/24 checks passed, exit 0**:
  - modifier-only hold raises `Pressed` and `state=Listening`; release raises `Released` and `state=Idle`; no duplicate on modifier auto-repeat;
  - events observed on thread `HeyGanesh.HotkeyMonitor.Worker`;
  - **listen-only proven**: a second LL hook installed *behind* the monitor's still received every notification (`observer=4 monitor=4`) — only possible because the hook proc returns `CallNextHookEx`;
  - a non-modifier key while held ends the press, re-arms only after full release, and itself still reaches the downstream hook;
  - ordinary key presses raise nothing and do **not** poison the next chord (chord still fires immediately after typing);
  - rapid tap yields a clean `Pressed`/`Released` pair, never stuck `Listening`;
  - `dropped=0`, `maxCallbackLatency=0.63 ms`, `lastError=none`, clean `Stop()` and re-hook, `Dispose()` leaves `Idle`.
- **Two real bugs found and fixed by that run** (both would have shipped silently, and both are exactly the kind the manual harness alone would have missed):
  1. `ResyncFromSystem()` overwrote fresh event-derived modifier state with stale async key state, including for the key being delivered — `LAlt` was cleared on its own key-down, so the chord never completed. Fixed by `ResyncModifiersExcept(currentKey)`.
  2. `_dirty` was set by *any* non-modifier key-down and only cleared on a required-modifier key-up, so after typing in any other app the next chord needed a wasted second attempt. Fixed by dirtying only while a required modifier is already down.
