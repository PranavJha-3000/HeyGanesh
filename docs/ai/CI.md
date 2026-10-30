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
| **G-12 Security offline** | build `tools\SecretSmoke`, then the EXE with each of `--self-test` (the Credential Manager store) `--assert-keyring` (KR-1, DEC-045) `--assert-keyring-cm` (KR-2: the real Credential Manager under throwaway `keyring-test.*` targets, removed after) | `secretSmoke=pass` | exit 0 for every mode |
| **G-13 Visual Lesson Engine offline** | build `tools\LessonSmoke`, then the EXE with each of `--assert-source` `--assert-parse` `--assert-limits` `--assert-refs` `--assert-degrade` `--assert-fixtures` `--assert-play` `--assert-play-degraded` `--assert-controls` `--assert-plot` `--assert-grounded` `--assert-scene3d` `--assert-schematic` (LS-14, DEC-048) | `lessonEngine=pass` | exit 0 for every mode |
| **G-14 Lesson Planner offline** | build `tools\LessonPlannerSmoke`, then the EXE with each of `--assert-source` `--assert-prompt` `--assert-multimodal` `--assert-plan` `--assert-repair` `--assert-errors` `--assert-followup` `--assert-hardening` `--assert-prompt-3d` (LP-10, DEC-032) `--assert-stream` (LP-11, DEC-041) `--assert-prompt-schematic` (LP-12, DEC-049) | `lessonPlanner=pass` | exit 0 for every mode |
| **G-15 Lesson Turn offline** | build `tools\LessonTurnSmoke`, then the EXE with each of `--assert-source` `--assert-turn` `--assert-turn-degraded` `--assert-turn-rejected` `--assert-turn-errors` `--assert-followup-pick` (LT-8, DEC-031) `--assert-turn-stream` (LT-10, DEC-042) | `lessonTurn=pass` | exit 0 for every mode |
| **G-16 Part Library offline** | build `tools\PartLibrarySmoke`, then the EXE with each of `--assert-source` `--assert-library` `--assert-physics` `--assert-catalog` `--assert-schematic` (PL-6, DEC-048) (`--dump` is the manual PL-5 row, not a gate) | `partLibrary=pass` | exit 0 for every mode |
| **G-17 Lesson Surface offline** | build `tools\LessonSurfaceSmoke`, then the EXE with `--assert-source` (LSU-1 + LSU-7: one NuGet, references, CSPs, no sinks, vendored Babylon hashes, licence, `.gitattributes`) and `--assert-scene-math` (LSU-8) | `lessonSurface=pass` | exit 0 for every mode (the window rows LSU-2..LSU-6 and LSU-9..LSU-14 are local-only) |
| **G-18 Turn Router offline** | build `tools\TurnRouterSmoke`, then the EXE with each of `--assert-source` `--assert-request` `--assert-map` `--assert-gate` `--assert-errors` `--assert-legs` (TR-1..TR-6, DEC-035/DEC-036) | `turnRouter=pass` | exit 0 for every mode |
| **G-19 Context Orchestrator check rule** | build `tools\ContextOrchestratorSmoke`, then the EXE with `--assert-check` (CO-12, DEC-037; the other orchestrator rows walk a real window and stay local-only) | `coCheckRule=pass` | exit 0 |
| **G-20 Element Selection text merge** | build `tools\ElementSelectionSmoke`, then the EXE with `--assert-text-merge` (ES-8, DEC-039; the other Element Selection rows walk a real window and stay local-only) | `esTextMerge=pass` | exit 0 |
| **G-21 Voice pre-roll** | build `tools\VoiceSmoke`, then the EXE with `--assert-preroll` (VO-11, DEC-040: the real client against a loopback HttpListener stub; no microphone, device or network) | `voicePreroll=pass` | exit 0 |
| **G-22 Planner evaluation set** | build `tools\PlannerEval`, then the EXE with each of `--assert-set` `--assert-checks` `--assert-record-stub` `--assert-drift` (PE-1, PE-2, PE-4, PE-5, DEC-043/DEC-044: no key; PE-4 uses a loopback stub and a dummy key); then `--replay tools\PlannerEval\baseline` once that baseline exists (PE-7: exit 1 on a regression, 3 when the planner prompt changed), otherwise "no baseline yet" | `plannerEval=pass` | exit 0 for every mode |
| **G-23 Key sources** | build `tools\ElementSelectionSmoke`, `tools\VisionSmoke`, `tools\VoiceSmoke`, then `--assert-key-source` (ES-10), `--assert-key-source` (V-9), `--assert-fish-format` (VO-8, now with the key-source checks), DEC-046 | `keySources=pass` | exit 0 for every mode |
| **G-24 Tray offline** | build `tools\TraySmoke`, then `--assert-source` (TY-1), `--assert-log` (TY-2) `--assert-keys-model` (TK-1) and `--assert-startup` (TS-1, a throwaway HKCU key, deleted after), DEC-052..DEC-054; TY-3..TY-5, TK-2, TK-3 and TS-2 need an interactive desktop and stay local | `tray=pass` | exit 0 for every mode |
| **G-25 Installer package** | `powershell.exe -File installer\build.ps1` (IN-1, DEC-055): a self-contained win-x64 publish of the tray into `artifacts\installer\app`, the scripts beside it, and the files a package needs checked; the runner downloads the .NET runtime packs from nuget.org; the install round trip (IN-2) is local | `installer=pass` | exit 0, `PACKAGE OK` |
| **G-26 Notes store offline** | build `tools\NotesSmoke`, then `--assert-source` `--assert-store` `--assert-migrate` `--assert-concurrent` `--assert-corrupt` `--assert-retention` (NS-1..NS-6, DEC-058): Windows' own `winsqlite3`, temporary databases only | `notesStore=pass` | exit 0 for every mode |

