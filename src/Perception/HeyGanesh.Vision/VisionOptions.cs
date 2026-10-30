namespace HeyGanesh.Vision;

/// <summary>
/// Tuning for <see cref="VisionService"/> and <see cref="DeepSeekVisionClient"/>.
///
/// <para>
/// All properties are immutable and set once, mirroring the shape of <see cref="ScreenCaptureOptions"/>.
/// No API key lives here -- the key is resolved from the process environment inside
/// <see cref="DeepSeekVisionClient"/> (see DEC-007). What lives here is operating policy: where to talk
/// to, which model, how hot to sample, how many samples drive the consistency signal, and how to format
/// the vision request.
/// </para>
/// </summary>
public sealed class VisionOptions
{
    /// <summary>Defaults: <c>deepseek-flash</c> @ high detail, 2 samples, 0.7 temperature.</summary>
    public static VisionOptions Default { get; } = new();

    /// <summary>Model identifier sent in the Chat Completions request. <c>deepseek-flash</c> is V4.1-Flash.</summary>
    public string ModelId { get; init; } = "deepseek-flash";

    /// <summary>Chat Completions base URL. Override to route through a local API key security proxy.</summary>
    public string BaseUrl { get; init; } = "https://api.deepseek.com";

    /// <summary>Provider image fidelity flag forwarded as <c>image_url.detail</c>. <c>high</c> by default.</summary>
    public string ImageDetail { get; init; } = "high";

    /// <summary>Per-call sampling temperature.</summary>
    /// <remarks>
    /// The model's general-purpose recommendation is 1.0, but grounding accuracy benefits from tighter
    /// sampling while we still need headroom so the cross-sample consistency signal is measurable
    /// (at temperature 0 two samples agree by construction and the signal is always 1.0). 0.7 is the
    /// compromise default; revisit once real-screen accuracy is measured (see DEC-006).
    /// </remarks>
    public float Temperature { get; init; } = 0.7f;

    /// <summary>Top-p nucleus sampling. Null = unset.</summary>
    public float? TopP { get; init; }

    /// <summary>Per-call output budget. JSON-mode answers are short; 1024 is ample headroom.</summary>
    public int MaxTokens { get; init; } = 1024;

    /// <summary>
    /// How many samples to draw per question. Two is the minimum that turns the model's fuzziness into a
    /// reliability signal: agreement across samples raises the confidence floor; disagreement flags doubt.
    /// Set to 1 to disable the signal entirely (DEC-006).
    /// </summary>
    public int MaxSamples { get; init; } = 2;

    /// <summary>
    /// Where the key comes from, per request (DEC-046): the App Shell passes its key ring's reader
    /// (Windows Credential Manager, then the environment). Null keeps this client's own environment read,
    /// so hosts and rows that pass nothing behave exactly as before. A delegate that returns null or blank
    /// is a missing key: the call fails as an authentication fault before any byte is sent.
    /// </summary>
    public Func<string?>? ApiKey { get; init; }

    /// <summary>How long a single provider call may take before it is abandoned.</summary>
    public TimeSpan PerCallTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Parse-failure retry count: when a sample's JSON cannot be parsed (empty content or malformed
    /// output -- both a documented JSON-mode failure mode), the call is re-issued this many times before
    /// the sample is marked failed. This is <i>not</i> the consistency sampler; that is driven by
    /// <see cref="MaxSamples"/>.
    /// </summary>
    public int ParseRetryCount { get; init; } = 1;
}
