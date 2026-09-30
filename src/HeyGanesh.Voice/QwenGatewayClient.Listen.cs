using System.Diagnostics;
using System.Net.WebSockets;
using System.Threading.Channels;

namespace HeyGanesh.Voice;

public sealed partial class QwenGatewayClient
{
    /// <summary>
    /// One utterance: connect, stream the microphone until the user's transcript commits (or silence, or
    /// the cap), then close the transport.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The verdict rules, in order of precedence: a cancellation propagates (never becomes a verdict); a
    /// transport/protocol/backend fault becomes <see cref="UtteranceResult.Failure"/>; a committed
    /// <c>transcript.final role=user</c> becomes the text; silence or a discard becomes
    /// <see cref="UtteranceResult.IsNoSpeech"/>. Assistant-side frames never enter that decision at all —
    /// they are cancelled/discarded by the loop and counted in <see cref="Ledger"/>.
    /// </para>
    /// <para>
    /// The transport is per-utterance by design (see the class remarks), so a dead Gateway from the last
    /// listen is not assumed here: reconnect is simply "connect again", bounded by
    /// <see cref="VoiceOptions.MaxReconnectAttempts"/>, and persistent failure latches after
    /// <see cref="VoiceOptions.MaxConsecutiveFailures"/> consecutive failed listens.
    /// </para>
    /// </remarks>
    public async Task<UtteranceResult> ListenOnceAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_socket is { State: WebSocketState.Open })
        {
            throw new InvalidOperationException(
                "A Gateway listen is already open on this client. VoiceService is meant to gate one listen " +
                "at a time; a second transport would take over the Gateway owner slot from itself.");
        }

        lock (_gate)
        {
            if (_consecutiveFailures >= _options.MaxConsecutiveFailures)
            {
                // A latch rather than a retry loop: a Gateway that failed three listens in a row is down,
                // and hammering it (or the operator with error dialogs) does not help.
                return UtteranceResult.Failure(
                    $"the Gateway failed {_consecutiveFailures} listens in a row; refusing further attempts " +
                    "until the client is re-created",
                    _gateway.FrontendId, TimeSpan.Zero, (int)_ledger.SuppressedAssistantEvents);
            }
        }

        var started = Stopwatch.GetTimestamp();

        for (var attempt = 0; attempt <= _options.MaxReconnectAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var verdict = await ListenOnceCoreAsync(started, cancellationToken).ConfigureAwait(false);
                RecordSuccess();
                return verdict;
            }
            catch (OperationCanceledException)
            {
                // The caller's own doing: no verdict, no failure counting, and the transport is already
                // torn down by the finally in the core loop.
                throw;
            }
            catch (VoiceClientException ex)
            {
                if (attempt >= _options.MaxReconnectAttempts || !IsTransient(ex))
                {
                    RecordFailure();
                    var suppressed = (int)_ledger.SuppressedAssistantEvents;
                    return UtteranceResult.Failure(ex.Message, _gateway.FrontendId,
                        Stopwatch.GetElapsedTime(started), suppressed);
                }
            }

            // Reconnect: brief backoff so a restarting Gateway is not retried against in a tight loop.
            await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)), cancellationToken).ConfigureAwait(false);
        }

        // Unreachable in practice (the loop returns or throws), but the compiler needs the truth: the
        // ledger has the counts either way.
        RecordFailure();
        return UtteranceResult.Failure(
            "the Gateway could not be reached after repeated attempts",
            _gateway.FrontendId, Stopwatch.GetElapsedTime(started), (int)_ledger.SuppressedAssistantEvents);
    }

    /// <summary>
    /// Connection-refused and mid-stream transport failures are worth retrying within one listen; auth,
    /// protocol and configuration failures are not — retrying them just repeats the same rejection.
    /// </summary>
    private static bool IsTransient(VoiceClientException ex) =>
        ex is not (VoiceAuthenticationException or VoiceProtocolException);

    private void RecordSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
        }
    }

    private void RecordFailure()
    {
        lock (_gate)
        {
            _consecutiveFailures++;
        }
    }

    /// <summary>
    /// The actual session: connect, pump audio, react to events, produce one verdict, always tear down.
    /// </summary>
    private async Task<UtteranceResult> ListenOnceCoreAsync(long startedAt, CancellationToken cancellationToken)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await CaptureUntilVerdictAsync(startedAt, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await CloseTransportAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs the two pumps (microphone → Gateway, Gateway → decisions) until one exit condition fires:
    /// committed user transcript, silence, utterance cap, protocol abort, or transport death.
    /// </summary>
    private async Task<UtteranceResult> CaptureUntilVerdictAsync(long startedAt, CancellationToken cancellationToken)
    {
        if (_inbox is null)
        {
            throw new VoiceClientException("The Gateway inbox was not created; cannot listen.");
        }

        var reader = _inbox.Reader;
        var frames = Channel.CreateBounded<byte[]>(
            // 100 blocks × 100 ms = 10 s: a ceiling on memory if the Gateway stops draining us, not a
            // quality choice. DropOldest rather than block: a stalled Gateway must never grow a backlog
            // on the capture thread.
            new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });

        // Sending is delegated to a pump because a WebSocket send must not run on the winmm capture
        // thread: dropping a frame costs a glitch, blocking capture costs everything after it.
        var sendPump = SendAudioPumpAsync(frames.Reader, cancellationToken);
        EventHandler<MicrophoneFrameEventArgs> onFrame = (_, e) =>
        {
            if (Volatile.Read(ref _captureSuspended) == 0)
            {
                frames.Writer.TryWrite(e.Pcm.ToArray());
            }
        };

        _microphone.Frame += onFrame;
        try
        {
            _microphone.Start();
        }
        catch
        {
            _microphone.Frame -= onFrame;
            frames.Writer.TryComplete();
            throw;
        }

        // Deadlines rather than blocking waits, so the loop wakes for time even when the Gateway is silent.
        var silenceDeadline = DateTimeOffset.UtcNow + _options.NoSpeechTimeout;
        var utteranceDeadline = DateTimeOffset.UtcNow + _options.MaxUtterance;
        string? transportGone = null;
        Volatile.Write(ref _captureSuspended, 0);

        try
        {
            while (true)
            {
                var now = DateTimeOffset.UtcNow;
                if (now >= utteranceDeadline)
                {
                    // Cap reached with no committed transcript. Deliberately NOT promoted from a partial:
                    // an uncommitted recognition is exactly the kind of text the Tutor must never be asked
                    // to answer from, so silence is the honest verdict here.
                    break;
                }

                if (now >= silenceDeadline)
                {
                    break; // Nobody spoke (or nothing committed) within the silence window → IsNoSpeech.
                }

                var wakeBy = (silenceDeadline < utteranceDeadline ? silenceDeadline : utteranceDeadline) - now;

                // No (timeout, token) overload of WaitToReadAsync exists on this TFM, so the deadline is
                // enforced with a linked token: its CancelAfter fires exactly at wakeBy, the wait unwinds,
                // and the loop top re-checks the real deadlines. The caller's own token is never masked —
                // an OCE from it has cancellationRequested and propagates instead of looping.
                bool ready;
                using (var wake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    wake.CancelAfter(wakeBy);
                    try
                    {
                        ready = await reader.WaitToReadAsync(wake.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        continue; // Our wake timer fired → loop top re-evaluates silence/utterance deadlines.
                    }
                }

                if (!ready)
                {
                    // The channel completed: the receive pump died or the socket closed under us.
                    transportGone = _transportFault ?? "the Gateway connection ended before an answer arrived";
                    break;
                }

                while (reader.TryRead(out var decision))
                {
                    switch (decision.Kind)
                    {
                        case GatewayEventKind.UserFinal:
                            if (!string.IsNullOrWhiteSpace(decision.Text))
                            {
                                return UtteranceResult.Spoken(
                                    decision.Text!,
                                    _gateway.FrontendId,
                                    Stopwatch.GetElapsedTime(startedAt),
                                    (int)_ledger.SuppressedAssistantEvents);
                            }

                            // A final with empty content is not a question: treat it as silence and listen on.
                            silenceDeadline = DateTimeOffset.UtcNow + _options.NoSpeechTimeout;
                            break;

                        case GatewayEventKind.UserPartial:
                            silenceDeadline = DateTimeOffset.UtcNow + _options.NoSpeechTimeout;
                            if (!string.IsNullOrWhiteSpace(decision.Text))
                            {
                                Partial?.Invoke(this, new VoicePartialEventArgs(decision.Text!));
                            }

                            break;

                        case GatewayEventKind.AssistantResponseStarted:
                            // DEC-013's countermeasure: the frontend began answering on its own, so cancel
                            // it. Prevention at the source does not exist for live audio (DEC-013 records
                            // the investigation); this is the supported stop, and the ledger counts use.
                            await SendAsync(QwenGatewayProtocol.BuildResponseCancel(), cancellationToken).ConfigureAwait(false);
                            await SendAsync(QwenGatewayProtocol.BuildInterrupt(), cancellationToken).ConfigureAwait(false);
                            _ledger.RecordCancelIssued();
                            break;

                        case GatewayEventKind.PlaybackClear:
                            // Nothing is playing here — this client never plays assistant audio — but the
                            // receipt keeps the Gateway's playback bookkeeping honest.
                            await SendAsync(QwenGatewayProtocol.BuildPlaybackCancelled(), cancellationToken).ConfigureAwait(false);
                            break;

                        case GatewayEventKind.InputSuspend:
                            // Another host took (or the Gateway released) the voice surface: stop feeding
                            // audio rather than fight over the owner slot; input.resume undoes it.
                            Volatile.Write(ref _captureSuspended, 1);
                            await SendAsync(
                                QwenGatewayProtocol.BuildInputSuspendAck(
                                    decision.State ?? "gateway", "capture suspended by host"),
                                cancellationToken).ConfigureAwait(false);
                            break;

                        case GatewayEventKind.InputResume:
                            Volatile.Write(ref _captureSuspended, 0);
                            silenceDeadline = DateTimeOffset.UtcNow + _options.NoSpeechTimeout;
                            break;

                        case GatewayEventKind.TaskEvent:
                            // Backend Agent protocol is on despite AGENT_PROTOCOL=none. Abort: a listen that
                            // continues here is a listen where an external agent could answer for the Tutor.
                            throw new VoiceProtocolException(
                                $"A Backend Agent event ('{decision.EventType}') arrived on a session that " +
                                "requires AGENT_PROTOCOL=none. Aborting the listen; check the Gateway's " +
                                "AGENT_PROTOCOL configuration.");

                        case GatewayEventKind.Error:
                            // The Gateway actively rejected something. Protocol-class (not transient):
                            // retrying replays the same rejection.
                            throw new VoiceProtocolException(
                                $"The Gateway reported an error: {decision.ErrorMessage}");

                        case GatewayEventKind.SessionPing:
                            await SendAsync(QwenGatewayProtocol.BuildSessionPong(), cancellationToken).ConfigureAwait(false);
                            break;

                        default:
                            // Assistant text/audio, voice.* lifecycle, tool.call, memory, unknown kinds —
                            // already classified and counted by the ledger; none can become a verdict.
                            break;
                    }
                }
            }
        }
        finally
        {
            _microphone.Frame -= onFrame;
            frames.Writer.TryComplete();
            try
            {
                _microphone.Stop();
            }
            catch (VoiceClientException)
            {
                // A teardown failure must not mask the verdict being returned; if the device stays broken
                // the next listen surfaces it.
            }

            await sendPump.ConfigureAwait(false);
        }

        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var suppressed = (int)_ledger.SuppressedAssistantEvents;

        return transportGone is not null
            ? UtteranceResult.Failure(transportGone, _gateway.FrontendId, elapsed, suppressed)
            : UtteranceResult.NoSpeech(_gateway.FrontendId, elapsed, suppressed);
    }

    /// <summary>
    /// Moves captured frames onto the wire. Never throws at the capture thread — a send failure fails the
    /// transport instead, and the verdict loop reports that as a failure rather than as silence.
    /// </summary>
    private async Task SendAudioPumpAsync(ChannelReader<byte[]> reader, CancellationToken cancellationToken)
    {
        await foreach (var frame in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await SendAsync(
                    QwenGatewayProtocol.BuildAudioAppend(frame, _gateway.InputSampleRate),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (VoiceClientException ex)
            {
                FailTransport(ex.Message);
                return;
            }
        }
    }

    /// <summary>
    /// Closes the socket and waits (briefly) for the receive pump. Best effort by design: teardown runs
    /// in a finally path, so an error here must never replace the verdict or exception already on its way.
    /// </summary>
    private async Task CloseTransportAsync()
    {
        var socket = _socket;
        var pump = _receivePump;
        _socket = null;
        _receivePump = null;

        if (socket is not null)
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await socket
                        .CloseAsync(WebSocketCloseStatus.NormalClosure, "listen complete", budget.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                // Best effort: the dispose below runs regardless.
            }
            finally
            {
                socket.Dispose();
            }
        }

        if (pump is not null)
        {
            try
            {
                await pump.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // The socket is disposed, so a pending receive faults and unwinds on its own.
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
            {
                // The pump reports transport faults through the ledger, not through teardown.
            }
        }

        _inbox?.Writer.TryComplete();
    }
}


