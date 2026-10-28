using System.Text;
using HeyGanesh.Security;

namespace HeyGanesh.App;

/// <summary>
/// A read-only <see cref="ISecretStore"/> over one environment variable — the repo's current key
/// arrangement (DEC-017: DEEPSEEK_API_KEY "pending the Security Proxy"), adapted to the seam the
/// provider adapters read credentials through. Every lookup, whatever its provider/alias, answers with
/// that one variable, read at request time so the value is never cached in the shell.
/// </summary>
/// <remarks>
/// The handle is disposed by the adapter straight after use. Nothing here logs or formats the value;
/// SecretHandle.ToString is already value-suppressed. Writing or deleting is not this store's job —
/// the environment is owned by whoever launched the process — so those calls refuse.
/// </remarks>
internal sealed class EnvironmentSecretStore : ISecretStore
{
    private readonly string _variable;

    public EnvironmentSecretStore(string variable) => _variable = variable;

    public SecretHandle? Get(string providerId, string keyAlias)
    {
        var value = Environment.GetEnvironmentVariable(_variable);
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

    public bool Exists(string providerId, string keyAlias) =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(_variable));

    public void Set(string providerId, string keyAlias, ReadOnlyMemory<byte> secret) =>
        throw new NotSupportedException("the environment secret store is read-only");

    public void Delete(string providerId, string keyAlias) =>
        throw new NotSupportedException("the environment secret store is read-only");

    public void Dispose()
    {
    }
}
