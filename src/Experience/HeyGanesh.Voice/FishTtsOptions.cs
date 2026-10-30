namespace HeyGanesh.Voice;

/// <summary>
/// How <see cref="FishTtsClient"/> talks to Fish Audio. Immutable, set once, like every other options type.
/// </summary>
/// <remarks>
/// No credential lives here (DEC-007 extended to Voice): the bearer token is read from
/// <see cref="ApiKeyEnvVar"/> at call time, never stored, never logged, never put in a result object.
/// </remarks>
public sealed class FishTtsOptions
{
    /// <summary>Defaults: WAV 16-bit mono @ 44.1 kHz, balanced latency, endpoint from the environment convention.</summary>
    public static FishTtsOptions Default { get; } = new();

    /// <summary>Fish Audio base URL. Override to route through the future Security Proxy (DEC-007's swap story).</summary>
    public string BaseUrl { get; init; } = "https://api.fish.audio";

    /// <summary>TTS path on <see cref="BaseUrl"/>.</summary>
    public string TtsPath { get; init; } = "/v1/tts";

    /// <summary>Environment variable holding the bearer token, read fresh on every synthesis.</summary>
    public string ApiKeyEnvVar { get; init; } = "FISH_AUDIO_API_KEY";

    /// <summary>
    /// Where the key comes from, per request (DEC-046): the App Shell passes its key ring's reader
    /// (Windows Credential Manager, then the environment). Null keeps this client's own environment read,
    /// so hosts and rows that pass nothing behave exactly as before. A delegate that returns null or blank
    /// is a missing key: the call fails as an authentication fault before any byte is sent.
    /// </summary>
    public Func<string?>? ApiKey { get; init; }

    /// <summary>
    /// Model sent in the <c>model</c> header. The v1 endpoint documents model selection through a header
    /// rather than the JSON body; if the provider rejects a name here, change this one field.
    /// </summary>
    public string Model { get; init; } = "s2.1-pro";

    /// <summary>
    /// The chosen v1 output format. WAV because it carries its own header: the module can validate what it
    /// received and play it with zero decoder dependencies, which the zero-NuGet invariant requires.
    /// The verified v1 enum is exactly wav | pcm | mp3 | opus — mp3/opus would need decoder packages and
    /// raw pcm has no header to validate or frame by.
    /// </summary>
    public string Format { get; init; } = "wav";

    /// <summary>Sample rate requested for WAV/PCM output. 44.1 kHz is in the provider's documented supported set (8/16/24/32/44.1).</summary>
    public int SampleRate { get; init; } = 44100;

    /// <summary>Latency/quality trade-off the provider honours: "normal", "balanced" or "low".</summary>
    public string Latency { get; init; } = "balanced";

    /// <summary>Whether the provider normalises input text (SSRF-clean pass over URLs etc.).</summary>
    public bool Normalize { get; init; } = true;

    /// <summary>Text length the provider chunks internally, in characters (its documented knob, default 300).</summary>
    public int ChunkLength { get; init; } = 300;

    /// <summary>Default voice reference (<c>reference_id</c>) when <see cref="SpeakRequest.VoiceId"/> is null.</summary>
    public string? DefaultVoiceId { get; init; }

    /// <summary>Per-request HTTP timeout. The higher-level <see cref="VoiceOptions.SpeakTimeout"/> may cut a call short before this.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(60);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw new ArgumentOutOfRangeException(nameof(BaseUrl), "A Fish Audio endpoint is required.");
        }

        if (!string.Equals(Format, "wav", StringComparison.OrdinalIgnoreCase))
        {
            // Guarding the decision, not the value: the output side is built for header-bearing WAV (see
            // WinMmAudioOutput). Accepting another format silently would push the break into the speaker.
            throw new ArgumentOutOfRangeException(
                nameof(Format),
                "This module's audio output consumes WAV. The verified v1 enum is wav | pcm | mp3 | opus; " +
                "choosing pcm/mp3/opus requires changing ITtsClient's decoded-PCM contract first.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(SampleRate, 8000);
        ArgumentOutOfRangeException.ThrowIfLessThan(ChunkLength, 1);
    }
}
