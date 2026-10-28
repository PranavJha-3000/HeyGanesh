# VisualPlan Contract v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A pure-C#, offline-verified contract for model-authored lessons: parse untrusted VisualPlan JSON, validate it against hard limits and reference rules, and degrade bad beats (or the whole plan) to plain spoken text instead of failing.

**Architecture:** New assembly `HeyGanesh.VisualLessonEngine` (Intelligence layer) with immutable records, a `System.Text.Json` (in-box) parser that never throws on model output, and a validator that returns an outcome + issue list. A new verification host `tools/LessonSmoke` proves it with golden fixtures, the house pattern (`--assert-*` rows, `PASS`/`FAIL` lines, exit codes). No rendering, no provider, no I/O beyond the host reading its fixtures.

**Tech Stack:** .NET 8 (`net8.0-windows10.0.22621.0`), C# latest, `System.Text.Json` (BCL). Zero PackageReference.

**Spec:** [docs/ai/features/FEATURE-007.md](../features/FEATURE-007.md) — §2.4 (workflows), §3 items 3–4 (closed vocabularies, referential integrity), §4 Phase 1; BUILD-PLAYBOOK M7 S7.1 ("malformed model output degrades to plain text with a notice").

## Global Constraints

- Target framework `net8.0-windows10.0.22621.0`; `LangVersion latest`; `Nullable enable`; `ImplicitUsings enable`.
- Zero `PackageReference`. Exactly one `ProjectReference`: `src/Foundation/HeyGanesh.Abstractions` (for `PixelRect`).
- Empty P/Invoke surface (no `DllImport`/`LibraryImport`), no `System.Net`, no file or registry access in the library.
- Screen-mark coordinates are physical pixels (`PixelRect`), validated against a caller-supplied virtual-desktop rectangle.
- Parser and validator never throw on model output; only a `null` argument (caller bug) throws `ArgumentNullException`.
- `.cs` files: tabs, size 4 (`.editorconfig`); UTF-8; CRLF line endings.
- Build library and host **sequentially** — parallel builds race on the shared obj DLL (CS2012).
- Read exit codes from the built EXE `tools/LessonSmoke/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.Tools.LessonSmoke.exe`, not `dotnet run`.
- Fixed limits (`LessonLimits.Default`): `MaxJsonBytes = 65536`, `MaxDepth = 16`, `MaxSurfaces = 4`, `MaxBeats = 40`, `MaxOpsPerBeat = 12`, `MaxTitleChars = 80`, `MaxSayChars = 400`, `MaxTextChars = 280`, `FallbackDegradedRatio = 0.5`.
- Schema id: exactly `"heyganesh.visualplan/1"`. Every id (surface, beat, element) matches `^[a-z][a-z0-9-]{0,31}$`.
- Glyph vocabulary (`LessonVocabulary.Glyphs`, ordinal): `atom, bulb, book, cell, flask, gear, leaf, magnet, cycle, scale`.

## Review Focus

1. **Model wraps JSON in prose or a ```json fence** — a real model often does. Expect: the parser extracts the outermost `{…}` once (first `{` to last `}`) before parsing; anything else stays malformed. Test in Task 2.
2. **A valid plan whose every beat is degraded** — expect `TextFallback` with the `say` lines joined by single spaces, never an empty lesson. Test in Task 5.
3. **A beat whose `say` is empty but whose ops are fine** — nothing would be spoken while things are drawn. Expect the beat to be degraded with `field.missing` (speech is mandatory). Test in Task 3.
4. **Screen mark on a second monitor at negative coordinates** (monitor left of primary) — expect valid when inside the virtual-desktop rect, `mark.out-of-bounds` only when outside. Test in Task 4.
5. **Huge or deeply nested payload** — expect `json.too-large` / `json.malformed` with outcome `Rejected`, no exception, no partial plan. Test in Task 2.

---

## File Structure

```
src/Intelligence/HeyGanesh.VisualLessonEngine/
  HeyGanesh.VisualLessonEngine.csproj   project; intent comment in house style
  VisualPlan.cs                         plan, surface, beat records + enums
  LessonOps.cs                          LessonOp hierarchy (closed set + UnknownOp)
  LessonLimits.cs                       LessonLimits + LessonVocabulary
  LessonIssue.cs                        LessonIssue + issue-code constants
  VisualPlanParser.cs                   JSON -> ParseResult (never throws on input)
  VisualPlanValidator.cs                ParseResult -> LessonValidationResult
