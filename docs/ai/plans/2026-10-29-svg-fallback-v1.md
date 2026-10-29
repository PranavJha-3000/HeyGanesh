# Plan: a drawing instead of silence when WebGL is missing (FEATURE-007, after spike 0d)

2026-10-29 · claude-opus-5-5 · STATUS: BUILT 2026-10-29 (owner: "Yes, build it"): LSU-12 8 PASS, LSU-16 8 PASS, LSU-7 20 PASS, every other row unchanged.

As built — differences from the plan below:
- `bandsOf` was not used: one outline over a whole body would paint a stem's buried part over the head. Long cylinders are cut into
  pieces instead (open bands inside, caps only at the true ends). **Long boxes are cut too** (the plan said boxes stay whole):
  the connecting rod was drawn over the piston until they were, and box pieces draw their own outline without the cut edges.
- A second rule was needed, which the plan didn't foresee: a piece whose centre lies inside another solid is painted just before
  that solid's settled order (containers nest), with ties broken by depth. Without it the piston pin showed through the piston.
- The drawing pane needed `#view[hidden]{display:none}`: `scene.css`'s `#view{display:block}` overrode `hidden`, which pushed
  the drawing (and the orientation markers) down the window.
- LSU-16 has 8 rows, not about 9. The module-MIME check was done in place (the page's `ready` waits for the module), not as a
  separate test.

## Problem, restated

Without WebGL, the scene page shows "3D view isn't available on this PC — the lesson continues in speech", acks every
scene op as `skipped`, and the student sees nothing (DEC-030, LSU-12). Spike 0d showed that Anatomy's MIT kit draws
the same Part Library geometry as an isometric SVG in 10–16 ms, under the page's CSP, posed to match Babylon. It
also left two conditions. This step ships that drawing as the no-WebGL path, and meets both conditions.

When WebGL works, nothing changes: Babylon stays the 3D path. No C# message changes either: the page already
receives the full `model` geometry, and it simply stops discarding it when WebGL is missing.

## Decisions proposed (owner to confirm)

1. **Scope of the SVG path in v1.** It covers `model`, `label`, `highlight`, `explode` (redrawn at the new amount),
   `clear`, and `camera` focus (re-framed on the focused part). Two ops are shown as a still:
   - `animate` draws the rest pose. A small "animation needs 3D" tag appears instead of motion.
   - `camera` views: every view is drawn isometric. Front, side and top weren't tested in the spike, and the kit's
     shading is tuned for its fixed camera.
2. **No click-to-ask on the drawing in v1.** `PickAsync` keeps returning null without WebGL. SVG hit-testing is
   easy, but it's another surface to verify; it's a follow-up.
3. **Painter's order** (condition 1): long round parts are cut into depth-sorted bands with the kit's own
   `bandsOf`, which exists for exactly this. That covers cylinders, cones and capsules whose length is more than
   2.5× their diameter. Boxes stay whole. Most of our boxes are slabs, and the defect the spike found was a
   cylinder (the valve stem).
4. **Module loading** (condition 2): the first build task is a 10-minute check that WebView2's virtual host
   serves `.mjs` as JavaScript. If it doesn't, the kit is vendored as `.js`. File contents are unchanged and the
   hashes are pinned either way; ES modules don't care about the extension.

## Files and changes

| File | Change |
|---|---|
| `Page/vendor/anatomy-1482e846/iso-kit.mjs`, `lathe.mjs`, `iso.css`, `LICENSE` | Vendored byte-for-byte: the spike's verified downloads, SHA-256 already recorded in FEATURE-007. No new download. |
| `Page/vendor/VENDOR.md` | Four new pinned rows, plus an MIT note. |
| `Page/scene-svg.js` (new, ES module) | The renderer, hardened from the spike's adapter. It turns the page's instance state into an SVG, with the corrected axis map (x, y, z) → (x, z, y), shapes mapped as in spike 0d, the torus as segments and long parts as bands. Exposes `window.SceneSvg = { draw(state), describe() }` for `scene.js`. **Label text never enters an SVG string:** labels are built with `createElementNS` + `textContent` (the page's no-markup rule). The kit's SVG strings carry only numbers and our fixed class names, and are parsed with `DOMParser` as `image/svg+xml`. |
| `Page/scene-svg.css` (new) | Per-finish tone ramps (generated once by a script, committed as plain CSS, since the CSP blocks inline styles), plus highlight and label styles. |
| `Page/scene.html` | A `#drawing` container, the module script tag and the two stylesheets. CSP unchanged. |
| `Page/scene.js` | No-WebGL path: instead of acking `skipped`, keep a small instance state (models, parts, explode amounts, labels, highlights, focus), redraw through `SceneSvg` per beat, and ack `{ drawing: true }`. The notice becomes "3D view isn't available on this PC — showing a drawing instead." `orient` is drawn in SVG too, so the pixel orientation probe also covers this path. `picking` stays `skipped`. `__describe` gains `drawing: { solids, buildMs }`. The page posts `ready` only once the module has loaded. |
| `CardSurface.cs` | `CheckOrientationAsync` runs on the drawing too: it accepts `{ drawing: true }` and checks pixels the same way. `DescribeSceneAsync` reads the drawing fields into the snapshot. |
| `CardSurfaceOptions.cs` | `SceneSnapshot` gains `DrawingSolids` and `DrawingBuildMs`. |
| `tools/LessonSurfaceSmoke` | **LSU-12** (no WebGL) now expects a drawing: the engine is drawn with solids above 0, the notice says "showing a drawing", the first build is under 100 ms, 0 CSP violations, every beat acked, 6 sentences spoken, and a pick still returns null. **New LSU-16 `--assert-scene-svg`** (desktop, `--disable-webgl` profile): (a) orientation from real pixels, +X right, +Y up, Z-turn → −X; (b) **depth order**: in the engine's drawing the intake-valve stem's top band is painted after the cylinder head, the spike's defect, now pinned; (c) a label's text equals the plan's, with no markup interpreted (a `<b>` label stays literal, like LSU-4); (d) highlight and explode change the drawing; (e) re-drawing all three models stays under 100 ms each. `--snapshot-3d` with `--no-webgl` writes the drawing PNGs (the manual LSU-13 row gains them). The source audit (LSU-7) gains 4 more pins, the MIT licence shipped, and no HTML or code sinks in `scene-svg.js`. |
| Docs | DEC-034; FLOW (the no-WebGL path); ARCHITECTURE; TEST-CHECKLIST (LSU-7, LSU-12, LSU-16, LSU-13 note); FEATURE-007; HANDOVER; AGENTS; ROLLBACK first (more than 3 files). |

## Execution path

From FLOW, today: `CardSurface.ApplyBeatAsync` → one `beat` message → `scene.js`; without WebGL it acks
`skipped`. New: the same message. `scene.js` updates its instance state → `SceneSvg.draw(state)` → depth-sorted
solids, each primitive's world transform computed from its part rotation, then its own rotation → SVG in
`#drawing`, labels laid over it → ack `{ drawing: true, buildMs }`. The C# routing is unchanged.

## Alternatives considered

- **Draw the SVG in C#** by porting the kit. Rejected: about 80 KB of geometry code to port and keep in step with
  upstream, for no gain over vendoring it.
- **Use the SVG drawing for every lesson, and drop Babylon.** Rejected: no real 3D motion, no orbit, and the 3D
  mode is beta upstream. The SVG path is the fallback, and maybe later a style choice.
- **Fix the painter's order with a full separating-plane sort** (a Newell-style sort). Rejected for v1:
  interpenetrating parts have no correct whole-part order anyway, and band-splitting with the kit's own `bandsOf`
  is the documented fix.
- **A static PNG per model, made at build time.** Rejected: it can't show this lesson's labels, highlights or
  explode, and it adds binary assets.

## What could break, and how we'd know

- **Babylon path regressions.** The WebGL branch of `scene.js` isn't touched. LSU-9, LSU-10, LSU-11, LSU-15 and
  LSU-7 rerun, and so do the 2D card rows.
- **Module or MIME failure in WebView2.** The page would never post `ready`, and every scene row would time out
  loudly. Checked first (decision 4).
- **Mirroring or a wrong pose** in the drawing: LSU-16(a) checks real pixels.
- **Depth glitches beyond the valve stem**: LSU-16(b) pins the known one. The PNGs of all three models get reviewed
  by me and then by you (LSU-13). Others may exist, for example box-versus-box overlaps; they'd be listed, not hidden.
- **Label injection**: LSU-16(c) plus the source audit.
- **Page weight**: about 92 KB more scripts and CSS, parsed only by the scene window, and in parallel with Babylon.
  The scene window's cold start (about 2 s) gets re-measured; I'd expect under +50 ms.

## Verification

- `--assert-scene-nowebgl` (LSU-12): about 8 PASS. `--assert-scene-svg` (LSU-16): about 9 PASS.
  `--assert-source`: about 17 PASS.
- Unchanged and rerun: `--assert-scene` 19, `--assert-scene-idle` 4, `--assert-orientation` 3, `--assert-pick` 9,
  `--assert-render` 17, `--assert-window` 9, `--assert-security` 4, `--assert-marks` 2, LT and LS rows, and the
  local CI runner (13 gates; G-17 picks up the extra audit rows).
- Manual: `--snapshot-3d <dir> --no-webgl`, the drawing PNGs reviewed and sent to you.

## Not in this step

- Click-to-ask on the drawing.
- Front, side and top views in the drawing.
- Animation in the drawing.
- Using the drawing for printable notes.
- Pushing to PR #1 (it would join that PR; your call).
