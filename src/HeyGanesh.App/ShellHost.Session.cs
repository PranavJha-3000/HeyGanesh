using System.Diagnostics;
using HeyGanesh.ContextOrchestrator;
using HeyGanesh.ElementSelection;
using HeyGanesh.ScreenOverlay;
using HeyGanesh.Vision;
using HeyGanesh.Voice;

namespace HeyGanesh.App;

// One turn, one session, one overlay. A turn is: press opens the microphone (Voice), release or the
// silence timeout ends it, the committed transcript becomes the question, and the session that was
// already here takes over from there.
//
// This is the whole job of the shell, and it is deliberately one method with two callers: `--once` and
// the hotkey pump both come through the same turn runner and the same RunSessionAsync, so the
// deterministic path an operator can script and the path a human drives with the chord cannot drift
// apart.
//
// Exit codes are part of the contract, and they are the ones the module hosts already established:
// 0 a verdict was produced (an honest no-match counts; so does a silent or release-abandoned voice
// turn, which resolves nothing on purpose), 1 the verdict is a failure — and a listen that produced no
// transcript, 3 a provider/HTTP error propagated rather than swallowed, 4 an unexpected fault,
// 12 a required key is missing, 13 cancelled. 2 is "the shell could not start" and is decided in
// ShellHost.RunAsync.

internal static partial class ShellHost
{
    /// <summary>
    /// Resolves <see cref="ShellOptions.Question"/> once, draws whatever verdict came back, and reports
    /// what happened. Never throws: every documented failure mode of the pipeline is mapped to a line of
    /// output and an exit code.
    /// </summary>
    private static async Task<int> RunSessionAsync(
        ContextOrchestratorService orchestrator,
        ScreenOverlayService overlay,
        ShellOptions options,
        CancellationToken lifetime)
    {
        // The session token: the process lifetime (Ctrl+C) plus the optional --cancel-ms probe, linked so
        // the shell can abort on shutdown without inventing a second cancellation story.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        if (options.CancelMs > 0)
        {
            linked.CancelAfter(options.CancelMs);
            Console.WriteLine($"cancelling the session token after {options.CancelMs} ms...");
        }

        var started = Stopwatch.GetTimestamp();

        GuidanceResult guidance;
        try
        {
            // DEC-010's trigger seam, used exactly as documented. Safe to call from the hotkey module's
            // worker thread: it returns a task and does no work inline beyond the re-entrancy check, so
            // the hook callback is never delayed by a capture or a provider call.
            guidance = await orchestrator
                .HandleHotkeyPressedAsync(options.Question, linked.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"CANCELLED: the token aborted the session after {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms; " +
                              "no verdict was produced, so nothing is drawn.");
            Console.WriteLine($"  phase       : {orchestrator.Phase} (cancellation unwinds to Idle, never a half-built verdict)");
            return 13;
        }
        catch (JevAuthenticationException ex)
        {
            Console.WriteLine($"KEY ERROR (Element Selection): {ex.Message}");
            return 12;
        }
        catch (JevClientException ex)
        {
            Console.WriteLine($"API ERROR (Element Selection, propagated not swallowed): {ex.Message}");
            return 3;
        }
        catch (VisionAuthenticationException ex)
        {
            Console.WriteLine($"KEY ERROR (Vision): {ex.Message}");
            return 12;
        }
        catch (VisionClientException ex)
        {
            Console.WriteLine($"API ERROR (Vision, propagated not swallowed): {ex.Message}");
            return 3;
        }
        catch (InvalidOperationException ex)
        {
            // The orchestrator refuses an overlapping session (CO-10). Reaching this from the shell means
            // two triggers raced past the pump's guard, which is worth seeing rather than hiding.
            Console.WriteLine($"REFUSED: {ex.Message}");
            return 0;
        }
        catch (Exception ex)
        {
            // Anything else is a defect, not a verdict. Named with its type so it can never be mistaken
            // for the pipeline answering.
            Console.WriteLine($"FAULT: {ex.GetType().Name}: {ex.Message}");
            return 4;
        }

        PrintVerdict(guidance, orchestrator.Phase);

        if (!options.Overlay)
        {
            Console.WriteLine("overlay     : skipped (--no-overlay)");
            return guidance.Failed ? 1 : 0;
        }

        try
        {
            // Awaiting waits out the full visible duration: the session is not finished until the
            // highlight has left the screen. That also means a new trigger is refused until then, which
            // is SO-8's refuse-don't-queue stance surfacing one layer up rather than a second policy
            // invented here.
            var shown = await overlay.ShowAsync(guidance, linked.Token).ConfigureAwait(false);
            Console.WriteLine($"overlay     : {shown}");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("CANCELLED: the token aborted the show; any window it created was removed.");
            return 13;
        }
        catch (InvalidOperationException ex)
        {
            // SO-8: a second simultaneous show is refused. The shell should never manage to ask for one,
            // because the pump keeps a session in flight until the overlay is done.
            Console.WriteLine($"REFUSED (overlay): {ex.Message}");
            return 0;
        }

        return guidance.Failed ? 1 : 0;
    }

