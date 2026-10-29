using HeyGanesh.TurnRouter;

namespace HeyGanesh.Tools.TurnRouterSmoke;

/// <summary>
/// Verification host for the Turn Router. Every row runs offline against a recording fake IJevClient
/// and scripted answers. Exit codes: 0 pass, 1 failed check, 2 usage.
/// </summary>
internal static partial class TurnRouterSmokeHost
{
    /// <summary>The shape of the real catalogue, written out so these rows never depend on the Part Library.</summary>
    private static readonly RouteModel[] Models =
    {
        new("four-stroke-engine", "Four-stroke engine", "mechanical engineering"),
        new("dna-helix", "DNA double helix", "biology"),
        new("na-k-pump", "Sodium-potassium pump", "biology"),
    };

    public static async Task<int> RunAsync(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "--help";
        Console.WriteLine("Turn Router verification host (recording fake Jev client, scripted answers, no key)");
        Console.WriteLine();
        try
        {
            return mode switch
            {
                "--assert-source" => RunAssertSource(),
                "--assert-request" => await RunAssertRequestAsync(),
                "--assert-map" => await RunAssertMapAsync(),
                "--assert-gate" => await RunAssertGateAsync(),
                "--assert-errors" => await RunAssertErrorsAsync(),
                "--assert-legs" => RunAssertLegs(),
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
        Console.WriteLine("Usage: TurnRouterSmoke <mode>");
        Console.WriteLine("  --assert-source   only Element Selection referenced; no NuGet, key, network, file, console or P/Invoke use, exit 0");
        Console.WriteLine("  --assert-request  one call, three choice questions, the question in every instruction, facts as state, exit 0");
        Console.WriteLine("  --assert-map      each answer maps to one route; an id never offered refuses the route, exit 0");
        Console.WriteLine("  --assert-gate     below 0.5 is no route (intent) or a 2D lesson (3D or model); 0.5 itself passes, exit 0");
        Console.WriteLine("  --assert-errors   auth, rate-limit, provider faults and cancellation pass through; bad requests never call, exit 0");
        Console.WriteLine("  --assert-legs     the App Shell's leg for every flag and route: --lesson, --no-route, none, point, explain, 2D, 3D, exit 0");
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

    private static RouteRequest Request(string question, bool withModels = true, bool? found = null) =>
        new() { Question = question, WindowTitle = "Notes - Physics", ScreenMatchFound = found, Models = withModels ? Models : Array.Empty<RouteModel>() };

    /// <summary>Routes one request against scripted answers and returns the verdict.</summary>
    private static Task<TurnRoute> RouteWith(RouteRequest request, params (string Id, string? Choice, double? Confidence)[] answers) =>
        new TurnRouter.TurnRouter(new FakeJevClient(_ => FakeJevClient.Answers(answers))).RouteAsync(request, CancellationToken.None);
}
