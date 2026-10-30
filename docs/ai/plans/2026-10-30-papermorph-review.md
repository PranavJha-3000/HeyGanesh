# Plan record: what to take from Papermorph

2026-10-30 · claude-opus-5-5 · STATUS: RECORDED, nothing built. A reference review, plus one new feature (FEATURE-008).

## What was reviewed

[DozenTwelve/Papermorph](https://github.com/DozenTwelve/Papermorph) (MIT): a Claude Code / OpenCode skill that turns
textbook PDFs and stories into whole books:
- narrated, interactive web lessons, picture-book films and documentaries;
- graded quick checks, progress saving, MP4 export and SCORM packages for school course systems.

It runs offline as an authoring pipeline (Python scripts, ffmpeg, Playwright, Edge/Kokoro voices, subagent teams). It
plans a book first: chapters, then a script, then a storyboard. It then reviews its own output and fixes it. Its README
puts one illustrated physics chapter at about $10–18 of model usage. Only the README was read; its code and skill files
were not.

## Decision

**No code is integrated.**
- **Different job:** Hey Ganesh plans one lesson live, in seconds, from a question and the screen. Papermorph makes
  whole books ahead of time, and it has no live path to call.
- **Different stack:** adding Python, ffmpeg and Playwright to the .NET app would break the no-new-dependency rule.
- **Duplication:** its drawing and player duplicate VisualPlan, the cards, the 3D scene and Voice.

**Ideas are taken, as follow-ups:**

| # | Idea | Where it would land | When |
|---|---|---|---|
| P-1 | **Course from a textbook:** prepared lessons, played later | FEATURE-008 (owner: a feature, "HeyGanesh should not be just used realtime only") | after step 11 |
| P-2 | **Grade the answer, not the string:** practice checks compared by value or expression in code | FEATURE-008 practice checks; later, live lessons | with FEATURE-008 |
| P-3 | **Word-timed narration:** drawing follows each spoken word, not only each beat | LessonPlayer + Voice; needs word timings from Fish Audio (unknown whether it returns them) | open item |
| P-4 | **The model checks its own output before showing it** (contact sheets, overlap and blank-frame checks) | PlannerEval checks run on prepared lessons first (cheap there); for live lessons only if fast enough | with FEATURE-008 |
| P-5 | **Plan before drawing:** outline → script → storyboard | FEATURE-008's outline step, which the student reviews | with FEATURE-008 |

## Alternatives considered

- **Use Papermorph itself to make courses, and play its web pages in Hey Ganesh.**
  - Rejected: its books are web pages with their own player, quizzes and style. They would not be VisualPlans, so the
    3D scene, click-to-ask, live questions in context and our checks would not apply to them.
  - It also brings the Python/ffmpeg/Playwright toolchain and its per-chapter cost.
- **Ignore it entirely.** Rejected: P-1 is a real product gap the owner wants filled, and P-2/P-4 are cheap to adopt
  there.

## Related review: baidu/Unlimited-OCR (2026-10-30)

- **Live screen OCR:** not adopted. Windows' built-in OCR (DEC-038) runs locally in milliseconds, with no GPU and no
  upload. Unlimited-OCR needs Python, a large NVIDIA GPU and seconds per page, and would send the screen away.
- **FEATURE-008's PDF step:** recorded as a candidate, used as an external service only. See FEATURE-008, piece 1.

## Next

Nothing now. Step 11 is finished, and the owner has deferred FEATURE-008 to the future (2026-10-30). When it is picked
up, it gets its own plan, built in steps, each approved on its own, starting with its open questions.
