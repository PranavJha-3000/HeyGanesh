using HeyGanesh.Providers.Core;

namespace HeyGanesh.Tools.ProvidersSmoke;

internal static class ProvidersSmokeHost
{
    public static async Task<int> Run(string[] args)
    {
        int failures = 0;
        int total = 0;

        // --- 1. Capability model ---
        total++;
        var caps = ProviderCapability.Text | ProviderCapability.Vision | ProviderCapability.Streaming;
        if ((caps & ProviderCapability.Text) != 0 && (caps & ProviderCapability.Embeddings) == 0)
        { Console.WriteLine("PASS: capability flags"); }
        else { Console.WriteLine("FAIL: capability flags"); failures++; }

        // --- 2. ProviderError classification ---
        total++;
        var ex = new ProviderException(ProviderErrorKind.AuthFailed, "test-provider", "gpt-4");
        if (ex.Kind == ProviderErrorKind.AuthFailed && !ex.IsRetryable && ex.ProviderId == "test-provider")
        { Console.WriteLine("PASS: ProviderException (AuthFailed, non-retryable)"); }
        else { Console.WriteLine("FAIL: ProviderException"); failures++; }

        total++;
        var ex2 = new ProviderException(ProviderErrorKind.RateLimited, "test-provider");
        if (ex2.IsRetryable)
        { Console.WriteLine("PASS: RateLimited is retryable by default"); }
        else { Console.WriteLine("FAIL: RateLimited retryable"); failures++; }

        total++;
        var ex3 = new ProviderException(ProviderErrorKind.Timeout, "p", isRetryable: false);
        if (!ex3.IsRetryable)
        { Console.WriteLine("PASS: explicit isRetryable override respected"); }
        else { Console.WriteLine("FAIL: isRetryable override"); failures++; }

        total++;
        var ex4 = new ProviderException(ProviderErrorKind.InvalidRequest, "test-provider");
        if (ex4.Kind == ProviderErrorKind.InvalidRequest && !ex4.IsRetryable)
        { Console.WriteLine("PASS: InvalidRequest is non-retryable by default"); }
        else { Console.WriteLine("FAIL: InvalidRequest retryable"); failures++; }

        // --- 3. Safe error reporting ---
        total++;
        var safeStr = ex.ToString();
        if (!safeStr.Contains("secret") && safeStr.Contains("AuthFailed"))
        { Console.WriteLine("PASS: safe error reporting"); }
        else { Console.WriteLine("FAIL: safe error reporting"); failures++; }

        // --- 4. Catalog ---
        total++;
        var descriptors = new List<ProviderDescriptor>
        {
            new() { Id = "openai", DisplayName = "OpenAI", BaseUrl = "https://api.openai.com", Capabilities = ProviderCapability.Text | ProviderCapability.Vision | ProviderCapability.Streaming | ProviderCapability.Embeddings },
            new() { Id = "anthropic", DisplayName = "Anthropic", BaseUrl = "https://api.anthropic.com", Capabilities = ProviderCapability.Text | ProviderCapability.Vision },
        };
        var catalog = new ProviderCatalog(descriptors);
        if (catalog.GetAll().Count == 2 && catalog.Get("openai") is not null && catalog.Get("missing") is null)
        { Console.WriteLine("PASS: catalog GetAll/Get"); }
        else { Console.WriteLine("FAIL: catalog GetAll/Get"); failures++; }

        total++;
        if (catalog.HasCapability("openai", ProviderCapability.Embeddings) && !catalog.HasCapability("anthropic", ProviderCapability.Embeddings))
        { Console.WriteLine("PASS: catalog HasCapability"); }
        else { Console.WriteLine("FAIL: catalog HasCapability"); failures++; }

        total++;
        var embProviders = catalog.GetByCapability(ProviderCapability.Embeddings);
        if (embProviders.Count == 1 && embProviders[0].Id == "openai")
        { Console.WriteLine("PASS: catalog GetByCapability"); }
        else { Console.WriteLine("FAIL: catalog GetByCapability"); failures++; }

        // --- 5. Registry ---
        total++;
        var registry = new ProviderRegistry();
        registry.Assign(new ProviderAssignment { Role = ProviderRole.Tutor, ProviderId = "openai", ModelId = "gpt-4", CredentialAlias = "openai-key" });
        var assignment = registry.GetAssignment(ProviderRole.Tutor);
        if (assignment is not null && assignment.ProviderId == "openai" && assignment.ModelId == "gpt-4")
        { Console.WriteLine("PASS: registry Assign/GetAssignment"); }
        else { Console.WriteLine("FAIL: registry Assign/GetAssignment"); failures++; }

        total++;
        var configuredRoles = registry.GetConfiguredRoles();
        if (configuredRoles.Count == 1 && configuredRoles[0] == ProviderRole.Tutor)
        { Console.WriteLine("PASS: registry GetConfiguredRoles"); }
        else { Console.WriteLine("FAIL: registry GetConfiguredRoles"); failures++; }

        total++;
        try
        {
            registry.Resolve(ProviderRole.Vision);
            Console.WriteLine("FAIL: registry Resolve missing should throw");
            failures++;
        }
        catch (ProviderException pe) when (pe.Kind == ProviderErrorKind.ModelNotFound)
        { Console.WriteLine("PASS: registry Resolve missing -> ModelNotFound"); }

        total++;
        registry.RegisterLlmFactory("openai", (model) => new FakeLlmClient("openai", model));
        var resolved = registry.Resolve(ProviderRole.Tutor);
        if (resolved.LlmClient is not null && resolved.ProviderId == "openai" && resolved.ModelId == "gpt-4")
        { Console.WriteLine("PASS: registry Resolve with factory"); }
        else { Console.WriteLine("FAIL: registry Resolve with factory"); failures++; }

        Console.WriteLine($"\n=== Partial: {total - failures}/{total} PASS so far ===");
        // --- 6. Cancellation handling ---
        total++;
        var cts2 = new CancellationTokenSource();
        cts2.Cancel();
        try
        {
            var fc = new FakeLlmClient("fake", "fake-model");
            await fc.GenerateAsync(new LlmRequest(), cts2.Token);
            Console.WriteLine("FAIL: cancellation should throw");
            failures++;
        }
        catch (OperationCanceledException)
        { Console.WriteLine("PASS: cancellation propagates"); }

        // --- 7. Timeout handling ---
        total++;
        var slowClient = new SlowLlmClient("slow", "slow-model", TimeSpan.FromSeconds(10));
        var policy = new ResiliencePolicy { Timeout = TimeSpan.FromMilliseconds(50), MaxRetries = 0 };
        var resilient = new ResilientLlmClient(slowClient, policy);
        try
        {
            await resilient.GenerateAsync(new LlmRequest());
            Console.WriteLine("FAIL: timeout should throw");
            failures++;
        }
        catch (ProviderException pe) when (pe.Kind == ProviderErrorKind.Timeout)
        { Console.WriteLine("PASS: timeout -> ProviderErrorKind.Timeout"); }

        // --- 8. Retry limits ---
        total++;
        var failingClient = new FailingLlmClient("failing", "failing-model", 2);
        var retryPolicy = new ResiliencePolicy { MaxRetries = 3, BaseDelay = TimeSpan.FromMilliseconds(1), MaxDelay = TimeSpan.FromMilliseconds(10), UseJitter = false };
        var resilientFailing = new ResilientLlmClient(failingClient, retryPolicy);
        var resp = await resilientFailing.GenerateAsync(new LlmRequest());
        if (resp.Text == "success" && failingClient.CallCount == 3)
        { Console.WriteLine("PASS: retry succeeds after 2 failures"); }
        else { Console.WriteLine($"FAIL: retry (calls={failingClient.CallCount})"); failures++; }

        // --- 9. Non-retryable failures ---
        total++;
        var authFailingClient = new AuthFailingLlmClient("auth", "auth-model");
        var authPolicy = new ResiliencePolicy { MaxRetries = 3, BaseDelay = TimeSpan.FromMilliseconds(1), UseJitter = false };
        var resilientAuth = new ResilientLlmClient(authFailingClient, authPolicy);
        try
        {
            await resilientAuth.GenerateAsync(new LlmRequest());
            Console.WriteLine("FAIL: auth failure should throw");
            failures++;
        }
        catch (ProviderException pe) when (pe.Kind == ProviderErrorKind.AuthFailed)
        {
            if (authFailingClient.CallCount == 1)
            { Console.WriteLine("PASS: non-retryable auth failure (1 call)"); }
            else { Console.WriteLine($"FAIL: non-retryable (calls={authFailingClient.CallCount})"); failures++; }
        }
        // --- 14. ResiliencePolicy.ComputeDelay ---
        total++;
        var p = new ResiliencePolicy { BaseDelay = TimeSpan.FromMilliseconds(100), MaxDelay = TimeSpan.FromMilliseconds(1000), UseJitter = false };
        var d0 = p.ComputeDelay(0);
        var d1 = p.ComputeDelay(1);
        var d2 = p.ComputeDelay(2);
        if (d0.TotalMilliseconds == 100 && d1.TotalMilliseconds == 200 && d2.TotalMilliseconds == 400)
        { Console.WriteLine("PASS: ComputeDelay exponential backoff"); }
        else { Console.WriteLine($"FAIL: ComputeDelay ({d0.TotalMilliseconds}, {d1.TotalMilliseconds}, {d2.TotalMilliseconds})"); failures++; }

        // --- 15. ResiliencePolicy.None ---
        total++;
        var none = ResiliencePolicy.None;
        if (none.MaxRetries == 0 && none.Timeout == System.Threading.Timeout.InfiniteTimeSpan)
        { Console.WriteLine("PASS: ResiliencePolicy.None"); }
        else { Console.WriteLine("FAIL: ResiliencePolicy.None"); failures++; }

        // --- 16. Embedding client contract ---
        total++;
        var embClient = new FakeEmbeddingClient("emb", "emb-model");
        var embResp = await embClient.EmbedAsync(new EmbeddingRequest { Texts = new[] { "hello" } });
        if (embResp.Embeddings.Count == 1 && embResp.Embeddings[0].Length == 4)
        { Console.WriteLine("PASS: IEmbeddingClient contract"); }
        else { Console.WriteLine("FAIL: IEmbeddingClient contract"); failures++; }

        // --- 17. Resilient embedding client ---
        total++;
        var embFailing = new FailingEmbeddingClient("emb-fail", "emb-fail-model", 1);
        var embRetryPolicy = new ResiliencePolicy { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(1), UseJitter = false };
        var resilientEmb = new ResilientEmbeddingClient(embFailing, embRetryPolicy);
        var embResult = await resilientEmb.EmbedAsync(new EmbeddingRequest { Texts = new[] { "test" } });
        if (embResult.Embeddings.Count == 1 && embFailing.CallCount == 2)
        { Console.WriteLine("PASS: resilient embedding retry"); }
        else { Console.WriteLine($"FAIL: resilient embedding (calls={embFailing.CallCount})"); failures++; }

        // --- 18. ProviderRole enum ---
        total++;
        var _t = (int)ProviderRole.Tutor;
        var _e = (int)ProviderRole.Embedder;
        if (_t == 0 && _e == 2)
        { Console.WriteLine("PASS: ProviderRole enum values"); }
        else { Console.WriteLine("FAIL: ProviderRole enum"); failures++; }

        // --- 19. IAsyncEnumerable streaming contract ---
        total++;
        var streamClient = new FakeLlmClient("stream", "stream-model");
        var chunks = new List<LlmStreamChunk>();
        await foreach (var c in streamClient.StreamAsync(new LlmRequest()))
            chunks.Add(c);
        if (chunks.Count == 1 && chunks[0].TextDelta == "hello")
        { Console.WriteLine("PASS: IAsyncEnumerable streaming contract"); }
        else { Console.WriteLine("FAIL: streaming contract"); failures++; }

        // --- 20. Resilient streaming (circuit breaker open) ---
        total++;
        var cbStream = new CircuitBreaker(failureThreshold: 1, recoveryTimeout: TimeSpan.FromMinutes(10));
        cbStream.RecordFailure();
        var resilientStream = new ResilientLlmClient(new FakeLlmClient("s", "m"), new ResiliencePolicy { MaxRetries = 0 }, cbStream);
        try
        {
            await foreach (var c in resilientStream.StreamAsync(new LlmRequest())) { }
            Console.WriteLine("FAIL: open circuit stream should throw");
            failures++;
        }
        catch (ProviderException pe) when (pe.Message.Contains("Circuit breaker is open"))
        { Console.WriteLine("PASS: resilient stream respects open circuit"); }
        // --- 10. Circuit breaker opening ---
        total++;
        var cb = new CircuitBreaker(failureThreshold: 3, recoveryTimeout: TimeSpan.FromMinutes(10));
        if (cb.CurrentState == CircuitBreaker.State.Closed && cb.IsAllowed())
        { Console.WriteLine("PASS: circuit breaker starts Closed"); }
        else { Console.WriteLine("FAIL: circuit breaker initial state"); failures++; }

        cb.RecordFailure(); cb.RecordFailure(); cb.RecordFailure();
        if (cb.CurrentState == CircuitBreaker.State.Open && !cb.IsAllowed())
        { Console.WriteLine("PASS: circuit breaker opens after threshold"); }
        else { Console.WriteLine("FAIL: circuit breaker open"); failures++; }

        // --- 11. Circuit breaker rejection while open ---
        total++;
        var cbClient = new FakeLlmClient("cb", "cb-model");
        var cbPolicy = new ResiliencePolicy { MaxRetries = 0, Timeout = TimeSpan.FromSeconds(5) };
        var resilientCb = new ResilientLlmClient(cbClient, cbPolicy, cb);
        try
        {
            await resilientCb.GenerateAsync(new LlmRequest());
            Console.WriteLine("FAIL: open circuit should reject");
            failures++;
        }
        catch (ProviderException pe) when (pe.Message.Contains("Circuit breaker is open"))
        { Console.WriteLine("PASS: open circuit rejects calls"); }

        // --- 12. Circuit breaker recovery ---
        total++;
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var cb2 = new CircuitBreaker(failureThreshold: 2, recoveryTimeout: TimeSpan.FromSeconds(5), clock: () => clock.Now);
        cb2.RecordFailure(); cb2.RecordFailure();
        if (cb2.CurrentState == CircuitBreaker.State.Open)
        {
            clock.Advance(TimeSpan.FromSeconds(6));
            if (cb2.CurrentState == CircuitBreaker.State.HalfOpen && cb2.IsAllowed())
            { Console.WriteLine("PASS: circuit breaker transitions to HalfOpen"); }
            else { Console.WriteLine("FAIL: circuit breaker half-open"); failures++; }
        }
        else { Console.WriteLine("FAIL: circuit breaker did not open"); failures++; }

        total++;
        cb2.RecordSuccess();
        if (cb2.CurrentState == CircuitBreaker.State.Closed)
        { Console.WriteLine("PASS: circuit breaker closes on success"); }
        else { Console.WriteLine("FAIL: circuit breaker close on success"); failures++; }

        // --- 13. Circuit breaker reopen on half-open failure ---
        total++;
        var cb3 = new CircuitBreaker(failureThreshold: 2, recoveryTimeout: TimeSpan.FromSeconds(5), clock: () => clock.Now);
        cb3.RecordFailure(); cb3.RecordFailure();
        clock.Advance(TimeSpan.FromSeconds(6));
        _ = cb3.IsAllowed();
        cb3.RecordFailure();
        if (cb3.CurrentState == CircuitBreaker.State.Open)
        { Console.WriteLine("PASS: circuit breaker reopens on half-open failure"); }
        else { Console.WriteLine("FAIL: circuit breaker reopen"); failures++; }

        Console.WriteLine($"\\n=== Providers.Core result: {total - failures}/{total} PASS, {failures} FAILURE(S) ===");
        return failures == 0 ? 0 : 1;
    }

