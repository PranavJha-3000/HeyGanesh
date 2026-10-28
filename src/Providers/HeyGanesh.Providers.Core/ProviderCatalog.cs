namespace HeyGanesh.Providers.Core;

/// <summary>
/// In-memory provider catalog. Populated from a list of descriptors (typically loaded
/// from providers.catalog.json by the host). Thread-safe for concurrent reads after
/// construction.
/// </summary>
public sealed class ProviderCatalog : IProviderCatalog
{
    private readonly Dictionary<string, ProviderDescriptor> _descriptors;

    public ProviderCatalog(IEnumerable<ProviderDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        _descriptors = new Dictionary<string, ProviderDescriptor>(StringComparer.Ordinal);
        foreach (var d in descriptors)
        {
            if (string.IsNullOrWhiteSpace(d.Id))
                throw new ArgumentException("Provider descriptor must have a non-empty Id.", nameof(descriptors));
            _descriptors[d.Id] = d;
        }
    }

    public IReadOnlyList<ProviderDescriptor> GetAll() => _descriptors.Values.ToList().AsReadOnly();

    public ProviderDescriptor? Get(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return null;
        _descriptors.TryGetValue(providerId, out var d);
        return d;
    }

    public bool HasCapability(string providerId, ProviderCapability capability)
    {
        var d = Get(providerId);
        if (d is null) return false;
        return (d.Capabilities & capability) == capability;
    }

    public IReadOnlyList<ProviderDescriptor> GetByCapability(ProviderCapability capability)
        => _descriptors.Values.Where(d => (d.Capabilities & capability) == capability).ToList().AsReadOnly();
}