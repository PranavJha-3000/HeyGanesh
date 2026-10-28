# Scene3d Contract (VisualPlan 1.1) Implementation Plan

> 2026-10-29 · claude-opus-5-5 · Status: **BUILT 2026-10-29** (owner: "Yes, build it"): LS-13 22 PASS, every existing lesson row unchanged. FEATURE-007 Phase 5, step 2 of 6.

**Goal:** Extend the VisualPlan contract so a lesson can declare one 3D scene and drive it beat by beat (place a library model, label a part, highlight it, explode, animate, move the camera), with every 3D reference checked in C# before anything is drawn. Pure C#, offline. No renderer, no Babylon, no part geometry in this step.

**Architecture:** Same assembly (`HeyGanesh.VisualLessonEngine`), same parse → validate → degrade pipeline (DEC-023). New: one surface kind, five ops, a `ModelCatalog` the validator checks model/part/animation names against, and a schema minor `heyganesh.visualplan/1.1`. 3D is **off unless the caller says its canvas can draw it**: `ValidationContext.Scene3d` (a `ModelCatalog?`) defaults to `null`, and with `null` any `scene3d` surface is a plan-level issue. Every caller that exists today passes nothing, so their behaviour is unchanged.

**Spec:** [FEATURE-007](../features/FEATURE-007.md) §2.4 ("click a 3D part"), §3 items 2–4 (bind to vetted content, closed vocabularies, referential integrity), §4 Phase 5; DEC-023 ("the 3D `scene3d` surface arrives as a schema minor"); spike 0b conditions (Phase 0 results: one scene at a time, ~360–500 MB).

## The problem, restated

Lessons can only draw on 2D cards. Phase 5's demo ("show me a 4-stroke engine in 3D, click the connecting rod") needs the model to *say* "put the engine here, label the connecting rod, highlight it, run the stroke cycle". Following DEC-023, that has to arrive as data in a closed vocabulary, checked against vetted content, so a confused model can at worst produce a wrong-but-valid scene: never invented parts, never a reference to something not in the scene yet.

## Execution path (FLOW.md "Lesson leg" / Lesson Planner sections)

`VisualPlanner.PlanAsync` → `VisualPlanParser.Parse` → `VisualPlanValidator.Validate(parsed, ValidationContext)` → `LessonPlayer` → `ILessonCanvas` (`CardSurface`). This step changes **only the parse and validate stages**. `LessonPlayer` does not inspect op types (checked: no op `switch` in `Runtime/`). `CardSurface.ApplyBeatAsync` silently skips op types it doesn't know and lays out one 2D card per declared surface whatever its kind. That is why the `Scene3d = null` gate matters: a 3D plan can't reach `CardSurface` until step 4 gives it a 3D renderer and the host passes a catalogue. `VisualPlanner` builds its `ValidationContext` without `Scene3d`, so live planning stays 2D-only (step 6 changes that).

## Design

### Schema

- `VisualPlanValidator.SupportedSchema` stays `"heyganesh.visualplan/1"` (the planner prompt and fixtures use it). Add `Schema11 = "heyganesh.visualplan/1.1"`. The validator accepts both.
- A `/1` plan that declares a `scene3d` surface gets a plan-level `schema.unsupported` issue ("scene3d needs /1.1"). A `/1.1` plan with only cards is valid.

### Surface

- `SurfaceKind.Scene3d`; JSON `"kind": "scene3d"`. At most **one** per plan (`LessonLimits.MaxScenes = 1`, `limit.scenes`), because the spike measured ~360–500 MB per 3D page.
- `ValidationContext.Scene3d is null` and the plan declares a scene → plan-level `surface.unsupported`, so the whole plan becomes TextFallback and the speech is kept (the existing "surface issues cost the drawing" rule).

### Ops (closed set; JSON `op` names in quotes)

| Op | JSON fields | Creates element | Rules |
|---|---|---|---|
| `ModelOp` `"model"` | `surface, id, model` | yes, a model *instance* | surface must be a `scene3d`; `model` must be in the catalogue (`model.unknown`); at most `MaxModelsPerScene = 3` live instances (`limit.models`) |
| `LabelOp` `"label"` | `surface, id, target, text` | yes | surface must be a `scene3d`; `target` is a live instance id or a part ref (below); text ≤ `MaxTextChars` |
| `ExplodeOp` `"explode"` | `target, amount` | no | `target` is a live instance; `amount` finite, 0..1 (`value.invalid`) |
| `AnimateOp` `"animate"` | `target, animation` | no | `target` is a live instance; `animation` is one of that model's catalogue animations (`animation.unknown`) |
| `CameraOp` `"camera"` | `surface, focus, view` | no | surface must be a `scene3d`; `focus` is a live instance or part ref; `view` ∈ `front, side, top, iso` |

