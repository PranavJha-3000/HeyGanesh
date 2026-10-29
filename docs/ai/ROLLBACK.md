## 2026-10-24 · Cline agent (M1/R2: Repository Structure)

- **Change:** create six new directories (`config/`, `prompts/`, `eval/`, `tests/`, `docs/product/`, `docs/architecture/`) each with a purpose README.md; no source changes, no project moves, no dependencies.
- **Rollback point:** delete the six directories and their README.md files; revert this ROLLBACK.md entry if needed.
- **Files to remove:** `config/README.md`, `prompts/README.md`, `eval/README.md`, `tests/README.md`, `docs/product/README.md`, `docs/architecture/README.md`
- **Re-check after revert:** `Get-ChildItem -Directory` should show no new directories at repo root; `git status` should be clean aside from this rollback entry.
- **Verified:** directory listing confirms six new directories created; no existing files modified.
## 2026-10-23 · Cline agent (S0.4: CI and Safety Gates)
## 2026-10-24 · Cline agent (M1/R3: Platform Layer Relocation)

- **Change:** move three platform-layer projects to nested paths under `src/Platform/`: `GlobalHotkeyMonitor`, `ScreenCapture`, `ScreenOverlay` via `git mv`. All project files, namespaces, assemblies, APIs, and source contents preserved intact. Solution file and `Directory.Build.props` references updated to new paths.
- **Rollback point:** delete the three relocated directories under `src/Platform/`, revert `src/HeyGanesh.sln` and `Directory.Build.props` to original paths, restore original directory layout.
- **Files to restore:** `git mv src/Platform/HeyGanesh.GlobalHotkeyMonitor/ src/HeyGanesh.GlobalHotkeyMonitor/`, `git mv src/Platform/HeyGanesh.ScreenCapture/ src/HeyGanesh.ScreenCapture/`, `git mv src/Platform/HeyGanesh.ScreenOverlay/ src/HeyGanesh.ScreenOverlay/`, then restore `HeyGanesh.sln` and `Directory.Build.props` to reference original `src/HeyGanesh.*` paths (no wildcards, explicit project references).
- **Re-check after revert:** `dotnet build -c Debug src\\HeyGanesh.GlobalHotkeyMonitor\\HeyGanesh.GlobalHotkeyMonitor.csproj` plus all 8 remaining original-project builds; `dotnet build -c Debug` (no solution) should behave as pre-R3. All original verification hosts (ES smoke, Vision smoke, Tutor smoke, etc.) must pass with original arguments.
- **Dependencies:** No new NuGet, no target-framework changes, no source modifications. Pure relocation.

- **Change:** add .github/workflows/ci.yml, docs/ai/CI.md, .github/workflows/scan-forbidden.ps1, .github/workflows/scan-secrets.ps1, and add six new standing rows (S-10..S-13, S-15..S-16) + local-only note to docs/ai/TEST-CHECKLIST.md. No source code, no project changes, no new dependencies, no live provider calls, no target-framework changes, no restructuring. docs/ai/HANDOVER.md is NOT modified as part of S0.4 (missing R1 closeout is reported separately).
- **Rollback point:** remove the 6 files listed above; restore docs/ai/TEST-CHECKLIST.md to its pre-S0.4 state; leave docs/ai/ROLLBACK.md unchanged except for this entry.
- **Files to remove:** .github/workflows/ci.yml, .github/workflows/scan-forbidden.ps1, .github/workflows/scan-secrets.ps1, docs/ai/CI.md, docs/ai/TEST-CHECKLIST.md (new rows only), docs/ai/ROLLBACK.md (this entry).
- **Re-check after revert:** dotnet build -c Debug (0 warnings/0 errors), all 9 project builds succeed, all existing verification hosts pass with the original arguments, docs/ai/CONFIGS.md and docs/ai/AGENTS.md unchanged, docs/ai/HANDOVER.md unchanged (no R1 closeout written as part of S0.4).
- **Re-verify with:** dotnet build -c Debug, dotnet build -c Debug <each of 9 projects>, dotnet run --project tools\<Host> -c Debug -- <original offline args>; confirm zero warnings/errors and original exit codes (0 for offline fakes, 12 for keyless live-path, 13 for cancel).
- **Dependencies:** None. All CI checks run offline; no external tools are installed.

---
# ROLLBACK — Rollback Points & Procedures
## 2026-10-24 · Cline agent (M1/R4: Perception Layer Relocation)

- **Change:** move two perception-layer projects to nested paths under `src/Perception/`: `ElementSelection`, `Vision` via `git mv`. All project files, namespaces, assemblies, APIs, and source contents preserved intact. Solution file references updated to new paths. ElementSelection and Vision remain independent siblings — the orchestration layer may compose both, but one does not depend on the other.
- **Rollback point:** delete the two relocated directories under `src/Perception/`, revert `src/HeyGanesh.sln` and `Directory.Build.props` to original paths, restore original directory layout.
- **Files to restore:** `git mv src/Perception/HeyGanesh.ElementSelection/ src/HeyGanesh.ElementSelection/`, `git mv src/Perception/HeyGanesh.Vision/ src/HeyGanesh.Vision/`, then restore `HeyGanesh.sln` to reference original `src/HeyGanesh.*` paths for ElementSelection and Vision (explicit project references, no wildcards).
- **Re-check after revert:** `dotnet build -c Debug src\\HeyGanesh.ElementSelection\\HeyGanesh.ElementSelection.csproj` plus all remaining original-project builds; all existing verification hosts (ES smoke, Vision smoke, Tutor smoke, ContextOrchestrator smoke) pass with original arguments.
- **Dependencies:** No new NuGet, no target-framework changes, no source modifications. Pure relocation.
## 2026-10-23 · Cline agent (R1: Solution and Build Hygiene)

- **Change:** add `HeyGanesh.sln`, `global.json`, `Directory.Build.props`, `.editorconfig` at repository root for solution-wide build hygiene; no source changes, no dependency additions, no target-framework changes, no restructuring. All 9 existing projects remain at their current paths.
- **Rollback point:** delete the 4 newly added root files. Verify pre-R1 state: each of 9 projects builds individually with explicit project paths (no wildcards, no `-project` flag). No solution file, no `global.json`, no `Directory.Build.props`, no `.editorconfig` present.
- **Files to remove:** `HeyGanesh.sln`, `global.json`, `Directory.Build.props`, `.editorconfig`
- **Re-check after revert:** `dotnet build -c Debug src\\HeyGanesh.App\\HeyGanesh.App.csproj` plus all 8 remaining project builds; `dotnet build -c Debug` (no solution) should fail as it did before R1.
- **Verified:** `dotnet build -c Debug src\\HeyGanesh.App\\HeyGanesh.App.csproj`, `dotnet build -c Debug src\\HeyGanesh.ContextOrchestrator\\HeyGanesh.ContextOrchestrator.csproj`, `dotnet build -c Debug src\\HeyGanesh.GlobalHotkeyMonitor\\HeyGanesh.GlobalHotkeyMonitor.csproj`, `dotnet build -c Debug src\\HeyGanesh.ScreenCapture\\HeyGanesh.ScreenCapture.csproj`, `dotnet build -c Debug src\\HeyGanesh.ElementSelection\\HeyGanesh.ElementSelection.csproj`, `dotnet build -c Debug src\\HeyGanesh.Vision\\HeyGanesh.Vision.csproj`, `dotnet build -c Debug src\\HeyGanesh.TutorEngine\\HeyGanesh.TutorEngine.csproj`, `dotnet build -c Debug src\\HeyGanesh.Voice\\HeyGanesh.Voice.csproj`, `dotnet build -c Debug src\\HeyGanesh.ScreenOverlay\\HeyGanesh.ScreenOverlay.csproj` all succeed with no solution scaffolding.

