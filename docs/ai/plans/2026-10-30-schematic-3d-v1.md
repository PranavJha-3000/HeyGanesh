# Plan: 3D for objects with no library model, as clearly tagged schematics (build sequence step 10)

2026-10-30 · claude-opus-5-5 · STATUS: 10a BUILT 2026-10-30 (owner: "Yes, build 10a"): LS-14 20 PASS, PL-6 7 PASS; DEC-048.
Changed while building: sizes are [0, 4] (a size spans the shape), not (0, 2]. 10b–10d are not built.

## Problem, restated

A 3D lesson can show only the three Part Library models: the four-stroke engine, the DNA helix and the
sodium-potassium pump. The validator refuses any other model id (`model.unknown`), which is how lessons are kept from
inventing content. So "draw the full jet in 3D", the reference video's E-04, or "show me the Raspberry Pi" can only
be taught with flat cards.

FEATURE-007 §3.2 already allows the alternative: "free-form primitive composition is allowed but rendered with a
visible schematic tag". Most of the machinery for this exists:
- `LibraryModel` has a `Schematic` flag (the pump uses it);
- `LibraryCheck` enforces geometry budgets;
- the scene window already shows a "schematic" tag for such models (`scene.js`);
- both the WebGL renderer and the SVG fallback draw any `LibraryModel` from the same five primitives.

What's missing is a way for a **lesson** to define one.

## Design

**One new optional plan field, schema 1.2.** It sits in the header, before `beats`:

```json
"schematics": [
  { "id": "f35b", "title": "F-35B (simplified)",
    "parts": [
      { "id": "fuselage", "name": "Fuselage",
        "shapes": [ { "shape": "capsule", "size": [0.5, 3.0, 0.5], "at": [0, 0, 0], "turn": [0, 0, 90], "finish": "metal" } ] },
      { "id": "lift-fan", "name": "Lift fan",
        "shapes": [ { "shape": "cylinder", "size": [0.6, 0.15, 0.6], "at": [-0.6, 0.2, 0], "turn": [0, 0, 0], "finish": "dark-metal" } ] }
    ] }
]
```

- **Closed vocabularies:**
  - shapes: `box`, `cylinder`, `sphere`, `torus`, `capsule` (the five the renderers already draw);
  - finishes: `metal`, `dark-metal`, `brass`, `glass` (the existing general ones; no new colours in v1).
- **Limits:**
  - 1 schematic per plan;
  - ≤ 12 parts and ≤ 40 shapes;
  - every number finite;
  - sizes in (0, 2] and positions within ±2 (`LibraryCheck.HalfExtent`);
  - names ≤ 40 characters;
  - the id may not collide with a catalogue id.
- **Using it:** a `model` op may name it, and `label`, `highlight`, `explode` and `camera` may target its parts as
  `<instance>.<part>`. `animate` is refused, because a schematic has no animations.
- **Honesty:**
  - the scene window's "schematic" tag appears automatically;
  - the planner prompt requires one beat to say it's a simplified drawing;
  - a new eval check, K-12, scores that.
- **Validation:**
  - **VisualLessonEngine**: parsing and the structural rules.
  - **PartLibrary**: geometry. A new `Schematics.ToModel(spec)` builds a `LibraryModel` (`Schematic = true`, basis
    "drawn by the lesson model; simplified, not to scale", never `Reviewed`) and runs the existing `LibraryCheck`.
  - **The seam:** `ValidationContext` gains an optional geometry check delegate, so the engine still doesn't
    reference the Part Library. The planner's callers (the shell, the hosts) pass PartLibrary's check.
  - **Failure:** a failed schematic is dropped, and the beats that used it degrade, the same path as an unknown model.
- **Streaming:** the scanner already releases nothing until the header is complete, and `schematics` is part of the
  header, so beats still validate against a complete header. Placing `schematics` before `beats` is a prompt rule,
  tested by a row.

## Steps (each approved on its own)

