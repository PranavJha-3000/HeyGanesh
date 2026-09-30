# FEATURE-003 — App Shell (hotkey → Context Orchestrator → Screen Overlay)

- **Date:** 2026-09-29
- **Status:** in-progress — composition and wiring verified offline; the live end-to-end path (real chord → real verdict → real highlight) needs both API keys and a human hand, and is not yet run
- **Module(s):** App Shell (new); drives Global Hotkey Monitor, Context Orchestrator, Screen Overlay, Screen Capture

## Description

The process that finally connects the modules: it sets PerMonitorV2 DPI awareness for the process,
owns one ScreenCaptureService, subscribes `GlobalHotkeyMonitor.Pressed`, maps one chord press to one
`ContextOrchestratorService.HandleHotkeyPressedAsync`, and hands the resulting `GuidanceResult` to
`ScreenOverlayService.ShowAsync`. Press Ctrl+Alt, and the thing you asked about is outlined on screen.

Because the Voice pipeline does not exist, `--question "<text>"` supplies the question and `--once`
runs exactly one session without installing the keyboard hook — so the whole wiring can be exercised
by an operator, or by a script, with no microphone and no keystrokes.

## Out of scope

- The Voice pipeline (STT/TTS). `--question` stands in for it; when it lands, the trigger moves from
  `Pressed` to `Released` (record on press, submit the transcript on release) and `--question` retires.
- WinUI 3 / Windows App SDK, a tray icon, or any window this process would draw. DEC-011 deferred the
  UI framework and DEC-012 defers it again; the overlay already is the UI, and it is Win32.
- Tutor Engine, Visual Lesson Engine, Notes persistence, Security Proxy, the Jev confidence-gate.
- Any input injection. The shell reads keys and draws; it never clicks, types, or focuses anything.
- Error *presentation* beyond a console line (DEC-011 left that to the shell; this is the shell's first,
  deliberately plain answer).
- Fake leg clients. Test doubles belong in verification hosts, so the shell has no `--fake` flags and
  its offline coverage stops where the real key gates stop.

## Acceptance criteria

- [x] Dependency-free: zero `PackageReference`, `net8.0-windows10.0.22621.0` (SH-2).
- [x] Sets process-wide PerMonitorV2 awareness before any coordinate work, and reports what the process
      actually got rather than trusting the setter's return value (SH-4 header line).
- [x] `monitor.Pressed` → `HandleHotkeyPressedAsync` → `ShowAsync` is the only path, and `--once` runs
      the identical method, so the scripted and human-driven paths cannot drift (SH-4, SH-9).
- [x] The hook callback is never blocked: the handler hands the session to the thread pool and returns.
- [x] One session at a time, refused rather than queued, spanning the overlay's visible duration (SH-5).
- [x] A leg's auth/HTTP failure propagates as its own exit code instead of becoming a verdict (SH-4, SH-9).
- [x] Ctrl+C cancels the session in flight, then tears down in a fixed order — hook, session, overlay,
      orchestrator, legs — and reports both hook health and the monitor's released state (SH-5, SH-7).
- [x] A missing question is a refusal to start (exit 2), not a defaulted question (SH-6).
- [ ] A live session with both keys: a real chord produces a real verdict and a real highlight, and no
      window is left behind.
- [ ] The `REFUSED` overlap path driven by two real chord presses.
- [ ] The overlay's `not-per-monitor-dpi-aware` refusal reached through `--no-dpi` (the flag and its
      header line work; the refusal itself needs a verdict, which needs a key).

## Test checklist

SH-1..SH-9 in [TEST-CHECKLIST.md](../TEST-CHECKLIST.md).

## Rollback

Delete `src/HeyGanesh.App/` and `docs/ai/features/FEATURE-003.md`, then revert this session's doc edits.
No existing source file is edited by this feature, so no module behaviour can regress — the 2026-09-29
entry in [ROLLBACK.md](../ROLLBACK.md) records the point and what to re-check afterwards.