> Record of known-good states and the steps to return to them. Add an entry before or
> immediately after any change that is hard to reverse. One entry, newest first.

## Index

| Date | Rollback point (commit / tag) | Applies to | Verified |
|------|-------------------------------|------------|----------|
| 2026-10-01 | delete the four untracked files in `src/HeyGanesh.TutorEngine/` (`DeepSeekTutorClient.cs`, `DeepSeekTutorOptions.cs`, `DeepSeekTutorMessages.cs`, `TutorClientFactory.cs`) + the three in `tools/TutorSmoke/` (`TutorSmokeHost.Payload.cs`, `TutorSmokeHost.Live.cs`, `TutorSmokeHost.StubServer.cs`), revert this session's edits (`TutorSmokeHost.cs` modes/usage/header, `TutorSmokeHost.Source.cs` audit, docs) | first live provider + smoke rows + docs; no service/shell/sibling file's behaviour changed | TU-11, TU-12 offline shape, TU-1..TU-10 regression |
| 2026-09-30 | `git checkout 9e73fdb -- src/HeyGanesh.App` + delete the untracked `src/HeyGanesh.App/ShellHost.Tutor.cs`, revert this session's doc edits | teaching-leg wiring in the App Shell + docs; no module source touched | SH-15..SH-17 |
| 2026-09-30 | revert commit `9d3ff37` for the module; delete `tools/TutorSmoke/` + `docs/ai/features/FEATURE-006.md` and revert this session's doc edits for the rest | new Tutor module (committed) + new smoke host + docs; no existing file's behaviour changed | TU-1..TU-10 offline rows |
| 2026-09-30 | restore 4 App Shell files from `%TEMP%\hg-rollback-shell-20260930`, delete `docs/ai/features/FEATURE-005.md`, revert this session's doc edits | push-to-talk shell wiring in `src/HeyGanesh.App/` + docs | SH-10..SH-14 (13 live-gated) |
| 2026-09-30 | delete `src/HeyGanesh.Voice/` + `tools/VoiceSmoke/` + `docs/ai/features/FEATURE-004.md`, revert this session's doc edits | new Voice module + new smoke host + docs; no existing file's behaviour changed | VO-1..VO-10 offline rows |
| 2026-09-29 | delete `src/HeyGanesh.App/` + `docs/ai/features/FEATURE-003.md`, revert this session's doc edits | new shell host + docs; no existing file's behaviour changed | SH-1..SH-6 offline rows |
| 2026-09-24 | delete `src/HeyGanesh.ScreenOverlay/` + `tools/OverlaySmoke/` + `docs/ai/features/FEATURE-002.md`, revert this session's doc edits | new module + new smoke host + docs; no existing file's behaviour changed | SO-1..SO-9 offline smoke scenarios |
| 2026-09-23 | revert of the Context Orchestrator commit (parent `8824568`) | new module + new smoke host + docs; no existing file's behaviour changed | build + offline smoke scenarios |
| — | — | *(no rollback points recorded yet)* | — |
---

<!-- ROLLBACK ENTRIES BELOW THIS LINE — newest first -->

## 2026-10-01 · Cline agent (Pixel Canary)

- **Change:** the first live teaching provider (DEC-017) — `src/HeyGanesh.TutorEngine/`:
  `DeepSeekTutorClient.cs` (internal client, call-time `DEEPSEEK_API_KEY`, fixed
  `thinking:{"type":"disabled"}`), `DeepSeekTutorOptions.cs`, `DeepSeekTutorMessages.cs`
  (payload mapping), `TutorClientFactory.cs` (the only new public construction API).
  `tools/TutorSmoke/`: `TutorSmokeHost.StubServer.cs` (loopback stub, free-port probe +
  bounded bind retry), `TutorSmokeHost.Payload.cs` (TU-11), `TutorSmokeHost.Live.cs`
  (TU-12); edits: host modes/usage/header, `--assert-source` audit extension. Docs:
  DEC-017, TU-11/TU-12 + scope note, ARCHITECTURE/FEATURE-006/FLOW updates, HANDOVER,
  this entry, AGENTS notes.
