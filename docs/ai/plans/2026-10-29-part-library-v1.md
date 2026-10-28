# Part Library v1 Implementation Plan

> 2026-10-29 · claude-opus-5-5 · Status: **BUILT 2026-10-29** (owner: "Yes, build it"): PL-1..PL-4 green (5/20/19/6 PASS). Deviations from this plan, as built: the type is `ModelLibrary` (a class named `PartLibrary` would clash with its namespace); the rotation convention and bounds helpers live in a separate `Geometry.cs`, and unrotated shapes get exact per-axis bounds (the sphere bound wrongly failed the flat membrane slab); `LibraryPart` carries an explicit rest pose (`Position`/`RotationDeg`) that a track's first key must equal (`rest.mismatch`); the DNA `unwind` animation became `spin` (bases are grouped by letter across both strands, so a strand-separation track has no part to move); the DNA total twist row checks 9 × 34.29° = 308.6° first-to-last pair (the 343° written below counted 10 steps for 9 gaps); hydrogen bonds are one bar per pair (25 separate bonds would break the 64-primitive budget). FEATURE-007 Phase 5, step 3 of 6.

**Goal:** The first three 3D teaching models as vetted, checkable data: named parts built from a closed primitive kit, one-line part descriptions, explode offsets, and keyframed animations. Wherever geometry or motion follows from physics or a standard measurement, it is **computed in C#**, never hand-placed. The library produces the `ModelCatalog` the scene3d contract checks against (DEC-028). Pure C#, offline. Nothing draws yet (step 4).

**Architecture:** New Intelligence assembly `HeyGanesh.PartLibrary`, referencing only `HeyGanesh.VisualLessonEngine` (for `ModelCatalog`/`CatalogModel`). Models are C# code built with a small builder, not data files. A deterministic `LibraryCheck` holds every model to structural rules. A new verification host `tools/PartLibrarySmoke` proves structure, physics and the catalogue round trip, and prints a human-readable dump for owner review.

