namespace HeyGanesh.Voice;

/// <summary>
/// How <see cref="GnaniTtsClient"/> talks to Gnani's Timbre. Immutable, set once, like <see cref="FishTtsOptions"/>.
/// </summary>
/// <remarks>
/// No credential lives here (DEC-007): the key is read per request from <see cref="ApiKey"/> or
/// <see cref="ApiKeyEnvVar"/>, never stored, never logged. Endpoint and header follow docs.gnani.ai
/// (POST /api/v1/tts/inference on api.vachana.ai, header X-API-Key-ID); the marketing page's
/// api.gnani.ai/v1/... with a Bearer token disagrees with the docs, so the docs win, and BaseUrl is the one
/// field to change if the provider moves.
/// </remarks>
public sealed class GnaniTtsOptions
{
    public static GnaniTtsOptions Default { get; } = new();

    public string BaseUrl { get; init; } = "https://api.vachana.ai";

    public string TtsPath { get; init; } = "/api/v1/tts/inference";

    /// <summary>Environment variable holding the key, read fresh on every synthesis.</summary>
    public string ApiKeyEnvVar { get; init; } = "GNANI_API_KEY";

    /// <summary>Per-request key source (DEC-046), as on <see cref="FishTtsOptions.ApiKey"/>; null or blank is a missing key.</summary>
    public Func<string?>? ApiKey { get; init; }

    /// <summary>timbre-v3.0 (expressive, audio tags) or timbre-v2.5. A voice only works with its own model.</summary>
    public string Model { get; init; } = "timbre-v3.0";

    /// <summary>Voice name from the chosen model's catalogue (docs: Available Voices). Every v3.0 voice speaks all 10 languages.</summary>
    public string Voice { get; init; } = "Shivani";

    /// <summary>"auto" detects from script, which suits an English lesson with Indian-language terms mixed in.</summary>
    public string Language { get; init; } = "auto";

    /// <summary>0.85 (slowest) to 1.15 (fastest), the documented range.</summary>
    public double Speed { get; init; } = 1.0;

    /// <summary>Documented set: 8000, 16000, 22050, 24000, 44100, 48000.</summary>
    public int SampleRate { get; init; } = 48000;

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(60);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw new ArgumentOutOfRangeException(nameof(BaseUrl), "A Gnani endpoint is required.");
        }

        if (Model is not ("timbre-v3.0" or "timbre-v2.5"))
        {
            throw new ArgumentOutOfRangeException(nameof(Model), "Gnani documents timbre-v2.5 and timbre-v3.0.");
        }

        if (string.IsNullOrWhiteSpace(Voice))
        {
            throw new ArgumentOutOfRangeException(nameof(Voice), "Gnani requires a voice name.");
        }

        if (Speed is < 0.85 or > 1.15)
        {
            throw new ArgumentOutOfRangeException(nameof(Speed), "Gnani accepts 0.85 to 1.15.");
        }

        if (Array.IndexOf(new[] { 8000, 16000, 22050, 24000, 44100, 48000 }, SampleRate) < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SampleRate), "Gnani supports 8000, 16000, 22050, 24000, 44100 and 48000 Hz.");
        }
    }
}
