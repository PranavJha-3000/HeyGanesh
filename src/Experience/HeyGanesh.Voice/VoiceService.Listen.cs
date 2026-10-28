namespace HeyGanesh.Voice;

public sealed partial class VoiceService
{
    /// <summary>
    /// Listens for exactly one utterance and returns what the user said, or an honest
    /// <see cref="UtteranceResult.IsNoSpeech"/>, or a failure.
    /// </summary>
    /// <remarks>
    /// This is the method the host will call on the push-to-talk press (or that a future shell calls on
    /// press and finalises on release). It returns a task immediately; the hook callback is never blocked.
    /// The result is the ONLY text HeyGanesh takes from the speech leg — the recognised transcript of the
    /// user's own speech, never anything the frontend generated.
    /// </remarks>
    public Task<UtteranceResult> ListenOnceAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            EnterSession("listen");
            _listen = ListenCoreAsync(cancellationToken);
            return _listen;
        }
    }

    private async Task<UtteranceResult> ListenCoreAsync(CancellationToken callerToken)
    {
        // Link the caller's token with the service lifetime: Dispose() aborts in-flight work too.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, _lifetime.Token);
        try
        {
            // No budget is applied here beyond the listener's own NoSpeechTimeout / MaxUtterance: an
            // utterance ends when speech ends, and a hard wall-clock cap on the whole call would cut off
            // a legitimate long sentence. The listener owns the "nobody spoke" verdict (DEC-013).
            return await _listener.ListenOnceAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _listen = null;
            }
        }
    }
}
