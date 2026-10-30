# Plan: Jev turn router (build sequence step 1)

2026-10-30 · claude-opus-5-5 · STATUS: BUILT 2026-10-30 (owner: "Yes, build step 1"): TR-1 3, TR-2 14, TR-3 13, TR-4 7,
TR-5 5 PASS; a gate mutation (`<` to `<=`) and a guard mutation (unoffered model accepted) each failed their row and were
reverted. TR-L1 is live-gated.

## The build sequence this belongs to (all offline, no keys)

1. **Jev turn router** (this plan).
2. The App Shell uses the router when `TYPESAFE_API_KEY` is set; `--lesson` still forces a lesson; without the key, nothing changes.
3. Confidence gate: a Jev element choice below 0.5 falls back to Vision (DEC-008's open item, 0.8/0.5 tiers kept).
4. OCR candidates: Element Selection adds `Windows.Media.Ocr` text boxes to what Jev chooses from.
5. Warm voice connection: the Gateway session stays open between presses, with reconnect on drop.
6. Stream the first lesson beat, aiming at the 2.5 s target.
7. Planner evaluation set: 30 recorded questions with automatic checks.
8. Security proxy, first cut: keys behind one local seam.

Sources: typesafe-computer-use (one request answers several Choices; the classifier picks, code decides facts; UIA plus
Windows.Media.Ocr on Windows) and qwen-audio-agent (upstream Gateway Client Protocol is still 7.0.0, matching our client;
our client connects once per press).

## Problem, restated

A lesson is chosen today only by the `--lesson` command-line flag, and the planner always sees the whole 3D catalogue.
One cheap Jev call can decide, from the question itself, whether to point, explain or draw, whether a lesson should be 2D
or 3D, and which catalogue model fits.

## Files

- New `src/Intelligence/HeyGanesh.TurnRouter/`: `TurnRouter.cs` (request building and mapping), `TurnRoute.cs` (the
  records and options), the csproj (one ProjectReference: Element Selection, for `IJevClient`).
- New `tools/TurnRouterSmoke/`: host, recording fake `IJevClient`, rows TR-1..TR-5.
- `HeyGanesh.sln` gains the library; `.github/workflows/ci.yml` gains G-18.
- Docs: DEC-035, ARCHITECTURE, FLOW (new, not-yet-wired section), AGENTS.md, TEST-CHECKLIST, CI.md, HANDOVER,
  ROLLBACK (written first: more than three files).

## Execution path affected

None yet. The router is not called by the shell until step 2. FLOW.md documents it as its own path.

## Alternative rejected

The router inside the Context Orchestrator: it would tie teaching choices to the module that locates things on screen,
against DEC-010's narrow job for it. Three separate Jev calls: three round trips for what one request answers.

## What could break, and how we'd know

- TypeSafe may limit questions per request. Element Selection sends two and typesafe-computer-use three; TR-L1 checks it
  once a key exists.
- The CS2012 race if library and host build in parallel: they are built one after the other.

## Verification

- `dotnet build src/Intelligence/HeyGanesh.TurnRouter -c Debug -warnaserror`, then `dotnet build tools/TurnRouterSmoke -c Debug -warnaserror`: 0/0.
- `HeyGanesh.Tools.TurnRouterSmoke.exe --assert-source|--assert-request|--assert-map|--assert-gate|--assert-errors`: all PASS, exit 0.
- The local CI runner: 14 gates, all exit 0.
