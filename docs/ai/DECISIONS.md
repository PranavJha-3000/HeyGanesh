# DECISIONS — Architecture & Design Log

> Dated, **model-versioned** decision entries. One decision per entry, **newest first**.
> Use the DECISIONS entry template in [TEMPLATES.md](./TEMPLATES.md) §2.
> Entries are append-only: never rewrite a past decision — supersede it with a new entry
> that references the old one (`Supersedes: DEC-<nnn>`).

## Index

| ID | Date | Model / version | Decision | Status |
|----|------|-----------------|----------|--------|
| [DEC-017](#dec-017--2026-10-01--cline-agent-pixel-canary) | 2026-10-01 | Cline agent (Pixel Canary) | First live teaching provider: internal DeepSeekTutorClient (deepseek-flash, direct Chat Completions, DEEPSEEK_API_KEY at call time per DEC-007, fixed thinking:disabled) exposed only through TutorClientFactory.CreateDeepSeek → ITutorClient; verified offline through the exact public construction path via a loopback BaseUrl stub (no InternalsVisibleTo, no handler seam); TU-11/TU-12 | accepted |
| [DEC-016](#dec-016--2026-09-30--cline-agent-pixel-canary) | 2026-09-30 | Cline agent (Pixel Canary) | Teaching leg wired into the shell: non-Failed verdict maps to a TutorContext snapshot at the call edge (Failed short-circuits; no-match → Empty; Found carries the whole retained frame as a DownscaledFrame visual), the explanation is spoken verbatim behind a new `--no-speak`; seam composed by `CreateTutorClient()` which returns null — DEC-015's provider decision stays unmade | accepted |
| [DEC-015](#dec-015--2026-09-30--cline-agent-pixel-canary) | 2026-09-30 | Cline agent (Pixel Canary) | Tutor Engine as a provider-agnostic teaching layer: question-first prompt owned by the service, screen data as labelled untrusted input, opaque bounded JPEG visuals over a caller-supplied seam; no live client, no actuation, no disposal of the injected seam | accepted |
| [DEC-014](#dec-014--2026-09-30--cline-agent-model-id-not-exposed-to-the-agent) | 2026-09-30 | Cline agent (model id not exposed to the agent) | Push-to-talk shell wiring: press listens, release ends the utterance, committed transcript becomes the question; `--question` demoted to legacy/`--once`; `Failed` utterance → exit 1 | accepted |
| [DEC-013](#dec-013--2026-09-30--cline-agent-model-id-not-exposed-to-the-agent) | 2026-09-30 | Cline agent (model id not exposed to the agent) | Voice owns the integration boundary: Qwen Gateway GCP-7.0 client with cancel/discard suppression (no pre-generation suppression exists for live audio) + Fish TTS behind `ITtsClient` as WAV/44.1 kHz; external runtimes, not dependencies | accepted |
| [DEC-012](#dec-012--2026-09-29--pixel-canary) | 2026-09-29 | Pixel Canary | App Shell as a dependency-free console host that owns DPI awareness, the service graph and the hotkey→orchestrator→overlay wiring; WinUI 3 deferred again; no test doubles in product code | accepted |
| [DEC-011](#dec-011--2026-09-24--muse-spark) | 2026-09-24 | Muse Spark (muse-spark) | Screen Overlay as dependency-free Win32 (WinUI 3 deferred to the shell); explicit `WM_NCHITTEST → HTTRANSPARENT` + `WS_EX_NOACTIVATE` click-through; capture exclusion as a hard gate | accepted |
| [DEC-010](#dec-010--2026-09-23--cline-agent-claude-sonnet-45) | 2026-09-23 | Cline agent (Claude Sonnet 4.5) | Context Orchestrator composes ES→Vision behind one entry point with orchestrator-internal normalization; fallback on the failure signal only; trigger received via `HandleHotkeyPressedAsync` | accepted |
| [DEC-009](#dec-009--2026-09-22--cline-agent-model-id-not-exposed-to-the-agent) | 2026-09-22 | Cline agent (model id not exposed to the agent) | Primary/fallback result-type unification deferred to the Context Orchestrator; SelectionResult/VisionResult kept separate-but-uniform, fallback gate deferred to a future Jev component | accepted |
| [DEC-008](#dec-008--2026-09-22--cline-agent-model-id-not-exposed-to-the-agent) | 2026-09-22 | Cline agent (model id not exposed to the agent) | Element Selection primary (UIA+Jev jev-1.13.0, 120-cap, none-option + hallucination guard); Vision fallback | accepted |
| [DEC-007](#dec-007--2026-09-22--laguna-s-21) | 2026-09-22 | Laguna S 2.1 | Temporary direct DeepSeek key from `DEEPSEEK_API_KEY` env var behind an injectable `IVisionClient` seam, pending the API key security proxy | accepted |
| [DEC-006](#dec-006--2026-09-22--laguna-s-21) | 2026-09-22 | Laguna S 2.1 | Vision asks `deepseek-flash` for a 0..1000 grid box in JSON mode and derives consistency across samples; no standard-resolution preprocessing for DeepSeek | accepted |
| [DEC-005](#dec-005--2026-09-21--glm-53-flash) | 2026-09-21 | GLM-5.3-Flash | Self-exclusion via `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` on our own windows | accepted |
| [DEC-004](#dec-004--2026-09-21--glm-53-flash) | 2026-09-21 | GLM-5.3-Flash | Screen Capture on Windows.Graphics.Capture with zero dependencies; bounded settle window | accepted |
| [DEC-003](#dec-003--2026-09-21--glm-53-flash) | 2026-09-21 | GLM-5.3-Flash | No automatic re-hook watchdog for the silent-unhook case (deferred) | accepted |
| [DEC-002](#dec-002--2026-09-21--glm-53-flash) | 2026-09-21 | GLM-5.3-Flash | AltGr guard and chord rules for the default Ctrl+Alt hotkey | accepted |
| [DEC-001](#dec-001--2026-09-21--glm-53-flash) | 2026-09-21 | GLM-5.3-Flash | Low-level hook + pump/worker split + SafeHandle for the Global Hotkey Monitor | accepted |

---

<!-- DEC-<nnn> ENTRIES BELOW THIS LINE — newest first -->
## DEC-017 — 2026-10-01 — Cline agent (Pixel Canary)

**Decision — the first live teaching provider is DeepSeek `deepseek-flash`, direct, internal, and factory-fronted.** `DeepSeekTutorClient` (internal) sends one OpenAI-compatible Chat Completions request per turn — `model`, `messages` (system string; user plain string, or the verified `text`+`image_url` content-part array when a `TutorVisual` rides along, as a `data:image/jpeg;base64,` URL of the exact opaque bytes), `max_tokens` from the prompt's budget, `stream:false` — plus the fixed v1 decision `thinking: {"type": "disabled"}`. The bearer key is `DEEPSEEK_API_KEY`, read at call time only (DEC-007's temporary stopgap, extended through a second seam; the same variable Vision reads, which DEC-017 accepts as recorded coupling). `TutorService`, the shell and every other module are untouched; `TutorClientFactory.CreateDeepSeek(DeepSeekTutorOptions?) → ITutorClient` is the only new public construction API, and the shell's flip is that one call.

**Reasoning.**
- Fields were verified against the live DeepSeek API docs before writing (endpoint, message shapes, `image_url.detail` vocab, `thinking` object, usage echo) — nothing invented. `deepseek-flash` is the current name and the vision-capable model; `detail: high` keeps screen legibility for teaching.
- `thinking: {"type": "disabled"}` is fixed in the payload, not an options knob: the model currently defaults to thinking enabled at high effort, which spends unpredictable latency and the `max_tokens` budget on a reasoning trace for an answer meant to be SPOKEN. `MaxOutputTokens` must bound the lesson. Re-enabling is a future decision with measured evidence, not a flag to drift on.
- Internal client + public factory returning the seam: the concrete provider type never enters any consumer's compile-time world, which is DEC-015's "swapping providers touches construction only" enforced at the API level. A public concrete client, a handler parameter, or a provider enum/registry were all rejected; one static method, seam-typed.
- Verification tests the REAL construction path (TU-11): the smoke host builds via the factory and points `DeepSeekTutorOptions.BaseUrl` at a strictly-loopback `HttpListener` stub (free-port probe via `TcpListener` on :0, concrete-prefix bind with bounded retry — `HttpListener` cannot bind `:0`; no wildcard prefix, no admin/URLACL). No `InternalsVisibleTo`, no exposed `HttpMessageHandler` — an assembly-identity hole would verify a path production cannot take. Stub replies are canned in enqueue order (request N ← Nth response; last repeats), deterministically, and the row owns `DEEPSEEK_API_KEY` for its duration only, sentinel-never-printed.
- Error mapping lives in the client (`401/403`→auth status-only message, body never quoted; `429`→rate-limit; other→client+status with a ≤500-char snippet); everything else is left to the service's single `Translate` authority. Empty content crosses as empty text — the service owns calling it a failure.

**Rejected.** Thinking-on/server-defaults (spoken latency/budget); thinking as a knob (policy before evidence); shared DeepSeek HTTP helper assembly (DEC-009 — the ~40 transport lines duplicated from Vision are the accepted price); reusing `DeepSeekVisionClient` (JSON-mode grid prompt ≠ teaching); a second env key (rotation drift); shell flip in this change (scope — TU-12's live half is the gate); `response_format`/sampling/stream fields (the teaching contract has no such policy).

**Consequences.** `deepseek-flash` + `DEEPSEEK_API_KEY` are now committed names (DEC-015's neutrality ends by owner decision, not by drift). Vision and Tutor share one key and one concurrency pool — typed 429s on both seams, token accounting in both results. TU-11 pins the wire offline; TU-12's live half (and the injection row) remain the human-gated truth. The shell flip — `CreateTutorClient()` returning `TutorClientFactory.CreateDeepSeek()` — is now a one-line change, deliberately not taken here.

## DEC-016 — 2026-09-30 — Cline agent (Pixel Canary)

**Decision — the shell wires the teaching leg; it does not wire a provider.** After every non-Failed
verdict — reported, and drawn when enabled — `RunSessionAsync` runs `TeachAndSpeakAsync`: the shell
maps the `GuidanceResult` into the plain-data `TutorContext` exactly as DEC-015's remarks prescribed
(`!Found` maps to `TutorContext.Empty`; `Captures[0]` — the cursor's display, already JPEG ≤ 1280 px —
attaches as a `DownscaledFrame` visual when `Found`, by reference), calls `TutorService.ExplainAsync`,
and feeds `ExplanationText` **verbatim** into the already-composed `SpeakAsync` (skippable with the
new `--no-speak`). `HeyGanesh.App` gains its fifth ProjectReference (TutorEngine; zero NuGet
unchanged). The seam is composed by one method, `CreateTutorClient()`, which **returns null today**:
DEC-015's boundary holds — the change wires the leg, not a provider, and a clientless leg says so
after the verdict (skip line, exit 0) instead of pretending to teach or refusing the working path.

**Reasoning.**
- DEC-015 left exactly one forward-declaration point; a null-returning factory with zero call-site
  changes is the honest minimum. Fail-closed ("no client ⇒ refuse the turn") would have broken the
  product path that works today; shipping a real client would have smuggled the owner's provider
  decision in as plumbing.
- Short-circuiting on `Failed` at the call sites — branch first, map never — is the CO-6 guard, not
  mapping paranoia: Vision's model-side not-found arrives `Found=false` WITH `Failed=true`, and a
  "no verdict" is not mappable; teaching from absence is fabrication, and the overlay already
  refuses to draw on exactly this signal.
- The whole-frame visual is the only derivation that exists without an image pipeline: no decode, no
  crop, no re-encode, no new package — `Captures[0]` already carries the bytes and the declared
  dimensions. Crop-around-box needs a JPEG decoder and is explicitly deferred (FEATURE-006), not
  quietly built.
- Size caps are enforced by `TutorService` alone (pre-seam, `ArgumentException` = caller bug), and the
  shell maps that to exit 4 labeled `TUTOR MAPPING FAULT (shell bug)`. Pre-checking caps in the shell
  would give the caps a second enforcer, and second enforcers drift.
- The exit-code alphabet is reused, never extended: 0/12/3/13/4 mean what they meant, and the verdict
  is always reported (and drawn) before any leg code can replace it. `--no-speak` exists because once
  a live client lands, the scripted `--once` path must be runnable without taking the audio device —
  a real-surface flag, not a test double, so DEC-012's no-`--fake` rule holds.

**Rejected.**
- *Always-attach visual, including no-match turns* — spends visual budget on a verdict that says
  nothing was seen and muddies the on-screen/in-general split the system prompt defends.
- *Fire-and-forget `SpeakAsync`* — breaks Voice's one-voice-session rule (VO-9) and the
  shutdown/Drain ordering; sequential speak keeps both true.
- *Summarising or rewriting the explanation before speaking* — the Tutor owns the prose;
  `MaxExplanationChars` is already the contract with this consumer.
- *A shared-abstractions assembly for the confidence-tier switch* — DEC-009; three switch arms in
  the shell instead.
- *A `--no-tutor` kill-switch* — with a clientless seam it can test nothing, and once a client
  exists, "no lesson" is a provider decision, not a flag; `--no-speak` covers the device concern.

**Consequences.** Both turn modes share the leg (one `RunSessionAsync`, no drift). A long lesson now
extends the busy window — a second chord is refused while speech plays (VO-9 surfacing one layer up);
recorded, not fixed. Offline verification reaches the gates and legacy invariance only (SH-15..SH-17);
end-to-end teach-and-speak is a live-only row until the first `ITutorClient` exists — which the owner
owns. Cropped-region visuals and scripted mapping coverage belong to the future `ShellSmoke` host
(FEATURE-005), now the natural next verification artifact.

## DEC-015 — 2026-09-30 — Cline agent (Pixel Canary)

**Decision — the Tutor Engine is the teaching layer, and it ships with no provider.**
`HeyGanesh.TutorEngine` turns one `TutorRequest` — a question (the teaching task, primary) plus an
optional `TutorContext` (a plain-data snapshot of already-resolved grounding, possibly carrying one
opaque `TutorVisual` JPEG) — into one `TutorResult.ExplanationText` over an `ITutorClient` seam.
The seam ships with fakes only: **no provider, model, endpoint or key name is committed by this
change.** `TutorService` owns the system prompt; everything screen-derived enters as data explicitly
labelled `UNTRUSTED DATA`, and the rendered prompt forbids following instructions found inside it;
coordinates travel as labelled metadata and are banned from answer prose; the visual is capped
(512 KiB, 1568 px edges), crosses as separate binary, never base64-spliced into text. The service
implements no `IDisposable` and never disposes the injected client, and the assembly carries zero
ProjectReference and an empty P/Invoke surface — there is nothing in it that could touch a screen.

**Reasoning.**
- DEC-013 made the Tutor the answer authority — Voice suppresses the Gateway's own answers precisely
  because this module will speak. Question-first is the load-bearing rule: an explanation must be a
  real lesson when the screen resolved nothing (TU-5 asserts exactly that), or the module degrades
  into a caption engine and duplicates the legs.
- `GuidanceResult` is NOT passed — the shell maps it into a `TutorContext` snapshot at the call edge.
  A reference to ContextOrchestrator would weld the teacher to the session state machine and drag
  capture/leg types along; DEC-009's discipline (siblings stay siblings, the host wires the edges)
  applies to the teacher too.
- A provider-neutral seam rather than a live client: the owner has not chosen a teaching provider, and
  choosing one here would smuggle a decision in as plumbing. `TutorPrompt`'s shape
  (system text / user text / max tokens / separate visual) is the least-common-denominator of
  Chat-Completions-style APIs, so freezing it now is safe.
- The untrusted-data labelling is prompt-level mitigation, not a guarantee: fakes can prove the
  labelling, the carrying and the non-echo — they cannot prove a real model's resistance. The
  checklist records live injection-resistance as an OPEN row rather than pretending otherwise.

**Rejected.**
- *Passing `GuidanceResult` / referencing ContextOrchestrator* — welds the layers; see above.
- *Base64-splicing the visual into the user text* — defeats byte-cap auditability and makes the text
  channels unscannable for secrets; the seam carries bytes as its own field instead.
- *Silently downscaling an over-cap visual* — quietly teaching a different picture than the caller
  cropped is drift; over-cap throws `ArgumentException` (caller bug) instead, named per option.
- *Shipping a live client with a chosen model now* — the decision belongs to the owner; when it lands
  it follows the DEC-007 stopgap pattern at call time. No field in this assembly can hold a
  credential (TU-8), and the service deliberately reads none itself.
- *A `TutorService : IDisposable` disposing the client* — the service never created the seam;
  disposal belongs to whoever opened it (TU-9 pins the absence via reflection, because the XML docs
  legitimately *talk about* disposal and would false-fail a text scan).

**Consequences.** `SpeakAsync` still has no caller: mapping `TutorResult → SpeakRequest` (and
`GuidanceResult → TutorContext`) in the shell is a separate change, filed with FEATURE-006; Jev's
position "before the Tutor Engine call" stays open. TU-1..TU-10 are green offline; every claim about
model behaviour is open by construction. `TutorOptions`' caps (1600-char answer, 500 output tokens,
400-char untrusted-description clamp, 512 KiB/1568 px visual) become shell config decisions at wiring
time.

## DEC-014 — 2026-09-30 — Cline agent (model id not exposed to the agent)

**Decision — push-to-talk shell wiring.** The App Shell composes one `VoiceService` and a chord press
opens the microphone: `Pressed → TriggerPump.OnPressed → VoiceService.ListenOnceAsync (on the pool)`;
`Released → TriggerPump.OnReleased → end the utterance`; a committed `UtteranceResult.FinalText` becomes
`options.Question` and flows into the one unchanged `HandleHotkeyPressedAsync → ShowAsync` path.
`--question` is demoted to a legacy/verification flag (ignored with a warning in hooked mode; still
deterministic under `--once`), and `--once` without it runs a single live listen. `SpeakAsync` is
composed but never called, because the Tutor Engine that would supply its text does not exist.

**Reasoning.**
- The trigger point moves, exactly as FEATURE-003 filed it: with speech, the chord's *press* starts an
  utterance and its *release* ends it. Keeping press as "resolve now" would ask the Gateway to commit
  an empty transcript.
- The orchestrator, overlay and legs keep their contracts untouched. Only the question's origin changes,
  so no screen, cursor, capture or fallback behaviour can shift (verified: `--once --question` output is
  byte-identical to before, and the ContextOrchestrator/Overlay/VoiceSmoke rows still pass).
- Voice stays a replaceable seam: the shell depends on `VoiceService`, never on `QwenGatewayClient`,
  `FishTtsClient` or any wire type.

**Limitation, accepted and recorded — "release finalises" can only mean "end the utterance".** Voice's
API (DEC-013) is one `ListenOnceAsync` per utterance with no force-commit call, and this task freezes
the Voice module, so the shell cannot make the Gateway commit early. Releasing before the transcript
commits therefore cancels the listen: the turn is abandoned cleanly (exit 0, nothing resolved, no
capture spent). Holding the chord through the sentence — the normal case — is the contract.

**Exit-code mapping (the one deliberate deviation).** `UtteranceResult.Failure` is a *returned* reason
string, not a typed exception, so the shell cannot recover 12-vs-3 without sniffing text, and Voice is
frozen so no typed channel can be added. A failed listen is therefore **exit 1** ("no transcript was
produced"), never a fabricated verdict; thrown `VoiceAuthenticationException`/`VoiceClientException`
still map to 12/3. A release-abandoned turn is exit 0 (a user action is not Ctrl+C); shutdown during a
listen is 13. Silence is a verdict: `IsNoSpeech` never reaches the orchestrator, so no capture is spent.

**Rejected.**
- *Force-commit on release* — needs a Voice-module change and a second unverified protocol claim; the
  Gateway's `smart_turn` commit is server-side. Rejected in favour of the accepted limitation above.
- *Delete `--question`* — would break SH-4/SH-7/SH-8, the only scripted coverage the shell has (DEC-012
  forbids `--fake` flags in the product host). Demoted, not deleted.
- *Queue a second trigger instead of refusing it* — contradicts CO-10/SO-8 and would open two
  microphones against one Gateway owner slot (DEC-013: GCP takes one active client per owner).
- *Inject keystrokes for an automated chord test* — forbidden by DEC-010; a `ShellSmoke` host with an
  injectable trigger is the correct follow-up, filed in FEATURE-005.
- *A second CTS per phase (listen then session)* — needless: the resolve half already links its own
  token inside `RunSessionAsync`, and one source per turn keeps `OnReleased` able to cancel the listen.

**Consequences.** Teardown gains a step (hotkey → voice → overlay → …) because a draining listen must
never outlive its `VoiceService`. The header prints the voice keys' presence, not values. Press/release
timing, the IsNoSpeech short-circuit and REFUSED-during-listen are human-gated until a `ShellSmoke` host
exists; the failure/cancel/refusal mappings above are exercised offline.


## DEC-013 — 2026-09-30 — Cline agent (model id not exposed to the agent)

- **Status:** accepted
- **Decision:** The Voice module (`src/HeyGanesh.Voice/`) owns the entire speech integration boundary behind three seams — `IVoiceListener` (speech in), `ITtsClient` (speech out), `IAudioOutput` (speaker) — with **both external runtimes kept out of the dependency graph**: the qwen-audio-agent Gateway is an operator-run service spoken to over its **Gateway Client Protocol (wire 7.0)** on a WebSocket (`session.hello` → `session.ready`, `input_audio_buffer.append` for microphone frames, `transcript.*` for text), and Fish Audio is a plain HTTPS `POST /v1/tts` behind `FishTtsClient`. Zero `PackageReference`; microphone and speaker are owned `winmm.dll` P/Invoke. **The Qwen frontend is never trusted to answer:** `transcript.final` with `role: user` is the only frame that becomes a question; every assistant-side frame is cancelled/discarded and counted in `GatewaySuppressionLedger`; a `task.*` Backend Agent event aborts the listen (`AGENT_PROTOCOL=none` is asserted at the health endpoint when reported, and enforced at runtime either way). **Fish format is `wav` @ 44.1 kHz** — the v1 enum is exactly `wav|pcm|mp3|opus`, and WAV is the only choice playable with zero decoder dependencies.

### Context / alternatives considered
- **Pre-generation suppression for live audio** — investigated against the Gateway/realtime interface before accepting generate-then-cancel: `session.hello` `connection.output_enabled`/`text_only` are client capability/presentation preferences, not generation gates; AgentDelivery's `handle`/`context`/`respond` modes apply to typed Client Events (`client.event.publish`), not to live audio appended via `input_audio_buffer.append`; sleep/`input.suspend`/`input.mute` are capture-side; `conversation.item.create` has no verified client-callable transcribe-without-reply semantic (the `contextOnly` concept belongs to the Gateway→provider adapter, not to GCP); prompt-level "stay silent" is advisory, not enforced. **No supported pre-generation suppression exists** — so cancel/discard is the design, marked as protocol limitation L-1 rather than a preference.
- Option A — treat `qwen-audio-agent` as a pure STT engine (first plan draft). Option B — in-process Qwen SDK/NuGet. Option C — invented NDJSON/stdio `partial|final|no_speech|error` protocol over a self-chosen localhost socket (first plan draft). Option D — Fish realtime-v3 MessagePack WebSocket. Option E — `mp3`/`opus` output with a decoder package. Option F — hold the Gateway socket open between utterances.

### Chosen because
- GCP 7.0 event names are published and version-negotiated (`session.ready` may state `protocolVersion`; a major mismatch refuses), so the module is pinned to a contract it can assert offline rather than a protocol it invented.
- Cancel/discard, enforced in HeyGanesh code, is the only Tutor-authority guarantee that survives a Gateway that reports nothing — which is why `GatewaySuppressionLedger` is pure and replay-tested (VO-5).
- WAV + `winmm` keeps the zero-NuGet invariant: `WavPcmReader` validates RIFF/PCM16/mono loudly, `WinMmAudioOutput` plays it with `waveOut*` (`CALLBACK_EVENT = 0x00050000` — the `0x00010000` window-callback value was a real bug caught by the smoke row).
- Per-utterance transport (`connect → hello → listen → close`) matches press-to-talk and bounds the GCP one-active-client-per-owner takeover to the utterance itself.

### Rejected because
- Option A is false: `AGENT_PROTOCOL=none` disables only the Backend Agent; the DashScope realtime frontend still converses (text+audio output, function calling), so a "pure STT" reading would let Qwen answer academic questions by accident.
- Option B violates the zero-dependency invariant and makes "backend disabled" a review promise instead of an asserted boundary.
- Option C couples HeyGanesh to a protocol the upstream repo does not define; it would break on the next upstream release and could never be verified against the real service.
- Option D adds a second stateful protocol (msgpack subprotocol + ticket auth) for no capability this module needs.
- Option E (mp3/opus) requires decoder packages; raw `pcm` has no header to validate. Option F keeps TUI/WebUI clients evicted between questions for no benefit.

### Consequences
- **L-1 (accepted):** each utterance may cost one wasted frontend generation. Its *effects* are bounded — no assistant bytes reach the speaker, overlay, or Context pipeline — but HeyGanesh prevents the effect, not the occurrence, because no supported pre-generation switch exists for live audio.
- HTTP `ResponseHeadersRead` is **transport streaming only**; no incremental-generation/first-audio claim is made for Fish `POST /v1/tts` (the verified contract does not guarantee one).
- `FISH_AUDIO_API_KEY` extends DEC-007's temporary direct-key arrangement (env, read at call time, presence/length only); the Gateway's provider key never crosses the Gateway Client Protocol, and `QwenGatewayOptions.SafeEndpoint` strips path/query so an endpoint can never leak a token into a log.
- The App Shell still takes `--question`; wiring `Released`-submits and retiring the flag is the next step (FEATURE-003's filed follow-up) and was deliberately not mixed into this change.

## DEC-012 — 2026-09-29 — Pixel Canary

- **Status:** accepted
- **Decision:** The App Shell is `src/HeyGanesh.App/` — an `OutputType=Exe` console host on `net8.0-windows10.0.22621.0` with **zero `PackageReference`**, project-referencing GlobalHotkeyMonitor, ContextOrchestrator and ScreenOverlay. It owns the three things no module may own: process-wide PerMonitorV2 DPI awareness, the single `ScreenCaptureService` (shared into `ScreenOverlayService` for monitor topology), and the wiring `GlobalHotkeyMonitor.Pressed` → `ContextOrchestratorService.HandleHotkeyPressedAsync` → `ScreenOverlayService.ShowAsync`. It renders no window of its own. Windows App SDK / WinUI 3 is deferred again, and **test doubles stay out of product code**: the shell has no `--fake` flags, so its offline coverage stops exactly where the real key gates stop.

### Context / alternatives considered
- Option A — WinUI 3 / Windows App SDK now, the "eventual shell direction" DEC-011 named.
- Option B — WPF (`<UseWPF>true</UseWPF>`): no NuGet needed, since WPF ships in the Windows Desktop runtime.
- Option C — dependency-free console host now, UI framework deferred until something actually needs a window.
- Option D — put the shell in `tools/` as `ShellSmoke` rather than `src/`.
- Option E — carry `--fake-jev` / `--fake-vision` flags so the whole chain could be verified without keys.

### Chosen because
- Option C is the smallest step that closes the gap DEC-010 left open ("Both gaps close in the app shell (Voice pipeline + this wiring)"), and it needs no dependency, no packaging story, and no UI thread. Its correctness is inspectable in one place: the wiring is the same method for `--once` (scriptable, no keystrokes) and for a real chord, so the deterministic path and the human path cannot drift.

### Rejected because
- Option A — requires `Microsoft.WindowsAppSDK`, this repo's first external package, and a packaging/self-contained-runtime decision DEC-011 already recorded as unresolved (the capture border needs a *packaged* app; `TryCreateFromDisplayId` is the documented fallback "once the Windows App SDK shell exists"). It also puts an activation-taking XAML window next to an overlay whose entire contract is `WS_EX_NOACTIVATE` + `HTTRANSPARENT`. Deferred, not repudiated — DEC-011 still names it.
- Option B — no NuGet, but it adds `Application`/dispatcher lifetime and an STA UI for a process that draws nothing today; the overlay is already the UI and it is Win32.
- Option D — the wiring here is product behaviour. A smoke host cannot legitimately own the process's DPI awareness or the hotkey subscription, and hosting it in `tools/` would have meant duplicating the very wiring the shell exists to own.
- Option E — shipping test doubles inside the product to make its own tests easier is the wrong trade. The cost is real and is recorded honestly instead: with no keys, the shell proves composition, the trigger seam, key fail-fast, cancellation and lifecycle, but **not** a drawn verdict.

### Consequences
- **The shell's own entry point is also its verification path.** `--once` makes the composition exercisable by a script with no microphone, no keystrokes and (until a key is missing) no human — which is why the `REFUSED` overlap path and a real chord remain unverified: they are the only two behaviours that need a person.
- **WinUI 3 remains unbuilt, now twice deferred.** It needs its own decision entry when something genuinely requires a window; the capture-border and packaged-identity questions travel with it.
- **The question source is a flag.** `--question` is required rather than defaulted, so the Voice pipeline cannot silently be replaced by a constant. When Voice lands, the trigger should move from `Pressed` to `Released` (record on press, submit the transcript on release) — recorded as an open item in [HANDOVER.md](./HANDOVER.md).
- **A leg exception cannot be located to a pipeline stage from outside.** `ContextOrchestratorService` resets `Phase` to `Idle` in its catch-all before rethrowing, so a `KEY ERROR (Vision)` and a `KEY ERROR (Element Selection)` are distinguishable only by which exception type arrived — not by phase. The shell prints what it can and claims nothing more; fixing it would mean changing the orchestrator's error contract, which is out of scope here.
- **Nothing in any module changed.** This is a new-file-only change; every behaviour the shell exercises was already verified by CO-1..CO-11 and SO-1..SO-9.

## DEC-011 — 2026-09-24 — Muse Spark (muse-spark)

- **Status:** accepted
- **Decision:** The Screen Overlay is dependency-free Win32 C# in `src/HeyGanesh.ScreenOverlay/` — BCL plus user32/gdi32 P/Invoke, zero `PackageReference` — consuming `GuidanceResult` directly. Windows App SDK / WinUI 3 stays the eventual *shell* direction, not this module. Every overlay window is a borderless `WS_POPUP` layered window carrying `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_TOOLWINDOW`, answering `WM_NCHITTEST` with `HTTRANSPARENT` for every point (`WS_EX_TRANSPARENT` alone is insufficient for a top-level window) and `WM_MOUSEACTIVATE` with `MA_NOACTIVATE`, so input falls through and activation is impossible. Capture exclusion is a hard gate: a window the OS still photographs is destroyed, never shown. `Found=false` and any `Failed` verdict draw nothing. No `SendInput`, no cursor/focus APIs, no injection, no UIA writes exist in the assembly.

### Context / alternatives considered
- Option A — WinUI 3 / Windows App SDK now (what the original overlay brief assumed).
- Option B — dependency-free Win32 now (layered windows + GDI), WinUI 3 deferred to the packaged shell.
- Option C — `WS_EX_TRANSPARENT` alone as the click-through mechanism.
- Option D — accept `PixelRect`/raw coordinates or a new shared `OverlayTarget` type instead of `GuidanceResult`.
- Option E — queue overlapping shows instead of refusing the second.

### Chosen because
- Option B keeps the zero-NuGet invariant intact with the smallest blast radius: it is the same shape as every existing module and host, satisfies "Windows-native C#" without a packaging story, and leaves the WinUI 3 shell direction untouched. The original brief's WinUI-vs-zero-NuGet branch is resolved by this entry, not left open.

### Rejected because
- Option A — requires the Windows App SDK NuGet package inside a core module, violating the enforced zero-`PackageReference` invariant, and drags in packaging/deployment requirements for a module whose job is one temporary highlight.
- Option C — `WS_EX_TRANSPARENT` is a painting/hit-test hint whose behaviour for top-level windows has varied; returning `HTTRANSPARENT` from `WM_NCHITTEST` is the explicit, authoritative statement. The correction's own history (user-flagged mid-build) is why the smoke host asserts it from the OS side.
- Option D — a raw-rect overload would create a second de-facto coordinate contract bypassing the `Found` gate; a shared type or `HeyGanesh.Abstractions` is what DEC-009/DEC-010 forbid.
- Option E — two simultaneous pointers are ambiguous output; the orchestrator's burst guard (CO-10) sets the precedent: refuse, don't queue.

### Consequences
- **Multi-monitor clipping math ships single-display-verified only** (this machine reports one 2560x1600 display): negative-origin handling and per-window clipping are coded but the second-display half is asserted synthetically, not measured.
- **`Displayed=true` requires the full duration:** an early end (cancel, hide, dispose) reports `Displayed=false` even if a window appeared briefly — windows are always removed before the result returns.
- **Stale coordinates are inherent, not tracked:** the screen may change between `ResolveAsync` and `ShowAsync`; the overlay shows the verdict it was given, never re-resolves.
- **Still open:** WinUI 3 shell integration, any error *presentation* (this module reports `failure`, the future shell decides what the user sees), confidence-gated display (tiers are carried through `GuidanceResult`, never gated here).

## DEC-010 — 2026-09-23 — Cline agent (Claude Sonnet 4.5)

- **Status:** accepted
- **Decision:** The Context Orchestrator composes the two coordinate legs behind one entry point and one result type. `ContextOrchestratorService.ResolveAsync(question, ct)` opens a session: capture every monitor once (cursor's display first) → PRIMARY `ElementSelectionService.SelectAsync` → FALLBACK `VisionService.LocateAsync` on `captures[0]` **only** when the primary result is `Failed` (`FailureReason != null`) → one normalized `GuidanceResult`. `Choice == "none"` (`Found = false`, no `FailureReason`) is a verdict, not a failure, and never reaches Vision. The normalization (`SelectionResult`/`VisionResult` → `GuidanceResult`) lives in the orchestrator assembly only: `GuidanceResult`, `GuidanceSource`, `GuidanceConfidence`, `GuidancePoint` are new orchestrator-owned types, the legs' types and their duplicated `ConfidenceLevel` are untouched, no `HeyGanesh.Abstractions` exists, and no threshold is applied anywhere. This executes DEC-009's deferral rather than superseding it.
- **Also decided (trigger seam):** the orchestrator does **not** subscribe to `GlobalHotkeyMonitor.Pressed`. The trigger arrives through `HandleHotkeyPressedAsync(question, ct)` and the host maps the event to it; the question source (Voice pipeline) does not exist yet, and `Pressed` cannot be raised by a committed verification host without injecting real keystrokes into the user's session. A trigger arriving while a session is in flight is refused, not queued.

### Context / alternatives considered
- Option A — a shared `HeyGanesh.Abstractions` assembly holding one result type that both legs populate (the shape DEC-009 deferred "until the Context Orchestrator exists").
- Option B — the orchestrator calls Vision from inside the Element Selection failure path, i.e. the legs reference each other.
- Option C — gate on confidence too: fall back unless the primary leg returned High, or re-blend the two legs' scores.
- Option D — keep both result types and normalize them privately in the orchestrator, falling back on the failure signal only.
- Option E — the orchestrator owns a `GlobalHotkeyMonitor` and subscribes to `Pressed` itself.

### Chosen because
- Option D satisfies DEC-009's intent with the smallest blast radius, and DEC-009's own Consequences section names this exact policy ("empty tree / unmapped choice / UIA access-denied / no-choice → Vision; Confidence == High and a non-degraded verdict accept Element Selection"). The uniform shape plus the preserved raw signals mean the future Jev gate can be added without re-plumbing either leg.

### Rejected because
- Option A — it forces a cross-module reference that `SelectionResult.cs` forbids today, and would install a resolver in a neutral assembly before its consumer needs it; DEC-009 reserves those names for a deliberate decision, not a side effect of wiring.
- Option B — inverts the DEC-008/DEC-009 sibling boundary and puts fallback policy inside a leg.
- Option C — would invent a threshold and a fallback rule the legs deliberately do not own (both are threshold-free by contract; the tier is classification, not a gate). Nothing measured yet says a Medium primary verdict is worse than a pixel-model answer.
- Option E — would ship unverifiable code: the event fires on the monitor's worker thread and cannot be raised by the smoke host without SendInput, which this repo deliberately keeps out of committed tooling (CONSTRAINTS.md also forbids the product acting on the user's behalf), and the question source such a subscription would need does not exist.

### Consequences
- **New public surface lives in the orchestrator** (`GuidanceResult`, `GuidanceSource`, `GuidanceConfidence`, `GuidancePoint`). It has to be visible to the next module (a different assembly), which is why it is public; what DEC-009 forbids — a shared abstraction assembly and any cross-leg reference — still holds, and the legs' own files are unchanged.
- **A fallback not-found arrives as `Failed = true`.** `VisionService.Reconcile` reports a model-side not-found as a failure, and the orchestrator carries the leg's verdict verbatim instead of re-classifying it (CO-6 in TEST-CHECKLIST.md records this).
- **One session at a time is now enforced:** a second trigger during a session throws `InvalidOperationException` rather than queueing, so a host awaits the returned task or tolerates the refusal.
- **Still open:** no confidence-tier gate (that stays Jev's), no question source (Voice pipeline), no overlay, no rendering, no input injection — nothing in this change captures, draws, or acts on the user's behalf.

## DEC-009 — 2026-09-22 — Cline agent (model id not exposed to the agent)

- **Status:** accepted
- **Decision:** Primary (Element Selection) and fallback (Vision) results stay as deliberately SEPARATE types — `SelectionResult.cs` states the two modules "must not reference each other (DEC-008)" — but are kept UNIFORM in shape (Found, Description, BoxOnScreen/CenterOnScreen in global physical pixels, a three-tier Confidence at 0.8/0.5 boundaries, plus the raw probability signals) so a future Context Orchestrator can consume both paths behind a single consumer and a future Jev confidence-gate (`SelectionResult.cs`: "a future Jev confidence-gate can branch on" the tier) decides ES→Vision fallback. No shared result type, abstraction assembly, or resolver is introduced now; unification is deferred until the Context Orchestrator exists.

### Context / alternatives considered
- `SelectionResult` and `VisionResult` already carry parallel verdicts (Found, Description, BoxOnScreen/CenterOnScreen, three-tier Confidence, raw signals), which is what makes a uniform consumer shape possible today.
- Option A — unify behind one shared result type + a resolver now. Option B — Vision-centric (let VisionResult drive). Option C — keep both types, uniform in shape, defer unification.

### Chosen because
- Option C: smallest blast radius, touches no working module, and the shape parity is already provable from the code. Option A would force a cross-module reference that `SelectionResult.cs` forbids now; Option B inverts the ES-primary / Vision-fallback contract (DEC-008).

### Rejected because
- Option A — a shared type + resolver requires one module to reference the other (or a new abstraction), violating "the two modules must not reference each other" and shipping a resolver before its consumer (the Context Orchestrator) exists.
- Option B — Vision-centric unification inverts DEC-008's ES-primary ordering.

### Consequences
- **Deferred (explicit):** no shared result type, abstraction assembly, or resolver exists in this tree; the uniform `SelectionResult` / `VisionResult` shape and the 0.8/0.5 Confidence tiers are the only contract. Those names are a future, Context-Orchestrator-driven decision.
- When the Context Orchestrator lands, DEC-008's fallback policy applies: empty tree / unmapped choice / UIA access-denied / no-choice → Vision; `Confidence == High` and a non-degraded verdict accept Element Selection.
- ConfidenceLevel stays classification here, not a gate — the gate belongs to a future Jev component (`SelectionResult.cs`: "a future Jev confidence-gate can branch on").

## DEC-008 — 2026-09-22 — Cline agent (model id not exposed to the agent)

- **Status:** accepted
- **Decision:** Element Selection is the PRIMARY coordinate-detection path (`ElementSelectionService.cs`: "the PRIMARY coordinate-detection path (DEC-008)"): it enumerates the target window's UIA tree, caps it to an interactive candidate manifest (MaxCandidates default 120; text/pane/group/custom controls are deliberately excluded as dense-text territory), and has Jev (model id `jev-1.13.0`, TypeSafe System One "choice" API at `api.typesafe.ai`; bearer key read from `TYPESAFE_API_KEY`) select among the offered `c001..` candidate ids; the selected candidate's UIA BoundingRectangle is the coordinate answer. DeepSeek Vision is the FALLBACK, invoked only when Element Selection returns a Failed result (no resolvable window, empty interactive tree, Jev returned no choice, or Jev chose an id never offered).

### Context / alternatives considered
- Candidate enumeration via UIA using hand-declared classic-COM interop (no WPF, no UIA SDK package, no `<COMReference>`; on .NET Core MSBuild this is rejected with MSB4803 and no Tlbimp host is present).
- Jev as the System One bounded-choice selector only: it receives the candidate ids plus the question and returns one id (or the synthetic `none` option) — never free text, never coordinates, never geometry (`IJevClient.cs`: "Jev returns typed decisions only — a choice among offered options … never free text, never coordinates").
- Vision over DeepSeek pixels (model `deepseek-flash`, 0..1000 integer grid); Vision-primary was the pre-ES design (DEC-006/007).

### Chosen because
- UIA exposes real structure; Jev selects among real candidates → the answer is an exact UIA rectangle rather than a visual estimate. For an interaction-target question (click Save) the UIA rectangle is authoritative; Vision is strongest for the excluded dense-text case. The `none` escape (`SelectionOptions.IncludeNoneOption`, default true) makes an honest no-match possible, and the hallucination guard (Jev choosing an unmapped id is refused — `MapResponse`) makes coordinate fabrication impossible.

### Rejected because
- Vision-primary — least accurate for dense text (where-does-it-say-X) and imprecise for interaction targets; it also costs a screen capture on the happy path this module avoids.
- A shared result type + resolver now — deferred to DEC-009 until the Context Orchestrator exists to consume it.

### Consequences
- Public surface: `ElementSelectionService.SelectAsync(string question, SelectionTarget? target = null, CancellationToken ct = default) -> Task<SelectionResult>` (`SelectionTarget`: CursorWindow default, ForegroundWindow). `SelectionResult` mirrors `VisionResult` — Found, Description, BoxOnScreen/CenterOnScreen in global physical px, ConfidenceLevel (High≥0.8 / Medium≥0.5), raw JevConfidence/WinnerProbability/NoneProbability/PlausibleProbability, plus diagnostics (Latency, InputTokens/OutputTokens, ModelId, candidate counts, Target, chosen UiaCandidate, FailureReason; `Failed => FailureReason != null`). `IJevClient` is the transport seam; `SelectionOptions` (MaxCandidates=120, ModelId=`jev-1.13.0`, BaseUrl=`api.typesafe.ai`, IncludeNoneOption=true, PerCallTimeout=30s).
- Geometry reuses `PixelRect` (ScreenCapture) in global physical screen pixels (DEC-003); DPI awareness stays the host's decision.
- **Threshold-free by contract** (same as Vision/DEC-006): raw signals are surfaced, not thresholded — the 0.8/0.5 tier (`blended = 0.5 * JevConfidence + 0.5 * WinnerProbability`; `Mapping.cs`: `blended >= 0.8f ? ConfidenceLevel.High : blended >= 0.5f ? ConfidenceLevel.Medium : ConfidenceLevel.Low`) and the ES→Vision fallback decision belong to a future Jev confidence-gate (DEC-009).
- **Guide-only (CONSTRAINTS.md):** locates and describes only; never moves the cursor, never focuses/activates, never injects input. The UIA `SetFocus` slot exists only to keep the COM vtable aligned.
- Fallback triggers return a Failed `SelectionResult` (FailureReason), not exceptions — signals, not errors: no resolvable window; `COMException` / `InvalidOperationException` during enumeration; empty interactive tree (manifest.Count == 0); Jev no-choice; Jev chose `none`; or Jev chose an id not in the manifest (hallucination guard).
- Keys: bearer read from `TYPESAFE_API_KEY` in TypeSafeJevClient, extending DEC-007's stopgap through a second seam; never in the module contract, logs (presence/length only), or committed files — ends when the API key security proxy lands as another `IJevClient`.

## DEC-007 — 2026-09-22 — Laguna S 2.1

- **Status:** accepted
- **Decision:** Vision calls DeepSeek directly through an injectable `IVisionClient` seam, reading the bearer key from the `DEEPSEEK_API_KEY` environment variable at call time only. This direct-key path is an explicitly temporary stopgap that dies when the **API key security proxy** module lands.

### Context / alternatives considered
- **Option A — injectable `IVisionClient` seam + env-var key** (what VisionService depends on; `DeepSeekVisionClient` is today's implementation).
- Option B — hardcode the endpoint/transport into VisionService and read the key there.
- Option C — block Vision until the proxy module exists.
- Option D — commit a dev key file alongside the code for the harness.

### Chosen because
- VisionService never touches a key, a URL, or HTTP: the proxy arrives later as another `IVisionClient` implementation (or as a `BaseUrl` redirect — both supported), with no change to the module. Clicky's key-isolation pattern is reproduced as a type boundary, not as prose intent.

### Rejected because
- Option B — couples transport to the module; a proxy that rewrites bodies/auth cannot be expressed as "just another HttpClient".
- Option C — serialises the whole project behind one module; the seam makes the proxy a drop-in.
- Option D — unacceptable: a committed key is a leaked key. The repo carries none (verified by scan).

### Consequences
- **Explicit key-handling risk until the proxy exists:** the key lives in a developer-shell environment (inheritable by child processes), transits directly to the provider, and could sit visibly in a terminal during a screen capture. Mitigations: harness never echoes it (presence/length only), fail-fast `KEY ERROR` (exit 12) when absent, loud TEMPORARY header on `DeepSeekVisionClient`, and this entry records the expiry condition.
- Privacy/product note for the owner: this module sends screen contents to a cloud API. Redaction/off-peak policy belongs at the proxy layer and may become a CONSTRAINTS entry later.
- The harness also accepts a recording fake `IVisionClient`, so the full parse→map→annotate path is verifiable with no key and no network (V-4).

## DEC-006 — 2026-09-22 — Laguna S 2.1

- **Status:** accepted
- **Decision:** Vision sends the capture JPEG inline (base64 data URL) to model id `deepseek-flash` via OpenAI-compatible Chat Completions, in JSON mode with a prompt-pinned **0..1000 integer grid** coordinate convention, takes **2 samples at temperature 0.7** by default, and derives **consistency** (1 − mean pairwise centre distance / grid diagonal) alongside the model's self-reported confidence.

### Context / alternatives considered
- **Option A — 0..1000 integer grid, box + confidence, 2 samples @ 0.7** (chosen).
- Option B — ask the model for raw pixel coordinates.
- Option C — normalised 0..1 float coordinates.
- Option D — single sample at temperature 0 (deterministic).
- Option E — resize/match the JPEG to a standard resolution before sending (Clicky's habit).
- Option F — self-reported confidence only, or no confidence at all.

### Chosen because
- Pixels force the model to guess internal scale (encoders tile/resize); ints are more reliably emitted than long decimals; and 2 @ 0.7 gives a measurable consistency signal (at temp 0 two samples agree by construction, so the signal would be permanently 1.0 and useless to Jev).
- 1.28 px granularity at 1280 px width is finer than the model's own grounding error (RefCOCO 86.0), so the grid is not the limiting factor.

### Rejected because
- Option B — systematic scale drift; exactly the distortion Clicky's matching tried to avoid.
- Option C — LLM decimal outputs are less reliable than integer grid outputs.
- Option D — yields determinism but zero consistency signal; the Jev gate would have nothing to weight.
- Option E — **answered by research, not assumed:** verified in the official DeepSeek vision docs (2026-09-21 session research) that V4.1-Flash accepts arbitrary aspect ratios natively (8192 px/side cap, any aspect, JPEG native). No standard-resolution matching is required or recommended; applying it would add distortion, not remove it. What removes scale ambiguity instead is stating exact image dimensions in the prompt + the grid convention.
- Option F — self-reports are poorly calibrated; the feasibility concern (DeepSeek vision vs a dedicated coordinate tool) demands doubt be *measurable*. Both signals are carried so Jev can weight consistency heaviest.

### Consequences
- JSON mode needs the word "json" + schema example in the prompt; it is documented to occasionally return empty content, so parsing is tolerant (fence strip → strict JSON → regex) with one parse retry.
- Out-of-grid values are parse failures (not silent clamps), so a scale-confused model cannot poison the result.
- `detail: high` is the default (fidelity over tokens); `--max-dim` lets later accuracy work trade tokens for small-target recall.
- Blank frames (Secure Desktop/elevated/RDP) short-circuit before any network call via the capture's non-black ratio.
- `CoordinateMapper` is the single place scale conversion lives (grid → JPEG px → monitor px → screen px); screen-space output assumes a PerMonitorV2 host (standing DPI risk, now load-bearing for coordinates).
- The blended High/Medium/Low verdict is Vision's *raw* signal only — `VisionResult` carries `RawConfidence` and `Consistency` separately so the future Jev gate owns thresholds, not Vision.
- Grounding precision on dense desktop UI is still unproven (RefCOCO 86% is natural images): the visual hit-check + `--max-dim` tuning remain the evidence path; bad numbers are a feasibility finding, not a bug to hide.

## DEC-005 — 2026-09-21 — GLM-5.3-Flash

- **Status:** accepted
- **Decision:** HeyGanesh's own windows mark themselves with `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)`, exposed as `CaptureExclusion.TryExcludeFromCapture`. Screen Capture does **not** attempt to filter its own output; the OS omits marked windows from capture output, for every capture path, ours included.

### Context / alternatives considered
- Option A — a per-capture exclusion list passed to WGC.
- Option B — `WDA_MONITOR`, which replaces the window's content with black.
- Option C — post-capture masking: capture everything, then blank the rectangles of HeyGanesh's own windows.
- Option D — avoid capturing while the overlay is visible (hide it first).

### Chosen because
- One call per window, enforced by the compositor, with no geometry bookkeeping and no dependence on which capture path is used. It also breaks a real feedback loop: an overlay that draws the answer on screen while a capture can still see it would feed HeyGanesh's own rendering back in as if it were the user's screen.

### Rejected because
- Option A — no such API exists in `Windows.Graphics.Capture`; monitor capture photographs the composited desktop.
- Option B — a black rectangle is just as visible to a vision model as the window itself, and it still occludes the content behind it.
- Option C — requires knowing overlay geometry at capture time, races with window movement and resizing, and silently fails for any window that forgets to register itself.
- Option D — the overlay is the product's output; hiding it to take a picture defeats the purpose and flickers.

### Consequences
- **Contract for Screen Overlay:** call it on the real top-level HWND (not a XAML `Window` object, not a child HWND), once the native window exists. Affinity is stored per window, so it must be re-applied whenever a window is recreated — per-monitor overlays, monitor hot-plug, a window destroyed and rebuilt.
- Only the calling process's own windows can be marked; a foreign window is refused with `ERROR_ACCESS_DENIED` (5), which the verification host asserts as correct behaviour.
- **Measured proof (verification host, this session):** a solid-white probe window at a known rectangle was captured with the affinity off and on. White pixels inside the probe rectangle: **100%** with affinity off (the probe is in the frame), and **100% of those pixels changed** with affinity on (the probe is gone) — against a same-rectangle noise floor of **0.0%** and a control-region change of **0.6%**. The exclusion is measured, not inferred.
- Gotcha for future tests: the probe's window class answers `HTTRANSPARENT` to hit-testing, so `WindowFromPoint` reports the window *beneath* it. Verify the probe by its appearance in the capture, never by hit-testing.
- By design this cannot hide other applications' windows.

## DEC-004 — 2026-09-21 — GLM-5.3-Flash

- **Status:** accepted
- **Decision:** Screen Capture is a dependency-free module over `Windows.Graphics.Capture`, using the projections that arrive purely by targeting `net8.0-windows10.0.22621.0` (**zero `PackageReference`**): monitor capture items from `IGraphicsCaptureItemInterop::CreateForMonitor(HMONITOR)`; a `Direct3D11CaptureFramePool.CreateFreeThreaded` pool of 1 buffer; pixels via `SoftwareBitmap.CreateCopyFromSurfaceAsync`; JPEG via `BitmapEncoder` (`ImageQuality` 0.8 as `Single`, `BitmapTransform` Fant scaling to a **1280 px** cap, aspect preserved, never upscaled). Monitors come from `EnumDisplayMonitors`/`GetMonitorInfoW`; the cursor's monitor from `GetCursorPos` + `MonitorFromPoint` and is ordered first; monitors are captured sequentially.

### Context / alternatives considered
- Option A — GDI `BitBlt` of the screen DC.
- Option B — Desktop Duplication API (`IDXGIOutputDuplication`).
- Option C — `GraphicsCapturePicker`.
- Option D — `TryCreateFromDisplayId` + Windows App SDK `GetDisplayIdFromMonitor`.
- Option E — a third-party capture/graphics package.
- Option F — staging-texture readback through `IDirect3DDxgiInterfaceAccess` and hand-declared D3D vtable calls.
- Option G — `System.Drawing` / WIC-COM for encoding.

### Chosen because
- The interop GUIDs and the vtable-significant method order were taken from Microsoft's own capture-interop sample rather than recalled: `IGraphicsCaptureItemInterop` is `{3628E81B-3CAC-4C60-B7F4-23CE0E0C3356}` with `CreateForWindow` at slot 0 and `CreateForMonitor` at slot 1, and the item IID is `{79C3F95B-31F7-4EC2-A464-632EF5D30760}`. An earlier draft had the interop IID wrong — check these, do not remember them.
- `CreateFreeThreaded` was chosen over `Create` because it requires no `DispatcherQueue`, so the module runs in a plain console host with no UI thread and no message pump.
- `SoftwareBitmap.CreateCopyFromSurfaceAsync` replaced Option F, removing vtable-level COM from the module entirely: a wrong vtable slot is an access violation, not a compile error.

### Rejected because
- Option A — legacy path, no hardware acceleration, DWM tearing under load, and the messiest DPI behaviour of the three capture routes.
- Option B — built around long-lived duplication sessions, which fights the one-shot model; raw COM from C# without a package means hand-rolled vtables.
- Option C — a UI dialog: not automatable, and not how a hotkey-driven product should behave.
- Option D — pulls the Windows App SDK bootstrap in for an unpackaged console. Retained as the documented fallback if `CreateForMonitor` ever fails.
- Option E — violates the standing "no new NuGet dependencies without asking" instruction.
- Option G — `System.Drawing` is package-only on .NET 8; WIC-COM is more COM surface than the already-projected `BitmapEncoder`.

### Consequences
- **The approved settle-frame plan was corrected by measurement.** WGC reports frames on screen *change*, so a still desktop offers only one frame. Preferring frame 2 while waiting the full 2 s `FrameTimeout` cost **~2.5 s per capture and then used frame 1 anyway**. It is now a bounded best-effort window: `SettleFrames` 2 preferred within `SettleTimeout` 250 ms, with `FrameTimeout` 2 s kept as the hard deadline for *any* frame. Capture is ~0.7 s per monitor at 2560x1600 (was ~2.5 s).
- Frames are requested as `B8G8R8A8UIntNormalized`. With Windows HD Color (HDR) enabled this tone-maps content, so colours can look washed out — acceptable for vision input, and the price of not carrying a 16-bit pipeline through the whole stack.
- A yellow capture border is drawn by the system around the captured item. Clearing it requires a **packaged** app declaring the `graphicsCaptureWithoutBorder` capability plus a user-consented `GraphicsCaptureAccess.RequestAccessAsync(Borderless)`; without that consent, setting `IsBorderRequired = false` **succeeds and is silently ignored**. That is a UX decision for the packaged shell, so the property is left at its default here. This is *not* the macOS TCC prompt: capture itself needs no permission on Windows.
- `CreateForMonitor` has been reported returning `E_INVALIDARG` on some Windows 11 build 26100 machines; the Microsoft response verified `S_OK` on 26200 builds and pointed at RDP sessions, GPU drivers and unusual integrity levels. It returns `S_OK` on this project's build-26200 machine, and the verification host fails loudly if that ever changes. Fallback (not implemented): `TryCreateFromDisplayId` once the Windows App SDK shell exists.
- Nothing is captured while the UAC Secure Desktop is showing, and monitor capture cannot be created at all in a Remote Desktop session.
- **DPI awareness stays the host's decision.** Coordinates and frames are physical pixels; the library never calls `SetProcessDpiAwarenessContext` because that is process-global. The verification host sets `PerMonitorV2` itself, and this machine's 150% scaling makes the difference visible (2560x1600 physical vs 1707x1067 virtualised).
- Each capture opens and closes its own session and pool, so no capture runs while the user is not asking a question. Verified: 25 consecutive captures, handle delta 1, flat managed heap.

## DEC-003 — 2026-09-21 — GLM-5.3-Flash

- **Status:** accepted
- **Decision:** Do not build an automatic re-hook watchdog for the silent-unhook failure mode. Instead keep the hook callback trivial, publish diagnostics (`HookCallbackCount`, `MaxCallbackLatency`, `DroppedEventCount`, `LastError`), and document the failure mode.

### Context / alternatives considered
- Windows silently removes a `WH_KEYBOARD_LL` hook whose callback exceeds `LowLevelHooksTimeout` (HKCU\Control Panel\Desktop, ~300 ms default). No exception, no return code, and no API to ask whether the hook is still installed. This is the Windows analog of macOS's "tap disabled by timeout".
- Option A — periodic re-hook timer (unhook + reinstall on a cadence).
- Option B — synthetic-keystroke liveness probe via `SendInput`.
- Option C — diagnostics only, trivially fast callback, deferred watchdog.

### Chosen because
- The real mitigation is already structural: the hook proc parses one struct and hands it to a single-reader/single-writer channel, with no user code running in the callback, so callbacks sit orders of magnitude below the timeout (measured max 0.63 ms). A watchdog would add a new failure mode to protect against a case the design already makes unlikely.

### Rejected because
- Option A — there is a window between unhook and reinstall in which real keystrokes are simply not observed; scheduling those gaps to recur is worse than a rare, diagnosable deafness.
- Option B — injecting keystrokes creates input the user never typed, is visible to every other application's hooks, and could trigger other hotkeys.

### Consequences
- If the OS does remove the hook, the module goes deaf until `Stop()`/`Start()` or process restart; callers can watch `MaxCallbackLatency` and `HookCallbackCount` for evidence.
- Revisit once the Context Orchestrator exists and can decide how to surface "degraded" to the user — that is a product decision, not a module decision.

## DEC-002 — 2026-09-21 — GLM-5.3-Flash

- **Status:** accepted
- **Decision:** The default push-to-talk chord is **Control (either side) + left Alt**, held with no other key typed in between. Generic `VK_MENU` is treated as right Alt (fails closed). Shift and Win are neutral. A non-modifier key pressed while a required modifier is down "dirties" the attempt, ending any press and preventing re-arming until the chord is fully released.

### Context / alternatives considered
- On many layouts AltGr is literally LCtrl + RAlt, so "Ctrl+Alt held" overlaps normal character typing on those layouts.
- Option A — treat any Ctrl + any Alt as the chord.
- Option B — require both modifiers to be left-hand.
- Option C — require Ctrl (either) + left Alt only.

### Chosen because
- Option C separates the PTT chord from AltGr traffic without shipping a keyboard-layout database, while still accepting right Ctrl (which carries no AltGr meaning).

### Rejected because
- Option A — fires during ordinary AltGr typing (e.g. `@`, `€` on European layouts).
- Option B — rejects legitimate right-Ctrl use and buys nothing over C.
- Ignoring the dirty rule — the module would report `Listening` while the user types a follow-up sentence.

### Consequences
- Right Alt can never complete the chord (`RequireLeftAlt` defaults to `true`; configurable in `HotkeyMonitorOptions`).
- A chord broken by typing requires a full release of both modifiers before it re-arms; this is intentional and verified.
- Modifier state must never be re-read from `GetAsyncKeyState` for the key in the event currently being delivered — the async key state is not guaranteed to have caught up, and doing so silently prevented the chord from ever completing (real bug, fixed 2026-09-21).

## DEC-001 — 2026-09-21 — GLM-5.3-Flash

- **Status:** accepted
- **Decision:** Observe the modifier-only chord with a `WH_KEYBOARD_LL` hook installed on a dedicated **pump** thread that owns the message loop; the hook proc only parses `KBDLLHOOKSTRUCT` and enqueues to a single-reader/single-writer `Channel<KeyEvent>`; a separate **worker** thread runs the chord state machine and raises `Pressed`/`Released`. The `HHOOK` is owned by a `SafeHandle`.

### Context / alternatives considered
- Option A — `RegisterHotKey` / `WM_HOTKEY`.
- Option B — `GetAsyncKeyState` polling on a timer.
- Option C — `WH_KEYBOARD` (non-low-level) global hook.
- Option D — third-party package (e.g. SharpHook).
- Option E — `WH_KEYBOARD_LL` with pump/worker split, BCL only.

### Chosen because
- A low-level hook is the only mechanism that reports both edges of a modifier-only hold system-wide, in-process, with no injected code in other processes and no new dependency.

### Rejected because
- Option A — requires a non-modifier virtual key, fires once on combo-down, and has no "held"/"released" concept; it cannot express push-to-talk.
- Option B — adds latency and constant CPU cost, and races with focus changes.
- Option C — global scope requires injecting a DLL into every process; not viable from managed code.
- Option D — violates the standing "no new NuGet dependencies without asking" instruction; the equivalent is ~200 lines of BCL P/Invoke that we fully own.

### Consequences
- Each monitor owns two background threads and a channel; events are raised on a non-UI thread, so UI subscribers must marshal.
- A slow subscriber delays *subsequent events* but can never delay system input — this separation is the reason for the split, and it is why the hook proc must never raise events itself.
- Anything added to `HookProc` risks the OS's ~300 ms callback budget; `MaxCallbackLatency` exists to police that.

## DEC-018

- **Status:** accepted
- **Decision:** ScreenOverlay to ContextOrchestrator dependency conflict resolved through shared HeyGanesh.Abstractions abstraction. Both modules now depend on the zero-NuGet shared contract rather than each other. This supersedes earlier Platform-to-Intelligence dependency conflict identification.
- **Supersedes:** earlier Platform-to-Intelligence dependency conflict (pre-R7)
- **Date:** 2026-10-24
- **Model version:** Cline agent (M1/R7+)


## DEC-019 — 2026-10-24 — Cline agent (M1/R7+ completion)

- **Status:** accepted
- **Decision:** Complete the ScreenOverlay→ContextOrchestrator abstraction migration. Moved `GuidanceResult`, `GuidanceSource`, `GuidanceConfidence`, `GuidancePoint`, `PixelRect`, `MonitorInfo`, and `MonitorCapture` from their original assemblies (ContextOrchestrator and ScreenCapture) into `HeyGanesh.Abstractions` as the single shared contract. ScreenOverlay, ContextOrchestrator, ElementSelection, Vision, ScreenCapture, and App Shell now all depend on Foundation for these types. ScreenOverlay no longer references ContextOrchestrator (Intelligence). Dependency direction: Platform→Foundation, Intelligence→Foundation+permitted lower layers. Zero new NuGet dependencies. Guide-only safety preserved — abstraction contains only data types and interfaces, no implementation or actuation.
- **Supersedes:** DEC-018 (initial abstraction creation — now completed with full type migration)
- **Date:** 2026-10-24
- **Model version:** Cline agent (M1/R7+ completion)
