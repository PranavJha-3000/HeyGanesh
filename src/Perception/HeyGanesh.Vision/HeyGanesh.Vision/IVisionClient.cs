namespace HeyGanesh.Vision;

/// <summary>
/// A dumb, transport-level request handed to <see cref="IVisionClient"/>. VisionService is the only
/// thing that builds one; the client does not know what a question means -- it just turns the request
/// into an HTTP call and hands back the raw completion text.
/// </summary>
/// <remarks>
/// Everything the client needs to form the provider's Chat Completions payload lives here: the model id,
/// the (already-assembled) system prompt, the user question, the raw JPEG, its on-the-wire dimensions,
/// and the per-call sampling/formatting knobs. Keeping the request a flat data shape is what lets the
/// future API key security proxy swap in as another implementation of <see cref="IVisionClient"/> with
/// no change to VisionService.
/// </remarks>
public sealed record VisionRequest
{
    public string Model { get; init; } = string.Empty;

    /// <summary>The system prompt, which includes the JSON schema + example (see VisionPrompt).</summary>
    public string SystemPrompt { get; init; } = string.Empty;

    /// <summary>The user's natural-language question.</summary>
    public string UserQuestion { get; init; } = string.Empty;

    /// <summary>The JPEG bytes the model will receive as a data URL.</summary>
    public byte[] Image { get; init; } = [];

    /// <summary>Width of the JPEG, in pixels, as seen by the model.</summary>
    public int ImageWidth { get; init; }

    /// <summary>Height of the JPEG, in pixels, as seen by the model.</summary>
    public int ImageHeight { get; init; }

    /// <summary>Provider image fidelity flag (high / low / auto).</summary>
    public string ImageDetail { get; init; } = "high";

    /// <summary>Per-call output budget.</summary>
    public int MaxTokens { get; init; } = 1024;

    /// <summary>Sampling temperature.</summary>
    public float Temperature { get; init; } = 0.7f;

    /// <summary>Top-p nucleus sampling. Null = unset.</summary>
    public float? TopP { get; init; }

    /// <summary>
    /// Whether to request strict JSON output from the provider. Kept even though VisionService parses
    /// tolerantly either way -- JSON mode narrows the failure mode but is documented to occasionally
    /// return empty content, hence the tolerant fallback parser.
    /// </summary>
    public bool UseJsonMode { get; init; } = true;
}

/// <summary>The raw, transport-level answer returned by <see cref="IVisionClient"/>.</summary>
/// <remarks>
/// Content is the provider's completion string verbatim (a JSON document when UseJsonMode is on).
/// VisionService owns turning this into a VisionSample; the client does not parse the result schema.
/// Token usage is best-effort: some providers do not populate it for image requests.
/// </remarks>
public sealed record VisionRawResponse(string Content, string Model, int PromptTokens, int CompletionTokens, TimeSpan Latency);

/// <summary>
/// The injectable HTTP seam -- the single boundary between Vision and the provider.
/// </summary>
/// <remarks>
/// This is the swappable point behind DEC-007: today it is a direct DeepSeek HTTP client that reads
/// the API key from the environment; tomorrow it can be a client that talks to the project's local API
/// key security proxy by pointing BaseUrl at it, or a recording fake for tests. VisionService depends
/// only on this interface.
/// </summary>
public interface IVisionClient
{
    /// <summary>
    /// Sends <paramref name="request"/> to the provider and returns the raw completion text. Never
    /// returns null: callers inspect VisionRawResponse.Content for emptiness and the returned
    /// <see cref="VisionSample.ParseError"/> lives one layer up in VisionService.
    /// </summary>
    Task<VisionRawResponse> AskAsync(VisionRequest request, CancellationToken cancellationToken);
}