- **Rollback point:** every new file is UNTRACKED and no existing file's runtime behaviour
  changed (the shell's `CreateTutorClient()` still returns null). Delete the seven new files,
  hand-revert the two smoke-host edits and the doc rows, and `dotnet build` returns to
  yesterday's shape with no provider name committed.
- **Re-check after revert:** `dotnet build src\HeyGanesh.TutorEngine -c Debug` +
  `dotnet build tools\TutorSmoke -c Debug` → 0 warnings; `TutorSmoke --assert-source` exits
  0 without the provider rows; `--help` no longer lists `--assert-tutor-payload`/`--live-explain`.
- **Blast radius if left in place:** none at runtime — no caller constructs the factory yet.
  The committed surface is names only (`deepseek-flash`, `DEEPSEEK_API_KEY`, the factory).

## 2026-09-30 · Cline agent (Pixel Canary)

- **Change:** the shell's teaching leg (DEC-016) — `src/HeyGanesh.App/`: new `ShellHost.Tutor.cs`
  (untracked; mapping, visual derivation, `CreateTutorClient()` null seam factory, `TeachAndSpeakAsync`
  with the exit-code mapping) plus edits to `HeyGanesh.App.csproj` (5th ProjectReference), `ShellHost.cs`
  (composition + plumbing), `ShellHost.Session.cs` (post-verdict call sites, pump, exit-code header),
  `ShellHost.Args.cs` (`--no-speak`, header/footer honesty). Docs: DEC-016, SH-15..SH-17, FLOW tutor
  path flip, ARCHITECTURE App Shell/Voice/Tutor bullets, FEATURE-006 item 1, HANDOVER, this entry,
  AGENTS notes.
- **Rollback point:** the App Shell is committed through `9e73fdb`; this change is entirely
  **uncommitted**. `git checkout 9e73fdb -- src/HeyGanesh.App` restores the four edited files, and
  deleting the untracked `ShellHost.Tutor.cs` removes the leg — the build fails on nothing else, since
  no other assembly was touched. Hand-revert this session's doc rows (DEC-016 entry + index row, the
  SH-15..SH-17 rows + scope-note sentence, the FLOW tutor-path flip, the ARCHITECTURE bullets,
  FEATURE-006's status, the HANDOVER row + entry, this entry, the AGENTS notes).
- **Re-check after revert:** `dotnet build src\HeyGanesh.App -c Debug` → 0 warnings; the legacy row
  `HeyGanesh.App.exe --once --question "where is the Save button"` → `KEY ERROR (Element Selection)`,
  exit **12** (keys unset); `--help` no longer lists `--no-speak`.
- **Blast radius if left in place:** runtime behavior changes only on the post-verdict path (new
  report lines; the honest skip line while `CreateTutorClient()` returns null; with a future live
  client, audio + 12/3/4/13 leg codes). Every pre-verdict path is untouched by construction.

## 2026-09-30 · Cline agent (Pixel Canary)

- **Change:** Tutor Engine module (`src/HeyGanesh.TutorEngine/`, committed as `9d3ff37 feat: add Tutor
  Engine module`) + TutorSmoke verification host (`tools/TutorSmoke/`, untracked) + docs (DEC-015,
  TEST-CHECKLIST TU-1..TU-10, FLOW Tutor path, ARCHITECTURE module line + detail, FEATURE-006, AGENTS
  module row + commands, HANDOVER index + entry).
- **Rollback point:** the module is a single clean commit — `git revert 9d3ff37` removes it. The smoke
  host and FEATURE-006 are untracked: delete `tools/TutorSmoke/` and
  `docs/ai/features/FEATURE-006.md`. The doc edits are uncommitted: hand-revert this session's rows
  (DEC-015 entry + index row; the `## Tutor Engine checks` section; the `## Tutor path` section; the
  ARCHITECTURE Tutor line + detail block; the AGENTS Tutor table row + commands block; the HANDOVER
  index row + entry; this ROLLBACK row + entry). No existing source file was modified — nothing
  pre-existing can regress.
- **Re-check after revert:** `dotnet build src\HeyGanesh.App -c Debug` (the shell never referenced the
  Tutor, so it must be unaffected) and any VoiceSmoke offline row, e.g. `--fake-transcript "x"`.
- **Blast radius if left in place:** none at runtime — no other module or the shell references
  `HeyGanesh.TutorEngine` yet; wiring it in is FEATURE-006's next change.

## 2026-09-30 · Cline agent (model id not exposed to the agent)

- **Change:** push-to-talk voice wiring in the App Shell (`src/HeyGanesh.App/`): `HeyGanesh.App.csproj`
  gains the 4th project-ref (Voice), `ShellHost.cs` composes one `VoiceService` and subscribes
  `Released`, `ShellHost.Session.cs` grows the 3-state `TriggerPump`, `ShellHost.Args.cs` demotes
  `--question` and prints the voice keys' presence. Plus docs (DECISIONS DEC-014, ARCHITECTURE App
  Shell, FLOW shell+voice legs, TEST-CHECKLIST SH-10..SH-14, AGENTS commands, HANDOVER,
  `features/FEATURE-005.md`).
