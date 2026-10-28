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
        var sources = Directory.GetFiles(moduleDir, "*.cs", SearchOption.TopDirectoryOnly)
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
        failures = Check(
            !AnySource("System.Net") && !AnySource("File.") && !AnySource("Registry"),
            "no network, file or registry access",
            failures);
        return Summarize(failures, "SOURCE AUDIT OK");
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
