# FEATURE-002 — Screen Overlay

- **Date:** 2026-09-24
- **Status:** done — implementation plus offline verification; multi-monitor placement is single-display-verified only
- **Module(s):** Screen Overlay (consumes Context Orchestrator's `GuidanceResult`)

## Description

One resolved verdict becomes one temporary highlight: the overlay draws the `GuidanceResult` box and
center on click-through layered windows — one per monitor the box touches — for a bounded duration,
then removes itself. No-match and failure verdicts draw nothing; cancellation removes the overlay
cleanly.

## Out of scope

- The Jev confidence-gate, any confidence-gated display policy, Tutor Engine, Visual Lesson Engine,
  Voice pipeline, Notes persistence, Security Proxy.
- WinUI 3 / Windows App SDK in this module (stays the shell direction — DEC-011); error *presentation*
  (this module reports `failure`, the shell decides what the user sees); tracking or re-resolving
  stale coordinates.
- Any change to `SelectionResult` / `VisionResult` / `GuidanceResult`, any shared abstraction
  assembly, any confidence threshold.
- Any input injection of any kind (see `CONSTRAINTS.md`).

## Acceptance criteria

- [x] Consumes `GuidanceResult` directly — no re-mapping, no new result type, no shared abstraction (DEC-011).
- [x] `Failed` is checked before `Found`: the CO-6 fallback not-found shape classifies as `failure`, never as a displayable verdict (SO-5).
- [x] `Found=false` draws nothing and creates no window (SO-4).
- [x] Click-through is explicit: `WM_NCHITTEST → HTTRANSPARENT` on every point plus `WS_EX_NOACTIVATE` (SO-7).
- [x] Capture exclusion is a hard gate on every real top-level HWND (SO-7, flips EX-5 ✅).
- [x] Coordinates are used verbatim in global physical pixels; the host owns PerMonitorV2 awareness (SO-2/3).
- [x] Bounded duration with clean removal on timeout, `HideAsync`, cancellation and disposal (SO-3, SO-6).
- [x] One show at a time: a second `ShowAsync` is refused, never queued (SO-8).
- [x] Zero new package dependencies (SO-2).
- [x] 25× show/hide soak leaks no GDI objects or handles (SO-9).
- [ ] Multi-monitor placement across real displays (needs a second display).
- [ ] Off-screen / zero-size box paths on real hardware (coded, unexercised).

## Test checklist

SO-1..SO-9 in [TEST-CHECKLIST.md](../TEST-CHECKLIST.md).

## Rollback

Delete `src/Platform/HeyGanesh.ScreenOverlay/` and `tools/OverlaySmoke/` and revert this session's doc
edits; the 2026-09-24 entry in [ROLLBACK.md](../ROLLBACK.md) records the point. No existing source
file is touched by this feature, so nothing else needs reverting.