# FEATURE-006 — Tutor Engine (teaching layer over an unimplemented provider seam)

- **Status:** module + verification host BUILT and green offline (2026-09-30); shell wiring DONE
  2026-09-30 (DEC-016, item 1 below); **first live provider BUILT 2026-10-01 (DEC-017, item 2
  below)** — internal `DeepSeekTutorClient` behind `TutorClientFactory.CreateDeepSeek`. Open: the
  one-line shell flip and the live-gated rows.
- **Decision:** [DEC-015](../DECISIONS.md#dec-015--2026-09-30--cline-agent-pixel-canary),
  [DEC-016](../DECISIONS.md#dec-016--2026-09-30--cline-agent-pixel-canary),
  [DEC-017](../DECISIONS.md#dec-017--2026-10-01--cline-agent-pixel-canary). **Checks:**
  TU-1..TU-12 + SH-15..SH-17 in [TEST-CHECKLIST.md](../TEST-CHECKLIST.md). **Flow:** Tutor path in
  [FLOW.md](../FLOW.md).

## Scope (as given by the owner)

An academic/technical tutor: it takes a question plus optional bounded screen context and optional
visuals, and produces explanations. It must be strictly separate from screen interaction —
describing actions is teaching, performing them is not. The teaching kind (academic/technical) was
the distinguishing requirement: the question is the teaching task and stays answerable with zero
screen data.

## What landed

- `src/Intelligence/HeyGanesh.TutorEngine/` — `TutorService.ExplainAsync` over `TutorRequest` (question +
  `TutorContext` snapshot, optional `TutorVisual` JPEG) → `TutorResult`. Service-owned system prompt;
  untrusted-data labelling of all screen content; coordinate ban in answer prose; visual caps;
  deterministic prompt rendering; Voice-mirrored exception families; no `IDisposable` (caller owns
  the seam). Zero NuGet, zero ProjectReference, empty P/Invoke surface.
- `tools/TutorSmoke/` — eight rows, TU-1..TU-10, all offline (fakes + recorded completions).
- Deliberately absent: any live provider, model name, endpoint, key name — DEC-015 leaves that
  decision to the owner.

## Left open (next changes, each its own ticket-able change)

1. ~~**Shell wiring**~~ **DONE 2026-09-30 (DEC-016):** `GuidanceResult → TutorContext` mapping at the
   call edge (Failed short-circuits before any map; no-match → `TutorContext.Empty`),
   `ExplanationText → SpeakRequest.Text` verbatim, `--no-speak` for silent turns; seam composed by
   `CreateTutorClient()` — still null by design (the DEC-017 flip is its own change), so the leg
   reports its skip today.
1b. **Cropped-region visual:** a crop around the resolved box needs a JPEG decoder (new dependency or
    hand-rolled). Deliberately NOT built by the wiring change (DEC-016); the whole retained frame is
    attached as `DownscaledFrame` in the meantime.
2. ~~**First live `ITutorClient`:** provider choice (owner), key resolution at call time (DEC-007
   pattern), visual → provider payload mapping inside the client.~~ **DONE 2026-10-01 (DEC-017):**
   owner chose DeepSeek `deepseek-flash`, direct; `DeepSeekTutorClient` is internal and constructed
   only via `TutorClientFactory.CreateDeepSeek(...) → ITutorClient`; key read at call time from
   `DEEPSEEK_API_KEY`; the `TutorVisual` crosses as the exact-bytes `data:image/jpeg;base64,` URL in
   the verified `text`+`image_url` content-part array (no decoder, no re-encode); fixed
   `thinking:{"type":"disabled"}`; TU-11 pins the wire offline through the exact factory path
   (loopback `BaseUrl` stub, no `InternalsVisibleTo`). **The remaining step is the shell flip:**
   `CreateTutorClient()` returning the factory's client — one line, gated on TU-12's live half.
3. **Live prompt-injection row** in TEST-CHECKLIST (TU-5's open half) — can only exist once a live
   client does.
4. Jev's position "before the Tutor Engine call" remains an open placement question
   (ARCHITECTURE.md) — unchanged by this feature.