- **Rollback point:** **the App Shell is not committed** (`src/HeyGanesh.App/` is untracked in git), so
  `git checkout HEAD -- src/HeyGanesh.App` cannot restore it — use the file copies taken before the
  change: `%TEMP%\hg-rollback-shell-20260930\` (`ShellHost.cs`, `ShellHost.Session.cs`,
  `ShellHost.Args.cs`, `HeyGanesh.App.csproj`). Copy them back over `src/HeyGanesh.App/`, delete
  `docs/ai/features/FEATURE-005.md`, and hand-revert this session's doc rows (the DEC-014 entry + index
  row, the ARCHITECTURE App Shell block, the FLOW shell-leg/voice-diagram rows, the SH-10..SH-14 rows,
  the AGENTS command rows, the HANDOVER entry). Nothing outside `src/HeyGanesh.App/` was touched, so no
  module behaviour can regress.
- **Re-check after revert:** `dotnet build src\HeyGanesh.App -c Debug` and the legacy row
  `HeyGanesh.App.exe --once --question "where is the Save button"` (KEY ERROR / exit 12 with keys
  unset — SH-4).
- **Blast radius if left in place:** the shell requires a running qwen-audio-agent Gateway for its
  product path; without one, a chord press reports `VOICE FAILURE` and exits 1 rather than resolving.

## 2026-09-30 · Cline agent (model id not exposed to the agent)

- **Change:** new Voice pipeline module (`src/HeyGanesh.Voice/`) + its verification host (`tools/VoiceSmoke/`)
  + doc updates (AGENTS commands, ARCHITECTURE module detail, FLOW voice legs, DECISIONS DEC-013,
  TEST-CHECKLIST VO-1..VO-10, HANDOVER, `features/FEATURE-004.md`).
- **Rollback point:** the working tree before this session's edits (HEAD `8824568`, with the App Shell /
  Context Orchestrator / Screen Overlay work still uncommitted). Everything this change adds to code is
  **new-file-only inside two new directories**: while uncommitted, deleting `src/HeyGanesh.Voice/` and
  `tools/VoiceSmoke/` (plus `docs/ai/features/FEATURE-004.md`) restores the tree exactly; once committed,
  `git revert <commit>` does the same. **No existing source file is edited**, so no module behaviour can
  regress — Voice is consumed by nobody yet (the App Shell still takes `--question`).
- **Files to restore if a doc edit is suspected:** `AGENTS.md`, `docs/ai/ARCHITECTURE.md`,
  `docs/ai/FLOW.md`, `docs/ai/DECISIONS.md`, `docs/ai/TEST-CHECKLIST.md`, `docs/ai/HANDOVER.md`,
  `docs/ai/ROLLBACK.md` — prefer hand-removing this session's added rows over `git checkout HEAD -- <path>`,
  because a checkout also loses the uncommitted CO / Overlay / Shell prose those files carry.
- **What to re-check after reverting:** `dotnet build src\HeyGanesh.App -c Debug` plus one shell row
  (`HeyGanesh.App.exe --once --question "x"` with keys unset → `KEY ERROR (Element Selection)`, exit 12) and
  one orchestrator row (`ContextOrchestratorSmoke --question "Save" --fake-none`). A failure there means the
  revert was incomplete, not that Voice was load-bearing.
- **Nothing outside the repo is touched:** no gateway deployment, no provider account, no data directory
  change; the Voice module keeps no state on disk.

## 2026-09-29 · Pixel Canary (Vercel AI Gateway early access; base model not announced)

- **Change:** new App Shell (`src/HeyGanesh.App/`) — the dependency-free console host that finally
  subscribes `GlobalHotkeyMonitor.Pressed` and maps one chord press to one
  `ContextOrchestratorService.HandleHotkeyPressedAsync` and one `ScreenOverlayService.ShowAsync` — plus
  doc updates (AGENTS commands, ARCHITECTURE module row, FLOW shell leg + diagram, DECISIONS DEC-012,
  TEST-CHECKLIST SH-1..SH-6, HANDOVER, `features/FEATURE-003.md`).
- **Rollback point:** the working tree before this session's edits (HEAD `8824568`, with the Context
  Orchestrator and Screen Overlay work still uncommitted). Everything this change adds to code is
  **new-file-only inside one new directory**: while uncommitted, deleting `src/HeyGanesh.App/` and
  `docs/ai/features/FEATURE-003.md` restores the tree exactly. Once committed, `git revert <commit>`
  does the same. **No existing source file is edited**, so no module behaviour can regress — the shell
  only consumes the public surface the modules already had.
- **Files to restore if a doc edit is suspected:** `AGENTS.md`, `docs/ai/ARCHITECTURE.md`,
  `docs/ai/FLOW.md`, `docs/ai/DECISIONS.md`, `docs/ai/TEST-CHECKLIST.md`, `docs/ai/HANDOVER.md`,
  `docs/ai/ROLLBACK.md` — `git checkout HEAD -- <path>` for each. Caution: at `8824568` these files
  carry the *uncommitted* CO + Overlay prose, so a checkout loses those sessions' entries; prefer
  hand-reverting this session's added rows over a checkout unless the whole feature is being dropped.
- **What to re-check after reverting:** `dotnet build src\HeyGanesh.ScreenOverlay -c Debug` plus one
  overlay row (`OverlaySmoke --found --duration-ms 1500`) and one orchestrator row
  (`ContextOrchestratorSmoke --question "Save" --fake-none`). A failure there means the revert was
  incomplete, not that the shell was load-bearing.

## 2026-09-24 · Muse Spark (muse-spark)

- **Change:** new Screen Overlay module (`src/HeyGanesh.ScreenOverlay/`) + its verification host (`tools/OverlaySmoke/`) + doc updates (AGENTS commands, ARCHITECTURE implemented section, FLOW overlay leg + diagram, DECISIONS DEC-011, TEST-CHECKLIST SO-1..SO-9 + EX-5 flip, HANDOVER, `features/FEATURE-002.md`).
- **Rollback point:** the working tree before this session's edits. Everything this change adds to code is new-file-only: while uncommitted, deleting `src/HeyGanesh.ScreenOverlay/`, `tools/OverlaySmoke/` and `docs/ai/features/FEATURE-002.md` restores the tree; once committed, `git revert <commit>` does the same. No existing source file is edited by this change, so no behaviour can regress.
- **Files to restore if a doc edit is suspected:** `AGENTS.md`, `docs/ai/ARCHITECTURE.md`, `docs/ai/FLOW.md`, `docs/ai/DECISIONS.md`, `docs/ai/TEST-CHECKLIST.md`, `docs/ai/HANDOVER.md`, `docs/ai/ROLLBACK.md` — `git checkout HEAD -- <path>` for each tracked file.
- **What to re-check after reverting:** `dotnet build src\\HeyGanesh.ContextOrchestrator -c Debug` plus one CO row (e.g. `--fake-none`); then the ES rows (ES-1..ES-7). A failure there means the revert was incomplete, not that the removed change was load-bearing.

## 2026-09-23 · Cline agent (Claude Sonnet 4.5)

- **Change:** new Context Orchestrator module (`src/HeyGanesh.ContextOrchestrator/`) + its verification host (`tools/ContextOrchestratorSmoke/`) + doc updates (AGENTS commands, ARCHITECTURE, FLOW, DECISIONS DEC-010, TEST-CHECKLIST CO-1..CO-7, HANDOVER, `features/FEATURE-001.md`).
- **Rollback point:** `8824568` (`main`). Everything this change adds to code is new-file-only: `git revert <commit>` restores the tree, or deleting the two new directories does the same while the work is uncommitted. No existing source file is edited by this change, so no behaviour can regress.
- **Files to restore if a doc edit is suspected:** `AGENTS.md`, `docs/ai/ARCHITECTURE.md`, `docs/ai/FLOW.md`, `docs/ai/DECISIONS.md`, `docs/ai/TEST-CHECKLIST.md`, `docs/ai/HANDOVER.md`, `docs/ai/ROLLBACK.md` — `git checkout HEAD -- <path>` for the tracked ones; `AGENTS.md` is untracked at `8824568` and `HANDOVER.md`/`TEST-CHECKLIST.md`/`FLOW.md` carry this session's prose, so restore them from the previous commit plus the entry text if the revert is partial.
- **What to re-check after reverting:** `dotnet build src\HeyGanesh.ElementSelection -c Debug` plus the ES smoke rows (ES-1..ES-7). A failure there means the revert was incomplete, not that the removed change was load-bearing.

## 2026-09-22 · Cline agent (model id not exposed to the agent)

- **Change:** `docs/ai/DECISIONS.md` restored to a clean 9-entry log (DEC-001–009), LF-only, no BOM, no gamma.
- **Rollback point:** the clean 7-entry recovery file `C:\Users\prana\AppData\Local\Temp\dec_7ent.md` (in the shared `.kilo\worktrees\rainbow-gourd` workspace). `git checkout HEAD -- docs/ai/DECISIONS.md` returns to the committed version at `94a8af1e`; restore `dec_7ent.md` to `docs/ai/DECISIONS.md`, then re-insert the DEC-009/DEC-008 entries before the `## DEC-007 — 2026-09-22 — Laguna S 2.1` header and the DEC-009/008 rows after the index separator to reproduce this exact state.
- **Verified:** `git diff --no-index --stat dec_7ent.md docs/ai/DECISIONS.md` → 48 insertions, 0 deletions; byte probe `bytes=29323, first=35 (no BOM), cr=0 (LF), emdash=106, gamma=0`; DEC-001..009 each once; 9 index rows; all anchors resolve.ECHO is on.

## 2026-10-24 · Cline agent (M1/R5: Intelligence Layer Relocation)

- **Change:** move two intelligence-layer projects to nested paths under `src/Intelligence/`: `ContextOrchestrator`, `TutorEngine` via `git mv`. All project files, namespaces, assemblies, APIs, and source contents preserved intact. Solution file references updated to new paths.

- **Rollback point:** delete the two relocated directories under `src/Intelligence/`, revert `src/HeyGanesh.sln` to original paths, restore original directory layout.

