using System.Text.RegularExpressions;

namespace HeyGanesh.Tools.LessonSmoke;

/// <summary>
/// LS-1, the source audit: the contract assembly must stay a pure data module. A NuGet package,
/// a second project reference, an interop declaration or any I/O would mean the module that decides
/// what is safe to draw had grown a way to reach the outside world.
/// </summary>
internal static partial class LessonSmokeHost
{
    private static int RunAssertSource()
    {
        var root = FindRepoRoot();
        if (root is null)
        {
            Console.WriteLine("FAIL: could not find HeyGanesh.sln above the host (exit 1).");
            return 1;
        }

        var moduleDir = Path.Combine(root, "src", "Intelligence", "HeyGanesh.VisualLessonEngine");
        var csproj = File.ReadAllText(Path.Combine(moduleDir, "HeyGanesh.VisualLessonEngine.csproj"));
        // Every source the module will ever compile, including future Planning/ and Runtime/ folders;
        // bin/obj are build output (obj holds the generated global usings, which name System.Net.Http).
        var sources = Directory.GetFiles(moduleDir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText)
            .ToList();
        bool AnySource(string needle) => sources.Any(s => s.Contains(needle, StringComparison.Ordinal));

        var failures = 0;
        failures = Check(!csproj.Contains("<PackageReference", StringComparison.Ordinal), "zero PackageReference", failures);
        failures = Check(
            Regex.Matches(csproj, "<ProjectReference").Count == 1 && csproj.Contains("HeyGanesh.Abstractions", StringComparison.Ordinal),
            "only ProjectReference is HeyGanesh.Abstractions",
            failures);
        failures = Check(!AnySource("DllImport") && !AnySource("LibraryImport"), "empty P/Invoke surface", failures);
        var found = sources.Select(ForbiddenIn).FirstOrDefault(n => n is not null);
        failures = Check(found is null, $"no network, file, process, environment or registry access{(found is null ? "" : $" (found '{found}')")}", failures);
        // Final review I-6: the needles must catch I/O that ImplicitUsings makes namespace-free.
        var samples = new[] { "new HttpClient()", "new StreamReader(p)", "new FileStream(p, m)", "Directory.GetFiles(d)", "Environment.GetEnvironmentVariable(k)", "Process.Start(x)", "new Socket(a, b, c)" };
        failures = Check(samples.All(s => ForbiddenIn(s) is not null), "audit needles flag namespace-free I/O samples (HttpClient, streams, Directory, Environment, Process, Socket)", failures);
        failures = Check(!csproj.Contains("<Compile", StringComparison.Ordinal), "csproj pulls in no extra <Compile> sources", failures);

        return Summarize(failures, "SOURCE AUDIT OK");
    }

    /// <summary>
    /// The first I/O marker in <paramref name="source"/>, or null. ImplicitUsings imports System.IO
    /// and System.Net.Http, so namespace needles alone miss most I/O; these are type/member names.
    /// </summary>
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

    private static string? FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HeyGanesh.sln")))
            {
                return dir.FullName;
            }
        }

        return null;
    }
}
