using System.Text.Json;
using System.Threading.Channels;

namespace HeyGanesh.Voice;

/// <summary>
/// Gnani Prisma behind <see cref="IVoiceListener"/>: records one utterance from the microphone, ends it when
/// the speaker goes quiet, sends the clip to <c>POST /stt/v3</c> and returns the transcript. It replaces the
/// external qwen-audio-agent Gateway when a gnani key is set.
/// </summary>
/// <remarks>
/// <para>
/// The REST endpoint transcribes a finished clip, so the end of speech is a local rule (a quiet stretch after
/// loud blocks, <see cref="GnaniSttOptions.EndSilence"/>), not the provider's. That is simple energy detection:
/// a noisy room can keep it open until <see cref="GnaniSttOptions.MaxClip"/>, and a very quiet speaker can be
/// heard as silence (tune <see cref="GnaniSttOptions.SpeechRms"/>).
/// </para>
/// <para>
/// Contract kept from <see cref="IVoiceListener"/>: silence is <see cref="UtteranceResult.IsNoSpeech"/> (also an
/// empty transcript), never an error; a missing key fails as authentication before the microphone opens; auth,
/// rate limit and transport faults are the typed <see cref="VoiceClientException"/> family. The microphone is
/// always stopped. The key is read per call and never cached or logged.
/// </para>
/// </remarks>
public sealed class GnaniSttListener : IVoiceListener
{
    /// <summary>Reported as <see cref="UtteranceResult.FrontendId"/>.</summary>
    public const string FrontendName = "gnani-prisma";

    private readonly GnaniSttOptions _options;
    private readonly VoiceOptions _voice;
    private readonly IMicrophoneSource _microphone;
    private readonly bool _ownsMicrophone;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public GnaniSttListener(
        GnaniSttOptions? options = null,
        VoiceOptions? voiceOptions = null,
        IMicrophoneSource? microphone = null,
        HttpClient? httpClient = null)
    {
        _options = options ?? GnaniSttOptions.Default;
        _options.Validate();
        _voice = voiceOptions ?? VoiceOptions.Default;

        _ownsMicrophone = microphone is null;
        _microphone = microphone ?? new WinMmMicrophoneSource();

        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient(new SocketsHttpHandler(), disposeHandler: true) { Timeout = _options.RequestTimeout };
    }

    /// <summary>The REST endpoint returns one final transcript, so there are no interim results to raise.</summary>
    public event EventHandler<VoicePartialEventArgs>? Partial
    {
        add { }
        remove { }
    }

    public async Task<UtteranceResult> ListenOnceAsync(CancellationToken cancellationToken)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();

