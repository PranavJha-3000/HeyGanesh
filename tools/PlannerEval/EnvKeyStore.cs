using System.Text;
using HeyGanesh.Security;

namespace HeyGanesh.Tools.PlannerEval;

/// <summary>
/// The App Shell's <c>EnvironmentSecretStore</c>, copied because that class is internal to the App
/// (DEC-044): a read-only <see cref="ISecretStore"/> over one environment variable, read at request time
/// so the value is never cached here. Nothing in this host logs or formats the value; callers report
/// presence and length only. Goes away with the Security Proxy (build sequence step 8).
/// </summary>
internal sealed class EnvKeyStore : ISecretStore
{
    private readonly string _variable;

    public EnvKeyStore(string variable) => _variable = variable;

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
        throw new NotSupportedException("the environment key store is read-only");

    public void Delete(string providerId, string keyAlias) =>
        throw new NotSupportedException("the environment key store is read-only");

    public void Dispose()
    {
    }
}
