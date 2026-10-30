using HeyGanesh.ElementSelection;

namespace HeyGanesh.Tools.ElementSelectionSmoke;

/// <summary>
/// ES-10 (DEC-046): the Jev client takes its key from <see cref="SelectionOptions.ApiKey"/> when one is given,
/// and otherwise reads TYPESAFE_API_KEY as before. Offline: the base URL is an unroutable loopback port, so
/// "the key was accepted" shows as a connection failure, never a request that reaches anyone. The row owns
/// TYPESAFE_API_KEY for its duration only and restores it exactly; no value is printed.
/// </summary>
internal static partial class ElementSelectionSmokeHost
{
    private static async Task<int> RunAssertKeySourceAsync()
    {
        var failures = 0;
        var saved = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", "es10-environment-dummy");
            failures = KeyCheck(await AuthFaultAsync(() => null), true, "a key source returning nothing is a missing key, even with TYPESAFE_API_KEY set", failures);
            failures = KeyCheck(await AuthFaultAsync(null), false, "no key source: TYPESAFE_API_KEY is read as before (accepted, the call goes out)", failures);

            Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", null);
            failures = KeyCheck(await AuthFaultAsync(() => "es10-delegate-dummy"), false, "a key source's key is used with TYPESAFE_API_KEY unset (accepted, the call goes out)", failures);
            failures = KeyCheck(await AuthFaultAsync(null), true, "no key source and no variable: the same missing-key fault as before", failures);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", saved);
        }

        Console.WriteLine(failures == 0 ? "KEY SOURCE OK (exit 0)" : $"FAILED: {failures} check(s) (exit 1)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>True when the call failed as a missing key; false when it got past the key and failed to connect.</summary>
    private static async Task<bool> AuthFaultAsync(Func<string?>? apiKey)
    {
        using var client = JevClients.CreateTypeSafe(new SelectionOptions { BaseUrl = "http://127.0.0.1:1", ApiKey = apiKey, PerCallTimeout = TimeSpan.FromSeconds(5) });
        try
        {
            await client.EvaluateAsync(new JevRequest { Model = "jev-1.13.0" }, CancellationToken.None);
            return false;
        }
        catch (JevAuthenticationException)
        {
            return true;
        }
        catch (Exception)
        {
            // Connection refused on the unroutable port: the key gate was passed.
            return false;
        }
    }

    private static int KeyCheck(bool actual, bool expected, string label, int failures)
    {
        var ok = actual == expected;
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {label}");
        return ok ? failures : failures + 1;
    }
}