        // Key first: a missing key must not open the microphone (and the user sees the cause at once).
        var key = _options.ApiKey is { } apiKey ? apiKey() : Environment.GetEnvironmentVariable(_options.ApiKeyEnvVar);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new VoiceAuthenticationException(
                $"Gnani key not found. Set the {_options.ApiKeyEnvVar} environment variable or store the gnani key. " +
                "(The app reads keys through its key ring, DEC-046: HeyGanesh.App --set-key gnani stores one.)");
        }

        var frames = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        EventHandler<MicrophoneFrameEventArgs> onFrame = (_, e) => frames.Writer.TryWrite(e.Pcm.ToArray());

        byte[] pcm;
        var rate = _microphone.SampleRate;
        _microphone.Frame += onFrame;
        try
        {
            _microphone.Start();
            pcm = await CollectAsync(frames.Reader, rate, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _microphone.Frame -= onFrame;
            _microphone.Stop();
        }

        if (pcm.Length == 0)
        {
            return UtteranceResult.NoSpeech(FrontendName, clock.Elapsed, 0);
        }

        var text = await TranscribeAsync(key, BuildWav(pcm, rate), cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text)
            ? UtteranceResult.NoSpeech(FrontendName, clock.Elapsed, 0)
            : UtteranceResult.Spoken(text, FrontendName, clock.Elapsed, 0);
    }

    /// <summary>Reads 100 ms blocks until speech has started and then stopped; empty when nobody spoke in time.</summary>
    private async Task<byte[]> CollectAsync(ChannelReader<byte[]> reader, int rate, CancellationToken ct)
    {
        var preRoll = new Queue<byte[]>();
        using var speech = new MemoryStream();
        var spoke = false;
        var elapsed = TimeSpan.Zero;
        var quiet = TimeSpan.Zero;
        var cap = _voice.MaxUtterance < _options.MaxClip ? _voice.MaxUtterance : _options.MaxClip;

        await foreach (var block in reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            var length = TimeSpan.FromSeconds(block.Length / 2.0 / rate);
            elapsed += length;
            var loud = Rms(block) >= _options.SpeechRms;

            if (!spoke)
            {
                if (loud)
                {
                    spoke = true;
                    foreach (var earlier in preRoll)
                    {
                        speech.Write(earlier);
                    }

                    speech.Write(block);
                }
                else
                {
                    preRoll.Enqueue(block);
                    while (preRoll.Count > _options.PreRollFrames)
                    {
                        preRoll.Dequeue();
                    }

                    if (elapsed >= _voice.NoSpeechTimeout)
                    {
                        return [];
                    }
                }

                continue;
            }

            speech.Write(block);
            quiet = loud ? TimeSpan.Zero : quiet + length;
            if (quiet >= _options.EndSilence || elapsed >= cap)
            {
                break;
            }
        }

        return speech.ToArray();
    }

    private async Task<string> TranscribeAsync(string key, byte[] wav, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var audio = new ByteArrayContent(wav);
        audio.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        form.Add(audio, "audio_file", "utterance.wav");
        form.Add(new StringContent(_options.Model), "model");
        form.Add(new StringContent(_options.LanguageCode), "language_code");
        form.Add(new StringContent(_options.Format), "format");

        var endpoint = new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/"), _options.SttPath.TrimStart('/'));
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = form };
        message.Headers.TryAddWithoutValidation("X-API-Key-ID", key);

        using var response = await _http.SendAsync(message, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var excerpt = body.Length <= 300 ? body : body[..300] + "…";

            // 402 is "insufficient credit": fixing it needs the account, so it reads as auth (exit 12), as for TTS.
            if (status is 401 or 402 or 403)
            {
                throw new VoiceAuthenticationException($"Gnani rejected the request (HTTP {status}): {excerpt}");
            }

            if (status == 429)
            {
                throw new VoiceRateLimitException("Gnani rate limited the request (HTTP 429).");
            }

            throw new VoiceClientException($"Gnani returned HTTP {status}: {excerpt}", status);
        }

        return ParseTranscript(body);
    }

    /// <summary>The transcript of a 200 answer; empty is silence, a body that is not the documented shape is a typed fault.</summary>
    public static string ParseTranscript(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
            {
                throw new VoiceClientException("Gnani answered 200 with success=false.", 200);
            }

            return doc.RootElement.TryGetProperty("transcript", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()!.Trim()
                : throw new VoiceClientException("Gnani's answer had no transcript string.", 200);
        }
        catch (JsonException)
        {
            throw new VoiceClientException("Gnani's answer was not JSON.", 200);
        }
    }

    /// <summary>Mean absolute amplitude of PCM16 little-endian samples (0 to 32768).</summary>
    public static double Rms(ReadOnlySpan<byte> pcm)
    {
        var samples = pcm.Length / 2;
        if (samples == 0)
        {
            return 0;
        }

        double sum = 0;
        for (var i = 0; i < samples; i++)
        {
            sum += Math.Abs((double)BitConverter.ToInt16(pcm.Slice(i * 2, 2)));
        }

        return sum / samples;
    }

    /// <summary>A 44-byte RIFF header plus mono PCM16 data: the clip the endpoint accepts.</summary>
    public static byte[] BuildWav(byte[] pcm, int sampleRate)
    {
        using var ms = new MemoryStream(44 + pcm.Length);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36 + pcm.Length);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(sampleRate);
        w.Write(sampleRate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(pcm.Length);
        w.Write(pcm);
        return ms.ToArray();
    }

    public void Dispose()
    {
        if (_ownsMicrophone)
        {
            _microphone.Dispose();
        }

        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}
