# FEATURE-004 — Voice pipeline (Qwen Gateway listen seam + Fish TTS out seam)

- **Date:** 2026-09-30
- **Status:** in-progress — offline rows VO-1..VO-10 green; every live path (real Gateway listen, real microphone, real Fish synthesis) needs a running qwen-audio-agent Gateway, `FISH_AUDIO_API_KEY`, a mic/speaker and a human, and is not yet run
- **Module(s):** Voice pipeline (new); consumes nothing, is consumed by nobody yet (the App Shell still takes `--question`)

## Description

The speech seam HeyGanesh owns, end to end in one module:

```text
Microphone → qwen-audio-agent Gateway (external realtime frontend, AGENT_PROTOCOL=none)
  → transcript.final role=user → VoiceService.ListenOnceAsync → UtteranceResult
  → (FUTURE Tutor/Context pipeline answers) → SpeakRequest
  → Fish Audio TTS behind ITtsClient → speaker → SpeakResult
```

Two facts shape the whole design and are recorded as DEC-013:

1. **The Qwen frontend is conversational, not a transcriber.** `AGENT_PROTOCOL=none` disables the Backend
   Agent only; the realtime frontend still generates its own answer to every utterance, and the Gateway
   protocol exposes **no supported pre-generation suppression for live audio** (connection prefs are
   presentation, AgentDelivery context-mode is Client-Events-only, sleep/suspend/mute are capture-side).
   So HeyGanesh neutralises rather than prevents: `transcript.final role=user` is the only frame that can
   become text, `response.started` triggers `response.cancel`+`interrupt`, every assistant transcript and
   audio frame is discarded and counted (`GatewaySuppressionLedger`), and a `task.*` event aborts the
   listen. Tutor authority is enforced in HeyGanesh code, not in Qwen's configuration — limitation L-1.
2. **Fish output is `wav` @ 44.1 kHz.** The v1 enum is `wav|pcm|mp3|opus`; WAV carries its own header so
   `WavPcmReader` can validate (RIFF/PCM16/mono, fail loud) and `WinMmAudioOutput` can play with zero
   decoder packages. HTTP `ResponseHeadersRead` gives transport streaming only — no incremental-generation
   claim is made, because the verified contract does not guarantee one.

Both providers sit behind HeyGanesh-owned seams (`IVoiceListener`, `ITtsClient`) so either can be swapped;
they are external runtimes/services, never C# dependencies. `--question` retires only when the shell is
wired to the seam (FEATURE-003's follow-up: `Pressed` records, `Released` submits).

## Out of scope

- **Tutor Engine** — Voice delivers the user's text and speaks answer text; it never answers a question,
  and Qwen's LLM is never allowed to answer one instead (VO-5 asserts the discard side).
- Shell wiring: no `HeyGanesh.App` file is edited; `--question` stays.
- Any edit to ElementSelection, Vision, ContextOrchestrator or ScreenOverlay.
- WinUI/WPF, tray icon, any new NuGet (zero `PackageReference`, zero `ProjectReference` in the module).
- Computer actions of any kind — the P/Invoke surface moves audio only.
- Security Proxy deployment (the `FISH_AUDIO_API_KEY` direct path extends DEC-007, same as Vision/ES).

## Acceptance criteria

- [x] Dependency-free and module-isolated: zero `PackageReference`, zero `ProjectReference` (VO-2).
- [x] `ListenOnceAsync` returns committed user text, honest silence (`IsNoSpeech`), or a typed failure —
      never one confused for another (VO-3, VO-4, VO-10).
- [x] Assistant output is neutralised: only `role=user` finals become text; audio/transcripts/tools counted
      and dropped; replayed offline (VO-5). Cancel is issued on `response.started` (counted in the ledger).
- [x] Backend Agent abort: `task.*` is classified and counted; health check asserts `AGENT_PROTOCOL=none`
      when the Gateway reports it and records `unknown` otherwise (VO-7 offline half).
- [x] GCP contract pinned: exact 7-message allowlist, all builders parse, all server-frame kinds classify,
      role-less transcripts fail safe to `Unknown` (VO-6).
- [x] Fish wav/44100 body, WAV validation fails loud, key gate fires before any I/O, key never echoed (VO-8, VO-10).
- [x] Cancellation propagates as OCE (exit 13), never as a verdict; one listen/speak at a time, refused
      not queued (VO-9).
- [x] The real speaker plays: fake TTS through `WinMmAudioOutput` → `SpeakResult` with duration from bytes
      that moved (VO-10; caught the `CALLBACK_EVENT` constant bug).
- [ ] Live: a real Gateway listen returns a real transcript, suppresses a real frontend answer, and reports
      the health endpoint's capability/backend fields.
- [ ] Live: real microphone capture and a real Fish synthesis audible end to end.
- [ ] Shell wiring (`Released` submits, `--question` retires) — FEATURE-003 follow-up.

## Test checklist

VO-1..VO-10 in [TEST-CHECKLIST.md](../TEST-CHECKLIST.md).

## Rollback

Delete `src/HeyGanesh.Voice/`, `tools/VoiceSmoke/` and `docs/ai/features/FEATURE-004.md`, then revert this
session's doc edits. No existing source file is edited by this feature, so no module behaviour can regress
— the 2026-09-30 entry in [ROLLBACK.md](../ROLLBACK.md) records the point and what to re-check afterwards.
