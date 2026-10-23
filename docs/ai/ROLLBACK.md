## 2026-10-23 · Cline agent (S0.4: CI and Safety Gates)

- **Change:** add .github/workflows/ci.yml, docs/ai/CI.md, .github/workflows/scan-forbidden.ps1, .github/workflows/scan-secrets.ps1, and add six new standing rows (S-10..S-13, S-15..S-16) + local-only note to docs/ai/TEST-CHECKLIST.md. No source code, no project changes, no new dependencies, no live provider calls, no target-framework changes, no restructuring. docs/ai/HANDOVER.md is NOT modified as part of S0.4 (missing R1 closeout is reported separately).
- **Rollback point:** remove the 6 files listed above; restore docs/ai/TEST-CHECKLIST.md to its pre-S0.4 state; leave docs/ai/ROLLBACK.md unchanged except for this entry.
- **Files to remove:** .github/workflows/ci.yml, .github/workflows/scan-forbidden.ps1, .github/workflows/scan-secrets.ps1, docs/ai/CI.md, docs/ai/TEST-CHECKLIST.md (new rows only), docs/ai/ROLLBACK.md (this entry).
- **Re-check after revert:** dotnet build -c Debug (0 warnings/0 errors), all 9 project builds succeed, all existing verification hosts pass with the original arguments, docs/ai/CONFIGS.md and docs/ai/AGENTS.md unchanged, docs/ai/HANDOVER.md unchanged (no R1 closeout written as part of S0.4).
- **Re-verify with:** dotnet build -c Debug, dotnet build -c Debug <each of 9 projects>, dotnet run --project tools\<Host> -c Debug -- <original offline args>; confirm zero warnings/errors and original exit codes (0 for offline fakes, 12 for keyless live-path, 13 for cancel).
- **Dependencies:** None. All CI checks run offline; no external tools are installed.

---
# ROLLBACK — Rollback Points & Procedures
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
- **Verified:** `git diff --no-index --stat dec_7ent.md docs/ai/DECISIONS.md` → 48 insertions, 0 deletions; byte probe `bytes=29323, first=35 (no BOM), cr=0 (LF), emdash=106, gamma=0`; DEC-001..009 each once; 9 index rows; all anchors resolve.