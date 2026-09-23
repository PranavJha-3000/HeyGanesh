# DECISIONS — Architecture & Design Log

> Dated, **model-versioned** decision entries. One decision per entry, **newest first**.
> Use the DECISIONS entry template in [TEMPLATES.md](./TEMPLATES.md) §2.
> Entries are append-only: never rewrite a past decision — supersede it with a new entry
> that references the old one (`Supersedes: DEC-<nnn>`).

## Index

| ID | Date | Model / version | Decision | Status |
|----|------|-----------------|----------|--------|
| [DEC-009](#dec-009--2026-09-22--cline-agent-model-id-not-exposed-to-the-agent) | 2026-09-22 | Cline agent (model id not exposed to the agent) | Primary/fallback result-type unification deferred to the Context Orchestrator; SelectionResult/VisionResult kept separate-but-uniform, fallback gate deferred to a future Jev component | accepted |
| [DEC-008](#dec-008--2026-09-22--cline-agent-model-id-not-exposed-to-the-agent) | 2026-09-22 | Cline agent (model id not exposed to the agent) | Element Selection primary (UIA+Jev jev-1.13.0, 120-cap, none-option + hallucination guard); Vision fallback | accepted |
| [DEC-007](#dec-007--2026-09-22--laguna-s-21) | 2026-09-22 | Laguna S 2.1 | Temporary direct DeepSeek key from `DEEPSEEK_API_KEY` env var behind an injectable `IVisionClient` seam, pending the API key security proxy | accepted |
| [DEC-006](#dec-006--2026-09-22--laguna-s-21) | 2026-09-22 | Laguna S 2.1 | Vision asks `deepseek-flash` for a 0..1000 grid box in JSON mode and derives consistency across samples; no standard-resolution preprocessing for DeepSeek | accepted |
| [DEC-005](#dec-005--2026-09-21--glm-53-flash) | 2026-09-21 | GLM-5.3-Flash | Self-exclusion via `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` on our own windows | accepted |
| [DEC-004](#dec-004--2026-09-21--glm-53-flash) | 2026-09-21 | GLM-5.3-Flash | Screen Capture on Windows.Graphics.Capture with zero dependencies; bounded settle window | accepted |
| [DEC-003](#dec-003--2026-09-21--glm-53-flash) | 2026-09-21 | GLM-5.3-Flash | No automatic re-hook watchdog for the silent-unhook case (deferred) | accepted |
| [DEC-002](#dec-002--2026-09-21--glm-53-flash) | 2026-09-21 | GLM-5.3-Flash | AltGr guard and chord rules for the default Ctrl+Alt hotkey | accepted |
| [DEC-001](#dec-001--2026-09-21--glm-53-flash) | 2026-09-21 | GLM-5.3-Flash | Low-level hook + pump/worker split + SafeHandle for the Global Hotkey Monitor | accepted |

---

<!-- DEC-<nnn> ENTRIES BELOW THIS LINE — newest first -->

## DEC-009 — 2026-09-22 — Cline agent (model id not exposed to the agent)

- **Status:** accepted
- **Decision:** Primary (Element Selection) and fallback (Vision) results stay as deliberately SEPARATE types — `SelectionResult.cs` states the two modules "must not reference each other (DEC-008)" — but are kept UNIFORM in shape (Found, Description, BoxOnScreen/CenterOnScreen in global physical pixels, a three-tier Confidence at 0.8/0.5 boundaries, plus the raw probability signals) so a future Context Orchestrator can consume both paths behind a single consumer and a future Jev confidence-gate (`SelectionResult.cs`: "a future Jev confidence-gate can branch on" the tier) decides ES→Vision fallback. No shared result type, abstraction assembly, or resolver is introduced now; unification is deferred until the Context Orchestrator exists.

### Context / alternatives considered
- `SelectionResult` and `VisionResult` already carry parallel verdicts (Found, Description, BoxOnScreen/CenterOnScreen, three-tier Confidence, raw signals), which is what makes a uniform consumer shape possible today.
- Option A — unify behind one shared result type + a resolver now. Option B — Vision-centric (let VisionResult drive). Option C — keep both types, uniform in shape, defer unification.

### Chosen because
- Option C: smallest blast radius, touches no working module, and the shape parity is already provable from the code. Option A would force a cross-module reference that `SelectionResult.cs` forbids now; Option B inverts the ES-primary / Vision-fallback contract (DEC-008).

### Rejected because
- Option A — a shared type + resolver requires one module to reference the other (or a new abstraction), violating "the two modules must not reference each other" and shipping a resolver before its consumer (the Context Orchestrator) exists.
- Option B — Vision-centric unification inverts DEC-008's ES-primary ordering.

### Consequences
- **Deferred (explicit):** no shared result type, abstraction assembly, or resolver exists in this tree; the uniform `SelectionResult` / `VisionResult` shape and the 0.8/0.5 Confidence tiers are the only contract. Those names are a future, Context-Orchestrator-driven decision.
- When the Context Orchestrator lands, DEC-008's fallback policy applies: empty tree / unmapped choice / UIA access-denied / no-choice → Vision; `Confidence == High` and a non-degraded verdict accept Element Selection.
- ConfidenceLevel stays classification here, not a gate — the gate belongs to a future Jev component (`SelectionResult.cs`: "a future Jev confidence-gate can branch on").

## DEC-008 — 2026-09-22 — Cline agent (model id not exposed to the agent)

- **Status:** accepted
- **Decision:** Element Selection is the PRIMARY coordinate-detection path (`ElementSelectionService.cs`: "the PRIMARY coordinate-detection path (DEC-008)"): it enumerates the target window's UIA tree, caps it to an interactive candidate manifest (MaxCandidates default 120; text/pane/group/custom controls are deliberately excluded as dense-text territory), and has Jev (model id `jev-1.13.0`, TypeSafe System One "choice" API at `api.typesafe.ai`; bearer key read from `TYPESAFE_API_KEY`) select among the offered `c001..` candidate ids; the selected candidate's UIA BoundingRectangle is the coordinate answer. DeepSeek Vision is the FALLBACK, invoked only when Element Selection returns a Failed result (no resolvable window, empty interactive tree, Jev returned no choice, or Jev chose an id never offered).

### Context / alternatives considered
- Candidate enumeration via UIA using hand-declared classic-COM interop (no WPF, no UIA SDK package, no `<COMReference>`; on .NET Core MSBuild this is rejected with MSB4803 and no Tlbimp host is present).
- Jev as the System One bounded-choice selector only: it receives the candidate ids plus the question and returns one id (or the synthetic `none` option) — never free text, never coordinates, never geometry (`IJevClient.cs`: "Jev returns typed decisions only — a choice among offered options … never free text, never coordinates").
- Vision over DeepSeek pixels (model `deepseek-flash`, 0..1000 integer grid); Vision-primary was the pre-ES design (DEC-006/007).

### Chosen because
- UIA exposes real structure; Jev selects among real candidates → the answer is an exact UIA rectangle rather than a visual estimate. For an interaction-target question (click Save) the UIA rectangle is authoritative; Vision is strongest for the excluded dense-text case. The `none` escape (`SelectionOptions.IncludeNoneOption`, default true) makes an honest no-match possible, and the hallucination guard (Jev choosing an unmapped id is refused — `MapResponse`) makes coordinate fabrication impossible.

### Rejected because
- Vision-primary — least accurate for dense text (where-does-it-say-X) and imprecise for interaction targets; it also costs a screen capture on the happy path this module avoids.
- A shared result type + resolver now — deferred to DEC-009 until the Context Orchestrator exists to consume it.

### Consequences
- Public surface: `ElementSelectionService.SelectAsync(string question, SelectionTarget? target = null, CancellationToken ct = default) -> Task<SelectionResult>` (`SelectionTarget`: CursorWindow default, ForegroundWindow). `SelectionResult` mirrors `VisionResult` — Found, Description, BoxOnScreen/CenterOnScreen in global physical px, ConfidenceLevel (High≥0.8 / Medium≥0.5), raw JevConfidence/WinnerProbability/NoneProbability/PlausibleProbability, plus diagnostics (Latency, InputTokens/OutputTokens, ModelId, candidate counts, Target, chosen UiaCandidate, FailureReason; `Failed => FailureReason != null`). `IJevClient` is the transport seam; `SelectionOptions` (MaxCandidates=120, ModelId=`jev-1.13.0`, BaseUrl=`api.typesafe.ai`, IncludeNoneOption=true, PerCallTimeout=30s).
- Geometry reuses `PixelRect` (ScreenCapture) in global physical screen pixels (DEC-003); DPI awareness stays the host's decision.
- **Threshold-free by contract** (same as Vision/DEC-006): raw signals are surfaced, not thresholded — the 0.8/0.5 tier (`blended = 0.5 * JevConfidence + 0.5 * WinnerProbability`; `Mapping.cs`: `blended >= 0.8f ? ConfidenceLevel.High : blended >= 0.5f ? ConfidenceLevel.Medium : ConfidenceLevel.Low`) and the ES→Vision fallback decision belong to a future Jev confidence-gate (DEC-009).
- **Guide-only (CONSTRAINTS.md):** locates and describes only; never moves the cursor, never focuses/activates, never injects input. The UIA `SetFocus` slot exists only to keep the COM vtable aligned.
- Fallback triggers return a Failed `SelectionResult` (FailureReason), not exceptions — signals, not errors: no resolvable window; `COMException` / `InvalidOperationException` during enumeration; empty interactive tree (manifest.Count == 0); Jev no-choice; Jev chose `none`; or Jev chose an id not in the manifest (hallucination guard).
- Keys: bearer read from `TYPESAFE_API_KEY` in TypeSafeJevClient, extending DEC-007's stopgap through a second seam; never in the module contract, logs (presence/length only), or committed files — ends when the API key security proxy lands as another `IJevClient`.

## DEC-007 — 2026-09-22 — Laguna S 2.1

- **Status:** accepted
- **Decision:** Vision calls DeepSeek directly through an injectable `IVisionClient` seam, reading the bearer key from the `DEEPSEEK_API_KEY` environment variable at call time only. This direct-key path is an explicitly temporary stopgap that dies when the **API key security proxy** module lands.

### Context / alternatives considered
- **Option A — injectable `IVisionClient` seam + env-var key** (what VisionService depends on; `DeepSeekVisionClient` is today's implementation).
- Option B — hardcode the endpoint/transport into VisionService and read the key there.
- Option C — block Vision until the proxy module exists.
- Option D — commit a dev key file alongside the code for the harness.

### Chosen because
- VisionService never touches a key, a URL, or HTTP: the proxy arrives later as another `IVisionClient` implementation (or as a `BaseUrl` redirect — both supported), with no change to the module. Clicky's key-isolation pattern is reproduced as a type boundary, not as prose intent.

### Rejected because
- Option B — couples transport to the module; a proxy that rewrites bodies/auth cannot be expressed as "just another HttpClient".
- Option C — serialises the whole project behind one module; the seam makes the proxy a drop-in.
- Option D — unacceptable: a committed key is a leaked key. The repo carries none (verified by scan).

### Consequences
- **Explicit key-handling risk until the proxy exists:** the key lives in a developer-shell environment (inheritable by child processes), transits directly to the provider, and could sit visibly in a terminal during a screen capture. Mitigations: harness never echoes it (presence/length only), fail-fast `KEY ERROR` (exit 12) when absent, loud TEMPORARY header on `DeepSeekVisionClient`, and this entry records the expiry condition.
- Privacy/product note for the owner: this module sends screen contents to a cloud API. Redaction/off-peak policy belongs at the proxy layer and may become a CONSTRAINTS entry later.
- The harness also accepts a recording fake `IVisionClient`, so the full parse→map→annotate path is verifiable with no key and no network (V-4).

## DEC-006 — 2026-09-22 — Laguna S 2.1

- **Status:** accepted
- **Decision:** Vision sends the capture JPEG inline (base64 data URL) to model id `deepseek-flash` via OpenAI-compatible Chat Completions, in JSON mode with a prompt-pinned **0..1000 integer grid** coordinate convention, takes **2 samples at temperature 0.7** by default, and derives **consistency** (1 − mean pairwise centre distance / grid diagonal) alongside the model's self-reported confidence.

### Context / alternatives considered
- **Option A — 0..1000 integer grid, box + confidence, 2 samples @ 0.7** (chosen).
- Option B — ask the model for raw pixel coordinates.
- Option C — normalised 0..1 float coordinates.
- Option D — single sample at temperature 0 (deterministic).
- Option E — resize/match the JPEG to a standard resolution before sending (Clicky's habit).
- Option F — self-reported confidence only, or no confidence at all.

### Chosen because
- Pixels force the model to guess internal scale (encoders tile/resize); ints are more reliably emitted than long decimals; and 2 @ 0.7 gives a measurable consistency signal (at temp 0 two samples agree by construction, so the signal would be permanently 1.0 and useless to Jev).
- 1.28 px granularity at 1280 px width is finer than the model's own grounding error (RefCOCO 86.0), so the grid is not the limiting factor.

### Rejected because
- Option B — systematic scale drift; exactly the distortion Clicky's matching tried to avoid.
- Option C — LLM decimal outputs are less reliable than integer grid outputs.
- Option D — yields determinism but zero consistency signal; the Jev gate would have nothing to weight.
- Option E — **answered by research, not assumed:** verified in the official DeepSeek vision docs (2026-09-21 session research) that V4.1-Flash accepts arbitrary aspect ratios natively (8192 px/side cap, any aspect, JPEG native). No standard-resolution matching is required or recommended; applying it would add distortion, not remove it. What removes scale ambiguity instead is stating exact image dimensions in the prompt + the grid convention.
- Option F — self-reports are poorly calibrated; the feasibility concern (DeepSeek vision vs a dedicated coordinate tool) demands doubt be *measurable*. Both signals are carried so Jev can weight consistency heaviest.

### Consequences
- JSON mode needs the word "json" + schema example in the prompt; it is documented to occasionally return empty content, so parsing is tolerant (fence strip → strict JSON → regex) with one parse retry.
- Out-of-grid values are parse failures (not silent clamps), so a scale-confused model cannot poison the result.
- `detail: high` is the default (fidelity over tokens); `--max-dim` lets later accuracy work trade tokens for small-target recall.
- Blank frames (Secure Desktop/elevated/RDP) short-circuit before any network call via the capture's non-black ratio.
- `CoordinateMapper` is the single place scale conversion lives (grid → JPEG px → monitor px → screen px); screen-space output assumes a PerMonitorV2 host (standing DPI risk, now load-bearing for coordinates).
- The blended High/Medium/Low verdict is Vision's *raw* signal only — `VisionResult` carries `RawConfidence` and `Consistency` separately so the future Jev gate owns thresholds, not Vision.
- Grounding precision on dense desktop UI is still unproven (RefCOCO 86% is natural images): the visual hit-check + `--max-dim` tuning remain the evidence path; bad numbers are a feasibility finding, not a bug to hide.

## DEC-005 — 2026-09-21 — GLM-5.3-Flash

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

## DEC-003 — 2026-09-21 — GLM-5.3-Flash

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

