# HANDOVER — Session Log

> Append-only session log. One entry per work session, **newest entry at the top**.
> Use the HANDOVER entry template in [TEMPLATES.md](./TEMPLATES.md) §1.
> Never delete or rewrite a prior entry — correct it with a new entry instead.

## Index

| Date | Model / version | Headline | Branch / commit |
|------|-----------------|----------|-----------------|
| 2026-09-23 | Muse Spark (muse-spark) | Scoped the AGENTS.md invariants claim — enforced vs design-only groups | `main` @ `94a8af1` (working tree) |
| 2026-09-23 | Muse Spark (muse-spark) | AGENTS.md sync to Element Selection reality + line-endings correction | `main` @ `94a8af1` (working tree) |
| 2026-09-23 | Muse Spark (muse-spark) | ES doc sync to green (ARCHITECTURE/TEST-CHECKLIST) + host `choice` print fix | `main` @ `94a8af1` (working tree) |
| 2026-09-23 | GLM-5.3-Flash | Restore `BuildCandidateSet` — ES module builds clean; ES-4..ES-7 verified against live windows | `main` @ `94a8af1` (working tree) |
| 2026-09-23 | GLM-5.3-Flash | Element Selection docs backfill (ARCHITECTURE / FLOW / TEST-CHECKLIST) — ES build still blocked (CS0103) | `main` @ `94a8af1` (working tree) |
| 2026-09-22 | Cline agent (model id not exposed to the agent) | DECISIONS.md recovery + Element Selection decisions (DEC-008/009) | `main` (working tree) |
| 2026-09-21 | GLM-5.3-Flash / Laguna S 2.1 | Vision / Coordinate Detection module + verification host | `main` (working tree) |
| 2026-09-21 | GLM-5.3-Flash | Screen Capture module + verification host | `main` (working tree) |
| 2026-09-21 | GLM-5.3-Flash | Global Hotkey Monitor module + verification host (first code in the repo) | `main` @ `fc1be37` |

---

<!-- SESSION ENTRIES BELOW THIS LINE — newest first -->

## 2026-09-23 — Muse Spark (muse-spark) — Scope the "tests enforce these" claim on the invariants section

- **Branch / commit:** `main` @ `94a8af1` — working tree, uncommitted (`AGENTS.md` itself is untracked).
- **Scope:** one logical change, doc-only. The `AGENTS.md` invariants header claimed "tests enforce these" for all five bullets, but only four have checks behind them. Split the section into an enforced group and a design group; log the fix here. No code, no new check rows, no `CONSTRAINTS.md` contact.

### Done
- `AGENTS.md:56` — header is now `## Architecture invariants — keep them true`. The parenthetical claim is gone, so nothing in the section is implied to be automated by default.
- `AGENTS.md:58` — new group intro above the four: "Enforced by automated checks (rows in `docs/ai/TEST-CHECKLIST.md`):".
- `AGENTS.md:65` — new group intro above the fifth: "Design invariants — hold by review and the cited decisions; no automated check yet:".
- All five bullets carried over **verbatim** — no rewording, no reordering beyond the sibling bullet dropping to the second group. Its DEC-008/DEC-009 citations and the duplicated-`ConfidenceLevel` / 0.8-0.5-tier language are untouched.
- The exact row-per-bullet audit lives in "Verification run" below rather than inline in `AGENTS.md`: four per-bullet row citations in the onboarding file would add cross-file couplings that go stale on the next checklist renumbering.

### Backing for each bullet (audited by reading TEST-CHECKLIST.md this session, not recalled)
- **Zero external NuGet dependencies** → S-2 ✅, V-2 ✅, ES-3 ✅ (all "none").
- **Capture Exclusion Contract** → EX-1..EX-4 ✅. EX-5 is 🚧 and belongs outside that set: it checks the *future overlay's* application of the contract, not the mechanism.
- **Physical Pixel Coordinate Space** → ES-4 ✅ (live cross-check: `GetWindowRect` vs UIA root `MATCH (same physical-px space)`). SC-8 covers the same ground but is a manual host-print check.
- **API Key Isolation** → V-3 ✅ and ES-7 ✅ (key-absent `KEY ERROR`, exit 12, no echo).
- **ES/Vision siblings** → **no row, hence the design group.** Grounded instead by direct inspection this session: each module's `.csproj` references only `ScreenCapture`, with no cross `ProjectReference` in either direction, and zero cross-`using` directives.

### In progress / next
- **Owner decision still open:** give the sibling invariant a real check row (a review row asserting no cross-reference in either `.csproj`/`using` set, or a CI grep), or accept the "design invariants" label as its permanent status. This step stops the header from lying; it does not create enforcement.
- **Nothing else in `AGENTS.md` is now over-claimed** — the four enforced bullets each have at least one ✅ automated row (see "Backing for each bullet" above), which was the specific doubt this change was made to settle.
- Remaining ES gaps are still coverage, not breakage: live Jev leg (needs `TYPESAFE_API_KEY`), `--annotate`/`annotated.jpg`, and a human-judged hit-check (the Vision V-6 analogue). All three live in the `TEST-CHECKLIST.md` ES note.

### Risks & gotchas
- **EX-5 is 🚧, and that is not a hole in the enforced group.** EX-1..EX-4 test the `CaptureExclusion` mechanism itself; EX-5 checks the *future overlay's* application of the contract, which cannot pass until the overlay exists. If someone later "tidies" the grouping by moving Capture Exclusion down to the design group because EX-5 is unfinished, that would be a regression in accuracy — the mechanism is enforced today.
- **Any new invariant defaults to the design group.** A bullet earns its way into the enforced group only with a real ✅ row; the whole point of this change is that the section no longer assumes enforcement.
- **Links to the old heading anchor are broken if any exist.** The heading changed from `## Architecture invariants (tests enforce these — keep them true)`; GitHub's slug for it changed with it. Sweep found no in-repo link using the old anchor (see Verification), but bookmark-style external references (a chat, a note) would 404 silently.
- **`read_files` returned `[outdated]` twice for `AGENTS.md` this session** while shell reads were correct. When editing this file, verify with `Get-Content`/`Select-String` rather than trusting a cached read.
- **Mixed encodings:** `AGENTS.md` is BOM-less CRLF, `HANDOVER.md` is BOM + CRLF. Normalize to each file's own shape after editing or the diff becomes whole-file churn.

