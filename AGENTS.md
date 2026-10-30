# AGENTS.md

This file provides guidance to AI coding assistants when working with code in this repository.

## What Hey Ganesh is

A Windows-native **AI desktop assistant & visual context engine**. It listens for global hotkeys, captures desktop screen state on demand, performs visual element grounding and coordinate detection, routes user intent, and delivers interactive visual lessons and voice feedback overlayed on the desktop.

```
hotkey -> Screen Capture -> Element Selection (PRIMARY; Vision is FALLBACK) → Jev choice → resolved element box → GuidanceResult → Screen Overlay → Tutor Engine → Voice TTS  (question source: committed transcript from VoiceService)
```

Subsystems, and the seams between them are load-bearing:

| Subsystem | Assembly / Package | Role |
|---|---|---|
| App Shell | `src/Experience/HeyGanesh.App/` | The process host: owns PerMonitorV2 DPI awareness, the service graph, and the `GlobalHotkeyMonitor.Pressed` → `VoiceService.ListenOnceAsync` → `HandleHotkeyPressedAsync` → `ScreenOverlayService.ShowAsync` → teaching-leg (`GuidanceResult`→`TutorContext`→`TutorService.ExplainAsync`→`SpeakAsync`, DEC-016) wiring (DEC-012/DEC-014/DEC-016). |
| Context Orchestrator | `src/Intelligence/HeyGanesh.ContextOrchestrator/` | Top-level state machine driving session lifecycle and module coordination. |
| Global Hotkey Monitor | `src/Platform/HeyGanesh.GlobalHotkeyMonitor/` | System-wide listen-only push-to-talk modifier chord monitor (Ctrl+Alt). |
| Screen Capture | `src/Platform/HeyGanesh.ScreenCapture/` | On-demand multi-monitor frame acquisition via `Windows.Graphics.Capture` and `CaptureExclusion` contract. |
| Element Selection (PRIMARY) | `src/Perception/HeyGanesh.ElementSelection/` | PRIMARY coordinate-detection path — UIA enumeration (plus up to 40 filtered lines of OCR'd screen text, DEC-039) and a Jev choice over a ranked manifest; the entry point is `SelectAsync(question, target?, ct)`, not Vision's `LocateAsync`. Vision is the fallback. |
| Vision / Coordinate Detection (FALLBACK) | `src/Perception/HeyGanesh.Vision/` | UI element locating and screen-space coordinate detection. The code sends ModelId `deepseek-flash` (VisionOptions.ModelId). Do not conflate this with a display name; the prior label "DeepSeek V4.1 Flash" in this file is a descriptive reference, not the ModelId value sent to the provider. |
| **Jev choice model** | **Integrated within `ElementSelection`** | **The Jev choice model routes Element Selection's ranked UIA candidates (`c001..`) to a selection; implemented via `IJevClient` / `TypeSafeJevClient` with `ModelId "jev-1.13.0"`; not a separate assembly. |
| Turn Router | `src/Intelligence/HeyGanesh.TurnRouter/` | One Jev call per turn decides point / explain / lesson, 2D or 3D, and the catalogue model (DEC-035), or a schematic when no listed model fits (DEC-051); below 0.5 or an id never offered is "no route". The App Shell routes each turn with it when `TYPESAFE_API_KEY` is set (DEC-036); `--lesson` / `--no-route` skip it. |
| Tutor Engine | `src/Intelligence/HeyGanesh.TutorEngine/` | Academic/technical teaching layer: question (+ optional bounded, untrusted screen grounding/visuals) → explanation. The only component allowed to answer the user; no screen actuation; provider seam fakes-verified + one live provider (DeepSeek `deepseek-flash`) behind `TutorClientFactory.CreateDeepSeek` (DEC-015/DEC-017). |
| Visual Lesson Engine | `src/Intelligence/HeyGanesh.VisualLessonEngine/` + `src/Intelligence/HeyGanesh.LessonPlanner/` + `src/Experience/HeyGanesh.LessonSurface/` + `src/Experience/HeyGanesh.LessonSession/` + `src/Intelligence/HeyGanesh.PartLibrary/` | VisualPlan v1 contract (parse/validate/degrade, DEC-023), `PlotExpression`, `LessonPlayer` runtime, the WebView2 card surface (DEC-020/DEC-025), the `VisualPlanner` (DEC-026) and `LessonTurn` (plan → cards → play, DEC-027); the 3D scene contract (VisualPlan 1.1, DEC-028), the Part Library of computed 3D teaching models (DEC-029), the Babylon.js scene window (DEC-030), click-a-part follow-ups (DEC-031) and a planner prompt that offers 3D (DEC-032). Wired into the App Shell behind `--lesson`, 3D included (DEC-033); the lesson model is built from `DEEPSEEK_API_KEY` or the leg reports its skip (FEATURE-007). |
| Screen Overlay | `src/Platform/HeyGanesh.ScreenOverlay/` | Per-monitor click-through visual overlay window with capture exclusion. |
| Voice Pipeline | `src/Experience/HeyGanesh.Voice/` | Speech-to-Text (STT) and Text-to-Speech (TTS) voice pipeline. |
| Notes Persistence | PLANNED — no assembly yet | SQLite-backed notes store for user learning session context; no `src/HeyGanesh.Notes/` assembly exists today (PLATFORM.md §94). |
| Tray | `src/Experience/HeyGanesh.Tray/` | Hey Ganesh as a desktop app (DEC-052): a notification-area icon with no console. It runs the App Shell's own composition through `ShellHost.RunAsync(args, stop, hookLive)`; Quit stops it like Ctrl+C. The shell's lines go to `logs/shell-<stamp>.log` (newest 5 kept); one tray per session (a second launch exits 3). Console-only flags (`--once`, `--help`, key commands) stay on `HeyGanesh.App.exe`. The key screen (DEC-053, "Keys…", and by itself when the DeepSeek or TypeSafe key is missing) stores keys through the key ring; Ctrl+Alt is refused while it is open. "Start at login" (DEC-054) writes or removes the per-user Run value `HeyGanesh`, only when chosen. |
| API key security proxy | `src/Foundation/HeyGanesh.Security/` (`KeyRing`) | One place for provider keys (DEC-045/DEC-046): Windows Credential Manager first, the environment variable as fallback, reported by presence/length/source only. App Shell `--keys`, `--set-key <name>`, `--remove-key <name>`. The shell's Jev, Vision, Fish, lesson and router clients read through it per request; the Tutor still reads `DEEPSEEK_API_KEY` until step 9. |

Canonical architecture definition: `docs/ai/ARCHITECTURE.md`. Interaction flow: `docs/ai/FLOW.md`.

## Commands

```bash
# Build core modules (.NET 8 Windows SDK)
dotnet build src/Platform/HeyGanesh.GlobalHotkeyMonitor -c Debug
dotnet build src/Platform/HeyGanesh.ScreenCapture -c Debug
dotnet build src/Perception/HeyGanesh.ElementSelection -c Debug
dotnet build src/Perception/HeyGanesh.Vision -c Debug

# Verification hosts / smoke tools
dotnet run --project tools/HotkeySmoke -c Debug -- --seconds 4
dotnet run --project tools/ScreenCaptureSmoke -c Debug
# Screen text (DEC-038, local-only: needs an OCR language): --assert-ocr reads the fixture; --read-text prints counts only.
tools/ScreenCaptureSmoke/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.Tools.ScreenCaptureSmoke.exe --assert-ocr
#   -> 17 PASS, OCR OK, exit 0
dotnet run --project tools/VisionSmoke -c Debug -- --question "where is the Start button" --samples 2

# Element Selection verification host (offline paths -- no API key required)
# Build the library and this host SEQUENTIALLY. Run in parallel they race on the shared
# obj DLL and fail with "error CS2012", which reads like a code fault but is not.
dotnet run --project tools/ElementSelectionSmoke -c Debug -- --question "x" --list-only
dotnet run --project tools/ElementSelectionSmoke -c Debug -- --question "Save" --fake
dotnet run --project tools/ElementSelectionSmoke -c Debug -- --question "Save" --fake --fake-none
# Screen text as candidates (DEC-039): --assert-text-merge is the merge rules alone (no window, CI G-20);
# --list-only --read-text OCRs the cursor monitor and merges it over the real window (counts only, local-only).
dotnet run --project tools/ElementSelectionSmoke -c Debug -- --assert-text-merge

# Screen Overlay verification host (offline verdict paths -- no API key required)
# --found: show a canned found box for the full duration.
# --no-match / --failed: honest no-match / synthetic failure must create no window.
# --assert-clickthrough: styles + WM_NCHITTEST -> HTTRANSPARENT + capture exclusion + no activation.
# --burst: a second ShowAsync while one is active is refused. --repeats: GDI/handle leak soak.
dotnet build src/Platform/HeyGanesh.ScreenOverlay -c Debug
dotnet run --project tools/OverlaySmoke -c Debug -- --found --duration-ms 1500
dotnet run --project tools/OverlaySmoke -c Debug -- --no-match
dotnet run --project tools/OverlaySmoke -c Debug -- --assert-clickthrough

# Context Orchestrator verification host (offline composition paths -- no API key required)
# --fake: primary leg answers. --fake-none: honest no-match, Vision must NOT be called.
# --fake-unmapped: primary leg fails (Jev chose an id never offered) -> exactly one Vision fallback.
# --burst: two overlapping triggers, second refused. --cancel-ms / --fake-error: propagation paths.
dotnet build src/Intelligence/HeyGanesh.ContextOrchestrator -c Debug
dotnet run --project tools/ContextOrchestratorSmoke -c Debug -- --question "Save" --fake
dotnet run --project tools/ContextOrchestratorSmoke -c Debug -- --question "Save" --fake-none
dotnet run --project tools/ContextOrchestratorSmoke -c Debug -- --question "Save" --fake-unmapped
# --fake-low: a found choice in the Low tier -> exactly one Vision check (DEC-037). --assert-check: the rule alone, no window (CI G-19).
dotnet run --project tools/ContextOrchestratorSmoke -c Debug -- --assert-check

# App Shell — the process host (DEC-012/DEC-014). Real legs only: no --fake flags, so with no keys set the
# offline rows stop at the key gate. Run the built EXE, not `dotnet run`: the wrapper hides exit codes.
# --question is LEGACY: the product path speaks (press Ctrl+Alt to listen, release to end the utterance).
dotnet build src/Experience/HeyGanesh.App -c Debug
src/Experience/HeyGanesh.App/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.App.exe --help
#   -> usage + exit-code contract (incl. "1 = ... or no transcript was produced"), exit 0
src/Experience/HeyGanesh.App/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.App.exe --once --question "where is the Save button"
#   keys unset -> header, real capture, KEY ERROR (Element Selection), exit 12  (legacy scripted path)
src/Experience/HeyGanesh.App/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.App.exe --seconds 4
#   -> no flag needed now: hook live: IsRunning=True; auto-stops, health block, exit 0
src/Experience/HeyGanesh.App/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.App.exe --question "x" --seconds 4
#   -> "--question is legacy and is IGNORED in hooked mode", hook live, exit 0
src/Experience/HeyGanesh.App/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.App.exe --once
#   -> one LIVE listen (mic as question source). No HEYGANESH_QWEN_GATEWAY_URL -> VOICE FAILURE, exit 1
src/Experience/HeyGanesh.App/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.App.exe --once --cancel-ms 60 --question "x"
#   -> CANCELLED, phase Idle, exit 13
# --no-speak skips the speech-out leg only. The teach/speak leg (DEC-016) runs only AFTER a produced
# non-Failed verdict, so the offline key gates are unchanged. Since DEC-047 the shell's
# CreateTutorClient() returns the DeepSeek Tutor (TutorClientFactory.CreateDeepSeek, deepseek-flash)
# whenever the key ring has the deepseek key, reading it per request; without one the header says
# "tutor : off: no DeepSeek key" and the leg reports its skip. Teach-and-speak end to end is live-gated (SH-17).
src/Experience/HeyGanesh.App/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.App.exe --once --no-speak --question "x"
#   -> flag accepted; keys unset -> same KEY ERROR (Element Selection), exit 12
#   Gateway running + keys + mic: hold Ctrl+Alt, speak, release -> utterance line, verdict, highlight
#   (SH-9/SH-13). Release early -> "released before the transcript committed", exit 0, no capture.

# Voice pipeline verification host (offline fakes + protocol assertions -- no Gateway, mic or key needed)
# --fake-transcript / --fake-nospeech: verdict shapes. --fake-auth-error: KEY ERROR, exit 12.
# --cancel-ms: OCE propagates, exit 13. --burst-fake: second listen refused, not queued.
# --speak-fake: fake TTS through the REAL winmm speaker + SpeakResult (needs an output device).
# --assert-gcp-events / --assert-tutor-authority / --assert-no-backend / --assert-fish-format:
#   recorded-frame contract checks -- message allowlist, assistant-output neutralised (DEC-013 L-1),
#   task.* abort guard, wav/44100 + WAV parser. Live paths (real Gateway, mic, Fish) are human-gated.
dotnet build src/Experience/HeyGanesh.Voice -c Debug
dotnet run --project tools/VoiceSmoke -c Debug -- --fake-transcript "where is the Save button"
dotnet run --project tools/VoiceSmoke -c Debug -- --assert-tutor-authority
# --assert-preroll (VO-11, DEC-040): the real client against a loopback Gateway stub (ready after 600 ms):
#   speech from the press reaches the Gateway, in order; a failed listen frees the microphone; no URL never opens it.
dotnet run --project tools/VoiceSmoke -c Debug -- --assert-preroll
dotnet run --project tools/VoiceSmoke -c Debug -- --speak-fake --text "Found the Save button"

# Tutor Engine verification host (offline fakes + prompt/error/source-contract assertions +
# the live provider's wire contract over a loopback stub -- no key or network needed for
# TU-1..TU-11; DEC-017 commits deepseek-flash + DEEPSEEK_API_KEY for the live rows only).
# Read exit codes from the built EXE: --assert-auth-error exits 12 and --cancel-ms <n> exits 13 BY DESIGN.
# Build library and host SEQUENTIALLY (shared obj DLL, the CS2012 trap).
dotnet build src/Intelligence/HeyGanesh.TutorEngine -c Debug
dotnet run --project tools/TutorSmoke -c Debug -- --fake-explain "what does git rebase actually do"
dotnet run --project tools/TutorSmoke -c Debug -- --assert-prompt
dotnet run --project tools/TutorSmoke -c Debug -- --assert-grounding
dotnet run --project tools/TutorSmoke -c Debug -- --assert-parity
dotnet run --project tools/TutorSmoke -c Debug -- --assert-client-error
dotnet run --project tools/TutorSmoke -c Debug -- --assert-source
# TU-11: the live provider's wire contract, offline -- the factory-built client's BaseUrl points at
# a loopback stub (no key, no real network; pins deepseek-flash payload + thinking:disabled).
dotnet run --project tools/TutorSmoke -c Debug -- --assert-tutor-payload
#   -> 36 PASS, exit 0   (two of them the key-source checks, DEC-047)
# TU-12: one real lesson. WITHOUT DEEPSEEK_API_KEY this prints KEY ERROR and exits 12 BY DESIGN
# (the offline shape); with the key it prints the live TutorResult and exits 0.
dotnet run --project tools/TutorSmoke -c Debug -- --live-explain "what does git rebase actually do"

# Visual Lesson Engine verification host (VisualPlan v1 contract, FEATURE-007 Phase 1 -- offline, no key,
# no renderer). Build library and host SEQUENTIALLY (CS2012). Read exit codes from the built EXE.
dotnet build src/Intelligence/HeyGanesh.VisualLessonEngine -c Debug
dotnet build tools/LessonSmoke -c Debug
tools/LessonSmoke/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.Tools.LessonSmoke.exe --assert-source
#   -> 6 PASS, exit 0   (also: --assert-parse 9, --assert-limits 20, --assert-refs 15, --assert-degrade 7, --assert-fixtures 6)
#   playback (fake canvas + gated fake speaker): --assert-play 5, --assert-play-degraded 5, --assert-controls 11, --assert-plot 16, --assert-grounded 6, --assert-scene3d 22 (schema 1.1 3D contract, DEC-028), --assert-schematic 22 (schema 1.2 schematics, DEC-048/DEC-050)

# Lesson Surface host (real WebView2 cards on the desktop; needs an interactive session, no key).
# Build library then host SEQUENTIALLY. Cards are capture-excluded: use --snapshot to see them as PNGs.
dotnet build src/Experience/HeyGanesh.LessonSurface -c Debug
dotnet build tools/LessonSurfaceSmoke -c Debug
tools/LessonSurfaceSmoke/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.Tools.LessonSurfaceSmoke.exe --assert-render
#   -> 17 PASS, exit 0   (also: --assert-source 20, --assert-window 9, --assert-security 4, --assert-marks 2)
#   3D scene window (DEC-030): --assert-scene-math 7 (offline), --assert-scene 19, --assert-scene-idle 4,
#   --assert-orientation 3, --assert-scene-nowebgl 8, --assert-scene-svg 8 (no-WebGL drawing, DEC-034), --assert-scene-schematic 10 (a lesson's schematic, DEC-050);
#   manual: --snapshot-3d <dir> [--no-webgl], --demo-3d --voice sapi
#   click a part to ask (DEC-031): --assert-pick 9 -- MOVES THE REAL CURSOR; run with nobody using the mouse
#   a lesson schematic's parts too (DEC-057): --assert-pick-schematic 4 -- MOVES THE REAL CURSOR
tools/LessonSurfaceSmoke/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.Tools.LessonSurfaceSmoke.exe --demo all --voice sapi
#   -> the three golden lessons drawn and spoken on screen (manual row LSU-6)

# Lesson Planner host (offline: recording fake ILlmClient + recorded answers, no key). Build library then host.
dotnet build src/Intelligence/HeyGanesh.LessonPlanner -c Debug
dotnet build tools/LessonPlannerSmoke -c Debug
tools/LessonPlannerSmoke/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.Tools.LessonPlannerSmoke.exe --assert-prompt
#   -> 10 PASS, exit 0   (also: --assert-source 3, --assert-multimodal 5, --assert-plan 3, --assert-repair 6, --assert-errors 3, --assert-followup 2, --assert-hardening 7, --assert-prompt-3d 10, --assert-stream 12, --assert-prompt-schematic 7)

# Lesson Turn host (one lesson turn: fake model + fake or REAL cards, no key; LT-6/LT-7 need an interactive desktop).
# Build library then host SEQUENTIALLY (CS2012). The App Shell carries no fakes (DEC-012): this host is where the turn is tested.
dotnet build src/Experience/HeyGanesh.LessonSession -c Debug
dotnet build tools/LessonTurnSmoke -c Debug
tools/LessonTurnSmoke/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.Tools.LessonTurnSmoke.exe --assert-turn
#   -> 4 PASS, exit 0   (also: --assert-source 4, --assert-turn-degraded 4, --assert-turn-rejected 2, --assert-turn-errors 6, --assert-turn-surface 6, --assert-followup-pick 15, --assert-turn-stream 11)
#   manual: --demo-pick --voice sapi   (the engine lesson, then click parts to ask about them, same window; LT-9)
tools/LessonTurnSmoke/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.Tools.LessonTurnSmoke.exe --demo-turn --voice sapi
#   -> the whole turn drawn and spoken on screen (manual row LT-7)

# Part Library host (3D teaching models, DEC-029 -- offline, no key, no renderer). Build library then host SEQUENTIALLY (CS2012).
dotnet build src/Intelligence/HeyGanesh.PartLibrary -c Debug
dotnet build tools/PartLibrarySmoke -c Debug
tools/PartLibrarySmoke/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.Tools.PartLibrarySmoke.exe --assert-physics
#   -> 19 PASS, exit 0   (also: --assert-source 5, --assert-library 20, --assert-catalog 6, --assert-schematic 7)
tools/PartLibrarySmoke/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.Tools.PartLibrarySmoke.exe --dump all
#   -> plain-text review of every model's parts and descriptions (manual row PL-5; no model is Reviewed yet)

# Turn Router host (DEC-035 -- offline: recording fake Jev client, scripted answers, no key). Build library then host SEQUENTIALLY (CS2012).
dotnet build src/Intelligence/HeyGanesh.TurnRouter -c Debug
dotnet build tools/TurnRouterSmoke -c Debug
tools/TurnRouterSmoke/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.Tools.TurnRouterSmoke.exe --assert-request
#   -> 15 PASS, exit 0   (also: --assert-source 3, --assert-map 15, --assert-gate 7, --assert-errors 5, --assert-legs 11; a 3D schematic route, DEC-051)

# Planner evaluation set (DEC-043 -- 30 questions + automatic checks K-1..K-12; offline, recording fake model, no key).
dotnet build tools/PlannerEval -c Debug
tools/PlannerEval/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.Tools.PlannerEval.exe --assert-checks
#   -> 26 PASS, exit 0   (also: --assert-set 7, --assert-record-stub 9, --assert-drift 5)
# Live recording (DEC-044, owner): needs DEEPSEEK_API_KEY (presence/length only); without it: KEY MISSING, exit 12.
tools/PlannerEval/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.Tools.PlannerEval.exe --record --cases E-01,E-03
#   -> per-case lines + report.txt under %LOCALAPPDATA%/HeyGanesh/eval/<stamp>; then --replay <dir> [--baseline <dir>] offline

# App Shell lesson leg (DEC-027): --lesson teaches with drawn, spoken cards instead of the Tutor's prose.
src/Experience/HeyGanesh.App/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.App.exe --once --lesson --question "x"
#   keys unset -> header 'lesson : ON (--lesson): no lesson model, DEEPSEEK_API_KEY MISSING ...', KEY ERROR (Element Selection), exit 12 (SH-18)
#   with keys: 3D lessons + click-to-ask follow-ups (30 s click wait; Ctrl+Alt refused meanwhile) -- live row SH-20
# Keys (DEC-045): one key ring, Credential Manager first, then the environment; values are never printed.
src/Experience/HeyGanesh.App/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.App.exe --keys
#   -> deepseek / typesafe / fish: MISSING or "present (N chars, <source>)", exit 0
#   store one yourself: HeyGanesh.App.exe --set-key deepseek  (typed or pasted, nothing shown); --remove-key deepseek
dotnet run --project tools/SecretSmoke -c Debug -- --assert-keyring
#   -> 11 PASS, exit 0   (also: --assert-keyring-cm 4, throwaway keyring-test.* credentials only)
# Tray (DEC-052): the app with no console. Build the host (it builds the tray and the shell first), read its exit codes.
dotnet build tools/TraySmoke -c Debug -warnaserror
tools/TraySmoke/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.Tools.TraySmoke.exe --assert-source
#   -> 8 PASS, exit 0   (also: --assert-log 7, --assert-keys-model 13, --assert-startup 10 (a throwaway HKCU key) offline; local-only, real hook for a few seconds: --assert-stop 5, --assert-tray 10, --assert-run 8,
#   --assert-keys-window 11 (shows the key window), --assert-keys-pause 3 (PRESSES Ctrl+Alt once), --assert-startup-menu 7)
#   by hand: src/Experience/HeyGanesh.Tray/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.Tray.exe  (icon menu: status, keys, Open log folder, Quit)
# Installer (DEC-055): per-user, no admin. Build the package, then install it on a PC (or run the round trip locally).
powershell -NoProfile -ExecutionPolicy Bypass -File installer\build.ps1
#   -> PACKAGE OK: artifacts\installer (self-contained win-x64, ~200 MB), exit 0   (-Zip also writes artifacts\installer-win-x64.zip)
tools/TraySmoke/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.Tools.TraySmoke.exe --assert-install
#   -> 16 PASS, exit 0 (temp folders and throwaway HKCU keys; starts the installed tray once)
#   real install (owner): powershell -NoProfile -ExecutionPolicy Bypass -File artifacts\installer\install.ps1
# Turn routing (DEC-036): without --lesson and with TYPESAFE_API_KEY set, Jev picks point / explain / 2D or 3D lesson per turn.
src/Experience/HeyGanesh.App/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.App.exe --once --no-route --question "x"
#   keys unset -> header 'route : off (--no-route ...)', KEY ERROR (Element Selection), exit 12 (SH-21); with keys: routed turns -- live row SH-22
```

Models: **DeepSeek V4.1 Flash** is used for visual grounding and coordinate detection. Live runs require `DEEPSEEK_API_KEY` set in the environment or passed via the Security Proxy module. Element Selection uses the Jev choice model over a ranked UIA manifest via TypeSafe (`ModelId` `jev-1.13.0`); live Element Selection runs require `TYPESAFE_API_KEY`, reported as presence/length only and never as a value — the same temporary direct-key arrangement as Vision's, pending the Security Proxy. Voice's TTS is **Fish Audio** (`format=wav`, `sample_rate=44100`; key `FISH_AUDIO_API_KEY`, same presence/length-only rule), and its speech-in frontend is an externally-run **qwen-audio-agent Gateway** (Gateway Client Protocol wire 7.0; `AGENT_PROTOCOL=none` required, its provider key never crosses the protocol; endpoint via `HEYGANESH_QWEN_GATEWAY_URL` — no default URL on purpose). Both are external runtimes behind `ITtsClient`/`IVoiceListener`, never C# dependencies.

## Architecture invariants — keep them true

Enforced by automated checks (rows in `docs/ai/TEST-CHECKLIST.md`):

- **Zero external NuGet dependencies for core modules.** Core system assemblies (`GlobalHotkeyMonitor`, `ScreenCapture`, `ElementSelection`, `Vision`) use direct Win32 P/Invoke and Windows SDK WinRT interop (`net8.0-windows10.0.22621.0`) with zero external `PackageReference` dependencies.
- **Capture Exclusion Contract.** Any overlay window must call `CaptureExclusion.TryExcludeFromCapture(hwnd)` on its top-level HWND so the capture module does not photograph the app's own UI output.
- **Physical Pixel Coordinate Space.** All bounding box coordinates and monitor boundaries are calculated in physical display pixels (`PixelRect`). Host applications must operate with PerMonitorV2 DPI awareness.
- **API Key Isolation.** API keys must never be logged, printed to console verification tools, or baked into captured screen artifacts. All external LLM requests must isolate credentials behind designated client seams (`IVisionClient`, `IJevClient`) or the Security Proxy module.

Design invariants — hold by review and the cited decisions; no automated check yet:

- **Element Selection and Vision are siblings, not one API.** They must never reference each other: Element Selection is the PRIMARY coordinate path (`ElementSelectionService.SelectAsync`) and Vision the FALLBACK (`VisionService.LocateAsync`), each returning its own result type with a duplicated `ConfidenceLevel` and a matching 0.8/0.5 tier boundary. The ES→Vision fallback is decided by the Context Orchestrator on a *failed* Element Selection result, never by either module (DEC-008 / DEC-009 / DEC-010). Since DEC-037 the orchestrator also asks Vision to check a *found* choice in the Low tier, keeping Element Selection's answer unless Vision is Medium+ and points elsewhere; Medium and High are never checked.

## Environment facts

- Runtime data lives in `%LOCALAPPDATA%/HeyGanesh` (override `HEYGANESH_DATA_DIR`; unit tests use temporary directories).
- Windows 11 (`net8.0-windows10.0.22621.0`), UTF-8 encoding with CRLF line endings (some files carry a BOM — preserve each file's existing encoding when editing).
- Screen capture uses `Windows.Graphics.Capture`; non-packaged hosts must account for OS capture border defaults.
- Low-level keyboard hooks (`WH_KEYBOARD_LL`) operate on dedicated worker threads with strict execution latency bounds (<300 ms `LowLevelHooksTimeout`).

# Working Agreement

You are working in a traced workflow. Speed is not the goal — a reviewable paper trail is.
Every change must be explainable, reversible, and recorded. Follow the protocols below
literally, not in spirit only.

All working documents live in `docs/ai/`. If a file referenced here does not exist, create it
from `docs/ai/TEMPLATES.md` before proceeding.

---

## Session start protocol

Before responding to the first real request of a session, read in this order:

1. `docs/ai/HANDOVER.md` — where things stand right now
2. `docs/ai/CONSTRAINTS.md` — what you are not allowed to do
3. `docs/ai/ARCHITECTURE.md` — the system map
4. `docs/ai/FLOW.md` — only the sections relevant to the current task

Then output a **Session Start Block** before anything else:

```
SESSION START
Model: <your model name and version>
Picking up from: <one line from HANDOVER.md>
Constraints in force: <the 2–3 constraints that actually apply to this task>
Open risks: <anything HANDOVER.md flags as broken or in progress>
```

Do not start editing files until this block has been printed.

---

## Before writing any code

**Plan first, always.** For any request beyond a one-line fix, respond with a plan and stop.
Do not write code in the same turn as the plan unless I explicitly say "plan and implement."

A plan contains:

- The problem restated in your own words
- Files you intend to touch, and what changes in each
- The execution path affected (reference `FLOW.md`; if the path isn't documented, say so)
- At least one alternative you considered and why you rejected it
- What could break, and how we'd know
- How this will be verified (specific commands from `docs/ai/TEST-CHECKLIST.md`)

**Scope control.** One logical change per request. If I ask for something that spans multiple
logical changes, do not attempt it — split it into a numbered sequence, show me the sequence,
and implement only step 1 after I confirm. Refusing to over-scope is expected behavior here,
not unhelpfulness.

**Constraint check.** If the task touches anything listed in `CONSTRAINTS.md`, stop and say so
explicitly. Never work around a constraint because it seems inconvenient or outdated. Ask.

---

## While writing code

- **Comment intent, not syntax.** Do not write `// increment counter`. Do write what a block
  is for, what calls into it, what it assumes already exists, and what breaks if it's removed.
- Match existing patterns in the file over your preferred style.
- No new dependencies. If one is genuinely required, stop and ask, with the reason and at
  least one no-dependency alternative.
- No opportunistic refactors, renames, formatting sweeps, or "while I was in there" cleanups.
  If you notice something worth fixing, add it to `HANDOVER.md` under Open Items instead.
- Do not delete or rewrite code you don't understand. Flag it.

---

## After every change

Report in this exact shape:

```
CHANGED
- <file>: <what changed, one line>

WHY
- <reasoning, including the tradeoff accepted>

VERIFY
- <exact commands I should run>
- <exact expected output>

NOT VERIFIED
- <anything you could not test, honestly listed>
```

Rules for this block:

- Never claim something "works," "is fixed," or "should be fine" unless you ran the
  verification and can show the output. Distinguish *I ran this and it passed* from
  *this ought to pass*. If you didn't run it, it goes under NOT VERIFIED.
- If a change is large, risky, touches data, or affects more than three files, write the
  rollback plan into `docs/ai/ROLLBACK.md` **before** making the change, and reference it here.
- I read the full diff, every time. Write diffs to be read: small, coherent, no unrelated noise.

---

## Documents you maintain

Update these as part of the work, not as an afterthought. A change is not complete until its
documents are updated.

| File | You update it when | Contains |
|---|---|---|
| `HANDOVER.md` | End of every session, and after any status change | Current state: done, in progress, broken, avoid |
| `DECISIONS.md` | Any time a non-obvious choice is made | Date, model version, decision, reasoning, alternatives rejected |
| `FLOW.md` | Whenever execution paths change | What calls what, in what order, across files |
| `ARCHITECTURE.md` | Only on structural change | Modules, services, how data moves |
| `CONSTRAINTS.md` | Never — I own this file | Hard boundaries |
| `TEST-CHECKLIST.md` | When a new verification step is needed | Real commands, real expected outputs |
| `ROLLBACK.md` | Before risky changes | Commit to revert to, files to restore, what to re-check |
| `bugs/BUG-<id>.md` | Throughout any bug investigation | Symptom, reproduction, hypotheses tried, what worked, verification |
| `features/FEATURE-<id>.md` | Throughout any feature build | Scope, decisions, status, what's left |

Bug and feature files are living traces. Log failed attempts too — knowing what *didn't* work
is often more valuable than the fix. Anyone should be able to read one cold and pick it up.

**Version-pin every entry.** In `DECISIONS.md`, `HANDOVER.md`, and bug/feature files, prefix
entries with the date and the model that made them, e.g. `2026-08-10 · claude-opus-5`. When
debugging later, knowing which model reasoned through a change matters.

---

## Session end protocol

When I say "wrap up," "end session," or the work reaches a natural stopping point, append to
`HANDOVER.md`:

```
## <date> · <model version>
Did: <what actually landed>
Left: <what's unfinished, and where it stops>
Watch: <traps, fragile spots, things I'd get wrong next time>
Next: <the single next action>
Verified: <what was actually run and passed>
```

Five lines. Then confirm what was written. Do not end a session silently.

---

## Standing rules

1. **Never present a summary as a substitute for the diff.** Summaries compress; I need the
   compression to be my job, not yours.
2. **Uncertainty is information.** Say "I don't know," "I'm guessing here," or "this assumes X"
   plainly. A confident wrong answer costs far more than an uncertain right one.
3. **If I can't explain the change in my own words, I shouldn't accept it.** If I approve
   something quickly and you suspect I haven't understood it, say so and offer a walkthrough.
   The documents exist to support my mental model, not replace it.
4. **Permission is scoped, never blanket.** An approval covers the change we discussed and
   nothing adjacent to it.
