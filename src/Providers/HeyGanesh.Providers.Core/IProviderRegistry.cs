namespace HeyGanesh.Providers.Core;

/// <summary>
/// Resolves a role to a configured provider implementation. The registry holds assignments
/// and factory delegates; it does not own secret material (that lives in ISecretStore).
/// </summary>
public interface IProviderRegistry
{
    /// <summary>Registers an assignment for a role. Replaces any existing assignment.</summary>
    void Assign(ProviderAssignment assignment);

    /// <summary>Returns the assignment for a role, or null if none configured.</summary>
    ProviderAssignment? GetAssignment(ProviderRole role);

    /// <summary>
    /// Resolves a role to a provider. Throws <see cref="ProviderException"/> with
    /// <see cref="ProviderErrorKind.ModelNotFound"/> when no assignment exists.
    /// </summary>
    ResolvedProvider Resolve(ProviderRole role);

    /// <summary>Returns all configured roles.</summary>
    IReadOnlyList<ProviderRole> GetConfiguredRoles();
}