- **Files to restore:** `git mv src/Intelligence/HeyGanesh.ContextOrchestrator/ src/HeyGanesh.ContextOrchestrator/`, `git mv src/Intelligence/HeyGanesh.TutorEngine/ src/HeyGanesh.TutorEngine/`, then restore `HeyGanesh.sln` to reference original `src/HeyGanesh.*` paths (no wildcards, explicit project references).

- **Re-check after revert:** `dotnet build -c Debug src\\HeyGanesh.ContextOrchestrator\\HeyGanesh.ContextOrchestrator.csproj` plus all remaining original-project builds; `dotnet build -c Debug` (no solution) should behave as pre-R5. All original verification hosts (CO smoke, ES smoke, Vision smoke, Tutor smoke, etc.) must pass with original arguments.

## 2026-10-24 ┬╖ Cline agent (M1/R6: Experience Layer Relocation)

- **Change:** move two experience-layer projects to nested paths under `src/Experience/`: `HeyGanesh.App`, `HeyGanesh.Voice` via `git mv`. All project files, namespaces, assemblies, APIs, and source contents preserved intact. Solution file and all affected `ProjectReference` paths updated to new locations.

- **Rollback point:** delete the two relocated directories under `src/Experience/`, revert `src/HeyGanesh.sln` and all affected `.csproj` ProjectReference paths to original `src/HeyGanesh.*` paths, restore original directory layout.

- **Files to restore:** `git mv src/Experience/HeyGanesh.App/ src/HeyGanesh.App/`, `git mv src/Experience/HeyGanesh.Voice/ src/HeyGanesh.Voice/`, then restore `HeyGanesh.sln` to reference original `src/HeyGanesh.App/` and `src/HeyGanesh.Voice/` paths, and restore all modified `.csproj` ProjectReference paths to original locations (ContextOrchestrator `..\\HeyGanesh.ContextOrchestrator\\`, Voice `..\\HeyGanesh.Voice\\`, etc.).

- **Re-check after revert:** `dotnet build -c Debug src\\HeyGanesh.App\\HeyGanesh.App.csproj` plus `dotnet build -c Debug src\\HeyGanesh.Voice\\HeyGanesh.Voice.csproj` plus all remaining original-project builds; `dotnet build -c Debug` (no solution) should behave as pre-R6. All original verification hosts must pass with original arguments.

- **Dependencies:** No new NuGet, no target-framework changes, no source modifications. Pure relocation.## 2026-10-24 ┬╖ Cline agent (M1/R7: Solution Reference Correction)

- **Change:** correct stale ProjectReference paths across all .csproj files and HeyGanesh.sln to reflect actual project directory locations after M1/R6 relocations. Key fixes: HeyGanesh.App references updated from single-dot to double-dot relative paths (..\\..\\Platform\\.., ..\\..\\Intelligence\\..); HeyGanesh.ElementSelection inner and HeyGanesh.Vision inner references corrected from ..\\HeyGanesh.ScreenCapture\\.. to ..\\..\\Platform\\HeyGanesh.ScreenCapture\\..; HeyGanesh.sln project entries verified for relocated Experience projects. All namespaces, assemblies, and public APIs preserved.

- **Rollback point:** restore all .csproj ProjectReference paths to pre-R7 state (single-dot paths for HeyGanesh.App; ..\\HeyGanesh.ScreenCapture\\.. for ElementSelection inner and Vision inner); restore HeyGanesh.sln project entries to pre-R7 paths; revert HeyGanesh.App from src/Experience/HeyGanesh.App/ back to src/HeyGanesh.App/ if needed. Delete src/Experience/ directories and restore original layout.

- **Files to restore:** 
  - `git mv src/Experience/HeyGanesh.App/ src/HeyGanesh.App/` (if reversing R6 relocation)
  - `git mv src/Experience/HeyGanesh.Voice/ src/HeyGanesh.Voice/` (if reversing R6 relocation)
  - Restore HeyGanesh.App/HeyGanesh.App.csproj ProjectReference paths: `..\\Platform\\...` → `..\\..\\Platform\\...`, `..\\Intelligence\\...` → `..\\..\\Intelligence\\...`, `..\\Experience\\HeyGanesh.Voice\\...` → `..\\HeyGanesh.Voice\\...`
  - Restore HeyGanesh.ElementSelection/HeyGanesh.ElementSelection.csproj: `..\\HeyGanesh.ScreenCapture\\...` → `..\\..\\Platform\\HeyGanesh.ScreenCapture\\...`
  - Restore HeyGanesh.Vision/HeyGanesh.Vision.csproj: `..\\HeyGanesh.ScreenCapture\\...` → `..\\..\\Platform\\HeyGanesh.ScreenCapture\\...`
  - Restore HeyGanesh.sln project entries to original `src\\HeyGanesh.*` paths
  - Run `git checkout HEAD -- docs/ai/ROLLBACK.md` to remove this R7 entry if rollback is partial.

- **Re-check after revert:** `dotnet build -c Debug src\\HeyGanesh.App\\HeyGanesh.App.csproj` plus `dotnet build -c Debug src\\HeyGanesh.ElementSelection\\HeyGanesh.ElementSelection.csproj` plus `dotnet build -c Debug src\\HeyGanesh.Vision\\HeyGanesh.Vision.csproj`; verify all original verification hosts (ES smoke, Vision smoke, Tutor smoke, etc.) pass with original arguments. `dotnet build -c Debug` (solution) should behave as pre-R7.

- **Dependencies:** No new NuGet, no target-framework changes, no source modifications other than ProjectReference path corrections. Pure path fixes.

## 2026-10-24 · Cline agent (S2.1: HeyGanesh.Security foundation)

- **Change:** Create `src/Foundation/HeyGanesh.Security/` with `ISecretStore` over Windows Credential Manager (advapi32 P/Invoke), `SecretHandle` (zeroing byte[] wrapper), `SecretTag` attribute, `SecretLogger` (redacting logger), plus `NativeMethods.cs` for advapi32 declarations. Add smoke host `tools/SecretSmoke/`. Add project to `HeyGanesh.sln` under `Foundation` solution folder. Update `scan-forbidden.ps1`, `scan-secrets.ps1`, `ci.yml` scope/build lists.
- **Rollback point:** Remove `src/Foundation/HeyGanesh.Security/` directory; remove `tools/SecretSmoke/` directory; restore `HeyGanesh.sln` to pre-S2.1 state (remove HeyGanesh.Security project entry and its GUID from NestedProjects under Foundation); restore `scan-forbidden.ps1`, `scan-secrets.ps1`, `ci.yml` to pre-S2.1 content.
- **Files to restore:** Delete `src/Foundation/HeyGanesh.Security/`; delete `tools/SecretSmoke/`; `git checkout HEAD -- HeyGanesh.sln .github/workflows/scan-forbidden.ps1 .github/workflows/scan-secrets.ps1 .github/workflows/ci.yml docs/ai/HANDOVER.md docs/ai/ROLLBACK.md`.
- **Re-check after revert:** `dotnet build HeyGanesh.sln -c Debug -warnaserror` → 0/0; `scan-forbidden.ps1` → pass; `scan-secrets.ps1` → pass; `scan-secrets.ps1 -ZeroNuGet` → pass; all existing smoke tests pass with original args.
- **Dependencies:** Zero new NuGet; pure .NET 8 zero-PackageReference assembly; target `net8.0-windows10.0.22621.0`; P/Invoke only to `advapi32.dll`.

