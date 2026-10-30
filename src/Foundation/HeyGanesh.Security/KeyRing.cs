using System.Text;

namespace HeyGanesh.Security;

/// <summary>One provider key the app knows: its short name and the environment variable that is its fallback.</summary>
public sealed record KeyName(string Name, string EnvironmentVariable);

/// <summary>Where a key was found. Never the key itself.</summary>
public enum KeySource
{
    Missing = 0,
    CredentialManager = 1,
    Environment = 2,
}

/// <summary>
/// A key described without its value: where it came from and how long it is. <see cref="ToString"/> is what
/// every console line prints, so a key can only ever be reported this way.
/// </summary>
public sealed record KeyState(string Name, KeySource Source, int Length, bool StoreUnavailable)
{
    public override string ToString() => Source switch
    {
        KeySource.CredentialManager => $"present ({Length} chars, Credential Manager)",
        KeySource.Environment => $"present ({Length} chars, environment)",
        _ => "MISSING",
    } + (StoreUnavailable ? " [Credential Manager unavailable]" : string.Empty);
}

/// <summary>
/// The one place the app's provider keys come from (the "API key security proxy", DEC-045). A key is read
/// from Windows Credential Manager first (per user, encrypted by Windows, through
/// <see cref="CredentialManagerSecretStore"/>), then from its environment variable, the arrangement DEC-007
/// started with, kept as a fallback so CI and every documented command still work.
/// </summary>
/// <remarks>
/// <para>
/// Keys are read at call time and never cached here: <see cref="Reader"/> hands a client a delegate, so a key
/// stored or removed while the app runs takes effect on the next request. <see cref="Read"/> returns a
/// string because an HTTP <c>Authorization</c> header is one; callers use it for that header and nothing
/// else, and the <see cref="SecretHandle"/> it came from is cleared at once.
/// </para>
/// <para>
/// A Credential Manager failure (a locked-down machine) is not fatal: the environment is tried and the
/// state says the store was unavailable. Nothing here logs; <see cref="Describe"/> is the only report.
/// </para>
/// </remarks>
public sealed class KeyRing : IDisposable
{
    /// <summary>The credential alias every key is stored under: target <c>HeyGanesh:&lt;name&gt;:api-key</c>.</summary>
    public const string Alias = "api-key";

    public static IReadOnlyList<KeyName> Known { get; } = new[]
    {
        new KeyName("deepseek", "DEEPSEEK_API_KEY"),
        new KeyName("typesafe", "TYPESAFE_API_KEY"),
        new KeyName("fish", "FISH_AUDIO_API_KEY"),
    };

    private readonly ISecretStore _store;
    private readonly bool _ownsStore;
    private readonly Func<string, string?> _environment;

    /// <summary>The app's ring: Windows Credential Manager, then the process environment.</summary>
    public KeyRing()
        : this(new CredentialManagerSecretStore(), Environment.GetEnvironmentVariable, ownsStore: true)
    {
    }

    /// <summary>A ring over another store and environment (verification hosts; the store is not disposed).</summary>
    public KeyRing(ISecretStore store, Func<string, string?> environment)
        : this(store, environment, ownsStore: false)
    {
    }

    private KeyRing(ISecretStore store, Func<string, string?> environment, bool ownsStore)
    {
        _store = store;
        _environment = environment;
        _ownsStore = ownsStore;
    }

    /// <summary>The known key called <paramref name="name"/>; an unknown name is refused with the list of known ones.</summary>
    public static KeyName Find(string name) =>
        Known.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"unknown key '{name}'; known keys: {string.Join(", ", Known.Select(k => k.Name))}", nameof(name));

    public KeyState Describe(string name)
    {
        var key = Find(name);
        var stored = TryStored(key, out var unavailable);
        if (stored is not null)
        {
            using (stored)
            {
                return new KeyState(key.Name, KeySource.CredentialManager, stored.Length, unavailable);
            }
        }

        var env = _environment(key.EnvironmentVariable);
        return string.IsNullOrWhiteSpace(env)
            ? new KeyState(key.Name, KeySource.Missing, 0, unavailable)
            : new KeyState(key.Name, KeySource.Environment, env.Length, unavailable);
    }

    /// <summary>True when the key is in Credential Manager itself (not only in the environment).</summary>
    public bool IsStored(string name)
    {
        using var stored = TryStored(Find(name), out _);
        return stored is not null;
    }

    /// <summary>The key's value for an Authorization header, or null when it is in neither place.</summary>
    public string? Read(string name)
    {
        var key = Find(name);
        using (var stored = TryStored(key, out _))
        {
            if (stored is not null)
            {
                return Encoding.UTF8.GetString(stored.AsMemory());
            }
        }

        var env = _environment(key.EnvironmentVariable);
        return string.IsNullOrWhiteSpace(env) ? null : env;
    }

    /// <summary>A delegate a client calls per request; it holds the ring, never a copy of the key.</summary>
    public Func<string?> Reader(string name)
    {
        var key = Find(name);
        return () => Read(key.Name);
    }

    /// <summary>
    /// Stores <paramref name="value"/> (surrounding whitespace trimmed) in Credential Manager and returns its
    /// length. Refused: an empty key, and one with whitespace or control characters inside (a pasted line
    /// break or a second word is never part of a key). The transient byte copy is cleared.
    /// </summary>
    public int Store(string name, ReadOnlySpan<char> value)
    {
        var key = Find(name);
        var trimmed = value.Trim();
        if (trimmed.IsEmpty)
        {
            throw new ArgumentException("the key is empty", nameof(value));
        }

        foreach (var c in trimmed)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                throw new ArgumentException("the key contains a space, a line break or a control character", nameof(value));
            }
        }

        var bytes = new byte[Encoding.UTF8.GetByteCount(trimmed)];
        try
        {
            Encoding.UTF8.GetBytes(trimmed, bytes);
            _store.Set(key.Name, Alias, bytes);
            return trimmed.Length;
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    /// <summary>Deletes the stored key; true when there was one. The environment variable is not touched.</summary>
    public bool Remove(string name)
    {
        var key = Find(name);
        var existed = _store.Exists(key.Name, Alias);
        _store.Delete(key.Name, Alias);
        return existed;
    }

    public void Dispose()
    {
        if (_ownsStore)
        {
            _store.Dispose();
        }
    }

    private SecretHandle? TryStored(KeyName key, out bool unavailable)
    {
        unavailable = false;
        try
        {
            var handle = _store.Get(key.Name, Alias);
            if (handle is { Length: 0 })
            {
                handle.Dispose();
                return null;
            }

            return handle;
        }
        catch (SecretStoreException)
        {
            unavailable = true;
            return null;
        }
    }
}
