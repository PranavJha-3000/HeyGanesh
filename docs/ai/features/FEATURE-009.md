# FEATURE-009 — An Apple-grade look: one visual language for every Hey Ganesh surface

- **Date:** 2026-10-31 · claude-opus-5-5
- **Status:** steps 1 and 2 done (owner: "Approve, plan step 2", then "Yes, implement step 2"); 2b next.
- **Decision:** [DEC-062](../DECISIONS.md#dec-062--2026-10-31--claude-opus-5-5).
- **Reference mockup:** a throwaway page built in the session scratchpad (not in the repo). It shows the ask flow,
  the lesson card, the 3D window, the key screen, and the theme / motion / transparency switches. The tokens below are
  copied from it exactly; if the two ever disagree, this file wins.

## Why

The owner asked for a UI that matches or beats duckyhelper.com and look-at-it.in and "feels like a product made by
Apple". Today each surface has its own look: cream graph paper and a beige tab (cards, 3D window), a hard-edged GDI
outline (overlay), default grey WinForms (tray dialogs).

## What was taken, ranked

1. **It grows from where you asked.** A window opens from the thing that caused it and closes back into it
   (DuckyHelper's orb → pill → board; Look At It's answer in place).
2. **Restrained palette:** near-white or near-black, one system blue; red only for ink marks (DuckyHelper).
3. **Glass frames** over the desktop: Windows 11 Acrylic/Mica where it works, a solid tint where it does not (Look At It).
4. **One shape language:** pills for buttons, chips and title controls; 14 px card corners; a soft glow on the one
   primary action (both).
5. **System font for chrome, hand for ink.** Segoe UI Variable frames the windows; the tutor still writes in Ink Free.
6. **Words light up as spoken** (DuckyHelper). Needs word timing from the speaker; its own step.
7. **Physical motion:** critically damped springs, interruptible, momentum on a throw, rubber-banding at edges.

Not taken: marketing-page scroll sections, the italic serif headline accent, emoji flags, low-contrast hero text,
either product's assets or names.

## Tokens (approved)

| Token | Light | Dark |
|---|---|---|
| accent | `#007aff` | `#0a84ff` |
| accent-soft / accent-glow | `rgba(0,122,255,.14)` / `.38` | `rgba(10,132,255,.20)` / `.50` |
| red / green / amber | `#ff3b30` / `#34c759` / `#ff9f0a` | `#ff453a` / `#30d158` / `#ffd60a` |
| text / text-2 / text-3 | `#1d1d1f` / `rgba(60,60,67,.62)` / `.34` | `#f5f5f7` / `rgba(235,235,245,.62)` / `.30` |
| fill-1 / fill-2 | `rgba(120,120,128,.12)` / `.18` | `rgba(120,120,128,.20)` / `.30` |
| hairline / glass-edge | `rgba(0,0,0,.08)` / `rgba(255,255,255,.7)` | `rgba(255,255,255,.09)` / `.14` |
| glass / glass-solid | `rgba(248,248,250,.72)` / `#f5f5f7` | `rgba(30,30,34,.64)` / `#1c1c1f` |
| board / board-dot | `#fdfcf9` / `rgba(0,0,0,.075)` (18 px dot grid) | `#121315` / `rgba(255,255,255,.065)` |
| ink / ink-2 / marker | `#1d1d1f` / `rgba(60,60,67,.82)` / `rgba(255,204,0,.45)` | `#f2f2ee` / `rgba(235,235,245,.6)` / `rgba(255,214,10,.28)` |

- **Type:** chrome `"Segoe UI Variable Text"`, display `"Segoe UI Variable Display"`, ink `"Ink Free"` (unchanged),
  math `"Cambria Math"` (unchanged). Title 13 px / 600 / -0.005em; heading ink 28 px / 1.1; body ink 19 px / 1.3.
- **Shape:** card 14 px, inner board 10 px inset 8 px, pills 999 px, icon buttons 28 px circles.
- **Depth:** large surfaces `0 30px 70px` shadow + 1 px bright top edge; small chips a lighter shadow.
- **Motion:** springs as damping ratio + response. Default ζ 1.0; reposition r 0.42 s; close r 0.34 s; fades r 0.24 s;
  ζ 0.82 only after a throw faster than 300 px/s. Momentum projection with deceleration 0.99. Press feedback
  `scale(.96)` in 100 ms on pointer-down.
- **Accessibility:** follows the Windows light/dark setting; reduced motion → opacity only, no springs or drawing
  animation; reduced transparency or high contrast → `glass-solid`, no blur.

## Sequence (one logical change per step)

| Step | What | Status |
|---|---|---|
| 1 | Mockup + approved tokens (this file, DEC-062) | ✅ done 2026-10-31 |
| 2 | Lesson card: page restyle to the tokens + rounded window corners, solid surfaces | ✅ done 2026-10-31 |
| 2b | Spike: can Acrylic blur stay on a card that never becomes active? Keep it if yes, stay solid if no | 📝 planned |
| 3 | 3D scene window restyle (`scene.css`, `scene-svg.css`) | ⏳ |
| 4 | Screen overlay: soft rounded ring with glow (per-pixel alpha instead of colour key) — ROLLBACK first | ⏳ |
| 5 | Tray dialogs: key screen, Recent lessons | ⏳ |
| 6 | Optional features: listening orb, cards growing from the ring, word-by-word captions | ⏳ own plans |

## Log

- 2026-10-31 · claude-opus-5-5 — Both sites analysed (scraped branding + screenshots). Mockup built and checked in
  the in-app browser: dark and light ask flow, card, scene popover (real click), key screen. Fixed during review:
  third step clipped (board taller, plot shorter), faint light-mode labels (`ink-2` .60 → .82). Not checked:
  mouse drag/throw, reduced-motion mode, a real Ctrl+Alt hold.
- 2026-10-31 · claude-opus-5-5 — Step 2. `lesson.css` rewritten to the tokens (light default, dark under
  `prefers-color-scheme`); every class `lesson.js` sets is kept, `lesson.js` is untouched. `lesson.html` gains the mark
  and an SVG close glyph; ids and CSP unchanged. `CardWindow` gives the lesson page (not the scene page) DWM rounded
  corners and a background equal to the frame colour for the Windows app theme. `CardSnapshot.CornerPreference` reads
  the corners back; LSU-2 9 → 10. Found in review: the close button showed Chromium's focus ring (also before this
  step); removed, since a card never takes keyboard focus. Title bar is 38 px, not the 40 planned, to keep room.
  Open: the dark-mode marker (`rgba(255,214,10,.28)`) reads olive on the board; a token change for the owner to decide.
  Not checked: light mode on the real window (Windows is in dark mode here; changing it is the owner's), the rounded
  corners by eye (snapshots render the page, not the window).
