using HeyGanesh.Voice;

namespace HeyGanesh.Tools.VoiceSmoke;

/// <summary>
/// Offline seams for the smoke host. Each mirrors a real failure/success mode so the row output means
/// what its name says: a fake auth error is a real <see cref="VoiceAuthenticationException"/>, a fake
/// transcript is a real <see cref="UtteranceResult"/>, and the fake TTS produces real PCM that the real
/// <see cref="WinMmAudioOutput"/> can actually play.
/// </summary>
internal sealed class FakeListener : IVoiceListener
{
    internal enum Mode
    {
        /// <summary>Committed transcript, after an optional delay (delay lets the burst row race it).</summary>
        Transcript,

        /// <summary>Honest silence verdict.</summary>
        NoSpeech,

        /// <summary>Throws the typed auth failure (the exit-12 path).</summary>
        AuthError,

        /// <summary>Hangs until cancelled — the exit-13 row and the burst row use this.</summary>
        Hang,
    }

    private readonly Mode _mode;
    private readonly string _text;
    private readonly TimeSpan _delay;

    internal FakeListener(Mode mode, string text = "where is the Save button", TimeSpan delay = default)
    {
        _mode = mode;
        _text = text;
        _delay = delay;
    }

    public event EventHandler<VoicePartialEventArgs>? Partial;

    public async Task<UtteranceResult> ListenOnceAsync(CancellationToken cancellationToken)
    {
        if (_delay > TimeSpan.Zero)
        {
            await Task.Delay(_delay, cancellationToken);
        }

        switch (_mode)
        {
            case Mode.Transcript:
                // An interim event first, exactly like the real pipeline (subscribers see it; the verdict
                // does not come from it).
                Partial?.Invoke(this, new VoicePartialEventArgs(_text));
                return UtteranceResult.Spoken(_text, "fake-listener", TimeSpan.FromMilliseconds(5), 2);

            case Mode.NoSpeech:
                return UtteranceResult.NoSpeech("fake-listener", TimeSpan.FromMilliseconds(5), 0);

            case Mode.AuthError:
                throw new VoiceAuthenticationException(
                    "fake key gate: the provider rejected credentials (this is the exit-12 path)");

            case Mode.Hang:
            default:
                // Waits for the caller's token: cancellation must propagate as OCE, never as a verdict.
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("unreachable: Task.Delay only returns by cancellation");
        }
    }

    public void Dispose() { }
}

/// <summary>
/// Fake TTS producing a short sine in real PCM — deliberately the same shape Fish returns (mono PCM16),
/// so the real <see cref="WinMmAudioOutput"/> plays it and the duration math in SpeakResult is exercised
/// for real rather than against a stub.
/// </summary>
internal sealed class FakeTts : ITtsClient
{
    internal const int Rate = 44100;

    public async IAsyncEnumerable<TtsAudioChunk> SynthesizeAsync(
        SpeakRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // 0.35 s of a 440 Hz sine at modest amplitude — audible proof the whole out-leg works.
        var samples = (int)(Rate * 0.35);
        var pcm = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var value = (short)(Math.Sin(2 * Math.PI * 440 * i / Rate) * 8000);
            pcm[i * 2] = (byte)(value & 0xFF);
            pcm[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }

        // Split into two chunks so streaming (not one-blob delivery) is what the output consumes.
        const int firstHalf = 16384;
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return new TtsAudioChunk { Pcm = pcm.AsMemory(0, firstHalf), SampleRate = Rate, IsFinal = false };
        yield return new TtsAudioChunk { Pcm = pcm.AsMemory(firstHalf), SampleRate = Rate, IsFinal = true };
    }

    public void Dispose() { }
}

/// <summary>
/// Minimal WAV bytes (RIFF/WAVE, PCM tag 1, mono, 16-bit) for the offline WAV-contract rows, plus
/// malformed variants so the "fail loud" paths can be proven without a provider.
/// </summary>
internal static class WavFixture
{
    /// <summary>0.1 s of silence as a valid mono 16-bit 44.1 kHz WAV.</summary>
    internal static byte[] Valid(int sampleRate = 44100, short channels = 1, int samples = 4410)
    {
        var dataBytes = samples * channels * 2;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);              // PCM
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(sampleRate * channels * 2);
        w.Write((short)(channels * 2)); // block align
        w.Write((short)16);             // bits
        w.Write("data"u8);
        w.Write(dataBytes);
        w.Write(new byte[dataBytes]);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Not a WAV at all (starts with MP3 frame-sync bytes) — the wrong-magic row.</summary>
    internal static byte[] NotWav() => [0xFF, 0xFB, 0x90, 0x00, 0x00, 0x01, 0x02, 0x03];
}