### Files touched
- `AGENTS.md` — header reworded, two group intros added, sibling bullet moved to the second group (all five bullets verbatim otherwise).
- `docs/ai/HANDOVER.md` — index row + this entry.

### Verification run
- Regroup in place, read back from disk: `56` header `## Architecture invariants — keep them true`; `58` enforced-group intro; `60-63` the four bullets; `65` design-group intro; `67` the sibling bullet; `69` `## Environment facts` (the following section, undisturbed).
- Row audit performed by reading `docs/ai/TEST-CHECKLIST.md` this session (not recalled): S-2 ✅, V-2 ✅, ES-3 ✅ (NuGet-zero); EX-1 ✅, EX-2 ✅, EX-3 ✅, EX-4 ✅, EX-5 🚧 (Capture Exclusion); SC-3 ✅ and SC-8 *manual* plus ES-4 ✅ (Physical Pixel); V-3 ✅ and ES-7 ✅ (Key Isolation). Exactly four ⛔/🚧 markers exist in the file — EX-5, V-6, V-7, V-8 — and none of them backs a bullet in the enforced group.
- Sibling-invariant basis, checked directly: `src/HeyGanesh.ElementSelection/*.csproj` and `src/HeyGanesh.Vision/*.csproj` each contain exactly one `ProjectReference`, and it is `..\HeyGanesh.ScreenCapture\...` in both — **no cross-reference in either direction**. Cross-`using` greps (`using HeyGanesh.Vision` in ES sources, `using HeyGanesh.ElementSelection` in Vision sources) both return **0**. The 21 "Vision" occurrences inside ES sources are comment prose, and 0 occurrences of "Element Selection"/"ElementSelection" exist in Vision sources.
- Old-claim sweep: `Select-String -Pattern 'tests enforce these'` across `AGENTS.md`, `ARCHITECTURE.md`, `FLOW.md`, `TEST-CHECKLIST.md`, `DECISIONS.md` → no hits outside this entry quoting it.
- Encoding: `AGENTS.md` `cr == lf`, `first3 = 23 20 41`, `bomcount = 0`; `HANDOVER.md` `first3 = EF BB BF` with `cr == lf`.
- HANDOVER heading set: all pre-existing `##` headings still present, this entry on top (10 total incl. `## Index`).

## 2026-09-23 — Muse Spark (muse-spark) — AGENTS.md sync to Element Selection reality + line-endings correction

- **Branch / commit:** `main` @ `94a8af1` — working tree, uncommitted (`AGENTS.md` itself is untracked).
- **Scope:** one logical change — sync root `AGENTS.md`, the file every agent reads first, from the pre-ES world to the state already recorded in `docs/ai/ARCHITECTURE.md`, `FLOW.md`, `TEST-CHECKLIST.md` and this log. Plus a one-line factual correction (line 67). No code, no other doc, no `CONSTRAINTS.md` contact.

### Done
- `AGENTS.md:10` — pipeline is now `hotkey -> Screen Capture -> Element Selection (PRIMARY; Vision is FALLBACK) -> Jev -> Tutor Engine -> Visual Lesson Engine -> Screen Overlay / Voice`.
- `AGENTS.md:20-21` — new `Element Selection` subsystem row (PRIMARY coordinate-detection path; "the entry point is `SelectAsync(question, target?, ct)`, not Vision's `LocateAsync`") and Vision's row role now reads "— FALLBACK to Element Selection".
- `AGENTS.md:38` — `dotnet build src/HeyGanesh.ElementSelection -c Debug` added to the core-module build list.
- `AGENTS.md:45-51` — ES verification-host block added: offline paths need no key, plus the explicit warning that the library and host must be built **SEQUENTIALLY** (in parallel they race on the shared `obj` DLL and fail with `error CS2012`, which reads like a code fault but is not), then `--list-only`, `--fake`, `--fake --fake-none`.
- `AGENTS.md:54` — Models paragraph extended: ES uses the Jev choice model via TypeSafe (`ModelId` `jev-1.13.0`); live ES runs require `TYPESAFE_API_KEY`, "reported as presence/length only and never as a value".
- `AGENTS.md:58` — zero-NuGet invariant assembly list now (`GlobalHotkeyMonitor`, `ScreenCapture`, `ElementSelection`, `Vision`).
- `AGENTS.md:61` — API-Key-Isolation seam list now (`IVisionClient`, `IJevClient`). Both this and the sibling bullet below go slightly past the enumerated plan for that step — they are the step's "invariants section describing an ES-less world" instruction, and were flagged to the owner rather than folded in silently.
- `AGENTS.md:62` — **new** invariant bullet: Element Selection and Vision are siblings and must never reference each other (primary `SelectAsync` / fallback `LocateAsync`, duplicated `ConfidenceLevel`, matching 0.8/0.5 tier, fallback decided by a future Jev confidence-gate — DEC-008/DEC-009).
- `AGENTS.md:67` — corrected this session: "UTF-8 line endings" → "UTF-8 encoding with CRLF line endings (some files carry a BOM — preserve each file's existing encoding when editing)". The old wording conflated encoding with line endings; both halves are now measured facts.
- **Ownership verdict, on the record:** `AGENTS.md` is untracked but **not** user-owned like `CONSTRAINTS.md`. Evidence: it shows as `?? AGENTS.md` while `CONSTRAINTS.md` is a tracked `M`; `git ls-files --error-unmatch AGENTS.md` answers "did not match any file(s) known to git"; a marker scan for `USER-OWNED|user-owned|never edit|only the project owner` over `AGENTS.md` returns zero hits, while `CONSTRAINTS.md` carries the explicit "USER-OWNED FILE. Cline reads this file only… only the project owner writes here" header. Untracked is not the same as user-owned — the ES module and smoke host are equally untracked and are worked normally. Nothing in `CONSTRAINTS.md` was touched by that session; this session touches only `AGENTS.md:67` and this log.

