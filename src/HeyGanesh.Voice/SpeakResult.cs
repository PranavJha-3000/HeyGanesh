namespace HeyGanesh.Voice;

/// <summary>
/// What actually happened when we spoke: how much text, how much audio, how long it took.
/// </summary>
/// <remarks>
/// Never carries the provider's payload, the request text, or a credential — the same stance the other
/// modules' result types take. <see cref="AudioDuration"/> is measured from the PCM this module received,
/// so it describes the audio that was actually played rather than what was asked for.
/// </remarks>
public sealed record SpeakResult
{
    /// <summary>Characters of text that were synthesised.</summary>
    public int SpokenChars { get; init; }

    /// <summary>Duration of the audio received, derived from the PCM byte count and its sample rate.</summary>
    public TimeSpan AudioDuration { get; init; }

    /// <summary>Wall-clock time for synthesis plus playback.</summary>
    public TimeSpan Latency { get; init; }

    /// <summary>The TTS provider that answered, for diagnostics. Never a URL or a key.</summary>
    public string ProviderId { get; init; } = string.Empty;

    public override string ToString() =>
        $"SpeakResult: {SpokenChars} chars, {AudioDuration.TotalSeconds:F2}s audio via {ProviderId} " +
        $"({Latency.TotalMilliseconds:F0} ms)";
}
