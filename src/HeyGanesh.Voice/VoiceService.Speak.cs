using System.Diagnostics;

namespace HeyGanesh.Voice;

public sealed partial class VoiceService
{
    /// <summary>
    /// Speaks <paramref name="request"/>.Text through the TTS seam and the speaker seam in one call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is where the future Tutor Engine's answer reaches the user's ears, and it is the only place
    /// HeyGanesh plays audio: the speech-in leg never plays anything it receives (DEC-013).
    /// </para>
    /// <para>
    /// The speak budget is enforced as a <b>failure</b>, not as cancellation: a synthesis that exceeds
    /// <see cref="VoiceOptions.SpeakTimeout"/> is a provider problem, so it surfaces as
    /// <see cref="VoiceClientException"/> (a host's exit-3 path) while a cancelled <i>caller</i> token — a
    /// Ctrl+C or a released chord — still propagates as <see cref="OperationCanceledException"/>
    /// (the exit-13 path). Merging the two would make a slow provider look like a user action.
    /// </para>
    /// </remarks>
    public Task<SpeakResult> SpeakAsync(SpeakRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            // Refused rather than sent: an empty synthesis would cost a provider round-trip and produce
            // an empty result a caller could mistake for "spoken".
            throw new ArgumentException("SpeakRequest.Text is required; there is nothing to speak.", nameof(request));
        }

        lock (_gate)
        {
            EnterSession("speak");
            _speak = SpeakCoreAsync(request, cancellationToken);
            return _speak;
        }
    }

    private async Task<SpeakResult> SpeakCoreAsync(SpeakRequest request, CancellationToken callerToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, _lifetime.Token);
        linked.CancelAfter(_options.SpeakTimeout);

        var token = linked.Token;
        var started = Stopwatch.GetTimestamp();

        // Counted as it flows to the speaker, so AudioDuration describes the audio that actually moved.
        long pcmBytes = 0;
        var sampleRate = 0;

        void Count(TtsAudioChunk chunk)
        {
            pcmBytes += chunk.Pcm.Length;
            if (chunk.SampleRate > 0) sampleRate = chunk.SampleRate;
        }

        try
        {
            await _output.PlayAsync(CountAsync(_tts.SynthesizeAsync(request, token), Count), token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            // Only the budget fired; the caller is still waiting for an answer.
            throw new VoiceClientException(
                $"Speech synthesis exceeded the {_options.SpeakTimeout.TotalSeconds:F0}s budget " +
                $"(or the output device stalled) and was abandoned; no complete audio was played.");
        }
        finally
        {
            lock (_gate)
            {
                _speak = null;
            }
        }

        // 16-bit mono: bytes / (2 bytes per sample) / sample rate.
        var duration = sampleRate > 0
            ? TimeSpan.FromSeconds((double)pcmBytes / (sampleRate * 2))
            : TimeSpan.Zero;

        return new SpeakResult
        {
            SpokenChars = request.Text.Length,
            AudioDuration = duration,
            Latency = Stopwatch.GetElapsedTime(started),
            ProviderId = "fish-audio",
        };
    }

    /// <summary>
    /// Passes chunks through untouched while tallying them. Deliberately a transparent wrapper: the
    /// speaker still receives the provider's stream as it arrives, so measuring does not decouple playback
    /// from synthesis.
    /// </summary>
    private static async IAsyncEnumerable<TtsAudioChunk> CountAsync(
        IAsyncEnumerable<TtsAudioChunk> source,
        Action<TtsAudioChunk> onChunk)
    {
        await foreach (var chunk in source.ConfigureAwait(false))
        {
            onChunk(chunk);
            yield return chunk;
        }
    }
}
