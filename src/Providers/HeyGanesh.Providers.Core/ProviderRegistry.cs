namespace HeyGanesh.Providers.Core;

/// <summary>
/// In-memory provider registry. Holds role assignments and provider client factories.
/// Thread-safe for concurrent reads after writes are complete.
/// </summary>
public sealed class ProviderRegistry : IProviderRegistry
{
    private readonly Dictionary<ProviderRole, ProviderAssignment> _assignments = new();
    private readonly Dictionary<string, Func<string, ILlmClient?>> _llmFactories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<string, IEmbeddingClient?>> _embeddingFactories = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <summary>Registers a factory that creates an ILlmClient for the given provider ID and model ID.</summary>
    public void RegisterLlmFactory(string providerId, Func<string, ILlmClient?> factory)
    {
        if (string.IsNullOrWhiteSpace(providerId)) throw new ArgumentException("Provider ID must not be empty.", nameof(providerId));
        ArgumentNullException.ThrowIfNull(factory);
        lock (_lock) { _llmFactories[providerId] = factory; }
    }

    /// <summary>Registers a factory that creates an IEmbeddingClient for the given provider ID and model ID.</summary>
    public void RegisterEmbeddingFactory(string providerId, Func<string, IEmbeddingClient?> factory)
    {
        if (string.IsNullOrWhiteSpace(providerId)) throw new ArgumentException("Provider ID must not be empty.", nameof(providerId));
        ArgumentNullException.ThrowIfNull(factory);
        lock (_lock) { _embeddingFactories[providerId] = factory; }
    }

    public void Assign(ProviderAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        if (string.IsNullOrWhiteSpace(assignment.ProviderId))
            throw new ArgumentException("Provider ID must not be empty.", nameof(assignment));
        if (string.IsNullOrWhiteSpace(assignment.ModelId))
            throw new ArgumentException("Model ID must not be empty.", nameof(assignment));
        lock (_lock) { _assignments[assignment.Role] = assignment; }
    }

    public ProviderAssignment? GetAssignment(ProviderRole role)
    {
        lock (_lock) { _assignments.TryGetValue(role, out var a); return a; }
    }

    public ResolvedProvider Resolve(ProviderRole role)
    {
        ProviderAssignment? assignment;
        lock (_lock) { _assignments.TryGetValue(role, out assignment); }

        if (assignment is null)
            throw new ProviderException(
                ProviderErrorKind.ModelNotFound,
                providerId: "<none>",
                modelId: null,
                message: $"No provider assigned for role {role}.");

        Func<string, ILlmClient?>? llmFactory;
        Func<string, IEmbeddingClient?>? embFactory;
        lock (_lock)
        {
            _llmFactories.TryGetValue(assignment.ProviderId, out llmFactory);
            _embeddingFactories.TryGetValue(assignment.ProviderId, out embFactory);
        }

        ILlmClient? llmClient = null;
        IEmbeddingClient? embClient = null;

        if (role is ProviderRole.Tutor or ProviderRole.Vision or ProviderRole.Jev or ProviderRole.Stt or ProviderRole.Tts)
        {
            if (llmFactory is not null)
                llmClient = llmFactory(assignment.ModelId);
        }

        if (role == ProviderRole.Embedder)
        {
            if (embFactory is not null)
                embClient = embFactory(assignment.ModelId);
        }

        return new ResolvedProvider(
            role,
            assignment.ProviderId,
            assignment.ModelId,
            llmClient,
            embClient);
    }

    public IReadOnlyList<ProviderRole> GetConfiguredRoles()
    {
        lock (_lock) { return _assignments.Keys.ToList().AsReadOnly(); }
    }
}