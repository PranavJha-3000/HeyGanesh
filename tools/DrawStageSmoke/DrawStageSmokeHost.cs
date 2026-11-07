using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HeyGanesh.DrawStage;

namespace HeyGanesh.Tools.DrawStageSmoke;

/// <summary>
/// Verification host for the drawing stage (FEATURE-011). Exit codes: 0 pass, 1 failed check, 2 usage.
/// </summary>
internal static class DrawStageSmokeHost
{
    public static int Run(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "--help";
        Console.WriteLine("Drawing stage verification host (vendored openclicky draw-stage, no key, no network)");
        Console.WriteLine();
        try
        {
            return mode switch
            {
                "--assert-source" => RunAssertSource(),
                "--assert-window" => DrawStageSmokeWindow.RunAssertWindow(),
                "--snapshot" when args.Length > 1 => DrawStageSmokeWindow.RunSnapshot(args[1]),
                "--assert-sandbox" => DrawStageSmokeWindow.RunAssertSandbox(),
                "--assert-marks" => DrawStageSmokeWindow.RunAssertMarks(),
                "--help" or "-h" or "/?" => PrintUsage(),
                _ => Usage(mode),
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"UNEXPECTED: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static int Usage(string bad)
    {
        Console.WriteLine($"unknown argument '{bad}' (exit 2)");
        return 2;
    }

    private static int PrintUsage()
    {
        Console.WriteLine("Usage: DrawStageSmoke <mode>");
        Console.WriteLine("  --assert-source   every page file matches VENDOR.md and none is unlisted; CSP has no network; no URL in the page's");
        Console.WriteLine("                    own files; the worker fences the network and workers; licences ship; the build copies the page; exit 0");
        Console.WriteLine("  --assert-window   (local, interactive desktop) the stage window over the cursor monitor: styles, capture exclusion,");
        Console.WriteLine("                    click-through except over reported boxes, never activated, navigation fenced, the page serves");
        Console.WriteLine("                    only its files and the worker its own CSP, a program draws; exit 0");
        Console.WriteLine("  --snapshot <dir>  (local) draws two samples on the stage and saves each as a PNG in <dir> for review; exit 0");
        Console.WriteLine("  --assert-sandbox  (local) programs that reach for the network, workers or the fences fail; a busy statement stops at");
        Console.WriteLine("                    2 s, a program at 20 s in all, 2000 marks at most; no request leaves the page's host; exit 0");
        Console.WriteLine("  --assert-marks    (local) bad and forged marks are skipped with a log line, good ones drawn; exit 0");
        return 0;
    }

    internal static int Check(bool ok, string label, int failures)
    {
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {label}");
        return ok ? failures : failures + 1;
    }

    internal static int Summarize(int failures, string okLabel)
    {
        Console.WriteLine(failures == 0 ? $"{okLabel} (exit 0)" : $"FAILED: {failures} check(s) (exit 1)");
        return failures == 0 ? 0 : 1;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    // DS-1: the vendored page is exactly what VENDOR.md pins, and its no-network fences are in place.
    private static int RunAssertSource()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "HeyGanesh.sln")))
        {
            root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        }

        var dir = Path.Combine(root!, "src", "Experience", "HeyGanesh.DrawStage");
        var page = Path.Combine(dir, "Page");
        var failures = 0;

        // Every file is pinned, and every file present is listed: nothing can be added or changed silently.
        var table = File.ReadAllText(Path.Combine(dir, "VENDOR.md"));
        var pins = Regex.Matches(table, @"^\|\s*`([^`]+)`\s*\|\s*\d+\s*\|\s*`([0-9a-f]{64})`", RegexOptions.Multiline)
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);
        var present = Directory.GetFiles(page, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(page, p).Replace(Path.DirectorySeparatorChar, '/')).ToHashSet(StringComparer.Ordinal);
        failures = Check(pins.Count == 23, $"VENDOR.md pins 23 files (found {pins.Count})", failures);
        var unlisted = present.Where(p => !pins.ContainsKey(p)).ToArray();
        failures = Check(unlisted.Length == 0, $"no page file is missing from VENDOR.md ({(unlisted.Length == 0 ? "none" : string.Join(", ", unlisted))})", failures);
        var mismatched = pins.Where(p => !present.Contains(p.Key) || Hash(Path.Combine(page, p.Key)) != p.Value).Select(p => p.Key).ToArray();
        failures = Check(mismatched.Length == 0, $"every pinned file exists and matches its SHA-256 ({(mismatched.Length == 0 ? "all 23" : "differs: " + string.Join(", ", mismatched))})", failures);

        // three.js r172 as npm publishes it (the hashes jsdelivr lists for three@0.172.0).
        failures = Check(pins.GetValueOrDefault("vendor/three/three.module.min.js") == "7700815c10cddde77e717bc9186f3f9b0fa64034c76622efc5f895459d2c305e"
                && pins.GetValueOrDefault("vendor/three/three.core.min.js") == "85c74c0fe8f35a348282bc1b2c099aa1e29a0ebf453c9df1c5ba68ea7d6d43f5",
            "three.js r172 module and core equal npm three@0.172.0", failures);

