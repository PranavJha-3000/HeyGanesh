using System.Text;
using HeyGanesh.Security;

namespace HeyGanesh.App;

/// <summary>
/// A read-only <see cref="ISecretStore"/> over one key of the shell's <see cref="KeyRing"/> (DEC-046): the
/// seam the provider adapters read credentials through, answered from Windows Credential Manager first and
/// the environment variable second. Every lookup, whatever its provider/alias, answers with that one key,
/// read at request time so the value is never cached in the shell.
/// </summary>
/// <remarks>
/// It replaced <c>EnvironmentSecretStore</c> (DEC-027), which read the environment variable directly. The
/// handle is disposed by the adapter straight after use. Nothing here logs or formats the value;
/// SecretHandle.ToString is already value-suppressed. Storing and deleting are the key commands' job
/// (<c>--set-key</c> / <c>--remove-key</c>), so those calls refuse.
/// </remarks>
internal sealed class KeyRingSecretStore : ISecretStore
{
    private readonly KeyRing _ring;
    private readonly string _name;

    public KeyRingSecretStore(KeyRing ring, string name)
    {
        _ring = ring;
        _name = KeyRing.Find(name).Name;
    }

    public SecretHandle? Get(string providerId, string keyAlias)
    {
        var value = _ring.Read(_name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // The handle takes its own copy; the transient encoding buffer is cleared, not left to the GC.
        var bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            return SecretHandle.Create(bytes);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    public bool Exists(string providerId, string keyAlias) => _ring.Describe(_name).Source != KeySource.Missing;

    public void Set(string providerId, string keyAlias, ReadOnlyMemory<byte> secret) =>
        throw new NotSupportedException("the key ring store is read-only here; use --set-key");

    public void Delete(string providerId, string keyAlias) =>
        throw new NotSupportedException("the key ring store is read-only here; use --remove-key");

    public void Dispose()
    {
    }
}
