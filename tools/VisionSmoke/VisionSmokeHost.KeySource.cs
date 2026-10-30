using HeyGanesh.Abstractions;
using HeyGanesh.Vision;

namespace HeyGanesh.Tools.VisionSmoke;

/// <summary>
/// V-9 (DEC-046): the Vision client takes its key from <see cref="VisionOptions.ApiKey"/> when one is given,
/// and otherwise reads DEEPSEEK_API_KEY as before. Offline: a tiny synthetic capture and an unroutable
/// loopback base URL, so "the key was accepted" shows as a connection failure. The row owns DEEPSEEK_API_KEY
/// for its duration only and restores it exactly; no value is printed.
/// </summary>
internal static partial class VisionSmokeHost
{
    private static async Task<int> RunAssertKeySourceAsync()
    {
        var failures = 0;
        var saved = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", "v3-environment-dummy");
            failures = KeyCheck(await AuthFaultAsync(() => null), true, "a key source returning nothing is a missing key, even with DEEPSEEK_API_KEY set", failures);
            failures = KeyCheck(await AuthFaultAsync(null), false, "no key source: DEEPSEEK_API_KEY is read as before (accepted, the call goes out)", failures);

            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null);
            failures = KeyCheck(await AuthFaultAsync(() => "v3-delegate-dummy"), false, "a key source's key is used with DEEPSEEK_API_KEY unset (accepted, the call goes out)", failures);
            failures = KeyCheck(await AuthFaultAsync(null), true, "no key source and no variable: the same missing-key fault as before", failures);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", saved);
        }

        Console.WriteLine(failures == 0 ? "KEY SOURCE OK (exit 0)" : $"FAILED: {failures} check(s) (exit 1)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>True when the call failed as a missing key; false when it got past the key (and failed to connect, or answered "failed").</summary>
    private static async Task<bool> AuthFaultAsync(Func<string?>? apiKey)
    {
        using var vision = new VisionService(options: new VisionOptions
        {
            BaseUrl = "http://127.0.0.1:1",
            ApiKey = apiKey,
            MaxSamples = 1,
            PerCallTimeout = TimeSpan.FromSeconds(5),
        });
        var capture = new MonitorCapture(new MonitorInfo(IntPtr.Zero, "synthetic", new PixelRect(0, 0, 16, 16), true), 16, 16, 16, 16, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }, double.NaN, 1, TimeSpan.Zero);
        try
        {
            await vision.LocateAsync(capture, "where is the Save button");
            return false;
        }
        catch (VisionAuthenticationException)
        {
            return true;
        }
        catch (Exception)
        {
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
