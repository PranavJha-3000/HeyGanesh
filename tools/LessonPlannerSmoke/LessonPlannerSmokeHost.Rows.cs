using System.Text.RegularExpressions;

namespace HeyGanesh.Tools.LessonPlannerSmoke;

/// <summary>
/// LP-1: the source audit. The VisualPlan rows LP-2..LP-13 were retired with the VisualPlan planner in FEATURE-011
/// step 10 (DEC-077); the drawing planner's rows are LP-14..LP-16 (LessonPlannerSmokeHost.Drawing.cs).
/// </summary>
internal static partial class LessonPlannerSmokeHost
{
    private static int RunAssertSource()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "HeyGanesh.sln")))
        {
            root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        }

        var dir = Path.Combine(root!, "src", "Intelligence", "HeyGanesh.LessonPlanner");
        var csproj = File.ReadAllText(Path.Combine(dir, "HeyGanesh.LessonPlanner.csproj"));
        var sources = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText).ToList();
        // "Stream" used to be one needle; since DEC-041 the planner calls ILlmClient.StreamAsync, so the
        // I/O stream types are named instead.
        string[] needles = { "DllImport", "LibraryImport", "HttpClient", "System.Net", "File.", "Directory.", "System.IO", "FileStream", "MemoryStream", "NetworkStream", "StreamReader", "StreamWriter", "Environment.", "Process", "Microsoft.Win32", "Socket" };
        var found = needles.Where(n => sources.Any(s => s.Contains(n, StringComparison.Ordinal))).ToArray();
        var refs = Regex.Matches(csproj, "<ProjectReference Include=\"[^\"]*\\\\([^\\\\\"]+)\\.csproj\"").Select(m => m.Groups[1].Value).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        var failures = 0;
        failures = Check(!csproj.Contains("<PackageReference", StringComparison.Ordinal), "zero PackageReference", failures);
        failures = Check(refs.SequenceEqual(new[] { "HeyGanesh.Providers.Core" }), $"references are Providers.Core only; VisualLessonEngine went with the VisualPlan planner (DEC-077) ({string.Join(", ", refs)})", failures);
        failures = Check(found.Length == 0, $"no P/Invoke, network, file, stream, environment, process or registry use ({(found.Length == 0 ? "none" : string.Join(", ", found))})", failures);
        return Summarize(failures, "SOURCE AUDIT OK");
    }
}
