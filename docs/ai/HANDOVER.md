# HANDOVER — Session Log

> Append-only session log. One entry per work session, **newest entry at the top**.
> Use the HANDOVER entry template in [TEMPLATES.md](./TEMPLATES.md) §1.
> Never delete or rewrite a prior entry — correct it with a new entry instead.

## Index

| Date | Model / version | Headline | Branch / commit |
|------|-----------------|----------|-----------------|
| 2026-10-01 | Cline agent (Pixel Canary) | First live teaching provider — internal `DeepSeekTutorClient` (`deepseek-flash`, call-time `DEEPSEEK_API_KEY`, fixed `thinking:disabled`) behind `TutorClientFactory.CreateDeepSeek → ITutorClient`; TU-11 pins the wire offline via loopback-`BaseUrl` stub on the exact factory path (no `InternalsVisibleTo`); TU-12 offline shape exit 12; shell flip deliberately NOT taken (DEC-017); TU-11/TU-12 | `main` (working tree) |
| 2026-09-30 | Cline agent (Pixel Canary) | Shell teaching leg wired — verdict maps to `TutorContext` at the call edge (Failed short-circuits, no-match → Empty, Found attaches the retained frame as a visual), lesson spoken verbatim behind `--no-speak`; seam composed null-client until the provider decision (DEC-016); SH-15..SH-17 | `main` (working tree) |
| 2026-09-30 | Cline agent (Pixel Canary) | Tutor Engine built — question-first teaching layer over `ITutorClient`; untrusted-data prompt, capped opaque visuals, zero actuation, no live provider by design (DEC-015); TU-1..TU-10 offline rows green; shell wiring pending | `main` (working tree) |
| 2026-09-30 | Cline agent (model id not exposed to the agent) | App Shell wired to Voice — press listens, release ends the utterance, transcript becomes the question; `--question` demoted to legacy (DEC-014); SH-10..SH-14, live rows human-gated | `main` (working tree) |
| 2026-09-30 | Cline agent (model id not exposed to the agent) | Voice pipeline built — Qwen Gateway GCP-7.0 listen seam with cancel/discard suppression + Fish WAV TTS behind `ITtsClient` (DEC-013); VO-1..VO-10 offline rows green, all live paths unrun | `main` (working tree) |
| 2026-09-29 | Pixel Canary | App Shell built — the process host that wires hotkey → Context Orchestrator → Screen Overlay (DEC-012); offline rows SH-1..SH-8 green, live path still unrun | `main` (working tree) |
| 2026-09-24 | Muse Spark (muse-spark) | Screen Overlay built — dependency-free Win32 click-through highlight over `GuidanceResult` (DEC-011, SO-1..SO-9, EX-5 ✅) | `main` (working tree) |
| 2026-09-23 | Cline agent (Claude Sonnet 4.5) | Context Orchestrator built — ES primary, Vision fallback on failure only, normalized `GuidanceResult` (DEC-010) | `main` @ `8824568` (working tree) |
| 2026-09-23 | Muse Spark (muse-spark) | Scoped the AGENTS.md invariants claim — enforced vs design-only groups | `main` @ `94a8af1` (working tree) |
| 2026-09-23 | Muse Spark (muse-spark) | AGENTS.md sync to Element Selection reality + line-endings correction | `main` @ `94a8af1` (working tree) |
| 2026-09-23 | Muse Spark (muse-spark) | ES doc sync to green (ARCHITECTURE/TEST-CHECKLIST) + host `choice` print fix | `main` @ `94a8af1` (working tree) |
| 2026-09-23 | GLM-5.3-Flash | Restore `BuildCandidateSet` — ES module builds clean; ES-4..ES-7 verified against live windows | `main` @ `94a8af1` (working tree) |
| 2026-09-23 | GLM-5.3-Flash | Element Selection docs backfill (ARCHITECTURE / FLOW / TEST-CHECKLIST) — ES build still blocked (CS0103) | `main` @ `94a8af1` (working tree) |
| 2026-09-22 | Cline agent (model id not exposed to the agent) | DECISIONS.md recovery + Element Selection decisions (DEC-008/009) | `main` (working tree) |
| 2026-09-21 | GLM-5.3-Flash / Laguna S 2.1 | Vision / Coordinate Detection module + verification host | `main` (working tree) |
| 2026-09-21 | GLM-5.3-Flash | Screen Capture module + verification host | `main` (working tree) |
| 2026-09-21 | GLM-5.3-Flash | Global Hotkey Monitor module + verification host (first code in the repo) | `main` @ `fc1be37` |

---

<!-- SESSION ENTRIES BELOW THIS LINE — newest first -->
## 2026-10-01 — Cline agent (Pixel Canary) — First live teaching provider: DeepSeek behind the factory; shell flip still one line away

- **Branch / commit:** `main` @ `9e73fdb` (+ the uncommitted DEC-016 shell leg from the previous
  session) — this change adds ONLY new untracked files plus two smoke-host edits and doc rows;
  rollback in ROLLBACK.md. `TutorService`, the shell, Voice/ES/Vision/CO/Overlay untouched;
  the App still builds against the unchanged public surface.
- **Scope:** close FEATURE-006 item 2 by owner decision — DeepSeek `deepseek-flash`, direct,
  `DEEPSEEK_API_KEY`, DEC-007 pattern (DEC-017).

### Done
- `src/HeyGanesh.TutorEngine/`: `DeepSeekTutorOptions` (public options: model/base/detail/timeout,
  no key, no handler, no thinking knob by design); `DeepSeekTutorMessages` (internal mapping:
  system string; user plain string or text+`image_url` data-URL parts — exact bytes, no decode);
  `DeepSeekTutorClient` (internal; call-time key; `{model, messages, max_tokens, stream:false,
  thinking:{type:disabled}}`; 401/403→auth status-only, 429→rate-limit, other→client+status+
  ≤500-char snippet; empty stays empty for the service); `TutorClientFactory.CreateDeepSeek` (the
  only new public API, returns the seam).
- `tools/TutorSmoke/`: loopback `StubServer` (TcpListener :0 probe → concrete-prefix `HttpListener`
  bind with bounded retry — `HttpListener` cannot bind `:0`; strictly 127.0.0.1, no wildcard, no
  admin; canned responses served in enqueue order, take-before-delay); TU-11 `--assert-tutor-payload`
  (34 checks: endpoint/headers/payload/thinking-pinned/absent-fields/data-URL byte fidelity/error
  families/empty/OCE/visibility pins — client built ONLY via the factory + stub `BaseUrl`);
  TU-12 `--live-explain` (offline shape: KEY ERROR exit 12); `--assert-source` extended (no
  `InternalsVisibleTo`, single sanctioned env read, factory shape, options carry no key/handler).
- Docs: DEC-017, TU-11/TU-12 + rewritten Tutor scope note, ARCHITECTURE (module line + Tutor
  bullets), FEATURE-006 (item 2 closed, flip named as the remainder), FLOW seam block, ROLLBACK,
  AGENTS (table row + new commands), this entry.

### In progress / next
- **The flip:** shell `CreateTutorClient()` returning `TutorClientFactory.CreateDeepSeek()` —
  deliberately not taken here; gate it on TU-12's live half being run once with a real key
  (owner's machine, owner's key).

### Risks & gotchas
- **`thinking:{"type":"disabled"}` is a pinned v1 decision, not a knob** — if live lessons read
  worse without the reasoning trace, the fix is a measured DECISIONS entry changing the payload
  value, never a new options property added "temporarily".
- **TU-11 alignment discipline:** the stub serves the Nth enqueued response to the Nth request — a
  row that drives a request must enqueue exactly one response first, or scenarios shift (a silent
  500 "ran out of canned responses" is the tripwire). Cancellation sub-row proves take-before-delay.
- **Stub ≠ provider acceptance** — TU-11 pins what we SEND only; the real 400-class surprises
  (if any) surface first in TU-12's live half, which is why the flip waits for it.
- Shared `DEEPSEEK_API_KEY` with Vision: 429s are typed on both seams; token budgets interact.
- `--live-explain` with a bad-but-present key also exits 12 (401 maps to auth) — same code,
  different cause; the header's presence/length line distinguishes them.
- The row mutates `DEEPSEEK_API_KEY` in-process (restored in `finally`) — do not parallelize
  smoke rows in one process.

### Files touched
- New: `src/HeyGanesh.TutorEngine/{DeepSeekTutorClient,DeepSeekTutorOptions,DeepSeekTutorMessages,TutorClientFactory}.cs`;
  `tools/TutorSmoke/{TutorSmokeHost.StubServer,TutorSmokeHost.Payload,TutorSmokeHost.Live}.cs`.
- Edited: `tools/TutorSmoke/TutorSmokeHost.cs` (modes/usage/header), `TutorSmokeHost.Source.cs`
  (audit), docs: `DECISIONS`, `TEST-CHECKLIST`, `ARCHITECTURE`, `FLOW`, `HANDOVER`, `ROLLBACK`,
  `features/FEATURE-006`, `AGENTS.md`.
- No module outside `HeyGanesh.TutorEngine` (additive files only) and no shell file touched.

### Verification run (all run this session, all green)
- `dotnet build src\HeyGanesh.TutorEngine` → 0 warnings; `dotnet build tools\TutorSmoke` → 0
  warnings; `dotnet build src\HeyGanesh.App` → 0 warnings (public surface unchanged).
- `--assert-tutor-payload` → 34/34 PASS, exit **0** (incl. `thinking.type == disabled` on both
  text and visual turns, 401/403/429/500 mapping, empty→service failure, cancel→OCE, factory/
  internal visibility pins; stub bound 127.0.0.1 with a probed free port).
- `--assert-source` → 15 PASS exit **0** (new provider audit rows included); `<PackageReference`
  real tags → 0; TU-1..TU-10 regression: prompt/grounding/parity/client-error exits 0,
  auth-error exit **12**, `--cancel-ms 60` exit **13**, `--fake-explain` exit 0.
