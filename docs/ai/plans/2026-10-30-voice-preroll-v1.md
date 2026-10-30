# Plan: voice pre-roll, don't lose the first words (build sequence step 5)

2026-10-30 · claude-opus-5-5 · STATUS: BUILT 2026-10-30 (owner: "Yes, build pre-roll"): VO-11 failed on the old client
(first frame #7) and passes 11/11 after the change on three runs (first frame #0; stub turn 1.91 s → 1.22 s); VO-3..VO-10
offline rows and the shell's listen rows unchanged. Found while building: the ready payload's version field name differs
from upstream's schema (see DEC-040, HANDOVER).

## Sequence change

Step 5 was "keep the Gateway connection warm between presses". DEC-013 Option F already rejected holding the socket open
(the Gateway takes one active client per owner, so it would keep the operator's own TUI/WebUI evicted), and an idle
upstream realtime session has an unverified cost. Pre-roll fixes the user-visible problem without reopening that.

## Problem, restated

The microphone started only after the WebSocket opened, `session.ready` arrived and the health check ran. Speech in that
window was never recorded.

## The change

1. At the press, resolve the endpoint; missing or malformed fails at once and never opens the microphone.
2. Start the microphone; frames wait in the existing bounded queue (100 × 100 ms, drop-oldest).
3. Connect as before, retries included; the queue survives attempts within one listen.
4. On `session.ready`, the send pump drains the queue in order, then continues live; it is stopped at the end of each attempt.
5. However the listen ends, the microphone stops and the queue is dropped.

The utterance cap counts from the press; the silence window from `session.ready`. `UtteranceResult` gains
`ConnectDuration` and `BufferedFrames`; the shell prints `connect :`.

## Files

- `src/Experience/HeyGanesh.Voice/QwenGatewayClient.Listen.cs`, `UtteranceResult.cs`.
- `src/Experience/HeyGanesh.App/ShellHost.Session.cs`: the `connect :` line.
- `tools/VoiceSmoke`: `GatewayStub.cs` (loopback HttpListener stub + `NumberedMicrophone`), `VoiceSmoke.Preroll.cs`
  (VO-11), host switch.
- CI G-21; docs: DEC-040, FLOW, AGENTS.md, TEST-CHECKLIST (VO-11, VO-L2), CI.md, HANDOVER, ROLLBACK (first).

## Alternatives rejected

A warm socket (DEC-013 Option F); shorter timeouts; running the health check alongside the handshake (follow-up).

## Verification

Voice, VoiceSmoke, App build `-warnaserror` 0/0; VO-11 before (fails) and after (passes); VO-3..VO-10 offline rows; shell
`--once` (no URL) 1, `--once --question` 12, `--seconds 3` 0, cancel 13; local CI 17 gates. Not verifiable here: the real
Gateway's connect time and how it handles the burst (VO-L2).
