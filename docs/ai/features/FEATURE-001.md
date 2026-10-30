# FEATURE-001 — Context Orchestrator

- **Date:** 2026-09-23
- **Status:** done — implementation plus offline verification; the live end-to-end session is still unverified
- **Module(s):** Context Orchestrator (composes Screen Capture, Element Selection and Vision)

## Description

One hotkey trigger becomes one guidance session: capture the screen once, run the PRIMARY coordinate path
(Element Selection), fall back to Vision only on a genuine primary failure, and hand the next module a
single normalized verdict (`GuidanceResult`) carrying the box in global physical pixels, a confidence tier
and every raw signal both legs expose.

## Out of scope

- The Jev confidence-gate (falling back on a *tier* rather than on a failure), Tutor Engine, Visual Lesson
  Engine, Screen Overlay, Voice pipeline, Notes persistence, Security Proxy.
- Subscribing to `GlobalHotkeyMonitor.Pressed` inside the orchestrator, and any question source (Voice).
- Any change to `SelectionResult` / `VisionResult`, any shared abstraction assembly, any confidence threshold.
- Any input injection of any kind (see `CONSTRAINTS.md`).

## Acceptance criteria

- [x] One session = one capture, taken before either leg runs; frames retained on the verdict (`RetainCaptures`).
- [x] Element Selection runs first; Vision runs only when the primary result is `Failed`.
- [x] `Choice == "none"` is a successful no-match and never triggers Vision (CO-4).
- [x] The fallback runs exactly once per session, on the cursor's frame (CO-5).
- [x] The two leg result types stay separate; normalization happens only inside the orchestrator (CO-2, DEC-010).
- [x] No threshold is applied anywhere; the tier is carried, not gated.
- [x] A second trigger during a session is refused, not queued (CO-10).
- [x] Cancellation and provider failures propagate unchanged (CO-7, CO-8).
- [x] Zero new package dependencies (CO-2).
- [ ] Live end-to-end session with both API keys (blocked on the ES live Jev leg — see TEST-CHECKLIST.md).
- [ ] Cursor-monitor-first capture ordering across multiple displays (needs a second display).

## Test checklist

CO-1..CO-11 in [TEST-CHECKLIST.md](../TEST-CHECKLIST.md).

## Rollback

Delete `src/Intelligence/HeyGanesh.ContextOrchestrator/` and `tools/ContextOrchestratorSmoke/` and revert this session's
doc edits; the 2026-09-23 entry in [ROLLBACK.md](../ROLLBACK.md) records the point. No existing source file
is touched by this feature, so nothing else needs reverting.
