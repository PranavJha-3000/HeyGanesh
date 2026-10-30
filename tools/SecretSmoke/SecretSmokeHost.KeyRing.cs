using System.Text;
using HeyGanesh.Security;

namespace HeyGanesh.Tools.SecretSmoke;

/// <summary>
/// KeyRing rows (DEC-045). <c>--assert-keyring</c> runs over an in-memory store and a fake environment, so it
/// proves the rules without touching the machine. <c>--assert-keyring-cm</c> runs over the real Windows
/// Credential Manager, but every target is renamed <c>keyring-test.&lt;name&gt;</c>, so it can never read,
/// overwrite or delete a key the owner stored for the app; it removes what it wrote.
/// </summary>
internal static partial class SecretSmokeHost
{
    private const string DummyKey = "keyring-dummy-not-a-real-key";

    private static int RunAssertKeyRing()
    {
        var failures = 0;
        var store = new MemoryStore();
        var env = new Dictionary<string, string?>(StringComparer.Ordinal);
        using var ring = new KeyRing(store, v => env.GetValueOrDefault(v));

        failures = KCheck(string.Join(",", KeyRing.Known.Select(k => $"{k.Name}={k.EnvironmentVariable}")) == "deepseek=DEEPSEEK_API_KEY,typesafe=TYPESAFE_API_KEY,fish=FISH_AUDIO_API_KEY",
            "three known keys, each with its environment variable", failures);

        var missing = ring.Describe("deepseek");
        failures = KCheck(missing.Source == KeySource.Missing && ring.Read("deepseek") is null && missing.ToString() == "MISSING",
            $"in neither place -> MISSING, nothing read ({missing})", failures);

        env["DEEPSEEK_API_KEY"] = "env-value-12";
        var fromEnv = ring.Describe("deepseek");
        failures = KCheck(fromEnv.Source == KeySource.Environment && fromEnv.Length == 12 && ring.Read("deepseek") == "env-value-12",
            $"environment only -> read from the environment ({fromEnv})", failures);

        var stored = ring.Store("deepseek", "  " + DummyKey + "\r\n");
        var written = Encoding.UTF8.GetString(store.Bytes[$"deepseek:{KeyRing.Alias}"]);
        var fromStore = ring.Describe("DeepSeek");
        failures = KCheck(stored == DummyKey.Length && fromStore.Source == KeySource.CredentialManager && ring.Read("deepseek") == DummyKey && ring.IsStored("deepseek"),
            $"stored (trimmed, {stored} chars) -> the store wins over the environment, names are case-insensitive ({fromStore})", failures);

        var reports = new[] { fromEnv.ToString(), fromStore.ToString(), missing.ToString() };
        failures = KCheck(reports.All(r => !r.Contains(DummyKey, StringComparison.Ordinal) && !r.Contains("env-value", StringComparison.Ordinal)),
            "no report ever contains a value, only presence, length and source", failures);

        var reader = ring.Reader("typesafe");
        var before = reader();
        env["TYPESAFE_API_KEY"] = "late-value";
        failures = KCheck(before is null && reader() == "late-value", "a Reader reads at call time, so a key added later is seen on the next call", failures);

        var refused = new[] { "", "   ", "two words", "line\nbreak", "tab\tkey" }.Count(v =>
        {
            try
            {
                ring.Store("fish", v);
                return false;
            }
            catch (ArgumentException ex) when (ex.ParamName == "value")
            {
                return true;
            }
        });
        failures = KCheck(refused == 5 && !ring.IsStored("fish"), $"empty keys and keys with spaces, line breaks or tabs inside are refused ({refused} of 5), nothing stored", failures);

        var first = ring.Remove("deepseek");
        var second = ring.Remove("deepseek");
        failures = KCheck(first && !second && ring.Describe("deepseek").Source == KeySource.Environment && env["DEEPSEEK_API_KEY"] == "env-value-12",
            "remove deletes the stored key once (true, then false) and falls back to the untouched environment", failures);

        var unknown = false;
        try
        {
            ring.Describe("openai");
        }
        catch (ArgumentException ex) when (ex.ParamName == "name" && ex.Message.Contains("deepseek, typesafe, fish", StringComparison.Ordinal))
        {
            unknown = true;
        }

        failures = KCheck(unknown, "an unknown key name is refused, naming the known ones", failures);

        using var broken = new KeyRing(new MemoryStore { Broken = true }, v => v == "FISH_AUDIO_API_KEY" ? "fish-env" : null);
        var fallback = broken.Describe("fish");
        failures = KCheck(fallback.Source == KeySource.Environment && fallback.StoreUnavailable && broken.Read("fish") == "fish-env" && broken.Describe("deepseek").ToString() == "MISSING [Credential Manager unavailable]",
            $"an unavailable Credential Manager falls back to the environment and says so ({fallback})", failures);

        failures = KCheck(written == DummyKey && store.Sets == 1,
            $"exactly one write reached the store, holding the trimmed key only ({store.Sets} write)", failures);

        return KSummary(failures, "KEYRING OK");
    }

