# FLOW — Interaction Flow

> Placeholder. Will hold the end-to-end interaction flow diagram once it is defined —
> expected shape: hotkey → Screen Capture → Vision/Coordinate Detection → Jev →
> Tutor Engine → Visual Lesson Engine → Screen Overlay / Voice pipeline → Notes persistence.
>
> Screen Capture leg today: `ScreenCaptureService.CaptureAllMonitorsAsync` → one `MonitorCapture`
> per monitor (cursor's monitor first) → `MonitorCapture.Jpeg`, a downscaled B8G8R8A8 JPEG (≤1280 px,
> aspect preserved), plus `NonBlackPixelRatio` so Vision can reject a blank frame. One-shot per call;
> no running capture while idle.
>
> Vision leg today: one `MonitorCapture` + a question → `VisionService.LocateAsync` →
> `VisionResult` carrying source-local and screen-space (`PixelRect`) boxes plus a confidence verdict
> (`Confidence` from `RawConfidence` + cross-sample `Consistency`) — the exact signal shapes the future
> Jev confidence-gate will consume. Transport is behind `IVisionClient` (direct DeepSeek today,
> temporary pending the API key security proxy).
>
> Element Selection leg today (the PRIMARY path — DEC-008): the target window's UIA tree is enumerated
> once (control view, one cross-process cache-request pass) → interactive candidates are ranked by
> lexical overlap with the question and capped at 120 (`c001..`) → Jev returns one option id (or the
> synthetic `none`) → that candidate's UIA BoundingRectangle is the answer, in global physical pixels.
> Element Selection itself consumes no frame — the capture exists for the fallback leg and for the
> overlay's annotation. `SelectionResult` carries the same shape as `VisionResult` (box + 0.8/0.5
> confidence tier) so a consumer can treat the two legs uniformly.
>
> Fallback rule today: a FAILED Element Selection result (no resolvable window / empty interactive
> tree / UIA access denied by UIPI / Jev returned no choice / Jev chose an id never offered) is what
> sends the request down the Vision leg. A valid "nothing matches" answer (`Choice == "none"`) is NOT
> a failure and does not fall back. The two modules must not reference each other (DEC-008/DEC-009).
>
> Context Orchestrator leg today (the entry point — DEC-010): `HandleHotkeyPressedAsync(question, ct)`
> opens one session per hotkey trigger (the host wires `GlobalHotkeyMonitor.Pressed` to it, since the
> question source does not exist yet). The session captures every monitor once (cursor's display first),
> runs Element Selection, and only on a FAILED primary result runs Vision on `captures[0]` — exactly once,
> never twice, and never on a `Choice == "none"` verdict. Whatever the winning leg said is normalized into
> `GuidanceResult` inside this module only: `SelectionResult`/`VisionResult` and their duplicated
> `ConfidenceLevel` are untouched, no threshold is applied, and no shared abstraction assembly exists
> (DEC-009's deferral, executed). A second trigger while a session is in flight is refused, not queued.
> Cancellation and provider auth/HTTP failures propagate to the caller unchanged.
>
> Screen Overlay leg today (the first visual act — DEC-011): `ScreenOverlayService.ShowAsync(guidance, ct)`
> consumes that `GuidanceResult` directly (no re-mapping, no new result type, no shared abstraction).
> `Found=false` (honest no-match) and any `Failed` verdict draw nothing — a pointer for "not found" would
> teach the wrong meaning. A real verdict gets one click-through layered window per monitor the box
> touches, shown for a bounded duration without activating, capture-excluded from the first frame, then
> removed. Coordinates are used verbatim in global physical pixels; the host owns PerMonitorV2 awareness
> (the overlay checks it, never sets it). The overlay captures nothing and never clicks, types, focuses,
> or otherwise acts — the user always acts.
>
> App Shell leg today (the process that owns everything above — DEC-012): `HeyGanesh.App` is the first
> assembly that *drives* the pipeline instead of being driven by a host. It sets process-wide
> PerMonitorV2 awareness, composes one `ScreenCaptureService` (shared into the overlay, which uses it for
> monitor topology only), one of each leg, the orchestrator and the overlay, then subscribes
> `GlobalHotkeyMonitor.Pressed`/`Released` and maps one chord **turn** to one
> `VoiceService.ListenOnceAsync` → one `HandleHotkeyPressedAsync` → one `ShowAsync` → one reported
> `OverlayShowResult`. The hook callback is never blocked (the turn runs on the pool), and a second
> trigger while a turn is in flight — or while its highlight is still on screen — is refused rather
> than queued, mirroring CO-10 and SO-8. `--once` runs the identical turn runner with no hook at all,
> which is how the wiring is exercised without keystrokes. The question comes from the committed
> transcript: `UtteranceResult.FinalText` is passed as `options.Question` into the one unchanged
> `HandleHotkeyPressedAsync`. `Released` ends the utterance — a release before the transcript commits
> abandons the turn cleanly (DEC-014), because the Voice API has no force-commit call. The shell owns
> no confidence threshold, no fallback rule and no display policy: it reports the verdict it was handed
> and draws no window of its own.
>
> Module names used here must match [ARCHITECTURE.md](./ARCHITECTURE.md) verbatim.

## Path today (App Shell driving the Context Orchestrator over Screen Capture + Element Selection / Vision, then Screen Overlay)

```
App Shell (HeyGanesh.App — DEC-012): PerMonitorV2 first, then one capture service, one orchestrator,
one overlay, one hook. It owns the process; it draws no window of its own and never injects input.

Ctrl+Alt hold ─▶ GlobalHotkeyMonitor.Pressed        (--once enters below without the hook)
                      │
                      │  the shell maps the event; the orchestrator never subscribes to the monitor
                      ▼
        Context Orchestrator.ResolveAsync (one session per trigger; a second trigger is refused)
              │
              │  1. Screen Capture      all monitors, cursor's display first (one frame per session)
              │  2. Element Selection   PRIMARY: enumerate ─▶ rank/cap 120 ─▶ Jev select ─▶ UIA rect
              │  3. Vision              FALLBACK, only if step 2 returned a FAILED result
              ▼
        GuidanceResult (Found, Description, box/center in global physical px, tier, raw signals)
              │
              │  4. Screen Overlay      ShowAsync(guidance): draw nothing for no-match/failure;
              │                         else one click-through window per touched monitor, bounded time
              ▼
        OverlayShowResult (Displayed, Reason, ShownFor, WindowCount — never a position)
              │
              ▼
        shell reports it (verdict, drawn-or-not, hook health) and returns to waiting
```

This is the only end-to-end path that exists in code today. The App Shell is its entry point (DEC-012) —
or rather the global hotkey is, and the shell is the only thing that listens for it. `GuidanceResult` is
the unified target: a global physical-pixel box (or an honest no-match) plus a confidence tier, with every
raw signal the legs expose carried through for whoever consumes it next. The Screen Overlay is the first
such consumer: it draws the verdict and returns `OverlayShowResult`, never coordinates back.
Downstream — the Jev confidence-gate that will branch on the tier, Tutor Engine, Visual Lesson Engine,
Notes persistence — is still undefined, so it is not drawn here rather than invented. Voice is wired into
this path as its question source (its legs are drawn separately below): the shell listens, the committed
transcript becomes the question, and `--question` survives only as the legacy `--once` scripted path
(FEATURE-003's follow-up, closed by DEC-014). The Tutor Engine that will consume the answer text and
speak it is still undefined — nothing in the shell invents one.

Both legs are specified in [ARCHITECTURE.md](./ARCHITECTURE.md); Element Selection is primary and
Vision is the fallback (DEC-008). The Vision leg fails fast (`KEY ERROR`, exit 12) when
`DEEPSEEK_API_KEY` is absent, as the Element Selection leg does when `TYPESAFE_API_KEY` is absent.

## Voice path (wired into the shell as the question source; DEC-013/DEC-014)

```text
Speech in (VoiceService.ListenOnceAsync — one utterance per call, transport opened per utterance)

  Ctrl+Alt press → TriggerPump.OnPressed → ListenOnceAsync (on the pool)   │
  Ctrl+Alt release → TriggerPump.OnReleased → the utterance ends           │
        (a release before the transcript commits abandons the turn:        │
         the Voice API has no force-commit call — DEC-014)                 │
        │
        ▼
  WinMmMicrophoneSource ── PCM16 16 kHz blocks ──▶ QwenGatewayClient
        │                                          │  session.hello → session.ready (wire 7.0,
        │                                          │  major-version mismatch refuses the listen)
        │                                          │  input_audio_buffer.append per block
        ▼                                          ▼
  qwen-audio-agent Gateway (EXTERNAL RUNTIME — realtime frontend, AGENT_PROTOCOL=none required)
        │
        │  transcript.delta / transcript.final (role=user) ────▶ Partial event / FinalText
        │  response.started ──▶ response.cancel + interrupt (DEC-013 L-1: prevention impossible,
        │                      cancel-after-start is the supported stop) ──▶ Ledger counts it
        │  audio.delta / transcript.*(role=assistant) / tool.call ──▶ discarded + counted, NEVER spoken
        │  task.* ──▶ VoiceProtocolException: the Backend Agent is on; listen aborts
        ▼
  UtteranceResult { FinalText | IsNoSpeech | FailureReason }   (silence ≠ failure, mirroring "none" ≠ failed)
        │
        ▼
  the shell passes FinalText as `options.Question` into the existing
  Context Orchestrator path above — this hand-off is BUILT (DEC-014).
        │
        ├─ IsNoSpeech / released-before-commit → nothing is resolved, no capture is spent
        └─ Failed → exit 1, no fabricated verdict

Speech out (VoiceService.SpeakAsync — mutually exclusive with a listen)

  (FUTURE) Tutor/Context answer text
        ▼
  SpeakRequest { Text, VoiceId? } ──▶ FishTtsClient (EXTERNAL SERVICE; ITtsClient seam)
        │   POST /v1/tts  format=wav  sample_rate=44100  (HTTP body streamed: transport streaming only)
        ▼
  WavPcmReader (RIFF/PCM16/mono validated, fails loud) ──▶ WinMmAudioOutput ──▶ speaker
        ▼
  SpeakResult { SpokenChars, AudioDuration, Latency }
```

Rules that hold on both legs: one listen and one speak at a time, second call refused never queued;
`OperationCanceledException` propagates as a cancellation (exit 13), never as a verdict; a transport /
auth / protocol failure is `Failed` with its leg named, never re-interpreted as silence; nothing here
classifies intent or answers a question — Voice only moves text in and out, so the future Tutor Engine
inserts between `ListenOnceAsync` and `SpeakAsync` without Voice being redesigned (DEC-013).
