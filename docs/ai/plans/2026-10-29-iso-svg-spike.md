# Plan: spike 0d, Part Library models as isometric SVG with Anatomy's kit

2026-10-29 · claude-opus-5-5 · STATUS: RUN 2026-10-29 (owner: "Yes, run the spike"): **GO with two conditions**, results in
FEATURE-007 "Spike 0d results". Renamed from 0c (already the planner spike's id). Deviation: the axis mapping is
(x, y, z) → (x, z, y); the (x, −z, y) below would have mirrored every model.

## Problem, restated

When WebGL is missing, a 3D lesson today shows a notice and continues in speech only (DEC-030, LSU-12), so the student
sees no picture at all. Anatomy (github.com/wheresryan22/anatomy, MIT, commit `1482e846`) ships a small drawing kit
with no dependencies. It draws shaded isometric solids as plain SVG, the clean textbook style of a technical
illustration. The question for this spike: **can that kit draw our existing Part Library models from the data we
already compute, fast enough, and so they read correctly?** If yes, a later step can use it as the no-WebGL fallback
(and for printable lesson notes). If no, we have lost a few hours and nothing in the product.

## What I've already learned (reading only)

I read `kit/iso-kit.mjs`, `kit/lathe.mjs`, `references/kit.md` and `LICENSE` through the GitHub API.
Copies of those four files are in this session's scratchpad (`anatomy-read/`), saved there only to be read: not run,
not in the repo. I can delete them if you'd rather.

- **The camera** is a fixed isometric one (azimuth 45°, elevation 30°), with z up. Shading uses four tones from a
  fixed light, and parts are painted back to front by depth. The output is SVG path strings, with CSS classes for
  the tones (`iso.css`, light and dark themes).
- **Parts on any axis** come from `lathe.mjs`. Our five shapes map like this:

  | Part Library shape | Kit call | Confidence |
  |---|---|---|
  | Box | `prismOf(frame, rectangle, s0, s1)` | good: a convex polygon extruded along any axis |
  | Cylinder (top/bottom diameters) | `solidOf([[−h/2, rBottom], [h/2, rTop]], frame)` | good: a turned profile, so cones work too |
  | Sphere | `sphereOf(centre, r)` | good: documented for exactly this |
  | Capsule | `solidOf` with rounded ends (smooth vertices) | likely |
  | Torus | no direct call. The turned profile would be a circle, which may not be allowed (`profileCurve` throws when `s` doesn't rise) | **unknown: the spike's main question.** Fallback: a ring of short discs, or `ringBand` |

- **Frames:** each primitive's position and rotation already exist as a quaternion (`SceneMessages`, DEC-030).
  Its axis and two radial directions come straight from that rotation. Our frame is Y-up, the kit's is z-up, so the
  mapping is (x, y, z) → (x, −z, y). That's a proper rotation, so handedness is kept.
- **Safety:** the two kit files import only each other, and use no `fetch`, `eval`, `new Function`, `innerHTML`,
  `document` or `window` (grep). They are pure functions returning strings, so they fit the page's CSP
  (`script-src 'self'`) once vendored.
- **Known gaps:**
  - Sorting by depth per part can mis-order parts that pass through each other. The rod sits inside the cylinder.
    Anatomy avoids this with its own geometry audit, which we would not have.
  - Glass and membrane transparency isn't in the kit's palette.
  - The 3D runtime (`turn*.mjs`, beta, 140 KB+) is out of scope.

## The spike (all in the scratchpad; nothing in `src/` changes)

1. **Download (needs your approval), from `raw.githubusercontent.com/wheresryan22/anatomy/1482e846…/` into a
   new, empty scratch folder `spike-iso/vendor/`:**
   - `kit/iso-kit.mjs`, 35,175 bytes
   - `kit/lathe.mjs`, 48,028 bytes
   - `kit/iso.css`, 7,698 bytes
   - `LICENSE`, 1,061 bytes

   That's 92 KB in total, MIT, pinned to that commit. I'll record each file's SHA-256 and compare the downloads
   with the copies I read.
2. **Export the geometry.** A throwaway console project in the scratchpad references LessonSurface and writes
   `SceneMessages.Model(...)` for each V1 model to JSON. It's the exact message the Babylon page receives, so both
   renderers draw the same numbers.
3. **Adapter page** `spike-iso/index.html` + `iso-scene.js`. It has the same CSP as `scene.html` and does this:
   - takes each primitive, maps its quaternion to a kit frame, and builds the solid with the calls above;
   - sorts by depth and wraps the result in `figureSvg`;
   - colours each part from our palette by finish, using CSS variables over the kit's tone classes.
4. **View it:** serve the folder with a local static server, open it in the in-app browser pane, and screenshot
   each model.
5. **Measure:**
   - time to build the SVG per model, and its size;
   - JS errors and CSP violations;
   - whether the torus works.
6. **Compare** each model side by side with its Babylon snapshot from `LessonSurfaceSmoke --snapshot-3d`. Same
   pose, parts in the right places, rod tilted the right way.

## GO / NO-GO

GO if all of these hold:
- the engine, the DNA and the pump are each recognisable and match their Babylon snapshots in pose;
- each SVG builds in under 100 ms in the page;
- 0 JS errors and 0 CSP violations under the scene page's CSP;
- every shape renders, the torus included (through the fallback if needed).

A depth glitch where parts pass through each other doesn't block GO; it gets recorded, with how often it shows.

## What could go wrong / how we'd know

- **The torus can't be built.** We'd see it in step 3 and use the fallback, recorded as a deviation.
- **The models look wrong:** depth errors, the glass hiding everything, a mistake in the axis mapping. The step 6
  side-by-side shows it. I'll review the PNGs and send them to you.
- **It's slower than expected** with about 50 primitives, given the smooth shading. Step 5 measures it.

## Alternatives considered

- **Port the kit to C#** and draw the SVG on the host side. Rejected for a spike: about 80 KB of geometry code to
  port before learning anything.
- **Use Babylon's software renderer for no-WebGL.** Rejected: spike 0b measured WARP and SwiftShader at 16–39 fps,
  and they still need a WebGL context, which is the very thing that's missing.
- **Take Anatomy as the foundation** (the model writes figure code). Rejected earlier: it runs model-written code
  and takes minutes per figure.

## Afterwards (only if GO, each its own plan)

- Vendor the kit into `Page/vendor/anatomy-1482e846/`, pinned in `VENDOR.md` with its LICENSE, and covered by the
  source audit.
- Draw the scene as SVG when WebGL is missing (LSU-12 changes from "speech only" to "speech plus a picture").
- Optional: SVG snapshots of each lesson for printable notes.

## Verification of the spike itself

- The PNGs, the timings and the CSP and error counts, recorded as "spike 0c" in FEATURE-007.
- Every existing row stays untouched, since no repo code changes.
- `git status` shows only the FEATURE-007 results and this plan.
