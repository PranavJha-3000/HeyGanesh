namespace HeyGanesh.Tools.LessonSmoke;

/// <summary>
/// Verification host for the VisualPlan contract. Every row runs offline against golden fixtures and
/// in-code mutations of them — no provider, renderer, key or network. Exit codes: 0 pass, 1 failed
/// check, 2 usage.
/// </summary>
internal static partial class LessonSmokeHost
{
    private enum Mode
    {
        Help,
        AssertSource,
        AssertParse,
        AssertLimits,
    }

    public static Task<int> RunAsync(string[] args)
    {
        var mode = ParseArgs(args);
        if (mode is null)
        {
            return Task.FromResult(2);
        }

        if (mode == Mode.Help)
        {
            return Task.FromResult(PrintUsage());
        }

        Console.WriteLine("Visual Lesson Engine verification host (offline fixtures + contract assertions)");
        Console.WriteLine();

        try
        {
            return Task.FromResult(mode switch
            {
                Mode.AssertSource => RunAssertSource(),
                Mode.AssertParse => RunAssertParse(),
                Mode.AssertLimits => RunAssertLimits(),
                _ => PrintUsage(),
            });
        }
        catch (Exception ex)
        {
            // The contract under test promises never to throw on model output, so any escape here
            // is itself a failed row, not a crash to shrug off.
            Console.WriteLine($"UNEXPECTED: {ex.GetType().Name}: {ex.Message}");
            return Task.FromResult(1);
        }
    }

    private static Mode? ParseArgs(string[] args)
    {
        var mode = Mode.Help;
        foreach (var arg in args)
        {
            switch (arg)
            {
                case "--assert-source": mode = Mode.AssertSource; break;
                case "--assert-parse": mode = Mode.AssertParse; break;
                case "--assert-limits": mode = Mode.AssertLimits; break;
                case "--help": case "-h": case "/?": mode = Mode.Help; break;
                default:
                    Console.WriteLine($"unknown argument '{arg}' (exit 2)");
                    return null;
            }
        }

        return mode;
    }

    private static int PrintUsage()
    {
        Console.WriteLine("Lesson smoke host");
        Console.WriteLine("Usage: LessonSmoke <mode>");
        Console.WriteLine();
        Console.WriteLine("Offline rows (no provider, no renderer, no network):");
        Console.WriteLine("  --assert-source               zero NuGet, Abstractions-only reference, no P/Invoke,");
        Console.WriteLine("                                no network/file/registry access, exit 0");
        Console.WriteLine("  --assert-parse                fixture parse, json fence, malformed/oversize/deep input, exit 0");
        Console.WriteLine("  --assert-limits               v1 limits, id rules, closed vocabularies, exit 0");
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

    /// <summary>Reads a golden fixture copied next to the EXE by the csproj's fixtures\*.json item.</summary>
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));
}