    private static int RunAssertKeyRingCredentialManager()
    {
        var failures = 0;
        using var real = new CredentialManagerSecretStore();
        var store = new RenamedStore(real);
        using var ring = new KeyRing(store, _ => null);
        var names = KeyRing.Known.Select(k => k.Name).ToList();
        var appTargetsBefore = names.Select(n => real.Exists(n, KeyRing.Alias)).ToList();
        foreach (var n in names)
        {
            store.Delete(n, KeyRing.Alias);
        }

        try
        {
            failures = KCheck(names.All(n => ring.Describe(n).Source == KeySource.Missing), "the throwaway targets start empty", failures);
            var length = ring.Store("typesafe", DummyKey);
            var state = ring.Describe("typesafe");
            failures = KCheck(length == DummyKey.Length && state.Source == KeySource.CredentialManager && state.Length == DummyKey.Length && ring.Read("typesafe") == DummyKey,
                $"stored in the real Credential Manager and read back exactly ({state})", failures);
            failures = KCheck(real.Exists("keyring-test.typesafe", KeyRing.Alias) && names.Select(n => real.Exists(n, KeyRing.Alias)).SequenceEqual(appTargetsBefore),
                "written under the throwaway target; the app's own targets are exactly as they were", failures);
            failures = KCheck(ring.Remove("typesafe") && ring.Describe("typesafe").Source == KeySource.Missing && !real.Exists("keyring-test.typesafe", KeyRing.Alias),
                "removed again; nothing left behind", failures);
        }
        finally
        {
            foreach (var n in names)
            {
                store.Delete(n, KeyRing.Alias);
            }
        }

        return KSummary(failures, "KEYRING CREDENTIAL MANAGER OK");
    }

    private static int KCheck(bool ok, string label, int failures)
    {
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {label}");
        return ok ? failures : failures + 1;
    }

    private static int KSummary(int failures, string okLabel)
    {
        Console.WriteLine(failures == 0 ? $"{okLabel} (exit 0)" : $"FAILED: {failures} check(s) (exit 1)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>An in-memory store; <see cref="Broken"/> makes every read fail as a locked-down Credential Manager would.</summary>
    private sealed class MemoryStore : ISecretStore
    {
        public Dictionary<string, byte[]> Bytes { get; } = new(StringComparer.Ordinal);

        public int Sets { get; private set; }

        public bool Broken { get; init; }

        public SecretHandle? Get(string providerId, string keyAlias) =>
            Broken ? throw new SecretStoreException("store unavailable", 1312)
            : Bytes.TryGetValue($"{providerId}:{keyAlias}", out var b) ? SecretHandle.Create(b) : null;

        public void Set(string providerId, string keyAlias, ReadOnlyMemory<byte> secret)
        {
            Sets++;
            Bytes[$"{providerId}:{keyAlias}"] = secret.ToArray();
        }

        public bool Exists(string providerId, string keyAlias) => Bytes.ContainsKey($"{providerId}:{keyAlias}");

        public void Delete(string providerId, string keyAlias) => Bytes.Remove($"{providerId}:{keyAlias}");

        public void Dispose()
        {
        }
    }

    /// <summary>Prefixes every provider id with <c>keyring-test.</c>, so the real store is used but the app's own targets never are.</summary>
    private sealed class RenamedStore : ISecretStore
    {
        private readonly ISecretStore _inner;

        public RenamedStore(ISecretStore inner) => _inner = inner;

        public SecretHandle? Get(string providerId, string keyAlias) => _inner.Get("keyring-test." + providerId, keyAlias);

        public void Set(string providerId, string keyAlias, ReadOnlyMemory<byte> secret) => _inner.Set("keyring-test." + providerId, keyAlias, secret);

        public bool Exists(string providerId, string keyAlias) => _inner.Exists("keyring-test." + providerId, keyAlias);

        public void Delete(string providerId, string keyAlias) => _inner.Delete("keyring-test." + providerId, keyAlias);

        public void Dispose()
        {
        }
    }
}
