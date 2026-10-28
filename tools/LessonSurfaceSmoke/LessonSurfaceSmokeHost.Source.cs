using System.Text.RegularExpressions;

namespace HeyGanesh.Tools.LessonSurfaceSmoke;

/// <summary>
/// LSU-1, the source audit: the one NuGet exception in HeyGanesh stays exactly one package at the
/// pinned version (DEC-020), and the page never gains a way to turn model text into markup or code.
/// </summary>
internal static partial class LessonSurfaceSmokeHost
{
    private static int RunAssertSource()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "HeyGanesh.sln")))
        {
            root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        }

        if (root is null)
        {
            Console.WriteLine("FAIL: could not find HeyGanesh.sln above the host (exit 1).");
            return 1;
        }

        var dir = Path.Combine(root, "src", "Experience", "HeyGanesh.LessonSurface");
        var csproj = File.ReadAllText(Path.Combine(dir, "HeyGanesh.LessonSurface.csproj"));
        var html = File.ReadAllText(Path.Combine(dir, "Page", "lesson.html"));
        var js = File.ReadAllText(Path.Combine(dir, "Page", "lesson.js"));

        var failures = 0;
        var packages = Regex.Matches(csproj, "<PackageReference[^>]*Include=\"([^\"]+)\"[^>]*Version=\"([^\"]+)\"");
        failures = Check(
            packages.Count == 1 && packages[0].Groups[1].Value == "Microsoft.Web.WebView2" && packages[0].Groups[2].Value == "1.0.4314.35",
            "exactly one PackageReference: Microsoft.Web.WebView2 1.0.4314.35 (DEC-020)",
            failures);
        var refs = Regex.Matches(csproj, "<ProjectReference Include=\"[^\"]*\\\\([^\\\\\"]+)\\.csproj\"").Select(m => m.Groups[1].Value).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        failures = Check(
            refs.SequenceEqual(new[] { "HeyGanesh.Abstractions", "HeyGanesh.ScreenCapture", "HeyGanesh.ScreenOverlay", "HeyGanesh.VisualLessonEngine" }),
            $"project references are Abstractions, ScreenCapture, ScreenOverlay, VisualLessonEngine only ({string.Join(", ", refs)})",
            failures);
        failures = Check(
            html.Contains("Content-Security-Policy", StringComparison.Ordinal) && html.Contains("default-src 'self'", StringComparison.Ordinal) && html.Contains("connect-src 'none'", StringComparison.Ordinal),
            "lesson.html carries a CSP: default-src 'self', connect-src 'none'",
            failures);
        string[] sinks = { "innerHTML", "outerHTML", "insertAdjacentHTML", "document.write", "eval(", "new Function", "setTimeout(\"", "setInterval(\"" };
        var found = sinks.Where(s => js.Contains(s, StringComparison.Ordinal)).ToArray();
        failures = Check(found.Length == 0, $"lesson.js has no HTML/code sinks ({(found.Length == 0 ? "none" : string.Join(", ", found))})", failures);
        return Summarize(failures, "SOURCE AUDIT OK");
    }
}