    // --- Fake implementations ---

    private class FakeLlmClient : ILlmClient
    {
        public string ProviderId { get; }
        public string ModelId { get; }
        public ProviderCapability Capabilities => ProviderCapability.Text;
        public FakeLlmClient(string pid, string mid) { ProviderId = pid; ModelId = mid; }
        public Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new LlmResponse { Text = "ok", ModelId = ModelId });
        }
        public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(LlmRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield return new LlmStreamChunk { TextDelta = "hello", IsDone = true };
        }
        public void Dispose() { }
    }

    private class SlowLlmClient : ILlmClient
    {
        public string ProviderId { get; }
        public string ModelId { get; }
        public ProviderCapability Capabilities => ProviderCapability.Text;
        private readonly TimeSpan _delay;
        public SlowLlmClient(string pid, string mid, TimeSpan delay) { ProviderId = pid; ModelId = mid; _delay = delay; }
        public async Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken ct = default)
        {
            await Task.Delay(_delay, ct);
            return new LlmResponse { Text = "slow", ModelId = ModelId };
        }
        public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(LlmRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Delay(_delay, ct);
            yield return new LlmStreamChunk { TextDelta = "slow", IsDone = true };
        }
        public void Dispose() { }
    }

    private class FailingLlmClient : ILlmClient
    {
        public string ProviderId { get; }
        public string ModelId { get; }
        public ProviderCapability Capabilities => ProviderCapability.Text;
        private readonly int _failCount;
        public int CallCount { get; private set; }
        public FailingLlmClient(string pid, string mid, int failCount) { ProviderId = pid; ModelId = mid; _failCount = failCount; }
        public Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken ct = default)
        {
            CallCount++;
            if (CallCount <= _failCount)
                throw new ProviderException(ProviderErrorKind.Transport, ProviderId, ModelId, "transient failure");
            return Task.FromResult(new LlmResponse { Text = "success", ModelId = ModelId });
        }
        public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(LlmRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield return new LlmStreamChunk { TextDelta = "ok", IsDone = true };
        }
        public void Dispose() { }
    }

    private class AuthFailingLlmClient : ILlmClient
    {
        public string ProviderId { get; }
        public string ModelId { get; }
        public ProviderCapability Capabilities => ProviderCapability.Text;
        public int CallCount { get; private set; }
        public AuthFailingLlmClient(string pid, string mid) { ProviderId = pid; ModelId = mid; }
        public Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken ct = default)
        {
            CallCount++;
            throw new ProviderException(ProviderErrorKind.AuthFailed, ProviderId, ModelId, "bad key");
        }
        public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(LlmRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield return new LlmStreamChunk { TextDelta = "ok", IsDone = true };
        }
        public void Dispose() { }
    }

    private class FakeEmbeddingClient : IEmbeddingClient
    {
        public string ProviderId { get; }
        public string ModelId { get; }
        public ProviderCapability Capabilities => ProviderCapability.Embeddings;
        public FakeEmbeddingClient(string pid, string mid) { ProviderId = pid; ModelId = mid; }
        public Task<EmbeddingResponse> EmbedAsync(EmbeddingRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new EmbeddingResponse
            {
                Embeddings = new[] { new[] { 0.1f, 0.2f, 0.3f, 0.4f } },
                ModelId = ModelId
            });
        }
        public void Dispose() { }
    }

    private class FailingEmbeddingClient : IEmbeddingClient
    {
        public string ProviderId { get; }
        public string ModelId { get; }
        public ProviderCapability Capabilities => ProviderCapability.Embeddings;
        private readonly int _failCount;
        public int CallCount { get; private set; }
        public FailingEmbeddingClient(string pid, string mid, int failCount) { ProviderId = pid; ModelId = mid; _failCount = failCount; }
        public Task<EmbeddingResponse> EmbedAsync(EmbeddingRequest request, CancellationToken ct = default)
        {
            CallCount++;
            if (CallCount <= _failCount)
                throw new ProviderException(ProviderErrorKind.Transport, ProviderId, ModelId, "transient");
            return Task.FromResult(new EmbeddingResponse { Embeddings = new[] { new[] { 1f, 2f } }, ModelId = ModelId });
        }
        public void Dispose() { }
    }

    private class FakeClock
    {
        public DateTimeOffset Now { get; private set; }
        public FakeClock(DateTimeOffset start) { Now = start; }
        public void Advance(TimeSpan delta) { Now = Now.Add(delta); }
    }
}