namespace HeyGanesh.Providers.Core;

/// <summary>
/// Explicit resilience policy for provider calls. All values are documented and
/// deterministic; no hidden defaults beyond what is stated here.
/// </summary>
public sealed record ResiliencePolicy
{
    /// <summary>Per-call timeout. Default: 30 seconds.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum number of retry attempts (0 = no retries). Default: 3.</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>Base delay between retries. Default: 200 ms.</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Maximum delay between retries. Default: 5 seconds.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Whether to apply jitter to retry delays. Default: true.</summary>
    public bool UseJitter { get; init; } = true;

    /// <summary>Set of error kinds that are safe to retry. Default: RateLimited, Timeout, Transport.</summary>
    public IReadOnlySet<ProviderErrorKind> RetryableKinds { get; init; } =
        new HashSet<ProviderErrorKind> { ProviderErrorKind.RateLimited, ProviderErrorKind.Timeout, ProviderErrorKind.Transport };

    /// <summary>
    /// Computes the delay for the given retry attempt (0-based). Applies exponential
    /// backoff with an optional jitter factor. The delay is clamped to <see cref="MaxDelay"/>.
    /// </summary>
    public TimeSpan ComputeDelay(int attempt, Random? rng = null)
    {
        if (attempt < 0) attempt = 0;
        var exponential = BaseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        var clamped = Math.Min(exponential, MaxDelay.TotalMilliseconds);
        if (UseJitter)
        {
            var r = rng ?? Random.Shared;
            // Jitter: random factor between 0.5 and 1.5
            var jitter = 0.5 + r.NextDouble();
            clamped *= jitter;
            clamped = Math.Min(clamped, MaxDelay.TotalMilliseconds);
        }
        return TimeSpan.FromMilliseconds(Math.Max(0, clamped));
    }

    /// <summary>Returns true when the given error kind is retryable under this policy.</summary>
    public bool IsRetryable(ProviderErrorKind kind) => RetryableKinds.Contains(kind);

    /// <summary>A policy with no retries and no timeout (for testing or passthrough).</summary>
    public static ResiliencePolicy None => new()
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        MaxRetries = 0,
        BaseDelay = TimeSpan.Zero,
        MaxDelay = TimeSpan.Zero,
        UseJitter = false,
    };
}