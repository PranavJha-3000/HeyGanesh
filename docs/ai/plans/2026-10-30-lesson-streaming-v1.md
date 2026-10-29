# Plan: the first lesson beat starts before the whole lesson is written (build sequence step 6)

2026-10-30 · claude-opus-5-5 · STATUS: 6a BUILT 2026-10-30 (owner: "Yes, build 6a"): LP-11 12 PASS (simulated: first beat
2.41 s, whole lesson 6.50 s); LP-1..LP-10 unchanged. Changed from the plan while building: the prompt needed no "header
first" sentence (its FORMAT block already lists `beats` last, and the 2D prompt stays character for character); LP-1's
"Stream" needle was narrowed to the I/O type names. 6b is planned below and not built.

## Problem, restated

`VisualPlanner.PlanAsync` waits for the model's entire VisualPlan, validates it (and repairs once if needed), and only then
does `LessonTurn` open the cards and play. On DeepSeek V4.1 Flash's published figures (about 1.1 s to the first token,
206 tokens/s) a 600–1,200-token lesson is roughly 4–7 s before anything is drawn; the target is 2.5 s.

## Step 6a: the planner streams and releases validated beats (built)

- `src/Intelligence/HeyGanesh.LessonPlanner/PlanStreamScanner.cs` (new, pure): header + each complete beat from chunks.
- `VisualPlanner.Streaming.cs` (new): `PlanStreamingAsync`; `VisualPlanner.cs`: `Prepare` + `RunAttemptsAsync` shared
  with `PlanAsync`; `PlannerTypes.cs`: `StreamedBeat`, three optional `PlanResult` fields.
- `tools/LessonPlannerSmoke`: the fake client streams (chunk size, simulated delays, a drop point); LP-11.
- CI G-14 gains `--assert-stream`; docs DEC-041, FLOW, ARCHITECTURE, AGENTS.md, TEST-CHECKLIST, CI.md, HANDOVER, ROLLBACK.

## Step 6b: cards open and play as beats arrive (planned, not built)

- `LessonPlayer`: a play method fed released beats (each still drawn, then spoken).
- `LessonTurn`: open the cards on the first released beat; a mid-stream stop ends the lesson after the shown beats with
  outcome `Stopped` and the reason.
- App Shell: the DeepSeek client advertises `Text | Streaming`; the lesson leg calls the streaming path; `first beat :`
  is printed beside the plan line.
- `LessonTurnSmoke`: fake streaming model + fake canvas: the first beat is drawn before the stream ends; a mid-stream
  failure stops cleanly; LT-6/LT-7 re-run.

## Alternatives rejected

Playing beats unvalidated; a short first call then the rest; a streaming JSON library (NuGet); fixing
`ResilientLlmClient` first (not on the shell's path; open item).

## Verification (6a)

VisualLessonEngine, LessonPlanner, LessonPlannerSmoke build `-warnaserror` 0/0; LP-11; a scanner mutation shown to fail
it; LP-1..LP-10 unchanged; local CI 17 gates. Not verifiable offline: the real time to the first beat (needs
`DEEPSEEK_API_KEY`, after 6b).
