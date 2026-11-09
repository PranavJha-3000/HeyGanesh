namespace HeyGanesh.Voice;

/// <summary>
/// How <see cref="GnaniSttListener"/> talks to Gnani's Prisma. Immutable, set once, like <see cref="GnaniTtsOptions"/>.
/// </summary>
/// <remarks>
/// No credential lives here (DEC-007). Endpoint and header follow docs.gnani.ai (POST /stt/v3, multipart,
/// header X-API-Key-ID, clips of at most 30 seconds). The end of an utterance is decided locally by
/// <see cref="SpeechRms"/> and <see cref="EndSilence"/>, because the REST endpoint transcribes a finished clip.
/// </remarks>
public sealed class GnaniSttOptions
{
    public static GnaniSttOptions Default { get; } = new();

    public string BaseUrl { get; init; } = "https://api.vachana.ai";

    public string SttPath { get; init; } = "/stt/v3";

    /// <summary>Environment variable holding the key, read fresh on every transcription.</summary>
    public string ApiKeyEnvVar { get; init; } = "GNANI_API_KEY";

    /// <summary>Per-request key source (DEC-046); null or blank is a missing key.</summary>
    public Func<string?>? ApiKey { get; init; }

    /// <summary>prisma-v3.0 or prisma-v2.5 (v2.5 takes one language and no codemix).</summary>
    public string Model { get; init; } = "prisma-v3.0";

    /// <summary>Prisma v3.0 takes up to four codes as hints; the transcript follows the language actually spoken.</summary>
    public string LanguageCode { get; init; } = "en-IN,hi-IN";

    /// <summary>"codemix" keeps English words in Latin script, which suits a question about a screen (v3.0 only).</summary>
    public string Format { get; init; } = "codemix";

    /// <summary>Mean amplitude (of 32768) above which a 100 ms block counts as speech; about -36 dBFS.</summary>
    public double SpeechRms { get; init; } = 500;

    /// <summary>Quiet time after speech that ends the utterance.</summary>
    public TimeSpan EndSilence { get; init; } = TimeSpan.FromMilliseconds(1000);

    /// <summary>Blocks kept from before the first loud block, so the start of a word is not clipped.</summary>
    public int PreRollFrames { get; init; } = 3;

    /// <summary>The endpoint's documented limit is 30 s; stay under it.</summary>
    public TimeSpan MaxClip { get; init; } = TimeSpan.FromSeconds(29);

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw new ArgumentOutOfRangeException(nameof(BaseUrl), "A Gnani endpoint is required.");
        }

        if (Model is not ("prisma-v3.0" or "prisma-v2.5"))
        {
            throw new ArgumentOutOfRangeException(nameof(Model), "Gnani documents prisma-v2.5 and prisma-v3.0.");
        }

        if (string.IsNullOrWhiteSpace(LanguageCode))
        {
            throw new ArgumentOutOfRangeException(nameof(LanguageCode), "Gnani requires a language code.");
        }

        if (MaxClip > TimeSpan.FromSeconds(30) || MaxClip <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxClip), "Gnani's REST limit is 30 seconds.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(SpeechRms);
        ArgumentOutOfRangeException.ThrowIfNegative(PreRollFrames);
    }
}
