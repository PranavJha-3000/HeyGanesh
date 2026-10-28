namespace HeyGanesh.Providers.Anthropic;

/// <summary>
/// Configuration for the Anthropic Messages API adapter.
/// </summary>
public sealed class AnthropicOptions
{
    public string ProviderId { get; init; } = "anthropic";
    public string BaseUrl { get; init; } = "https://api.anthropic.com";
    public string ModelId { get; init; } = string.Empty;
    public HeyGanesh.Providers.Core.ProviderCapability Capabilities { get; init; } = HeyGanesh.Providers.Core.ProviderCapability.Text;
    public HeyGanesh.Security.ISecretStore? SecretStore { get; init; }
    public string CredentialProviderId { get; init; } = string.Empty;
    public string CredentialKeyAlias { get; init; } = "api-key";
    public string AnthropicVersion { get; init; } = "2023-06-01";
    public int DefaultMaxTokens { get; init; } = 4096;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);
}