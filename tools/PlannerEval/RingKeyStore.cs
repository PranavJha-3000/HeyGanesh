using System.Text;
using HeyGanesh.Security;

namespace HeyGanesh.Tools.PlannerEval;

/// <summary>
/// The App Shell's <c>KeyRingSecretStore</c>, copied because that class is internal to the App (DEC-046): a
/// read-only <see cref="ISecretStore"/> over one key of a <see cref="KeyRing"/>, read at request time so the
/// value is never cached here. A live recording uses the app's own ring (Credential Manager, then
/// <c>DEEPSEEK_API_KEY</c>); PE-4 uses a ring over an empty store and a dummy environment, so it never touches
/// either. Nothing in this host logs or formats the value; callers report <see cref="KeyState"/> only.
/// </summary>
internal sealed class RingKeyStore : ISecretStore
{
    private readonly KeyRing _ring;
    private readonly string _name;

    public RingKeyStore(KeyRing ring, string name)
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
        throw new NotSupportedException("the eval key store is read-only");

    public void Delete(string providerId, string keyAlias) =>
        throw new NotSupportedException("the eval key store is read-only");

    public void Dispose()
    {
    }
}
