using HeyGanesh.Abstractions;
using HeyGanesh.LessonPlanner;
using HeyGanesh.Providers.Core;
using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.Tools.LessonPlannerSmoke;

/// <summary>
/// Verification host for the Lesson Planner. Every row runs offline against a recording fake
/// ILlmClient and recorded model answers. Exit codes: 0 pass, 1 failed check, 2 usage.
/// </summary>
internal static partial class LessonPlannerSmokeHost
{
    private static readonly PixelRect Desktop = new(0, 0, 2560, 1440);
    private static readonly PixelRect Target = new(1200, 840, 160, 60);
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
                "--assert-prompt" => await RunAssertPromptAsync(),
                "--assert-multimodal" => await RunAssertMultimodalAsync(),
                "--assert-plan" => await RunAssertPlanAsync(),
                "--assert-repair" => await RunAssertRepairAsync(),
                "--assert-errors" => await RunAssertErrorsAsync(),
                "--assert-followup" => await RunAssertFollowupAsync(),
                "--assert-hardening" => await RunAssertHardeningAsync(),
                "--assert-prompt-3d" => await RunAssertPrompt3dAsync(),
                "--assert-stream" => await RunAssertStreamAsync(),
                "--assert-prompt-schematic" => await RunAssertPromptSchematicAsync(),
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
        Console.WriteLine("  --assert-source      only VisualLessonEngine + Providers.Core; no NuGet, I/O, env or P/Invoke, exit 0");
        Console.WriteLine("  --assert-prompt      contract drawn from the code; question first; screen data untrusted, exit 0");
        Console.WriteLine("  --assert-multimodal  image only for a Vision model; otherwise an explicit note, exit 0");
        Console.WriteLine("  --assert-plan        recorded answer -> validated plan, grounded marks enforced, exit 0");
        Console.WriteLine("  --assert-repair      one repair call with the issues; best answer kept, exit 0");
        Console.WriteLine("  --assert-errors      provider faults and cancellation pass through, exit 0");
        Console.WriteLine("  --assert-followup    previous lesson carried as labelled context, exit 0");
        Console.WriteLine("  --assert-hardening   fenced untrusted text, safe repair echo, failed repair keeps the lesson, disposal, exit 0");
        Console.WriteLine("  --assert-prompt-3d   3D offered only with a catalogue; every name and op shape from the code; repair of an invented part, exit 0");
        Console.WriteLine("  --assert-stream      beats released as they validate, for every golden lesson at every chunk size; faults, repair, fallback, timing, exit 0");
        Console.WriteLine("  --assert-prompt-schematic  schematics offered only with a catalogue and the geometry check; block from the code; the F-35B answer plans and streams, exit 0");
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

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    private static PlanRequest Request(string question, ScreenGrounding? screen = null, VisualPlan? previous = null) =>
        new() { Question = question, Screen = screen, VirtualDesktop = Desktop, PreviousPlan = previous };

    private static string UserText(LlmRequest r) => r.Messages.Last(m => m.Role == "user").Text ?? string.Empty;

    private static string FirstUserText(LlmRequest r) => r.Messages.First(m => m.Role == "user").Text ?? string.Empty;
}
