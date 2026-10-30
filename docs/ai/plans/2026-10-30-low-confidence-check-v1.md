# Plan: a Low-confidence Element Selection choice is checked by Vision (build sequence step 3)

2026-10-30 · claude-opus-5-5 · STATUS: BUILT 2026-10-30 (owner: "Yes, on by default"): CO-12 13 PASS; CO-13 both forms
PASS locally; CO-3..CO-11 re-run with their old exits (0, 0, 0, 0, 13, 3, 0, 0); App rows unchanged (help 0, 12, 12, 12,
13, 13). Found while building: the host's per-mode expectation needed explicit rows for the new mode, placed before the
not-found rows.

## Owner decision recorded

This reverses, for the Low tier only, DEC-010's rejection of a confidence gate (Option C) and DEC-009's "classification,
not a gate", and it rewrites one sentence of an AGENTS.md design invariant. Both were flagged before building; the owner
chose the check on by default.

## Problem, restated

Element Selection grades each choice from Jev's own numbers (blended = 0.5 × Jev confidence + 0.5 × winner probability:
High ≥ 0.8, Medium ≥ 0.5, Low below). A Low choice is drawn and taught exactly like a High one; Vision runs only when
Element Selection fails. When Jev picked something but said it was unsure, the user got no second opinion.

## The rule

Only found + Low is checked (never High, Medium, the honest no-match or a failure). Vision looks at the same frame:

| Vision's answer | Verdict |
|---|---|
| Centre inside Element Selection's box | Element Selection's ("Vision agrees") |
| Elsewhere, Medium or High | Vision's |
| Elsewhere Low, not found, failed, or threw (incl. no `DEEPSEEK_API_KEY`) | Element Selection's, with the reason |
| Cancelled | Unwinds as today (exit 13) |

## Files

- `src/Intelligence/HeyGanesh.ContextOrchestrator/ContextOrchestratorOptions.cs`: `CheckLowConfidence` (default on).
- `.../ContextOrchestratorService.Check.cs` (new): `LowConfidenceCheck.Applies/Decide`, `CheckDecision`, the check step.
- `.../ContextOrchestratorService.cs`: phase `CheckingConfidence = 5`; one branch before the non-failed return.
- `src/Foundation/HeyGanesh.Abstractions/SharedTypes.cs`: `GuidanceResult.ConfidenceCheck`.
- `src/Experience/HeyGanesh.App/ShellHost.Session.cs`: `PrintVerdict` prints `check :`.
- `tools/ContextOrchestratorSmoke`: `--assert-check` (CO-12, new Check.cs), `--fake-low` (CO-13), expectations, output line.
- CI G-19; docs: DEC-037, AGENTS.md invariant + command, FLOW, ARCHITECTURE, TEST-CHECKLIST, CI.md, HANDOVER, ROLLBACK (first).

## Alternatives rejected

The check inside Element Selection (siblings never reference each other); a Jev component or second Jev call; checking
Medium or always running Vision; blending the two scores.

## What could break, and how we'd know

More screenshots to DeepSeek on Low turns (visible as `check :`); existing rows (re-run, unchanged); the new
`GuidanceResult` property (solution build, every gate); latency on Low turns (one Vision call).

## Verification

Orchestrator, host and App build `-warnaserror` 0/0; CO-12; CO-3..CO-11 and CO-13 locally; App rows; local CI 15 gates.
Not verifiable offline: whether the check improves real answers (keys + the model shoot-out).