- `--live-explain` (no key in this shell) → `KEY ERROR` naming the variable, exit **12** — the
  offline shape; **live half not run** (needs the owner's key).
- **Not run / not verified:** the real-provider call (TU-12 live), the shell flip (not built by
  design), SH-17 end-to-end (still gated on both).

## 2026-09-30 — Cline agent (Pixel Canary) — Shell teaching leg: the verdict now reaches the Tutor, no provider yet

- **Branch / commit:** `main` @ `9e73fdb` — this change is entirely **uncommitted** (four edited
  `src/HeyGanesh.App/` files + new untracked `ShellHost.Tutor.cs` + doc rows); rollback recorded in
  ROLLBACK.md. No module assembly touched — the App gained only a ProjectReference to TutorEngine.
- **Scope:** close FEATURE-006 item 1 — App Shell → Tutor Engine wiring per DEC-015's prescription,
  without choosing a provider and without changing any pre-verdict behavior.

### Done
- `src/HeyGanesh.App/ShellHost.Tutor.cs` (new): `CreateTutorClient()` — THE forward-declaration point,
  returns null today by design; `MapGuidanceToContext` — scalar snapshot exactly as TutorContext's
  remarks prescribe (`!Found → Empty`; Choice/Jev probabilities/`Captures` deliberately unmapped);
  `BuildVisual` — `Captures[0]`'s already-encoded JPEG attached by reference as `DownscaledFrame`
  only when `Found` (no decoder, no crop, no re-capture); `TeachAndSpeakAsync` — ExplainAsync → print
  → `SpeakAsync` verbatim (or `--no-speak`), never throws; leg errors map 12/3/13/4 after the verdict.
- `ShellHost.Session.cs`: both post-verdict return sites (`--no-overlay`, after-show tail) became
  `Failed ? 1 : TeachAndSpeakAsync(...)` — branch-before-map is the CO-6 guard; pump + `RunSessionAsync`
  plumbing; exit-code header extended. `ShellHost.cs`: seam composed via the null factory (using-owned),
  `--once` and hooked paths share the leg. `ShellHost.Args.cs`: `--no-speak` + usage/footer/header
  honesty (the stale "TTS not wired into the shell yet" line is gone). csproj: 5th ProjectReference.
- Docs: DEC-016, SH-15..SH-17 + scope note, FLOW tutor-path flip, ARCHITECTURE bullets, FEATURE-006
  item 1 closed (+ 1b crop follow-up), ROLLBACK point, this entry, AGENTS notes.

### In progress / next
- The single next action is the owner's: **decide the first live `ITutorClient`** — then
  `CreateTutorClient()` returns it and SH-17 (live teach-and-speak) becomes runnable.

### Risks & gotchas
- **`CreateTutorClient()` returning null is the design, not a bug** — SH-17 being impossible offline
  is by DEC-015; never "fix" the leg to fake a lesson or fail closed when it skips.
- Header's `tutor      : seam wired, no client yet` line must stay in step with the factory.
- `ArgumentException` from `ExplainAsync` means the shell's own mapping produced a malformed snapshot
  → exit 4 `TUTOR MAPPING FAULT` — do not re-map it to 3.
- A spoken lesson now extends the busy window: the second chord REFUSED during speech is VO-9
  surfacing at the trigger, not a new bug. Speak runs sequentially (fire-and-forget would break
  Drain/shutdown).
- Caps live in `TutorService` alone; shell-side pre-checks would be a second enforcer (rejected,
  DEC-016). Crop-around-box is FEATURE-006 item 1b — needs a JPEG decoder, stop-and-ask territory.

### Files touched
- `src/HeyGanesh.App/`: `ShellHost.Tutor.cs` (new), `ShellHost.cs`, `ShellHost.Session.cs`,
  `ShellHost.Args.cs`, `HeyGanesh.App.csproj`. Docs: `DECISIONS` (DEC-016), `TEST-CHECKLIST`
  (SH-15..17), `FLOW`, `ARCHITECTURE`, `HANDOVER`, `ROLLBACK`, `features/FEATURE-006.md`, `AGENTS.md`.
- No module source outside `src/HeyGanesh.App/` was edited.

### Verification run (all run this session, all green)
- `dotnet build src\HeyGanesh.TutorEngine` then `src\HeyGanesh.App` (sequential) → **0 Warning(s)
  0 Error(s)**; `<PackageReference` in the App csproj → **0** (SH-15).
- `--help` → lists `--no-speak` + reused-code footer, exit **0**;
  `--once --question "…"` (keys unset) → unchanged `KEY ERROR (Element Selection)`, exit **12**;
  `--once --cancel-ms 60 --question "x"` → exit **13**; `--once --no-speak --question "x"` and
  `--once --no-overlay --no-speak --question "x"` → exit **12** each (SH-16).
- **Not run / not verified:** SH-17 end-to-end teach-and-speak (needs a live `ITutorClient` — none
  exists); the skip line itself never printed in a live run (every offline row stops at the key gate
  before the verdict); the mapping's scripted assertion (deferred to ShellSmoke, FEATURE-005).

## 2026-09-30 — Cline agent (Pixel Canary) — Tutor Engine: the teaching seam, deliberately provider-less

- **Branch / commit:** `main` @ `9e73fdb`. The module itself is **committed** — `9d3ff37 feat: add Tutor Engine module` (10 files, 808 insertions; working tree identical to that commit, builds verified green against it). Uncommitted after this session: `tools/TutorSmoke/` (untracked), `docs/ai/features/FEATURE-006.md` (untracked), and the doc edits listed under Files touched — all reversible; no existing **source** file outside the new module was edited (rollback point recorded in ROLLBACK.md).
- **Scope:** build the Tutor Engine per the owner's requirement — an academic/technical tutor taking question + optional bounded screen context/visuals, strictly separated from screen interaction; verify offline; change no other module.

### Done
- `src/HeyGanesh.TutorEngine/` (10 files, zero PackageReference, **zero ProjectReference**): `TutorService.ExplainAsync(TutorRequest, ct) → TutorResult` — validation → deterministic `RenderPrompt` → seam call → `Translate`d failures → capped result. Contracts: `ITutorClient` (disposable, caller-owned; **no live implementation shipped — DEC-015**), `TutorRequest/Context/Visual/Prompt/Completion/Result`, `TutorOptions` (1600-char answer, 500 tokens, 400-char untrusted-description clamp, 512 KiB/1568 px visual caps), `TutorClientException`/`TutorAuthenticationException`/`TutorRateLimitException` (Voice-mirrored shapes, no shared assembly).
- Load-bearing semantics: question is the teaching task and leads every prompt; all screen text enters labelled `UNTRUSTED DATA`; coordinates are metadata banned from answer prose; visuals cross as separate capped bytes, never base64 in text; over-cap is `ArgumentException` (caller bug) never silent downscale; empty completion is provider failure, never a blank lesson.
- `tools/TutorSmoke/` (7 files): `--fake-explain`, `--assert-prompt`, `--assert-grounding`, `--assert-parity`, `--assert-source`, `--assert-client-error`, `--assert-auth-error`, `--cancel-ms` — TU-1..TU-10 of TEST-CHECKLIST.
- Docs: DEC-015, TU rows + scope notes, FLOW Tutor path section, ARCHITECTURE module line + detail section, FEATURE-006, this entry, AGENTS.md module row + smoke commands.

### In progress / next
- **The shell wiring is the next change** (FEATURE-006): map `GuidanceResult → TutorContext` at the call edge, call `ExplainAsync`, feed `ExplanationText` into the already-composed `SpeakAsync`; a `Failed` verdict skips the Tutor (branch first for the CO-6 quirk: Found=false can arrive with Failed=true).
- The first **live `ITutorClient`** is the owner's provider decision — deliberately not made here (DEC-015); it will resolve its key at call time, DEC-007 pattern, outside this assembly's surface.

### Risks & gotchas
- **Do not add `InternalsVisibleTo`** — `RenderPrompt`/`SystemContract` are internal and pinned through the recording fake; that seam is the test surface.
- `ArgumentException` from validation/caps must stay unre-wrapped (`Translate`'s default passes it through); tidying it into a client exception would erase the caller-bug/provider fault line.
- The auth smoke row **exits 12** and cancel **exits 13** on purpose; read codes from the built EXE (CO-8 lesson). The fake auth message embeds a sentinel credential — the row prints type+length only; never "improve" it into printing the message.
- Source-audit tokens are anchored (`<PackageReference`, not `PackageReference`) because csproj comments legitimately contain the bare words; keep new banned tokens anchored the same way.
- `TutorVisual` bytes are never decoded here — if a future provider needs a data URL, that mapping belongs to the provider client, not to `TutorPrompt`.

### Files touched
- `src/HeyGanesh.TutorEngine/` — new: csproj + 9 source files. `tools/TutorSmoke/` — new: csproj, `Program.cs`, `Fakes.cs`, `TutorSmokeHost.cs`, `.Rows.cs`, `.Errors.cs`, `.Asserts.cs`, `.Grounding.cs`, `.Source.cs` (9 total incl. csproj/Program).
- `docs/ai/DECISIONS.md` (DEC-015), `TEST-CHECKLIST.md` (TU-1..TU-10), `FLOW.md` (Tutor path), `ARCHITECTURE.md` (module line + detail), `HANDOVER.md` (index + entry), `features/FEATURE-006.md` (new), `AGENTS.md` (module row + commands). No existing **source** file was edited.

### Verification run (all run this session, all green)
- `dotnet build src\HeyGanesh.TutorEngine -c Debug` → **0 Warning(s) 0 Error(s** (TU-1); same for `tools\TutorSmoke`.
- `TutorSmoke --fake-explain "what does git rebase actually do"` → result block, exit **0** (TU-2); `--assert-prompt` → **15/15 PASS**, exit **0** (TU-3/TU-4); `--assert-grounding` → **9/9 PASS**, exit **0** (TU-5); `--assert-parity` → **3/3 PASS**, exit **0** (TU-10).
- `--assert-client-error` → **5/5 PASS**, exit **0**; `--assert-auth-error` → **3/3 PASS**, exit **12**; sentinel absent from console (TU-6); `--cancel-ms 60` → exit **13** (TU-7); `--assert-source` → **11/11 PASS**, exit **0** (TU-8/TU-9).
- **Not run / not verified:** everything a real model would decide — injection resistance, lesson quality, live transport. There is no live client to run (DEC-015). Shell integration is untouched by design.

## 2026-09-30 — Cline agent (model id not exposed to the agent) — App Shell: the microphone is the question source

- **Branch / commit:** `main` — working tree, uncommitted (`src/HeyGanesh.App/` is **untracked**, so a `git checkout` rollback cannot restore it; the file copies at `%TEMP%\hg-rollback-shell-20260930\` can — see ROLLBACK).
- **Did:** `HeyGanesh.App` now composes one `VoiceService` (4th project-ref) and a chord turn is `Pressed → ListenOnceAsync (pool)`, `Released → end the utterance`, committed `FinalText → options.Question → the unchanged HandleHotkeyPressedAsync → ShowAsync`. `TriggerPump` became a 3-state machine (`Idle/Listening/Resolving`) with a per-turn CTS created on press and disposed by the next press / `RunDirectAsync` / `DrainAsync`. `--question` demoted to legacy: ignored with a warning in hooked mode, still deterministic under `--once`; `--once` without it runs one live listen. `SpeakAsync` is composed but never called (no Tutor). Exit 2 is now only "the shell could not start".
- **Left / Watch:** `UtteranceResult.Failed` maps to **exit 1**, not 12/3 — the reason is a returned string and Voice is frozen, so the typed signal does not exist (DEC-014). **Release cannot force-commit** (no finalize call in the Voice API): releasing mid-sentence abandons the utterance, exit 0, no capture — this is the accepted limitation, not a bug. `IsNoSpeech` and a release-abandoned turn never reach the orchestrator. Press/release ordering and the IsNoSpeech short-circuit are human-gated; a `ShellSmoke` host with an injectable trigger is the filed follow-up. `OnReleased` cancels *outside* the gate on purpose (cancel callbacks re-take it) and relies on `hotkey.Stop()` joining the monitor thread before `DrainAsync` disposes — do not "tidy" that ordering.
- **Next:** run SH-13/SH-14 live (Gateway + keys + mic) — that run also confirms the two GCP details DEC-013 left unverified — then build `ShellSmoke`; the Tutor Engine and `SpeakAsync` stay separate changes.
- **Verified:** `dotnet build src\HeyGanesh.App` → 0 warnings/0 errors; `--help` 0; `--seconds 4` (no flag) hooks and exits 0; `--once --question` still exits 12 at the key gate (SH-4/7/8 intact); `--question x --seconds 4` warns and exits 0; `--once` with no Gateway → `VOICE FAILURE` exit 1, no capture, no crash; regression sweep green for VO/CO/SO rows. **Not verified:** SH-13, SH-14's silence half, and `HotkeySmoke --seconds 4` (needs real keystrokes; would not self-terminate here — two orphans killed; the hotkey module itself was not modified).

## 2026-09-30 — Cline agent (model id not exposed to the agent) — Voice pipeline: the speech seam, not a second teacher

- **Branch / commit:** `main` — working tree, uncommitted (App Shell / CO / Overlay work also uncommitted; HEAD `8824568`).
- **Scope:** one logical change — build `src/HeyGanesh.Voice/` + `tools/VoiceSmoke/` as the speech in/out seam, with the Qwen Gateway as an external realtime frontend (Backend Agent off) and Fish TTS behind `ITtsClient`. No Tutor Engine, no shell rewiring (`--question` stays), no edits to ElementSelection / Vision / ContextOrchestrator / ScreenOverlay source, no WinUI/WPF, no new NuGet.

### Done
- `src/HeyGanesh.Voice/` — zero `PackageReference`, zero `ProjectReference`. Seams: `IVoiceListener`, `ITtsClient`, `IAudioOutput`, `IMicrophoneSource`; results: `UtteranceResult` (silence≠failure), `SpeakRequest`/`SpeakResult`; facade `VoiceService` (`ListenOnceAsync`/`SpeakAsync`, one session at a time, linked-token cancellation, refuse-not-queue).
- **Qwen side:** `QwenGatewayClient` speaks the Gateway Client Protocol (wire 7.0): per-utterance socket, `session.hello` (input-only capabilities, `output_enabled=false`), version-checked `session.ready`, mic → `input_audio_buffer.append`, `transcript.final role=user` as the only question source. **Pre-generation suppression does not exist for live audio** (investigated: connection prefs are presentation, AgentDelivery context-mode is Client-Events-only, sleep/suspend/mute are capture-side) → cancel/discard after `response.started`, all assistant audio/transcripts discarded, everything counted in `GatewaySuppressionLedger` (DEC-013 L-1). `task.*` aborts (`AGENT_PROTOCOL=none`), health pre-flight asserts capability `realtime.conversation-client-v1` + backend-none when reported ("unknown" recorded, never trusted). Pure layer: `QwenGatewayProtocol.Classify`/builders — replay-testable with no Gateway.
- **Fish side:** `FishTtsClient` behind `ITtsClient` — `POST /v1/tts`, `format=wav` @ 44100 (v1 enum `wav|pcm|mp3|opus`; WAV is the only zero-decoder choice), key from `FISH_AUDIO_API_KEY` at call time (DEC-007 pattern), HTTP `ResponseHeadersRead` = **transport streaming only** (no generation-streaming claim). `WavPcmReader` validates RIFF/PCM16/mono and fails loud.
- **Devices:** `WinMmMicrophoneSource` (waveIn, 4 rotating buffers, static thunk + GCHandle) and `WinMmAudioOutput` (waveOut, `CALLBACK_EVENT`, cancellation = real `waveOutReset` stop) — P/Invoke we own, no audio package.
- `tools/VoiceSmoke/` — offline rows over fakes + recorded frames: transcript/no-speech/auth/cancel/burst/speak + `--assert-gcp-events` / `--assert-tutor-authority` / `--assert-no-backend` / `--assert-fish-format`.
- Docs: DEC-013, ARCHITECTURE Voice detail, FLOW Voice path, TEST-CHECKLIST VO-1..VO-10, this entry, FEATURE-004, rollback point, four AGENTS commands.

### In progress / next
- **Wire the shell:** move the trigger `Pressed` → `Released` (record on press, submit on release) and retire `--question` — FEATURE-003's filed follow-up, deliberately not mixed in here. Then the (future) Tutor Engine inserts between `ListenOnceAsync` and `SpeakAsync`.

### Risks & gotchas
- **L-1:** the frontend still *generates* an answer per utterance; we cancel its effects, not its occurrence. A generation the Gateway completes before our cancel is wasted cost, never wasted *audio* (VO-5 asserts the discard side).
- **Known-unverified protocol details** (marked in TEST-CHECKLIST): `input_audio_buffer.append` payload field names (`audio`/`sample_rate`) and `session.ready`'s version field name — both live-only to confirm; VO-6 pins everything else. GCP takes **one active client per owner**: a listen evicts the operator's TUI/WebUI for the utterance's duration.
- **Two real bugs this session's own rows caught:** `CALLBACK_EVENT` for waveform is `0x00050000` (the `0x00010000` window value made `waveOutOpen` fail with MMSYSERR 11), and the winmm `Describe` code→message table was off by N (code 4 is ALLOCATED, not NODRIVER). Both fixed before the row went green.
- The health pre-flight treats "cannot reach/parse health" as `unknown`, not as safe — the `task.*` runtime abort is the guarantee that does not depend on the Gateway's honesty. `WaitToReadAsync` has no timeout overload on this TFM; the silence/utterance deadlines are enforced with a linked token + `CancelAfter` (do not "simplify" it into a blocking wait).

### Files touched
- `src/HeyGanesh.Voice/` — 29 new files (csproj + facade partials + seams/results/options/exceptions + Qwen options/client/protocol/ledger + Fish options/client/WAV reader + winmm P/Invoke/mic/speaker); `tools/VoiceSmoke/` — 6 new files (csproj + Program + host + rows + asserts + fakes). Full names are in the HANDOVER diff.
- `docs/ai/features/FEATURE-004.md` — new ticket; `docs/ai/{DECISIONS,ARCHITECTURE,FLOW,TEST-CHECKLIST,ROLLBACK,HANDOVER}.md` + `AGENTS.md` — DEC-013, one module detail, one flow section, ten check rows, one rollback point, one entry, four commands.
- No existing source file was edited.

### Verification run (all run this session, all green)
- `dotnet build src\HeyGanesh.Voice -c Debug` → **Build succeeded. 0 Warning(s) 0 Error(s)** (VO-1); same for `tools\VoiceSmoke` (after fixing an XML-comment `--` in the csproj).
- `VoiceSmoke --fake-transcript "where is the Save button"` → verdict printed, exit **0** (VO-3); `--fake-nospeech` → `no speech: True`, no FAILURE line, exit **0** (VO-4).
- `VoiceSmoke --fake-auth-error` → `KEY ERROR: …`, exit **12** (VO-10); `--cancel-ms 50` → `CANCELLED: … no verdict`, exit **13**; `--burst-fake` → `second ListenOnceAsync REFUSED (not queued)`, first completes, exit **0** (VO-9).
- `--assert-gcp-events` → 20/20 PASS, exit **0** (VO-6); `--assert-tutor-authority` → 7/7 PASS, exit **0** (VO-5); `--assert-no-backend` → 4/4 PASS, exit **0** (VO-7); `--assert-fish-format` → 8/8 PASS, exit **0** (VO-8).
- `--speak-fake --text "Found the Save button"` → `SpeakResult: 21 chars, 0.35s audio via fish-audio (327 ms)` through the **real** winmm speaker, exit **0** (VO-10).
- **Not run / not verified:** every live path — real Gateway listen (incl. health capability/backend fields, append payload names, live suppression), real microphone capture, real Fish synthesis, and the listen-loop `task.*` abort end-to-end (its classifier+ledger half is VO-7 offline).

## 2026-09-29 — Pixel Canary — App Shell: the process host that finally subscribes the hotkey

- **Branch / commit:** `main` — working tree, uncommitted (the Context Orchestrator and Screen Overlay work is also still uncommitted; HEAD is `8824568`).
- **Scope:** one logical change — build the App Shell, the first assembly that *drives* the pipeline rather than being driven by a verification host, closing the two gaps DEC-010 and DEC-011 deliberately left to it. No Voice pipeline, no WinUI/WPF, no tray icon, no Jev gate, no Tutor/Lesson/Notes/Proxy work, and **no edit to any existing source file**.

### Done
- `src/HeyGanesh.App/` — new `OutputType=Exe` console host, `net8.0-windows10.0.22621.0`, **zero `PackageReference`**; project-refs GlobalHotkeyMonitor + ContextOrchestrator + ScreenOverlay (ScreenCapture/ElementSelection/Vision arrive transitively and are used directly). Files: `Program.cs` (PerMonitorV2 first), `NativeFns.cs` (the one P/Invoke pair — set awareness, and read back what the process actually got), `ShellHost.cs` (composition root + lifecycle + `RunHookedAsync`), `ShellHost.Args.cs` (flags/usage/header), `ShellHost.Session.cs` (`RunSessionAsync` + `TriggerPump`).
- The wiring DEC-010 left open: `monitor.Pressed` → `HandleHotkeyPressedAsync` → `ShowAsync`, with the handler handing the session to the thread pool so the hook callback is never blocked, and one-session-at-a-time enforced with the busy window spanning the overlay's visible duration. `--once` runs the **identical** method with no hook, so the scriptable path and the chord path cannot drift.
- Shutdown ordered and bounded: release hook → cancel lifetime token → wait ≤5 s for the session in flight → reverse-declaration disposal (hotkey, overlay, orchestrator, legs, capture). The wait is what stops a session completing into a disposed overlay.
- One `ScreenCaptureService` for the process, shared into `ScreenOverlayService` as its topology source (the overlay only disposes a capture service it created itself).
- Exit-code contract carried over from the module hosts: 0 verdict (an honest no-match counts) / 1 failed verdict / 2 could not start / 3 provider error / 4 unexpected fault / 12 key missing / 13 cancelled.
- Docs: `DECISIONS.md` **DEC-012** (dependency-free console shell; WinUI 3 deferred again; no test doubles in product code), `ARCHITECTURE.md` (module row + implemented section), `FLOW.md` (shell leg, redrawn diagram, corrected "entry point" prose), `TEST-CHECKLIST.md` (SH-1..SH-9), `ROLLBACK.md` (rollback point written *before* the change), `AGENTS.md` (subsystem row + run commands), `features/FEATURE-003.md` (new file), this entry.

### In progress / next
- **SH-9 is the one that matters and it is unrun:** with both keys set, hold Ctrl+Alt and confirm a real verdict becomes a real highlight that removes itself, then press twice in a row for `REFUSED`. Everything else about the shell is verified; the drawn verdict is not.
- Then: decide the Voice trigger move (`Pressed` → `Released`, record on press / submit on release) and retire `--question`.

### Risks & gotchas
- **The shell has no `--fake` flags on purpose**, so its offline coverage stops exactly where the real key gates stop. Everything past the gate — the visible highlight, `--no-overlay`, the `--duration-ms` effect, the `REFUSED` overlap path, `not-per-monitor-dpi-aware` — is unverified, not unverifiable. If those rows need to be green offline, the seam to add is a `tools/ShellSmoke` that injects fake legs into the shell's composition — *not* fake flags in the product.
- **A leg exception cannot be located to a pipeline stage from outside.** `ContextOrchestratorService` resets `Phase = Idle` in its catch-all before rethrowing, so `KEY ERROR (Vision)` and `KEY ERROR (Element Selection)` are distinguishable only by exception type. A phase diagnostic was added to the shell's leg-error paths, observed to print `Idle` for every thrown session, and **removed again** rather than shipped implying an answer it does not have. Changing that is the orchestrator's error contract to change, not a side effect of the shell.
- **`HotkeyState` is the chord state, not the hook state.** After a successful `Start()`, `State` is `Idle` (nothing held) while `IsRunning` is `True`. The shell prints both; a single-value line read as a failed start.
- **Watch the stale binary.** Running the shell in the same batch as its `dotnet build` executed the *previous* binary and briefly showed a diagnostic already deleted. Build, then run, then read.
- **Cancellation is observed, not forced.** `--cancel-ms 10` aborted at ~704 ms and `--cancel-ms 300` at ~572 ms: the token is only seen at an await boundary and UIA enumeration is not cancellable mid-flight. Do not read the requested ms as the abort latency.

### Files touched
- `src/HeyGanesh.App/HeyGanesh.App.csproj`, `Program.cs`, `NativeFns.cs`, `ShellHost.cs`, `ShellHost.Args.cs`, `ShellHost.Session.cs` — new.
- `docs/ai/features/FEATURE-003.md` — new ticket.
- `docs/ai/DECISIONS.md`, `ARCHITECTURE.md`, `FLOW.md`, `TEST-CHECKLIST.md`, `ROLLBACK.md`, `HANDOVER.md`, `AGENTS.md` — one decision, one module, one flow leg, nine check rows, one rollback point, one entry, four commands.
- No existing source file was edited.

### Verification run
- `dotnet build src\HeyGanesh.App -c Debug` -> `Build succeeded. 0 Warning(s) 0 Error(s)` (SH-1).
- `Select-String src\HeyGanesh.App\*.csproj -Pattern '<PackageReference'` -> no matches (SH-2).
- `HeyGanesh.App.exe --help` -> usage + exit-code contract, exit **0** (SH-3).
- `HeyGanesh.App.exe --once --question "where is the Save button"` (both keys unset) -> header with `keys … MISSING` and `dpi: per-monitor-v2 aware`, a real capture, `KEY ERROR (Element Selection) …`, exit **12** (SH-4).
- …and separately that day, cursor over a window with no interactive candidates -> ES failed, Vision ran once, `KEY ERROR (Vision)`, exit **12** — the fallback leg driven end-to-end through the shell.
- `HeyGanesh.App.exe --question "x" --seconds 6` -> `hook live: IsRunning=True (installed), chord state=Idle (nothing held)`, health block, `still running: False`, exit **0** (SH-5).
- `HeyGanesh.App.exe` with no flags -> `no --question was given…` + usage, exit **2** (SH-6).
- `HeyGanesh.App.exe --once --cancel-ms N --question "x"` for N ∈ {1,10,60,300} -> `CANCELLED … phase Idle`, exit **13** in all four (SH-7).
- `HeyGanesh.App.exe --once --no-dpi --question "x"` -> `dpi: awareness not claimed (--no-dpi)`, exit 12 at the key gate (SH-8, flag + header only).
- **Not run:** SH-9 (live verdict + highlight — needs both keys and a human hand), the `REFUSED` overlap path, `--no-overlay`.

## 2026-09-24 — Muse Spark (muse-spark) — Screen Overlay: dependency-free click-through highlight over `GuidanceResult`

- **Branch / commit:** `main` — working tree, uncommitted (prior Context Orchestrator work also still uncommitted).
- **Scope:** one logical change — build the Screen Overlay (temporary click-through `GuidanceResult` highlight, bounded duration, capture-excluded), its offline verification host, and the docs that record it. No WinUI, no voice, tutor, lesson-engine, notes or proxy work, and no edit to any existing source file.

### Done
- `src/HeyGanesh.ScreenOverlay/` — new module, zero `PackageReference`, project-refs ScreenCapture + ContextOrchestrator. Surface: `ScreenOverlayService` (`ShowAsync`, `HideAsync`, `IsShowing`, `VisibleDuration`, `Dispose`), `ScreenOverlayOptions` (bounded `VisibleDuration`, `RequirePerMonitorDpi`), `OverlayShowResult` (outcome + reason, never a position); internals `OverlayWindow`, `OverlayRenderer` (GDI), `OverlayPump`, `OverlayNative`/`OverlayPInvoke`, `OverlayDraw`.
- `tools/OverlaySmoke/` — verification host with canned offline verdicts (no capture, no keys, no network); `FindOwnWindows` PID-filters assertions so a foreign lingering window is logged, never asserted; exit codes 0/2/13.
- Docs: `DECISIONS.md` **DEC-011** (Win32-now/WinUI-shell-later, `HTTRANSPARENT` correction, exclusion hard gate), `FLOW.md` (overlay leg + step 4 in the diagram), `ARCHITECTURE.md` (implemented section), `TEST-CHECKLIST.md` (SO-1..SO-9, EX-5 ✅), `ROLLBACK.md` (rollback point), `AGENTS.md` (commands), `features/FEATURE-002.md` (new file).
- Two deviations from the approved plan, both user-directed revisions applied before/during build: WinUI branch resolved to dependency-free Win32 (no App SDK package), and click-through corrected to explicit `WM_NCHITTEST → HTTRANSPARENT` + `WS_EX_NOACTIVATE` (the plan's `WS_EX_TRANSPARENT`-alone gap). Also: `GuidanceResult` consumed directly (public per DEC-010, legs untouched, no abstractions assembly); `OverlayPump` is per-service-instance per the approved lifecycle section.

### In progress / next
- Nothing in this change is unfinished. The single next action: exercise multi-monitor placement and the coded `off-screen` / `no-box` / `not-per-monitor-dpi-aware` paths on real hardware (single 2560x1600 display here).

### Risks & gotchas
- **Cross-run window pollution is real:** parallel `dotnet run` invocations enumerate each other's windows via `FindWindowEx`. This bit twice mid-verification (stale `WARN … still enumerable` + false FAILs) before the host was PID-filtered. Always run OverlaySmoke scenarios **sequentially**, and never two at once.
- **`GetStockObject` is gdi32, not user32** — shipping it under user32 crashed the first SO-3 run (`EntryPointNotFoundException` in the paint path). Fixed; the comment at the declaration records it.
- **Stray edits reverted, not shipped:** an earlier batch wrote OverlaySmoke content into the ContextOrchestrator smoke host's `Program.cs`/`Args.cs`/`Fakes.cs` and overwrote the overlay csproj; all reverted byte-clean (verified: CO host rebuilds 0/0 and CO-3/CO-4 re-pass). Also reverted at once: the working agreement demands `docs/ai/` docs, but `CONSTRAINTS.md` is user-owned read-only — a same-batch edit there was caught and rolled back immediately, before any other edit.
- **`dotnet run --project tools/OverlaySmoke` from a stale shell double-reports the shell's own prompt text after output** (observed: trailing `\u001b\` + exit-1 noise on an otherwise PASS run). Re-running the scenario alone prints clean. Trust the scenario's own `expectation :` line and a fresh-shell re-run, not the wrapper noise.
- Pre-existing open items carried forward, **not** touched: Vision-internal doc/implementation mismatch behind CO-6; `ROLLBACK.md`'s stale placeholder row; multi-monitor ordering (CO-11) still untested.

### Files touched
- `src/HeyGanesh.ScreenOverlay/{HeyGanesh.ScreenOverlay.csproj, ScreenOverlayOptions.cs, OverlayShowResult.cs, OverlayNative.cs, OverlayPInvoke.cs, OverlayRenderer.cs, OverlayDraw.cs, OverlayWindow.cs, OverlayPump.cs, ScreenOverlayService.cs}` — the module.
- `tools/OverlaySmoke/{OverlaySmoke.csproj, Program.cs, OverlaySmokeHost.cs, OverlaySmokeHost.Args.cs, OverlaySmokeHost.Output.cs}` — the verification host.
- `AGENTS.md`, `docs/ai/{ARCHITECTURE.md, FLOW.md, DECISIONS.md, TEST-CHECKLIST.md, ROLLBACK.md, HANDOVER.md}`, `docs/ai/features/FEATURE-002.md` — the paper trail.

### Verification run
- `dotnet build src\\HeyGanesh.ScreenOverlay -c Debug` → **Build succeeded, 0 warnings, 0 errors**; `dotnet build tools\\OverlaySmoke -c Debug` → same (SO-1).
- `<PackageReference` scan over both new csproj → **no matches** (SO-2).
- `--found --duration-ms 1500` → `displayed True`, 1 window, ShownFor ~1.44 s, `PASS`, **exit 0** (SO-3).
- `--no-match` → `displayed False`, `reason=no-match`, `windows=0`, `PASS`, **exit 0** (SO-4).
- `--failed` → `displayed False`, `reason=failure` (+ CO-6-shape detail), `windows=0`, `PASS`, **exit 0** (SO-5).
- `--cancel-ms 300 --duration-ms 5000` → `displayed False`, `reason=cancelled`, `PASS`, **exit 13** (SO-6).
- `--assert-clickthrough` → 1 window inspected (styles + `WM_NCHITTEST → HTTRANSPARENT` + `WDA_EXCLUDEFROMCAPTURE` + never foreground), removed on hide, `PASS`, **exit 0** (SO-7, flips EX-5).
- `--burst` → second `ShowAsync` `REFUSED`, first `Displayed`, `PASS`, **exit 0** (SO-8).
- `--repeats 25` → GDI delta 0, handles delta ≤ 8, `PASS`, **exit 0** (SO-9).
- Regression: all five sibling modules + ContextOrchestratorSmoke build 0/0; CO-3/CO-4 re-pass; forbidden-API grep over the overlay module matches comment text only (no declarations).
## 2026-09-23 — Cline agent (Claude Sonnet 4.5) — Context Orchestrator: ES → Vision fallback on failure only, one normalized `GuidanceResult`

- **Branch / commit:** `main` @ `8824568` — working tree, uncommitted (`AGENTS.md` is untracked).
- **Scope:** one logical change — build the Context Orchestrator (hotkey trigger → capture → Element Selection → Vision only on a genuine primary failure → one normalized `GuidanceResult`), its offline verification host, and the docs that record it. No overlay, voice, tutor, lesson-engine, notes or proxy work, and no edit to any existing source file.

### Done
- `src/HeyGanesh.ContextOrchestrator/` — new module, zero `PackageReference`, project-refs ScreenCapture + ElementSelection + Vision (deliberately **no** GlobalHotkeyMonitor reference). Surface: `ContextOrchestratorService` (`ResolveAsync`, `HandleHotkeyPressedAsync`, `Phase`, `IsSessionActive`, `Dispose`), `ContextOrchestratorOptions` (`Target`, `RetainCaptures`), `GuidanceResult` + `GuidanceSource` / `GuidanceConfidence` / `GuidancePoint`; the mapping lives in `ContextOrchestratorService.Normalize.cs`.
- `tools/ContextOrchestratorSmoke/` — verification host with an offline fake ES client (modes: top candidate / none / unmapped / error) and a call-counting fake Vision client; exit codes 0/1/2/3/12/13.
- Docs: `DECISIONS.md` **DEC-010**, `FLOW.md` (orchestrator leg + diagram), `ARCHITECTURE.md` (module detail), `TEST-CHECKLIST.md` (CO-1..CO-11), `ROLLBACK.md` (rollback point), `AGENTS.md` (commands + the one invariant sentence DEC-010 makes stale), `features/FEATURE-001.md` (new file).
- Two deliberate deviations from the approved plan, both recorded in DEC-010: `GuidanceResult` is **public** (the next module is a different assembly — what DEC-009 forbids is a shared abstraction assembly / cross-leg reference, and neither exists), and the orchestrator does **not** subscribe to `Pressed` (the trigger arrives through `HandleHotkeyPressedAsync`, wired by the host).

### In progress / next
- Nothing in this change is unfinished. The single next action: verify the live end-to-end session once a TypeSafe key exists — `dotnet run --project tools/ContextOrchestratorSmoke -c Debug -- --question "Save"` with `TYPESAFE_API_KEY` and `DEEPSEEK_API_KEY` set (the ES live Jev leg is itself still unverified — see the ES note in TEST-CHECKLIST.md).

### Risks & gotchas
- **A fallback not-found arrives as `Failed = true`**: `VisionService.Reconcile` sets a failure reason for "the model inspected the image but could not locate the requested element", while `VisionResult.FailureReason`'s own XML doc says a valid not-found carries none. The orchestrator carries the leg's verdict verbatim rather than re-classifying it (CO-6). **Vision-internal doc/implementation mismatch, deliberately not touched** — different module, different decision, and out of this change's scope.
- **The orchestrator does not subscribe to `Pressed`.** The shell must wire `monitor.Pressed` → `HandleHotkeyPressedAsync(question)`. A committed smoke host cannot raise that event without injecting real keystrokes, which this repo keeps out of committed tooling (and `CONSTRAINTS.md` forbids the product acting on the user's behalf), and the question source (Voice) does not exist yet.
- A second trigger during a session **throws** `InvalidOperationException` — refused, never queued. A host that drops the returned task must tolerate that, and `Dispose()` cancels an in-flight session without awaiting it.
- Injected module services are caller-owned: the orchestrator never disposes `ScreenCaptureService` / `ElementSelectionService` / `VisionService`.
- Pre-existing doc inaccuracy noticed and **not** touched: `ROLLBACK.md`'s index still carries the placeholder row "*(no rollback points recorded yet)*" beneath two real rows, and its 2026-09-22 entry was never given an index row.

### Files touched
- `src/HeyGanesh.ContextOrchestrator/{HeyGanesh.ContextOrchestrator.csproj, ContextOrchestratorService.cs, ContextOrchestratorService.Normalize.cs, ContextOrchestratorOptions.cs, GuidanceResult.cs}` — the module.
- `tools/ContextOrchestratorSmoke/{ContextOrchestratorSmoke.csproj, Program.cs, ContextOrchestratorSmokeHost.cs, ContextOrchestratorSmokeHost.Args.cs, ContextOrchestratorSmokeHost.Fakes.cs, ContextOrchestratorSmokeHost.Output.cs}` — the verification host.
- `AGENTS.md`, `docs/ai/{ARCHITECTURE.md, FLOW.md, DECISIONS.md, TEST-CHECKLIST.md, ROLLBACK.md, HANDOVER.md}`, `docs/ai/features/FEATURE-001.md` — the paper trail.

### Verification run
- `dotnet build src\HeyGanesh.ContextOrchestrator -c Debug` → **Build succeeded, 0 warnings, 0 errors**; `dotnet build tools\ContextOrchestratorSmoke -c Debug` → same (CO-1).
- `<PackageReference` scan over both new csproj → **no matches** (CO-2).
- `--question "Save" --fake --max-candidates 40` → `source=ElementSelection`, `choice=c001`, `boxOnScreen 70x51+2423+0`, `center (2458,25)`, `confidence High`, `vision calls: 0`, `PASS`, **exit 0** (CO-3).
- `--fake-none` → `found=False`, `choice=none`, no FAILURE line, `vision calls: 0`, `PASS`, **exit 0** (CO-4 — the load-bearing row: `none` must not fall back).
- `--fake-unmapped` → primary `FAILURE: Jev chose option id 'c999' which is not in the offered manifest (40 candidates) -- refusing to fabricate coordinates.`, then `source=Vision`, `found=True`, `boxOnScreen 306x160+614+192`, `vision calls: 1`, **exit 0** (CO-5).
- `--fake-vision-none` → `source=Vision`, `FAILURE: the model inspected the image but could not locate the requested element.`, `confidence Low`, no box, `vision calls: 1`, **exit 0** (CO-6).
- `--cancel-ms 150` (fake ES delay auto-raised to 450 ms) → `CANCELLED`, `phase=Idle`, `vision calls: 0`, **exit 13** (CO-7).
- `--fake-error` → `API ERROR (Element Selection, propagated not swallowed)`, **exit 3** (CO-8).
- live, both keys unset → `KEY ERROR (Element Selection)`, **exit 12**, value never echoed (CO-9).
- `--fake --burst` → session 1 verdict, second trigger `REFUSED -- A guidance session is already in flight...`, `PASS`, **exit 0** (CO-10).
- `--fake --no-retain-captures` → `frames: 0 retained`, **exit 0**; default run → `frames: 1 retained` (CO-11; single-display machine, so cursor-first ordering is untested).
- No regression: GlobalHotkeyMonitor / ScreenCapture / ElementSelection / Vision all build with **0 warnings, 0 errors**, and ES-4 (`--list-only` with `MATCH (same physical-px space)`), ES-5 (`--fake`) and ES-6 (`--fake-none`) re-run with **exit 0** each.

## 2026-09-23 — Muse Spark (muse-spark) — Scope the "tests enforce these" claim on the invariants section

- **Branch / commit:** `main` @ `94a8af1` — working tree, uncommitted (`AGENTS.md` itself is untracked).
- **Scope:** one logical change, doc-only. The `AGENTS.md` invariants header claimed "tests enforce these" for all five bullets, but only four have checks behind them. Split the section into an enforced group and a design group; log the fix here. No code, no new check rows, no `CONSTRAINTS.md` contact.

### Done
- `AGENTS.md:56` — header is now `## Architecture invariants — keep them true`. The parenthetical claim is gone, so nothing in the section is implied to be automated by default.
- `AGENTS.md:58` — new group intro above the four: "Enforced by automated checks (rows in `docs/ai/TEST-CHECKLIST.md`):".
- `AGENTS.md:65` — new group intro above the fifth: "Design invariants — hold by review and the cited decisions; no automated check yet:".
- All five bullets carried over **verbatim** — no rewording, no reordering beyond the sibling bullet dropping to the second group. Its DEC-008/DEC-009 citations and the duplicated-`ConfidenceLevel` / 0.8-0.5-tier language are untouched.
- The exact row-per-bullet audit lives in "Verification run" below rather than inline in `AGENTS.md`: four per-bullet row citations in the onboarding file would add cross-file couplings that go stale on the next checklist renumbering.

### Backing for each bullet (audited by reading TEST-CHECKLIST.md this session, not recalled)
- **Zero external NuGet dependencies** → S-2 ✅, V-2 ✅, ES-3 ✅ (all "none").
- **Capture Exclusion Contract** → EX-1..EX-4 ✅. EX-5 is 🚧 and belongs outside that set: it checks the *future overlay's* application of the contract, not the mechanism.
- **Physical Pixel Coordinate Space** → ES-4 ✅ (live cross-check: `GetWindowRect` vs UIA root `MATCH (same physical-px space)`). SC-8 covers the same ground but is a manual host-print check.
- **API Key Isolation** → V-3 ✅ and ES-7 ✅ (key-absent `KEY ERROR`, exit 12, no echo).
- **ES/Vision siblings** → **no row, hence the design group.** Grounded instead by direct inspection this session: each module's `.csproj` references only `ScreenCapture`, with no cross `ProjectReference` in either direction, and zero cross-`using` directives.

### In progress / next
- **Owner decision still open:** give the sibling invariant a real check row (a review row asserting no cross-reference in either `.csproj`/`using` set, or a CI grep), or accept the "design invariants" label as its permanent status. This step stops the header from lying; it does not create enforcement.
- **Nothing else in `AGENTS.md` is now over-claimed** — the four enforced bullets each have at least one ✅ automated row (see "Backing for each bullet" above), which was the specific doubt this change was made to settle.
- Remaining ES gaps are still coverage, not breakage: live Jev leg (needs `TYPESAFE_API_KEY`), `--annotate`/`annotated.jpg`, and a human-judged hit-check (the Vision V-6 analogue). All three live in the `TEST-CHECKLIST.md` ES note.

### Risks & gotchas
- **EX-5 is 🚧, and that is not a hole in the enforced group.** EX-1..EX-4 test the `CaptureExclusion` mechanism itself; EX-5 checks the *future overlay's* application of the contract, which cannot pass until the overlay exists. If someone later "tidies" the grouping by moving Capture Exclusion down to the design group because EX-5 is unfinished, that would be a regression in accuracy — the mechanism is enforced today.
- **Any new invariant defaults to the design group.** A bullet earns its way into the enforced group only with a real ✅ row; the whole point of this change is that the section no longer assumes enforcement.
- **Links to the old heading anchor are broken if any exist.** The heading changed from `## Architecture invariants (tests enforce these — keep them true)`; GitHub's slug for it changed with it. Sweep found no in-repo link using the old anchor (see Verification), but bookmark-style external references (a chat, a note) would 404 silently.
- **`read_files` returned `[outdated]` twice for `AGENTS.md` this session** while shell reads were correct. When editing this file, verify with `Get-Content`/`Select-String` rather than trusting a cached read.
- **Mixed encodings:** `AGENTS.md` is BOM-less CRLF, `HANDOVER.md` is BOM + CRLF. Normalize to each file's own shape after editing or the diff becomes whole-file churn.

### Files touched
- `AGENTS.md` — header reworded, two group intros added, sibling bullet moved to the second group (all five bullets verbatim otherwise).
- `docs/ai/HANDOVER.md` — index row + this entry.

### Verification run
- Regroup in place, read back from disk: `56` header `## Architecture invariants — keep them true`; `58` enforced-group intro; `60-63` the four bullets; `65` design-group intro; `67` the sibling bullet; `69` `## Environment facts` (the following section, undisturbed).
- Row audit performed by reading `docs/ai/TEST-CHECKLIST.md` this session (not recalled): S-2 ✅, V-2 ✅, ES-3 ✅ (NuGet-zero); EX-1 ✅, EX-2 ✅, EX-3 ✅, EX-4 ✅, EX-5 🚧 (Capture Exclusion); SC-3 ✅ and SC-8 *manual* plus ES-4 ✅ (Physical Pixel); V-3 ✅ and ES-7 ✅ (Key Isolation). Exactly four ⛔/🚧 markers exist in the file — EX-5, V-6, V-7, V-8 — and none of them backs a bullet in the enforced group.
- Sibling-invariant basis, checked directly: `src/HeyGanesh.ElementSelection/*.csproj` and `src/HeyGanesh.Vision/*.csproj` each contain exactly one `ProjectReference`, and it is `..\HeyGanesh.ScreenCapture\...` in both — **no cross-reference in either direction**. Cross-`using` greps (`using HeyGanesh.Vision` in ES sources, `using HeyGanesh.ElementSelection` in Vision sources) both return **0**. The 21 "Vision" occurrences inside ES sources are comment prose, and 0 occurrences of "Element Selection"/"ElementSelection" exist in Vision sources.
- Old-claim sweep: `Select-String -Pattern 'tests enforce these'` across `AGENTS.md`, `ARCHITECTURE.md`, `FLOW.md`, `TEST-CHECKLIST.md`, `DECISIONS.md` → no hits outside this entry quoting it.
- Encoding: `AGENTS.md` `cr == lf`, `first3 = 23 20 41`, `bomcount = 0`; `HANDOVER.md` `first3 = EF BB BF` with `cr == lf`.
- HANDOVER heading set: all pre-existing `##` headings still present, this entry on top (10 total incl. `## Index`).

## 2026-09-23 — Muse Spark (muse-spark) — AGENTS.md sync to Element Selection reality + line-endings correction

- **Branch / commit:** `main` @ `94a8af1` — working tree, uncommitted (`AGENTS.md` itself is untracked).
- **Scope:** one logical change — sync root `AGENTS.md`, the file every agent reads first, from the pre-ES world to the state already recorded in `docs/ai/ARCHITECTURE.md`, `FLOW.md`, `TEST-CHECKLIST.md` and this log. Plus a one-line factual correction (line 67). No code, no other doc, no `CONSTRAINTS.md` contact.

### Done
- `AGENTS.md:10` — pipeline is now `hotkey -> Screen Capture -> Element Selection (PRIMARY; Vision is FALLBACK) -> Jev -> Tutor Engine -> Visual Lesson Engine -> Screen Overlay / Voice`.
- `AGENTS.md:20-21` — new `Element Selection` subsystem row (PRIMARY coordinate-detection path; "the entry point is `SelectAsync(question, target?, ct)`, not Vision's `LocateAsync`") and Vision's row role now reads "— FALLBACK to Element Selection".
- `AGENTS.md:38` — `dotnet build src/HeyGanesh.ElementSelection -c Debug` added to the core-module build list.
- `AGENTS.md:45-51` — ES verification-host block added: offline paths need no key, plus the explicit warning that the library and host must be built **SEQUENTIALLY** (in parallel they race on the shared `obj` DLL and fail with `error CS2012`, which reads like a code fault but is not), then `--list-only`, `--fake`, `--fake --fake-none`.
- `AGENTS.md:54` — Models paragraph extended: ES uses the Jev choice model via TypeSafe (`ModelId` `jev-1.13.0`); live ES runs require `TYPESAFE_API_KEY`, "reported as presence/length only and never as a value".
- `AGENTS.md:58` — zero-NuGet invariant assembly list now (`GlobalHotkeyMonitor`, `ScreenCapture`, `ElementSelection`, `Vision`).
- `AGENTS.md:61` — API-Key-Isolation seam list now (`IVisionClient`, `IJevClient`). Both this and the sibling bullet below go slightly past the enumerated plan for that step — they are the step's "invariants section describing an ES-less world" instruction, and were flagged to the owner rather than folded in silently.
- `AGENTS.md:62` — **new** invariant bullet: Element Selection and Vision are siblings and must never reference each other (primary `SelectAsync` / fallback `LocateAsync`, duplicated `ConfidenceLevel`, matching 0.8/0.5 tier, fallback decided by a future Jev confidence-gate — DEC-008/DEC-009).
- `AGENTS.md:67` — corrected this session: "UTF-8 line endings" → "UTF-8 encoding with CRLF line endings (some files carry a BOM — preserve each file's existing encoding when editing)". The old wording conflated encoding with line endings; both halves are now measured facts.
- **Ownership verdict, on the record:** `AGENTS.md` is untracked but **not** user-owned like `CONSTRAINTS.md`. Evidence: it shows as `?? AGENTS.md` while `CONSTRAINTS.md` is a tracked `M`; `git ls-files --error-unmatch AGENTS.md` answers "did not match any file(s) known to git"; a marker scan for `USER-OWNED|user-owned|never edit|only the project owner` over `AGENTS.md` returns zero hits, while `CONSTRAINTS.md` carries the explicit "USER-OWNED FILE. Cline reads this file only… only the project owner writes here" header. Untracked is not the same as user-owned — the ES module and smoke host are equally untracked and are worked normally. Nothing in `CONSTRAINTS.md` was touched by that session; this session touches only `AGENTS.md:67` and this log.

### In progress / next
- **Decide the fate of the new sibling bullet** (see "Risks & gotchas" below): either add a check row that actually enforces it, or reword the section header so it stops claiming universal test coverage. Owner's call; no code change is implied either way.
- **Nothing is blocked in ES.** Remaining ES gaps are coverage, not breakage: the live Jev leg (needs `TYPESAFE_API_KEY`), `--annotate`/`annotated.jpg`, and a human-judged hit-check (the analogue of Vision's V-6). All three are listed in the `TEST-CHECKLIST.md` ES note.
- **The primary/fallback composition now lives in four places inside `AGENTS.md`** (pipeline line, two table rows, invariant bullet) plus `ARCHITECTURE.md:14-15`, `FLOW.md:18-29` and `TEST-CHECKLIST.md`. A future reversal means coordinated edits across all of them — accepted as the cost of making the onboarding file correct, but worth remembering before anyone "simplifies" one of them.

### Risks & gotchas
- **The sibling-invariant bullet is not actually enforced by a test.** The section header says "Architecture invariants (tests enforce these — keep them true)", but the other four bullets each have real check rows (SC/EX/ES/V); "ES and Vision must not reference each other" rests on DEC-008/DEC-009 wording plus `TEST-CHECKLIST.md` ES-3's project-reference inspection. The header over-claims by exactly this one bullet until it gains a check row — do not treat the header as proof.
- **Console rendering lies about encoding.** PowerShell displays em-dashes from these UTF-8 files as `â€”` — a CP1252 console artifact, not file corruption. Judge encoding by byte probe, never by what the terminal shows.
- **PowerShell 5.1 has no `` `u{...} `` escape.** A probe written as `[regex]::Matches($s,"`u{2014}")` silently searches for the literal text `u{2014}` and reports 0, which reads as "the characters are missing" when they are fine. Use `[string][char]0x2014` or raw hex (`E28094`) — this produced a false negative during the sync session before it was caught.
- **Mixed encodings across the repo:** `HANDOVER.md` and the ES module files are BOM + CRLF; `AGENTS.md`, `TEST-CHECKLIST.md`, `ARCHITECTURE.md`, `Helpers2.cs` are BOM-less CRLF. Normalize to the file's own existing shape or the diff becomes whole-file churn.
- **`HANDOVER.md` insert mechanics:** anchoring an insert on the *previous* entry's heading text consumes it and orphans the body (this happened during the ES doc-sync session; caught by a heading-set check and repaired). Insert positionally, then verify the heading set.
- **Build the ES library and its host sequentially** — in parallel they collide on the shared `obj\...\HeyGanesh.ElementSelection.dll` with `error CS2012`.

### Files touched
- `AGENTS.md` — the seven sync edits above plus the line-67 correction; still BOM-less CRLF.
- `docs/ai/HANDOVER.md` — index row + this entry.

### Verification run
- `AGENTS.md` encoding after the sync: `bytes=12667 cr=226 lf=226 first3=23 20 41 bomcount=0` (BOM-less CRLF; up from 10984 bytes / cr=216 by exactly the ten added lines).
- Byte-level multibyte check (definitive): `U+2014 count = 12`, `E28094 pairs = 12`, `U+2192 count = 1`, `E28692 pairs = 1`, `U+FFFD count = 0`, `EFBFBD pairs = 0` — real UTF-8 em-dashes and the `ES→Vision` arrow, zero replacement characters.
- Content counts: 11 `Element Selection`/`ElementSelection` hits (lines 10, 20, 21, 38, 46, 49, 50, 51, 54, 58, 62), 3 `FALLBACK` hits, 1 `TYPESAFE_API_KEY` hit, all 12 pre-existing `##` headings still present.
- The commands the sync added were run sequentially and passed: `dotnet build src\HeyGanesh.ElementSelection -c Debug` → **Build succeeded, 0 Warning(s), 0 Error(s)**; `dotnet build tools\ElementSelectionSmoke -c Debug` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- Containment: `git status --short` shape unchanged across the sync session — same 7 `M docs/ai/*` files, same 3 `??` entries, nothing staged or committed, no `docs/ai/*` or `CONSTRAINTS.md` write.
- Line-67 correction: verified by reading the line back and by a stale-claim sweep (`Select-String -Path AGENTS.md -Pattern 'UTF-8 line endings'` → no hits).

## 2026-09-23 — Muse Spark (muse-spark) — ES doc sync to green + host `choice` print fix

- **Branch / commit:** `main` @ `94a8af1` — working tree, uncommitted.
- **Scope:** one step with one code change in it — flip the Element Selection docs from "blocked" to their verified-green state, and make the smoke host actually print the Jev `choice` on the not-found path (the ES-6 row promised output the host never produced). Nothing else.

### Done
- `docs/ai/ARCHITECTURE.md:39` — dropped the stale `; build currently blocked, see ES-1` clause. Heading is now `### Element Selection (implemented — the PRIMARY coordinate-detection path)`.
- `docs/ai/TEST-CHECKLIST.md` — ES-1/ES-2 `⛔ blocked` → `✅`; ES-4/ES-5/ES-6/ES-7 `🚧` → `✅`, each citing the output actually observed (an earlier audit had already moved ES-3 to ✅, so the whole ES table is now green). The 5-line ES-1 "blocked" note became a 1-line resolved note pointing at these HANDOVER entries, plus an explicit list of what is still **not** verified for ES.
- `tools/ElementSelectionSmoke/ElementSelectionSmokeHost.Helpers2.cs` — **the fix I chose: move the print, not the row.** The `choice` line moved out of `if (r.Found)` into `if (r.Choice is not null)`, so the honest no-match (`choice=none`) and the hallucination-guard refusal now both show the raw id. `selected` / `boxOnScreen` / `center` stay gated on `Found` (they are null otherwise). Output-only change, no logic touched.
- `docs/ai/HANDOVER.md` — index row + this entry.

### In progress / next
- **Nothing is blocked in ES any more.** The remaining ES gaps are *coverage*, not breakage: the live Jev leg (needs `TYPESAFE_API_KEY`), `--annotate`/`annotated.jpg`, and any human-judged hit-check (the analogue of Vision's V-6). These are recorded in the TEST-CHECKLIST note rather than as failures.
- The ES ranking path has only ever selected the top-ranked candidate (`FakeJevClient` picks the first non-`none` id), so "does Jev pick the *right* element among 120?" is genuinely untested — today's runs prove plumbing, not judgement.

### Risks & gotchas
- ES-5's run this session selected `Slider 'Volume'` for the question "Volume" — nice corroboration that lexical ranking works, but it also means the fake client can look smarter than it is: it was handed the answer. Do not read ES-5 as an accuracy check.
- `PrintResult` now emits `choice` on the failure paths too (hallucination guard sets `Choice` alongside `FailureReason`). Intended; a reader seeing `FAILURE` + `choice` + no box should read it as "Jev named an id that was never offered".
- Coordinates legitimately go negative (`center (1209,−978)` seen this session when the cursor's monitor sits above the primary). Consumers must not assume positive screen coordinates.
- Repo mixes encodings: the ES module files and `HANDOVER.md` are BOM + CRLF, while `Helpers2.cs`, `TEST-CHECKLIST.md` and `ARCHITECTURE.md` are BOM-less CRLF. Both were preserved (verified by byte probe after each edit) — pick the wrong one and the diff is whole-file churn.
- Build the library and the host **sequentially**: in parallel they race on the shared `obj\...\HeyGanesh.ElementSelection.dll` and fail with `error CS2012`, which reads like a code fault but is not.

## 2026-09-23 — GLM-5.3-Flash — Restore `BuildCandidateSet` (ES-1/ES-2 unblocked, ES-3..ES-7 verified live)

- **Branch / commit:** `main` @ `94a8af1` — working tree, uncommitted (the module itself is untracked).
- **Scope:** one logical change — reconstruct the missing `CandidateEnumerator.BuildCandidateSet` that `Walk.cs:45` calls, so the module compiles and the documented ES checks can actually be run. No other refactors.

### Done
- `CandidateEnumerator.Elements.cs` (was a 65-byte no-op file) — `BuildCandidateSet(UiaElementArray found, IntPtr hwnd, string title, PixelRect windowRect, PixelRect rootPixelRect)` → `CandidateSet`, plus `TryBuildCandidate`: the DEC-008 filter in cheap-first order (non-empty name → usable non-empty rectangle → on-screen → enabled → control type in `SelectionOptions.InteractableControlTypes` / `DefaultInteractableControlTypes`). Ids are deliberately left empty — `c001..` is assigned after ranking in `ElementSelectionService`, because an id must mean rank position, not tree position.
- `CandidateEnumerator.Reads.cs` (was a 63-byte no-op file) — fail-closed cached-property readers: `TryReadName`, `TryReadControlType` (accepts both the documented VT_I4 id and the measured VT_BSTR localized name, mapping back through `UiaInterop.ControlTypeNamesByName`), `TryReadBool` (VT_BOOL or VT_I4 0/1; anything else is "unknown" and fails closed), `TryReadRect`, and `ComputePositionHint` (3x3, "top-left" … "center" … "bottom-right") for the Jev option text.
- Per-element guard inside `BuildCandidateSet`: a node that goes stale mid-walk is skipped and counted in `TotalEnumerated`, never fatal.
- `docs/ai/HANDOVER.md` — the docs-session entry appended first (as instructed), then this entry.

### In progress / next
- **Doc sync, deliberately not done today:** `docs/ai/ARCHITECTURE.md:39` still says "build currently blocked, see ES-1"; in `docs/ai/TEST-CHECKLIST.md`, ES-1/ES-2 still read ⛔ and ES-4/5/6 still 🚧. All of ES-1..ES-7 except ES-3's trivial repass are now verified green, so those statuses are stale. This is the single next action.
- **ES-6's checklist wording is wrong about the host's output.** `PrintResult` prints `choice` only inside `if (r.Found)`, so the not-found path shows `found=False`, `confidence=Low` and `none=0.62` in the probabilities line but never prints `choice=none`. Correct the row (or make the host print the choice on that path) during the doc sync.

### Risks & gotchas
- `GetCachedPropertyValue` throws `COMException 0x80040201` (**UIA_E_ELEMENTNOTAVAILABLE**) for nodes that go stale during the walk — reproduced live on a 44,599-node browser tree. The first version of this restore let that exception abort the entire enumeration (unhandled crash, exit −532462766). The per-element guard in `BuildCandidateSet` is load-bearing: do not remove it, and do not push the catches back down into the readers where they would only cover some reads.
- Live enumeration is large: a YouTube page gave ~44.6k control-view elements, ~7.7k after the filter. `ElementSelectionService`'s 120 cap plus `CandidateRanker` is what keeps the Jev request inside the state budget — the raw set must never be sent.
- Coordinates can legitimately be **negative** (`center (1209,−978)` in one run) when a monitor sits above/left of the primary. Consumers must not assume positive screen coordinates.
- Run the two builds sequentially. In parallel they collide on the shared `obj\...\HeyGanesh.ElementSelection.dll` and fail with `error CS2012` — that failure is a build-ordering artefact, not a code fault.

### Files touched
- `src/HeyGanesh.ElementSelection/CandidateEnumerator.Elements.cs` — `BuildCandidateSet` + `TryBuildCandidate` + the per-element guard.
- `src/HeyGanesh.ElementSelection/CandidateEnumerator.Reads.cs` — the cached-property readers and the position hint.
- `docs/ai/HANDOVER.md` — the docs-session entry, then this entry.

### Verification run
- `dotnet build src\HeyGanesh.ElementSelection -c Debug` → **Build succeeded, 0 Warning(s), 0 Error(s)** (before: `CS0103` at `Walk.cs(45,20)`, 0 warnings / 1 error).
- `dotnet build tools\ElementSelectionSmoke -c Debug` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- ES-4 `--question "x" --list-only --foreground` → window `'(145) YouTube - Brave'`, `enumerated: control-view elements=44599  interactive after filter=7669`, `coordinate cross-check: GetWindowRect=2582x1550+-11+-11  UIA root=2582x1550+-11+-11  ->  MATCH (same physical-px space)`, exit **0**. Same command with the default cursor target → `elements=455  interactive after filter=30`, exit **0**.
- ES-5 `--question "Save" --fake --foreground` → `found=True`, `choice=c001`, `selected=Edit 'Enter keywords...' 1350x106+534+-1031`, `boxOnScreen` + `center` printed in global physical px, `confidence=High (jev=0.88, winner=0.91)`, exit **0**.
- ES-6 `--question "Save" --fake --fake-none --foreground` → `found=False`, `confidence=Low`, probabilities `none=0.62`, no box, no `FailureReason`, exit **0** — the honest no-match, not a failure.
- ES-7 live mode with no key → `KEY ERROR: TypeSafe API key not found. Set the TYPESAFE_API_KEY environment variable.`, exit **12**, key never echoed. No network call was made (the key is absent in this environment).
- ES-3 → `Select-String -Pattern '<PackageReference'` across both `.csproj` files → **0 hits**.
- Change containment: only the two stub files carry today's timestamp inside the module; every other module file and every smoke-host file still shows 9/22. Both edited files remain BOM + CRLF (`EF BB BF`, `cr == lf`).
## 2026-09-23 — GLM-5.3-Flash — Element Selection docs backfill (ARCHITECTURE / FLOW / TEST-CHECKLIST)

- **Branch / commit:** `main` @ `94a8af1` — working tree, uncommitted (docs only).
- **Scope:** document the already-written Element Selection module (untracked on disk) as the PRIMARY coordinate-detection path with Vision as FALLBACK, in the three working docs. No code was changed in this session.

### Done
- `docs/ai/ARCHITECTURE.md` — Element Selection added to the module list (Vision's line marked FALLBACK) and given a detail block: assembly, public surface `SelectAsync(question, target?, ct)` (explicitly **not** `LocateAsync` — that is Vision's entry point), the enumeration → rank/cap 120 → Jev choice → UIA BoundingRectangle pipeline, the `0.5·JevConfidence + 0.5·WinnerProbability` blend with 0.8/0.5 tiers, PRIMARY-vs-FALLBACK composition, and the guide-only boundary.
- `docs/ai/FLOW.md` — Element Selection leg added to the leg list and a real (partial) path drawn: `hotkey → Screen Capture → [enumerate → rank/cap 120 → Jev select → UIA rect] → unified target`, with the failed-result branch down to Vision. The "intentionally empty" placeholder is gone. The `Choice == "none"` no-match case is documented as **not** a fallback trigger.
- `docs/ai/TEST-CHECKLIST.md` — new `## Element Selection checks` section, ES-1..ES-7, mirroring the S-/SC-/V- column shape.

### In progress / next
- **ES-1/ES-2 are red.** `CandidateEnumerator.Walk.cs:45` calls `BuildCandidateSet(...)` and no definition exists anywhere in `src/HeyGanesh.ElementSelection/`: `dotnet build src\HeyGanesh.ElementSelection -c Debug` → **Build FAILED, 0 warnings, 1 error**, `error CS0103: The name 'BuildCandidateSet' does not exist in the current context`. This also blocks the smoke host (ES-2) and therefore ES-4/5/6. The failure is recorded in the ES-table note rather than papered over. Restoring that method is the single next action.

### Risks & gotchas
- `CandidateEnumerator.Elements.cs` and `CandidateEnumerator.Reads.cs` are **not cruft**: each holds only two `using` directives, nothing references either by name, and `CandidateEnumerator.Walk.cs`'s header comment ("partial by necessity") says the class was split to satisfy an editor size limit — an interrupted refactor that most likely lost the `BuildCandidateSet` body. Both were left in place; disposition is the owner's call.
- Every file in this set is **BOM + CRLF** (`EF BB BF`, `cr == lf`), unlike the LF-only `DECISIONS.md` recovery blob. Match that or the diff picks up whole-file churn.
- The tree is dirty: 7 modified docs plus the untracked `src/HeyGanesh.ElementSelection/` and `tools/ElementSelectionSmoke/`. Nothing was staged or committed, and `CONSTRAINTS.md` was not touched (user-owned).
- Still stale after this session: `AGENTS.md`'s subsystem table and build commands do not mention Element Selection at all.

### Files touched
- `docs/ai/ARCHITECTURE.md` — module list line + Element Selection detail block; Vision marked as fallback.
- `docs/ai/FLOW.md` — Element Selection leg, fallback rule, and the composed path diagram.
- `docs/ai/TEST-CHECKLIST.md` — ES-1..ES-7 rows plus the ES-1/ES-2 blocking note.
- `docs/ai/HANDOVER.md` — this entry.

### Verification run
- `git --no-pager diff --stat` → `ARCHITECTURE.md | 15 ++--`, `FLOW.md | 30 ++++++`, `TEST-CHECKLIST.md | 18 +++`; no code file modified.
- Byte probe on the three edited docs → `cr == lf` on each, `first = 35` (`#`, no BOM), bytes 6906 / 3377 / 7908.
- `dotnet build src\HeyGanesh.ElementSelection -c Debug` → **0 Warning(s), 1 Error(s)** (CS0103 `BuildCandidateSet`) — re-run after the docs were written, which is how ES-1/2 were confirmed blocked rather than assumed.
- ES anchors resolve against real `DECISIONS.md` headings; ES rows split to the same column count as the V-* rows.

## 2026-09-22 — Cline agent (model id not exposed to the agent) — DECISIONS.md recovery + Element Selection decisions

- **Branch / commit:** `main` — working tree, uncommitted (`docs/ai/DECISIONS.md` only).
- **Scope:** restore the corrupted/decayed architecture-decision log to a clean, byte-integrity-preserved 9-entry state and record the Element Selection module's primary/Vision-fallback decision (DEC-008), which the code already references.
- **Did:**
  - Found `2891b919` (`dec_head`) is decayed: DEC-005/003 bodies are orphaned at the tail of DEC-006/004 (verified `bydesign_count=2`). Rebasing onto the clean 7-entry `dec_7ent` reference — where DEC-005/003 live as proper entries (`bydesign=1`) — avoids the duplication the literal "restore 2891b919 + insert" plan would have produced (24 relocated orphan lines + 48 added).
  - Added **DEC-008** (Element Selection = primary UIA path; Vision = fallback; Jev `jev-1.13.0` client seam at `IJevClient.cs`; 120-candidate cap + none-option + hallucination guard in `SelectionOptions.cs`; threshold-free tiering in `Mapping.cs`; guide-only — never injects input; the `SetFocus` slot at `UiaInterop.Interfaces.cs:58` exists only for COM vtable alignment, never called) — line-verified against `ElementSelectionService.cs`, `Mapping.cs`, `SelectionResult.cs`, `IJevClient.cs`, `SelectionOptions.cs`, `UiaInterop.Interfaces.cs`.
  - Added **DEC-009** (uniform `SelectionResult`/`VisionResult` shape; cross-module unification + the Jev confidence-gate deferred to the Context Orchestrator) — grounded in `SelectionResult.cs`.
  - Appended DEC-008/009 index rows; all 9 anchors resolve.
  - Fixed one misquoted tier expression (`blended >= 0.8f ? … : blended >= 0.5f ? … Medium : Low`).
- **Left:** nothing — recovery complete and verified; no code changed.
- **Watch:** `core.autocrlf=true` + `.gitattributes: * text=auto` CRLF-normalizes the worktree on a hard checkout until `docs/ai/*.md` is pinned to `text eol=lf`; on-disk file is LF-only (cr=0) and the committed blob stores LF. `src/HeyGanesh.ElementSelection` exists on disk but is not in the `ARCHITECTURE.md` subsystem table or the documented build commands — out of build scope here.
- **Next:** `git add docs/ai/DECISIONS.md` (committed as a clean LF blob); optionally pin `docs/ai/*.md` eol in `.gitattributes`.
- **Verified:** `git diff --no-index --stat dec_7ent.md docs/ai/DECISIONS.md` → 48 insertions, 0 deletions; `bytes=29323, first=35 (no BOM), cr=0 (LF), emdash=106, gamma=0`; DEC-001..009 each once; 9 index rows; order strictly-decreasing newest-first.

## 2026-09-22 — GLM-5.3-Flash / Laguna S 2.1 — Vision / Coordinate Detection module + verification host

- **Branch / commit:** `main` — working tree changes, uncommitted.
- **Scope:** implement **Vision / Coordinate Detection** (DeepSeek V4.1 Flash grounding over ScreenCapture output) + `tools/VisionSmoke`, before any orchestrator, overlay, Jev, or proxy. Plan approved with no overrides (0–1000 grid, temp 0.7 / 2 samples, detail high).

### Done
- `src/HeyGanesh.Vision/`, `net8.0-windows10.0.22621.0`, **zero `PackageReference`**, project-ref to ScreenCapture: `VisionService` (`LocateAsync` — sampling, tolerant parse + retry, blank-frame short-circuit, `CoordinateMapper` back-mapping), `VisionResult`/`VisionSample` (`Found`, description, normalized/image/source/screen boxes, `Confidence` + `RawConfidence`/`Consistency`, diagnostics), `IVisionClient` seam, `DeepSeekVisionClient` (temporary direct path, key from `DEEPSEEK_API_KEY`), `VisionPrompt`, `VisionResponseParser`, `CoordinateMapper`, `VisionOptions`.
- `tools/VisionSmoke/`: capture-or-`--image` → locate → console result + `capture.jpg`/`annotated.jpg`; `--samples/--max-dim/--detail/--out/--fake`; DPI-v2 opt-in. `--fake` uses an in-harness fixed-box client; live mode calls DeepSeek directly; harness never echoes the key.
- DEC-006 (grid + confidence design; no resolution matching) and DEC-007 (seam + temporary direct-key risk).

### In progress / next
- **Live visual hit-check is yours:** `dotnet run --project tools\VisionSmoke -c Debug -- --question "where is the Start button" --samples 2` with `DEEPSEEK_API_KEY` set; open `out\annotated.jpg`; per-target screen-px error belongs in the next HANDOVER entry. Also: key-absent fail-fast (exit 12), not-found path (exit 0, `found=false`), `--samples 4` consistency movement.

### Risks & gotchas
- **No live call has been made from this tree yet** — grounding precision on dense desktop UI is unproven; bad numbers are a feasibility finding.
- Direct-key stopgap risk recorded in DEC-007 (env inheritance, direct transit, capture-visible key); dies with the proxy module.
- Screen shots go to a cloud API by design — redaction policy is an undecided CONSTRAINTS item, flagged not solved.
- DPI: screen-space output assumes a PerMonitorV2 host (standing risk, now load-bearing for coordinates).
- Single-monitor machine: multi-monitor fan-out is the future orchestrator's job; untested here.
- UAC Secure Desktop / RDP / elevated: inherited blank-frame path from capture; `WasBlankFrame` surfaces it.

### Files touched
- `src/HeyGanesh.Vision/*` (9 files, incl. `IVisionClient.cs` now holding the consolidated `VisionRequest`/`VisionRawResponse` shape); `tools/VisionSmoke/*` (5 files); `docs/ai/{DECISIONS,HANDOVER,ARCHITECTURE,FLOW,TEST-CHECKLIST}.md` (below).

### Verification run
- `dotnet build src\HeyGanesh.Vision -c Debug` → **0 warnings, 0 errors**; `dotnet build tools\VisionSmoke -c Debug` → **0 warnings, 0 errors**; no `PackageReference` in either csproj (comment mentions only).
- `--help` → usage, exit 0. Live mode with key absent → `KEY ERROR`, exit **12**. `--fake --image <prior capture>` → `found=True`, `normalized {0.240,0.120 0.120x0.100}`, `rectangle 153x80+307+96`, `confidence High (model=0.86, consistency=1, samples=2)`, `annotated.jpg` written, exit **0** — parse→map→annotate path proven without network or key.

## 2026-09-21 — GLM-5.3-Flash — Screen Capture module + verification host

- **Branch / commit:** `main` — working tree changes, uncommitted.
- **Scope:** implement the **Screen Capture** module — on-demand still frames of every monitor via `Windows.Graphics.Capture`, plus a runnable verification host, before any app shell, orchestrator or UI exists.
- **Done:**
  - `src/HeyGanesh.ScreenCapture/`, `net8.0-windows10.0.22621.0`, **zero `PackageReference`**:
    - `NativeMethods.cs` — `EnumDisplayMonitors`, `GetMonitorInfoW`, `GetCursorPos`/`MonitorFromPoint`, `D3D11CreateDevice`, `CreateDirect3D11DeviceFromDXGIDevice`, `SetWindowDisplayAffinity`/`WDA_EXCLUDEFROMCAPTURE`.
    - `Monitors.cs` — `PixelRect`, `MonitorInfo`, `Monitors` (enumerate, cursor monitor, capture order).
    - `CaptureExclusion.cs` — public `TryExcludeFromCapture` / `TryIncludeInCapture` contract for our own windows.
    - `CpuFrameReader.cs` — `SoftwareBitmap.CreateCopyFromSurfaceAsync` + blank-frame detection via the `CryptographicBuffer` byte[]↔IBuffer bridge.
    - `WgcFrameGrabber.cs` — one-shot `CreateFreeThreaded` pool + two-phase settle wait; `IGraphicsCaptureItemInterop` vtable order and GUIDs confirmed from Microsoft's own sample.
    - `JpegEncoder.cs` — `BitmapEncoder` JPEG, quality 0.8, Fant downscale to 1280 px, aspect preserved, never upscales, Linear fallback.
    - `ScreenCaptureService.cs` — public API: `EnumerateMonitors`, `GetCursorMonitor`, `IsCaptureSupported`, `CaptureMonitorAsync`, `CaptureAllMonitorsAsync`, with `ScreenCaptureOptions` / `MonitorCapture`.
  - `tools/ScreenCaptureSmoke/` — verification host: monitor table + cursor marker, capture-all → JPEGs, JPEG magic / dimension / aspect / content checks, A/B exclusion proof (decode JPEGs + pixel diff with a same-rectangle noise floor and a control region), `--move-cursor` cursor sweep, `--repeats` leak soak, DPI opt-in.
  - DEC-004 (capture design, zero deps, **corrected settle window**) and DEC-005 (self-exclusion contract + measured proof).
- **Evidence (this session, 2560x1600 @ 150% DPI, build 26200):**
  - `dotnet build` for the library and the host → **0 warnings, 0 errors**; no `PackageReference`.
  - Every monitor captured with valid JPEGs (e.g. 2560x1600 → 1280x800, ~80 KB, 97 %+ non-black). `CreateForMonitor` returns `S_OK`. Per-capture time ~0.7 s (was ~2.5 s before the settle fix).
  - A 25× capture loop leaves handle delta 1 and a flat managed heap.
  - Exclusion A/B on an owned white-window probe: **100 %** of the probe rectangle is white with affinity off; **100 %** of those pixels change with affinity on; noise floor **0 %**, control region **0.6 %** — i.e. exclusion is measured, not inferred. The console HWND is correctly refused (`ERROR_ACCESS_DENIED`), proving the OS enforces the per-process scope.
- **Risks & gotchas:**
  - **Settle correction vs the plan:** WGC delivers frames on screen *change*, so a still desktop offers one frame. Waiting for frame 2 wasted ~2.5 s and still used frame 1 — now a bounded 250 ms preference window (`SettleTimeout`) with the 2 s `FrameTimeout` kept as the hard deadline (DEC-004).
  - **HDR:** `B8G8R8A8` content is tone-mapped on HD-color displays (washed colours); acceptable for vision input.
  - **Yellow border / borderless:** clearing the capture border needs a packaged app + `graphicsCaptureWithoutBorder` capability + consented `RequestAccessAsync(Borderless)`; a non-packaged host's `IsBorderRequired=false` is silently ignored. Deferred to the packaged shell.
  - **RDP / Secure Desktop:** monitor capture items cannot be created under RDP, and nothing is captured on the UAC Secure Desktop.
  - **`CreateForMonitor` E_INVALIDARG** reported on some 26100 machines; `S_OK` here on 26200. Documented fallback: `TryCreateFromDisplayId` once the Windows App SDK shell exists.
  - **DPI:** the library never sets process DPI awareness (process-global side effect). The host must be PerMonitorV2; the verification host sets it itself. Coordinates and frames are physical pixels.
  - **Exclusion lifecycle:** `WDA_EXCLUDEFROMCAPTURE` is per window and must be re-applied whenever a window is recreated (per-monitor overlays, hot-plug, rebuild). The Screen Overlay must apply it to the real top-level HWND.
- **Files touched:** `src/HeyGanesh.ScreenCapture/*.cs` (+ `.csproj`); `tools/ScreenCaptureSmoke/{Program.cs,ScreenCaptureSmoke.csproj}`; `docs/ai/DECISIONS.md` (DEC-004, DEC-005).
- **Open:** Context Orchestrator still pending; Vision/Coordinate Detection is the next consumer of `MonitorCapture.Jpeg`.

## 2026-09-21 — GLM-5.3-Flash — Global Hotkey Monitor module (first code in the repo)

- **Branch / commit:** `main` @ `fc1be37` — working tree has new `src/`, `tools/`, `.gitignore`, `docs/` (all uncommitted)
- **Scope:** implement the **Global Hotkey Monitor** (system-wide, listen-only, modifier-only push-to-talk) with a runnable verification host, before any app shell, orchestrator or UI exists

### Done
- `src/HeyGanesh.GlobalHotkeyMonitor/` class library, `net8.0-windows`, **zero `PackageReference`**:
  - `NativeMethods.cs` — `WH_KEYBOARD_LL` P/Invoke, `SafeHookHandle`, `KBDLLHOOKSTRUCT`.
  - `HotkeyChord.cs` — `ChordTracker`, the modifier-only chord state machine.
  - `GlobalHotkeyMonitor.cs` — `HotkeyState`, `HotkeyMonitorOptions`, `GlobalHotkeyMonitor`.
- Public surface exactly as specified: `Pressed` / `Released` events, `State` (`Idle`/`Listening`), `Start()`/`Stop()`/`Dispose()`, plus diagnostics `HookCallbackCount`, `DroppedEventCount`, `MaxCallbackLatency`, `LastError`.
- `tools/HotkeySmoke/` — manual verification host: timestamped events to console + `smoke.log`, `--seconds=N` for unattended runs, summary (including max callback latency) on exit.
- `.gitignore` — `bin/`, `obj/`, `.vs/`, `*.user`, `smoke.log`. Added because this change is the first to produce build output in the repo; without it `git status` is drowned in artifacts.
- Environment: installed **.NET SDK 8.0.425** via `winget install Microsoft.DotNet.SDK.8`. The machine had a broken runtime-only `dotnet` (no `sdk\`, no `host\fxr\` → `dotnet` exited 0x80008083).

### In progress / next
- Nothing half-finished. The module is standalone and consumes nothing else. Next candidates: the Context Orchestrator (subscribe to `Pressed`/`Released`), or Screen Capture.

### Risks & gotchas
- **AltGr:** chord requires **left** Alt, so AltGr (LCtrl+RAlt on many layouts) can never complete it. Right Alt is deliberately inert (`RequireLeftAlt` default `true`).
- **UIPI / elevated focus:** no events while an elevated window has focus, or on the UAC Secure Desktop. Presents as "chord does nothing", never as stuck `Listening`.
- **Silent unhook** (Windows' version of macOS "tap disabled by timeout"): the OS removes LL hooks whose callbacks exceed `LowLevelHooksTimeout` (~300 ms) with no notification. Mitigation is the trivial callback + diagnostics; **auto-rehook deferred on purpose** (DEC-003).
- **Do not add work to `HookProc`.** Measured max callback latency 0.63 ms (first-call JIT dominated; `WarmUpHookPath()` mitigates). Budget is 300 ms.
- **Events fire on `HeyGanesh.HotkeyMonitor.Worker`**, not the UI thread — WinUI subscribers must marshal.
- **`GetAsyncKeyState` staleness:** never re-read the modifier belonging to the event currently being delivered; it is not guaranteed to have caught up (this was a real bug — see below).
- `dotnet` SDK is now installed **machine-wide** in `C:\Program Files\dotnet` (winget, no elevation needed); the earlier broken install was overwritten by a working one.

### Files touched
- `src/HeyGanesh.GlobalHotkeyMonitor/{HeyGanesh.GlobalHotkeyMonitor.csproj, NativeMethods.cs, HotkeyChord.cs, GlobalHotkeyMonitor.cs}` — the module.
- `tools/HotkeySmoke/{HotkeySmoke.csproj, Program.cs}` — manual verification host.
- `.gitignore` — build output hygiene.
- `docs/ai/HANDOVER.md`, `docs/ai/DECISIONS.md` — this entry; DEC-001 (hook architecture), DEC-002 (AltGr/chord rules), DEC-003 (rehook deferred).

### Verification run
- `dotnet build src\HeyGanesh.GlobalHotkeyMonitor\HeyGanesh.GlobalHotkeyMonitor.csproj -c Debug` → **Build succeeded, 0 warnings, 0 errors**.
- `dotnet build tools\HotkeySmoke\HotkeySmoke.csproj -c Debug` → **Build succeeded, 0 warnings, 0 errors**.
- `HeyGanesh.Tools.HotkeySmoke.exe --seconds=4` → hook live at `state=Idle`, clean stop, summary printed, `last error: none`, **exit 0** (`hook callbacks seen: 0` — no keys were pressed during the unattended run, as expected).
- **Throwaway SendInput verifier** (built in `%TEMP%\heyganesh-hotkeyverify`, deliberately *not* committed; injects only Ctrl/Alt/F23/F24 so no text can reach a focused window) → **24/24 checks passed, exit 0**:
  - modifier-only hold raises `Pressed` and `state=Listening`; release raises `Released` and `state=Idle`; no duplicate on modifier auto-repeat;
  - events observed on thread `HeyGanesh.HotkeyMonitor.Worker`;
  - **listen-only proven**: a second LL hook installed *behind* the monitor's still received every notification (`observer=4 monitor=4`) — only possible because the hook proc returns `CallNextHookEx`;
  - a non-modifier key while held ends the press, re-arms only after full release, and itself still reaches the downstream hook;
  - ordinary key presses raise nothing and do **not** poison the next chord (chord still fires immediately after typing);
  - rapid tap yields a clean `Pressed`/`Released` pair, never stuck `Listening`;
  - `dropped=0`, `maxCallbackLatency=0.63 ms`, `lastError=none`, clean `Stop()` and re-hook, `Dispose()` leaves `Idle`.
- **Two real bugs found and fixed by that run** (both would have shipped silently, and both are exactly the kind the manual harness alone would have missed):
  1. `ResyncFromSystem()` overwrote fresh event-derived modifier state with stale async key state, including for the key being delivered — `LAlt` was cleared on its own key-down, so the chord never completed. Fixed by `ResyncModifiersExcept(currentKey)`.
  2. `_dirty` was set by *any* non-modifier key-down and only cleared on a required-modifier key-up, so after typing in any other app the next chord needed a wasted second attempt. Fixed by dirtying only while a required modifier is already down.