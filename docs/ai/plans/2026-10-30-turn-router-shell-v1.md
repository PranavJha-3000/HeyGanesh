# Plan: the App Shell uses the Turn Router (build sequence step 2)

2026-10-30 · claude-opus-5-5 · STATUS: BUILT 2026-10-30 (owner: "Yes, build step 2", `--no-route` kept): TR-6 10 PASS,
TR-1..TR-5 unchanged; SH-21 verified (help, the three header states, exits 12/12/12/13/13, hooked header with a dummy
key, dummy printed 0 times). Found while building: the lesson header still said "off" when a router could choose a
lesson, so it now says "when the route chooses one". SH-22 and TR-L1 are live-gated.

## Problem, restated

The router from step 1 exists but nothing calls it. Today a turn always goes to the Tutor, or to a lesson with `--lesson`.
After this step, with Jev available, the shell asks the router whether to point, explain or teach, and a lesson gets 2D or
the one model Jev picked. `--lesson` keeps exactly its old behaviour.

`TypeSafeJevClient` is internal to Element Selection and built inside its constructor, so the shell had no client to give
the router. Element Selection gains one public factory, `JevClients.CreateTypeSafe`; the shell builds one client and
hands the same instance to both.

## What the shell runs

| Situation | Leg |
|---|---|
| `--lesson` | Lesson on the full catalogue; router not called |
| `--no-route` | Tutor; router not called |
| No router, router failed, or "no route" | Tutor, printed with the reason |
| point | Highlight only, exit 0 |
| explain | Tutor (still clientless until SH-17) |
| lesson, 2D | Lesson, no 3D offered |
| lesson, 3D on model X | Lesson offered only X |

The route starts right after the verdict is printed and runs while the highlight is shown; teaching waits for it. Router
faults print `route : FAILED` and fall back; cancellation while routing exits 13.

## Files

- `src/Perception/HeyGanesh.ElementSelection/JevClients.cs` (new): `JevClients.CreateTypeSafe`, `OwnedJevClient`.
- `src/Intelligence/HeyGanesh.TurnRouter/TurnLegs.cs` (new): `TurnLegs.Choose`, `TurnLeg`, `LessonCanvas`, `LegChoice`.
- `tools/TurnRouterSmoke/TurnRouterSmokeHost.Legs.cs` (new) + host switch: TR-6 `--assert-legs`.
- `src/Experience/HeyGanesh.App/`: csproj (TurnRouter, PartLibrary), `ShellHost.Route.cs` (new), `ShellHost.cs`,
  `ShellHost.Session.cs`, `ShellHost.Lesson.cs`, `ShellHost.Args.cs`.
- `.github/workflows/ci.yml`: G-18 gains `--assert-legs`.
- Docs: DEC-036, FLOW, ARCHITECTURE, AGENTS.md, TEST-CHECKLIST (TR-6, SH-21, SH-22), CI.md, HANDOVER, ROLLBACK (first).

## Execution path affected

FLOW.md "Lesson leg" (`TeachAsync` dispatch) and "Turn routing path". Nothing before the verdict changes.

## Alternatives rejected

Routing before the locate step (loses "a match was found"; lessons need the grounding anyway). Routing after the highlight
ends (adds Jev's wait to every turn). Making `TypeSafeJevClient` public, or a second TypeSafe client in the shell.

## What could break, and how we'd know

- Element Selection with a client it does not own: the shell disposes it after the service (`using` order).
- With keys set and no flag, turns can now end at the highlight or become lessons. `--no-route` restores the old leg.
- A one-model catalogue through validation and click-to-ask: only proven live (SH-22).

## Verification

- Builds, sequential: ElementSelection, TurnRouter, TurnRouterSmoke, App, each `-warnaserror` 0/0.
- TurnRouterSmoke: source 3, request 14, map 13, gate 7, errors 5, legs 10.
- App EXE offline: SH-3, SH-4, SH-18, SH-21, SH-7 (cancel 1 and 60).
- Local CI: 14 gates, all exit 0.
