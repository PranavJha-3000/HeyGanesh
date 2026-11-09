using HeyGanesh.Providers.Core;

namespace HeyGanesh.Tools.LessonPlannerSmoke;

/// <summary>
/// Verification host for the Lesson Planner's DrawingPlanner. Every row runs offline against a recording fake
/// ILlmClient and scripted model answers. Exit codes: 0 pass, 1 failed check, 2 usage.
/// </summary>
internal static partial class LessonPlannerSmokeHost
{
    private const ProviderCapability TextOnly = ProviderCapability.Text;
    private const ProviderCapability WithVision = ProviderCapability.Text | ProviderCapability.Vision;

    public static async Task<int> RunAsync(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "--help";
        Console.WriteLine("Lesson Planner verification host (recording fake model, recorded answers, no key)");
        Console.WriteLine();
        try
        {
            return mode switch
            {
                "--assert-source" => RunAssertSource(),
                "--assert-drawing-prompt" => await RunAssertDrawingPromptAsync(),
                "--assert-drawing-stream" => await RunAssertDrawingStreamAsync(),
                "--assert-drawing-none" => await RunAssertDrawingNoneAsync(),
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
        Console.WriteLine("Usage: LessonPlannerSmoke <mode>");
        Console.WriteLine("  --assert-source      only Providers.Core; no NuGet, I/O, env or P/Invoke, exit 0");
        Console.WriteLine("  --assert-drawing-prompt  the drawing planner's prompts: the vendored ones, filled from code, context fenced (DEC-072), exit 0");
        Console.WriteLine("  --assert-drawing-stream  the program read as it streams or whole; only program text reaches the stage; keep(), faults, exit 0");
        Console.WriteLine("  --assert-drawing-none    NONE and prose never reach the stage; a program is a program, exit 0");
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

    private static string UserText(LlmRequest r) => r.Messages.Last(m => m.Role == "user").Text ?? string.Empty;
}
