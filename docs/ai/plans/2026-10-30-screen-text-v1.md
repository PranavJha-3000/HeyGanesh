# Plan: on-screen text as Element Selection candidates (build sequence step 4)

2026-10-30 · claude-opus-5-5 · STATUS: 4a BUILT 2026-10-30 (owner: "Yes, build 4a"): SC-9 17 PASS (fixture read exactly in
20 ms), SC-10 4 PASS (110 lines, OCR 141 ms on 2560×1600). Found while building: an XML comment in the smoke csproj
cannot contain "--", so the row name is spelled out there. 4b is planned below and not built.

## Spike (before planning, nothing changed in the repo)

OCR languages here: en-US, en-GB; max image 10,000 px. The current screen, counts only, images deleted:

| Image | Lines | Words | Time |
|---|---|---|---|
| Full size, 2560×1600 | 110 | 391 | 135 ms |
| Orchestrator frame size, 1280×800 | 68 | 329 | 111 ms |

## Step 4a: the capture can read screen text (built)

- `src/Foundation/HeyGanesh.Abstractions/SharedTypes.cs`: `ScreenTextLine(Text, Box)`; `MonitorCapture.TextLines`,
  `TextNote`, `TextDuration` (init-only).
- `src/Platform/HeyGanesh.ScreenCapture/ScreenTextReader.cs` (new): `Windows.Media.Ocr` on a `SoftwareBitmap`, boxes in
  global physical pixels; no language / too large / failure → a note.
- `ScreenCaptureService.cs`: `ScreenCaptureOptions.ReadText` (default off); the first capture of
  `CaptureAllMonitorsAsync` (or the monitor `CaptureMonitorAsync` was asked for) is read before JPEG encoding.
- `tools/ScreenCaptureSmoke`: `OcrRows.cs` (`--assert-ocr`, `--read-text`), `fixtures/ocr-words.png` (drawn by the
  scratchpad script `make_ocr_fixture.ps1`: Segoe UI 32 px, lines at y 20/120/220 from x 40).
- Docs: DEC-038, FLOW, ARCHITECTURE, AGENTS.md, TEST-CHECKLIST (SC-9, SC-10), CI.md, HANDOVER, ROLLBACK (first).

## Step 4b: Element Selection offers the text to Jev (planned, not built)

- `src/Perception/HeyGanesh.ElementSelection/TextCandidates.cs` (new, pure): keep OCR lines inside the target window
  and not on a UIA control; cap 40 lines of 80 characters; drop secret-looking lines (long key-like tokens, `sk-`,
  `ghp_`, `AKIA`); candidates of type "Text (OCR)" with the usual position hints.
- New `SelectAsync` overload taking the lines; the orchestrator passes `captures[0].TextLines`. A window with no UIA
  tree but readable text then gets a Jev choice instead of an immediate Vision fallback.
- App Shell: `ReadText` on, `--no-ocr`, a header line.
- ElementSelectionSmoke `--assert-text-merge` (pure, CI gate G-20).
- Privacy: OCR text joins the Jev request (TypeSafe); the caps, the window rule and the secret filter limit it.

## Alternatives rejected

OCR on the 1280-px JPEG (about 40% of lines lost); a second full-size capture in Element Selection; the full-size bitmap
across the shared types; reading every monitor.

## Verification (4a)

Abstractions, ScreenCapture, ScreenCaptureSmoke build `-warnaserror` 0/0; SC-9; SC-10; the default capture run
(SC-1..SC-7) unchanged; local CI 15 gates.
