namespace HeyGanesh.Voice;

/// <summary>
/// The speaker seam: plays a stream of PCM chunks, and stops immediately when the token fires.
/// </summary>
/// <remarks>
/// <para>
/// Split out from <see cref="ITtsClient"/> on purpose, so "what was said" and "where it comes out" can
/// change independently — a future audio device router, a virtual cable, or a recording fake for
/// verification all become one constructor argument.
/// </para>
/// <para>
/// Implementations must consume the whole sequence (or abandon it on cancellation) and must not buffer
/// the entire utterance before starting playback unless they have no choice; the default
/// <see cref="WinMmAudioOutput"/> writes each chunk as it arrives and waits for that chunk to finish.
/// </para>
/// </remarks>
public interface IAudioOutput : IDisposable
{
    /// <summary>Plays <paramref name="audio"/> to completion, or throws/returns early on cancellation.</summary>
    Task PlayAsync(IAsyncEnumerable<TtsAudioChunk> audio, CancellationToken cancellationToken);
}
