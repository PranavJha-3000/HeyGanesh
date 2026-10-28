namespace HeyGanesh.Voice;

/// <summary>
/// Tuning for <see cref="VoiceService"/> and the provider implementations behind its seams. Immutable,
/// set once, mirroring the other modules' options types.
/// </summary>
/// <remarks>
/// No credentials live here. The Qwen Gateway's provider key belongs to the Gateway's own configuration
/// (it is a separate process HeyGanesh does not own, and no key crosses the Gateway Client Protocol),
/// and Fish Audio's key is resolved from the process environment inside <see cref="FishTtsClient"/> at
/// call time — DEC-007's temporary stopgap, extended to Voice.
/// </remarks>
public sealed class VoiceOptions
{
    /// <summary>Defaults: 5 s of silence ends an utterance, 30 s caps one, 3 handshake retries.</summary>
    public static VoiceOptions Default { get; } = new();

    /// <summary>
    /// How long one utterance may go without any recognised speech before it is reported as
    /// <see cref="UtteranceResult.IsNoSpeech"/>.
    /// </summary>
    /// <remarks>
    /// This is the "user pressed the chord and said nothing" case, and it must never be modelled as a
    /// failure: silence is a verdict, not an error (the same distinction Element Selection draws between
    /// an honest no-match and a failed selection).
    /// </remarks>
    public TimeSpan NoSpeechTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Hard cap on a single utterance, after which the transcript collected so far is returned and the
    /// microphone is closed. Bounded on purpose: an open microphone with no end condition is the one
    /// failure mode this module must never have.
    /// </summary>
    public TimeSpan MaxUtterance { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Budget for the WebSocket connect plus the <c>session.hello</c>/<c>session.ready</c> handshake.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Budget for a whole <see cref="VoiceService.SpeakAsync"/> call, header validation included.</summary>
    public TimeSpan SpeakTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many times the handshake may be retried inside one listen before the failure is surfaced.
    /// The transport is created per utterance, so a retry here is what "reconnect" means for a
    /// press-to-talk client (see <see cref="VoiceOptions.MaxConsecutiveFailures"/> for the cross-utterance latch).
    /// </summary>
    public int MaxReconnectAttempts { get; init; } = 3;

    /// <summary>
    /// Consecutive failed utterances after which the listener refuses further attempts until it is
    /// disposed and re-created. Three is deliberately low: a Gateway that is down stays down, and a
    /// shouting loop of failed provider calls helps nobody.
    /// </summary>
    public int MaxConsecutiveFailures { get; init; } = 3;

    internal void Validate()
    {
        if (NoSpeechTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(NoSpeechTimeout), "NoSpeechTimeout must be positive.");
        }

        if (MaxUtterance < NoSpeechTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxUtterance),
                "MaxUtterance must not be shorter than NoSpeechTimeout, or every utterance would end as silence.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(MaxReconnectAttempts, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxConsecutiveFailures, 1);
    }
}
