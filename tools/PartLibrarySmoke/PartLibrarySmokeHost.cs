using System.Text.RegularExpressions;

namespace HeyGanesh.Tools.PartLibrarySmoke;

/// <summary>
/// Verification host for the Part Library. Every row is offline: no renderer, provider, key or
/// network. Exit codes: 0 pass, 1 failed check, 2 usage.
/// </summary>
internal static partial class PartLibrarySmokeHost
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "/?")
        {
            return PrintUsage();
        }

        Console.WriteLine("Part Library verification host (offline structure, physics and catalogue rows)");
        Console.WriteLine();
        try
        {
            return args[0] switch
            {
                "--assert-source" when args.Length == 1 => RunAssertSource(),
                "--assert-library" when args.Length == 1 => RunAssertLibrary(),
                "--assert-physics" when args.Length == 1 => RunAssertPhysics(),
                "--assert-catalog" when args.Length == 1 => RunAssertCatalog(),
                "--dump" when args.Length == 2 => RunDump(args[1]),
                _ => Usage($"unknown arguments '{string.Join(' ', args)}'"),
            };
        }
        catch (Exception ex)
        {
            // The library and its checks promise not to throw on shipped models; any escape is a failed row.
            Console.WriteLine($"UNEXPECTED: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static int Usage(string why)
    {
        Console.WriteLine($"{why} (exit 2)");
        PrintUsage();
        return 2;
    }

    private static int PrintUsage()
    {
        Console.WriteLine("Part library smoke host");
        Console.WriteLine("Usage: PartLibrarySmoke <mode>");
        Console.WriteLine("  --assert-source        zero NuGet, VisualLessonEngine-only reference, no P/Invoke, no I/O, exit 0");
        Console.WriteLine("  --assert-library       every model meets the structural rules; each rule fires on a broken model, exit 0");
        Console.WriteLine("  --assert-physics       slider-crank, B-DNA and pump numbers hold in the shipped data, exit 0");
        Console.WriteLine("  --assert-catalog       ToCatalog() matches the models; lessons validate against it; nothing Reviewed yet, exit 0");
        Console.WriteLine("  --dump <model|all>     plain-text review dump of the parts, descriptions and animations, exit 0");
        return 0;
    }

    private static int Check(bool ok, string label, int failures)
    {
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {label}");
        return ok ? failures : failures + 1;
    }

    private static int Summarize(int failures, string okLabel)
    {
        Console.WriteLine(failures == 0 ? $"{okLabel} (exit 0)" : $"FAILED: {failures} check(s) (exit 1)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>PL-1: the library stays pure data - the same audit needles as LessonSmoke's LS-1.</summary>
    private static int RunAssertSource()
    {
        var failures = 0;
        var root = FindRoot();
        if (root is null)
        {
            Console.WriteLine("FAIL: could not find HeyGanesh.sln above the host (exit 1).");
            return 1;
        }

        var moduleDir = Path.Combine(root, "src", "Intelligence", "HeyGanesh.PartLibrary");
        var csproj = File.ReadAllText(Path.Combine(moduleDir, "HeyGanesh.PartLibrary.csproj"));
        var sep = Path.DirectorySeparatorChar;
        var sources = Directory.GetFiles(moduleDir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{sep}obj{sep}", StringComparison.OrdinalIgnoreCase) && !p.Contains($"{sep}bin{sep}", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p => p, File.ReadAllText);
        Console.WriteLine($"  auditing {sources.Count} source files in {moduleDir}");

        failures = Check(!csproj.Contains("<PackageReference", StringComparison.Ordinal), "zero PackageReference", failures);
        failures = Check(Regex.Matches(csproj, "<ProjectReference").Count == 1 && csproj.Contains("HeyGanesh.VisualLessonEngine.csproj", StringComparison.Ordinal),
            "only ProjectReference is HeyGanesh.VisualLessonEngine", failures);
        failures = Check(!sources.Values.Any(s => s.Contains("DllImport", StringComparison.Ordinal) || s.Contains("LibraryImport", StringComparison.Ordinal)), "empty P/Invoke surface", failures);
        var found = sources.Select(kv => (File: Path.GetFileName(kv.Key), Hit: ForbiddenIn(kv.Value))).FirstOrDefault(x => x.Hit is not null);
        failures = Check(found.Hit is null, $"no network, file, process, environment or registry access{(found.Hit is null ? "" : $" (found '{found.Hit}' in {found.File})")}", failures);
        failures = Check(new[] { "new HttpClient()", "File.ReadAllText(p)", "Environment.GetEnvironmentVariable(k)", "Process.Start(x)" }.All(s => ForbiddenIn(s) is not null),
            "audit needles flag I/O samples", failures);
        return Summarize(failures, "SOURCE AUDIT OK");
    }

    private static string? ForbiddenIn(string source)
    {
        string[] needles =
        {
            "System.Net", "HttpClient", "Socket", "WebRequest",
            "File.", "FileStream", "StreamReader", "StreamWriter", "Directory.", "FileInfo", "DirectoryInfo",
            "Process", "Environment.", "Registry",
        };
        return needles.FirstOrDefault(n => source.Contains(n, StringComparison.Ordinal));
    }

    private static string? FindRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "HeyGanesh.sln")))
                {
                    return dir.FullName;
                }
            }
        }

        return null;
    }
}
