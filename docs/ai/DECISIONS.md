# DECISIONS — Architecture & Design Log

> Dated, **model-versioned** decision entries. One decision per entry, **newest first**.
> Use the DECISIONS entry template in [TEMPLATES.md](./TEMPLATES.md) §2.
> Entries are append-only: never rewrite a past decision — supersede it with a new entry
> that references the old one (`Supersedes: DEC-<nnn>`).

## Index

| ID | Date | Model / version | Decision | Status |
|----|------|-----------------|----------|--------|
| [DEC-054](#dec-054--2026-10-30--claude-opus-5-5) | 2026-10-30 | claude-opus-5-5 | Start at login is the per-user HKCU `Run` value `HeyGanesh` (the quoted tray path), toggled only by the tray's menu item, off by default; `StartupEntry` is the tray's only registry use | accepted |
| [DEC-053](#dec-053--2026-10-30--claude-opus-5-5) | 2026-10-30 | claude-opus-5-5 | The tray's key screen: one masked box per key, Save/Remove through the key ring, presence and length only; it opens by itself when DeepSeek or TypeSafe is missing; Ctrl+Alt is refused while it is open, through a new optional `triggerPaused` on `RunAsync` | accepted |
| [DEC-052](#dec-052--2026-10-30--claude-opus-5-5) | 2026-10-30 | claude-opus-5-5 | A tray host (`HeyGanesh.Tray`, WinExe, WinForms `NotifyIcon`) runs the App Shell's own composition through a new `RunAsync(args, stop, hookLive)`; the console exe is unchanged; output goes to a log file (newest 5); one tray per session | accepted |
| [DEC-051](#dec-051--2026-10-30--claude-opus-5-5) | 2026-10-30 | claude-opus-5-5 | The router can choose a 3D schematic: with `RouteRequest.Schematics` set (the shell sets it), "3D, no listed model fits" is a schematic lesson on the full catalogue instead of 2D cards; off by default | accepted |
| [DEC-050](#dec-050--2026-10-30--claude-opus-5-5) | 2026-10-30 | claude-opus-5-5 | The scene window draws a lesson's schematic (optional `ISchematicCanvas`, handed the schematics by `LessonPlayer`) and the shell asks for them whenever it offers 3D; a click on a schematic part stays refused, because follow-ups use vetted library text only | accepted |
| [DEC-049](#dec-049--2026-10-30--claude-opus-5-5) | 2026-10-30 | claude-opus-5-5 | The planner offers one schematic only when the request carries a catalogue and the Part Library's geometry check; the validator accepts none without that check; the eval gains K-12 (a schematic lesson must say it is simplified) | accepted |
| [DEC-048](#dec-048--2026-10-30--claude-opus-5-5) | 2026-10-30 | claude-opus-5-5 | VisualPlan 1.2: a lesson may define one schematic model (closed shapes and finishes, ≤ 12 parts / 40 shapes, ±2 box) for an object the catalogue lacks; the engine checks structure, the Part Library's `Schematics.Check` checks geometry through a delegate; a refused schematic costs only its beats | accepted |
| [DEC-047](#dec-047--2026-10-30--claude-opus-5-5) | 2026-10-30 | claude-opus-5-5 | The Tutor is on: `CreateTutorClient()` returns the DeepSeek Tutor whenever the key ring has the deepseek key (null and an honest skip otherwise); TU-8 allows exactly one key-named option, `DeepSeekTutorOptions.ApiKey` typed `Func<string?>` (amends DEC-017) | accepted |
| [DEC-046](#dec-046--2026-10-30--claude-opus-5-5) | 2026-10-30 | claude-opus-5-5 | The App Shell's API clients (Jev, Vision, Fish, the lesson and router models) read their keys through the key ring per request; `SelectionOptions`, `VisionOptions`, `FishTtsOptions` gain an optional `Func<string?> ApiKey` that defaults to the old environment read; the Tutor waits for step 9 because TU-8 forbids a key-named option | accepted |
| [DEC-045](#dec-045--2026-10-30--claude-opus-5-5) | 2026-10-30 | claude-opus-5-5 | The "API key security proxy" is `HeyGanesh.Security.KeyRing`: three known keys, Windows Credential Manager first and the environment variable as fallback, described by presence/length/source only; the App Shell gains `--keys`, `--set-key`, `--remove-key`; clients read through it from step 8b | accepted |
| [DEC-044](#dec-044--2026-10-30--claude-opus-5-5) | 2026-10-30 | claude-opus-5-5 | The evaluation set records live (`--record`, the shell's DeepSeek client, key presence only) and replays offline (`--replay`); a committed baseline gates CI: a check that passed there and fails now exits 1, a changed planner prompt exits 3 | accepted |
| [DEC-043](#dec-043--2026-10-30--claude-opus-5-5) | 2026-10-30 | claude-opus-5-5 | A planner evaluation set: 30 questions (six verbatim from the HeyClicky reference video) scored by 11 deterministic checks in a new host `tools/PlannerEval`; 7a proves every check passes and fails offline (CI G-22); no LLM judge in v0 | accepted |
| [DEC-042](#dec-042--2026-10-30--claude-opus-5-5) | 2026-10-30 | claude-opus-5-5 | A lesson turn plans through `PlanStreamingAsync`: the cards open on the first released beat and each beat plays while the rest is written; a stream that stops early ends `Stopped` with the reason; the shell's DeepSeek client advertises `Streaming` and prints `first beat :` | accepted |
| [DEC-041](#dec-041--2026-10-30--claude-opus-5-5) | 2026-10-30 | claude-opus-5-5 | The planner can stream: `PlanStreamingAsync` releases each beat once header + beats-so-far validate (the validator's own output), repairs only while nothing was released, and stops after the released beats on a mid-stream fault; `PlanAsync` unchanged; not wired into the shell yet | accepted |
| [DEC-040](#dec-040--2026-10-30--claude-opus-5-5) | 2026-10-30 | claude-opus-5-5 | Voice pre-roll: the microphone starts at the press and buffers until `session.ready`, then the buffer is sent in order; the socket stays per-utterance (DEC-013 Option F stands); configuration is checked before the microphone opens | accepted |
| [DEC-039](#dec-039--2026-10-30--claude-opus-5-5) | 2026-10-30 | claude-opus-5-5 | Element Selection offers up to 40 lines of screen text to Jev as "Text (OCR)" candidates (inside the target window, off every UIA control, at most 80 characters, none that look secret); the shell turns OCR on, `--no-ocr` turns it off | accepted |
| [DEC-038](#dec-038--2026-10-30--claude-opus-5-5) | 2026-10-30 | claude-opus-5-5 | The capture can read the cursor monitor's text with Windows' built-in OCR on the full-size frame (`ReadText`, off by default), into `MonitorCapture.TextLines` in global physical pixels; local only; nothing consumes it yet | accepted |
| [DEC-037](#dec-037--2026-10-30--claude-opus-5-5) | 2026-10-30 | claude-opus-5-5 | A found Element Selection choice in the Low tier is checked by Vision on the same frame (on by default, `CheckLowConfidence`); Vision replaces it only when Medium+ and elsewhere; a failed or unsure check keeps the choice. Supersedes DEC-010's rejection of Option C and DEC-009's "not a gate", for Low only | accepted |
| [DEC-036](#dec-036--2026-10-30--claude-opus-5-5) | 2026-10-30 | claude-opus-5-5 | The App Shell routes each turn with the Turn Router when `TYPESAFE_API_KEY` is set: point → highlight only, explain → Tutor, lesson → 2D or one 3D model; `--lesson` and the new `--no-route` keep the old behaviour; a router fault falls back; one shared Jev client from `JevClients.CreateTypeSafe` | accepted |
| [DEC-035](#dec-035--2026-10-30--claude-opus-5-5) | 2026-10-30 | claude-opus-5-5 | A Turn Router asks Jev once per turn: intent (point / explain / lesson), 2D or 3D, and which catalogue model; below 0.5 or an unoffered id is no route, an unsure canvas is a 2D lesson; own assembly over `IJevClient`, not wired yet | accepted |
| [DEC-034](#dec-034--2026-10-29--claude-opus-5-5) | 2026-10-29 | claude-opus-5-5 | Without WebGL the 3D lesson is drawn as an isometric SVG with Anatomy's vendored MIT kit (models, labels, highlight, explode, focus; animation and views as stills; no click-to-ask yet); long parts cut into pieces and buried pieces painted just before their container | accepted |
| [DEC-033](#dec-033--2026-10-29--claude-opus-5-5) | 2026-10-29 | claude-opus-5-5 | `--lesson` turns 3D on: the shell passes `CardSurface.Scene3dCatalog` and calls `RunWithFollowUpsAsync` (30 s click wait); Ctrl+Alt stays refused while a lesson waits, with a message that says how to end it; exit code = the last turn's (13 if cancelled while waiting) | accepted |
| [DEC-032](#dec-032--2026-10-29--claude-opus-5-5) | 2026-10-29 | claude-opus-5-5 | The planner offers 3D only when the request carries a catalogue: the same catalogue then validates the answer; schema 1.1, the 3D op shapes, limits, camera views and every model/part/animation id are interpolated from code; without one the prompt is byte-identical to v1 | accepted |
| [DEC-031](#dec-031--2026-10-29--claude-opus-5-5) | 2026-10-29 | claude-opus-5-5 | Click a 3D part to ask: picking only after a lesson has played; the page sends two ids, CardSurface checks them against what it drew and the catalogue; the question is written in C# from the Part Library; the follow-up plays on the same cards; at most 3, 60 s idle | accepted |
| [DEC-030](#dec-030--2026-10-29--claude-opus-5-5) | 2026-10-29 | claude-opus-5-5 | 3D renderer: a scene window in CardSurface (same isolation as cards) running vendored, SHA-pinned Babylon.js 9.30.0; rotations sent as C#-computed quaternions + an orientation self-test from real pixels; created on the first 3D lesson with a shader warm-up; render on demand; no-WebGL → speech | accepted |
| [DEC-029](#dec-029--2026-10-29--claude-opus-5-5) | 2026-10-29 | claude-opus-5-5 | Part library v1 as C# code in a new `HeyGanesh.PartLibrary`: closed primitive kit, computed geometry/motion where physics defines it, `Schematic` and `Reviewed` (false) flags, budgets 16 parts / 64 primitives / ±2 box; produces the scene3d `ModelCatalog` | accepted |
| [DEC-028](#dec-028--2026-10-29--claude-opus-5-5) | 2026-10-29 | claude-opus-5-5 | 3D as VisualPlan 1.1: one `scene3d` surface, ops model/label/explode/animate/camera + part refs, names bound to a caller-supplied `ModelCatalog`; off unless the canvas passes a catalogue (`ValidationContext.Scene3d`) | accepted |
| [DEC-027](#dec-027--2026-10-28--claude-opus-5-5) | 2026-10-28 | claude-opus-5-5 | App Shell lesson leg: `--lesson` runs one LessonTurn (new HeyGanesh.LessonSession) after a produced verdict; planner built from `DEEPSEEK_API_KEY` (DeepSeek via OpenAiCompat) or the leg reports its skip; WebView2 reaches the App transitively | accepted |
| [DEC-026](#dec-026--2026-10-28--claude-opus-5-5) | 2026-10-28 | claude-opus-5-5 | Lesson Planner v1: `VisualPlanner` in its own assembly over any `ILlmClient`; prompt contract interpolated from validator code; image only to Vision models; grounded marks; one repair turn keeping the better answer; no structured output and no streaming yet | accepted |
| [DEC-025](#dec-025--2026-10-28--claude-opus-5-5) | 2026-10-28 | claude-opus-5-5 | Lesson Surface v1: `CardSurface` (WebView2 cards on their own STA thread, text-only page, acked beats with timeout), seam `ClearAsync`→`ResetAsync(surfaces)`, plots computed by C# `PlotExpression` and refused by the validator when unparsable, TeX-lite + in-page strokes until KaTeX/Rough.js are vendored, marks as overlay box+pointer | accepted |
| [DEC-024](#dec-024--2026-10-28--claude-opus-5-5) | 2026-10-28 | claude-opus-5-5 | Lesson planner is provider-agnostic and multimodal: resolved through ProviderRegistry (no hard-wired provider), sends the question + one bounded screen crop as an image part when the model has Vision, otherwise an explicit text-only grounding path; refines DEC-021 | accepted |
| [DEC-022](#dec-022--2026-10-28--claude-opus-5-5) | 2026-10-28 | claude-opus-5-5 | Tutor session state: in-memory only — last validated plan + last 6 turns, cleared on idle/exit; no persistence until Notes (M8) | accepted |
| [DEC-021](#dec-021--2026-10-28--claude-opus-5-5) | 2026-10-28 | claude-opus-5-5 | Lesson planner calls models through Providers.Core `ILlmClient` (structured output + streaming already built), default DeepSeek `deepseek-flash` via OpenAiCompat; Tutor's DeepSeek client untouched | accepted |
| [DEC-020](#dec-020--2026-10-28--claude-opus-5-5) | 2026-10-28 | claude-opus-5-5 | Lesson rendering: WebView2 (`Microsoft.Web.WebView2` 1.0.4314.35, one NuGet in one new Experience assembly) as OPAQUE borderless no-activate capture-excluded card windows; screen marks stay on the native ScreenOverlay; SVG + vendored KaTeX/Rough.js; Babylon.js deferred to Phase 5; transparent full-screen surface rejected for now | accepted |
| [DEC-023](#dec-023--2026-10-28--claude-opus-5-5) | 2026-10-28 | claude-opus-5-5 | VisualPlan v1 contract: closed op set, hard limits, plan-wide ids with time-ordered references, never-throw parser; degrade bad beats to speech, ≥50% degraded or any plan-level issue to text; `HeyGanesh.VisualLessonEngine` references Abstractions only; LS-1..LS-7 | accepted |
| [DEC-017](#dec-017--2026-10-01--cline-agent-pixel-canary) | 2026-10-01 | Cline agent (Pixel Canary) | First live teaching provider: internal DeepSeekTutorClient (deepseek-flash, direct Chat Completions, DEEPSEEK_API_KEY at call time per DEC-007, fixed thinking:disabled) exposed only through TutorClientFactory.CreateDeepSeek → ITutorClient; verified offline through the exact public construction path via a loopback BaseUrl stub (no InternalsVisibleTo, no handler seam); TU-11/TU-12 | accepted |
| [DEC-016](#dec-016--2026-09-30--cline-agent-pixel-canary) | 2026-09-30 | Cline agent (Pixel Canary) | Teaching leg wired into the shell: non-Failed verdict maps to a TutorContext snapshot at the call edge (Failed short-circuits; no-match → Empty; Found carries the whole retained frame as a DownscaledFrame visual), the explanation is spoken verbatim behind a new `--no-speak`; seam composed by `CreateTutorClient()` which returns null — DEC-015's provider decision stays unmade | accepted |
| [DEC-015](#dec-015--2026-09-30--cline-agent-pixel-canary) | 2026-09-30 | Cline agent (Pixel Canary) | Tutor Engine as a provider-agnostic teaching layer: question-first prompt owned by the service, screen data as labelled untrusted input, opaque bounded JPEG visuals over a caller-supplied seam; no live client, no actuation, no disposal of the injected seam | accepted |
| [DEC-014](#dec-014--2026-09-30--cline-agent-model-id-not-exposed-to-the-agent) | 2026-09-30 | Cline agent (model id not exposed to the agent) | Push-to-talk shell wiring: press listens, release ends the utterance, committed transcript becomes the question; `--question` demoted to legacy/`--once`; `Failed` utterance → exit 1 | accepted |
| [DEC-013](#dec-013--2026-09-30--cline-agent-model-id-not-exposed-to-the-agent) | 2026-09-30 | Cline agent (model id not exposed to the agent) | Voice owns the integration boundary: Qwen Gateway GCP-7.0 client with cancel/discard suppression (no pre-generation suppression exists for live audio) + Fish TTS behind `ITtsClient` as WAV/44.1 kHz; external runtimes, not dependencies | accepted |
| [DEC-012](#dec-012--2026-09-29--pixel-canary) | 2026-09-29 | Pixel Canary | App Shell as a dependency-free console host that owns DPI awareness, the service graph and the hotkey→orchestrator→overlay wiring; WinUI 3 deferred again; no test doubles in product code | accepted |
| [DEC-011](#dec-011--2026-09-24--muse-spark) | 2026-09-24 | Muse Spark (muse-spark) | Screen Overlay as dependency-free Win32 (WinUI 3 deferred to the shell); explicit `WM_NCHITTEST → HTTRANSPARENT` + `WS_EX_NOACTIVATE` click-through; capture exclusion as a hard gate | accepted |
| [DEC-010](#dec-010--2026-09-23--cline-agent-claude-sonnet-45) | 2026-09-23 | Cline agent (Claude Sonnet 4.5) | Context Orchestrator composes ES→Vision behind one entry point with orchestrator-internal normalization; fallback on the failure signal only; trigger received via `HandleHotkeyPressedAsync` | accepted |
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
## DEC-054 — 2026-10-30 — claude-opus-5-5

**Decision — start at login is one per-user Run value, changed only by the menu.** Step 11c of the build sequence (the owner: "Yes, build 11c").

- **Where it lives:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `HeyGanesh`, holding the tray exe's path in quotes (a folder with spaces must still start the right file). Per user: no admin rights, no other account affected.
- **Who changes it:** only a click on "Start at login". Nothing is written at start or by default, which TS-2 checks.
- **The check mark** is read from the registry each time the menu opens. Three states:
  - On: the value points at this copy;
  - Off: no value;
  - Elsewhere: the value points at another copy, such as an older build folder. The item reads "(set for another copy)", and a click points it here.
- **Errors:** a registry refusal is a log line and a balloon, never a crash.
- **Code:** `StartupEntry` is the only class in the tray that touches the registry (TY-1). Its key path is injectable, so the rows use a throwaway `HKCU\Software\HeyGanesh\Test-<id>` key and only read the real one.

**Not handled, said instead.** Task Manager's "Startup apps" can switch the entry off without touching the value (it records that elsewhere, under `StartupApproved`). The menu would then show it checked while Windows skips it. Reading that undocumented key was left out; TS-3 says so.

**Rejected.**
- A shortcut in the Startup folder: it needs COM (`WScript.Shell`) to create, and is easier to break by moving files.
- A Scheduled Task: more privilege and harder to remove.
- MSIX's StartupTask: only for a packaged app, which is installer option B (11d).

Plan: [plans/2026-10-30-tray-installer-v1.md](plans/2026-10-30-tray-installer-v1.md). Rows: TS-1 (CI G-24), TS-2 (local), TS-3 (owner); TY-1 +1.

## DEC-053 — 2026-10-30 — claude-opus-5-5

**Decision — a key screen in the tray, writing only through the key ring.** Step 11b of the build sequence (the owner: "Yes, build 11b").

- **Rules, no UI:** `KeyScreenModel`. Save is `KeyRing.Store` (trimmed; empty and inner-whitespace keys refused with KeyRing's sentence, .NET's "(Parameter …)" suffix stripped). Remove is `KeyRing.Remove`, and it says when an environment variable still provides the key. A status is "missing" or `KeyState`'s "present (N chars, source)". No text it returns holds a value (TK-1).
- **Window:** `KeyScreen`, WinForms. One `UseSystemPasswordChar` box per key, with Save and Remove; nothing is written until Save, and the box is cleared at once. It is the tray's one window that takes focus, on purpose. What was typed lives in a .NET string until then, like any text box; it is handed only to `KeyRing.Store`.
- **When it opens:** from "Keys…", and by itself at start when DeepSeek or TypeSafe is missing (`KeyScreenModel.Required`). Fish is optional. This was plan decision 3. The owner approved building 11b as planned, so it follows the plan's recommendation.
- **The pause:** `ShellHost.RunAsync(args, stop, hookLive, triggerPaused)`, the last optional. In hooked mode a Ctrl+Alt press while it returns true prints `REFUSED: the key screen is open; …` and starts nothing. The reason: AltGr is Ctrl+Alt on many keyboards, so typing a key could otherwise start a listen. The tray passes "the key screen is open".

**Rejected.**
- A modal dialog: it would block the tray's menu, including Quit.
- Showing the last 4 characters: even a partial value is a value, and the API Key Isolation invariant has no exception for it.
- Pausing inside `TriggerPump`: the hotkey handler in `RunHookedAsync` is the one place a press enters, so the check sits there, before the pump.

**Owner rows.** TK-4: a real save and remove writes the real Credential Manager slot, which this session's guard keeps for the owner, like SH-25.

Plan: [plans/2026-10-30-tray-installer-v1.md](plans/2026-10-30-tray-installer-v1.md). Rows: TK-1 (CI G-24), TK-2, TK-3 (local), TK-4 (owner); TY-1 and TY-4 updated.

## DEC-052 — 2026-10-30 — claude-opus-5-5

**Decision — Hey Ganesh gets a tray host that runs the App Shell's own composition; the console exe stays as it is.** Step 11a of the build sequence (the owner: "Yes, build 11a").

- **Seam:** `ShellHost.RunAsync(args, stop, hookLive)`. In hooked mode, `stop` ends the wait the way Ctrl+C does (`stopToken.Register(stop.Set)`), so the teardown is the same code; `hookLive` is called once the hook is installed. `RunAsync(args)` passes neither, so every console row and exit code is unchanged. `InternalsVisibleTo` lets the tray and its host use the internal shell.
- **Host:** `src/Experience/HeyGanesh.Tray`, `OutputType=WinExe`, `UseWindowsForms` (a framework reference that ships with the Desktop runtime and LessonSurface already uses; no PackageReference). The shell runs on the pool, because hooked mode blocks its thread before any await; the icon's message loop owns the STA main thread. Every UI change goes through a hidden control's `BeginInvoke`.
- **Menu:** status, one line per key saying present or missing (not even a length), Open log folder, Quit. Keys… and Start at login are not there yet: 11b and 11c add them, so the menu has no item that does nothing.
- **Log:** the shell's console lines, unchanged, in `logs/shell-<stamp>.log` under the data folder; the newest 5 kept. The shell already prints keys only as "present (N chars, source)", and TY-5 checks the real exe with planted keys. Transcribed questions are logged as the console shows them. The owner has not decided this yet (plan decision 2), so the default is the console's behaviour.
- **One instance:** a `Local\` mutex; a second launch sets a `Local\` event, the first shows a balloon, and the second exits 3. The console exe does not take the mutex.
- **Console-only flags** (`--once`, `--help`, key commands) are refused with exit 2 and a log line: key entry reads hidden keystrokes, and a WinExe has no console to read them from.

**Rejected.**
- A `--tray` flag on the console exe: a console app always opens a console window.
- Raw `Shell_NotifyIcon`: no dependency either way, but hundreds of lines WinForms already has.
- WinUI 3: the reasons DEC-012 gives still hold.
- Building the pipeline again in the tray: the two would drift.

**Known gaps.**
- The icon is the generic application icon; there is no Hey Ganesh artwork in the repo yet.
- A shell that fails at start (exit 2, e.g. no capture support) shows a message box. That is the one place the tray takes focus, and only when it is about to exit.

Plan: [plans/2026-10-30-tray-installer-v1.md](plans/2026-10-30-tray-installer-v1.md). Rows: TY-1..TY-5 (TY-1/TY-2 in CI G-24), TY-6 manual.

## DEC-051 — 2026-10-30 — claude-opus-5-5

**Decision — "3D, but no listed model fits" becomes a schematic lesson when the caller can draw one.** Step 10d of the build sequence (the owner: "Yes, build 10d").

- **Router:** `RouteRequest.Schematics` (default false). With it, the model answer `none` at 0.5 or above, after a 3D answer at 0.5 or above, maps to `Dimension = Scene3d`, `Schematic = true`, no `ModelId`, with a reason. `none` is gated like any model answer, so an unsure `none` is still a 2D lesson. Only then do the 3D and `none` options tell Jev a simplified 3D drawing is made. Without the flag, every answer maps exactly as before (TR-3's old `none` check still passes unchanged).
- **Legs:** `TurnLegs.Choose` maps it to the new `LessonCanvas.Schematic`. A 3D route with no model id that is not a schematic still falls back to 2D.
- **Shell:** the route request sets `Schematics = true`. `CatalogFor` offers the whole catalogue for a schematic leg, so `RunLessonAsync` also hands over `Schematics.Check` (DEC-050). The planner's prompt already says to use a catalogue model when one fits, so a wrong "none" can still end up on a library model.

**Rejected.** Meaning "schematic" by `Scene3d` with a null `ModelId` and no flag: `TurnLegs` already treats that as a malformed route and draws 2D, and that guard is worth keeping. Always on, with no request flag: a router caller that cannot draw schematics would be sent lessons it cannot show.

Plan: [plans/2026-10-30-schematic-3d-v1.md](plans/2026-10-30-schematic-3d-v1.md). Rows: TR-2 (+1), TR-3 (+2), TR-6 (+1).

## DEC-050 — 2026-10-30 — claude-opus-5-5

**Decision — the cards draw a lesson's schematic like any model, and the shell asks for one whenever it offers 3D.** Step 10c of the build sequence (the owner: "Yes, build 10c").

- **Seam:** `ISchematicCanvas.UseSchematics` (VisualLessonEngine Runtime) is optional, like `IPartPicker`, so `ILessonCanvas` and every fake canvas are unchanged.
  - `LessonPlayer` calls it before each reset, with the validated plan's schematics on `PlayAsync` and with a new optional argument on `PlayStreamAsync`. A later lesson's call replaces an earlier one's.
  - `LessonTurn` passes the first released beat's header schematics on the streamed path.
- **Surface:** `CardSurface` converts them with `Schematics.ToModel` and resolves a `model` op from them before `ModelLibrary`. `SceneMessages.Model(op, model)` is the shared message, so `scene.js` and `scene-svg.js` draw it unchanged, with the existing "schematic" and "not yet reviewed" tags.
- **Shell:** `PlanRequest.SchematicGeometry = Schematics.Check` whenever `Scene3d` is offered (`--lesson`, or a route that chose 3D). The `--lesson` header line adds "or one schematic for anything else".

**Not done, deliberately: click-to-ask on a schematic.** `PartQuestion` writes a follow-up only from the Part Library's vetted names and descriptions (DEC-031: "never from anything the page or a model said"), and a schematic's part names come from the model. `CardSurface.OnPartClicked` checks the library catalogue only, so a click on a schematic part is counted and refused, and the 30 s wait for a click continues. Allowing it means relaxing DEC-031 (for example a question built from the part's model-written name, filtered and fenced as untrusted). That decision is the owner's.

**Changed from the plan while building.** The fixture's exploded-view camera is `top` instead of `side`: `side` looks straight down the fuselage, nose-on, and the labels piled up in the snapshot.

**Rejected.** Adding a schematics argument to `ILessonCanvas.ResetAsync` (it would change every canvas and fake for a feature only one canvas has).

Plan: [plans/2026-10-30-schematic-3d-v1.md](plans/2026-10-30-schematic-3d-v1.md). Rows: LSU-17 (local), LS-14 (+2), LT-10 (+1).

## DEC-049 — 2026-10-30 — claude-opus-5-5

**Decision — schematics are offered, and accepted, only where they can be drawn: a request that carries both the catalogue and the geometry check.** Step 10b of the build sequence (the owner: "Yes, build 10b").

- **Engine:** `ValidationContext.SchematicGeometry` is now also the caller's statement that its canvas draws schematics. Null refuses every schematic with `schematic.unavailable`, even one the model sends unasked. 10a accepted structurally-valid ones without a check; that would have let a schematic reach the shell's scene window before it can draw one (10c), which would throw.
- **Planner:**
  - `PlanRequest.SchematicGeometry` flows into the validation context;
  - with a catalogue and the check, `PlannerPrompt.System` asks for schema 1.2 and appends a SCHEMATICS block drawn from the code (shapes, finishes, every limit, the ±2 model space, what each size means), with three rules: catalogue models first, one beat must say it is a simplified drawing, not to scale, and no animations;
  - it also asks for the header before the beats, so streamed beats are checked against the schematic (the scanner's header is everything before `beats`);
  - without the check the prompt is unchanged character for character (LP-10 and LP-11 unchanged).
- **Eval:**
  - 3D cases pass `Schematics.Check`;
  - K-12: when the validated lesson draws a schematic, its speech must say simplified, schematic, not to scale or sketch;
  - E-04 gains a pass seed (the F-35B fixture) and a fail seed (the same lesson without that sentence).

**Measured.** The 3D prompt grows from 5,026 to 6,498 characters (about 370 tokens). Its effect on the first beat shows only in the live run (PE-6).

**Rejected.**
- Offering schematics whenever a catalogue is passed (the shell would then receive them before it can draw them).
- Scoring the drawing's quality offline (no honest offline measure; the live E-04 run and a human look are the measure).

**Consequences.**
- The shell does not pass the check yet, so it still never asks for or accepts a schematic: 10c (the scene window) and 10d (the router) turn it on.
- Any baseline recorded later must be re-recorded if this prompt changes again (the prompt-hash gate).

Plan: [plans/2026-10-30-schematic-3d-v1.md](plans/2026-10-30-schematic-3d-v1.md). Rows: LP-12 (CI G-14), LS-14 (updated), PE-2 (K-12).

## DEC-048 — 2026-10-30 — claude-opus-5-5

**Decision — a lesson may define one simplified 3D model of its own, a schematic, for an object the Part Library does not have.** Step 10a of the build sequence (the owner: "Yes, build 10a"). FEATURE-007 §3.2 already allowed free-form composition "rendered with a visible schematic tag"; this is its contract.

- **The contract** (schema `heyganesh.visualplan/1.2` = 1.1 plus a header field `schematics`):
  - each part is a list of shapes, `{ shape, size, at, turn, finish }`;
  - shapes are `box`, `cylinder`, `sphere`, `torus`, `capsule`, and finishes `metal`, `dark-metal`, `brass`, `glass`, both closed vocabularies;
  - limits: 1 per lesson, ≤ 12 parts, ≤ 40 shapes;
  - every number finite, centres in ±2, sizes in [0, 4], turns in ±360;
  - the id may not be a catalogue id.
- **Placing it:** a `model` op may place it, and label, highlight, explode and camera may name its parts; it has no animations.
- **Geometry:** checked by the caller's `ValidationContext.SchematicGeometry` delegate. The Part Library's `Schematics.Check` converts the schematic to a `LibraryModel` (`Schematic`, never `Reviewed`, basis "drawn by the lesson model for this lesson only: simplified, not to scale, not reviewed") and runs `LibraryCheck`. The engine still references nothing.
- **A refused schematic** gets a `schematic.*` plan-level issue and is left out. Unlike other plan-level issues it does not force TextFallback: only the beats that place or name it degrade (they fail `model.unknown` / `ref.target`).

**Changed from the plan while building.** The plan said sizes in (0, 2]; a size spans the shape, so in a ±2 box it may be up to 4 (the golden fixture's fuselage is 3.2 long). The rule is [0, 4], and `LibraryCheck` still refuses any placed shape that leaves the box, and any zero size a shape actually uses.

**Rejected.**
- More hand-built library models as the only answer (never enough; kept as the complement, driven by eval misses).
- Raw meshes or vertices from the model (unbounded, uncheckable, slow).
- Schematic animations (invented motion is the riskiest content).

**Consequences.**
- Nothing draws or plans a schematic yet: the planner prompt (10b), the scene window (10c) and the router (10d) follow.
- Without the collision rule a schematic named like a catalogue model would make the merged catalogue throw. The rule is what keeps the validator from ever throwing on model output here.

Plan: [plans/2026-10-30-schematic-3d-v1.md](plans/2026-10-30-schematic-3d-v1.md). Rows: LS-14 (CI G-13), PL-6 (CI G-16).

## DEC-047 — 2026-10-30 — claude-opus-5-5

**Decision — the App Shell's teaching leg uses the DeepSeek Tutor whenever there is a DeepSeek key, and the Tutor reads that key through the key ring.** Step 9 of the build sequence (SH-17). The owner chose "turn the Tutor on" as a must-have, and option A for TU-8: "Option A, build it". The provider stays as DEC-017 chose it: DeepSeek `deepseek-flash`, the lesson planner's key.

- **Shell:** `CreateTutorClient()` returns `TutorClientFactory.CreateDeepSeek(new DeepSeekTutorOptions { ApiKey = Keys.Reader("deepseek") })` when `HasKey("deepseek")`, and null otherwise.
  - With no key the leg prints "tutor : skipped — no DeepSeek key …" and the verdict's exit code stands, the same gating as the lesson leg (DEC-027).
  - The header's `tutor :` line comes from `TutorHeader()`, in step with that gate.
- **TutorEngine:** `DeepSeekTutorOptions.ApiKey` (`Func<string?>?`) is called per request; null keeps the `DEEPSEEK_API_KEY` read; a source returning nothing is a `TutorAuthenticationException` before any byte is sent.
- **TU-8 amended (DEC-017's "no credential surface" rule):** `DeepSeekTutorOptions` may carry exactly one key-named property, `ApiKey`, and only typed `Func<string?>`.
  - It is matched by name and type, and TU-8 now also asserts it, so a stored key (any string or bytes named Key/Secret/Credential) or a handler still fails.
  - Proven by adding a stored `ApiKeyValue` string beside the source: TU-8 failed.
- **TU-11** gains the two key-source checks.

**Rejected.**
- Option B, keeping TU-8 and leaving the Tutor environment-only: a key stored through the app (and the step 11 first-run screen) would give lessons but no prose answers.
- Copying a stored key into the process environment at startup: every child process (the WebView2 cards included) would inherit it.
- Routing "explain" turns to a 2D lesson instead: a spoken prose answer is the faster, cheaper reply to "what does this mean?".

**Consequences.**
- With a DeepSeek key, every turn that does not draw a lesson makes one DeepSeek call. A real spoken answer end to end (SH-17) is live-gated.
- Without the Fish key the spoken half fails after the printed lesson, with exit 12 (the existing SH-17 mapping).

Plan: [plans/2026-10-30-tutor-on-v1.md](plans/2026-10-30-tutor-on-v1.md). Rows: TU-8, TU-11, SH-27; SH-17 (live).

## DEC-046 — 2026-10-30 — claude-opus-5-5

**Decision — every API client the App Shell composes reads its key through the key ring, per request.** Step 8b of the build sequence (the owner: "Yes, build 8b"). A key stored with `--set-key` is now used.

- **Element Selection, Vision, Voice:**
  - `SelectionOptions`, `VisionOptions` and `FishTtsOptions` gain `Func<string?>? ApiKey`;
  - `TypeSafeJevClient`, `DeepSeekVisionClient` and `FishTtsClient` call it per request when given, and otherwise read their environment variable exactly as before;
  - a source returning null or blank is a missing key (the same authentication fault, before any byte is sent);
  - no module gains a project reference.
- **App Shell:**
  - one static `KeyRing` (`Keys`) hands `Keys.Reader(name)` to the Jev client, Vision and a Fish client it now builds itself (declared before `VoiceService`, so disposed after it);
  - the lesson model's `ISecretStore` is `KeyRingSecretStore` (it replaced `EnvironmentSecretStore`);
  - which legs run (router, lesson) is decided by `HasKey` (Credential Manager or environment);
  - the header's `keys :` line prints `KeyState` ("present (N chars, source)" or "MISSING").
- **PlannerEval:** `--record` reads through the same ring (`RingKeyStore`), so a stored key works for it; PE-4 uses a ring over an empty store and a dummy environment, so it reads nothing real; PE-5 checks the shell's `new KeyRingSecretStore(Keys, "deepseek")`.
- **Missing-key messages** now end "(The app reads keys through its key ring, DEC-046: HeyGanesh.App --set-key stores one.)".

**Changed from the plan while building.** The Tutor (`DeepSeekTutorOptions`) is left out. TU-8 deliberately refuses any option whose name contains "Key", "Secret", "Credential" or "Handler" (DEC-017: no credential surface, no transport knob), and the shell does not use the Tutor yet (`CreateTutorClient()` returns null). Whether TU-8 should allow a key *source* (a delegate, never a value) is a decision for step 9, which turns the Tutor on. Until then the Tutor reads `DEEPSEEK_API_KEY` only.

**Rejected.**
- Renaming the Tutor's option to dodge TU-8's name check: that would defeat the check instead of deciding it.
- Passing the key ring object itself into the modules (TutorEngine and Voice reference nothing).

**Consequences.**
- With a key stored in Credential Manager and no environment variable, the shell's legs run and authenticate. This is live-gated: no key here.
- The Tutor stays environment-only until step 9.
- The environment fallback is kept, so CI and every documented command still work.

Plan: [plans/2026-10-30-security-proxy-v1.md](plans/2026-10-30-security-proxy-v1.md). Rows: ES-10, V-9, VO-8 (key-source checks), PE-4/PE-5 (updated), CI G-23.

## DEC-045 — 2026-10-30 — claude-opus-5-5

**Decision — the app's provider keys come from one key ring, held in Windows Credential Manager, with the environment variable as a fallback.** Step 8a of the build sequence (the owner: "Yes, build 8a"). It ends DEC-007's "temporary stopgap … pending the API key security proxy" once 8b routes the clients through it.

- **`HeyGanesh.Security.KeyRing`:**
  - knows `deepseek` (`DEEPSEEK_API_KEY`), `typesafe` (`TYPESAFE_API_KEY`) and `fish` (`FISH_AUDIO_API_KEY`);
  - reads Credential Manager first (target `HeyGanesh:<name>:api-key`, the existing `CredentialManagerSecretStore`), then the variable;
  - `Describe` returns presence, length and source, never a value (`KeyState.ToString` is the only report);
  - `Reader(name)` is a per-request delegate that caches nothing;
  - `Store` trims, and refuses an empty key or one with a space, line break or control character inside;
  - a Credential Manager failure falls back to the environment and says "[Credential Manager unavailable]".
- **App Shell key commands:**
  - `--keys` shows each key's state;
  - `--set-key <name>` reads the key with nothing echoed (or one line of piped stdin) and stores it;
  - `--remove-key <name>` deletes it;
  - exit codes: 0 done, 1 nothing entered or refused, 2 unknown name, 4 Credential Manager refused.
- **The header:** the `keys :` line keeps its environment-based text (it still decides which legs run), and adds "(stored in Credential Manager; the clients read it from build step 8b)" when a key is stored but not in the environment.

**Rejected.**
- A separate proxy process that injects the key into requests: a second process to supervise and a localhost port to lock down, for risks (a key in logs, screenshots, files, history) that Credential Manager plus presence-only reporting already cover. It can sit behind the same delegates later.
- A new `HeyGanesh.SecurityProxy` assembly: `HeyGanesh.Security` already holds the store.
- Giving the clients an `ISecretStore`: TutorEngine and Voice deliberately reference nothing. 8b passes delegates instead.

**Consequences.**
- Until 8b the API clients read only the environment, and the `--keys` note and the header say so.
- `--assert-keyring-cm` and SecretSmoke's self-test write throwaway credentials, in CI as well.
- Storing a real key is the owner's action: `--set-key <name>`.

Plan: [plans/2026-10-30-security-proxy-v1.md](plans/2026-10-30-security-proxy-v1.md). Rows: KR-1, KR-2 (CI G-12), SH-24, SH-25 (owner).

## DEC-044 — 2026-10-30 — claude-opus-5-5

**Decision — the evaluation set records live once and replays offline forever; a reviewed recording becomes the baseline CI gates on.** Step 7b of the build sequence (the owner: "For now build all systems with API keys... I will add them later").

- **Record** (`--record [dir] [--cases ...]`, live):
  - the shell's DeepSeek client (`LiveClient`: `https://api.deepseek.com`, `deepseek-flash`, `Text | Streaming`, `DEEPSEEK_API_KEY`); PE-5 fails if it stops matching `ShellHost.Lesson.cs`;
  - `RecordingLlmClient` keeps every answer as it streams;
  - writes `cases/<id>.json`, `summary.json` and `report.txt`, which hold checks, first-beat p50/p90 against 2.5 s, library misses, tokens when reported, and a SHA-256 of the planner prompt;
  - the key is described as "present (N chars)" only.
- **Exit codes:**
  - no key → exit 12 before any client exists;
  - an auth error stops the run (12), and the cases so far are written;
  - another provider fault is recorded on its case and the run continues;
  - Ctrl+C → 13.
- **Replay** (`--replay <dir> [--baseline <dir>]`, offline):
  - recorded answers go back through the current planner and checks;
  - exit 1 when a check that passed in the baseline fails now, or the total drops;
  - **exit 3 when the prompt hash changed** (frozen answers say nothing about a new prompt; re-record);
  - cases with a provider error or a cut-short stream keep their recorded result.
- **The shared case loop:** `EvalRun.RunOneAsync` (PE-2 now uses it too; its output is unchanged).
- **CI:** G-22 adds PE-4 and PE-5. It replays `tools/PlannerEval/baseline` once the owner commits one, and says "no baseline yet" until then.

**Choices taken without an explicit answer.** The owner was asked "fail or warn on a prompt change" and replied "build all systems", so I took the recommended **fail** (exit 3). Switching to a warning is one line in `RunReplayAsync`.

**Rejected.**
- A `--base-url` flag (it would send the key wherever it is told).
- Moving the shell's client factory into a product assembly now (it would change a product module's dependencies; PE-5 guards drift until the Security Proxy replaces both).
- Several runs per case (the report says "one run" instead).

**Consequences.**
- Scores, timings and the baseline exist only after the owner's first live run (PE-6).
- The client sends no `stream_options`, so DeepSeek reports no tokens in streams: the report says "not reported".
- A follow-up whose previous case could not be replayed is scored without its previous lesson.

Plan: [plans/2026-10-30-planner-eval-v1.md](plans/2026-10-30-planner-eval-v1.md). Rows: PE-4, PE-5 (CI G-22), PE-6 (live, owner), PE-7.

## DEC-043 — 2026-10-30 — claude-opus-5-5

**Decision — lessons are measured on a fixed set of 30 student questions with automatic checks, in a host of their own.** Step 7a of the build sequence (the owner: "Yes, build 7a"). Before this, the planner rows proved the machinery on four golden lessons and said nothing about whether real lessons are usable.

- **The set** (`tools/PlannerEval/eval-set-v1.json`):
  - E-01..E-06 are the turns of the HeyClicky "introducing real-time graphics" video (2026-10-29), verbatim from its transcript with the wake phrase removed: a tensor taught from the screen, a ChatGPT follow-up, "can an airplane hover?", "draw the jet in 3D", a Raspberry Pi, attaching a camera;
  - 24 more across math, physics, chemistry, biology, CS and engineering, including one screen-injection case;
  - screen text is written for the set, never captured; four cases are answered by the golden lessons.
- **The checks** (`EvalChecks.cs`, pure). Thresholds sit at the top of the file with their reasons.
  - K-1: valid, with ≤ 25 % degraded beats.
  - K-2: 3–8 beats of ≤ 45 words.
  - K-3: required terms said.
  - K-4: listed misconceptions absent.
  - K-5: the expected canvas and library model.
  - K-6: no invented model, part or animation (and a library miss is reported).
  - K-7: marks only with a grounded target, and at least one when there is one.
  - K-8: teaches from the screen text.
  - K-9: the injected word absent.
  - K-10: a fact check opens with yes / no / partly.
  - K-11: a follow-up was sent the previous lesson and does not repeat it.
- **Proof offline:** PE-2 runs the goldens and `seeds.json` (answers built to pass, or to fail exactly one check) through the real `VisualPlanner.PlanStreamingAsync` with the recording fake model. Turning any one check off makes PE-2 fail.

**Changed from the plan while building.**
- K-6 applies to every case offered the 3D catalogue, not only E-04, and K-5 to every case that expects a canvas: an invented model is a miss wherever it happens.
- The host is not added to `HeyGanesh.sln` (no tool host is).
- ARCHITECTURE is unchanged (no product module changed).

**Rejected.**
- Rows inside LessonPlannerSmoke: that host proves fixed machinery; this set grows to ≥ 100 and gets a baseline.
- An LLM judge now: a second key and a non-deterministic gate.
- Hand-written "ideal" answers for all 30: they would measure my writing, not the model.

**Consequences.**
- The checks catch broken, off-topic, ungrounded and manipulated lessons, not subtle factual errors.
- Real scores need step 7b (`--record` with `DEEPSEEK_API_KEY`, `--replay`, and a committed baseline that G-22 then gates on).
- The 24 non-video questions and their terms are my wording, for the owner to review (PE-3).

**Revised after the owner's review (2026-10-30 · claude-opus-5-5, the owner: "Yes, apply all 8").**
1. K-4 no longer counts a misconception named in order to correct it: a listed phrase counts only in a sentence with no correcting cue ("myth", "not true", "wrong", "isn't", "doesn't", "don't", "can't", "never", …). Bare "no"/"not" are deliberately not cues.
2. Terms that failed correct answers were widened (M-05 "three out of four", M-02 "two times x").
3. Terms that proved nothing were replaced (G-02 now needs a small current controlling a larger one; P-05 deflection plus pressure/reaction; E-04 "thrust/downward" instead of "down").
4. E-06 now requires a power-off step instead of listing "while it's on" phrases.
5. P-02 is replaced by U-01, "What score did I get on last week's quiz?": the lesson must say it cannot know, and `EvalCase.MinBeats` lets K-2 accept one or two beats there.
6. E-06 (not E-05: attaching the camera is where pointing at the board matters) has a grounded target, so its lesson must mark the connector.
7. A "2d" case is never offered the 3D catalogue; it scores the planner, not the decision to draw in 3D (a Turn Router evaluation).
8. The file header says E-01's slide numbers are a recollection of the lecture, not read from the video.

New seeds prove fixes 1 and 5 (P-01 myth corrected → pass; U-01 abstains in 2 beats → pass; U-01 guesses → fails K-3 only); undoing either fix fails PE-2.

Plan: [plans/2026-10-30-planner-eval-v1.md](plans/2026-10-30-planner-eval-v1.md). Rows: PE-1, PE-2 (CI G-22), PE-3 (owner review, done).

## DEC-042 — 2026-10-30 — claude-opus-5-5

**Decision — a lesson turn opens the cards on the first released beat and plays each beat while the rest of the lesson is still being written.** Step 6b of the build sequence (the owner: "Yes, build 6b"), on top of DEC-041.

- **Player:** `LessonPlayer.PlayStreamAsync(surfaces, beats, outcome, ct)` plays an async sequence of already-validated beats. `PlayAsync` now runs through the same frame and beat loop (its play rows are unchanged: 5, 5, 11, 6, 22).
- **Turn:** `LessonTurn` always plans through `PlanStreamingAsync`; with a model that cannot stream that is exactly `PlanAsync` and the old whole-plan path runs. When a beat is released first, `PlayStreamedTurnAsync` opens the cards (or reuses the follow-up lease), plays from a queue the planner fills, and, when planning ends, queues any beats of the finished plan that were not released before closing it.
  - a planner `StreamNote` (dropped stream, or a finished plan that disowns a shown beat) → the turn ends `Stopped` after the shown beats, the note in `Error`;
  - caller cancellation → `Cancelled`, the stream stopped, cards closed; a fault while playing → planning stopped, cards closed, the fault rethrown (as before);
  - `LessonTurnReport.FirstBeatShown`: from the start of the turn to the first beat starting on the cards.
- **App Shell:** the DeepSeek client advertises `Text | Streaming` (`OpenAiCompatLlmClient.StreamAsync` reads server-sent events as they arrive); `PrintLesson` adds `streamed :` (beats released while writing) and `first beat :` (the student's wait, the line the 2.5 s target is measured on).
- **Measured offline (fake model, 200 ms to the first token, 2,000 characters/s):** first beat drawn at 741 ms, the lesson finished arriving at 1,972 ms. A mutation that always waits for the whole plan moves the first beat to 1,995 ms and fails LT-10.

**Rejected.**
- A separate streamed turn API: a non-streaming model already degrades to the old path inside the same call, so the shell needs no new switch.
- Opening the cards before the first beat validates: a lesson that turns out unusable would flash an empty window.

**Consequences.**
- Live timing is still unmeasured (needs `DEEPSEEK_API_KEY`); SH-19/SH-20 will show `first beat :`.
- Pause/Skip/Stop controls are not reachable through `LessonTurn` (it creates its player internally), so a student Stop mid-stream is handled in code but not exercised by a row.

Plan: [plans/2026-10-30-lesson-streaming-v1.md](plans/2026-10-30-lesson-streaming-v1.md). Rows: LT-10 (CI G-15).

## DEC-041 — 2026-10-30 — claude-opus-5-5

**Decision — the Lesson Planner can stream a lesson and release each beat as soon as it is safe to teach.** Step 6a of the build sequence (the owner: "Yes, build 6a"); 6b plays the released beats.

- **Why:** the whole VisualPlan used to be generated, validated and only then drawn. On DeepSeek V4.1 Flash's published figures (about 1.1 s to the first token, 206 tokens/s) a 600–1,200-token lesson is roughly 4–7 s before anything appears, against a 2.5 s target.
- **`PlanStreamScanner`** (pure): fed chunks, it tracks depth, strings and escapes, finds the header (the top-level object before the `"beats"` key) and each complete beat object. The same text split anywhere yields the same beats.
- **`VisualPlanner.PlanStreamingAsync(request, onBeat, ct)`:** asks exactly as `PlanAsync` asks (shared `Prepare`). For each new beat, "header + beats so far" goes through the existing parser and validator; a beat is released only when that prefix is not Rejected and kept every beat. A released beat is the validator's own output — validation is never skipped, it runs beat by beat.
  - nothing released before the header and the first beat validate;
  - nothing released by the end → the finished text becomes the first attempt of the usual repair loop (`RunAttemptsAsync`, shared with `PlanAsync`);
  - something released → no repair; the finished plan is validated and must contain the released beats (fingerprints compared), otherwise the lesson stops after them;
  - a provider fault after a release returns the released beats with `StreamNote`; before one it propagates; cancellation always propagates;
  - a client without `ProviderCapability.Streaming` gets exactly `PlanAsync`.
- **`PlanResult`** gains `ReleasedBeats`, `FirstBeatLatency` and `StreamNote` (optional, so every existing construction compiles). `StreamedBeat` carries the beat, its index, the validated prefix and the time.
- **The prompt is unchanged.** Its FORMAT block already puts `beats` last, and the 2D prompt is meant to stay character for character (DEC-032), so the planned "header first" sentence was not needed.
- **LP-1's source audit** used the needle "Stream" to keep I/O out; the planner now calls `ILlmClient.StreamAsync`, so the needle names the I/O types (`System.IO`, `FileStream`, `MemoryStream`, `NetworkStream`, `StreamReader`, `StreamWriter`) instead. LP-1 still passes 3/3.
- **Simulated, not measured:** with a fake at 1.1 s to the first token and 824 characters/s, the photosynthesis lesson's first beat was released at 2.41 s and the whole lesson arrived at 6.50 s.

**Rejected.**
- Playing beats before validating them: it skips what the validator exists for (grounded marks, vetted 3D parts).
- A short first call then a second for the rest: two round trips and two halves that may not join up.
- A streaming JSON library: a NuGet dependency.
- Fixing `ResilientLlmClient.StreamAsync` (it buffers the whole stream before yielding): it is not on the shell's path; logged as an open item.

**Consequences.**
- Nothing in the App Shell changes yet; `PlanAsync` is still what it calls.
- Once a beat is shown, a broken later beat can no longer be repaired; the validator degrades it (speech only) or the lesson stops after the shown beats.

Plan: [plans/2026-10-30-lesson-streaming-v1.md](plans/2026-10-30-lesson-streaming-v1.md). Rows: LP-11 (CI G-14).

## DEC-040 — 2026-10-30 — claude-opus-5-5

**Decision — the speech-in leg pre-rolls: the microphone starts at the Ctrl+Alt press and its frames wait in the existing bounded queue until the Gateway's `session.ready`, then go out in order.** Step 5 of the build sequence, changed from "keep the connection warm" before building, because DEC-013 already rejected holding the socket open (Option F: it keeps the operator's own Gateway clients evicted).

- **The bug it fixes, shown first:** VO-11 on the old client, against a stub that answers `session.ready` after 600 ms, got first frame #7. About 700 ms of speech after the press never reached the Gateway, because the microphone only started after the handshake and health check. After the change the first frame is #0, and the stub turn finished in about 1.22 s instead of 1.91 s.
- **Order at the press:** the endpoint is resolved first (missing or malformed fails at once, not retried, and the microphone is never opened); then the microphone starts; then the connect loop runs as before. The queue (100 × 100 ms, drop-oldest) belongs to the whole listen and survives reconnect attempts; the send pump drains it per attempt and is stopped, not completed, when the attempt ends.
- **Limits:** the utterance cap counts from the press; the silence window starts at `session.ready`, so a slow handshake is never mistaken for a silent user.
- **Teardown:** however the listen ends, the microphone is stopped and anything still queued is dropped. Nothing leaves the process before `session.ready`.
- **Measured:** `UtteranceResult.ConnectDuration` (press to ready) and `BufferedFrames`; the shell prints them as `connect :` under the utterance line.
- **Unchanged:** one socket per utterance, the release rule (only what has committed is kept), every protocol message.

**Rejected.**
- A warm socket between presses: DEC-013 Option F, plus unverified idle cost of an upstream realtime session.
- Shorter timeouts: they fail sooner, they do not connect sooner.
- Running the health check alongside the handshake: a separate, smaller saving; recorded as an open item.

**Consequences.**
- The microphone opens slightly earlier, at the press. If connecting fails, it is stopped and the queue discarded (VO-11).
- A real Gateway receives the pre-roll as a burst faster than real time; whether its speech detection handles that is unverified until a live run.
- Found while building, not fixed here: the classifier reads the ready payload's version from `protocolVersion`/`protocol`, but upstream's schema names it `protocol_version`, so against a real Gateway the wire-version check may record "not stated" instead of checking it. Logged in HANDOVER.

Plan: [plans/2026-10-30-voice-preroll-v1.md](plans/2026-10-30-voice-preroll-v1.md). Rows: VO-11 (CI G-21), VO-L2 (live).

## DEC-039 — 2026-10-30 — claude-opus-5-5

**Decision — Element Selection offers Jev the screen text the capture read (DEC-038) as extra candidates, next to the UIA controls.** Step 4b of the build sequence; the owner approved it as planned ("Yes, build 4b").

- **The merge** is `TextCandidates.FromScreenText`, a public pure function (ES-8). A line is offered only when its centre is inside the target window and not on a UIA control already offered, it is at most 80 characters after spacing is normalised, and it does not look like a secret (`sk-`/`pk-`/`rk-`, `gh?_`, `AKIA`, `xox?-`, JWT shapes, or any 24+ character run mixing letters and digits). At most 40 lines, in reading order; the same text at the same box is offered once.
- **Candidates:** type "Text (OCR)", id assigned after ranking like any other, box = the line's box, position hint computed as for controls (`ComputePositionHint` is now internal). They are ranked with the UIA candidates under the same `MaxCandidates` cap. When any is offered, the Jev instruction says they are read text, not controls.
- **API:** a `SelectAsync(question, target, screenText, ct)` overload; the old signature passes none. `SelectionResult.ScreenTextOffered` counts them. The Context Orchestrator passes `captures[0].TextLines` (the cursor's monitor).
- **Consequence for the fallback:** a window with no UIA controls but readable text now gets a Jev choice instead of an immediate Vision fallback (the "no interactive candidates" failure now also requires no usable text).
- **The App Shell** sets `ReadText` on; `--no-ocr` turns it off. The header's `ocr :` line states it and the OCR language.
- **Privacy:** this is the first time screen text, not just control names, goes to TypeSafe. The window rule, the 80-character limit, the 40-line cap and the secret filter bound it; `--no-ocr` removes it. OCR itself stays local.
- **Measured here:** the window under the cursor had 3 UIA controls; OCR read 111 lines in 150 ms and 40 became candidates (ES-9).

**Rejected.**
- Offering every OCR line: paragraphs and other windows' text would crowd the cap and send far more to the cloud.
- Redacting only the secret part of a line: the rest of the line would still describe it.
- A separate Jev question for text: one question over one ranked list keeps a single choice and a single hallucination guard.

**Consequences.**
- The secret filter is a pattern list; it can miss secrets that look like ordinary words. `--no-ocr` is the hard stop.
- Whether Jev chooses text candidates well is unmeasured until a live run (keys).

Plan: [plans/2026-10-30-screen-text-v1.md](plans/2026-10-30-screen-text-v1.md). Rows: ES-8 (CI G-20), ES-9 (local), SH-23.

## DEC-038 — 2026-10-30 — claude-opus-5-5

**Decision — the Screen Capture module can read the text on the cursor's monitor with `Windows.Media.Ocr`, on the full-size frame, before it is downscaled.** Step 4a of the build sequence; step 4b offers the lines to Jev as Element Selection candidates.

- **Why in the capture:** a spike on this machine (2560×1600) read 110 lines / 391 words at full size and 68 lines / 329 words at the orchestrator's 1280-px size, before JPEG loss. The full-size frame exists only inside the capture, so that is where the text is read.
- **What:** `ScreenTextReader` (public, WinRT, no NuGet) runs the recogniser from the user's profile languages and returns each line with its box (the union of its words) in global physical pixels: frame pixels plus the monitor's top-left. `ScreenCaptureOptions.ReadText` (default **off**) turns it on; `CaptureAllMonitorsAsync` reads the first capture only (the cursor's monitor), `CaptureMonitorAsync` reads the monitor it was asked for.
- **Shared types:** `ScreenTextLine(Text, Box)`, and `MonitorCapture` gains init-only `TextLines`, `TextNote` (why there is no text: no language, too large, a failure) and `TextDuration`. Every existing construction compiles unchanged.
- **Never fails a capture:** no OCR language, a frame over 10,000 px, or a recogniser error becomes a note. Cancellation propagates.
- **Local only:** nothing in this step sends the text anywhere. Step 4b will put some of it into the Jev request, with caps and a secret filter, and is planned separately.
- **Measured:** the fixture's three lines read exactly in 20 ms; a live read found 110 lines in 141 ms.

**Rejected.**
- OCR on the 1280-px JPEG: about 40% of lines lost.
- A second, full-size capture from Element Selection: two captures per turn, and Element Selection would depend on screen capture.
- Handing the full-size bitmap across the shared types: a WinRT object of about 16 MB in records that hold plain data.
- Reading every monitor: about 135 ms each for text the user is not looking at.

**Consequences.**
- Rows SC-9 and SC-10 need an OCR language and stay local-only; CI's Windows image is not known to have one.
- Only one monitor was available here, so "other monitors are not read" was checked vacuously.

Plan: [plans/2026-10-30-screen-text-v1.md](plans/2026-10-30-screen-text-v1.md). Rows: SC-9, SC-10.

## DEC-037 — 2026-10-30 — claude-opus-5-5

**Decision — when Element Selection finds an element but its own score is in the Low tier, the Context Orchestrator asks Vision for a second opinion on the same frame.**

- **Supersedes, for the Low tier only:** DEC-010's rejection of Option C ("gate on confidence too") and DEC-009's "ConfidenceLevel stays classification here, not a gate". The fallback on a *failed* result is unchanged. Medium and High are still accepted as-is, because DEC-010's point stands: nothing measured says a Medium choice is worse than a pixel model's. The owner approved this on 2026-10-30 ("Yes, on by default").
- **When:** `ContextOrchestratorOptions.CheckLowConfidence` (default on) and `LowConfidenceCheck.Applies`: not failed, found, Low (Element Selection's blended score below 0.5). The honest no-match is never checked.
- **The rule** is `LowConfidenceCheck.Decide`, a public pure function (CO-12):
  - Vision's centre inside Element Selection's box → "Vision agrees", Element Selection's box kept (it is the exact control);
  - Vision found it elsewhere at Medium or High → Vision's verdict;
  - Vision found it elsewhere at Low, not found, failed, or threw → Element Selection's choice kept.
- **Errors:** the check catches Vision's provider errors (`VisionClientException`, including no `DEEPSEEK_API_KEY`), because a failed second opinion must not lose the first answer. This is the one place the orchestrator does not pass a leg's error up; the fallback still does. Cancellation propagates.
- **Visible:** `GuidanceResult.ConfidenceCheck` (Abstractions) says what happened; the App Shell prints it as `check :`, and the orchestrator's phase reads `CheckingConfidence` while it runs.
- **Where:** the orchestrator, because it is the only module that holds both legs (DEC-008). It reads the tier Element Selection already computed; it sets no threshold of its own.

**Rejected.**
- The check inside Element Selection: the siblings never reference each other (DEC-008).
- A Jev component or a second Jev call: it does not hold Vision, and Jev's numbers are already in the tier.
- Checking Medium, or running Vision on every turn: far more screenshots sent to the cloud with nothing measured to justify it.
- Blending the two legs' scores: a scale neither module defines.

**Consequences.**
- A Low turn sends one screenshot to DeepSeek and waits for one Vision call, as a fallback does. The `check :` line shows each one.
- Whether the check improves real answers is unmeasured; it needs keys and the model shoot-out.

Plan: [plans/2026-10-30-low-confidence-check-v1.md](plans/2026-10-30-low-confidence-check-v1.md). Rows: CO-12 (CI G-19), CO-13 (local).

## DEC-036 — 2026-10-30 — claude-opus-5-5

**Decision — the App Shell asks the Turn Router (DEC-035) how to teach each turn, and runs the leg it picks.**

- **When:** the router is composed only with `TYPESAFE_API_KEY` set and neither `--lesson` nor `--no-route`. It starts right after the verdict is printed, so it runs while the highlight is on screen; `TeachAsync` waits for it. A Failed verdict is never routed (nothing is taught from it).
- **The leg** is `TurnLegs.Choose(route, --lesson, --no-route)`, a pure function in the TurnRouter assembly so every case is checked offline (TR-6):
  - `--lesson`: a lesson on the full catalogue, the router never asked (the old behaviour);
  - `--no-route`, no router, a failed router, or "no route": the Tutor leg (the old default);
  - point: nothing more, the highlight was the answer, exit 0;
  - explain: the Tutor leg (still clientless until SH-17's flip, so it prints its skip);
  - lesson: 2D (the planner is offered no 3D) or 3D on the one model Jev picked (a one-model catalogue).
- **A router fault never fails the turn.** Auth, rate-limit, provider and bad-request errors print a `route : FAILED` line and fall back; cancellation while routing exits 13.
- **One Jev client.** `TypeSafeJevClient` stays internal; Element Selection gains `JevClients.CreateTypeSafe(SelectionOptions?)` returning an `OwnedJevClient`. The shell builds one, gives the same instance to `ElementSelectionService` and the router, and disposes it after the service.
- **The lesson seam** is composed whenever a router exists, since a route can choose a lesson; the lesson header says "when the route chooses one".
- **Header:** a `route :` line states ON, or why it is off (`--lesson`, `--no-route`, key MISSING), presence only.

**Rejected.**
- Routing before the locate step: the route would lose "a match was found", and lessons need the grounding anyway.
- Routing after the highlight ends: it would add Jev's whole wait to every turn.
- Making `TypeSafeJevClient` public, or a second TypeSafe client in the shell.
- Letting `--lesson` also take the router's model choice: the flag keeps exactly what SH-18..SH-20 verified.

**Consequences.**
- With keys set and no flag, a turn can now end at the highlight or become a lesson; before, it always went to the (clientless) Tutor. `--no-route` restores that.
- The shell's application of a route is only provable live (SH-22): the shell carries no fakes (DEC-012). TR-L1 is still open.

Plan: [plans/2026-10-30-turn-router-shell-v1.md](plans/2026-10-30-turn-router-shell-v1.md). Rows: TR-6, SH-21, SH-22 (live).

## DEC-035 — 2026-10-30 — claude-opus-5-5

**Decision — a Turn Router decides how each turn is taught, with one Jev call that answers up to three typed choices.**

- **Where:** new assembly `src/Intelligence/HeyGanesh.TurnRouter/` (`TurnRouter`, `TurnRoute`, `TurnRouterOptions`). Its only reference is Element Selection, for `IJevClient` and its records (DEC-008). It reads no key and opens nothing: the caller hands it a client, so the Security Proxy can later stand behind the same seam.
- **The questions:** `intent` (point / explain / lesson); when the caller offers models, also `dimension` (2d / 3d) and `model` (the offered ids plus `none`, each described by title and subject). Every instruction quotes the user's question, because question ids are never shown to Jev.
- **Facts as state:** window title, whether the locate leg found a match ("found", "not found" or "not checked"), the question's word count and the model count. Computed in code, never asked. This is typesafe-computer-use's rule: the classifier picks, code decides facts.
- **Mapping:**
  - an intent, dimension or model id that was never offered refuses the whole route, as Element Selection refuses an unknown candidate;
  - an intent scored below 0.5 is no route; the caller keeps its behaviour without a router (today, `--lesson`);
  - for a lesson, every unsure step toward 3D (dimension below 0.5, model `none`, model below 0.5) gives a 2D lesson with a reason, because the lesson was wanted and only the canvas was in doubt;
  - the score is Jev's confidence, else the chosen option's probability, else 0.
- **0.5** is the Medium tier boundary Element Selection and Vision already share (DEC-009). `TurnRouterOptions.MinScore` moves it.
- **Provider faults and cancellation pass through unchanged.** The caller decides what a failed router means.

**Rejected.**
- Putting the router in the Context Orchestrator: it would tie teaching choices to the locate module, against DEC-010's narrow job for it.
- Three separate Jev calls: three round trips for what one request answers.
- Letting a lesson's model choice fall back to a guessed id: only offered ids can come out.

**Consequences.**
- Nothing calls the router yet. Wiring it into the App Shell is step 2 of the build sequence.
- Not verified: that TypeSafe accepts three choice questions in one request. Element Selection already sends two, and typesafe-computer-use sends three; a live row (TR-L1) checks it once `TYPESAFE_API_KEY` exists.

Plan: [plans/2026-10-30-turn-router-v1.md](plans/2026-10-30-turn-router-v1.md). Rows: TR-1..TR-5 (CI G-18), TR-L1 live.

## DEC-034 — 2026-10-29 — claude-opus-5-5

**Decision — when WebGL is missing, the scene page draws the lesson's 3D models as an isometric SVG with Anatomy's kit, instead of showing only a notice.**

- **Vendored:** `Page/vendor/anatomy-1482e846/` holds `iso-kit.mjs`, `lathe.mjs`, `iso.css` and `LICENSE` (MIT, commit `1482e846`). They are the spike 0d downloads, byte-identical and pinned in `VENDOR.md`; LSU-7 re-hashes them and checks the licence ships.
- **Module loading:** WebView2's virtual host serves `.mjs` as JavaScript (checked first: the page's `ready` now waits for the module, and every scene row passed), so no renaming was needed.
- **The renderer** is `Page/scene-svg.js`, an ES module. `scene.js` keeps the drawing's state in its no-WebGL branch and redraws after every message, acking `{ drawing: true }`. The WebGL branch is untouched.
- **Ops in the drawing:**
  - drawn: `model`, `label`, `highlight` (orange outline), `explode` (redrawn at the amount), `clear`, and `camera` focus (re-framed);
  - shown as stills: `animate` draws the rest pose plus an "animation needs 3D" tag, and every `camera` view is drawn isometric;
  - `picking` stays `skipped`, so `PickAsync` returns null and there's no click-to-ask on the drawing yet.
- **Axis map:** (x, y, z) → (x, z, y). The kit's projection reflects, so a non-reflecting map would mirror every model. The orientation probe runs on the drawing too: same three markers, front view (azimuth 90°, elevation 0°), found by colour in real pixels.
- **Painter's order (spike 0d's condition 1)**, built in three passes over what the pictures showed:
  1. Cylinders and boxes longer than 2.5× their width are cut into pieces about one width long. Cylinder pieces are open bands, with caps only at the true ends. Box pieces get their own outline with the cut edges dropped.
  2. A piece whose centre lies inside another solid is painted just before that solid's own settled order. Containers nest: the pin is in the piston, which is in the glass cylinder.
  3. Ties fall back to the item's own depth.

  The first picture had stripes and a pin showing through the piston; the second hid the pin but drew the rod over the piston; the third matches Babylon.
- **Lesson text never enters an SVG string:** labels are DOM nodes with `textContent`. LSU-7 audits that, and LSU-16 checks a `<b>` label stays literal.

**Measured (this machine; LSU-12, LSU-16).**

| Measure | Result |
|---|---|
| Engine | 74 solids, 11–14 ms |
| DNA | 220 solids, 33–49 ms |
| Pump | 11 solids, 16–17 ms |
| CSP violations | 0 |
| Scene window cold start | 1.89–2.05 s, unchanged from DEC-030's 1.95–2.14 s |

**Rejected.**
- Porting the kit to C#.
- Using the drawing for every lesson, which would lose orbit and motion.
- A full separating-plane sort: buried parts have no correct whole-part order anyway.
- Pre-rendered PNGs, which can't show the lesson's labels or highlights.
- `bandsOf`, the kit's band splitter: its one outline over a whole body would paint the buried part of a stem over the head.

**Consequences.**
- Faint tick marks remain inside the shaded faces of cut parts: the rod, the crankshaft, some DNA bases. They're cosmetic and recorded.
- Box-against-box overlaps without containment aren't specially handled. The three V1 models show none, but a new model could.
- Click-to-ask, real views, animation and printable notes from the drawing are each a later step.

Plan: [plans/2026-10-29-svg-fallback-v1.md](plans/2026-10-29-svg-fallback-v1.md). Rows: LSU-7, LSU-12, LSU-16 (LSU-13 gains the drawing PNGs).

## DEC-033 — 2026-10-29 — claude-opus-5-5

**Decision — the App Shell's `--lesson` leg offers the planner the cards' 3D catalogue and runs click-to-ask follow-ups. A new Ctrl+Alt press is still refused while a finished 3D lesson waits for a click.**

- `TeachWithLessonAsync` builds the `PlanRequest` with `Scene3d = CardSurface.Scene3dCatalog`, so the prompt offers 3D (DEC-032) and the validator accepts it. It calls `LessonTurn.RunWithFollowUpsAsync` with `IdleTimeout` 30 s (`LessonPickIdle`). The session default is 60 s.
- **Output:** each turn is printed with `PrintLesson`. A follow-up is preceded by `follow-up N : the student clicked '<part>' on '<model>'`, and the leg ends with `follow-ups : N, ended <FollowUpEnd>`.
- **Exit code:** the last turn's outcome, mapped as before. The loop stops at the first turn that didn't play to the end, so that turn is the one that ended it. The exception is `FollowUpEnd.Cancelled` (cancelled while waiting for a click), which exits 13 as a cancel mid-lesson does.
- **Header:** with a key, the lesson line adds `3D: 3 models, click a part to ask`. The no-key line is unchanged.
- **Ctrl+Alt during the wait (owner's choice: "Refuse (simple)"):** refused as today. The REFUSED line now adds "If a 3D lesson is waiting for a click, close it with its x button or wait up to 30 s." No new shell state.

**Reasoning.** Refusing keeps the shell's one-turn-at-a-time rule (SH-9) and adds no turn phase that the shell, which carries no fakes (DEC-012), couldn't test offline. 30 s is long enough to look and click, and short enough not to block the next spoken question for long.

**Rejected.**
- A press that closes the waiting lesson and starts the new voice turn. Better for demos, but it needs a new phase in the shell's turn state machine. It's a later change if the wait gets in the way.
- Keeping `RunAsync` for 2D-only lessons: `RunWithFollowUpsAsync` already ends at once (`NoScene`) when nothing 3D was drawn.

**Consequences.**
- `--once --lesson` with a 3D lesson keeps the process alive up to 30 s after the lesson for a click.
- The live path needs `TYPESAFE_API_KEY`, `DEEPSEEK_API_KEY`, the Gateway, a mic and Fish (SH-20, human-gated). Offline, only the header and skip paths can be checked.

Plan: [plans/2026-10-29-planner-3d-v1.md](plans/2026-10-29-planner-3d-v1.md) (6b). Rows: SH-18 (updated), SH-20.

## DEC-032 — 2026-10-29 — claude-opus-5-5

**Decision — `PlannerPrompt.System(limits, scene3d)` adds a 3D section exactly when `PlanRequest.Scene3d` is set, built from the same `ModelCatalog` the validator checks the answer against.**

- **Without a catalogue** the system prompt is the v1 prompt, byte for byte. Checked by dumping it from HEAD and from the change: 2935 bytes each, identical. A 2D-only caller is never told about schema 1.1 or the 3D ops.
- **With a catalogue** the FORMAT names `heyganesh.visualplan/1.1`, and a section follows the screen rules. It contains:
  - the `scene3d` surface, with `MaxScenes`;
  - the six 3D op shapes (`model`, `label`, `highlight` on `instance.part`, `explode`, `animate`, `camera`);
  - `MaxModelsPerScene`;
  - the camera views from `CameraView`;
  - a CATALOGUE block: every model id with its part and animation ids, sorted so the text is stable;
  - five 3D rules: a scene only for a catalogue model; place before naming; words stay on cards; `explode` needs a `camera` after it; `say` still explains.
- `VisualPlanner` passes `request.Scene3d` to both the prompt and `ValidationContext`. The repair turn already carries `model.unknown` / `part.unknown` / `animation.unknown` back to the model.
- **Size:** +2101 characters for the 3-model V1 catalogue (2925 → 5026), only when 3D is on.

**Reasoning.** The rule this file already follows: ask for exactly what will be accepted. Names come from code, so the prompt can't offer an id or op shape the validator refuses. LP-10 checks every id and parses every shown op line.

**Rejected.**
- Part titles and descriptions in the catalogue block: that changes `CatalogModel` (DEC-028 keeps it names-only) and makes the prompt about 3× longer. The ids are readable English. Revisit if a live run mislabels parts.
- Always describing 3D, which would give a 2D-only canvas plans that all degrade.
- A separate 3D planner: one lesson mixes a scene and cards.

**Consequences.**
- No product caller passes a catalogue yet. 6b (the App Shell) is the first.
- Whether a real model plans good 3D lessons is unknown until a live run (LP-8 needs `DEEPSEEK_API_KEY`).

Plan: [plans/2026-10-29-planner-3d-v1.md](plans/2026-10-29-planner-3d-v1.md). Row: LP-10.

## DEC-031 — 2026-10-29 — claude-opus-5-5

**Decision — after a 3D lesson has played, the student can click a part; the click becomes a follow-up lesson about that part, on the same window.**

- **When.** `LessonTurn.RunWithFollowUpsAsync` asks for a pick only once a lesson that drew a `scene3d` surface has played to the end (`Played`). Clicks during playback do nothing. The page shows "Click a part to ask about it" only while a pick is wanted.
- **The seam.** `IPartPicker.PickAsync(idle, ct)` in VisualLessonEngine.Runtime, optional on an `ILessonCanvas`. `CardSurface` implements it. Null means no click: the student closed the lesson, the idle time passed, or there was no scene with a model on show (including no WebGL).
- **What crosses the page boundary.** On a click the page posts `{ kind: "part-clicked", instance, part }`: two ids and nothing else (LSU-7 audits that). `CardSurface.OnPartClicked` accepts them only while a pick is in progress, only for an instance this surface drew, and only for a part the catalogue lists for that instance's model. Anything else is counted in `RefusedPicks` and dropped. `ProbePickGuardAsync` is the self-test.
- **What is clicked.** Lesson meshes are pickable and tagged with their instance and part; warm-up and orientation meshes are not. A part seen through glass or a membrane (finish alpha < 0.5) wins over the see-through surface in front. A see-through part is picked only where nothing solid is behind it.
- **The question.** `PartQuestion.For(pick)` writes it from the Part Library's own name, title and description: "Tell me more about the connecting rod in the single-cylinder four-stroke engine. The part library describes it as: …". It goes to the planner with the lesson just shown as `PlanRequest.PreviousPlan` (the existing DEC-022 path) and with no screen grounding, so a follow-up can never mark the student's screen.
- **Same cards.** The follow-up is reset and played on the lease the first lesson opened. The lease is opened at most once and closed on every path. `RunAsync` keeps its old behaviour and is what the App Shell still calls.
- **Bounds.** `FollowUpOptions`: `MaxFollowUps` 3 and `IdleTimeout` 60 s. `FollowUpEnd` says why the loop stopped.
- **Planner pass-through.** `PlanRequest.Scene3d` (a `ModelCatalog?`) now reaches `ValidationContext.Scene3d`. Without it the planner refuses every 3D plan, so the loop could not be tested. The prompt does not teach the 3D ops yet, and no caller in the product passes a catalogue (step 6).

**Measured (this machine; LSU-15, LT-8).**
- A real click on the connecting rod comes back as `PartPick(m, four-stroke-engine, connecting-rod)` in 128–153 ms.
- 6–8 of the engine's 10 parts have a steady point to click from the iso view. The glass cylinder, the piston pin (inside the piston) and, with the 2 px steadiness check, the thin valve stems have none.
- Forged clicks: 3 of 3 refused. The close button ends a wait in under 140 ms.

**Reasoning.**
- Ids checked against what was drawn keep the follow-up question C#-authored. Page text can't become model input, which is the same rule as every other page message.
- Reusing the window avoids a second ~2 s scene start-up (DEC-030) on every click.
- Picking only after playback avoids racing the speaker; the player has no stop-then-replan path.

**Rejected.**
- Clicks during playback that interrupt the lesson (later: stop, then pick).
- A new window per follow-up.
- Sending the part's name from the page.
- Running the loop in the App Shell, which carries no fakes (DEC-012), so the loop couldn't be tested offline.

**Consequences.**
- Thin parts (valve stems) are hard to hit at this zoom. A screen-space tolerance or a larger hit volume is a later refinement.
- LSU-15 moves the real cursor. It needs a desktop nobody is using: a hand on the mouse is reported as interference rather than as a wrong part.
- The App Shell can't reach any of this until step 6 teaches the planner the 3D ops and the shell calls `RunWithFollowUpsAsync` with `CardSurface.Scene3dCatalog`.

Plan: [plans/2026-10-29-part-pick-v1.md](plans/2026-10-29-part-pick-v1.md). Rows: LT-8, LT-9, LSU-15 (and LSU-7, LSU-12 extended).

## DEC-030 — 2026-10-29 — claude-opus-5-5

**Decision — 3D lessons render in one more CardSurface window, built from Part Library data by vendored Babylon.js, with the rotation convention fixed in C# and proven from pixels.**

- **The window.** `CardSurface` (the real `ILessonCanvas`, DEC-025) gets a scene window for a lesson's `scene3d` surface (DEC-028). It's the same `CardWindow`: borderless, NOACTIVATE + TOOLWINDOW + TOPMOST, `WDA_EXCLUDEFROMCAPTURE`, same virtual host, navigation and new-window blocking, no dev tools or accelerator keys. It hosts `Page/scene.html`, whose CSP is identical to `lesson.html`'s (no `unsafe-eval`, no `unsafe-inline`, no network).
- **The renderer.** `scene.js` builds each model from `SceneMessages`, which resolves geometry from `ModelLibrary` (DEC-029):
  - per part, an explode node, then a pose node, then the meshes;
  - labels are Babylon GUI notes on leader lines, with text drawn on a canvas, never as markup;
  - `HighlightLayer` for highlights, and `AnimationGroup`s from the library's keys.
- **Rotations.** They leave C# as quaternions computed with `Geometry.Rotate`'s convention: q = qY·qX·qZ, with rotation keys densified to ≤ 45°. The page never interprets Euler angles. `CardSurface.CheckOrientationAsync()` draws markers at +X, at +Y, and on a parent turned 90° about Z, then finds them by colour in the captured page.
- **Vendored files.** Babylon.js 9.30.0 and babylon.gui 9.30.0 plus the Apache-2.0 licence live under `Page/vendor/`, SHA-256-pinned in `VENDOR.md` (all equal to jsdelivr's published hashes). The folder is `-text` in `.gitattributes`.
- **Created on demand.** The scene window is created on the first lesson that declares a scene, with a shader warm-up (one hidden mesh per shape × finish), and lives as long as its surface.
- **Render on demand.** Frames are drawn only while a tween, an animation or a pending op is active.
- **No WebGL.** The page says so and acknowledges every scene op as skipped; the lesson completes in speech.
- **Catalogue.** `CardSurface.Scene3dCatalog` is the catalogue a caller passes as `ValidationContext.Scene3d`. No caller passes it yet (step 6).

**Measured (this machine, RTX 3060 Laptop + WARP-capable; LSU-9..LSU-12).**

| Measure | Result |
|---|---|
| Scene window cold start (environment page + Babylon parse + warm-up) | 1.95–2.14 s across 5 runs |
| Warm model beat (engine: meshes + shaders ready + first frame) | 114–120 ms, inside the 300 ms budget |
| Frames while `stroke-cycle` loops | 168 in 1 s |
| Frames idle after the non-looping `pump-cycle` | 0 in 2 s |
| Orientation (+X right, +Y up, Z-turn maps +Y → −X) | correct from real pixels |
| CSP violations on the scene page | 0 |
| `--disable-webgl` | the six-beat lesson completes, every sentence spoken |

Memory was not re-measured in this step. Spike 0b's 357–506 MB WebView2 private memory for a 3D page stands.

**Reasoning.**
- Euler order and handedness are where a renderer silently disagrees with the library while every C# check passes. Quaternions from C# plus a pixel check remove that class of bug. DEC-029 asked for exactly this confirmation.
- Creating the scene window only for a 3D lesson keeps 2D lessons at their ~143 MB, and keeps every run from paying 360–500 MB for a lesson type the planner can't produce yet. The cost is about 2 s of `ResetAsync` before a 3D lesson's first beat, paid per 3D turn while `CardSurfaceHost` makes a fresh surface each turn.

**Rejected.**
- Sending Euler angles to Babylon.
- A transparent full-screen 3D layer (DEC-020 again).
- 3D inside the card page: every card would carry 8.6 MB of Babylon and GPU memory.
- Pre-warming at app start: deferred, see Consequences.
- A tree-shaken ES-module build: it needs npm and a bundler, ruled out by FEATURE-007 §1.5.

**Consequences.**
- About 9.3 MB of vendored JavaScript is now in the repo and its history.
- *(2026-10-29 · claude-opus-5-5 update: `NOTICE.md` is now vendored and pinned in `VENDOR.md`; LSU-7 checks it ships.)* `babylonjs@9.30.0`'s 46-byte `NOTICE.md` is **not yet vendored**. It wasn't in the approved download, and Apache-2.0 §4(d) asks redistributions to include it, so it must be added before this branch is pushed or shared.
- `explode` moves parts but does not re-frame the camera. A lesson that wants the whole exploded view adds a `camera` op.
- Making a 3D lesson's first beat fast is a later change: keep one `CardSurface` across turns, or pre-warm at app start.
- Step 5 (click a part to ask) adds input to this window.
- Step 6 makes `VisualPlanner` pass `Scene3dCatalog`, which is when live lessons can first contain 3D.

Plan: [plans/2026-10-29-scene-renderer-v1.md](plans/2026-10-29-scene-renderer-v1.md). Rows: LSU-7..LSU-14.

## DEC-029 — 2026-10-29 — claude-opus-5-5

**Decision — 3D teaching models are C# code in their own assembly, computed wherever physics defines the shape or motion, and honestly flagged.** New Intelligence assembly `HeyGanesh.PartLibrary` (only reference: VisualLessonEngine). A model is named parts built from a closed primitive kit (box, cylinder, sphere, torus, capsule) with a closed set of finishes, a one-line description per part, an explode offset, and keyframed position/rotation tracks. `ModelLibrary.V1` ships three: `four-stroke-engine` (slider-crank kinematics computed at 49 keys), `dna-helix` (B-DNA: 10.5 bp/turn, 0.34 nm rise, 2.0 nm diameter), and `na-k-pump` (`Schematic = true`; only the 3 Na⁺ out / 2 K⁺ in stoichiometry is a fact). Every model is `Reviewed = false` until the owner reads `PartLibrarySmoke --dump` and records it here. `LibraryCheck` holds each model to 16 parts, 64 primitives (three per scene stay under spike 0b's 200), a ±2 box, and ordered, rest-matching, loop-consistent tracks. `ModelLibrary.ToCatalog()` is the `ModelCatalog` the scene3d contract validates against (DEC-028). One rotation convention (`Geometry.Rotate`: Z, then X, then Y, fixed axes, right-hand rule) is used by the models and the checks alike.

**Reasoning.**
- FEATURE-007 §3.1 "compute, don't recall": the host checks the shipped tracks against the physics: the rod is exactly L long at all 49 keys, the valves only open in their strokes and never reach the crown, every DNA pair is A-T or G-C at the right rise and twist, and the ions end on the right side. A deliberate sign flip in the rod formula fails that row (worst error 0.4).
- §3.2 "bind to vetted content": names live in one reviewable place. Saying "vetted" when no person has read them would be false, so the flag says otherwise.
- The library depends on the contract, not the reverse (DEC-028), so content can grow without touching the validator.

**Rejected.** glTF assets (binary, poorly reviewable, motion not checkable against physics; kept for organic shapes later); JSON data files (no compile-time checking, a second parser, the computed models still need code); putting the models in VisualLessonEngine; shipping ten models now.

**Consequences.** Step 4's renderer must reproduce `Geometry.Rotate`'s convention and confirm it with a snapshot of each model; it is also the first caller to pass `ModelLibrary.ToCatalog()` as `ValidationContext.Scene3d`. Organic models (heart, eye) will need a different source. The DNA model draws the strands 180° apart, so no major/minor groove, and says so in its basis. The engine has no valve overlap or timing advance, also stated. Plan: [plans/2026-10-29-part-library-v1.md](plans/2026-10-29-part-library-v1.md). Rows: PL-1..PL-5.

## DEC-028 — 2026-10-29 — claude-opus-5-5

**Decision — 3D arrives as VisualPlan 1.1: one `scene3d` surface, five ops, names bound to a caller-supplied `ModelCatalog`, and off by default.** Schema `heyganesh.visualplan/1.1` is v1 plus `SurfaceKind.Scene3d` (at most one per plan) and the ops `model` (place catalogue model → instance), `label` (sticky note on an instance or part), `explode` (0..1), `animate` (a catalogue animation of that model) and `camera` (`front`/`side`/`top`/`iso`). `highlight` also takes a part reference `"<instance>.<part>"`. The validator accepts `/1` and `/1.1`; a scene under `/1` is `schema.unsupported`. 3D is accepted only when `ValidationContext.Scene3d` carries a catalogue. With none (every caller today), a scene is plan-level `surface.unsupported` and the lesson is spoken. New codes: `surface.unsupported`, `ref.surface-kind`, `model.unknown`, `part.unknown`, `animation.unknown`, `limit.scenes`, `limit.models` (`MaxModelsPerScene = 3`).

**Reasoning.**
- DEC-023 already said 3D comes as a schema minor and that ops grow by contract, not by renderer. Nothing in v1 changes meaning, so every v1 fixture and the planner prompt stay valid untouched (all 106 existing LessonSmoke PASS lines unchanged).
- FEATURE-007 §3.2 "bind to vetted content": a scene can only name models, parts and animations a catalogue lists, so a model cannot invent a part. `part.unknown` is a separate code from `ref.target` so evals can tell "made up a part" from "referred to it too early".
- The gate is needed now: `CardSurface` lays out one 2D card per declared surface whatever its kind and silently skips op types it doesn't know, so a scene that passed validation would show as an empty card. The catalogue doubles as the capability flag. A host passes one only when its canvas draws 3D (Phase 5 step 4).
- One scene per plan: spike 0b measured ~360–500 MB of WebView2 memory per 3D page.

**Rejected.** A separate scene document beside the beats (loses beat-by-beat sync with speech, needs a second validator); a static global model list like `LessonVocabulary.Glyphs` (couples contract to content, and cannot say "this host has no 3D"); schema `/2`; parameters on `model` (deferred until a library model or eval needs them).

**Consequences.** Step 3's part library produces a `ModelCatalog` plus geometry; step 4's renderer is the first canvas to pass one; step 6 teaches the planner the 1.1 ops and makes `VisualPlanner` pass the catalogue. Until then live lessons are exactly as before. The `ModelCatalog` constructor throws on malformed ids or duplicates, since it is built by code, never from model output. Plan: [plans/2026-10-29-scene3d-contract-v1.md](plans/2026-10-29-scene3d-contract-v1.md). Rows: LS-13.

## DEC-027 — 2026-10-28 — claude-opus-5-5

**Decision — the lesson leg is wired into the App Shell behind `--lesson`, and its model comes from the environment.** After a produced (non-Failed) verdict, `--lesson` replaces the Tutor's prose leg with one lesson turn: the shell maps the verdict to `ScreenGrounding` (Found → description, resolved box, `Captures[0]` JPEG; honest no-match → no grounding, so every screen mark is refused) and calls `HeyGanesh.LessonSession.LessonTurn.RunAsync` (plan → open cards → play → close) with Voice's `SpeakAsync` as the speaker through `DelegateLessonSpeaker`. `CreateLessonPlanner()` builds `VisualPlanner.FromRegistry` over DeepSeek (`deepseek-flash`, OpenAI-compatible adapter, `DEEPSEEK_API_KEY` read per request by a read-only `EnvironmentSecretStore`) when the key is present and returns null otherwise, in which case the leg prints an honest skip. Exit codes reuse the shell alphabet: 0 played/skip, 1 no usable lesson (Rejected), 12 key, 3 provider/speech, 13 cancelled, 4 shell or card defect.

**Accompanying choices.**
- **New assembly `src/Experience/HeyGanesh.LessonSession/`** (refs LessonPlanner + LessonSurface + VisualLessonEngine): the turn logic lives where a verification host can reach it, because the shell carries no fakes (DEC-012). It references neither Voice nor ContextOrchestrator (LT-1), so DEC-015/016 hold: the shell is still the only verdict→teaching mapper and Voice still knows only text.
- **Cards open only after a plan exists** (`ILessonCanvasHost`): a rejected plan or a provider fault costs no window and no WebView2 start-up; the cards are closed on every path once opened.
- **Provider faults and cancellation become outcomes; everything else propagates** (speaker faults are mapped by the shell, card timeouts are a defect). The report's error string is `ProviderException.ToString()`, which carries no message body.
- **The App gains a transitive NuGet package** (WebView2, through LessonSession → LessonSurface — the exception DEC-020/025 already accepted). The App csproj still has no direct `PackageReference` (SH-2) and carries the same `DropWebView2Wpf` target.
- **`--no-speak` with `--lesson`** keeps speech as the clock without the voice: each sentence holds its beat for ~60 ms per character (0.6–8 s).

**Reasoning.** The owner asked that the first real lesson work the moment a key is added, with no code change: that rules out the Tutor's deliberately-untaken flip (DEC-017) for this leg. DeepSeek is the provider the repo already commits to (DEC-017); it is text-only, so the planner sends no image and says so (DEC-024), and a Vision provider is one registry assignment away.

**Rejected.** A `--fake-lesson` flag on the App (breaks DEC-012: product code would ship a test double). Putting the turn directly in the App (no host can reach it — the whole turn would stay untested until a key exists, the SH-15/17 gap). Letting the Tutor call the planner (breaks DEC-015). Mapping `GuidanceResult` inside LessonSession (breaks DEC-016 and needs a ContextOrchestrator reference). Returning null like `CreateTutorClient` (safer on paper, but needs another code change before the first demo — the owner chose the env path).

**Consequences.** The App now builds only where `src/Providers` and `src/Foundation` exist — both still untracked (HANDOVER open item). The full lesson turn is offline-verified by LT-1..LT-6 (fake model; fake and real cards); the shell side is verified up to the key gates (SH-18). SH-19 (a real chord → verdict → drawn, spoken lesson) needs TYPESAFE_API_KEY, DEEPSEEK_API_KEY, a Gateway, Fish and a human.

## DEC-026 — 2026-10-28 — claude-opus-5-5

**Decision — lessons are planned by `HeyGanesh.LessonPlanner.VisualPlanner`.** `PlanAsync(PlanRequest)` sends one request through the `ILlmClient` it was given (by the caller or `FromRegistry` → role Tutor; DEC-021/DEC-024), validates the answer with `ValidationContext { RequireGroundedMarks = true, GroundedTarget = screen box }`, and — if the answer is not Valid — makes exactly one repair call carrying the original question, the first answer and the issue list. The better of the two outcomes is returned (Valid > PartiallyDegraded > TextFallback > Rejected; ties go to the repair). The prompt contract (schema, op shapes, limits, glyph vocabulary, plot grammar) is interpolated from the same code the validator uses. The screen image is attached only when the client advertises `ProviderCapability.Vision` and the JPEG is within `MaxImageBytes`; otherwise the prompt says it is not attached and `PlanResult.ImageNote` records why.

**Grounded marks (new validator rule).** `ValidationContext` lets a caller require that every mark sits inside the grounded target box (+24 px); with no box, marks are refused (`mark.ungrounded`). A model therefore cannot invent coordinates on the student's real screen — it can only point where Element Selection / Vision already found something. The desktop-only `Validate` overload keeps its behaviour for fixtures.

**Reasoning.** Interpolating the contract from code removes a whole class of drift bugs (prompt promises one vocabulary, validator enforces another). One repair turn recovers the common "nearly right" answer for one extra call; a second would double cost for diminishing returns. Grounded marks are the cheapest strong anti-hallucination rule available for screen pointing.

**Rejected.** Structured output (`LlmResponseFormat.JsonSchema`): the Providers adapters disagree on its shape (OpenAI-compatible wraps it as `{name, schema}`, Anthropic sends it raw) and DeepSeek may not support it — fixing the adapters is its own change. Streaming beats: the first-beat latency target cannot be measured without a key (spike 0c), so it waits for one. Putting the planner inside VisualLessonEngine: that module's audit pins Abstractions as its only reference, and the planner needs Providers.Core.

**Final-review fixes (same day).** Untrusted text (screen description, previous lesson, and model-authored values echoed in the repair turn) is flattened to one line, stripped of fence markers and bounded, and sits between `<<<BEGIN …>>>` / `<<<END …>>>` lines — it can no longer forge a `QUESTION` or `GROUNDED TARGET BOX` line. A repair call that fails with `ProviderException` keeps the usable first lesson (`PlanResult.RepairError`). `FromRegistry` planners own and dispose the client the registry built (`VisualPlanner : IDisposable`). Prompt examples are valid as written (non-zero mark) and state truthfully that an unknown op costs the whole beat's drawing.

**Consequences.** Every model the registry can assign is usable unchanged. Repair doubles worst-case cost per lesson (2 calls). Until the adapter mismatch is fixed, JSON adherence rests on the prompt + tolerant parser + repair. Live quality is unmeasured until a key exists (LP-8, eval harness).

## DEC-025 — 2026-10-28 — claude-opus-5-5

**Decision — the first real lesson canvas.** `HeyGanesh.LessonSurface.CardSurface` implements `ILessonCanvas` (DEC-020): one STA UI thread with a WinForms loop; one shared WebView2 environment (profile under `%LOCALAPPDATA%/HeyGanesh/webview2` or `$HEYGANESH_DATA_DIR/webview2`) with one card pre-warmed; one card window per declared surface, laid out top-right on the monitor under the cursor, each capture-excluded before first show (hard error otherwise). The page (`LessonPage/lesson.html|css|js`, virtual host `lesson.heyganesh.invalid`, CSP `default-src 'self'; connect-src 'none'`) builds DOM/SVG from beat messages through `textContent` only and acknowledges each one; an unacknowledged beat fails after `ApplyTimeout` (3 s). Navigation off the virtual host is cancelled and new windows refused. Accompanying choices:
- **Seam change:** `ILessonCanvas.ClearAsync(ct)` → `ResetAsync(IReadOnlyList<LessonSurface> surfaces, ct)`; a beat does not carry its card's id/title, and the cards must exist before beat 1. `LessonPlayer` passes `plan.Surfaces`.
- **Plots computed by the app:** `PlotExpression` (closed grammar, implicit multiplication, depth/length caps) evaluates the model's expression in C#; the validator now refuses an unparsable expression (`value.invalid`), so the beat degrades before rendering. The page only draws host-sent points.
- **No new downloads this phase:** math uses an in-page TeX-lite formatter (fractions, scripts, roots, matrices, arrows, Greek) and strokes are jittered in-page; KaTeX/Rough.js (DEC-020) can replace both later without touching C#. Fonts are Windows' own (Ink Free / Segoe Print).
- **Marks:** every mark shape is shown as the existing native overlay's box + pointer; a new mark hides the previous one first (the overlay refuses overlap).
- **Final-review fixes:** exponent notation (`2e3`) is refused instead of read as 2·e·3; finite far outliers become plot gaps so `tan(x)` is not flattened; beats and layout messages have separate budgets (`ApplyTimeout` 3 s, `ControlTimeout` 10 s) and a timeout names the message kind; card height shrinks so every row fits the working area; browser accelerator keys, pinch zoom and swipe navigation are off on cards.
- **Diagnostics in product code, read-only:** `DescribeCardAsync`, `ProbeIsolationAsync` (also usable as a startup health check) and `CapturePageAsync` (the page itself, not the screen).

**Reasoning.** Opaque WebView2 cards were proven by spike 0a; computing plots in C# is FEATURE-007 §3.1's highest-value accuracy rule; deferring KaTeX keeps the phase free of downloads beyond the already-approved package while the visible demo works today. Reviewing page snapshots found and fixed four pedagogy-affecting layout faults (full-width rings, stub arrows between stacked items, a clipped plot, and a marker band that singled out a matrix's bottom row).

**Rejected.** Letting the page evaluate expressions (moves the accuracy guarantee into JavaScript and widens the page's attack surface); `innerHTML`-based rendering (model text becomes markup); one window for all cards (couples layout and loses per-card dragging); downloading KaTeX/Rough.js now (the owner delegated "quicker and cheaper", and the TeX-lite formatter covers the fixtures).

**Consequences.** `HeyGanesh.LessonSurface` is the only assembly with a PackageReference (LSU-1). Its rows need an interactive desktop. The App Shell wiring (hotkey → planner → player over CardSurface + a Voice speaker) is not done — it needs the Phase 4 planner to have lessons to play.

## DEC-024 — 2026-10-28 — claude-opus-5-5

**Decision — the lesson planner stays multimodal and multi-model.** Refines DEC-021 (owner, 2026-10-28: "I want to keep it multimodal"). (1) *Multi-model:* the planner gets its `ILlmClient` from `ProviderRegistry` by role, never by naming a provider in code; DeepSeek `deepseek-flash` is only the default assignment, and any adapter already in `src/Providers` (OpenAI-compatible family, Anthropic, Gemini once finished) can be assigned by configuration. (2) *Multimodal input:* each planning request carries the question text plus, when the screen was grounded, one bounded screen crop as an `LlmImagePart` (same JPEG caps and untrusted-data labelling as the Tutor, DEC-015). If the assigned model lacks `ProviderCapability.Vision`, the planner takes an explicit text-only path (UIA/element description only) and records that it did — never a silent downgrade, matching Providers.Core's capability rule. (3) *Multimodal output* is already the design: speech + 2D cards + screen marks now, 3D at Phase 5.

**Reasoning.** Providers.Core already models images, a Vision capability flag and role-based mix-and-match assignment, so this costs no new code before Phase 4 — only a constraint on how Phase 4 is written. Keys can be added later without touching the planner.

**Rejected.** Hard-wiring the planner to one provider client (locks the product to one vendor's pricing and quality); text-only planning (loses the screen, which is the point of a screen-aware tutor).

**Consequences.** Phase 4 tests must cover both the image path and the no-Vision text path against fakes, offline. No provider key is needed for any work before Phase 4's live rows.

## DEC-022 — 2026-10-28 — claude-opus-5-5

**Decision — tutor session state is in memory only.** The session keeps the last validated `VisualPlan` and the last 6 question/answer turns so follow-ups ("simpler", "just the combustion part", a clicked part) have context. Cleared after 10 minutes idle and on exit. Nothing is written to disk. Owner delegated the choice on 2026-10-28 ("choose the path which is quicker to build and demo, is cheaper and is efficient").

**Reasoning.** Follow-ups are the core of HeyClicky's teaching loop and need only the current conversation; persistence, a learner model and cross-day memory are M8 work with their own privacy questions. In-memory state costs nothing to build or secure now and can be replaced behind the same seam.

**Rejected.** SQLite persistence now (D3 is undecided, adds a store before there is anything worth keeping); fully stateless follow-ups (re-describing the whole lesson each turn costs tokens and loses the drawing context).

**Consequences.** Screen-derived text lives only in process memory, never on disk. Restarting the app forgets the conversation — acceptable for the demo, revisited with Notes (M8).

## DEC-021 — 2026-10-28 — claude-opus-5-5

**Decision — the lesson planner calls models through `HeyGanesh.Providers.Core`'s `ILlmClient`**, using its existing structured-output (`JsonSchema`) and streaming paths; default provider DeepSeek `deepseek-flash` through `Providers.OpenAiCompat`, key `DEEPSEEK_API_KEY` (already in use by Vision and Tutor, DEC-007/DEC-017). The Tutor Engine's own `DeepSeekTutorClient` stays as is for plain explanations. Owner delegated the choice on 2026-10-28 ("choose the path which is quicker to build and demo, is cheaper and is efficient").

**Reasoning.** The provider layer is already built and smoke-verified (ProvidersSmoke/OpenAiCompatSmoke) with streaming, retries and a circuit breaker; reusing it is the cheapest path and lets the planner switch to a stronger model later by configuration, not code. DeepSeek flash is the cheapest model the project already holds a key for.

**Rejected.** Extending `DeepSeekTutorClient` (no streaming, no structured output, and DEC-017 pinned its payload); a new provider (cost and a new key for no demo gain).

**Consequences.** The planner assembly takes a ProjectReference on Providers.Core. Phase 0c (first-beat latency) could not be measured: no provider key is set on this machine — it runs the first time a key is present, before Phase 4 is called done.

## DEC-020 — 2026-10-28 — claude-opus-5-5

**Decision — lessons render in WebView2, as opaque card windows.** One new Experience assembly, `HeyGanesh.LessonSurface`, is the only assembly allowed the `Microsoft.Web.WebView2` package (pinned 1.0.4314.35; the Evergreen runtime ships with Windows 11 — 155.0 here). Each card is a borderless, top-most, `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` window with `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`, PerMonitorV2, positioned in physical pixels. Screen marks (circle/underline/arrow on the student's own screen) stay on the existing native, click-through `ScreenOverlay`. 2D drawing is SVG with vendored KaTeX (math) and Rough.js (hand-drawn strokes); Babylon.js is added only at Phase 5. One WebView2 environment is created hidden at startup and reused. Owner delegated the choice on 2026-10-28 ("choose the path which is quicker to build and demo, is cheaper and is efficient").

**Reasoning — Phase 0a spike (throwaway WinForms host, never committed), 3 runs:**
- No focus steal: `GetForegroundWindow` never became the card — True on every run.
- Capture exclusion covers WebView2's out-of-process content: a screen sample saw the cream card before `WDA_EXCLUDEFROMCAPTURE` and did not after — True on every run.
- Cold start: environment + controller 384–495 ms; first paint 761–872 ms (incl. a 300 ms settle). Warm text update 2.3–2.5 ms.
- Cost: 6 msedgewebview2 processes, 143 MB private for the first card; host 12 MB.
- So: pre-warm one environment at startup and the first beat draws in milliseconds instead of ~0.8 s.

Opaque cards are also what HeyClicky shows (paper sheets), they keep WebView2 in its simplest, best-supported hosting mode, and they remove FEATURE-007's top risk (transparent, selectively click-through composition hosting) from the demo path entirely.

**Rejected.** Transparent full-screen WebView2 via composition hosting (the riskiest, slowest path — revisit at Phase 5 if 3D must float over the screen); native GDI/Direct2D cards (no NuGet, but no math typesetting and 3D would need a second renderer later); WPF `WebView2CompositionControl` in a layered window (software-composited full-screen layer, worse for 3D); hand-declared WebView2 COM without the NuGet (+2–3 weeks).

**Consequences.** The zero-NuGet invariant keeps holding for every core module; `HeyGanesh.LessonSurface` is the one documented exception (Experience layer), and its source audit must pin that exactly one PackageReference exists. Memory budget ~150 MB while lessons are available. The page loads only vendored files through a virtual host mapping, blocks navigation, and receives only validated plans (DEC-023).

## DEC-023 — 2026-10-28 — claude-opus-5-5

**Decision — model-authored lessons cross one validated, versioned contract before anything is spoken or drawn.** Schema `heyganesh.visualplan/1`: declared surfaces (cards), then beats of one `say` sentence plus up to 12 ops from a closed set (`write`, `step`, `arrow`, `icon`, `plot`, `highlight`, `mark`, `clear`). `VisualPlanParser` never throws on model text (only a null argument does) and recovers the outermost `{…}` once, so a prose-or-fence wrapper is not fatal. `VisualPlanValidator` enforces the `LessonLimits.Default` bounds, a fixed glyph vocabulary, the id pattern `^[a-z][a-z0-9-]{0,31}$`, plan-wide unique element ids, references only to elements live at that beat (no forward references, `clear` removes), and screen marks inside the caller's virtual desktop in physical pixels. Outcome policy: a beat with any issue keeps its `say` and loses its ops, and what it would have written never exists (dependants cascade); any plan-level issue, or ≥50% degraded beats, gives `TextFallback` with every `say` joined; nothing to say gives `Rejected`. Numbering note: DEC-020..022 are reserved for the owner's rendering-stack, planner-provider and session-state decisions named in FEATURE-007.

**Reasoning.**
- Accuracy and safety are decided above the renderer (FEATURE-007 §1.1): a closed op set means a renderer implements exactly what can be validated, and a model can only ever produce a wrong-but-valid drawing, never code.
- Speech is the part a student cannot lose: degrading to speech instead of failing keeps the lesson when the drawing is broken — BUILD-PLAYBOOK M7 S7.1's "malformed model output degrades to plain text with a notice".
- Time-ordered reference checks plus rollback of degraded beats let a renderer play beats strictly in sequence without ever meeting a dangling id.
- `PlotOp` carries an expression and range, never points — the app computes curves, so a plotted curve cannot be hallucinated.

**Rejected.** JSON Schema validation library (a NuGet for less than the reference rules need); throwing on bad model output (loses the spoken lesson and dresses model faults as caller bugs); per-code plan-level severity (one rule — an issue without a beat id is plan-level — is auditable); repairing malformed JSON beyond outer-brace extraction (guessing at model intent).

**Consequences.** Every later phase builds on this: the planner must emit it, the runtime plays it, the surface renders only `Valid`/`PartiallyDegraded` plans. New ops or the 3D `scene3d` surface arrive as a schema minor driven by eval failures. An off-contract `mode` string alone forces `TextFallback` — strict by design, revisit if evals show it costs good lessons. Numbers are never silently defaulted: a mark coordinate or plot bound that is missing, non-numeric, fractional or out of int range is `value.invalid` on its beat (integral decimals like 512.0 are accepted). The 400-char `say` limit bounds what is spoken, not only what is drawn: an over-long say is treated as no usable speech — its beat is dropped and it is excluded from `FallbackText` — rather than truncated, because cutting a sentence can change what it teaches.

## DEC-017 — 2026-10-01 — Cline agent (Pixel Canary)

**Decision — the first live teaching provider is DeepSeek `deepseek-flash`, direct, internal, and factory-fronted.** `DeepSeekTutorClient` (internal) sends one OpenAI-compatible Chat Completions request per turn — `model`, `messages` (system string; user plain string, or the verified `text`+`image_url` content-part array when a `TutorVisual` rides along, as a `data:image/jpeg;base64,` URL of the exact opaque bytes), `max_tokens` from the prompt's budget, `stream:false` — plus the fixed v1 decision `thinking: {"type": "disabled"}`. The bearer key is `DEEPSEEK_API_KEY`, read at call time only (DEC-007's temporary stopgap, extended through a second seam; the same variable Vision reads, which DEC-017 accepts as recorded coupling). `TutorService`, the shell and every other module are untouched; `TutorClientFactory.CreateDeepSeek(DeepSeekTutorOptions?) → ITutorClient` is the only new public construction API, and the shell's flip is that one call.

**Reasoning.**
- Fields were verified against the live DeepSeek API docs before writing (endpoint, message shapes, `image_url.detail` vocab, `thinking` object, usage echo) — nothing invented. `deepseek-flash` is the current name and the vision-capable model; `detail: high` keeps screen legibility for teaching.
- `thinking: {"type": "disabled"}` is fixed in the payload, not an options knob: the model currently defaults to thinking enabled at high effort, which spends unpredictable latency and the `max_tokens` budget on a reasoning trace for an answer meant to be SPOKEN. `MaxOutputTokens` must bound the lesson. Re-enabling is a future decision with measured evidence, not a flag to drift on.
- Internal client + public factory returning the seam: the concrete provider type never enters any consumer's compile-time world, which is DEC-015's "swapping providers touches construction only" enforced at the API level. A public concrete client, a handler parameter, or a provider enum/registry were all rejected; one static method, seam-typed.
- Verification tests the REAL construction path (TU-11): the smoke host builds via the factory and points `DeepSeekTutorOptions.BaseUrl` at a strictly-loopback `HttpListener` stub (free-port probe via `TcpListener` on :0, concrete-prefix bind with bounded retry — `HttpListener` cannot bind `:0`; no wildcard prefix, no admin/URLACL). No `InternalsVisibleTo`, no exposed `HttpMessageHandler` — an assembly-identity hole would verify a path production cannot take. Stub replies are canned in enqueue order (request N ← Nth response; last repeats), deterministically, and the row owns `DEEPSEEK_API_KEY` for its duration only, sentinel-never-printed.
- Error mapping lives in the client (`401/403`→auth status-only message, body never quoted; `429`→rate-limit; other→client+status with a ≤500-char snippet); everything else is left to the service's single `Translate` authority. Empty content crosses as empty text — the service owns calling it a failure.

**Rejected.** Thinking-on/server-defaults (spoken latency/budget); thinking as a knob (policy before evidence); shared DeepSeek HTTP helper assembly (DEC-009 — the ~40 transport lines duplicated from Vision are the accepted price); reusing `DeepSeekVisionClient` (JSON-mode grid prompt ≠ teaching); a second env key (rotation drift); shell flip in this change (scope — TU-12's live half is the gate); `response_format`/sampling/stream fields (the teaching contract has no such policy).

**Consequences.** `deepseek-flash` + `DEEPSEEK_API_KEY` are now committed names (DEC-015's neutrality ends by owner decision, not by drift). Vision and Tutor share one key and one concurrency pool — typed 429s on both seams, token accounting in both results. TU-11 pins the wire offline; TU-12's live half (and the injection row) remain the human-gated truth. The shell flip — `CreateTutorClient()` returning `TutorClientFactory.CreateDeepSeek()` — is now a one-line change, deliberately not taken here.

## DEC-016 — 2026-09-30 — Cline agent (Pixel Canary)

**Decision — the shell wires the teaching leg; it does not wire a provider.** After every non-Failed
verdict — reported, and drawn when enabled — `RunSessionAsync` runs `TeachAndSpeakAsync`: the shell
maps the `GuidanceResult` into the plain-data `TutorContext` exactly as DEC-015's remarks prescribed
(`!Found` maps to `TutorContext.Empty`; `Captures[0]` — the cursor's display, already JPEG ≤ 1280 px —
attaches as a `DownscaledFrame` visual when `Found`, by reference), calls `TutorService.ExplainAsync`,
and feeds `ExplanationText` **verbatim** into the already-composed `SpeakAsync` (skippable with the
new `--no-speak`). `HeyGanesh.App` gains its fifth ProjectReference (TutorEngine; zero NuGet
unchanged). The seam is composed by one method, `CreateTutorClient()`, which **returns null today**:
DEC-015's boundary holds — the change wires the leg, not a provider, and a clientless leg says so
after the verdict (skip line, exit 0) instead of pretending to teach or refusing the working path.

**Reasoning.**
- DEC-015 left exactly one forward-declaration point; a null-returning factory with zero call-site
  changes is the honest minimum. Fail-closed ("no client ⇒ refuse the turn") would have broken the
  product path that works today; shipping a real client would have smuggled the owner's provider
  decision in as plumbing.
- Short-circuiting on `Failed` at the call sites — branch first, map never — is the CO-6 guard, not
  mapping paranoia: Vision's model-side not-found arrives `Found=false` WITH `Failed=true`, and a
  "no verdict" is not mappable; teaching from absence is fabrication, and the overlay already
  refuses to draw on exactly this signal.
- The whole-frame visual is the only derivation that exists without an image pipeline: no decode, no
  crop, no re-encode, no new package — `Captures[0]` already carries the bytes and the declared
  dimensions. Crop-around-box needs a JPEG decoder and is explicitly deferred (FEATURE-006), not
  quietly built.
- Size caps are enforced by `TutorService` alone (pre-seam, `ArgumentException` = caller bug), and the
  shell maps that to exit 4 labeled `TUTOR MAPPING FAULT (shell bug)`. Pre-checking caps in the shell
  would give the caps a second enforcer, and second enforcers drift.
- The exit-code alphabet is reused, never extended: 0/12/3/13/4 mean what they meant, and the verdict
  is always reported (and drawn) before any leg code can replace it. `--no-speak` exists because once
  a live client lands, the scripted `--once` path must be runnable without taking the audio device —
  a real-surface flag, not a test double, so DEC-012's no-`--fake` rule holds.

**Rejected.**
- *Always-attach visual, including no-match turns* — spends visual budget on a verdict that says
  nothing was seen and muddies the on-screen/in-general split the system prompt defends.
- *Fire-and-forget `SpeakAsync`* — breaks Voice's one-voice-session rule (VO-9) and the
  shutdown/Drain ordering; sequential speak keeps both true.
- *Summarising or rewriting the explanation before speaking* — the Tutor owns the prose;
  `MaxExplanationChars` is already the contract with this consumer.
- *A shared-abstractions assembly for the confidence-tier switch* — DEC-009; three switch arms in
  the shell instead.
- *A `--no-tutor` kill-switch* — with a clientless seam it can test nothing, and once a client
  exists, "no lesson" is a provider decision, not a flag; `--no-speak` covers the device concern.

**Consequences.** Both turn modes share the leg (one `RunSessionAsync`, no drift). A long lesson now
extends the busy window — a second chord is refused while speech plays (VO-9 surfacing one layer up);
recorded, not fixed. Offline verification reaches the gates and legacy invariance only (SH-15..SH-17);
end-to-end teach-and-speak is a live-only row until the first `ITutorClient` exists — which the owner
owns. Cropped-region visuals and scripted mapping coverage belong to the future `ShellSmoke` host
(FEATURE-005), now the natural next verification artifact.

## DEC-015 — 2026-09-30 — Cline agent (Pixel Canary)

**Decision — the Tutor Engine is the teaching layer, and it ships with no provider.**
`HeyGanesh.TutorEngine` turns one `TutorRequest` — a question (the teaching task, primary) plus an
optional `TutorContext` (a plain-data snapshot of already-resolved grounding, possibly carrying one
opaque `TutorVisual` JPEG) — into one `TutorResult.ExplanationText` over an `ITutorClient` seam.
The seam ships with fakes only: **no provider, model, endpoint or key name is committed by this
change.** `TutorService` owns the system prompt; everything screen-derived enters as data explicitly
labelled `UNTRUSTED DATA`, and the rendered prompt forbids following instructions found inside it;
coordinates travel as labelled metadata and are banned from answer prose; the visual is capped
(512 KiB, 1568 px edges), crosses as separate binary, never base64-spliced into text. The service
implements no `IDisposable` and never disposes the injected client, and the assembly carries zero
ProjectReference and an empty P/Invoke surface — there is nothing in it that could touch a screen.

**Reasoning.**
- DEC-013 made the Tutor the answer authority — Voice suppresses the Gateway's own answers precisely
  because this module will speak. Question-first is the load-bearing rule: an explanation must be a
  real lesson when the screen resolved nothing (TU-5 asserts exactly that), or the module degrades
  into a caption engine and duplicates the legs.
- `GuidanceResult` is NOT passed — the shell maps it into a `TutorContext` snapshot at the call edge.
  A reference to ContextOrchestrator would weld the teacher to the session state machine and drag
  capture/leg types along; DEC-009's discipline (siblings stay siblings, the host wires the edges)
  applies to the teacher too.
- A provider-neutral seam rather than a live client: the owner has not chosen a teaching provider, and
  choosing one here would smuggle a decision in as plumbing. `TutorPrompt`'s shape
  (system text / user text / max tokens / separate visual) is the least-common-denominator of
  Chat-Completions-style APIs, so freezing it now is safe.
- The untrusted-data labelling is prompt-level mitigation, not a guarantee: fakes can prove the
  labelling, the carrying and the non-echo — they cannot prove a real model's resistance. The
  checklist records live injection-resistance as an OPEN row rather than pretending otherwise.

**Rejected.**
- *Passing `GuidanceResult` / referencing ContextOrchestrator* — welds the layers; see above.
- *Base64-splicing the visual into the user text* — defeats byte-cap auditability and makes the text
  channels unscannable for secrets; the seam carries bytes as its own field instead.
- *Silently downscaling an over-cap visual* — quietly teaching a different picture than the caller
  cropped is drift; over-cap throws `ArgumentException` (caller bug) instead, named per option.
- *Shipping a live client with a chosen model now* — the decision belongs to the owner; when it lands
  it follows the DEC-007 stopgap pattern at call time. No field in this assembly can hold a
  credential (TU-8), and the service deliberately reads none itself.
- *A `TutorService : IDisposable` disposing the client* — the service never created the seam;
  disposal belongs to whoever opened it (TU-9 pins the absence via reflection, because the XML docs
  legitimately *talk about* disposal and would false-fail a text scan).

**Consequences.** `SpeakAsync` still has no caller: mapping `TutorResult → SpeakRequest` (and
`GuidanceResult → TutorContext`) in the shell is a separate change, filed with FEATURE-006; Jev's
position "before the Tutor Engine call" stays open. TU-1..TU-10 are green offline; every claim about
model behaviour is open by construction. `TutorOptions`' caps (1600-char answer, 500 output tokens,
400-char untrusted-description clamp, 512 KiB/1568 px visual) become shell config decisions at wiring
time.

## DEC-014 — 2026-09-30 — Cline agent (model id not exposed to the agent)

**Decision — push-to-talk shell wiring.** The App Shell composes one `VoiceService` and a chord press
opens the microphone: `Pressed → TriggerPump.OnPressed → VoiceService.ListenOnceAsync (on the pool)`;
`Released → TriggerPump.OnReleased → end the utterance`; a committed `UtteranceResult.FinalText` becomes
`options.Question` and flows into the one unchanged `HandleHotkeyPressedAsync → ShowAsync` path.
`--question` is demoted to a legacy/verification flag (ignored with a warning in hooked mode; still
deterministic under `--once`), and `--once` without it runs a single live listen. `SpeakAsync` is
composed but never called, because the Tutor Engine that would supply its text does not exist.

**Reasoning.**
- The trigger point moves, exactly as FEATURE-003 filed it: with speech, the chord's *press* starts an
  utterance and its *release* ends it. Keeping press as "resolve now" would ask the Gateway to commit
  an empty transcript.
- The orchestrator, overlay and legs keep their contracts untouched. Only the question's origin changes,
  so no screen, cursor, capture or fallback behaviour can shift (verified: `--once --question` output is
  byte-identical to before, and the ContextOrchestrator/Overlay/VoiceSmoke rows still pass).
- Voice stays a replaceable seam: the shell depends on `VoiceService`, never on `QwenGatewayClient`,
  `FishTtsClient` or any wire type.

**Limitation, accepted and recorded — "release finalises" can only mean "end the utterance".** Voice's
API (DEC-013) is one `ListenOnceAsync` per utterance with no force-commit call, and this task freezes
the Voice module, so the shell cannot make the Gateway commit early. Releasing before the transcript
commits therefore cancels the listen: the turn is abandoned cleanly (exit 0, nothing resolved, no
capture spent). Holding the chord through the sentence — the normal case — is the contract.

**Exit-code mapping (the one deliberate deviation).** `UtteranceResult.Failure` is a *returned* reason
string, not a typed exception, so the shell cannot recover 12-vs-3 without sniffing text, and Voice is
frozen so no typed channel can be added. A failed listen is therefore **exit 1** ("no transcript was
produced"), never a fabricated verdict; thrown `VoiceAuthenticationException`/`VoiceClientException`
still map to 12/3. A release-abandoned turn is exit 0 (a user action is not Ctrl+C); shutdown during a
listen is 13. Silence is a verdict: `IsNoSpeech` never reaches the orchestrator, so no capture is spent.

**Rejected.**
- *Force-commit on release* — needs a Voice-module change and a second unverified protocol claim; the
  Gateway's `smart_turn` commit is server-side. Rejected in favour of the accepted limitation above.
- *Delete `--question`* — would break SH-4/SH-7/SH-8, the only scripted coverage the shell has (DEC-012
  forbids `--fake` flags in the product host). Demoted, not deleted.
- *Queue a second trigger instead of refusing it* — contradicts CO-10/SO-8 and would open two
  microphones against one Gateway owner slot (DEC-013: GCP takes one active client per owner).
- *Inject keystrokes for an automated chord test* — forbidden by DEC-010; a `ShellSmoke` host with an
  injectable trigger is the correct follow-up, filed in FEATURE-005.
- *A second CTS per phase (listen then session)* — needless: the resolve half already links its own
  token inside `RunSessionAsync`, and one source per turn keeps `OnReleased` able to cancel the listen.

**Consequences.** Teardown gains a step (hotkey → voice → overlay → …) because a draining listen must
never outlive its `VoiceService`. The header prints the voice keys' presence, not values. Press/release
timing, the IsNoSpeech short-circuit and REFUSED-during-listen are human-gated until a `ShellSmoke` host
exists; the failure/cancel/refusal mappings above are exercised offline.


## DEC-013 — 2026-09-30 — Cline agent (model id not exposed to the agent)

- **Status:** accepted
- **Decision:** The Voice module (`src/HeyGanesh.Voice/`) owns the entire speech integration boundary behind three seams — `IVoiceListener` (speech in), `ITtsClient` (speech out), `IAudioOutput` (speaker) — with **both external runtimes kept out of the dependency graph**: the qwen-audio-agent Gateway is an operator-run service spoken to over its **Gateway Client Protocol (wire 7.0)** on a WebSocket (`session.hello` → `session.ready`, `input_audio_buffer.append` for microphone frames, `transcript.*` for text), and Fish Audio is a plain HTTPS `POST /v1/tts` behind `FishTtsClient`. Zero `PackageReference`; microphone and speaker are owned `winmm.dll` P/Invoke. **The Qwen frontend is never trusted to answer:** `transcript.final` with `role: user` is the only frame that becomes a question; every assistant-side frame is cancelled/discarded and counted in `GatewaySuppressionLedger`; a `task.*` Backend Agent event aborts the listen (`AGENT_PROTOCOL=none` is asserted at the health endpoint when reported, and enforced at runtime either way). **Fish format is `wav` @ 44.1 kHz** — the v1 enum is exactly `wav|pcm|mp3|opus`, and WAV is the only choice playable with zero decoder dependencies.

### Context / alternatives considered
- **Pre-generation suppression for live audio** — investigated against the Gateway/realtime interface before accepting generate-then-cancel: `session.hello` `connection.output_enabled`/`text_only` are client capability/presentation preferences, not generation gates; AgentDelivery's `handle`/`context`/`respond` modes apply to typed Client Events (`client.event.publish`), not to live audio appended via `input_audio_buffer.append`; sleep/`input.suspend`/`input.mute` are capture-side; `conversation.item.create` has no verified client-callable transcribe-without-reply semantic (the `contextOnly` concept belongs to the Gateway→provider adapter, not to GCP); prompt-level "stay silent" is advisory, not enforced. **No supported pre-generation suppression exists** — so cancel/discard is the design, marked as protocol limitation L-1 rather than a preference.
- Option A — treat `qwen-audio-agent` as a pure STT engine (first plan draft). Option B — in-process Qwen SDK/NuGet. Option C — invented NDJSON/stdio `partial|final|no_speech|error` protocol over a self-chosen localhost socket (first plan draft). Option D — Fish realtime-v3 MessagePack WebSocket. Option E — `mp3`/`opus` output with a decoder package. Option F — hold the Gateway socket open between utterances.

### Chosen because
- GCP 7.0 event names are published and version-negotiated (`session.ready` may state `protocolVersion`; a major mismatch refuses), so the module is pinned to a contract it can assert offline rather than a protocol it invented.
- Cancel/discard, enforced in HeyGanesh code, is the only Tutor-authority guarantee that survives a Gateway that reports nothing — which is why `GatewaySuppressionLedger` is pure and replay-tested (VO-5).
- WAV + `winmm` keeps the zero-NuGet invariant: `WavPcmReader` validates RIFF/PCM16/mono loudly, `WinMmAudioOutput` plays it with `waveOut*` (`CALLBACK_EVENT = 0x00050000` — the `0x00010000` window-callback value was a real bug caught by the smoke row).
- Per-utterance transport (`connect → hello → listen → close`) matches press-to-talk and bounds the GCP one-active-client-per-owner takeover to the utterance itself.

### Rejected because
- Option A is false: `AGENT_PROTOCOL=none` disables only the Backend Agent; the DashScope realtime frontend still converses (text+audio output, function calling), so a "pure STT" reading would let Qwen answer academic questions by accident.
- Option B violates the zero-dependency invariant and makes "backend disabled" a review promise instead of an asserted boundary.
- Option C couples HeyGanesh to a protocol the upstream repo does not define; it would break on the next upstream release and could never be verified against the real service.
- Option D adds a second stateful protocol (msgpack subprotocol + ticket auth) for no capability this module needs.
- Option E (mp3/opus) requires decoder packages; raw `pcm` has no header to validate. Option F keeps TUI/WebUI clients evicted between questions for no benefit.

### Consequences
- **L-1 (accepted):** each utterance may cost one wasted frontend generation. Its *effects* are bounded — no assistant bytes reach the speaker, overlay, or Context pipeline — but HeyGanesh prevents the effect, not the occurrence, because no supported pre-generation switch exists for live audio.
- HTTP `ResponseHeadersRead` is **transport streaming only**; no incremental-generation/first-audio claim is made for Fish `POST /v1/tts` (the verified contract does not guarantee one).
- `FISH_AUDIO_API_KEY` extends DEC-007's temporary direct-key arrangement (env, read at call time, presence/length only); the Gateway's provider key never crosses the Gateway Client Protocol, and `QwenGatewayOptions.SafeEndpoint` strips path/query so an endpoint can never leak a token into a log.
- The App Shell still takes `--question`; wiring `Released`-submits and retiring the flag is the next step (FEATURE-003's filed follow-up) and was deliberately not mixed into this change.

## DEC-012 — 2026-09-29 — Pixel Canary

- **Status:** accepted
- **Decision:** The App Shell is `src/HeyGanesh.App/` — an `OutputType=Exe` console host on `net8.0-windows10.0.22621.0` with **zero `PackageReference`**, project-referencing GlobalHotkeyMonitor, ContextOrchestrator and ScreenOverlay. It owns the three things no module may own: process-wide PerMonitorV2 DPI awareness, the single `ScreenCaptureService` (shared into `ScreenOverlayService` for monitor topology), and the wiring `GlobalHotkeyMonitor.Pressed` → `ContextOrchestratorService.HandleHotkeyPressedAsync` → `ScreenOverlayService.ShowAsync`. It renders no window of its own. Windows App SDK / WinUI 3 is deferred again, and **test doubles stay out of product code**: the shell has no `--fake` flags, so its offline coverage stops exactly where the real key gates stop.

### Context / alternatives considered
- Option A — WinUI 3 / Windows App SDK now, the "eventual shell direction" DEC-011 named.
- Option B — WPF (`<UseWPF>true</UseWPF>`): no NuGet needed, since WPF ships in the Windows Desktop runtime.
- Option C — dependency-free console host now, UI framework deferred until something actually needs a window.
- Option D — put the shell in `tools/` as `ShellSmoke` rather than `src/`.
- Option E — carry `--fake-jev` / `--fake-vision` flags so the whole chain could be verified without keys.

### Chosen because
- Option C is the smallest step that closes the gap DEC-010 left open ("Both gaps close in the app shell (Voice pipeline + this wiring)"), and it needs no dependency, no packaging story, and no UI thread. Its correctness is inspectable in one place: the wiring is the same method for `--once` (scriptable, no keystrokes) and for a real chord, so the deterministic path and the human path cannot drift.

### Rejected because
- Option A — requires `Microsoft.WindowsAppSDK`, this repo's first external package, and a packaging/self-contained-runtime decision DEC-011 already recorded as unresolved (the capture border needs a *packaged* app; `TryCreateFromDisplayId` is the documented fallback "once the Windows App SDK shell exists"). It also puts an activation-taking XAML window next to an overlay whose entire contract is `WS_EX_NOACTIVATE` + `HTTRANSPARENT`. Deferred, not repudiated — DEC-011 still names it.
- Option B — no NuGet, but it adds `Application`/dispatcher lifetime and an STA UI for a process that draws nothing today; the overlay is already the UI and it is Win32.
- Option D — the wiring here is product behaviour. A smoke host cannot legitimately own the process's DPI awareness or the hotkey subscription, and hosting it in `tools/` would have meant duplicating the very wiring the shell exists to own.
- Option E — shipping test doubles inside the product to make its own tests easier is the wrong trade. The cost is real and is recorded honestly instead: with no keys, the shell proves composition, the trigger seam, key fail-fast, cancellation and lifecycle, but **not** a drawn verdict.

### Consequences
- **The shell's own entry point is also its verification path.** `--once` makes the composition exercisable by a script with no microphone, no keystrokes and (until a key is missing) no human — which is why the `REFUSED` overlap path and a real chord remain unverified: they are the only two behaviours that need a person.
- **WinUI 3 remains unbuilt, now twice deferred.** It needs its own decision entry when something genuinely requires a window; the capture-border and packaged-identity questions travel with it.
- **The question source is a flag.** `--question` is required rather than defaulted, so the Voice pipeline cannot silently be replaced by a constant. When Voice lands, the trigger should move from `Pressed` to `Released` (record on press, submit the transcript on release) — recorded as an open item in [HANDOVER.md](./HANDOVER.md).
- **A leg exception cannot be located to a pipeline stage from outside.** `ContextOrchestratorService` resets `Phase` to `Idle` in its catch-all before rethrowing, so a `KEY ERROR (Vision)` and a `KEY ERROR (Element Selection)` are distinguishable only by which exception type arrived — not by phase. The shell prints what it can and claims nothing more; fixing it would mean changing the orchestrator's error contract, which is out of scope here.
- **Nothing in any module changed.** This is a new-file-only change; every behaviour the shell exercises was already verified by CO-1..CO-11 and SO-1..SO-9.

## DEC-011 — 2026-09-24 — Muse Spark (muse-spark)

- **Status:** accepted
- **Decision:** The Screen Overlay is dependency-free Win32 C# in `src/HeyGanesh.ScreenOverlay/` — BCL plus user32/gdi32 P/Invoke, zero `PackageReference` — consuming `GuidanceResult` directly. Windows App SDK / WinUI 3 stays the eventual *shell* direction, not this module. Every overlay window is a borderless `WS_POPUP` layered window carrying `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_TOOLWINDOW`, answering `WM_NCHITTEST` with `HTTRANSPARENT` for every point (`WS_EX_TRANSPARENT` alone is insufficient for a top-level window) and `WM_MOUSEACTIVATE` with `MA_NOACTIVATE`, so input falls through and activation is impossible. Capture exclusion is a hard gate: a window the OS still photographs is destroyed, never shown. `Found=false` and any `Failed` verdict draw nothing. No `SendInput`, no cursor/focus APIs, no injection, no UIA writes exist in the assembly.

### Context / alternatives considered
- Option A — WinUI 3 / Windows App SDK now (what the original overlay brief assumed).
- Option B — dependency-free Win32 now (layered windows + GDI), WinUI 3 deferred to the packaged shell.
- Option C — `WS_EX_TRANSPARENT` alone as the click-through mechanism.
- Option D — accept `PixelRect`/raw coordinates or a new shared `OverlayTarget` type instead of `GuidanceResult`.
- Option E — queue overlapping shows instead of refusing the second.

### Chosen because
- Option B keeps the zero-NuGet invariant intact with the smallest blast radius: it is the same shape as every existing module and host, satisfies "Windows-native C#" without a packaging story, and leaves the WinUI 3 shell direction untouched. The original brief's WinUI-vs-zero-NuGet branch is resolved by this entry, not left open.

### Rejected because
- Option A — requires the Windows App SDK NuGet package inside a core module, violating the enforced zero-`PackageReference` invariant, and drags in packaging/deployment requirements for a module whose job is one temporary highlight.
- Option C — `WS_EX_TRANSPARENT` is a painting/hit-test hint whose behaviour for top-level windows has varied; returning `HTTRANSPARENT` from `WM_NCHITTEST` is the explicit, authoritative statement. The correction's own history (user-flagged mid-build) is why the smoke host asserts it from the OS side.
- Option D — a raw-rect overload would create a second de-facto coordinate contract bypassing the `Found` gate; a shared type or `HeyGanesh.Abstractions` is what DEC-009/DEC-010 forbid.
- Option E — two simultaneous pointers are ambiguous output; the orchestrator's burst guard (CO-10) sets the precedent: refuse, don't queue.

### Consequences
- **Multi-monitor clipping math ships single-display-verified only** (this machine reports one 2560x1600 display): negative-origin handling and per-window clipping are coded but the second-display half is asserted synthetically, not measured.
- **`Displayed=true` requires the full duration:** an early end (cancel, hide, dispose) reports `Displayed=false` even if a window appeared briefly — windows are always removed before the result returns.
- **Stale coordinates are inherent, not tracked:** the screen may change between `ResolveAsync` and `ShowAsync`; the overlay shows the verdict it was given, never re-resolves.
- **Still open:** WinUI 3 shell integration, any error *presentation* (this module reports `failure`, the future shell decides what the user sees), confidence-gated display (tiers are carried through `GuidanceResult`, never gated here).

## DEC-010 — 2026-09-23 — Cline agent (Claude Sonnet 4.5)

- **Status:** accepted
- **Decision:** The Context Orchestrator composes the two coordinate legs behind one entry point and one result type. `ContextOrchestratorService.ResolveAsync(question, ct)` opens a session: capture every monitor once (cursor's display first) → PRIMARY `ElementSelectionService.SelectAsync` → FALLBACK `VisionService.LocateAsync` on `captures[0]` **only** when the primary result is `Failed` (`FailureReason != null`) → one normalized `GuidanceResult`. `Choice == "none"` (`Found = false`, no `FailureReason`) is a verdict, not a failure, and never reaches Vision. The normalization (`SelectionResult`/`VisionResult` → `GuidanceResult`) lives in the orchestrator assembly only: `GuidanceResult`, `GuidanceSource`, `GuidanceConfidence`, `GuidancePoint` are new orchestrator-owned types, the legs' types and their duplicated `ConfidenceLevel` are untouched, no `HeyGanesh.Abstractions` exists, and no threshold is applied anywhere. This executes DEC-009's deferral rather than superseding it.
- **Also decided (trigger seam):** the orchestrator does **not** subscribe to `GlobalHotkeyMonitor.Pressed`. The trigger arrives through `HandleHotkeyPressedAsync(question, ct)` and the host maps the event to it; the question source (Voice pipeline) does not exist yet, and `Pressed` cannot be raised by a committed verification host without injecting real keystrokes into the user's session. A trigger arriving while a session is in flight is refused, not queued.

### Context / alternatives considered
- Option A — a shared `HeyGanesh.Abstractions` assembly holding one result type that both legs populate (the shape DEC-009 deferred "until the Context Orchestrator exists").
- Option B — the orchestrator calls Vision from inside the Element Selection failure path, i.e. the legs reference each other.
- Option C — gate on confidence too: fall back unless the primary leg returned High, or re-blend the two legs' scores.
- Option D — keep both result types and normalize them privately in the orchestrator, falling back on the failure signal only.
- Option E — the orchestrator owns a `GlobalHotkeyMonitor` and subscribes to `Pressed` itself.

### Chosen because
- Option D satisfies DEC-009's intent with the smallest blast radius, and DEC-009's own Consequences section names this exact policy ("empty tree / unmapped choice / UIA access-denied / no-choice → Vision; Confidence == High and a non-degraded verdict accept Element Selection"). The uniform shape plus the preserved raw signals mean the future Jev gate can be added without re-plumbing either leg.

### Rejected because
- Option A — it forces a cross-module reference that `SelectionResult.cs` forbids today, and would install a resolver in a neutral assembly before its consumer needs it; DEC-009 reserves those names for a deliberate decision, not a side effect of wiring.
- Option B — inverts the DEC-008/DEC-009 sibling boundary and puts fallback policy inside a leg.
- Option C — would invent a threshold and a fallback rule the legs deliberately do not own (both are threshold-free by contract; the tier is classification, not a gate). Nothing measured yet says a Medium primary verdict is worse than a pixel-model answer.
- Option E — would ship unverifiable code: the event fires on the monitor's worker thread and cannot be raised by the smoke host without SendInput, which this repo deliberately keeps out of committed tooling (CONSTRAINTS.md also forbids the product acting on the user's behalf), and the question source such a subscription would need does not exist.

### Consequences
- **New public surface lives in the orchestrator** (`GuidanceResult`, `GuidanceSource`, `GuidanceConfidence`, `GuidancePoint`). It has to be visible to the next module (a different assembly), which is why it is public; what DEC-009 forbids — a shared abstraction assembly and any cross-leg reference — still holds, and the legs' own files are unchanged.
- **A fallback not-found arrives as `Failed = true`.** `VisionService.Reconcile` reports a model-side not-found as a failure, and the orchestrator carries the leg's verdict verbatim instead of re-classifying it (CO-6 in TEST-CHECKLIST.md records this).
- **One session at a time is now enforced:** a second trigger during a session throws `InvalidOperationException` rather than queueing, so a host awaits the returned task or tolerates the refusal.
- **Still open:** no confidence-tier gate (that stays Jev's), no question source (Voice pipeline), no overlay, no rendering, no input injection — nothing in this change captures, draws, or acts on the user's behalf.

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

## DEC-018

- **Status:** accepted
- **Decision:** ScreenOverlay to ContextOrchestrator dependency conflict resolved through shared HeyGanesh.Abstractions abstraction. Both modules now depend on the zero-NuGet shared contract rather than each other. This supersedes earlier Platform-to-Intelligence dependency conflict identification.
- **Supersedes:** earlier Platform-to-Intelligence dependency conflict (pre-R7)
- **Date:** 2026-10-24
- **Model version:** Cline agent (M1/R7+)


## DEC-019 — 2026-10-24 — Cline agent (M1/R7+ completion)

- **Status:** accepted
- **Decision:** Complete the ScreenOverlay→ContextOrchestrator abstraction migration. Moved `GuidanceResult`, `GuidanceSource`, `GuidanceConfidence`, `GuidancePoint`, `PixelRect`, `MonitorInfo`, and `MonitorCapture` from their original assemblies (ContextOrchestrator and ScreenCapture) into `HeyGanesh.Abstractions` as the single shared contract. ScreenOverlay, ContextOrchestrator, ElementSelection, Vision, ScreenCapture, and App Shell now all depend on Foundation for these types. ScreenOverlay no longer references ContextOrchestrator (Intelligence). Dependency direction: Platform→Foundation, Intelligence→Foundation+permitted lower layers. Zero new NuGet dependencies. Guide-only safety preserved — abstraction contains only data types and interfaces, no implementation or actuation.
- **Supersedes:** DEC-018 (initial abstraction creation — now completed with full type migration)
- **Date:** 2026-10-24
- **Model version:** Cline agent (M1/R7+ completion)
