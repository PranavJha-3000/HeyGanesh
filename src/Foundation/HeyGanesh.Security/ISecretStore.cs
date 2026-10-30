namespace HeyGanesh.Security;

/// <summary>
/// Secret store backed by Windows Credential Manager (advapi32 CredWrite/CredRead/CredDelete).
/// Never returns secret material as a string. Callers receive a <see cref="SecretHandle"/>
/// that zeroes its buffer on disposal.
/// </summary>
public interface ISecretStore : IDisposable
{
    /// <summary>Retrieves a secret by provider alias. Returns null when no credential exists.</summary>
    SecretHandle? Get(string providerId, string keyAlias);

    /// <summary>Stores a secret. The caller retains ownership of <paramref name="secret"/>;
    /// the store copies it into Credential Manager.</summary>
    void Set(string providerId, string keyAlias, ReadOnlyMemory<byte> secret);

    /// <summary>Returns true when a credential exists for the given provider/alias.</summary>
    bool Exists(string providerId, string keyAlias);

    /// <summary>Removes the credential for the given provider/alias. No-op when absent.</summary>
    void Delete(string providerId, string keyAlias);
}