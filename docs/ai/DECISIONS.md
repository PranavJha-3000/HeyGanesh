# DECISIONS — Architecture & Design Log

> Dated, **model-versioned** decision entries. One decision per entry, **newest first**.
> Use the DECISIONS entry template in [TEMPLATES.md](./TEMPLATES.md) §2.
> Entries are append-only: never rewrite a past decision — supersede it with a new entry
> that references the old one (`Supersedes: DEC-<nnn>`).

## Index

| ID | Date | Model / version | Decision | Status |
|----|------|-----------------|----------|--------|
| [DEC-007](#dec-007--2026-09-21--glm-53-flash) | 2026-09-21 | GLM-5.3-Flash / Laguna S 2.1 | Vision via injectable `IVisionClient`; direct-key stopgap pending the API key security proxy | accepted |
| [DEC-006](#dec-006--2026-09-21--glm-53-flash) | 2026-09-21 | GLM-5.3-Flash / Laguna S 2.1 | Vision on DeepSeek with a 0..1000 grid + blend/consistency confidence (no resolution matching) | accepted |
| [DEC-005](#dec-005--2026-09-21--glm-53-flash) | 2026-09-21 | GLM-5.3-Flash | Self-exclusion via `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` on our own windows | accepted |
| [DEC-004](#dec-004--2026-09-21--glm-53-flash) | 2026-09-21 | GLM-5.3-Flash | Screen Capture on Windows.Graphics.Capture with zero dependencies; bounded settle window | accepted |
| [DEC-003](#dec-003--2026-09-21--glm-53-flash) | 2026-09-21 | GLM-5.3-Flash | No automatic re-hook watchdog for the silent-unhook case (deferred) | accepted |
| [DEC-002](#dec-002--2026-09-21--glm-53-flash) | 2026-09-21 | GLM-5.3-Flash | AltGr guard and chord rules for the default Ctrl+Alt hotkey | accepted |
| [DEC-001](#dec-001--2026-09-21--glm-53-flash) | 2026-09-21 | GLM-5.3-Flash | Low-level hook + pump/worker split + SafeHandle for the Global Hotkey Monitor | accepted |

---

<!-- DEC-<nnn> ENTRIES BELOW THIS LINE — newest first -->

## DEC-007 — 2026-09-21 — GLM-5.3-Flash / Laguna S 2.1

- **Status:** accepted
- **Decision:** Vision reaches DeepSeek through the injectable `IVisionClient` seam (one method, `AskAsync`); the only implementation today is a direct `DeepSeekVisionClient` that reads `DEEPSEEK_API_KEY` from the environment as an explicitly-temporary stopgap, overridable to a proxy via `VisionOptions.BaseUrl`. VisionService never sees a key and is unchanged when the proxy lands.

### Context / alternatives considered
- Option A — `IVisionClient` seam with direct-HTTP default now, proxy later.
- Option B — raw `HttpClient` injected into VisionService.
- Option C — `DelegatingHandler` on the client's pipeline.

### Chosen because
- The proxy is a different auth topology, not a transport tweak: a separate type keeps it greppable, testable, and swappable in one constructor argument. The key never appears in the module contract, in logs (presence/length only), or in any committed file.

### Rejected because
- Option B — leaks transport concerns into the service and cannot express a proxy that rewrites bodies/auth.
- Option C — couples the proxy to the client's HTTP internals rather than to a clean boundary.

### Consequences
- **RISK (explicit, temporary):** until the API key security proxy module exists, the key lives in the shell/process environment (inherited by children), transits to `api.deepseek.com` from the client, and could land inside a screen capture if visible in a terminal. The harness never echoes it; `--fake` exists for keyless verification. This exception dies when the proxy module lands.
- **Privacy (CONSTRAINTS-relevant, undecided):** screens are sent to a cloud API by design; redaction/off-peak policy belongs to a future constraints decision, not to this module.

## DEC-006 — 2026-09-21 — GLM-5.3-Flash / Laguna S 2.1

- **Status:** accepted
- **Decision:** Vision grounds elements with DeepSeek (`deepseek-flash`) over a **0..1000 integer grid** on the JPEG, no aspect-ratio preprocessing (the provider accepts any ratio; resizing would add distortion), temp **0.7** with **2 samples** driving a pairwise-center **consistency** signal blended 50/50 with the model's self-reported confidence into a High/Medium/Low verdict for a future Jev gate.

### Context / alternatives considered
- Option A — pixels; Option B — 0..1 floats; Option C — 0..1000 integer grid.
- Standard-resolution matching (Clicky's approach): confirmed unnecessary for DeepSeek; rejected.
- JSON-mode-only parsing: rejected as a single point of failure (documented empty-content mode); tolerant fallback + one retry.
- Temp 0 sampling: rejected — agreement by construction makes the consistency signal always 1.0.
- Single signal (model confidence only): rejected — self-reports are poorly calibrated.

### Chosen because
- The integer grid follows the Qwen-VL convention for exactly this problem; exact dimensions in the prompt remove scale ambiguity without touching the image; two samples at 0.7 keep accuracy while making variance measurable; `VisionResult` carries both raw inputs so Jev owns thresholds, not Vision.

### Rejected because
- Pixels force the model to guess the image's internal scale (encoders tile/resize); floats are emitted less reliably than integers; Clicky-style resizing solves a problem this provider does not have.

### Consequences
- Grounding precision on dense desktop UI is still unproven (RefCOCO 86% is natural images): the visual hit-check + `--max-dim` tuning remain the evidence path; bad numbers are a feasibility finding, not a bug to hide.
- `CoordinateMapper` is the only place scale conversion lives; screen-space output assumes a PerMonitorV2 host (standing DPI risk).
- Out-of-range model answers are rejected, never clamped silently; blank frames short-circuit to `Failed` with `WasBlankFrame`.

- **Status:** accepted
- **Decision:** HeyGanesh's own windows mark themselves with `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)`, exposed as `CaptureExclusion.TryExcludeFromCapture`. Screen Capture does **not** attempt to filter its own output; the OS omits marked windows from capture output, for every capture path, ours included.

### Context / alternatives considered
- Option A — a per-capture exclusion list passed to WGC.
- Option B — `WDA_MONITOR`, which replaces the window's content with black.
- Option C — post-capture masking: capture everything, then blank the rectangles of HeyGanesh's own windows.
- Option D — avoid capturing while the overlay is visible (hide it first).

### Chosen because
- One call per window, enforced by the compositor, with no geometry bookkeeping and no dependence on which capture path is used. It also breaks a real feedback loop: an overlay that draws the answer on screen while a capture can still see it would feed HeyGanesh's own rendering back in as if it were the user's screen.

### Rejected because
- Option A — no such API exists in `Windows.Graphics.Capture`; monitor capture photographs the composited desktop.
- Option B — a black rectangle is just as visible to a vision model as the window itself, and it still occludes the content behind it.
- Option C — requires knowing overlay geometry at capture time, races with window movement and resizing, and silently fails for any window that forgets to register itself.
- Option D — the overlay is the product's output; hiding it to take a picture defeats the purpose and flickers.

### Consequences
- **Contract for Screen Overlay:** call it on the real top-level HWND (not a XAML `Window` object, not a child HWND), once the native window exists. Affinity is stored per window, so it must be re-applied whenever a window is recreated — per-monitor overlays, monitor hot-plug, a window destroyed and rebuilt.
- Only the calling process's own windows can be marked; a foreign window is refused with `ERROR_ACCESS_DENIED` (5), which the verification host asserts as correct behaviour.
- **Measured proof (verification host, this session):** a solid-white probe window at a known rectangle was captured with the affinity off and on. White pixels inside the probe rectangle: **100%** with affinity off (the probe is in the frame), and **100% of those pixels changed** with affinity on (the probe is gone) — against a same-rectangle noise floor of **0.0%** and a control-region change of **0.6%**. The exclusion is measured, not inferred.
- Gotcha for future tests: the probe's window class answers `HTTRANSPARENT` to hit-testing, so `WindowFromPoint` reports the window *beneath* it. Verify the probe by its appearance in the capture, never by hit-testing.
- By design this cannot hide other applications' windows.

## DEC-004 — 2026-09-21 — GLM-5.3-Flash

- **Status:** accepted
- **Decision:** Screen Capture is a dependency-free module over `Windows.Graphics.Capture`, using the projections that arrive purely by targeting `net8.0-windows10.0.22621.0` (**zero `PackageReference`**): monitor capture items from `IGraphicsCaptureItemInterop::CreateForMonitor(HMONITOR)`; a `Direct3D11CaptureFramePool.CreateFreeThreaded` pool of 1 buffer; pixels via `SoftwareBitmap.CreateCopyFromSurfaceAsync`; JPEG via `BitmapEncoder` (`ImageQuality` 0.8 as `Single`, `BitmapTransform` Fant scaling to a **1280 px** cap, aspect preserved, never upscaled). Monitors come from `EnumDisplayMonitors`/`GetMonitorInfoW`; the cursor's monitor from `GetCursorPos` + `MonitorFromPoint` and is ordered first; monitors are captured sequentially.

### Context / alternatives considered
- Option A — GDI `BitBlt` of the screen DC.
- Option B — Desktop Duplication API (`IDXGIOutputDuplication`).
- Option C — `GraphicsCapturePicker`.
- Option D — `TryCreateFromDisplayId` + Windows App SDK `GetDisplayIdFromMonitor`.
- Option E — a third-party capture/graphics package.
- Option F — staging-texture readback through `IDirect3DDxgiInterfaceAccess` and hand-declared D3D vtable calls.
- Option G — `System.Drawing` / WIC-COM for encoding.

### Chosen because
- The interop GUIDs and the vtable-significant method order were taken from Microsoft's own capture-interop sample rather than recalled: `IGraphicsCaptureItemInterop` is `{3628E81B-3CAC-4C60-B7F4-23CE0E0C3356}` with `CreateForWindow` at slot 0 and `CreateForMonitor` at slot 1, and the item IID is `{79C3F95B-31F7-4EC2-A464-632EF5D30760}`. An earlier draft had the interop IID wrong — check these, do not remember them.
- `CreateFreeThreaded` was chosen over `Create` because it requires no `DispatcherQueue`, so the module runs in a plain console host with no UI thread and no message pump.
- `SoftwareBitmap.CreateCopyFromSurfaceAsync` replaced Option F, removing vtable-level COM from the module entirely: a wrong vtable slot is an access violation, not a compile error.

### Rejected because
- Option A — legacy path, no hardware acceleration, DWM tearing under load, and the messiest DPI behaviour of the three capture routes.
- Option B — built around long-lived duplication sessions, which fights the one-shot model; raw COM from C# without a package means hand-rolled vtables.
- Option C — a UI dialog: not automatable, and not how a hotkey-driven product should behave.
- Option D — pulls the Windows App SDK bootstrap in for an unpackaged console. Retained as the documented fallback if `CreateForMonitor` ever fails.
- Option E — violates the standing "no new NuGet dependencies without asking" instruction.
- Option G — `System.Drawing` is package-only on .NET 8; WIC-COM is more COM surface than the already-projected `BitmapEncoder`.

### Consequences
- **The approved settle-frame plan was corrected by measurement.** WGC reports frames on screen *change*, so a still desktop offers only one frame. Preferring frame 2 while waiting the full 2 s `FrameTimeout` cost **~2.5 s per capture and then used frame 1 anyway**. It is now a bounded best-effort window: `SettleFrames` 2 preferred within `SettleTimeout` 250 ms, with `FrameTimeout` 2 s kept as the hard deadline for *any* frame. Capture is ~0.7 s per monitor at 2560x1600 (was ~2.5 s).
- Frames are requested as `B8G8R8A8UIntNormalized`. With Windows HD Color (HDR) enabled this tone-maps content, so colours can look washed out — acceptable for vision input, and the price of not carrying a 16-bit pipeline through the whole stack.
- A yellow capture border is drawn by the system around the captured item. Clearing it requires a **packaged** app declaring the `graphicsCaptureWithoutBorder` capability plus a user-consented `GraphicsCaptureAccess.RequestAccessAsync(Borderless)`; without that consent, setting `IsBorderRequired = false` **succeeds and is silently ignored**. That is a UX decision for the packaged shell, so the property is left at its default here. This is *not* the macOS TCC prompt: capture itself needs no permission on Windows.
- `CreateForMonitor` has been reported returning `E_INVALIDARG` on some Windows 11 build 26100 machines; the Microsoft response verified `S_OK` on 26200 builds and pointed at RDP sessions, GPU drivers and unusual integrity levels. It returns `S_OK` on this project's build-26200 machine, and the verification host fails loudly if that ever changes. Fallback (not implemented): `TryCreateFromDisplayId` once the Windows App SDK shell exists.
- Nothing is captured while the UAC Secure Desktop is showing, and monitor capture cannot be created at all in a Remote Desktop session.
- **DPI awareness stays the host's decision.** Coordinates and frames are physical pixels; the library never calls `SetProcessDpiAwarenessContext` because that is process-global. The verification host sets `PerMonitorV2` itself, and this machine's 150% scaling makes the difference visible (2560x1600 physical vs 1707x1067 virtualised).
- Each capture opens and closes its own session and pool, so no capture runs while the user is not asking a question. Verified: 25 consecutive captures, handle delta 1, flat managed heap.



- **Status:** accepted
- **Decision:** Do not build an automatic re-hook watchdog for the silent-unhook failure mode. Instead keep the hook callback trivial, publish diagnostics (`HookCallbackCount`, `MaxCallbackLatency`, `DroppedEventCount`, `LastError`), and document the failure mode.

### Context / alternatives considered
- Windows silently removes a `WH_KEYBOARD_LL` hook whose callback exceeds `LowLevelHooksTimeout` (HKCU\Control Panel\Desktop, ~300 ms default). No exception, no return code, and no API to ask whether the hook is still installed. This is the Windows analog of macOS's "tap disabled by timeout".
- Option A — periodic re-hook timer (unhook + reinstall on a cadence).
- Option B — synthetic-keystroke liveness probe via `SendInput`.
- Option C — diagnostics only, trivially fast callback, deferred watchdog.

### Chosen because
- The real mitigation is already structural: the hook proc parses one struct and hands it to a single-reader/single-writer channel, with no user code running in the callback, so callbacks sit orders of magnitude below the timeout (measured max 0.63 ms). A watchdog would add a new failure mode to protect against a case the design already makes unlikely.

### Rejected because
- Option A — there is a window between unhook and reinstall in which real keystrokes are simply not observed; scheduling those gaps to recur is worse than a rare, diagnosable deafness.
- Option B — injecting keystrokes creates input the user never typed, is visible to every other application's hooks, and could trigger other hotkeys.

### Consequences
- If the OS does remove the hook, the module goes deaf until `Stop()`/`Start()` or process restart; callers can watch `MaxCallbackLatency` and `HookCallbackCount` for evidence.
- Revisit once the Context Orchestrator exists and can decide how to surface "degraded" to the user — that is a product decision, not a module decision.

## DEC-002 — 2026-09-21 — GLM-5.3-Flash

- **Status:** accepted
- **Decision:** The default push-to-talk chord is **Control (either side) + left Alt**, held with no other key typed in between. Generic `VK_MENU` is treated as right Alt (fails closed). Shift and Win are neutral. A non-modifier key pressed while a required modifier is down "dirties" the attempt, ending any press and preventing re-arming until the chord is fully released.

### Context / alternatives considered
- On many layouts AltGr is literally LCtrl + RAlt, so "Ctrl+Alt held" overlaps normal character typing on those layouts.
- Option A — treat any Ctrl + any Alt as the chord.
- Option B — require both modifiers to be left-hand.
- Option C — require Ctrl (either) + left Alt only.

### Chosen because
- Option C separates the PTT chord from AltGr traffic without shipping a keyboard-layout database, while still accepting right Ctrl (which carries no AltGr meaning).

### Rejected because
- Option A — fires during ordinary AltGr typing (e.g. `@`, `€` on European layouts).
- Option B — rejects legitimate right-Ctrl use and buys nothing over C.
- Ignoring the dirty rule — the module would report `Listening` while the user types a follow-up sentence.

### Consequences
- Right Alt can never complete the chord (`RequireLeftAlt` defaults to `true`; configurable in `HotkeyMonitorOptions`).
- A chord broken by typing requires a full release of both modifiers before it re-arms; this is intentional and verified.
- Modifier state must never be re-read from `GetAsyncKeyState` for the key in the event currently being delivered — the async key state is not guaranteed to have caught up, and doing so silently prevented the chord from ever completing (real bug, fixed 2026-09-21).

## DEC-001 — 2026-09-21 — GLM-5.3-Flash

- **Status:** accepted
- **Decision:** Observe the modifier-only chord with a `WH_KEYBOARD_LL` hook installed on a dedicated **pump** thread that owns the message loop; the hook proc only parses `KBDLLHOOKSTRUCT` and enqueues to a single-reader/single-writer `Channel<KeyEvent>`; a separate **worker** thread runs the chord state machine and raises `Pressed`/`Released`. The `HHOOK` is owned by a `SafeHandle`.

### Context / alternatives considered
- Option A — `RegisterHotKey` / `WM_HOTKEY`.
- Option B — `GetAsyncKeyState` polling on a timer.
- Option C — `WH_KEYBOARD` (non-low-level) global hook.
- Option D — third-party package (e.g. SharpHook).
- Option E — `WH_KEYBOARD_LL` with pump/worker split, BCL only.

### Chosen because
- A low-level hook is the only mechanism that reports both edges of a modifier-only hold system-wide, in-process, with no injected code in other processes and no new dependency.

### Rejected because
- Option A — requires a non-modifier virtual key, fires once on combo-down, and has no "held"/"released" concept; it cannot express push-to-talk.
- Option B — adds latency and constant CPU cost, and races with focus changes.
- Option C — global scope requires injecting a DLL into every process; not viable from managed code.
- Option D — violates the standing "no new NuGet dependencies without asking" instruction; the equivalent is ~200 lines of BCL P/Invoke that we fully own.

### Consequences
- Each monitor owns two background threads and a channel; events are raised on a non-UI thread, so UI subscribers must marshal.
- A slow subscriber delays *subsequent events* but can never delay system input — this separation is the reason for the split, and it is why the hook proc must never raise events itself.
- Anything added to `HookProc` risks the OS's ~300 ms callback budget; `MaxCallbackLatency` exists to police that.