    /// <summary>
    /// Reports the verdict the way a module host does — the summary line, then the fields a person
    /// actually needs when an answer looks wrong: which leg answered, what the primary leg said if it did
    /// not, where the box is, and what it cost.
    /// </summary>
    private static void PrintVerdict(GuidanceResult guidance, OrchestratorPhase phase)
    {
        Console.WriteLine($"verdict     : {guidance}");

        if (guidance.PrimaryFailureReason is { } primary)
        {
            // DEC-010: the documented fallback triggers are indistinguishable inside the verdict itself,
            // so the primary leg's own words are surfaced rather than letting a fallback look unmotivated.
            Console.WriteLine($"  primary   : {primary}");
        }

        Console.WriteLine($"  leg       : {guidance.Source}  confidence={guidance.Confidence}  model={guidance.ModelId}");
        Console.WriteLine($"  choice    : {guidance.Choice ?? "-"}  box={guidance.BoxOnScreen?.ToString() ?? "none"}  " +
                          $"center={guidance.CenterOnScreen?.ToString() ?? "none"}");
        Console.WriteLine($"  frames    : {guidance.Captures.Count} retained  latency={guidance.Latency.TotalMilliseconds:F0} ms  " +
                          $"tokens={guidance.InputTokens}/{guidance.OutputTokens}");
        Console.WriteLine($"  phase     : {phase}");
    }

    /// <summary>
    /// Maps <c>GlobalHotkeyMonitor.Pressed</c>/<c>Released</c> onto voice turns, one at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three constraints shape this class, and all three come from the modules rather than from taste:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>The handlers must not block.</b> <c>Pressed</c>/<c>Released</c> are raised on the hotkey module's
    /// worker thread,
    /// and that worker exists to keep the OS hook callback inside <c>LowLevelHooksTimeout</c> (~300 ms).
    /// Blocking it there — even for a capture — would cost the whole hook. So the session is handed to
    /// the thread pool and the handler returns.
    /// </description></item>
    /// <item><description>
    /// <b>One session at a time, refused rather than queued.</b> A second overlapping session would race
    /// the first over the same screen and the same keys; that is CO-10's and SO-8's stated stance, and
    /// the shell honours it at the trigger so the refusal is explained to the user instead of surfacing
    /// as a stack trace. The busy window deliberately spans the overlay's visible duration too: the
    /// answer is still on screen, and a second pointer would be ambiguous.
    /// </description></item>
    /// <item><description>
    /// <b>Release finalises only what has already committed.</b> The Voice API deliberately has no
    /// force-commit call (its contract is one ListenOnceAsync per utterance, DEC-013), so a release
    /// while the transcript is still forming cancels the listen — the utterance is abandoned cleanly,
    /// with no verdict and nothing resolved — while a release after commit cannot undo anything,
    /// because the session has already taken over. Holding the chord through the sentence is the
    /// contract; DEC-014 records this limitation rather than hiding it.
    /// </description></item>
    /// </list>
    /// </remarks>
    private sealed class TriggerPump
    {
        /// <summary>Where the current turn is. Only ever read and written under the gate.</summary>
        private enum TurnPhase
        {
            /// <summary>No turn in flight — a press starts one.</summary>
            Idle = 0,

            /// <summary>The microphone is open (or about to be): release ends the utterance here.</summary>
            Listening = 1,

            /// <summary>The transcript committed and the session owns the turn; release is logged only.</summary>
            Resolving = 2,
        }

        private readonly VoiceService _voice;
        private readonly ContextOrchestratorService _orchestrator;
        private readonly ScreenOverlayService _overlay;
        private readonly ShellOptions _options;
        private readonly CancellationToken _lifetime;

        private readonly object _gate = new();
        private Task<int>? _current;

        // Turn state, all under the gate. The CTS is created synchronously on press (not inside the
        // pooled task) so a release that arrives before the pool even starts the listen can still find
        // and cancel it — otherwise a fast tap would leave an orphan microphone session running.
        private CancellationTokenSource? _utterCts;
        private TurnPhase _phase;
        private bool _releaseRequested;

        public TriggerPump(
            VoiceService voice,
            ContextOrchestratorService orchestrator,
            ScreenOverlayService overlay,
            ShellOptions options,
            CancellationToken lifetime)
        {
            _voice = voice;
            _orchestrator = orchestrator;
            _overlay = overlay;
            _options = options;
            _lifetime = lifetime;
        }

