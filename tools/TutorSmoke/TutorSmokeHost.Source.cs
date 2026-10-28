using System.Reflection;
using System.Text.RegularExpressions;
using HeyGanesh.TutorEngine;

namespace HeyGanesh.Tools.TutorSmoke;

/// <summary>
/// The architecture audit TU-8/TU-9: the Tutor assembly must carry NO way to touch the screen, no
/// sibling references, and no place a credential could live. String checks run on raw file text;
/// disposal rules run on REFLECTION so the XML docs (which discuss disposal by name) can't false-fail.
/// </summary>
internal static partial class TutorSmokeHost
{
    private static int RunAssertSource()
    {
        var failures = 0;
        var dir = FindModuleDir();
        if (dir is null)
        {
            Console.WriteLine("FAIL  could not locate src/Intelligence/HeyGanesh.TutorEngine from cwd or base dir (exit 1)");
            return 1;
        }
        Console.WriteLine($"auditing {dir}");

        var files = Directory.GetFiles(dir, "*.cs").Append(Path.Combine(dir, "HeyGanesh.TutorEngine.csproj")).ToArray();

        var banned = new (string Token, string Why)[]
        {
            ("DllImport", "no P/Invoke — nothing in the Tutor can actuate"),
            ("user32", "no Win32 input surface"),
            ("GraphicsCapture", "no capture surface"),
            ("<PackageReference", "dependency-free stance"),
            ("<ProjectReference", "no sibling may be referenced — the host wires the edges"),
            ("InternalsVisibleTo", "no assembly-identity hole for tests — TU-11 tests the REAL public construction path"),
        };
        foreach (var (token, why) in banned)
        {
            var hits = files.Where(f => File.ReadAllText(f).Contains(token, StringComparison.Ordinal)).ToArray();
            failures = Check(hits.Length == 0, $"no '{token}' anywhere in the assembly ({why})", failures);
            foreach (var hit in hits)
            {
                Console.WriteLine($"          hit: {Path.GetFileName(hit)}");
            }
        }

        // The one sanctioned env read: the live provider resolves its own key, at call time, in
        // exactly one file (DEC-007's stopgap). Everywhere else is a leak of the boundary.
        var envReads = files
            .Where(f => File.ReadAllText(f).Contains("Environment.GetEnvironmentVariable", StringComparison.Ordinal))
            .ToArray();
        failures = Check(envReads.Length == 1 && Path.GetFileName(envReads[0]) == "DeepSeekTutorClient.cs",
            "the only env-key read in the assembly is DeepSeekTutorClient's call-time DEEPSEEK_API_KEY resolution", failures);
        foreach (var hit in envReads.Where(f => Path.GetFileName(f) != "DeepSeekTutorClient.cs"))
        {
            Console.WriteLine($"          unexpected: {Path.GetFileName(hit)}");
        }

        var foreignUsing = files.Where(f => Regex.IsMatch(File.ReadAllText(f), @"using\s+HeyGanesh\.(?!TutorEngine)")).ToArray();
        failures = Check(foreignUsing.Length == 0, "no 'using HeyGanesh.<sibling>' — ES/Vision/Voice/CO/Overlay stay outside this assembly", failures);
        foreach (var hit in foreignUsing)
        {
            Console.WriteLine($"          hit: {Path.GetFileName(hit)}");
        }

        // Reflection, not text: the service's own XML docs contain the words it must not implement.
        failures = Check(typeof(TutorService).GetInterface("IDisposable") is null, "TutorService implements no IDisposable (it owns nothing to free)", failures);
        failures = Check(typeof(TutorService).GetMethod("Dispose") is null, "TutorService exposes no Dispose method at all (caller-owned seam)", failures);
        failures = Check(typeof(ITutorClient).IsAssignableTo(typeof(IDisposable)), "ITutorClient IS disposable — whoever opened the seam closes it", failures);

        var credentialProps = typeof(TutorOptions).GetProperties()
            .Where(p => p.Name.Contains("Key", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .ToArray();
        failures = Check(credentialProps.Length == 0, "TutorOptions has no field a credential could live in", failures);

        // The provider's own options: no key/secret, no exposed transport (TU-11's stub works via
        // BaseUrl precisely because there is no handler knob to reach for).
        var providerProps = typeof(DeepSeekTutorOptions).GetProperties()
            .Where(p => p.Name.Contains("Key", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("Handler", StringComparison.OrdinalIgnoreCase)
                     || p.PropertyType.Name.Contains("HttpMessageHandler", StringComparison.Ordinal))
            .Select(p => p.Name)
            .ToArray();
        failures = Check(providerProps.Length == 0, "DeepSeekTutorOptions carries no credential and no handler — BaseUrl is the only redirect seam", failures);

        var factoryCtor = typeof(TutorClientFactory).GetConstructors();
        failures = Check(factoryCtor.Length == 0, "TutorClientFactory is not instantiable — it is one method, not a service", failures);
        var factoryMethods = typeof(TutorClientFactory)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name != nameof(ToString))
            .ToArray();
        var factoryLeaks = factoryMethods
            .Where(m => m.ReturnType != typeof(ITutorClient)
                     || m.GetParameters().Any(p =>
                            p.ParameterType.Name.Contains("Handler", StringComparison.Ordinal)
                         || p.ParameterType.Name.Contains("Client", StringComparison.Ordinal)))
            .Select(m => m.Name)
            .ToArray();
        failures = Check(factoryLeaks.Length == 0, "every factory method returns the ITutorClient seam and takes no handler/client parameter", failures);

        return Verdict(failures, "SOURCE AUDIT OK");
    }

    /// <summary>Walks up from cwd and from the binary's directory so the row works from any launch point.</summary>
    private static string? FindModuleDir()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "src", "Intelligence", "HeyGanesh.TutorEngine");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}