## 2026-10-24 ┬╖ Cline agent (LEAD-AUTH M1/R7+: ScreenOverlay→ContextOrchestrator Abstraction)

- **Change:** resolve ScreenOverlay→ContextOrchestrator dependency conflict by introducing `src/Foundation/HeyGanesh.Abstractions/` shared contract. ScreenOverlay (Platform module) previously referenced ContextOrchestrator (Intelligence module) violating Platform dependency rule. Solution: extract smallest shared contract into standalone zero-NuGet project, update both modules to depend on abstraction instead of each other. All namespaces, assemblies, and public APIs preserved where feasible. Guide-only safety: abstraction contains only data types and interfaces, no implementation, no actuation. Rollback: remove `src/Foundation/HeyGanesh.Abstractions/` project, restore ScreenOverlay→ContextOrchestrator direct reference, remove HeyGanesh.Abstractions from both modules' ProjectReferences and any using directives. Delete project from HeyGanesh.sln. Restore original conflict state.

- **Rollback point:** restore ScreenOverlay→ContextOrchestrator direct ProjectReference; remove HeyGanesh.Abstractions ProjectReference from both modules; remove HeyGanesh.Abstractions from HeyGanesh.sln project entries; restore any using directives referencing old direct dependency; `git checkout HEAD -- docs/ai/ROLLBACK.md` to remove this entry if rollback is partial.

- **Files to restore:** 
  - `git mv src/Foundation/HeyGanesh.Abstractions/ ...` (reverse creation)
  - Remove HeyGanesh.Abstractions ProjectReference from ScreenOverlay.csproj and ContextOrchestrator.csproj
  - Remove HeyGanesh.Abstractions ProjectReference from HeyGanesh.sln 
  - Restore original ScreenOverlay→ContextOrchestrator direct dependency
  - Run `git checkout HEAD -- docs/ai/ROLLBACK.md` to remove this R7+ entry if rollback is partial.

- **Re-check after revert:** Verify ScreenOverlay.csproj and ContextOrchestrator.csproj no longer reference HeyGanesh.Abstractions; verify direct ScreenOverlay→ContextOrchestrator reference is restored; verify HeyGanesh.sln project entries restored; `dotnet build -c Debug` (solution) behaves as pre-R7+. `HANDOVER.md` Open Items section restored to pre-R7+ state with Platform-to-Intelligence conflict.

- **Dependencies:** Zero new NuGet; HeyGanesh.Abstractions is pure .NET 8 zero-PackageReference assembly with physical-pixel `PixelRect` types and `ConfidenceLevel` enum; no external packages; build target `net8.0-windows10.0.22621.0`.

- **Safety guide-only:** Abstraction contains only shared data types, enums, and interface signatures needed by both ScreenOverlay and ContextOrchestrator. No implementation, no P/Invoke, no window handles, no audio, no input. Both modules remain independently buildable and testable. The abstraction is a contract-seam only.

## 2026-10-27 · Cline agent (S2.4 follow-up: provider HTTP error classification fix)

- **Change:** Added `InvalidRequest` error kind to `ProviderErrorKind` (non-retryable by default). Updated both adapters: HTTP 400 / `invalid_request_error` → `InvalidRequest` instead of `ContentBlocked`; image validation errors → `InvalidRequest`. Updated smoke tests across ProvidersSmoke (29/29), OpenAiCompatSmoke (8/8), AnthropicSmoke (14/14). All scans pass, full solution build 0/0.
- **Rollback point:** Revert `ProviderError.cs`, `OpenAiCompatLlmClient.cs`, `AnthropicLlmClient.cs` smoke test changes; restore test expectations to pre-S2.4-follow-up state; `dotnet build HeyGanesh.sln -c Debug -warnaserror` → 0/0.
- **Files to restore:** `git checkout HEAD -- src/Providers/HeyGanesh.Providers.Core/ProviderError.cs src/Providers/HeyGanesh.Providers.OpenAiCompat/OpenAiCompatLlmClient.cs src/Providers/HeyGanesh.Providers.Anthropic/AnthropicLlmClient.cs tools/ProvidersSmoke/ProvidersSmokeHost.cs tools/OpenAiCompatSmoke/OpenAiCompatSmokeHost.cs tools/AnthropicSmoke/AnthropicSmokeHost.cs`.

## 2026-10-27 · Cline agent (S2.5: Gemini Provider)

- **Change:** Create `src/Providers/HeyGanesh.Providers.Gemini/` zero-NuGet project implementing `ILlmClient` against the Gemini REST API (generateContent). Add `tools/GeminiSmoke/` verification host with loopback stub server. Update `HeyGanesh.sln` to include Gemini project entry. Update `.github/workflows/scan-forbidden.ps1` and `scan-secrets.ps1` to include Gemini module. Update `docs/ai/ARCHITECTURE.md` to describe Gemini module. No live provider calls, no new NuGet dependencies, zero-PackageReference assembly.
- **Rollback point:** Remove `src/Providers/HeyGanesh.Providers.Gemini/` directory; remove `tools/GeminiSmoke/` directory; restore `HeyGanesh.sln` to pre-S2.5 state (remove HeyGanesh.Gemini project entry and GUID from NestedProjects under Providers); restore `scan-forbidden.ps1`, `scan-secrets.ps1` to pre-S2.5 content; restore `docs/ai/ARCHITECTURE.md` and `docs/ai/HANDOVER.md` to pre-S2.5 state.
- **Files to restore:** Delete `src/Providers/HeyGanesh.Providers.Gemini/`; delete `tools/GeminiSmoke/`; `git checkout HEAD -- HeyGanesh.sln .github/workflows/scan-forbidden.ps1 .github/workflows/scan-secrets.ps1 docs/ai/ARCHITECTURE.md docs/ai/HANDOVER.md docs/ai/ROLLBACK.md`.
- **Re-check after revert:** `dotnet build HeyGanesh.sln -c Debug -warnaserror` → 0/0; `scan-forbidden.ps1` → pass; `scan-secrets.ps1` → pass; all existing smoke tests pass with original args.
- **Dependencies:** Zero new NuGet; pure .NET 8 zero-PackageReference assembly; target `net8.0-windows10.0.22621.0`; `System.Net.Http` only.