- **Part refs** are written `"<instanceId>.<partId>"`, e.g. `"engine.connecting-rod"`. Ids may not contain `.` (`IdPattern`), so a part ref can't collide with an element id. A part ref resolves only if the instance is live **and** the instance's catalogue model has that part (`part.unknown`, distinct from `ref.target` so evals can tell "invented part" from "out-of-order reference").
- **Existing ops on the scene:** `highlight` now accepts a part ref as well as an element id (the target rules above). `clear` on a scene removes its instances and labels (the existing per-surface `Live` sweep already does this). `write`/`step`/`icon`/`plot`/`arrow` on a scene, or `model`/`label`/`camera` on a card → `ref.surface-kind`.
- `mark` (screen marks) is unchanged; it has no surface.

### Catalogue (`ModelCatalog.cs`, new)

```csharp
public sealed record CatalogModel(string Id, IReadOnlySet<string> Parts, IReadOnlySet<string> Animations);
public sealed class ModelCatalog { public static ModelCatalog Empty { get; } ...; public bool TryGet(string id, out CatalogModel model); public IReadOnlyCollection<CatalogModel> Models { get; } }
```

Names only: no geometry, no descriptions. Step 3 (part library v1) adds the real library and its geometry as a separate type that *produces* a `ModelCatalog`. Keeping the contract ignorant of content means the validator can be tested with a small test catalogue, and "3D off" is expressible. Catalogue ids follow `IdPattern`, enforced when the catalogue is built (an `ArgumentException`, since a bad catalogue is a programming error, not model output).

### Determinism and degrade

Unchanged rules: issues in plan order; a degraded beat rolls back the board (instances created in it never exist); ≥ 50 % degraded → TextFallback; speech always survives. The board gains `instance → model` and `surface → kind` maps.

## Files

| File | Change |
|---|---|
| `src/Intelligence/HeyGanesh.VisualLessonEngine/VisualPlan.cs` | `SurfaceKind.Scene3d` |
| `.../LessonOps.cs` | `ModelOp`, `LabelOp`, `ExplodeOp`, `AnimateOp`, `CameraOp`, `CameraView` enum |
| `.../ModelCatalog.cs` (new) | `CatalogModel`, `ModelCatalog` (+ `Empty`) |
| `.../LessonLimits.cs` | `MaxScenes = 1`, `MaxModelsPerScene = 3` |
| `.../LessonIssue.cs` | codes `surface.unsupported`, `ref.surface-kind`, `model.unknown`, `part.unknown`, `animation.unknown`, `limit.scenes`, `limit.models` |
| `.../VisualPlanParser.cs` | `"scene3d"` kind; the five ops; `view` enum read with `value.invalid` on unknowns; `amount` via `ReqDbl` |
| `.../VisualPlanValidator.cs` | `Schema11`; `ValidationContext.Scene3d`; scene checks; part-ref resolution in `CheckTarget` |
| `tools/LessonSmoke/LessonSmokeHost.Scene3d.cs` (new) | `--assert-scene3d` rows + a test catalogue (`engine-test` with parts `piston, connecting-rod, crankshaft, valve-intake`, animation `stroke-cycle`) |
| `tools/LessonSmoke/LessonSmokeHost.cs` | register the mode |
| `tools/LessonSmoke/fixtures/engine-3d.json` (new) | hand-authored /1.1 golden lesson: scene + card, 6 beats, model/label/highlight part/explode/animate/camera |
| `.github/workflows/ci.yml`, `docs/ai/CI.md` | add `--assert-scene3d` to G-13 |
| `docs/ai/DECISIONS.md` | DEC-028 (the 1.1 contract shape and the off-by-default gate) |
| `docs/ai/TEST-CHECKLIST.md` | LS-13 |
| `docs/ai/FLOW.md` | one paragraph in the validation section: the `Scene3d` gate |
| `docs/ai/features/FEATURE-007.md`, `docs/ai/HANDOVER.md`, `docs/ai/ROLLBACK.md` | status, handover, rollback entry (written **before** the code: more than 3 files) |

