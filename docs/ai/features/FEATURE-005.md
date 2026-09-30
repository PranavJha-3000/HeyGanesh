# FEATURE-005 — App Shell push-to-talk voice wiring

**Status:** implemented (offline rows green; the live chord rows are human-gated)
**Date opened / closed:** 2026-09-30
**Decisions:** [DEC-014](../DECISIONS.md#dec-014--2026-09-30--cline-agent-model-id-not-exposed-to-the-agent), [DEC-013](../DECISIONS.md#dec-013--2026-09-30--cline-agent-model-id-not-exposed-to-the-agent)
**Rows:** SH-10, SH-11, SH-12a/b/c (automated), SH-13, SH-14 (live half) in `docs/ai/TEST-CHECKLIST.md`
**Rollback:** entry dated 2026-09-30 in `docs/ai/ROLLBACK.md`

## Scope

Wire the existing Voice seam into the App Shell as the production question source, and retire
`--question` from the normal product flow:

- `Ctrl+Alt Pressed` → `VoiceService.ListenOnceAsync` starts the utterance (on the thread pool).
- `Ctrl+Alt Released` → ends the utterance.
- A committed `UtteranceResult.FinalText` → `options.Question` → the unchanged
  `ContextOrchestratorService.HandleHotkeyPressedAsync` → `GuidanceResult` → `ScreenOverlayService.ShowAsync`.
- `--question` survives only as the legacy `--once` scripted path and is ignored (with a warning) in
  hooked mode. `--once` without it runs one live listen.
- Exit/error semantics preserved; `IsNoSpeech` resolves nothing; no overlapping turns; no screen,
  cursor or action behaviour changes; no new dependency; no UI framework.

## Out of scope (deliberately)

- **Tutor Engine** — not implemented, not stubbed. `VoiceService.SpeakAsync` is composed and never
  called; the Tutor inserts between the committed transcript and the resolve in a later change.
- **The Voice module itself** — untouched this session (no edits to `src/HeyGanesh.Voice/`).
- **Force-commit on release** — needs a Voice-module change; recorded as an accepted limitation in
  DEC-014 instead.
- **A `ShellSmoke` host** — the shell carries no `--fake` flags (DEC-012) and keystrokes cannot be
  injected (DEC-010), so press/release timing stays human-gated. Filed as the next step below.
- **The Gateway deployment itself** — external runtime, run by the operator.

## Decisions made while building

- **One CTS per turn, created synchronously on press.** If the pool had not yet started the listen when
  the release arrived, there would be nothing to cancel and the microphone would keep running. The CTS
  is disposed by whoever starts the next turn, by `RunDirectAsync`, or by `DrainAsync` — never by the
  turn itself, which could race a release still holding the reference.
- **`options with { Question = … }` instead of a new parameter.** `RunSessionAsync` stays the one
  session path, reading the same field, so the legacy and voice paths cannot drift.
- **`OnReleased` cancels outside the gate.** Cancellation runs callbacks that take the same lock.
  Safe because `Pressed`/`Released` share the monitor's worker thread and no path disposes an abandoned
  CTS; `hotkey.Stop()` joins that thread before `DrainAsync` disposes anything.
- **Exit 1 for a `Failed` utterance.** `UtteranceResult.FailureReason` is a returned string, so 12-vs-3
  is not recoverable without text sniffing and Voice is frozen. Documented deviation (DEC-014).
- **A release-abandoned turn exits 0, not 13.** A user letting go of the chord is not Ctrl+C; 13 would
  claim a cancellation that did not happen.

## Verification actually run

`dotnet build src\HeyGanesh.App -c Debug` → `Build succeeded. 0 Warning(s) 0 Error(s)`.
SH-10 (0 `PackageReference`, 4 refs), SH-11, SH-12a, SH-12b, SH-12c, SH-14's `Failed` half — all as
recorded in TEST-CHECKLIST. Regression sweep: VO rows (`--fake-transcript`, `--fake-nospeech`,
`--assert-tutor-authority`, `--assert-gcp-events`, `--assert-no-backend`, `--assert-fish-format`,
`--burst-fake`), CO `--fake`, SO `--found` — all still green, confirming the other modules are untouched.

## Not verified

- **SH-13 / SH-14 silence half** — need a live Gateway, keys, a microphone and a human hand.
- **`HotkeySmoke --seconds 4`** — the documented row could not complete in this non-interactive shell
  (it needs real keystrokes and never self-terminated; two orphans were killed). The shell's own hook
  row (SH-12a) covers install/teardown instead. Unrelated to this change: the hotkey module was not
  modified.

## What's left / next

1. `ShellSmoke`: a verification host with an injectable trigger and fake `IVoiceListener`, so
   press/release ordering, the IsNoSpeech short-circuit and REFUSED-during-listen become automated
   rather than human-gated.
2. Live SH-13/SH-14 run, which also confirms the two protocol details DEC-013 marked unverified.
3. The Tutor Engine, then `SpeakAsync` wiring — a separate change on top of this seam.