**Spec:** [FEATURE-007](../features/FEATURE-007.md) §1.4 ("primitive kit + curated part library — parametric STEM models with vetted part names"), §3.1 (compute, don't recall), §3.2 (bind to vetted content; free-form composition is tagged **schematic**), §4 Phase 5 (10 models; v1 ships 3); DEC-028 (the contract stays ignorant of content; a library *produces* a catalogue); spike 0b (200 primitives is the measured budget for a whole scene).

## The problem, restated

The scene3d contract can now refuse a lesson that names a part that doesn't exist, but no real parts exist yet, only a test catalogue. Step 4's renderer needs actual geometry to draw. Students need the geometry to be *right*: an engine whose piston doesn't follow the crank, or a DNA helix with the wrong twist, teaches something false. So the library must make correctness checkable. Motion and shape come from formulas the host can verify. Names and descriptions sit in one reviewable place, honestly marked as not yet human-reviewed.

## Execution path

None changes. Nothing calls the library yet: `ValidationContext.Scene3d` stays null in every caller (DEC-028), so live lessons are unaffected. FLOW.md gets no new path. Step 4 is the first consumer: the renderer receives a model's geometry, and its host passes `PartLibrary.V1.ToCatalog()`.

## Design

### Types (`LibraryModel.cs`)

```csharp
public readonly record struct Vec3(double X, double Y, double Z);           // scene units, Y up, Babylon's left-handed frame
public enum PrimitiveShape { Box, Cylinder, Sphere, Torus, Capsule }        // closed: what step 4 must implement
public enum Finish { Metal, DarkMetal, Brass, Glass, Membrane, Protein, IonSodium, IonPotassium, Atp,
                     BaseAdenine, BaseThymine, BaseGuanine, BaseCytosine, Backbone }   // closed colour/material roles
public sealed record Primitive(PrimitiveShape Shape, Vec3 Size, Vec3 Position, Vec3 RotationDeg, Finish Finish);
  // Size: Box = w,h,d; Cylinder = diameterTop, height, diameterBottom (cone when they differ); Sphere = diameter,-,-;
  //       Torus = diameter, thickness, -; Capsule = diameter, height, -
public sealed record LibraryPart(string Id, string Name, string Description, IReadOnlyList<Primitive> Primitives, Vec3 ExplodeOffset);
public sealed record Keyframe(double T, Vec3 Value);                         // T in [0,1] of the animation's duration
public enum TrackProperty { Position, RotationDeg }
public sealed record Track(string Part, TrackProperty Property, IReadOnlyList<Keyframe> Keys);
public sealed record LibraryAnimation(string Id, string Name, double DurationSeconds, bool Loop, IReadOnlyList<Track> Tracks);
public sealed record LibraryModel(string Id, string Title, string Subject, bool Schematic, string Basis,
                                  IReadOnlyList<LibraryPart> Parts, IReadOnlyList<LibraryAnimation> Animations)
{ public bool Reviewed { get; init; } }                                     // false in v1, see "Vetting" below
```

`PartLibrary.V1` is an `IReadOnlyList<LibraryModel>` plus `ToCatalog()` → `ModelCatalog` (ids, part ids and animation ids only).

### `LibraryCheck` (structural rules, returns issues and never throws)

Each issue has a stable code, like `LessonIssueCodes`:

- **Ids:** model, part and animation ids match the contract's id pattern (`^[a-z][a-z0-9-]{0,31}$`) and are unique within the model.
- **Text:** name ≤ 40 chars, description 1–140 chars, title ≤ 80.
- **Primitives:** every part has ≥ 1 primitive; all sizes finite and > 0 where the shape uses them; all numbers finite.
- **Budgets:** ≤ 16 parts and **≤ 64 primitives per model**, so three live models (`MaxModelsPerScene`) stay under the 200 primitives spike 0b measured.
- **Bounds:** the assembled model fits a 4 × 4 × 4 box centred on the origin, so the camera framing in step 4 can assume one scale.
- **Animations:**
  - every track names an existing part;
  - keyframe `T` strictly ascending, first 0 and last 1;
  - duration in 0.5–20 s;
  - a looping animation's last key equals its first.

The library's own models must produce **zero** issues (row PL-2), and every rule also has a deliberately broken model in the host that proves the rule fires.

### The three models

1. **`four-stroke-engine`**: single-cylinder four-stroke engine, *computed*.
   - **Basis:** slider-crank kinematics with bore 0.8, crank radius r = 0.4 (stroke 0.8), rod length L = 1.4 (L/r = 3.5, a typical ratio).
   - **Parts** (10): `cylinder` (glass sleeve), `cylinder-head`, `piston`, `piston-pin`, `connecting-rod`, `crankshaft`, `counterweight`, `valve-intake`, `valve-exhaust`, `spark-plug`.
   - **Animation `stroke-cycle`** (720° of crank over 4 s, looping), sampled every 15° (49 keys), all computed:
     - crankshaft and counterweight rotate θ;
     - piston and pin height y(θ) = r·cos θ + √(L² − r²·sin² θ);
     - rod centre and angle φ = asin(r·sin θ / L);
     - intake valve open 0–180°, exhaust valve open 540–720°, closed otherwise.
     - Simplification stated in the descriptions: no valve overlap, no timing advance.
2. **`dna-helix`**: B-DNA, 10 base pairs, *computed*.
   - **Basis:** standard B-DNA parameters: 10.5 bp per turn (34.3° twist per bp), 0.34 nm rise per bp, 2.0 nm diameter, scaled ×1 scene unit per nm.
   - **Fixed sequence** `ATGCGTACCA` with its Watson–Crick complement.
   - **Parts** (7): `backbone-left`, `backbone-right`, `adenine`, `thymine`, `guanine`, `cytosine`, `hydrogen-bonds`.
     - Each backbone is a series of spheres on its helix.
     - Each base part holds the half-rungs of that base.
     - Rungs are short cylinders oriented by computed vectors.
   - **Animation `unwind`:** the two strands separate along the explode axis, looping.
3. **`na-k-pump`**: sodium–potassium pump, **schematic** (`Schematic = true`, which step 4 shows as a visible "schematic" tag per §3.2).
   - **Basis:** stoichiometry, not geometry: 3 Na⁺ out and 2 K⁺ in per ATP hydrolysed. Shapes are illustrative.
   - **Parts** (6): `membrane` (bilayer slab), `pump-protein`, `sodium-ions` (3 spheres), `potassium-ions` (2 spheres), `atp`, `binding-sites`.
   - **Animation `pump-cycle`:** Na⁺ moves inside → outside, then K⁺ moves outside → inside, with ATP at the protein's cytoplasmic side.

These are the three FEATURE-007 names the earlier step-2 plan pointed at (engine, DNA helix, cell-membrane pump). The other seven of Phase 5's ten stay for later, driven by eval misses (FEATURE-007 Risks).

### Physics rows (what "computed" buys: the host checks the numbers, not the look)

- **Engine:**
  - piston at top dead centre at θ = 0° and bottom dead centre at 180°;
  - TDC − BDC height = stroke (0.8 ± 1e-9);
  - the distance from crank pin to piston pin equals L at every one of the 49 keys;
  - the intake valve is open only in keys inside 0–180° and the exhaust only inside 540–720°.
- **DNA:**
  - consecutive base pairs rise 0.34 and twist 34.3°;
  - every backbone sphere sits at radius 1.0 from the axis;
  - every pair is A–T or G–C;
  - the total twist over 10 bp is 343° ± 0.01.
- **Pump:**
  - `sodium-ions` has exactly 3 spheres and `potassium-ions` exactly 2;
  - after `pump-cycle`, every Na⁺ sits above the membrane's outer face and every K⁺ below its inner face.

### Vetting (honest about what "vetted" means today)

The part names and descriptions are written by the model building this, from standard textbook facts, and are **not yet reviewed by a person**. Every v1 model therefore ships `Reviewed = false`. `PartLibrarySmoke --dump <model>` prints each model's title, basis, parts with descriptions, primitive counts, bounds and animations as plain text for owner review. Flipping `Reviewed` is an owner action recorded in DECISIONS, never something a model does. Step 4 can show unreviewed models with the same visible tag as schematic ones; that's step 4's call.

## Files

| File | Change |
|---|---|
| `src/Intelligence/HeyGanesh.PartLibrary/HeyGanesh.PartLibrary.csproj` (new) | net8 Windows TFM, zero PackageReference, one ProjectReference (VisualLessonEngine); header comment states what the module must never do |
| `.../LibraryModel.cs` (new) | the types above |
| `.../ModelBuilder.cs` (new) | small fluent builder used by the models (keeps model files readable) |
| `.../LibraryCheck.cs` (new) | structural rules + issue codes |
| `.../PartLibrary.cs` (new) | `V1`, `ToCatalog()`, `Find(id)` |
| `.../Models/FourStrokeEngine.cs`, `DnaHelix.cs`, `SodiumPotassiumPump.cs` (new) | the three models; formulas commented with their source |
| `tools/PartLibrarySmoke/` (new: csproj, `Program.cs`, `PartLibrarySmokeHost.cs`, `.Rows.cs`) | modes below |
| `HeyGanesh.sln` | add the library under the Intelligence folder |
| `.github/workflows/ci.yml`, `docs/ai/CI.md` | new gate **G-16 Part Library offline** |
| `docs/ai/DECISIONS.md` | DEC-029: library as C# code; computed geometry; schematic and reviewed flags; budgets |
| `docs/ai/ARCHITECTURE.md`, `AGENTS.md` | the new assembly in the module map; host commands |
| `docs/ai/TEST-CHECKLIST.md` | PL-1..PL-5 |
| `docs/ai/features/FEATURE-007.md`, `docs/ai/HANDOVER.md`, `docs/ai/ROLLBACK.md` | status, handover, rollback (written **before** the code: more than 3 files) |

No change to VisualLessonEngine, LessonSurface, LessonPlanner, LessonSession or the App.

## `PartLibrarySmoke` modes (rows)

| Row | Mode | Asserts |
|---|---|---|
| PL-1 | `--assert-source` | zero PackageReference; exactly one ProjectReference (VisualLessonEngine); no P/Invoke; no file/network/process/environment/registry access (the LessonSmoke audit needles) |
| PL-2 | `--assert-library` | every V1 model has 0 `LibraryCheck` issues; budgets and bounds hold; a broken copy per rule produces exactly that rule's code |
| PL-3 | `--assert-physics` | the engine, DNA and pump rows above |
| PL-4 | `--assert-catalog` | `ToCatalog()` holds exactly the V1 ids/parts/animations; `engine-3d.json` rewritten to `four-stroke-engine` validates `Valid` against it; a plan naming `engine.flux-capacitor` gets `part.unknown`; every V1 model is `Reviewed = false` |
| PL-5 | `--dump <model\|all>` | human-readable review dump, exit 0 (manual: the owner reads it) |

## Alternatives considered

- **glTF assets authored in Blender.** Rejected for v1: it needs an asset pipeline and binary files that review poorly. Motion couldn't be checked against physics, and the result would be "recall, not compute". It stays a later option for organic shapes (heart, eye) that formulas don't describe.
- **JSON data files as embedded resources.** Rejected for v1: no compile-time checking, a second parser, and the computed models (engine, DNA) would still need code. Worth revisiting if non-programmers start authoring models.
- **Putting the library inside VisualLessonEngine.** Rejected: DEC-028 keeps the contract ignorant of content. The library depends on the contract, not the other way round.
- **Ten models now.** Rejected: three cover the three kinds of model the library must handle (mechanical/computed, molecular/computed, schematic). Further models are content, not design, and FEATURE-007 says the library grows from eval misses.

## What could break, and how we'd know

- **Geometry that passes every check but looks wrong.** Only visible in step 4. Mitigations: the bounds row, the physics rows, and the review dump. Step 4 then adds snapshot PNGs of each model.
- **Wrong or misleading part descriptions.** The checks can't detect this. Mitigation: `Reviewed = false` plus the dump for owner review, before any of it reaches a student (the planner learns the library only in step 6).
- **Budget too tight for later models** (64 primitives). It's a named constant; raising it needs a re-measure like spike 0b.
- **sln build** picks up a new project: G-1 and the fresh-checkout build confirm it.

## Verification

Sequential builds (CS2012), exit codes from the built EXE:

```
dotnet build src/Intelligence/HeyGanesh.PartLibrary -c Debug -warnaserror
dotnet build tools/PartLibrarySmoke -c Debug -warnaserror
tools/PartLibrarySmoke/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.Tools.PartLibrarySmoke.exe --assert-source | --assert-library | --assert-physics | --assert-catalog   -> all PASS, exit 0
...PartLibrarySmoke.exe --dump all                                     -> readable dump, exit 0
LessonSmoke (12 modes, 128 PASS), LessonPlannerSmoke (39), LessonTurnSmoke (20)   -> unchanged
dotnet build HeyGanesh.sln -c Debug -warnaserror; local CI runner (now 12 gates); fresh-checkout build   -> 0 / 0, all exit 0
```

Not verifiable in this step: how the models look. That's step 4.
