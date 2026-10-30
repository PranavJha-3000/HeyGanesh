using HeyGanesh.Abstractions;
using HeyGanesh.ScreenCapture;
using System.Text.Json.Nodes;

namespace HeyGanesh.ElementSelection;

/// <summary>Provider/HTTP-level failure from an <see cref="IJevClient"/> that is neither an auth problem nor rate limiting. Carries the HTTP status when known.</summary>
public class JevClientException : Exception
{
    public int? StatusCode { get; }

    public JevClientException(string message, int? statusCode = null) : base(message) => StatusCode = statusCode;
}

/// <summary>A bearer-key problem (missing key / HTTP 401 / 403) — surfaced separately from model behaviour.</summary>
public sealed class JevAuthenticationException : JevClientException
{
    public JevAuthenticationException(string message) : base(message, null) { }
}

/// <summary>Provider rate limit (HTTP 429).</summary>
public sealed class JevRateLimitException : JevClientException
{
    public JevRateLimitException(string message) : base(message, 429) { }
}

/// <summary>
/// One typed question for the System One API. Jev returns typed decisions only — a choice among
/// offered options, a score on a scale, or a yes/no probability — never free text, never coordinates
/// (confirmed against the API reference; this is why geometry lives in the candidates, not the prompt).
/// </summary>
public sealed record JevQuestion
{
    /// <summary>"choice" (select one of <see cref="Criteria"/>) or "noul" (yes/no probability).</summary>
    public string Type { get; init; } = "choice";

    /// <summary>
    /// Full instruction text. The provider documents that our question ids are not shown to the model,
    /// so everything the model must know — including the user's question — lives here.
    /// </summary>
    public string Instructions { get; init; } = string.Empty;

    /// <summary>
    /// choice: option-id → human description (option ids come back verbatim in the answer, which is
    /// what makes candidate→answer mapping exact). noul: optional "true"/"false" descriptions.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? Criteria { get; init; }
}

/// <summary>
/// A dumb, transport-level request handed to <see cref="IJevClient"/>. ElementSelectionService is the
/// only thing that builds one; the client does not know what a candidate is — it turns the request
/// into an HTTP call and hands back the raw typed answers. Keeping the request a flat data shape is
/// what lets the future API key security proxy swap in as another <see cref="IJevClient"/> with no
/// change to the service (DEC-008, mirroring DEC-007's IVisionClient boundary).
/// </summary>
public sealed record JevRequest
{
    public string Model { get; init; } = string.Empty;

    /// <summary>The state Jev judges: ambient context as a JSON object (window title, geometry summary, counts). The question itself lives in Instructions.</summary>
    public JsonNode? State { get; init; }

    /// <summary>Our own question ids → typed questions. Ids are ours and are not shown to the model.</summary>
    public IReadOnlyDictionary<string, JevQuestion> Questions { get; init; } =
        new Dictionary<string, JevQuestion>();
}

/// <summary>
/// Raw typed answer for one question id. Geometry never appears here — Jev selects, it does not
/// localize. <see cref="Choice"/> is the option id we offered; <see cref="Probabilities"/> is the
/// per-option distribution; <see cref="Confidence"/> is the model's self-rated confidence.
/// </summary>
public sealed record JevAnswer(
    string? Type,
    string? Choice,
    double? Noul,
    double? Confidence,
    IReadOnlyDictionary<string, double>? Probabilities);

/// <summary>The raw, transport-level answer returned by <see cref="IJevClient"/>.</summary>
/// <remarks>Mapping answers → candidates is policy and lives in ElementSelectionService, one layer up.</remarks>
public sealed record JevRawResponse(
    string Model,
    IReadOnlyDictionary<string, JevAnswer> Answers,
    int InputTokens,
    int OutputTokens,
    TimeSpan Latency);

/// <summary>
/// The injectable HTTP seam — the single boundary between Element Selection and the Jev provider.
/// </summary>
/// <remarks>
/// Swappable point behind DEC-008: today a direct TypeSafe HTTP client reading the key from the
/// environment (temporary, extends DEC-007's stopgap); tomorrow the API key security proxy as another
/// implementation, or a BaseUrl redirect — ElementSelectionService is unchanged either way. There is
/// no .NET SDK for TypeSafe (Python/JS only), so implementations are plain BCL HttpClient.
/// </remarks>
public interface IJevClient
{
    /// <summary>Sends <paramref name="request"/> to the provider and returns the raw typed answers. Auth/HTTP failures throw the typed exceptions above.</summary>
    Task<JevRawResponse> EvaluateAsync(JevRequest request, CancellationToken cancellationToken);
}
