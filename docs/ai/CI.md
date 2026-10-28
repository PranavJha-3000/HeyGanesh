# CI — HeyGanesh Continuous Integration Gates

> This file specifies the exact gate commands, data fields, exit contracts, failure criteria, and local-only markers for the HeyGanesh CI suite. It is the authoritative specification; the `.github/workflows/ci.yml` workflow runs these same commands. All checks are offline/key-free unless explicitly noted.

## Gates (in execution order)

| Gate | Command | Data fields / Expected | Exit contract |
|------|---------|------------------------|---------------|
| **G-1 Build** | `dotnet build -c Debug -warnaserror` | `buildWarnings=0` `buildErrors=0` | exit 0 |
| **G-2 Zero-NuGet** | `pwsh -File .github/workflows/scan-secrets.ps1 -ZeroNuGet` — XML check over the 14 core `.csproj` files listed in that script (inspects descendant `PackageReference` elements) | `projectWarnings=0` `packageRefs=0` | exit 0 |
| **G-3 Screen Capture builds** | `dotnet build tools\ScreenCaptureSmoke -c Debug -warnaserror` (build only: running the host takes real captures) | `screenCaptureBuild=pass` `screenCaptureZeroWarnings=pass` | exit 0 |
| **G-4 Element Selection offline** | `dotnet run --project tools\ElementSelectionSmoke -c Debug -- --question "Save" --fake` and `--fake --fake-none` | `esFakes=pass` `esFakesNone=pass` | exit 0 (local-only; excluded from automated CI) |
| **G-5 Context Orchestrator offline** | `--question "Save" --fake`, `--fake-none`, `--fake-unmapped` | `coFakes=pass` `coFakesNone=pass` `coFakesUnmapped=pass` | exit 0 (local-only; excluded from automated CI) |
| **G-6 Overlay offline** | `--no-match`, `--found --duration-ms 1500`, `--assert-clickthrough` | `overlayNoMatch=pass` `overlayFound=pass` `overlayClickthrough=pass` | exit 0 (CI runs `--no-match` only; the other two are local-only) |
| **G-7 Tutor Engine offline** | `--fake-explain "what does git rebase actually do"`, `--assert-prompt`, `--assert-grounding`, `--assert-parity`, `--assert-source`, `--assert-client-error` | `tutorFake=pass` `tutorAssertPrompt=pass` `tutorAssertGrounding=pass` `tutorAssertParity=pass` `tutorAssertSource=pass` `tutorAssertClientError=pass` | exit 0 |
| **G-8 Vision offline (no provider)** | `dotnet run --project tools\VisionSmoke -c Debug -- --question "x" --samples 2` | `visionBuild=pass` `visionOffline=pass` | exit 0 |
| **G-9 Voice offline** | `--fake-transcript "where is the Save button"`, `--assert-tutor-authority`, `--speak-fake --text "Found the Save button"` | `voiceFakeTranscript=pass` `voiceAssertTutorAuthority=pass` `voiceSpeakFake=pass` | exit 0 (local-only; excluded from automated CI) |
| **G-10 Forbidden-API scan** | `pwsh -File .github/workflows/scan-forbidden.ps1` | `forbiddenApiScan=pass` (clean) or `forbiddenApiScan=FAIL` | exit 0 clean / non-zero on violation |
| **G-11 Secret scan** | `pwsh -File .github/workflows/scan-secrets.ps1` | `secretScan=pass` (clean) or `secretScan=FAIL` | exit 0 clean / non-zero on credible finding |
| **G-12 Security offline** | `dotnet run --project tools\SecretSmoke -c Debug` | `secretSmoke=pass` | exit 0 |
| **G-13 Visual Lesson Engine offline** | build `tools\LessonSmoke`, then the EXE with each of `--assert-source` `--assert-parse` `--assert-limits` `--assert-refs` `--assert-degrade` `--assert-fixtures` `--assert-play` `--assert-play-degraded` `--assert-controls` `--assert-plot` `--assert-grounded` | `lessonEngine=pass` | exit 0 for every mode |
| **G-14 Lesson Planner offline** | build `tools\LessonPlannerSmoke`, then the EXE with each of `--assert-source` `--assert-prompt` `--assert-multimodal` `--assert-plan` `--assert-repair` `--assert-errors` `--assert-followup` `--assert-hardening` | `lessonPlanner=pass` | exit 0 for every mode |
| **G-15 Lesson Turn offline** | build `tools\LessonTurnSmoke`, then the EXE with each of `--assert-source` `--assert-turn` `--assert-turn-degraded` `--assert-turn-rejected` `--assert-turn-errors` | `lessonTurn=pass` | exit 0 for every mode |

## Local-Only Notes

The following existing TEST-CHECKLIST.md rows **cannot run in a headless/CI pipeline** because they require an interactive Windows desktop or a second monitor. They are marked `local-only` and are **not executed** in the workflow:

- S-3, V-1, V-2, SC-1..SC-8, EX-1..EX-5, CO-1..CO-7, ES-1..ES-7, SO-1..SO-9, VO-1..VO-7 — all require a live Windows display.
- G-8, G-9 — Vision and Voice smoke invocations are local-only; excluded from automated CI per safety uncertainty (per S0.4 plan §4).
- G-4, G-5 — 2026-10-29 · claude-opus-5-5: local-only. Their fake Jev still chooses from the REAL foreground window's UIA tree, so the verdict depends on what is on screen: the same command passed and then failed on one machine with no code change.
- G-6 `--found` / `--assert-clickthrough`, LessonSurfaceSmoke, LT-6, LT-7 — draw real windows; local-only.

## Workflow Reference

The `.github/workflows/ci.yml` workflow executes every gate listed above in the exact order shown. No gate is omitted or reordered. The workflow is the single source of truth for CI execution; `docs/ai/CI.md` is the human-readable specification.

## False-Positive Handling

- `G-2` (Zero-NuGet): Only `<PackageReference>` elements cause failure; `<ProjectReference>` is preserved.
- `G-10` (Forbidden-API): Declaration-only cases in `src\Platform\HeyGanesh.ScreenCapture\NativeMethods.cs` (`setcursorpos`) and `src\Perception\HeyGanesh.ElementSelection\UiaInterop.Interfaces.cs` (`setfocus`) are exempted and reported but do not fail CI.
- `G-11` (Secret): Bare `DEEPSEEK_API_KEY`, `FISH_AUDIO_API_KEY`, `TYPESAFE_API_KEY` names with no `=` or value are not findings. Key-value patterns require 16+ alphanumeric characters after `=` or `:`.