## 2026-10-28 · claude-opus-5-5 (FEATURE-007 Phase 4.5: lesson shell wiring)

- **Change:** Add `src/Experience/HeyGanesh.LessonSession/` (LessonTurn, DelegateLessonSpeaker, CardSurfaceHost) and `tools/LessonTurnSmoke/`; App Shell gains `--lesson`, `ShellHost.Lesson.cs`, an env-backed read-only secret store, and ProjectReferences to LessonSession + Providers.OpenAiCompat (WebView2 now arrives transitively, DEC-027). Docs: DEC-027, FLOW, ARCHITECTURE, TEST-CHECKLIST, AGENTS.md, FEATURE-007, HANDOVER.
- **Rollback point:** main at `4c4be17`. All work is on `feat/lesson-shell-wiring-v1`; until it is merged, rollback = `git checkout main && git branch -D feat/lesson-shell-wiring-v1`. After a merge: `git revert` the branch's commits.
- **Files to restore:** delete `src/Experience/HeyGanesh.LessonSession/`, `tools/LessonTurnSmoke/`, `src/Experience/HeyGanesh.App/ShellHost.Lesson.cs`, `src/Experience/HeyGanesh.App/EnvironmentSecretStore.cs`; `git checkout 4c4be17 -- HeyGanesh.sln src/Experience/HeyGanesh.App docs/ai AGENTS.md`.
- **Re-check after revert:** `dotnet build HeyGanesh.sln -c Debug -warnaserror` -> 0/0; `HeyGanesh.App.exe --help` has no `--lesson`; SH-4/SH-7/SH-16 exits 12/13/12; LessonSmoke/LessonSurfaceSmoke/LessonPlannerSmoke rows exit 0.

## 2026-10-29 · claude-opus-5-5 (repo cleanup steps 1-2: stray files + Gemini provider)

- **Change:** Delete the stray files `%PYFILE%`, `.editorconfigmp`, `.editorconfigmpix.py` (untracked UTF-16 script fragments), the tracked root file `4` (a HotkeySmoke log written to the repo root), and the whole untracked `src/Providers/HeyGanesh.Providers.Gemini/` (does not compile, not in the sln, unreferenced, contains a stray `test.txt`) plus `tools/GeminiSmoke/` (a 36-byte broken csproj stub).
- **Rollback point:** branch `feat/lesson-shell-wiring-v1` at `d562fb3`. `4` comes back with `git checkout d562fb3 -- 4`. Everything else was never tracked: a byte-for-byte copy was taken before deletion, in the session scratchpad `cleanup-backup/` folder (temporary, not in the repo), so after that folder is gone the untracked files are unrecoverable.
- **Files to restore:** `git checkout d562fb3 -- 4`; copy `cleanup-backup/*` back to the repo root, keeping paths.
- **Re-check after revert:** `dotnet build HeyGanesh.sln -c Debug -warnaserror` -> 0/0 (Gemini was never in the sln, so the build is unchanged either way).

## 2026-10-29 · claude-opus-5-5 (repo cleanup step 3: placeholder folders)

- **Change:** Delete the six untracked README-only placeholder folders `config/`, `docs/architecture/`, `docs/product/`, `eval/`, `prompts/`, `tests/` (created by BUILD-PLAYBOOK step R2; each README describes files that do not exist, some with invented figures such as prompt versions and latency budgets). No source, project or build file references them.
- **Rollback point:** never tracked, so git cannot restore them. A byte-for-byte copy was taken first, in the session scratchpad `cleanup-backup/` folder (temporary, outside the repo).
- **Files to restore:** copy `cleanup-backup/{config,docs/architecture,docs/product,eval,prompts,tests}` back to the repo root, keeping paths.
- **Re-check after revert:** `dotnet build HeyGanesh.sln -c Debug -warnaserror` -> 0/0 (unchanged either way).

## 2026-10-29 · claude-opus-5-5 (repo cleanup step 4: CI workflow tracked + fixed)

- **Change:** Track `.github/workflows/ci.yml`, `scan-forbidden.ps1`, `scan-secrets.ps1` (never committed before). ci.yml rewritten to CI.md's gate order: G-2 now runs the real `-ZeroNuGet` XML check, G-3 is build-only, G-4/G-5 and G-6's window-drawing modes moved local-only, G-12..G-15 added (SecretSmoke + the three offline lesson hosts). CI.md updated to match. Separate commit: `tools/TutorSmoke/TutorSmokeHost.Source.cs` looked for the pre-restructure `src/HeyGanesh.TutorEngine`, so `--assert-source` (TU row) always failed; now `src/Intelligence/HeyGanesh.TutorEngine`. (This entry was written after the edits but before either commit.)
- **Rollback point:** branch `feat/lesson-shell-wiring-v1` at `cbdf116`.
- **Files to restore:** `git checkout cbdf116 -- docs/ai/CI.md docs/ai/HANDOVER.md tools/TutorSmoke/TutorSmokeHost.Source.cs`; `git rm -r --cached .github` (the original untracked ci.yml was overwritten before it was backed up; a reconstruction from its printed text is in the session scratchpad `cleanup-backup/.github/workflows/ci.yml.reconstructed` (temporary), and the scan scripts were unchanged).
- **Re-check after revert:** `dotnet build HeyGanesh.sln -c Debug -warnaserror` -> 0/0; `HeyGanesh.Tools.TutorSmoke.exe --assert-source` goes back to `FAIL could not locate`, exit 1.

## 2026-10-29 · claude-opus-5-5 (FEATURE-007 Phase 5 step 2: scene3d contract, VisualPlan 1.1)

- **Change:** VisualLessonEngine gains `SurfaceKind.Scene3d`, ops `model`/`label`/`explode`/`animate`/`camera`, `ModelCatalog.cs`, limits `MaxScenes`/`MaxModelsPerScene`, seven issue codes, schema `heyganesh.visualplan/1.1`, and `ValidationContext.Scene3d` (null = 3D off, the default for every existing caller). LessonSmoke gains `--assert-scene3d` and `fixtures/engine-3d.json`. Docs: DEC-028, LS-13, FLOW, FEATURE-007, CI (G-13), HANDOVER. Plan: docs/ai/plans/2026-10-29-scene3d-contract-v1.md.
- **Rollback point:** branch `feat/lesson-shell-wiring-v1` at `15f838d` (the commit before this step).
- **Files to restore:** `git checkout 15f838d -- src/Intelligence/HeyGanesh.VisualLessonEngine tools/LessonSmoke .github/workflows/ci.yml docs/ai`; delete `src/Intelligence/HeyGanesh.VisualLessonEngine/ModelCatalog.cs`, `tools/LessonSmoke/LessonSmokeHost.Scene3d.cs`, `tools/LessonSmoke/fixtures/engine-3d.json`.
- **Re-check after revert:** `dotnet build HeyGanesh.sln -c Debug -warnaserror` -> 0/0; every LessonSmoke mode with its old PASS count (106 across the 11 modes); LessonPlannerSmoke and LessonTurnSmoke offline modes exit 0.