        // The page allows no network: default-src 'none' and connect-src 'none' (eval is for the program worker only).
        var html = File.ReadAllText(Path.Combine(page, "index.html"));
        failures = Check(html.Contains("Content-Security-Policy", StringComparison.Ordinal) && html.Contains("default-src 'none'", StringComparison.Ordinal)
                && html.Contains("connect-src 'none'", StringComparison.Ordinal) && !html.Contains("unsafe-inline", StringComparison.Ordinal),
            "index.html CSP: default-src 'none', connect-src 'none', no unsafe-inline", failures);

        // The page's own files name no address; the SVG namespace is an identifier, not a fetch. Vendored three.js is
        // covered by its hash instead.
        string[] own = { "index.html", "stage.js", "stage-worker.js", "figure3d.js", "stage.css" };
        var urls = own.SelectMany(f => Regex.Matches(File.ReadAllText(Path.Combine(page, f)), @"(?:https?|wss?)://[^\s'""<>)]+")
                .Select(m => m.Value).Where(u => u != "http://www.w3.org/2000/svg").Select(u => $"{f}: {u}"))
            .ToArray();
        failures = Check(urls.Length == 0, $"no address in the page's own files ({(urls.Length == 0 ? "none" : string.Join(", ", urls))})", failures);

        // The worker that runs the model's program removes every way out before any program runs.
        var worker = File.ReadAllText(Path.Combine(page, "stage-worker.js"));
        var fence = Regex.Match(worker, @"const FENCED = \[([^\]]*)\]");
        string[] needed = { "fetch", "XMLHttpRequest", "WebSocket", "WebTransport", "EventSource", "importScripts", "Worker", "SharedWorker", "Blob", "Request" };
        var missing = needed.Where(n => !fence.Success || !fence.Groups[1].Value.Contains($"'{n}'", StringComparison.Ordinal)).ToArray();
        failures = Check(missing.Length == 0, $"the worker fences the network and nested workers ({(missing.Length == 0 ? string.Join(", ", needed) : "missing: " + string.Join(", ", missing))})", failures);
        failures = Check(worker.Contains("configurable: false", StringComparison.Ordinal) && worker.Contains("URL.createObjectURL", StringComparison.Ordinal)
                && Regex.IsMatch(worker, @"LIMIT_MARKS\s*=\s*2000\b"),
            "the fences cannot be undone (configurable: false), Blob URLs go too, and the mark limit is 2000", failures);
        var stage = File.ReadAllText(Path.Combine(page, "stage.js"));
        failures = Check(Regex.IsMatch(stage, @"STATEMENT_SECONDS\s*=\s*2\b") && stage.Contains("worker.terminate()", StringComparison.Ordinal),
            "a statement gets 2 s before the page terminates the worker", failures);

        // Step 3 (DEC-070), the one local edit to stage.js: nothing the worker returns is painted unchecked.
        failures = Check(Regex.IsMatch(stage, @"PROGRAM_SECONDS\s*=\s*20\b") && stage.Contains("const problem = markProblem(mark);", StringComparison.Ordinal)
                && stage.Contains("if (m.do === 'photo') return", StringComparison.Ordinal) && stage.Contains("if (m.kind === 'piece') return", StringComparison.Ordinal),
            "stage.js checks every mark before painting it, refuses photo() and f.piece(), and gives a program 20 s of worker time (DEC-070)", failures);

        // MIT: both licences travel with the copied files.
        failures = Check(File.ReadAllText(Path.Combine(page, "LICENSE-openclicky")).Contains("MIT License", StringComparison.Ordinal)
                && File.ReadAllText(Path.Combine(page, "vendor", "three", "LICENSE")).Contains("MIT License", StringComparison.Ordinal),
            "openclicky's and three.js's MIT licences ship with the page", failures);

        // Checkouts must never rewrite the page's line endings, or the hashes break.
        var attributes = File.ReadAllText(Path.Combine(root!, ".gitattributes"));
        failures = Check(attributes.Contains("src/Experience/HeyGanesh.DrawStage/Page/** -text", StringComparison.Ordinal),
            ".gitattributes marks the page -text", failures);

        // Step 2 (DEC-069): only the already-pinned WebView2 package, and only CaptureExclusion's and PixelRect's assemblies.
        var csproj = File.ReadAllText(Path.Combine(dir, "HeyGanesh.DrawStage.csproj"));
        var packages = Regex.Matches(csproj, @"<PackageReference\s+Include=""([^""]+)""\s+Version=""([^""]+)""").Select(m => $"{m.Groups[1].Value} {m.Groups[2].Value}").ToArray();
        var projects = Regex.Matches(csproj, @"<ProjectReference\s+Include=""[^""]*\\([^\\""]+)\.csproj""").Select(m => m.Groups[1].Value).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        failures = Check(packages.SequenceEqual(new[] { "Microsoft.Web.WebView2 1.0.4314.35" })
                && projects.SequenceEqual(new[] { "HeyGanesh.Abstractions", "HeyGanesh.ScreenCapture" }),
            $"the stage project takes only WebView2 1.0.4314.35 (DEC-020) and references only Abstractions and ScreenCapture ({string.Join(", ", packages.Concat(projects))})", failures);

        // The build copies the page beside a consumer, byte for byte.
        var built = DrawStagePage.Directory;
        var copied = pins.Keys.Where(k => File.Exists(Path.Combine(built, k)) && Hash(Path.Combine(built, k)) == pins[k]).Count();
        failures = Check(copied == pins.Count, $"the build copied all {pins.Count} files to {DrawStagePage.Folder}/ unchanged ({copied})", failures);

        return Summarize(failures, "SOURCE AUDIT OK");
    }
}