        /// <summary>Called on the hotkey module's worker thread. Must return immediately.</summary>
        public void OnPressed()
        {
            lock (_gate)
            {
                if (_current is { IsCompleted: false })
                {
                    Console.WriteLine("REFUSED: a voice or guidance turn is already running (or its highlight " +
                                      "is still on screen); one Ctrl+Alt hold maps to one turn.");
                    return;
                }

                _releaseRequested = false;
                var cts = NewUtteranceCts();
                _phase = TurnPhase.Listening;
                _current = Task.Run(() => RunVoiceTurnAsync(cts));
            }
        }

        /// <summary>
        /// Called on the monitor's worker thread when the chord comes up. During the listen it ends the
        /// utterance; during the resolve it is a no-op with a log line, because the transcript has
        /// already committed and the session is past the point where a release means anything.
        /// </summary>
        public void OnReleased()
        {
            CancellationTokenSource? toCancel = null;
            lock (_gate)
            {
                switch (_phase)
                {
                    case TurnPhase.Listening:
                        _releaseRequested = true;
                        toCancel = _utterCts;
                        Console.WriteLine("chord released: closing the microphone...");
                        break;

                    case TurnPhase.Resolving:
                        Console.WriteLine("chord released (the transcript was already committed; the session is running)");
                        break;

                    default:
                        Console.WriteLine("chord released (nothing was listening)");
                        break;
                }
            }

            // Outside the gate: cancelling runs registered callbacks, some of which take this same
            // lock. Safe because Pressed/Released share the monitor's worker thread (no OnPressed can
            // swap the source underneath this reference) and because no turn path disposes a CTS it
            // has abandoned — disposal happens only once the turn is provably finished.
            toCancel?.Cancel();
        }

        /// <summary>
        /// One turn without the hook: the <c>--once</c> voice path. The same runner the chord uses, so
        /// the scripted and human-driven paths cannot drift apart.
        /// </summary>
        public async Task<int> RunDirectAsync()
        {
            CancellationTokenSource cts;
            lock (_gate)
            {
                _releaseRequested = false;
                cts = NewUtteranceCts();
                _phase = TurnPhase.Listening;
            }

            try
            {
                // Not Task.Run: --once is already on the async main path, and there is no event thread
                // here that a synchronous start could block.
                return await RunVoiceTurnAsync(cts).ConfigureAwait(false);
            }
            finally
            {
                lock (_gate)
                {
                    _utterCts?.Dispose();
                    _utterCts = null;
                }
            }
        }

        /// <summary>
        /// Creates this turn's cancellation source. Called under the gate; the previous turn's source
        /// it replaces is always finished by then (a press only arrives while idle, and a direct run is
        /// single-file), which is what makes disposing it here safe.
        /// </summary>
        private CancellationTokenSource NewUtteranceCts()
        {
            _utterCts?.Dispose();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
            if (_options.CancelMs > 0)
            {
                // The unwind probe, applied to the listen as well as the session: an operator watching
                // a turn die should see the microphone close, not just the capture skip.
                cts.CancelAfter(_options.CancelMs);
            }

            _utterCts = cts;
            return cts;
        }

