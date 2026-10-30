namespace HeyGanesh.Providers.Core;

/// <summary>
/// Contract for an embedding provider.
/// </summary>
public interface IEmbeddingClient : IDisposable
{
    /// <summary>The provider identifier (never a secret).</summary>
    string ProviderId { get; }

    /// <summary>The model identifier currently selected.</summary>
    string ModelId { get; }

    /// <summary>Capability flags the selected model advertises.</summary>
    ProviderCapability Capabilities { get; }

    /// <summary>
    /// Embeds the given texts. Honors cancellation and the configured timeout.
    /// Throws <see cref="ProviderException"/> on failure.
    /// </summary>
    Task<EmbeddingResponse> EmbedAsync(EmbeddingRequest request, CancellationToken ct = default);
}

/// <summary>Request to an embedding provider.</summary>
public sealed record EmbeddingRequest
{
    /// <summary>Texts to embed.</summary>
    public IReadOnlyList<string> Texts { get; init; } = Array.Empty<string>();

    /// <summary>Model override; null uses the client's default.</summary>
    public string? ModelOverride { get; init; }
}

/// <summary>Response from an embedding provider.</summary>
public sealed record EmbeddingResponse
{
    /// <summary>One embedding vector per input text.</summary>
    public IReadOnlyList<float[]> Embeddings { get; init; } = Array.Empty<float[]>();

    /// <summary>Model that produced the embeddings.</summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>Input token count.</summary>
    public int InputTokens { get; init; }

    /// <summary>Latency of the call.</summary>
    public TimeSpan Latency { get; init; }
}