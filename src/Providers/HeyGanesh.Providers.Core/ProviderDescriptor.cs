namespace HeyGanesh.Providers.Core;

/// <summary>
/// Describes a configured provider: display name, base URL, capabilities, and suggested
/// model IDs. Model IDs are user data, not constants — suggested IDs are hints only.
/// </summary>
public sealed record ProviderDescriptor
{
    /// <summary>Unique provider identifier (e.g. "openai", "anthropic").</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable display name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Base URL for the provider's API.</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>Capability flags the provider advertises.</summary>
    public ProviderCapability Capabilities { get; init; }

    /// <summary>Suggested model IDs (hints, not constants).</summary>
    public IReadOnlyList<string> SuggestedModels { get; init; } = Array.Empty<string>();

    /// <summary>Whether this provider requires an API key.</summary>
    public bool RequiresKey { get; init; } = true;
}