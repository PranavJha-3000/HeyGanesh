namespace HeyGanesh.ElementSelection;

/// <summary>
/// The one public way to get the real Jev transport (DEC-036). The App Shell builds one and hands the
/// same instance to <see cref="ElementSelectionService"/> and the Turn Router, so both decisions go
/// through one HTTP client and one key read per call. <see cref="TypeSafeJevClient"/> itself stays
/// internal: callers get an <see cref="IJevClient"/> they own, nothing about the wire.
/// </summary>
public static class JevClients
{
    /// <summary>
    /// A TypeSafe client with <paramref name="options"/>' BaseUrl and per-call timeout. It reads
    /// <c>TYPESAFE_API_KEY</c> at call time only, exactly as the service's own default client does.
    /// The caller disposes it: a service handed a client never disposes it.
    /// </summary>
    public static OwnedJevClient CreateTypeSafe(SelectionOptions? options = null) =>
        new(new TypeSafeJevClient(options));
}

/// <summary>An <see cref="IJevClient"/> the caller owns and must dispose (it holds an HttpClient).</summary>
public sealed class OwnedJevClient : IJevClient, IDisposable
{
    private readonly TypeSafeJevClient _inner;

    internal OwnedJevClient(TypeSafeJevClient inner) => _inner = inner;

    public Task<JevRawResponse> EvaluateAsync(JevRequest request, CancellationToken cancellationToken) =>
        _inner.EvaluateAsync(request, cancellationToken);

    public void Dispose() => _inner.Dispose();
}