## 2026-10-29 · claude-opus-5-5 (FEATURE-007 Phase 5 step 3: part library v1)

- **Change:** New assembly `src/Intelligence/HeyGanesh.PartLibrary/` (three models: four-stroke-engine, dna-helix, na-k-pump; LibraryCheck; ToCatalog) and host `tools/PartLibrarySmoke/`; `HeyGanesh.sln` gains the library under `src`. Docs: DEC-029, ARCHITECTURE, AGENTS.md, TEST-CHECKLIST PL-1..PL-5, CI G-16, FEATURE-007, HANDOVER, plan. No existing code path changes: nothing calls the library yet.
- **Rollback point:** branch `feat/lesson-shell-wiring-v1` at `eab7904` (the commit before this step).
- **Files to restore:** delete `src/Intelligence/HeyGanesh.PartLibrary/` and `tools/PartLibrarySmoke/`; `git checkout eab7904 -- HeyGanesh.sln .github/workflows/ci.yml AGENTS.md docs/ai`.
- **Re-check after revert:** `dotnet build HeyGanesh.sln -c Debug -warnaserror` -> 0/0; LessonSmoke 12 modes (128 PASS), LessonPlannerSmoke (39), LessonTurnSmoke (20) exit 0.

## 2026-10-29 · claude-opus-5-5 (FEATURE-007 Phase 5 step 4: 3D scene renderer)

- **Change:** LessonSurface gains the scene window (CardWindow page parameter, SceneMessages.cs, CardSurface scene routing, DescribeSceneAsync, CheckOrientationAsync, Scene3dCatalog), the scene page (Page/scene.html|css|js), vendored Babylon.js 9.30.0 (Page/vendor/babylonjs-9.30.0/, ~9.3 MB, pinned in Page/vendor/VENDOR.md), a ProjectReference to PartLibrary; `.gitattributes` marks the vendor folder `-text`. LessonSurfaceSmoke gains the scene rows. Docs: DEC-030, FLOW, ARCHITECTURE, AGENTS.md, TEST-CHECKLIST LSU-7..LSU-14, CI G-17, FEATURE-007, HANDOVER, plan.
- **Rollback point:** branch `feat/lesson-shell-wiring-v1` at `0265b8d` (the commit before this step).
- **Files to restore:** `git checkout 0265b8d -- src/Experience/HeyGanesh.LessonSurface tools/LessonSurfaceSmoke .gitattributes .github/workflows/ci.yml AGENTS.md docs/ai`; delete `src/Experience/HeyGanesh.LessonSurface/SceneMessages.cs`, `src/Experience/HeyGanesh.LessonSurface/Page/scene.*`, `src/Experience/HeyGanesh.LessonSurface/Page/vendor/`, `tools/LessonSurfaceSmoke/LessonSurfaceSmokeHost.Scene.cs`. The vendored files stay in git history after a revert (~9.3 MB); removing them from history would need a rewrite the owner must approve.
- **Re-check after revert:** `dotnet build HeyGanesh.sln -c Debug -warnaserror` -> 0/0; LessonSurfaceSmoke --assert-source/-window/-render/-security/-marks and LessonTurnSmoke --assert-turn-surface with their old PASS counts.

## 2026-10-29 · claude-opus-5-5 (FEATURE-007 Phase 5 step 5: click a 3D part to ask)

- **Change:** VisualLessonEngine gains the optional canvas seam `IPartPicker` + `PartPick` (Runtime/LessonSeams.cs). LessonSurface: scene page picking (Page/scene.js|css|html), `CardWindow` raises `PartClicked`, `CardSurface` implements `IPartPicker` and validates every click against what it drew and the catalogue. LessonSession: `LessonTurn.RunWithFollowUpsAsync`, `FollowUpOptions`, `PartQuestion` (`RunAsync` unchanged). LessonTurnSmoke `--assert-followup-pick`, LessonSurfaceSmoke `--assert-pick` / `--demo-pick`. CI G-15 gains one mode. Docs: DEC-031, FLOW, ARCHITECTURE, AGENTS.md, TEST-CHECKLIST, FEATURE-007, HANDOVER, plan.
- **Rollback point:** branch `feat/lesson-shell-wiring-v1` at `40b7ea9` (the commit before this step).
- **Files to restore:** `git checkout 40b7ea9 -- src/Intelligence/HeyGanesh.VisualLessonEngine src/Experience/HeyGanesh.LessonSurface src/Experience/HeyGanesh.LessonSession tools/LessonTurnSmoke tools/LessonSurfaceSmoke .github/workflows/ci.yml AGENTS.md docs/ai`; delete any file this step added (`git status` after the checkout lists them).
- **Re-check after revert:** `dotnet build HeyGanesh.sln -c Debug -warnaserror` -> 0/0; LessonTurnSmoke all modes and LessonSurfaceSmoke --assert-source / --assert-scene with their step 4 PASS counts.

## 2026-10-29 · claude-opus-5-5 (FEATURE-007: SVG drawing when WebGL is missing, DEC-034)

- **Change:** LessonSurface vendors Anatomy's kit (`Page/vendor/anatomy-1482e846/`: iso-kit.mjs, lathe.mjs, iso.css, LICENSE; pinned in VENDOR.md) and adds `Page/scene-svg.js` + `Page/scene-svg.css`; `scene.html` loads them; `scene.js`'s no-WebGL path draws through them instead of acking `skipped`; `CardSurface` runs the orientation probe on the drawing and reads drawing fields into `SceneSnapshot`. LessonSurfaceSmoke: LSU-12 reworked, LSU-16 new, LSU-7 extended. Docs: DEC-034, FLOW, ARCHITECTURE, AGENTS, TEST-CHECKLIST, FEATURE-007, HANDOVER, plan.
- **Rollback point:** branch `feat/lesson-shell-wiring-v1` at `516d385` (the commit before this step).
- **Files to restore:** `git checkout 516d385 -- src/Experience/HeyGanesh.LessonSurface tools/LessonSurfaceSmoke AGENTS.md docs/ai`; delete `src/Experience/HeyGanesh.LessonSurface/Page/vendor/anatomy-1482e846/`, `Page/scene-svg.js`, `Page/scene-svg.css` and any new smoke file (`git status` lists them).
- **Re-check after revert:** `dotnet build HeyGanesh.sln -c Debug -warnaserror` -> 0/0; LessonSurfaceSmoke --assert-source 14, --assert-scene-nowebgl 5, --assert-scene 19.
