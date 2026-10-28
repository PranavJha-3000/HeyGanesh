namespace HeyGanesh.Voice;

/// <summary>
/// One utterance's verdict: what the user said, or an honest "nothing was said", or a failure that
/// produced no transcript at all.
/// </summary>
/// <remarks>
/// <para>
/// The shape copies the discipline the coordinate legs already established: a NEGATIVE answer
/// (<see cref="IsNoSpeech"/>) is a success and carries no <see cref="FailureReason"/>, while a failure
/// means no verdict was produced. Callers branch on <see cref="Failed"/> first, exactly as the Screen
/// Overlay checks a failed verdict before a found one — a failed listen must never be read as "the user
/// said nothing" and quietly skipped, because then a broken microphone looks like a quiet user.
/// </para>
/// <para>
/// <see cref="FinalText"/> is the only text this module ever hands to the rest of HeyGanesh, and it is
/// assembled exclusively from the <c>transcript.final</c> Gateway event whose <c>role</c> is
/// <c>user</c>. Assistant-role text never reaches this type (see
/// <see cref="GatewaySuppressionLedger"/>), which is what keeps the future Tutor Engine the only thing
/// that answers the user.
/// </para>
/// </remarks>
public sealed record UtteranceResult
{
    /// <summary>The user's recognised speech. Null when nothing was said or the listen failed.</summary>
    public string? FinalText { get; init; }

    /// <summary>
    /// True when the microphone was open but no recognisable user speech arrived (silence, or a partial
    /// utterance that was discarded). A real answer to "did the user say anything?" — not an error.
    /// </summary>
    public bool IsNoSpeech { get; init; }

    /// <summary>Non-null when no transcript could be produced. Carried verbatim from the listener.</summary>
    public string? FailureReason { get; init; }

    /// <summary>Convenience: true when a failure prevented a transcript.</summary>
    public bool Failed => FailureReason is not null;

    /// <summary>Which realtime frontend produced this verdict, for diagnostics. Never a URL or a key.</summary>
    public string FrontendId { get; init; } = string.Empty;

    /// <summary>Wall-clock time from the start of the listen to this verdict.</summary>
    public TimeSpan ListenLatency { get; init; }

    /// <summary>
    /// How many assistant-side events the frontend emitted and this module neutralised during the same
    /// window. Exposed because it is the honest measure of the limitation in DEC-013: the Qwen frontend
    /// generates a conversational answer whether or not HeyGanesh wants one, and this counts the ones we
    /// cancelled. Zero here means the frontend stayed quiet, not that we prevented it.
    /// </summary>
    public int SuppressedAssistantEvents { get; init; }

    /// <summary>An utterance was recognised.</summary>
    public static UtteranceResult Spoken(string text, string frontendId, TimeSpan latency, int suppressed) => new()
    {
        FinalText = text,
        FrontendId = frontendId,
        ListenLatency = latency,
        SuppressedAssistantEvents = suppressed,
    };

    /// <summary>The microphone was open and the user said nothing recognisable.</summary>
    public static UtteranceResult NoSpeech(string frontendId, TimeSpan latency, int suppressed) => new()
    {
        IsNoSpeech = true,
        FrontendId = frontendId,
        ListenLatency = latency,
        SuppressedAssistantEvents = suppressed,
    };

    /// <summary>No transcript was produced. The reason is carried, never re-interpreted.</summary>
    public static UtteranceResult Failure(string reason, string frontendId, TimeSpan latency, int suppressed) => new()
    {
        FailureReason = reason,
        FrontendId = frontendId,
        ListenLatency = latency,
        SuppressedAssistantEvents = suppressed,
    };

    public override string ToString()
    {
        if (Failed) return $"UtteranceResult: failed via {FrontendId} ({FailureReason})";
        if (IsNoSpeech) return $"UtteranceResult: no speech via {FrontendId} (suppressed={SuppressedAssistantEvents})";
        return $"UtteranceResult: \"{FinalText}\" via {FrontendId} (suppressed={SuppressedAssistantEvents})";
    }
}