        /// <summary>
        /// One full turn: listen for the utterance, then — only if one committed — resolve it through
        /// the exact session path the legacy <c>--question</c> trigger uses. Never throws; every
        /// documented failure mode maps to a line of output and an exit code, the same contract
        /// <see cref="RunSessionAsync"/> keeps for the resolve half.
        /// </summary>
        private async Task<int> RunVoiceTurnAsync(CancellationTokenSource utterCts)
        {
            try
            {
                UtteranceResult utter;
                try
                {
                    utter = await _voice.ListenOnceAsync(utterCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return HandleListenCancellation();
                }
                catch (VoiceAuthenticationException ex)
                {
                    Console.WriteLine($"KEY ERROR (Voice): {ex.Message}");
                    return 12;
                }
                catch (VoiceClientException ex)
                {
                    // VoiceProtocolException derives from this: a broken protocol (or a Backend Agent
                    // that should be off) is propagated like any provider fault, never swallowed into
                    // a verdict. Exit 3 is the existing leg-error contract.
                    Console.WriteLine($"VOICE ERROR (propagated not swallowed): {ex.Message}");
                    return 3;
                }
                catch (InvalidOperationException ex)
                {
                    // VoiceService refuses a second concurrent listen rather than queueing it (VO-9).
                    // The pump's own gate should make this unreachable; if it is ever reached, the
                    // refusal text is more useful than a stack trace.
                    Console.WriteLine($"REFUSED: {ex.Message}");
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FAULT: {ex.GetType().Name}: {ex.Message}");
                    return 4;
                }

                // The one line that names what the user said (and, via Voice's own ToString, how many
                // assistant events were suppressed on the way). The transcript is the user's speech —
                // never a key, never a URL, never the frontend's answer.
                Console.WriteLine($"utterance   : {utter}");

                if (utter.Failed)
                {
                    // A listen that produced no transcript is a failure, not a silent user — and not a
                    // fabrication either: no capture is spent, no verdict is invented (DEC-014 records
                    // the exit-1 choice, since the reason string carries no typed 12/3 signal).
                    Console.WriteLine($"VOICE FAILURE: no transcript was produced ({utter.FailureReason}); nothing is resolved.");
                    return 1;
                }

                if (utter.IsNoSpeech || string.IsNullOrWhiteSpace(utter.FinalText))
                {
                    // Silence is a verdict, not an error (VO-4): the mic opened and nobody said
                    // anything. The orchestrator is never called — spending a capture on silence would
                    // be the "Choice==none falls back" mistake one layer up.
                    Console.WriteLine("no speech heard: the microphone opened and nothing was said; nothing to resolve.");
                    return 0;
                }

                lock (_gate)
                {
                    _phase = TurnPhase.Resolving;
                }

                // options-with rather than a new parameter: RunSessionAsync is the one session path and
                // keeps reading ShellOptions.Question, which here is the committed transcript.
                return await RunSessionAsync(
                    _orchestrator,
                    _overlay,
                    _options with { Question = utter.FinalText },
                    _lifetime).ConfigureAwait(false);
            }
            finally
            {
                // Phase only: the CTS is disposed by whoever starts (and replaces) the next turn, or by
                // RunDirectAsync/DrainAsync once this turn is provably finished. Disposing it here
                // would race OnReleased, which may still be holding the reference it captured.
                lock (_gate)
                {
                    _phase = TurnPhase.Idle;
                }
            }
        }

        /// <summary>
        /// Decides what an OperationCanceledException from the listen means. Three causes, three
        /// different honest answers: shutdown (13), a release that beat the transcript (0 — the user
        /// ended the utterance, which is not the same as Ctrl+C), and the cancel-ms probe or a
        /// teardown race (13). Never fabricates a verdict.
        /// </summary>
        private int HandleListenCancellation()
        {
            bool released;
            lock (_gate)
            {
                released = _releaseRequested;
            }

            if (_lifetime.IsCancellationRequested)
            {
                Console.WriteLine("CANCELLED: shutdown aborted the voice turn; no transcript was produced, " +
                                  "so nothing is resolved.");
                return 13;
            }

            if (released)
            {
                Console.WriteLine("released before the transcript committed — the utterance was abandoned; " +
                                  "nothing to resolve. Hold Ctrl+Alt until the sentence is finished.");
                return 0;
            }

            Console.WriteLine("CANCELLED: the turn's token aborted the listen; no transcript was produced, " +
                              "so nothing is resolved.");
            return 13;
        }

        /// <summary>
        /// Waits (bounded) for the turn that was in flight when shutdown began, and returns its exit
        /// code so the caller can report it. Never throws. This is also where a completed turn's
        /// cancellation source is finally released: by now the turn is provably finished.
        /// </summary>
        public async Task<int> DrainAsync()
        {
            Task<int>? current;
            lock (_gate)
            {
                current = _current;
            }

            if (current is null)
            {
                return 0;
            }

            // Bounded because the lifetime token has already been cancelled and a turn wedged inside a
            // provider call must not wedge Ctrl+C. Five seconds is slack for unwinding, not a second
            // timeout: the modules own their own provider timeouts.
            var finished = await Task.WhenAny(current, Task.Delay(DrainGrace)).ConfigureAwait(false);
            if (finished != current)
            {
                Console.WriteLine($"note: the in-flight turn had not unwound {DrainGrace.TotalSeconds:F0}s after cancellation; " +
                                  "leaving it to the (already cancelled) lifetime token.");
                DisposeUtteranceCts();
                return 0;
            }

            // The monitor's worker thread was joined by hotkey.Stop() before this runs, so no OnReleased
            // can still be holding the source: disposing here cannot pull it from under an event thread.
            DisposeUtteranceCts();

            return current.Status == TaskStatus.RanToCompletion ? current.Result : 4;
        }

        /// <summary>Releases the last turn's cancellation source. Caller is not holding the gate.</summary>
        private void DisposeUtteranceCts()
        {
            lock (_gate)
            {
                _utterCts?.Dispose();
                _utterCts = null;
            }
        }
    }
}