### In progress / next
- **Decide the fate of the new sibling bullet** (see "Risks & gotchas" below): either add a check row that actually enforces it, or reword the section header so it stops claiming universal test coverage. Owner's call; no code change is implied either way.
- **Nothing is blocked in ES.** Remaining ES gaps are coverage, not breakage: the live Jev leg (needs `TYPESAFE_API_KEY`), `--annotate`/`annotated.jpg`, and a human-judged hit-check (the analogue of Vision's V-6). All three are listed in the `TEST-CHECKLIST.md` ES note.
- **The primary/fallback composition now lives in four places inside `AGENTS.md`** (pipeline line, two table rows, invariant bullet) plus `ARCHITECTURE.md:14-15`, `FLOW.md:18-29` and `TEST-CHECKLIST.md`. A future reversal means coordinated edits across all of them — accepted as the cost of making the onboarding file correct, but worth remembering before anyone "simplifies" one of them.

### Risks & gotchas
- **The sibling-invariant bullet is not actually enforced by a test.** The section header says "Architecture invariants (tests enforce these — keep them true)", but the other four bullets each have real check rows (SC/EX/ES/V); "ES and Vision must not reference each other" rests on DEC-008/DEC-009 wording plus `TEST-CHECKLIST.md` ES-3's project-reference inspection. The header over-claims by exactly this one bullet until it gains a check row — do not treat the header as proof.
- **Console rendering lies about encoding.** PowerShell displays em-dashes from these UTF-8 files as `â€”` — a CP1252 console artifact, not file corruption. Judge encoding by byte probe, never by what the terminal shows.
- **PowerShell 5.1 has no `` `u{...} `` escape.** A probe written as `[regex]::Matches($s,"`u{2014}")` silently searches for the literal text `u{2014}` and reports 0, which reads as "the characters are missing" when they are fine. Use `[string][char]0x2014` or raw hex (`E28094`) — this produced a false negative during the sync session before it was caught.
- **Mixed encodings across the repo:** `HANDOVER.md` and the ES module files are BOM + CRLF; `AGENTS.md`, `TEST-CHECKLIST.md`, `ARCHITECTURE.md`, `Helpers2.cs` are BOM-less CRLF. Normalize to the file's own existing shape or the diff becomes whole-file churn.
- **`HANDOVER.md` insert mechanics:** anchoring an insert on the *previous* entry's heading text consumes it and orphans the body (this happened during the ES doc-sync session; caught by a heading-set check and repaired). Insert positionally, then verify the heading set.
- **Build the ES library and its host sequentially** — in parallel they collide on the shared `obj\...\HeyGanesh.ElementSelection.dll` with `error CS2012`.

### Files touched
- `AGENTS.md` — the seven sync edits above plus the line-67 correction; still BOM-less CRLF.
- `docs/ai/HANDOVER.md` — index row + this entry.

### Verification run
- `AGENTS.md` encoding after the sync: `bytes=12667 cr=226 lf=226 first3=23 20 41 bomcount=0` (BOM-less CRLF; up from 10984 bytes / cr=216 by exactly the ten added lines).
- Byte-level multibyte check (definitive): `U+2014 count = 12`, `E28094 pairs = 12`, `U+2192 count = 1`, `E28692 pairs = 1`, `U+FFFD count = 0`, `EFBFBD pairs = 0` — real UTF-8 em-dashes and the `ES→Vision` arrow, zero replacement characters.
- Content counts: 11 `Element Selection`/`ElementSelection` hits (lines 10, 20, 21, 38, 46, 49, 50, 51, 54, 58, 62), 3 `FALLBACK` hits, 1 `TYPESAFE_API_KEY` hit, all 12 pre-existing `##` headings still present.
- The commands the sync added were run sequentially and passed: `dotnet build src\HeyGanesh.ElementSelection -c Debug` → **Build succeeded, 0 Warning(s), 0 Error(s)**; `dotnet build tools\ElementSelectionSmoke -c Debug` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- Containment: `git status --short` shape unchanged across the sync session — same 7 `M docs/ai/*` files, same 3 `??` entries, nothing staged or committed, no `docs/ai/*` or `CONSTRAINTS.md` write.
- Line-67 correction: verified by reading the line back and by a stale-claim sweep (`Select-String -Path AGENTS.md -Pattern 'UTF-8 line endings'` → no hits).

## 2026-09-23 — Muse Spark (muse-spark) — ES doc sync to green + host `choice` print fix

- **Branch / commit:** `main` @ `94a8af1` — working tree, uncommitted.
- **Scope:** one step with one code change in it — flip the Element Selection docs from "blocked" to their verified-green state, and make the smoke host actually print the Jev `choice` on the not-found path (the ES-6 row promised output the host never produced). Nothing else.

### Done
- `docs/ai/ARCHITECTURE.md:39` — dropped the stale `; build currently blocked, see ES-1` clause. Heading is now `### Element Selection (implemented — the PRIMARY coordinate-detection path)`.
- `docs/ai/TEST-CHECKLIST.md` — ES-1/ES-2 `⛔ blocked` → `✅`; ES-4/ES-5/ES-6/ES-7 `🚧` → `✅`, each citing the output actually observed (an earlier audit had already moved ES-3 to ✅, so the whole ES table is now green). The 5-line ES-1 "blocked" note became a 1-line resolved note pointing at these HANDOVER entries, plus an explicit list of what is still **not** verified for ES.
- `tools/ElementSelectionSmoke/ElementSelectionSmokeHost.Helpers2.cs` — **the fix I chose: move the print, not the row.** The `choice` line moved out of `if (r.Found)` into `if (r.Choice is not null)`, so the honest no-match (`choice=none`) and the hallucination-guard refusal now both show the raw id. `selected` / `boxOnScreen` / `center` stay gated on `Found` (they are null otherwise). Output-only change, no logic touched.
- `docs/ai/HANDOVER.md` — index row + this entry.

### In progress / next
- **Nothing is blocked in ES any more.** The remaining ES gaps are *coverage*, not breakage: the live Jev leg (needs `TYPESAFE_API_KEY`), `--annotate`/`annotated.jpg`, and any human-judged hit-check (the analogue of Vision's V-6). These are recorded in the TEST-CHECKLIST note rather than as failures.
- The ES ranking path has only ever selected the top-ranked candidate (`FakeJevClient` picks the first non-`none` id), so "does Jev pick the *right* element among 120?" is genuinely untested — today's runs prove plumbing, not judgement.

### Risks & gotchas
- ES-5's run this session selected `Slider 'Volume'` for the question "Volume" — nice corroboration that lexical ranking works, but it also means the fake client can look smarter than it is: it was handed the answer. Do not read ES-5 as an accuracy check.
- `PrintResult` now emits `choice` on the failure paths too (hallucination guard sets `Choice` alongside `FailureReason`). Intended; a reader seeing `FAILURE` + `choice` + no box should read it as "Jev named an id that was never offered".
- Coordinates legitimately go negative (`center (1209,−978)` seen this session when the cursor's monitor sits above the primary). Consumers must not assume positive screen coordinates.
- Repo mixes encodings: the ES module files and `HANDOVER.md` are BOM + CRLF, while `Helpers2.cs`, `TEST-CHECKLIST.md` and `ARCHITECTURE.md` are BOM-less CRLF. Both were preserved (verified by byte probe after each edit) — pick the wrong one and the diff is whole-file churn.
- Build the library and the host **sequentially**: in parallel they race on the shared `obj\...\HeyGanesh.ElementSelection.dll` and fail with `error CS2012`, which reads like a code fault but is not.

## 2026-09-23 — GLM-5.3-Flash — Restore `BuildCandidateSet` (ES-1/ES-2 unblocked, ES-3..ES-7 verified live)

- **Branch / commit:** `main` @ `94a8af1` — working tree, uncommitted (the module itself is untracked).
- **Scope:** one logical change — reconstruct the missing `CandidateEnumerator.BuildCandidateSet` that `Walk.cs:45` calls, so the module compiles and the documented ES checks can actually be run. No other refactors.

### Done
- `CandidateEnumerator.Elements.cs` (was a 65-byte no-op file) — `BuildCandidateSet(UiaElementArray found, IntPtr hwnd, string title, PixelRect windowRect, PixelRect rootPixelRect)` → `CandidateSet`, plus `TryBuildCandidate`: the DEC-008 filter in cheap-first order (non-empty name → usable non-empty rectangle → on-screen → enabled → control type in `SelectionOptions.InteractableControlTypes` / `DefaultInteractableControlTypes`). Ids are deliberately left empty — `c001..` is assigned after ranking in `ElementSelectionService`, because an id must mean rank position, not tree position.
- `CandidateEnumerator.Reads.cs` (was a 63-byte no-op file) — fail-closed cached-property readers: `TryReadName`, `TryReadControlType` (accepts both the documented VT_I4 id and the measured VT_BSTR localized name, mapping back through `UiaInterop.ControlTypeNamesByName`), `TryReadBool` (VT_BOOL or VT_I4 0/1; anything else is "unknown" and fails closed), `TryReadRect`, and `ComputePositionHint` (3x3, "top-left" … "center" … "bottom-right") for the Jev option text.
- Per-element guard inside `BuildCandidateSet`: a node that goes stale mid-walk is skipped and counted in `TotalEnumerated`, never fatal.
- `docs/ai/HANDOVER.md` — the docs-session entry appended first (as instructed), then this entry.

### In progress / next
- **Doc sync, deliberately not done today:** `docs/ai/ARCHITECTURE.md:39` still says "build currently blocked, see ES-1"; in `docs/ai/TEST-CHECKLIST.md`, ES-1/ES-2 still read ⛔ and ES-4/5/6 still 🚧. All of ES-1..ES-7 except ES-3's trivial repass are now verified green, so those statuses are stale. This is the single next action.
- **ES-6's checklist wording is wrong about the host's output.** `PrintResult` prints `choice` only inside `if (r.Found)`, so the not-found path shows `found=False`, `confidence=Low` and `none=0.62` in the probabilities line but never prints `choice=none`. Correct the row (or make the host print the choice on that path) during the doc sync.

### Risks & gotchas
- `GetCachedPropertyValue` throws `COMException 0x80040201` (**UIA_E_ELEMENTNOTAVAILABLE**) for nodes that go stale during the walk — reproduced live on a 44,599-node browser tree. The first version of this restore let that exception abort the entire enumeration (unhandled crash, exit −532462766). The per-element guard in `BuildCandidateSet` is load-bearing: do not remove it, and do not push the catches back down into the readers where they would only cover some reads.
- Live enumeration is large: a YouTube page gave ~44.6k control-view elements, ~7.7k after the filter. `ElementSelectionService`'s 120 cap plus `CandidateRanker` is what keeps the Jev request inside the state budget — the raw set must never be sent.
- Coordinates can legitimately be **negative** (`center (1209,−978)` in one run) when a monitor sits above/left of the primary. Consumers must not assume positive screen coordinates.
- Run the two builds sequentially. In parallel they collide on the shared `obj\...\HeyGanesh.ElementSelection.dll` and fail with `error CS2012` — that failure is a build-ordering artefact, not a code fault.

### Files touched
- `src/HeyGanesh.ElementSelection/CandidateEnumerator.Elements.cs` — `BuildCandidateSet` + `TryBuildCandidate` + the per-element guard.
- `src/HeyGanesh.ElementSelection/CandidateEnumerator.Reads.cs` — the cached-property readers and the position hint.
- `docs/ai/HANDOVER.md` — the docs-session entry, then this entry.

### Verification run
- `dotnet build src\HeyGanesh.ElementSelection -c Debug` → **Build succeeded, 0 Warning(s), 0 Error(s)** (before: `CS0103` at `Walk.cs(45,20)`, 0 warnings / 1 error).
- `dotnet build tools\ElementSelectionSmoke -c Debug` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- ES-4 `--question "x" --list-only --foreground` → window `'(145) YouTube - Brave'`, `enumerated: control-view elements=44599  interactive after filter=7669`, `coordinate cross-check: GetWindowRect=2582x1550+-11+-11  UIA root=2582x1550+-11+-11  ->  MATCH (same physical-px space)`, exit **0**. Same command with the default cursor target → `elements=455  interactive after filter=30`, exit **0**.
- ES-5 `--question "Save" --fake --foreground` → `found=True`, `choice=c001`, `selected=Edit 'Enter keywords...' 1350x106+534+-1031`, `boxOnScreen` + `center` printed in global physical px, `confidence=High (jev=0.88, winner=0.91)`, exit **0**.
- ES-6 `--question "Save" --fake --fake-none --foreground` → `found=False`, `confidence=Low`, probabilities `none=0.62`, no box, no `FailureReason`, exit **0** — the honest no-match, not a failure.
- ES-7 live mode with no key → `KEY ERROR: TypeSafe API key not found. Set the TYPESAFE_API_KEY environment variable.`, exit **12**, key never echoed. No network call was made (the key is absent in this environment).
- ES-3 → `Select-String -Pattern '<PackageReference'` across both `.csproj` files → **0 hits**.
- Change containment: only the two stub files carry today's timestamp inside the module; every other module file and every smoke-host file still shows 9/22. Both edited files remain BOM + CRLF (`EF BB BF`, `cr == lf`).
## 2026-09-23 — GLM-5.3-Flash — Element Selection docs backfill (ARCHITECTURE / FLOW / TEST-CHECKLIST)

- **Branch / commit:** `main` @ `94a8af1` — working tree, uncommitted (docs only).
- **Scope:** document the already-written Element Selection module (untracked on disk) as the PRIMARY coordinate-detection path with Vision as FALLBACK, in the three working docs. No code was changed in this session.

### Done
- `docs/ai/ARCHITECTURE.md` — Element Selection added to the module list (Vision's line marked FALLBACK) and given a detail block: assembly, public surface `SelectAsync(question, target?, ct)` (explicitly **not** `LocateAsync` — that is Vision's entry point), the enumeration → rank/cap 120 → Jev choice → UIA BoundingRectangle pipeline, the `0.5·JevConfidence + 0.5·WinnerProbability` blend with 0.8/0.5 tiers, PRIMARY-vs-FALLBACK composition, and the guide-only boundary.
- `docs/ai/FLOW.md` — Element Selection leg added to the leg list and a real (partial) path drawn: `hotkey → Screen Capture → [enumerate → rank/cap 120 → Jev select → UIA rect] → unified target`, with the failed-result branch down to Vision. The "intentionally empty" placeholder is gone. The `Choice == "none"` no-match case is documented as **not** a fallback trigger.
- `docs/ai/TEST-CHECKLIST.md` — new `## Element Selection checks` section, ES-1..ES-7, mirroring the S-/SC-/V- column shape.

### In progress / next
- **ES-1/ES-2 are red.** `CandidateEnumerator.Walk.cs:45` calls `BuildCandidateSet(...)` and no definition exists anywhere in `src/HeyGanesh.ElementSelection/`: `dotnet build src\HeyGanesh.ElementSelection -c Debug` → **Build FAILED, 0 warnings, 1 error**, `error CS0103: The name 'BuildCandidateSet' does not exist in the current context`. This also blocks the smoke host (ES-2) and therefore ES-4/5/6. The failure is recorded in the ES-table note rather than papered over. Restoring that method is the single next action.

### Risks & gotchas
- `CandidateEnumerator.Elements.cs` and `CandidateEnumerator.Reads.cs` are **not cruft**: each holds only two `using` directives, nothing references either by name, and `CandidateEnumerator.Walk.cs`'s header comment ("partial by necessity") says the class was split to satisfy an editor size limit — an interrupted refactor that most likely lost the `BuildCandidateSet` body. Both were left in place; disposition is the owner's call.
- Every file in this set is **BOM + CRLF** (`EF BB BF`, `cr == lf`), unlike the LF-only `DECISIONS.md` recovery blob. Match that or the diff picks up whole-file churn.
- The tree is dirty: 7 modified docs plus the untracked `src/HeyGanesh.ElementSelection/` and `tools/ElementSelectionSmoke/`. Nothing was staged or committed, and `CONSTRAINTS.md` was not touched (user-owned).
- Still stale after this session: `AGENTS.md`'s subsystem table and build commands do not mention Element Selection at all.

### Files touched
- `docs/ai/ARCHITECTURE.md` — module list line + Element Selection detail block; Vision marked as fallback.
- `docs/ai/FLOW.md` — Element Selection leg, fallback rule, and the composed path diagram.
- `docs/ai/TEST-CHECKLIST.md` — ES-1..ES-7 rows plus the ES-1/ES-2 blocking note.
- `docs/ai/HANDOVER.md` — this entry.

### Verification run
- `git --no-pager diff --stat` → `ARCHITECTURE.md | 15 ++--`, `FLOW.md | 30 ++++++`, `TEST-CHECKLIST.md | 18 +++`; no code file modified.
- Byte probe on the three edited docs → `cr == lf` on each, `first = 35` (`#`, no BOM), bytes 6906 / 3377 / 7908.
- `dotnet build src\HeyGanesh.ElementSelection -c Debug` → **0 Warning(s), 1 Error(s)** (CS0103 `BuildCandidateSet`) — re-run after the docs were written, which is how ES-1/2 were confirmed blocked rather than assumed.
- ES anchors resolve against real `DECISIONS.md` headings; ES rows split to the same column count as the V-* rows.

## 2026-09-22 — Cline agent (model id not exposed to the agent) — DECISIONS.md recovery + Element Selection decisions

- **Branch / commit:** `main` — working tree, uncommitted (`docs/ai/DECISIONS.md` only).
- **Scope:** restore the corrupted/decayed architecture-decision log to a clean, byte-integrity-preserved 9-entry state and record the Element Selection module's primary/Vision-fallback decision (DEC-008), which the code already references.
- **Did:**
  - Found `2891b919` (`dec_head`) is decayed: DEC-005/003 bodies are orphaned at the tail of DEC-006/004 (verified `bydesign_count=2`). Rebasing onto the clean 7-entry `dec_7ent` reference — where DEC-005/003 live as proper entries (`bydesign=1`) — avoids the duplication the literal "restore 2891b919 + insert" plan would have produced (24 relocated orphan lines + 48 added).
  - Added **DEC-008** (Element Selection = primary UIA path; Vision = fallback; Jev `jev-1.13.0` client seam at `IJevClient.cs`; 120-candidate cap + none-option + hallucination guard in `SelectionOptions.cs`; threshold-free tiering in `Mapping.cs`; guide-only — never injects input; the `SetFocus` slot at `UiaInterop.Interfaces.cs:58` exists only for COM vtable alignment, never called) — line-verified against `ElementSelectionService.cs`, `Mapping.cs`, `SelectionResult.cs`, `IJevClient.cs`, `SelectionOptions.cs`, `UiaInterop.Interfaces.cs`.
  - Added **DEC-009** (uniform `SelectionResult`/`VisionResult` shape; cross-module unification + the Jev confidence-gate deferred to the Context Orchestrator) — grounded in `SelectionResult.cs`.
  - Appended DEC-008/009 index rows; all 9 anchors resolve.
  - Fixed one misquoted tier expression (`blended >= 0.8f ? … : blended >= 0.5f ? … Medium : Low`).
- **Left:** nothing — recovery complete and verified; no code changed.
- **Watch:** `core.autocrlf=true` + `.gitattributes: * text=auto` CRLF-normalizes the worktree on a hard checkout until `docs/ai/*.md` is pinned to `text eol=lf`; on-disk file is LF-only (cr=0) and the committed blob stores LF. `src/HeyGanesh.ElementSelection` exists on disk but is not in the `ARCHITECTURE.md` subsystem table or the documented build commands — out of build scope here.
- **Next:** `git add docs/ai/DECISIONS.md` (committed as a clean LF blob); optionally pin `docs/ai/*.md` eol in `.gitattributes`.
- **Verified:** `git diff --no-index --stat dec_7ent.md docs/ai/DECISIONS.md` → 48 insertions, 0 deletions; `bytes=29323, first=35 (no BOM), cr=0 (LF), emdash=106, gamma=0`; DEC-001..009 each once; 9 index rows; order strictly-decreasing newest-first.

## 2026-09-22 — GLM-5.3-Flash / Laguna S 2.1 — Vision / Coordinate Detection module + verification host

- **Branch / commit:** `main` — working tree changes, uncommitted.
- **Scope:** implement **Vision / Coordinate Detection** (DeepSeek V4.1 Flash grounding over ScreenCapture output) + `tools/VisionSmoke`, before any orchestrator, overlay, Jev, or proxy. Plan approved with no overrides (0–1000 grid, temp 0.7 / 2 samples, detail high).

### Done
- `src/HeyGanesh.Vision/`, `net8.0-windows10.0.22621.0`, **zero `PackageReference`**, project-ref to ScreenCapture: `VisionService` (`LocateAsync` — sampling, tolerant parse + retry, blank-frame short-circuit, `CoordinateMapper` back-mapping), `VisionResult`/`VisionSample` (`Found`, description, normalized/image/source/screen boxes, `Confidence` + `RawConfidence`/`Consistency`, diagnostics), `IVisionClient` seam, `DeepSeekVisionClient` (temporary direct path, key from `DEEPSEEK_API_KEY`), `VisionPrompt`, `VisionResponseParser`, `CoordinateMapper`, `VisionOptions`.
- `tools/VisionSmoke/`: capture-or-`--image` → locate → console result + `capture.jpg`/`annotated.jpg`; `--samples/--max-dim/--detail/--out/--fake`; DPI-v2 opt-in. `--fake` uses an in-harness fixed-box client; live mode calls DeepSeek directly; harness never echoes the key.
- DEC-006 (grid + confidence design; no resolution matching) and DEC-007 (seam + temporary direct-key risk).

### In progress / next
- **Live visual hit-check is yours:** `dotnet run --project tools\VisionSmoke -c Debug -- --question "where is the Start button" --samples 2` with `DEEPSEEK_API_KEY` set; open `out\annotated.jpg`; per-target screen-px error belongs in the next HANDOVER entry. Also: key-absent fail-fast (exit 12), not-found path (exit 0, `found=false`), `--samples 4` consistency movement.

### Risks & gotchas
- **No live call has been made from this tree yet** — grounding precision on dense desktop UI is unproven; bad numbers are a feasibility finding.
- Direct-key stopgap risk recorded in DEC-007 (env inheritance, direct transit, capture-visible key); dies with the proxy module.
- Screen shots go to a cloud API by design — redaction policy is an undecided CONSTRAINTS item, flagged not solved.
- DPI: screen-space output assumes a PerMonitorV2 host (standing risk, now load-bearing for coordinates).
- Single-monitor machine: multi-monitor fan-out is the future orchestrator's job; untested here.
- UAC Secure Desktop / RDP / elevated: inherited blank-frame path from capture; `WasBlankFrame` surfaces it.

### Files touched
- `src/HeyGanesh.Vision/*` (9 files, incl. `IVisionClient.cs` now holding the consolidated `VisionRequest`/`VisionRawResponse` shape); `tools/VisionSmoke/*` (5 files); `docs/ai/{DECISIONS,HANDOVER,ARCHITECTURE,FLOW,TEST-CHECKLIST}.md` (below).

### Verification run
- `dotnet build src\HeyGanesh.Vision -c Debug` → **0 warnings, 0 errors**; `dotnet build tools\VisionSmoke -c Debug` → **0 warnings, 0 errors**; no `PackageReference` in either csproj (comment mentions only).
- `--help` → usage, exit 0. Live mode with key absent → `KEY ERROR`, exit **12**. `--fake --image <prior capture>` → `found=True`, `normalized {0.240,0.120 0.120x0.100}`, `rectangle 153x80+307+96`, `confidence High (model=0.86, consistency=1, samples=2)`, `annotated.jpg` written, exit **0** — parse→map→annotate path proven without network or key.

## 2026-09-21 — GLM-5.3-Flash — Screen Capture module + verification host

- **Branch / commit:** `main` — working tree changes, uncommitted.
- **Scope:** implement the **Screen Capture** module — on-demand still frames of every monitor via `Windows.Graphics.Capture`, plus a runnable verification host, before any app shell, orchestrator or UI exists.
- **Done:**
  - `src/HeyGanesh.ScreenCapture/`, `net8.0-windows10.0.22621.0`, **zero `PackageReference`**:
    - `NativeMethods.cs` — `EnumDisplayMonitors`, `GetMonitorInfoW`, `GetCursorPos`/`MonitorFromPoint`, `D3D11CreateDevice`, `CreateDirect3D11DeviceFromDXGIDevice`, `SetWindowDisplayAffinity`/`WDA_EXCLUDEFROMCAPTURE`.
    - `Monitors.cs` — `PixelRect`, `MonitorInfo`, `Monitors` (enumerate, cursor monitor, capture order).
    - `CaptureExclusion.cs` — public `TryExcludeFromCapture` / `TryIncludeInCapture` contract for our own windows.
    - `CpuFrameReader.cs` — `SoftwareBitmap.CreateCopyFromSurfaceAsync` + blank-frame detection via the `CryptographicBuffer` byte[]↔IBuffer bridge.
    - `WgcFrameGrabber.cs` — one-shot `CreateFreeThreaded` pool + two-phase settle wait; `IGraphicsCaptureItemInterop` vtable order and GUIDs confirmed from Microsoft's own sample.
    - `JpegEncoder.cs` — `BitmapEncoder` JPEG, quality 0.8, Fant downscale to 1280 px, aspect preserved, never upscales, Linear fallback.
    - `ScreenCaptureService.cs` — public API: `EnumerateMonitors`, `GetCursorMonitor`, `IsCaptureSupported`, `CaptureMonitorAsync`, `CaptureAllMonitorsAsync`, with `ScreenCaptureOptions` / `MonitorCapture`.
  - `tools/ScreenCaptureSmoke/` — verification host: monitor table + cursor marker, capture-all → JPEGs, JPEG magic / dimension / aspect / content checks, A/B exclusion proof (decode JPEGs + pixel diff with a same-rectangle noise floor and a control region), `--move-cursor` cursor sweep, `--repeats` leak soak, DPI opt-in.
  - DEC-004 (capture design, zero deps, **corrected settle window**) and DEC-005 (self-exclusion contract + measured proof).
- **Evidence (this session, 2560x1600 @ 150% DPI, build 26200):**
  - `dotnet build` for the library and the host → **0 warnings, 0 errors**; no `PackageReference`.
  - Every monitor captured with valid JPEGs (e.g. 2560x1600 → 1280x800, ~80 KB, 97 %+ non-black). `CreateForMonitor` returns `S_OK`. Per-capture time ~0.7 s (was ~2.5 s before the settle fix).
  - A 25× capture loop leaves handle delta 1 and a flat managed heap.
  - Exclusion A/B on an owned white-window probe: **100 %** of the probe rectangle is white with affinity off; **100 %** of those pixels change with affinity on; noise floor **0 %**, control region **0.6 %** — i.e. exclusion is measured, not inferred. The console HWND is correctly refused (`ERROR_ACCESS_DENIED`), proving the OS enforces the per-process scope.
- **Risks & gotchas:**
  - **Settle correction vs the plan:** WGC delivers frames on screen *change*, so a still desktop offers one frame. Waiting for frame 2 wasted ~2.5 s and still used frame 1 — now a bounded 250 ms preference window (`SettleTimeout`) with the 2 s `FrameTimeout` kept as the hard deadline (DEC-004).
  - **HDR:** `B8G8R8A8` content is tone-mapped on HD-color displays (washed colours); acceptable for vision input.
  - **Yellow border / borderless:** clearing the capture border needs a packaged app + `graphicsCaptureWithoutBorder` capability + consented `RequestAccessAsync(Borderless)`; a non-packaged host's `IsBorderRequired=false` is silently ignored. Deferred to the packaged shell.
  - **RDP / Secure Desktop:** monitor capture items cannot be created under RDP, and nothing is captured on the UAC Secure Desktop.
  - **`CreateForMonitor` E_INVALIDARG** reported on some 26100 machines; `S_OK` here on 26200. Documented fallback: `TryCreateFromDisplayId` once the Windows App SDK shell exists.
  - **DPI:** the library never sets process DPI awareness (process-global side effect). The host must be PerMonitorV2; the verification host sets it itself. Coordinates and frames are physical pixels.
  - **Exclusion lifecycle:** `WDA_EXCLUDEFROMCAPTURE` is per window and must be re-applied whenever a window is recreated (per-monitor overlays, hot-plug, rebuild). The Screen Overlay must apply it to the real top-level HWND.
- **Files touched:** `src/HeyGanesh.ScreenCapture/*.cs` (+ `.csproj`); `tools/ScreenCaptureSmoke/{Program.cs,ScreenCaptureSmoke.csproj}`; `docs/ai/DECISIONS.md` (DEC-004, DEC-005).
- **Open:** Context Orchestrator still pending; Vision/Coordinate Detection is the next consumer of `MonitorCapture.Jpeg`.

## 2026-09-21 — GLM-5.3-Flash — Global Hotkey Monitor module (first code in the repo)

- **Branch / commit:** `main` @ `fc1be37` — working tree has new `src/`, `tools/`, `.gitignore`, `docs/` (all uncommitted)
- **Scope:** implement the **Global Hotkey Monitor** (system-wide, listen-only, modifier-only push-to-talk) with a runnable verification host, before any app shell, orchestrator or UI exists

### Done
- `src/HeyGanesh.GlobalHotkeyMonitor/` class library, `net8.0-windows`, **zero `PackageReference`**:
  - `NativeMethods.cs` — `WH_KEYBOARD_LL` P/Invoke, `SafeHookHandle`, `KBDLLHOOKSTRUCT`.
  - `HotkeyChord.cs` — `ChordTracker`, the modifier-only chord state machine.
  - `GlobalHotkeyMonitor.cs` — `HotkeyState`, `HotkeyMonitorOptions`, `GlobalHotkeyMonitor`.
- Public surface exactly as specified: `Pressed` / `Released` events, `State` (`Idle`/`Listening`), `Start()`/`Stop()`/`Dispose()`, plus diagnostics `HookCallbackCount`, `DroppedEventCount`, `MaxCallbackLatency`, `LastError`.
- `tools/HotkeySmoke/` — manual verification host: timestamped events to console + `smoke.log`, `--seconds=N` for unattended runs, summary (including max callback latency) on exit.
- `.gitignore` — `bin/`, `obj/`, `.vs/`, `*.user`, `smoke.log`. Added because this change is the first to produce build output in the repo; without it `git status` is drowned in artifacts.
- Environment: installed **.NET SDK 8.0.425** via `winget install Microsoft.DotNet.SDK.8`. The machine had a broken runtime-only `dotnet` (no `sdk\`, no `host\fxr\` → `dotnet` exited 0x80008083).

### In progress / next
- Nothing half-finished. The module is standalone and consumes nothing else. Next candidates: the Context Orchestrator (subscribe to `Pressed`/`Released`), or Screen Capture.

### Risks & gotchas
- **AltGr:** chord requires **left** Alt, so AltGr (LCtrl+RAlt on many layouts) can never complete it. Right Alt is deliberately inert (`RequireLeftAlt` default `true`).
- **UIPI / elevated focus:** no events while an elevated window has focus, or on the UAC Secure Desktop. Presents as "chord does nothing", never as stuck `Listening`.
- **Silent unhook** (Windows' version of macOS "tap disabled by timeout"): the OS removes LL hooks whose callbacks exceed `LowLevelHooksTimeout` (~300 ms) with no notification. Mitigation is the trivial callback + diagnostics; **auto-rehook deferred on purpose** (DEC-003).
- **Do not add work to `HookProc`.** Measured max callback latency 0.63 ms (first-call JIT dominated; `WarmUpHookPath()` mitigates). Budget is 300 ms.
- **Events fire on `HeyGanesh.HotkeyMonitor.Worker`**, not the UI thread — WinUI subscribers must marshal.
- **`GetAsyncKeyState` staleness:** never re-read the modifier belonging to the event currently being delivered; it is not guaranteed to have caught up (this was a real bug — see below).
- `dotnet` SDK is now installed **machine-wide** in `C:\Program Files\dotnet` (winget, no elevation needed); the earlier broken install was overwritten by a working one.

### Files touched
- `src/HeyGanesh.GlobalHotkeyMonitor/{HeyGanesh.GlobalHotkeyMonitor.csproj, NativeMethods.cs, HotkeyChord.cs, GlobalHotkeyMonitor.cs}` — the module.
- `tools/HotkeySmoke/{HotkeySmoke.csproj, Program.cs}` — manual verification host.
- `.gitignore` — build output hygiene.
- `docs/ai/HANDOVER.md`, `docs/ai/DECISIONS.md` — this entry; DEC-001 (hook architecture), DEC-002 (AltGr/chord rules), DEC-003 (rehook deferred).

### Verification run
- `dotnet build src\HeyGanesh.GlobalHotkeyMonitor\HeyGanesh.GlobalHotkeyMonitor.csproj -c Debug` → **Build succeeded, 0 warnings, 0 errors**.
- `dotnet build tools\HotkeySmoke\HotkeySmoke.csproj -c Debug` → **Build succeeded, 0 warnings, 0 errors**.
- `HeyGanesh.Tools.HotkeySmoke.exe --seconds=4` → hook live at `state=Idle`, clean stop, summary printed, `last error: none`, **exit 0** (`hook callbacks seen: 0` — no keys were pressed during the unattended run, as expected).
- **Throwaway SendInput verifier** (built in `%TEMP%\heyganesh-hotkeyverify`, deliberately *not* committed; injects only Ctrl/Alt/F23/F24 so no text can reach a focused window) → **24/24 checks passed, exit 0**:
  - modifier-only hold raises `Pressed` and `state=Listening`; release raises `Released` and `state=Idle`; no duplicate on modifier auto-repeat;
  - events observed on thread `HeyGanesh.HotkeyMonitor.Worker`;
  - **listen-only proven**: a second LL hook installed *behind* the monitor's still received every notification (`observer=4 monitor=4`) — only possible because the hook proc returns `CallNextHookEx`;
  - a non-modifier key while held ends the press, re-arms only after full release, and itself still reaches the downstream hook;
  - ordinary key presses raise nothing and do **not** poison the next chord (chord still fires immediately after typing);
  - rapid tap yields a clean `Pressed`/`Released` pair, never stuck `Listening`;
  - `dropped=0`, `maxCallbackLatency=0.63 ms`, `lastError=none`, clean `Stop()` and re-hook, `Dispose()` leaves `Idle`.
- **Two real bugs found and fixed by that run** (both would have shipped silently, and both are exactly the kind the manual harness alone would have missed):
  1. `ResyncFromSystem()` overwrote fresh event-derived modifier state with stale async key state, including for the key being delivered — `LAlt` was cleared on its own key-down, so the chord never completed. Fixed by `ResyncModifiersExcept(currentKey)`.
  2. `_dirty` was set by *any* non-modifier key-down and only cleared on a required-modifier key-up, so after typing in any other app the next chord needed a wasted second attempt. Fixed by dirtying only while a required modifier is already down.
