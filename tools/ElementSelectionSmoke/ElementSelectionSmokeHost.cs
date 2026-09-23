using HeyGanesh.ElementSelection;
using System.Runtime.InteropServices;

namespace HeyGanesh.Tools.ElementSelectionSmoke;

/// <summary>Per-monitor-v2 DPI awareness must be enabled by the host before any coordinate work (physical px).</summary>
internal static class NativeFns
{
    internal static readonly IntPtr PerMonitorV2 = new(-4); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

    [DllImport("user32.dll")]
    internal static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
}

/// <summary>Verification host for the Element Selection module (UIA enumeration + Jev selection).</summary>
internal static partial class ElementSelectionSmokeHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = ParseArgs(args);
        if (options.Help || string.IsNullOrWhiteSpace(options.Question))
        {
            PrintUsage();
            return 0;
        }

        Directory.CreateDirectory(options.OutDir);
        Console.WriteLine("Element Selection verification host (UIA + Jev, .NET 8)");
        Console.WriteLine($"  question : {options.Question}");
        Console.WriteLine($"  target   : {(options.Foreground ? "foreground window" : "window under cursor")}");
        Console.WriteLine($"  mode     : {(options.ListOnly ? "list-only (no Jev call)" : options.Fake ? "fake Jev client (no network)" : "live TypeSafe call")}");
        Console.WriteLine($"  cap      : {options.MaxCandidates} candidates");
        var keyPresent = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY"));
        Console.WriteLine($"  api key  : {(keyPresent ? "present (value suppressed)" : "MISSING -- set TYPESAFE_API_KEY")}");
        Console.WriteLine();

        // --list-only: enumeration + coordinate cross-check without any Jev call (no key needed).
        if (options.ListOnly)
        {
            return RunListOnly(options);
        }

        using ElementSelectionService service = new(
            options.Fake ? new FakeJevClient(options.FakeNone) : null,
            new SelectionOptions { MaxCandidates = options.MaxCandidates });

        SelectionResult result;
        try
        {
            result = await service.SelectAsync(
                options.Question,
                options.Foreground ? SelectionTarget.ForegroundWindow : SelectionTarget.CursorWindow);
        }
        catch (JevAuthenticationException aex)
        {
            Console.WriteLine($"KEY ERROR: {aex.Message}");
            return 12;
        }
        catch (JevClientException cex)
        {
            Console.WriteLine($"API ERROR: {cex.Message}");
            return 3;
        }

        PrintResult(result, options);

        // Coordinate cross-check runs on EVERY mode: UIA's root BoundingRectangle must agree with
        // GetWindowRect — both global physical pixels under this host's PerMonitorV2 awareness.
        if (result.Target is { } target)
        {
            CrossCheck(target);
        }

        if (result.Found && options.Annotate)
        {
            await AnnotateAsync(result, options.OutDir);
        }

        return result.Failed ? 1 : 0;
    }

    private static HostOptions ParseArgs(string[] args)
    {
        var question = ""; var outDir = Path.Combine(AppContext.BaseDirectory, "out");
        var help = false; var fake = false; var fakeNone = false; var foreground = false;
        var listOnly = false; var annotate = false; var maxCandidates = 120;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--question": question = args[++i]; break;
                case "--out": outDir = args[++i]; break;
                case "--max-candidates": maxCandidates = int.Parse(args[++i]); break;
                case "--fake": fake = true; break;
                case "--fake-none": fakeNone = true; break;
                case "--foreground": foreground = true; break;
                case "--list-only": listOnly = true; break;
                case "--annotate": annotate = true; break;
                case "--help": case "-h": case "/?": help = true; break;
                default: Console.WriteLine($"ignoring unknown argument '{args[i]}'"); break;
            }
        }

        return new HostOptions(question, outDir, help, fake, fakeNone, foreground, listOnly, annotate, maxCandidates);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Element Selection smoke host");
        Console.WriteLine("Usage: ElementSelectionSmoke --question \"<text>\" [--foreground] [--list-only]");
        Console.WriteLine("            [--fake] [--fake-none] [--annotate] [--max-candidates <n>] [--out <dir>] [--help]");
        Console.WriteLine();
        Console.WriteLine("--foreground      : walk the focused window instead of the window under the cursor.");
        Console.WriteLine("--list-only       : enumerate + coordinate cross-check only, no Jev call (no key needed).");
        Console.WriteLine("--fake            : offline fake Jev client (picks the top-ranked real candidate).");
        Console.WriteLine("--fake-none       : with --fake, answer the none escape option instead (not-found path).");
        Console.WriteLine("--annotate        : capture the cursor monitor and draw the chosen box (annotated.jpg).");
        Console.WriteLine("--max-candidates  : candidate cap sent to Jev (default 120).");
        Console.WriteLine("--out             : output directory (default ./out).");
        Console.WriteLine("Set TYPESAFE_API_KEY in the environment for live runs. The host never echoes it.");
    }

    private sealed record HostOptions(
        string Question,
        string OutDir,
        bool Help,
        bool Fake,
        bool FakeNone,
        bool Foreground,
        bool ListOnly,
        bool Annotate,
        int MaxCandidates);
}
