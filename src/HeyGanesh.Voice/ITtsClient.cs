namespace HeyGanesh.Voice;

/// <summary>
/// One piece of decoded speech audio: linear PCM, the format <see cref="IAudioOutput"/> plays.
/// </summary>
/// <remarks>
/// The carrier format (WAV container, streaming chunks, provider quirks) belongs to the
/// <see cref="ITtsClient"/> implementation, which strips it and hands back samples. That division is
/// deliberate: it keeps the container knowledge in the one file that knows about the provider, and it
/// lets the duration in <see cref="SpeakResult"/> be computed from the audio that actually moved rather
/// than from a header's claim.
/// </remarks>
public readonly record struct TtsAudioChunk
{
    /// <summary>PCM samples, little-endian, 16-bit, mono at <see cref="SampleRate"/>.</summary>
    public ReadOnlyMemory<byte> Pcm { get; init; }

    /// <summary>Samples per second of <see cref="Pcm"/>. 16-bit mono is the only supported shape for now.</summary>
    public int SampleRate { get; init; }

    /// <summary>True on the last chunk of a synthesis, so playback can close cleanly without guessing.</summary>
    public bool IsFinal { get; init; }
}

/// <summary>
/// The speech-out seam — the single boundary between HeyGanesh and whatever synthesises speech.
/// </summary>
/// <remarks>
/// <para>
/// Today <see cref="FishTtsClient"/> implements it against Fish Audio's <c>POST /v1/tts</c>. The
/// interface is streaming-shaped (an async sequence of PCM chunks) because that is how the audio actually
/// arrives, NOT because generation is guaranteed to be incremental: see DEC-013's note — HTTP response
/// chunking is transport streaming, and no first-audio-latency claim is made for it. A provider that
/// returns one large body simply yields one large chunk.
/// </para>
/// <para>
/// Failures use the same typed exception family as the listener, so a host can tell "no key" (exit 12)
/// from "provider broke" (exit 3) from "the user cancelled" (exit 13) without string-matching.
/// </para>
/// </remarks>
public interface ITtsClient : IDisposable
{
    /// <summary>
    /// Synthesises <paramref name="request"/> and streams its PCM. The sequence is lazy: nothing is sent
    /// to the provider until the caller enumerates it.
    /// </summary>
    IAsyncEnumerable<TtsAudioChunk> SynthesizeAsync(SpeakRequest request, CancellationToken cancellationToken);
}
