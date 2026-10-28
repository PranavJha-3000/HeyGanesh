namespace HeyGanesh.Providers.OpenAiCompat;

/// <summary>
/// Configuration for an OpenAI-compatible provider adapter.
/// </summary>
public sealed class OpenAiCompatOptions
{
    /// <summary>Unique provider identifier (e.g. "openai", "deepseek").</summary>
    public string ProviderId { get; init; } = "openai-compat";

    /// <summary>Base URL (e.g. "https://api.openai.com/v1"). No trailing slash.</summary>
    public string BaseUrl { get; init; } = "https://api.openai.com/v1";

    /// <summary>Model ID to use (user data, not a constant).</summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>Provider capability flags.</summary>
    public HeyGanesh.Providers.Core.ProviderCapability Capabilities { get; init; } = HeyGanesh.Providers.Core.ProviderCapability.Text;

    /// <summary>Credential store for API key retrieval. Null for keyless providers (e.g. local Ollama).</summary>
    public HeyGanesh.Security.ISecretStore? SecretStore { get; init; }

    /// <summary>Provider ID used for credential lookup in ISecretStore.</summary>
    public string CredentialProviderId { get; init; } = string.Empty;

    /// <summary>Key alias used for credential lookup in ISecretStore.</summary>
    public string CredentialKeyAlias { get; init; } = "api-key";

    /// <summary>Optional organization ID header.</summary>
    public string? OrganizationId { get; init; }

    /// <summary>Request timeout. Default: 120 seconds.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);
}