No change to LessonSurface, LessonPlanner, LessonSession or the App.

## `--assert-scene3d` rows (LS-13)

1. The golden `engine-3d.json` with the test catalogue → `Valid`, zero issues; twice → identical issue lists (determinism).
2. The same plan with `Scene3d = null` → `TextFallback` with `surface.unsupported`, and the fallback text equals the joined `say` lines.
3. The same plan declared `/1` → `schema.unsupported`.
4. Two `scene3d` surfaces → `limit.scenes`; a fourth live model → `limit.models` on that beat only.
5. `model` not in the catalogue → `model.unknown`, beat degraded, later beats that use the instance → `ref.target`.
6. Part ref to an invented part (`engine.flux-capacitor`) → `part.unknown`; part ref before the model beat → `ref.target`; a part ref after `clear` → `ref.target`.
7. `animate` with an animation the model lacks → `animation.unknown`; `explode` amount 1.5 / NaN → `value.invalid`.
8. `write` on the scene / `model` on a card → `ref.surface-kind`; `camera` with `view: "fisheye"` → `value.invalid`.
9. `highlight` on a part ref on a live instance → valid; the board rolls back a degraded model beat (its instance never becomes live).
10. Every existing LessonSmoke mode still passes with unchanged counts (v1 behaviour is untouched).

## Alternatives considered

- **A separate 3D scene document beside the plan** (a scene graph the model writes once). Rejected: it loses beat-by-beat sync with speech (temporal contiguity, FEATURE-007 §3.7), and needs a second validator and reference model.
- **A static global catalogue (`LessonVocabulary.Models`, like `Glyphs`).** Rejected: it couples the contract to content, so step 3 couldn't change the library without touching the validator. It also can't express "this host has no 3D renderer", which is exactly what keeps step 2 safe while `CardSurface` can't draw 3D.
- **Schema `/2`.** Rejected: nothing in v1 changes meaning, so DEC-023's "schema minor" is enough, and every v1 fixture and the planner prompt stay valid untouched.
- **Parameters on `model` (e.g. cylinder count).** Deferred: DEC-023 says ops grow from eval failures, not speculation. Step 3 decides whether any library model needs them.

## What could break, and how we'd know

- **Existing v1 rows change behaviour.** The new op names were previously `UnknownOp`. Checked: no existing row or fixture uses `model`/`label`/`explode`/`animate`/`camera`/`scene3d`, and the one `op.unknown` row uses `teleport`. Row 10 re-runs every mode; counts must match today's (106 PASS lines across G-13's 11 modes).
- **A 3D plan reaches `CardSurface` and draws an empty card.** Prevented by the `Scene3d = null` default; row 2 pins it. `LessonTurnSmoke` and `LessonPlannerSmoke` re-run to show the callers are unchanged.
- **Issue ordering drifts** (LS-6 determinism): row 1 runs twice and compares.
- **Source audit** (`--assert-source`): the new file must stay I/O-free and add no references; the audit re-runs.

## Verification

Sequential builds (CS2012), exit codes from the built EXEs:

```
dotnet build src/Intelligence/HeyGanesh.VisualLessonEngine -c Debug -warnaserror
dotnet build tools/LessonSmoke -c Debug -warnaserror
tools/LessonSmoke/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.Tools.LessonSmoke.exe --assert-scene3d   -> all PASS, exit 0
...LessonSmoke.exe  each of the 11 existing modes                                                      -> unchanged PASS counts, exit 0
tools/LessonPlannerSmoke ... all 8 modes; tools/LessonTurnSmoke ... the 5 offline modes                  -> exit 0
dotnet build HeyGanesh.sln -c Debug -warnaserror                                                         -> 0 / 0
the local CI runner over all 11 gates; scan-forbidden / scan-secrets                                     -> pass
```

Not verifiable in this step: anything visual. Nothing renders 3D until step 4.

## Phase 5 sequence (for context)

1. ~~Spike 0b~~ (done, GO). 2. **This plan.** 3. Part library v1 (3 models → 10; produces a `ModelCatalog` + geometry). 4. 3D renderer in LessonSurface (vendored Babylon 9.30.0, pre-warm, render on demand, DEC on memory). 5. Click-a-part → follow-up question. 6. Planner prompt + golden lessons; `VisualPlanner` passes the catalogue.
