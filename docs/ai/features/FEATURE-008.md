# FEATURE-008 — Course from a textbook: prepared lessons, not only real-time ones

- **Date:** 2026-10-30 · claude-opus-5-5
- **Status:** deferred, kept for the future (2026-10-30 · claude-opus-5-5, owner: "for feature 008 keep it for future").
  Scope recorded on the owner's word ("we will use make a course from my textbook also as a feature because HeyGanesh
  should not be just used realtime only"). Nothing is designed or built. Step 11 is finished; when the owner picks it
  up, it gets its own plan and approval before any code, starting with the open questions below.
- **Plan record:** [plans/2026-10-30-papermorph-review.md](../plans/2026-10-30-papermorph-review.md) (why this feature
  exists, and what was taken from Papermorph and what was not).

## Scope (proposed)

A student gives Hey Ganesh a textbook or lecture-notes PDF they own. Hey Ganesh prepares a course from it ahead of time:
- the chapters, as a list of lessons;
- each lesson as a VisualPlan (the same contract, cards, 3D scene and voice as live lessons);
- a few practice checks per lesson.

The student then plays the course from the tray, at their own pace. Live Ctrl+Alt questions still work on top, and a
question asked during a prepared lesson can use that lesson as context.

## Why

Real-time lessons answer one question at a time and are gone after. A prepared course gives structure (an order and
progress), costs nothing extra when replayed, and can be reviewed before the student sees it, which matters because
live lessons cannot be.

## What it reuses

- **VisualPlan 1.2:** validation, degradation, schematics. A prepared lesson is just a stored, already-validated plan.
- **The VisualPlanner,** run in a batch mode with a lesson outline instead of a screen question.
- **LessonPlayer, the card surface and the scene window** for playback; Voice for narration.
- **PlannerEval-style automatic checks (K-1..K-12),** run on every prepared lesson before it is kept.
- **KeyRing** for the model key; the tray for the "Courses" entry point.

## New pieces (rough, to be planned)

1. **Reading the PDF:** text and headings per page, then chapters. This needs a PDF text reader. .NET has none built in,
   so it is a dependency question for the owner (or Windows.Data.Pdf rendering plus the OCR we already use, which needs
   no new package).
   **Candidate for scanned or complex PDFs: [baidu/Unlimited-OCR](https://github.com/baidu/Unlimited-OCR) 2** (MIT, 2026-10):
   whole-document parsing that handles maths and keeps context across pages. It is a Python model that needs a large
   NVIDIA GPU (tested on an A800-80GB), so it could never run on a student's laptop. It would be used only as an external
   service behind a seam, the way the Voice Gateway is: its OpenAI-compatible server, or Baidu Cloud's hosted API.
   - Upload is fine here (a course is prepared, not live), but the textbook leaves the machine: the owner's call.
   - A key would go through KeyRing.
   - Decide by testing it against the built-in OCR on one real chapter.
2. **An outline step:** chapters → lessons → the learning goal of each, reviewed by the student before generating.
3. **Batch planning:** each lesson planned and checked, failures re-planned or marked, with a cost estimate shown first.
4. **A course store:** under `%LOCALAPPDATA%/HeyGanesh/courses/<id>/`, plain JSON plans and progress (the planned Notes
   Persistence may hold it later).
5. **Practice checks that grade the answer, not the string:** maths answers compared by value or expression, computed in
   code (an idea from Papermorph).
6. **A course player:** a lesson list, next/back, progress saved; from the tray menu.

## Open questions for the owner

- Which PDFs first: maths/physics textbooks, lecture slides, or anything?
- Copyright: courses stay on the student's own machine and are never shared. Is that the rule?
- Is a cost cap per course wanted (prepared courses use many model calls)?
- One subject first, with a hand-checked example course, as the Part Library did?

## Log

- 2026-10-30 · claude-opus-5-5: recorded from the Papermorph review. Not started.