tools/LessonSmoke/
  LessonSmoke.csproj
  Program.cs
  LessonSmokeHost.cs                    mode dispatch, usage, Check helper, fixture loader
  LessonSmokeHost.Parse.cs              --assert-parse
  LessonSmokeHost.Limits.cs             --assert-limits
  LessonSmokeHost.Refs.cs               --assert-refs
  LessonSmokeHost.Degrade.cs            --assert-degrade
  LessonSmokeHost.Fixtures.cs           --assert-fixtures
  LessonSmokeHost.Source.cs             --assert-source
  fixtures/chain-rule.json              golden: 5 beats, math + plot + screen mark
  fixtures/tensor-2d.json               golden: 4 beats, steps + arrows + highlight
  fixtures/photosynthesis.json          golden: 6 beats, icons + steps
```

Docs touched in Task 6 only: `docs/ai/TEST-CHECKLIST.md` (LS rows), `docs/ai/ARCHITECTURE.md` (module line), `docs/ai/DECISIONS.md` (DEC-020-contract entry — numbered at write time after the owner's DEC-020/021/022), `docs/ai/features/FEATURE-007.md` (status), `docs/ai/HANDOVER.md` (session entry), `AGENTS.md` (Commands block).

### Schema v1 (what fixtures and parser agree on)

```json
{
  "schema": "heyganesh.visualplan/1",
  "lessonId": "chain-rule-ln",
  "title": "Differentiating ln(3x^2 - 1)",
  "mode": "explain",
  "surfaces": [ { "id": "card-main", "kind": "card", "title": "chain rule" } ],
  "beats": [
    { "id": "b1", "say": "First, square and scale x.",
      "ops": [ { "op": "write", "surface": "card-main", "id": "w1", "text": "inside", "style": "heading", "math": "3x^2 - 1" } ] },
    { "id": "b2", "say": "The slope of the inside is 6x.",
      "ops": [ { "op": "highlight", "target": "w1" },
               { "op": "mark", "shape": "circle", "x": 1240, "y": 860, "width": 96, "height": 32 } ] }
  ]
}
```

Op wire names → records: `write` (surface,id,text,style∈heading|body|note,math?), `step` (surface,id,index≥1,text), `arrow` (surface,from,to,label?), `icon` (surface,id,glyph), `plot` (surface,id,expr,xMin<xMax), `highlight` (target), `mark` (shape∈circle|underline|arrow, x,y,width>0,height>0), `clear` (surface). `mode` ∈ `explain|socratic|worked-example`; `kind` ∈ `card` (v1 only). Any other `op` string parses to `UnknownOp`.

---

### Task 1: Scaffold assembly + host + source audit

**Files:**
- Create: `src/Intelligence/HeyGanesh.VisualLessonEngine/HeyGanesh.VisualLessonEngine.csproj`
- Create: `tools/LessonSmoke/LessonSmoke.csproj`, `Program.cs`, `LessonSmokeHost.cs`, `LessonSmokeHost.Source.cs`
- Modify: `HeyGanesh.sln` (two `dotnet sln add`)

**Interfaces:**
- Produces: `internal static partial class LessonSmokeHost` with `static Task<int> RunAsync(string[] args)`; `static int Check(bool ok, string label, int failures)` printing `  PASS  <label>` / `  FAIL  <label>`; `static int Summarize(int failures, string okLabel)` printing `<okLabel> (exit 0)` or `FAILED: <n> check(s) (exit 1)`; `static string Fixture(string name)` reading `fixtures/<name>` next to the EXE. Exit codes: 0 pass, 1 failed check, 2 usage.

- [ ] **Step 1: Write the failing row** — `LessonSmokeHost.Source.cs` → `RunAssertSource()`: locate repo root by walking up to `HeyGanesh.sln`, read the library csproj and every `src/Intelligence/HeyGanesh.VisualLessonEngine/*.cs`, and check:
  - `Check(!csproj.Contains("PackageReference"), "zero PackageReference", …)`
  - `Check(Regex.Matches(csproj, "ProjectReference").Count == 1 && csproj.Contains("HeyGanesh.Abstractions"), "only ProjectReference is Abstractions", …)`
  - `Check(!anySource("DllImport") && !anySource("LibraryImport"), "empty P/Invoke surface", …)`
  - `Check(!anySource("System.Net") && !anySource("File.") && !anySource("Registry"), "no network, file or registry access", …)`
  - `Summarize(failures, "SOURCE AUDIT OK")`
- [ ] **Step 2: Build host, see it fail** — `dotnet build tools/LessonSmoke -c Debug` → error: project reference not found (library does not exist yet).
- [ ] **Step 3: Create the library csproj** — mirror `HeyGanesh.TutorEngine.csproj` PropertyGroup, `RootNamespace`/`AssemblyName` `HeyGanesh.VisualLessonEngine`, one `ProjectReference` to `..\..\Foundation\HeyGanesh.Abstractions\HeyGanesh.Abstractions.csproj`, and an intent comment block: what the module is for (validated lesson contract), what it must never do (render, speak, call providers, touch the screen), and why the model's output is untrusted. Host csproj mirrors `tools/TutorSmoke/TutorSmoke.csproj`, references the library, and copies `fixtures\*.json` to output (`<None Include="fixtures\*.json" CopyToOutputDirectory="PreserveNewest" />`). `dotnet sln add` both.
- [ ] **Step 4: Run** — build library, then host (sequentially), then `HeyGanesh.Tools.LessonSmoke.exe --assert-source` → 4 PASS, `SOURCE AUDIT OK (exit 0)`; `--help` → usage, exit 0; no args → usage, exit 0.
- [ ] **Step 5: Commit** — `git add src/Intelligence/HeyGanesh.VisualLessonEngine tools/LessonSmoke HeyGanesh.sln` · `git commit -m "feat(lesson): scaffold VisualLessonEngine + LessonSmoke source audit"`

### Task 2: Plan model + parser

**Files:**
- Create: `VisualPlan.cs`, `LessonOps.cs`, `LessonIssue.cs`, `LessonLimits.cs`, `VisualPlanParser.cs`
- Create: `tools/LessonSmoke/LessonSmokeHost.Parse.cs`, `fixtures/chain-rule.json`

**Interfaces:**
- Produces (namespace `HeyGanesh.VisualLessonEngine`):
  - `public enum LessonMode { Explain, Socratic, WorkedExample }`, `public enum SurfaceKind { Card }`, `public enum TextStyle { Heading, Body, Note }`, `public enum MarkShape { Circle, Underline, Arrow }`
  - `public sealed record LessonSurface(string Id, SurfaceKind Kind, string Title)`
  - `public sealed record LessonBeat(string Id, string Say, IReadOnlyList<LessonOp> Ops) { public bool Degraded { get; init; } }`
  - `public sealed record VisualPlan(string Schema, string LessonId, string Title, LessonMode Mode, IReadOnlyList<LessonSurface> Surfaces, IReadOnlyList<LessonBeat> Beats)`
  - `public abstract record LessonOp;` with `WriteOp(string Surface, string Id, string Text, TextStyle Style, string? Math)`, `StepOp(string Surface, string Id, int Index, string Text)`, `ArrowOp(string Surface, string From, string To, string? Label)`, `IconOp(string Surface, string Id, string Glyph)`, `PlotOp(string Surface, string Id, string Expr, double XMin, double XMax)`, `HighlightOp(string Target)`, `ScreenMarkOp(MarkShape Shape, PixelRect Region)`, `ClearOp(string Surface)`, `UnknownOp(string Name)` — all `public sealed record … : LessonOp`
  - `public sealed record LessonIssue(string Code, string? BeatId, string Message)` + `public static class LessonIssueCodes` with string consts: `JsonMalformed="json.malformed"`, `JsonTooLarge="json.too-large"`, `SchemaUnsupported="schema.unsupported"`, `FieldMissing="field.missing"`, `FieldTooLong="field.too-long"`, `LimitSurfaces="limit.surfaces"`, `LimitBeats="limit.beats"`, `LimitOps="limit.ops"`, `IdInvalid="id.invalid"`, `IdDuplicate="id.duplicate"`, `RefSurface="ref.surface"`, `RefTarget="ref.target"`, `MarkOutOfBounds="mark.out-of-bounds"`, `OpUnknown="op.unknown"`, `GlyphUnknown="glyph.unknown"`, `ValueInvalid="value.invalid"`
  - `public sealed record LessonLimits` with the Global-Constraints values as `init` properties and `public static LessonLimits Default { get; }`; `public static class LessonVocabulary { public static IReadOnlySet<string> Glyphs { get; } }`
  - `public sealed record ParseResult(VisualPlan? Plan, IReadOnlyList<LessonIssue> Issues)`
  - `public static class VisualPlanParser { public static ParseResult Parse(string json, LessonLimits? limits = null); }`

- [ ] **Step 1: Write the failing row** — `RunAssertParse()`:
  - `Parse(Fixture("chain-rule.json"))` → `Plan != null`, `Issues.Count == 0`, `Beats.Count == 5`, `Beats[0].Ops[0] is WriteOp { Math: "3x^2 - 1" }`, a `ScreenMarkOp` with `Region == new PixelRect(1240, 860, 96, 32)`
  - same fixture wrapped as `"Here is the lesson:\n```json\n" + json + "\n```"` → identical plan (Review Focus 1)
  - `Parse("{not json")` → `Plan == null`, single issue `json.malformed`
  - `Parse(new string(' ', 65537))` → `Plan == null`, issue `json.too-large` (Review Focus 5)
  - 20-level nested object → `Plan == null`, `json.malformed` (Review Focus 5)
  - a beat with `{"op":"teleport"}` → plan parses, that op `is UnknownOp { Name: "teleport" }`
  - `"mode":"lecture"` → `Plan != null` with `Mode == Explain` and issue `value.invalid` (unknown enum values default and are reported, never throw)
  - `Summarize(failures, "PARSE OK")`
- [ ] **Step 2: Build, see it fail** — build errors: `VisualPlanParser` not defined.
- [ ] **Step 3: Implement** the records and `Parse`. Size check on UTF-8 byte count before anything else; then extract outermost `{…}`; then `JsonDocument.Parse` with `MaxDepth = limits.MaxDepth` inside one `try/catch (JsonException)` → `json.malformed`. Map properties by exact camelCase name; missing required strings become `""` (the validator reports them) rather than parse failures. Write `chain-rule.json` per the schema section (5 beats: inside function, outer derivative, chain multiply, simplify, plot of `6*x/(3*x^2-1)` on `[1, 4]` + one screen mark).
- [ ] **Step 4: Run** — build library then host; `--assert-parse` → 7 PASS, `PARSE OK (exit 0)`; `--assert-source` still exit 0.
- [ ] **Step 5: Commit** — `git commit -m "feat(lesson): VisualPlan v1 records and untrusted-input parser"`

### Task 3: Structural validation and limits

**Files:**
- Create: `VisualPlanValidator.cs`, `tools/LessonSmoke/LessonSmokeHost.Limits.cs`

**Interfaces:**
- Consumes: Task 2 types.
- Produces:
  - `public enum LessonOutcome { Valid, PartiallyDegraded, TextFallback, Rejected }`
  - `public sealed record LessonValidationResult(LessonOutcome Outcome, VisualPlan? Plan, string FallbackText, IReadOnlyList<LessonIssue> Issues)`
  - `public sealed class VisualPlanValidator { public VisualPlanValidator(LessonLimits? limits = null); public LessonValidationResult Validate(ParseResult parsed, PixelRect virtualDesktop); }`
  - Plan-level failures (schema id ≠ `heyganesh.visualplan/1`, zero beats, beats > `MaxBeats`, surfaces > `MaxSurfaces`, title missing/too long, surface id invalid/duplicate) → `Rejected` when no beat has a non-empty `say`, otherwise `TextFallback`. Beat-level failures degrade only that beat (Task 5 defines the policy; here the validator records issues with the beat's id).

- [ ] **Step 1: Write the failing row** — `RunAssertLimits()` builds plans in code from the chain-rule fixture (mutate a parsed copy with `with`) and asserts:
  - unmodified fixture → `Outcome == Valid`, zero issues
  - `Schema = "heyganesh.visualplan/2"` → issue `schema.unsupported`, outcome `TextFallback`
  - 41 beats → `limit.beats`, outcome `TextFallback`
  - 5 surfaces → `limit.surfaces`
  - a beat with 13 ops → issue `limit.ops` carrying that beat's id
  - `Say` of 401 chars → `field.too-long`; `Say = ""` with valid ops → `field.missing` on that beat (Review Focus 3)
  - a `WriteOp` text of 281 chars → `field.too-long`
  - beat id `"B 1"` → `id.invalid`; two beats `"b1"` → `id.duplicate`
  - `StepOp` index 0 → `value.invalid`; `PlotOp` with `XMin == XMax` → `value.invalid`
  - `IconOp` glyph `"rocket"` → `glyph.unknown`; `UnknownOp` → `op.unknown`
  - `Summarize(failures, "LIMITS OK")`
- [ ] **Step 2: Build, see it fail** — `VisualPlanValidator` not defined.
- [ ] **Step 3: Implement** the checks above. Issues are emitted in plan order (surfaces, then beats in order, ops in order) — Task 6's determinism row depends on it.
- [ ] **Step 4: Run** — `--assert-limits` → 14 PASS, `LIMITS OK (exit 0)`.
- [ ] **Step 5: Commit** — `git commit -m "feat(lesson): structural validation and hard limits"`

### Task 4: Reference integrity and screen-mark bounds

**Files:**
- Modify: `VisualPlanValidator.cs`
- Create: `tools/LessonSmoke/LessonSmokeHost.Refs.cs`, `fixtures/tensor-2d.json`

**Interfaces:**
- Consumes: `VisualPlanValidator.Validate(ParseResult, PixelRect)` from Task 3.
- Produces: no new public API — new issue emissions only.

- [ ] **Step 1: Write the failing row** — `RunAssertRefs()`, desktop `new PixelRect(-1920, 0, 3840, 1080)` (a monitor left of primary):
  - `tensor-2d.json` (4 beats: write grid, steps for rows/columns, arrow from `w-row` to `w-col`, highlight `w-cell`) → `Valid`
  - op `surface: "card-x"` not declared → `ref.surface`
  - `ArrowOp.To` names an element first created in a **later** beat → `ref.target` (forward reference)
  - `HighlightOp.Target` names nothing → `ref.target`
  - `ClearOp` on a surface, then a later `HighlightOp` of an element written before the clear → `ref.target` (cleared elements are gone)
  - element ids are unique across the whole plan: second `WriteOp` id `w1` → `id.duplicate`
  - mark at `(-1800, 100, 50, 50)` → valid (Review Focus 4); mark at `(1900, 100, 50, 50)` → `mark.out-of-bounds`; mark width `0` → `value.invalid`
  - `Summarize(failures, "REFS OK")`
- [ ] **Step 2: Run, see it fail** — rows report FAIL (no ref checks yet), exit 1.
- [ ] **Step 3: Implement** — walk beats in order with a `Dictionary<string, string>` of live element id → surface; `write/step/icon/plot` add, `clear` removes that surface's ids, `arrow/highlight` look up. A mark is in bounds when fully contained in `virtualDesktop` (`X >= d.X && Y >= d.Y && Right <= d.Right && Bottom <= d.Bottom`). Write `tensor-2d.json`.
- [ ] **Step 4: Run** — `--assert-refs` → 9 PASS, `REFS OK (exit 0)`; `--assert-limits`, `--assert-parse` still exit 0.
- [ ] **Step 5: Commit** — `git commit -m "feat(lesson): reference integrity and physical-pixel mark bounds"`

### Task 5: Degradation policy

**Files:**
- Modify: `VisualPlanValidator.cs`
- Create: `tools/LessonSmoke/LessonSmokeHost.Degrade.cs`

**Interfaces:**
- Produces the outcome rules other phases rely on:
  - any beat with ≥1 beat-level issue → that beat is returned with `Ops = []`, `Degraded = true`, `Say` unchanged (speech survives; drawing is dropped). A beat whose `Say` is empty is dropped entirely.
  - `Valid`: zero issues. `PartiallyDegraded`: ≥1 degraded beat and degraded/total `< FallbackDegradedRatio`. `TextFallback`: ratio `>=` 0.5, or a plan-level failure, with `Plan = null`. `Rejected`: no beat has a non-empty `Say`, or `ParseResult.Plan == null`.
  - `FallbackText`: always populated except for `Rejected` (where it is `""`) — non-empty `Say` values of all beats in order, joined with a single space. Lets the caller speak *something* in every non-rejected outcome.

- [ ] **Step 1: Write the failing row** — `RunAssertDegrade()`:
  - chain-rule with beat 2's highlight pointing at `nope` → `PartiallyDegraded`, `Plan.Beats[1].Degraded && Ops.Count == 0`, `Plan.Beats[1].Say` unchanged, other beats intact
  - 3 of 5 beats broken → `TextFallback`, `Plan == null`, `FallbackText == string.Join(" ", allFiveSays)` (Review Focus 2)
  - all beats broken → `TextFallback` with all five says
  - every `Say` empty → `Rejected`, `FallbackText == ""`
  - `Parse("{not json")` fed to `Validate` → `Rejected`, issues carry `json.malformed`
  - `Summarize(failures, "DEGRADE OK")`
- [ ] **Step 2: Run, see it fail** — exit 1.
- [ ] **Step 3: Implement** the rules exactly as listed under Interfaces.
- [ ] **Step 4: Run** — `--assert-degrade` → 5 PASS, `DEGRADE OK (exit 0)`; all earlier rows exit 0.
- [ ] **Step 5: Commit** — `git commit -m "feat(lesson): degrade-to-speech and text-fallback policy"`

### Task 6: Golden fixtures, determinism, docs

**Files:**
- Create: `tools/LessonSmoke/LessonSmokeHost.Fixtures.cs`, `fixtures/photosynthesis.json`
- Modify: `docs/ai/TEST-CHECKLIST.md`, `docs/ai/ARCHITECTURE.md`, `docs/ai/DECISIONS.md`, `docs/ai/features/FEATURE-007.md`, `docs/ai/HANDOVER.md`, `AGENTS.md` (Commands block — LessonSmoke lines)

**Interfaces:**
- Consumes everything above. Produces the LS row ids later phases extend.

- [ ] **Step 1: Write the failing row** — `RunAssertFixtures()`: for each of `chain-rule.json`, `tensor-2d.json`, `photosynthesis.json` → `Validate(Parse(f), new PixelRect(0, 0, 2560, 1440))` is `Valid`; and run each twice through fresh parser/validator instances asserting identical outcome, identical issue sequence, and identical `FallbackText` (determinism, the TU-10 analogue). `Summarize(failures, "FIXTURES OK")`.
- [ ] **Step 2: Run, see it fail** — `photosynthesis.json` missing → FAIL, exit 1.
- [ ] **Step 3: Write `photosynthesis.json`** — 6 beats, surfaces `card-inputs` + `card-process`, icons `leaf`/`bulb`/`flask`, steps 1–4, one arrow. Content reviewed for accuracy (6CO₂ + 6H₂O → C₆H₁₂O₆ + 6O₂, light-dependent reactions in thylakoids, Calvin cycle in stroma).
- [ ] **Step 4: Run all rows** from the built EXE: `--assert-source` (4), `--assert-parse` (7), `--assert-limits` (14), `--assert-refs` (9), `--assert-degrade` (5), `--assert-fixtures` (6) — every one exit 0.
- [ ] **Step 5: Docs** — TEST-CHECKLIST rows `LS-1` source audit … `LS-6` fixtures + `LS-7` build (library and host build with 0 warnings, 0 errors), each with command, expected PASS count, exit 0, and verification date. ARCHITECTURE: one module line for `HeyGanesh.VisualLessonEngine` (contract only; renderer pending DEC-020). DECISIONS: entry recording schema v1, its limits, and the degrade policy, with the alternatives rejected (JSON Schema validator → NuGet; throwing on bad model output → loses the spoken lesson). FEATURE-007: Phase 1 status. HANDOVER: five-line session entry. AGENTS.md: LessonSmoke commands next to TutorSmoke.
- [ ] **Step 6: Commit** — `git commit -m "feat(lesson): golden fixtures, determinism row, LS-1..LS-7 docs"`
