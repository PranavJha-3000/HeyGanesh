namespace HeyGanesh.Providers.Core;

/// <summary>
/// Typed failure categories for provider operations. Never carries secret material.
/// </summary>
public enum ProviderErrorKind
{
    /// <summary>Authentication failed (bad or missing key).</summary>
    AuthFailed,
    /// <summary>Rate limit exceeded; retry after the indicated delay if available.</summary>
    RateLimited,
    /// <summary>Requested model does not exist or is not accessible.</summary>
    ModelNotFound,
    /// <summary>The selected model lacks a capability the request requires.</summary>
    CapabilityMissing,
    /// <summary>The operation exceeded its timeout.</summary>
    Timeout,
    /// <summary>Transport-level failure (network, DNS, connection reset).</summary>
    Transport,
    /// <summary>Content was blocked by the provider's safety filter.</summary>
    ContentBlocked,
    /// <summary>The request was malformed or invalid (e.g. bad parameters, missing required field). Non-retryable.</summary>
    InvalidRequest,
}

/// <summary>
/// Typed provider exception. Carries only safe, non-secret diagnostic information.
/// </summary>
public sealed class ProviderException : Exception
{
    /// <summary>The failure category.</summary>
    public ProviderErrorKind Kind { get; }

    /// <summary>The provider identifier (never a secret).</summary>
    public string ProviderId { get; }

    /// <summary>The model identifier, if known.</summary>
    public string? ModelId { get; }

    /// <summary>Whether this failure is safe to retry.</summary>
    public bool IsRetryable { get; }

    /// <summary>Optional retry-after delay suggested by the provider.</summary>
    public TimeSpan? RetryAfter { get; }

    public ProviderException(
        ProviderErrorKind kind,
        string providerId,
        string? modelId = null,
        string? message = null,
        Exception? innerException = null,
        bool? isRetryable = null,
        TimeSpan? retryAfter = null)
        : base(message ?? $"{kind} for provider '{providerId}'" + (modelId is not null ? $" model '{modelId}'" : string.Empty), innerException)
    {
        Kind = kind;
        ProviderId = providerId;
        ModelId = modelId;
        IsRetryable = isRetryable ?? IsDefaultRetryable(kind);
        RetryAfter = retryAfter;
    }

    private static bool IsDefaultRetryable(ProviderErrorKind kind) => kind switch
    {
        ProviderErrorKind.RateLimited => true,
        ProviderErrorKind.Timeout => true,
        ProviderErrorKind.Transport => true,
        _ => false,
    };

    /// <summary>
    /// Returns a safe string representation that never includes secret material.
    /// </summary>
    public override string ToString()
        => $"ProviderException: {Kind} (provider={ProviderId}, model={ModelId ?? "<none>"}, retryable={IsRetryable})";
}