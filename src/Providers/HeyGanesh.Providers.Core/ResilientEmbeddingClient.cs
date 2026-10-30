namespace HeyGanesh.Providers.Core;

/// <summary>
/// Wraps an <see cref="IEmbeddingClient"/> with timeout, bounded retry (with jitter), and a
/// per-provider circuit breaker. Mirrors <see cref="ResilientLlmClient"/>.
/// </summary>
public sealed class ResilientEmbeddingClient : IEmbeddingClient
{
    private readonly IEmbeddingClient _inner;
    private readonly ResiliencePolicy _policy;
    private readonly CircuitBreaker _circuitBreaker;
    private bool _disposed;

    public string ProviderId => _inner.ProviderId;
    public string ModelId => _inner.ModelId;
    public ProviderCapability Capabilities => _inner.Capabilities;

    public ResilientEmbeddingClient(IEmbeddingClient inner, ResiliencePolicy? policy = null, CircuitBreaker? circuitBreaker = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _policy = policy ?? new ResiliencePolicy();
        _circuitBreaker = circuitBreaker ?? new CircuitBreaker();
    }

    public async Task<EmbeddingResponse> EmbedAsync(EmbeddingRequest request, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_circuitBreaker.IsAllowed())
            throw new ProviderException(ProviderErrorKind.Transport, ProviderId, ModelId,
                "Circuit breaker is open; call rejected.");

        Exception? lastException = null;
        for (int attempt = 0; attempt <= _policy.MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                if (!_circuitBreaker.IsAllowed())
                    throw new ProviderException(ProviderErrorKind.Transport, ProviderId, ModelId,
                        "Circuit breaker opened during retries; call rejected.");

                var delay = _policy.ComputeDelay(attempt - 1);
                if (delay > TimeSpan.Zero)
                {
                    try { await Task.Delay(delay, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                }
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (_policy.Timeout != Timeout.InfiniteTimeSpan)
                cts.CancelAfter(_policy.Timeout);
            var token = cts.Token;

            try
            {
                var result = await _inner.EmbedAsync(request, token).ConfigureAwait(false);
                _circuitBreaker.RecordSuccess();
                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                lastException = new ProviderException(ProviderErrorKind.Timeout, ProviderId, ModelId,
                    "Operation timed out.", isRetryable: true);
                _circuitBreaker.RecordFailure();
                if (attempt >= _policy.MaxRetries || !_policy.IsRetryable(ProviderErrorKind.Timeout))
                    throw (ProviderException)lastException;
            }
            catch (ProviderException ex)
            {
                lastException = ex;
                _circuitBreaker.RecordFailure();
                if (attempt >= _policy.MaxRetries || !ex.IsRetryable || !_policy.IsRetryable(ex.Kind))
                    throw;
            }
            catch (Exception ex)
            {
                lastException = new ProviderException(ProviderErrorKind.Transport, ProviderId, ModelId,
                    "Transport failure.", ex, isRetryable: true);
                _circuitBreaker.RecordFailure();
                if (attempt >= _policy.MaxRetries)
                    throw (ProviderException)lastException;
            }
        }

        throw lastException ?? new ProviderException(ProviderErrorKind.Transport, ProviderId, ModelId, "Unknown failure.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _inner.Dispose();
        GC.SuppressFinalize(this);
    }
}