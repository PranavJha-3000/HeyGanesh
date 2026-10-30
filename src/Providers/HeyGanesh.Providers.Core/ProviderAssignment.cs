namespace HeyGanesh.Providers.Core;

/// <summary>
/// A role assignment: which provider+model (+ credential alias) serves a given role.
/// </summary>
public sealed record ProviderAssignment
{
    public ProviderRole Role { get; init; }
    public string ProviderId { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public string CredentialAlias { get; init; } = string.Empty;
}

/// <summary>
/// Resolved provider for a role: the client instance, model ID, and credential handle.
/// </summary>
public sealed class ResolvedProvider
{
    public ProviderRole Role { get; }
    public string ProviderId { get; }
    public string ModelId { get; }
    public ILlmClient? LlmClient { get; }
    public IEmbeddingClient? EmbeddingClient { get; }
    public HeyGanesh.Security.SecretHandle? CredentialHandle { get; }

    public ResolvedProvider(
        ProviderRole role,
        string providerId,
        string modelId,
        ILlmClient? llmClient = null,
        IEmbeddingClient? embeddingClient = null,
        HeyGanesh.Security.SecretHandle? credentialHandle = null)
    {
        Role = role;
        ProviderId = providerId;
        ModelId = modelId;
        LlmClient = llmClient;
        EmbeddingClient = embeddingClient;
        CredentialHandle = credentialHandle;
    }
}