| Step | What | Where | Rows |
|---|---|---|---|
| **10a** | Contract: parse, validate, convert, check geometry; a golden `f35b-schematic.json` fixture | VisualLessonEngine (`VisualPlan`, parser, validator, `ValidationContext`), PartLibrary (`Schematics.cs`) | LessonSmoke `--assert-schematic`; PartLibrarySmoke `--assert-schematic`; existing LS/PL rows unchanged |
| **10b** | The planner offers schematics: a SCHEMATIC block in the 3D prompt (format, limits, "only when no catalogue model fits", "say it is simplified", header before beats). Eval: K-12, and E-04 gets a schematic pass seed plus an untagged fail seed | LessonPlanner `PlannerPrompt`; PlannerEval | LP-10 update, LP-12 `--assert-prompt-schematic`, LP-11 streams the fixture; PE-2 +2 |
| **10c** | The scene window draws them: `SceneMessages.Model` takes the resolved `LibraryModel` (library or the lesson's schematic), and `CardSurface` keeps the lesson's schematics for drawing and for click-to-ask part checks | LessonSurface (C# only; scene.js already shows the tag) | LSU `--assert-scene-schematic` (real window, local) and its SVG twin; `--snapshot-3d` gains the fixture |
| **10d** | The router can choose a 3D schematic. "3d" plus model "none" means a schematic lesson (catalogue still offered) instead of falling back to flat cards; the shell header says so | TurnRouter, `TurnLegs`, App Shell | TR-3/TR-6 updated and re-proved |

10a and 10b need no window or key. 10c needs the desktop (local only, like the other scene rows). The live result,
a real "draw the jet in 3D" turn, needs your keys.

## Execution path

- **Planning:** the planner prompt (with a catalogue) → the model's JSON, with `schematics` in the header →
  `VisualPlanParser` → `VisualPlanValidator`, which builds the plan's own catalogue (`ValidationContext.Scene3d` plus
  its schematic ids and parts) and runs the geometry delegate → plan.
- **Drawing:** `CardSurface` turns a `ModelOp` into a `LibraryModel`, from `ModelLibrary.Find` or the lesson's
  schematic → `SceneMessages.Model` → `scene.js` / `scene-svg.js`, unchanged.
- **Picking:** part checks use the same lookup.

## Alternatives considered

- **Hand-build more library models** (jet, Raspberry Pi, heart and so on). They're more accurate and reviewed, but
  there would never be enough. Kept as the complement: the eval's library misses list which models to build next.
- **Let the model send meshes or raw vertices.** Rejected: unbounded, impossible to check, and a token cost that would
  wreck the first-beat time.
- **Keep flat cards for unmodelled objects.** That's today, and it's the gap against the reference video.
- **Schematic animations** (spinning a fan, say). Deferred: animations are hand-checked physics in the library
  (PL-3). An invented animation is the riskiest kind of made-up content.

## What could break, and how we'd know

- **Ugly or wrong shapes.** The model may compose a poor jet. The tag and the spoken "simplified" are the honesty
  guard, and E-04's live run (PE-6) shows the quality. Not gated offline beyond the geometry budget.
- **Prompt growth slowing the first beat.** LP-12 prints the prompt's size; the live first-beat p50 (PE-6) measures
  the effect.
- **A schematic id shadowing a catalogue model.** Refused by the validator (10a row).
- **The planner prompt changes,** which changes the eval's prompt hash. There's no baseline yet, so nothing breaks;
  after one exists, re-record.
- **Existing 3D lessons changing.** All golden fixtures stay 1.1, and the LS, LP and LSU counts must not move except
  the new rows.

## Verification (10a, the first step)

- VisualLessonEngine, PartLibrary, LessonSmoke and PartLibrarySmoke build with `-warnaserror` 0/0.
- The new rows pass:
  - fixture valid;
  - every limit, the closed vocabularies, the catalogue collision, `animate` refused, and geometry failure degrading
    each fire alone;
  - a mutation per rule.
- LS-1..LS-13, PL-1..PL-4 and LP-1..LP-11 unchanged.
- Local CI 19 gates.
