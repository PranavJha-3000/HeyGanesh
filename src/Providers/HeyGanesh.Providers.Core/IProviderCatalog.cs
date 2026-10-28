namespace HeyGanesh.Providers.Core;

/// <summary>
/// Loads and queries the provider catalog (display names, base URLs, capability flags,
/// suggested models). The catalog is the single source of provider metadata.
/// </summary>
public interface IProviderCatalog
{
    /// <summary>Returns all known providers.</summary>
    IReadOnlyList<ProviderDescriptor> GetAll();

    /// <summary>Returns the descriptor for the given provider ID, or null if unknown.</summary>
    ProviderDescriptor? Get(string providerId);

    /// <summary>Returns true when the provider exists and advertises the given capability.</summary>
    bool HasCapability(string providerId, ProviderCapability capability);

    /// <summary>Returns all providers that advertise the given capability.</summary>
    IReadOnlyList<ProviderDescriptor> GetByCapability(ProviderCapability capability);
}