## Local-Only Notes

The following existing TEST-CHECKLIST.md rows **cannot run in a headless/CI pipeline** because they require an interactive Windows desktop or a second monitor. They are marked `local-only` and are **not executed** in the workflow:

- S-3, V-1, V-2, SC-1..SC-10 (SC-9/SC-10 also need an OCR language), EX-1..EX-5, CO-1..CO-7, ES-1..ES-7, SO-1..SO-9, VO-1..VO-7 — all require a live Windows display.
- G-8, G-9 — Vision and Voice smoke invocations are local-only; excluded from automated CI per safety uncertainty (per S0.4 plan §4).
- G-4, G-5 — 2026-10-29 · claude-opus-5-5: local-only. Their fake Jev still chooses from the REAL foreground window's UIA tree, so the verdict depends on what is on screen: the same command passed and then failed on one machine with no code change.
- G-6 `--found` / `--assert-clickthrough`, LessonSurfaceSmoke, LT-6, LT-7 — draw real windows; local-only.

## Workflow Reference

The `.github/workflows/ci.yml` workflow executes every gate listed above in the exact order shown. No gate is omitted or reordered. The workflow is the single source of truth for CI execution; `docs/ai/CI.md` is the human-readable specification.

## False-Positive Handling

- `G-2` (Zero-NuGet): Only `<PackageReference>` elements cause failure; `<ProjectReference>` is preserved.
- `G-10` (Forbidden-API): Declaration-only cases in `src\Platform\HeyGanesh.ScreenCapture\NativeMethods.cs` (`setcursorpos`) and `src\Perception\HeyGanesh.ElementSelection\UiaInterop.Interfaces.cs` (`setfocus`) are exempted and reported but do not fail CI.
- `G-11` (Secret): Bare `DEEPSEEK_API_KEY`, `FISH_AUDIO_API_KEY`, `TYPESAFE_API_KEY` names with no `=` or value are not findings. Key-value patterns require 16+ alphanumeric characters after `=` or `:`.