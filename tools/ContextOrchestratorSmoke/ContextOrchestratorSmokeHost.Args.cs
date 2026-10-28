using HeyGanesh.Abstractions;
namespace HeyGanesh.Tools.ContextOrchestratorSmoke;

// Argument parsing, usage text and the run header for the Context Orchestrator smoke host.

internal static partial class ContextOrchestratorSmokeHost
{
    private static HostOptions ParseArgs(string[] args)
    {
        var question = "";
        var help = false; var foreground = false; var visionNotFound = false; var burst = false;
        FakeJevMode? fakeJevMode = null;
        var fakeDelayMs = 0; var cancelMs = 0; var maxDim = 1280; var maxCandidates = 120;
        var retainCaptures = true;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--question": question = args[++i]; break;
                case "--foreground": foreground = true; break;
                case "--fake": fakeJevMode = FakeJevMode.TopCandidate; break;
                case "--fake-none": fakeJevMode = FakeJevMode.None; break;
                case "--fake-unmapped": fakeJevMode = FakeJevMode.Unmapped; break;
                case "--fake-error": fakeJevMode = FakeJevMode.Error; break;
                case "--fake-vision-none": visionNotFound = true; break;
                case "--fake-delay-ms": fakeDelayMs = int.Parse(args[++i]); break;
                case "--cancel-ms": cancelMs = int.Parse(args[++i]); break;
                case "--burst": burst = true; break;
                case "--no-retain-captures": retainCaptures = false; break;
                case "--max-dim": maxDim = int.Parse(args[++i]); break;
                case "--max-candidates": maxCandidates = int.Parse(args[++i]); break;
                case "--help": case "-h": case "/?": help = true; break;
                default: Console.WriteLine($"ignoring unknown argument '{args[i]}'"); break;
            }
        }

        // A fake Vision answering "not found" only has anything to say on a fallback session, so it
        // implies a failed primary leg unless the caller named one explicitly.
        if (visionNotFound && fakeJevMode is null)
        {
            fakeJevMode = FakeJevMode.Unmapped;
        }

        // --burst needs the first session to still be in flight when the second trigger arrives: an
        // offline ES client, deliberately slow. A live one would refuse in milliseconds without a key,
        // which would leave the guard untested.
        if (burst)
        {
            if (fakeJevMode is null)
            {
                Console.WriteLine("note: --burst implies --fake (an offline primary leg); added it.");
                fakeJevMode = FakeJevMode.TopCandidate;
            }

            if (fakeDelayMs == 0)
            {
                fakeDelayMs = 400;
            }
        }

        // --cancel-ms needs the session to still be inside the primary leg when the token fires.
        if (cancelMs > 0 && fakeDelayMs <= cancelMs)
        {
            fakeDelayMs = cancelMs + 300;
        }

        // "Live" is decided by the ES client only: with a fake ES client the fallback leg is faked too,
        // so no mode here mixes a fake primary leg with a real cloud fallback.
        var live = fakeJevMode is null;

        return new HostOptions(
            question, help, foreground, live, fakeJevMode, visionNotFound, fakeDelayMs, cancelMs,
            burst, maxDim, maxCandidates, retainCaptures);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Context Orchestrator smoke host");
        Console.WriteLine("Usage: ContextOrchestratorSmoke --question \"<text>\" [--foreground] [--fake | --fake-none |");
        Console.WriteLine("                                  --fake-unmapped | --fake-error] [--fake-vision-none]");
        Console.WriteLine("                                  [--fake-delay-ms <n>] [--cancel-ms <n>] [--burst]");
        Console.WriteLine("                                  [--max-dim <px>] [--max-candidates <n>] [--help]");
        Console.WriteLine();
        Console.WriteLine("Every mode runs the same session: capture -> Element Selection (primary) -> Vision only on a");
        Console.WriteLine("genuine primary failure -> one normalized GuidanceResult for the next module.");
        Console.WriteLine();
        Console.WriteLine("--foreground      : walk the focused window instead of the window under the cursor.");
        Console.WriteLine("--fake            : fake ES client answers the top-ranked real candidate (primary verdict).");
        Console.WriteLine("--fake-none       : fake ES client answers the none option (honest no-match; must NOT fall back).");
        Console.WriteLine("--fake-unmapped   : fake ES client answers an id never offered -> ES fails -> Vision fallback.");
        Console.WriteLine("--fake-error      : fake ES client throws -> the orchestrator must propagate, not swallow.");
        Console.WriteLine("--fake-vision-none: the fallback answers \"not found\" (implies --fake-unmapped; Vision");
        Console.WriteLine("                    reports a model-side not-found as a Failed result, so expect Failed, Low, no box).");
        Console.WriteLine("--fake-delay-ms   : delay inside the fake ES client, so a session can be caught in flight.");
        Console.WriteLine("--cancel-ms       : cancel the session token after n ms (exit 13; no verdict produced).");
        Console.WriteLine("--burst           : two overlapping triggers; the second must be refused (one session at a time).");
        Console.WriteLine("--no-retain-captures: drop the captured frames from the verdict (RetainCaptures=false).");
        Console.WriteLine("--max-dim         : capture downscale cap in px (default 1280).");
        Console.WriteLine("--max-candidates  : candidate cap handed to Element Selection (default 120).");
        Console.WriteLine("Live runs need TYPESAFE_API_KEY (primary leg) and DEEPSEEK_API_KEY (fallback leg). The host");
        Console.WriteLine("prints key presence only -- never the value.");
    }

    private static void PrintHeader(HostOptions options)
    {
        var typesafeKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY"));
        var deepseekKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY"));

        Console.WriteLine("Context Orchestrator verification host (capture -> Element Selection -> Vision fallback)");
        Console.WriteLine($"  question  : {options.Question}");
        Console.WriteLine($"  target    : {(options.Foreground ? "foreground window" : "window under cursor")}");
        Console.WriteLine($"  mode      : {options.Mode}");
        Console.WriteLine($"  keys      : TYPESAFE_API_KEY={(typesafeKey ? "present (value suppressed)" : "MISSING")}  " +
                          $"DEEPSEEK_API_KEY={(deepseekKey ? "present (value suppressed)" : "MISSING")}");
        if (options.FakeDelayMs > 0)
        {
            Console.WriteLine($"  fake delay: {options.FakeDelayMs} ms inside the fake ES client");
        }

        Console.WriteLine();
    }


    private sealed record HostOptions(
        string Question,
        bool Help,
        bool Foreground,
        bool Live,
        FakeJevMode? FakeJevMode,
        bool VisionNotFound,
        int FakeDelayMs,
        int CancelMs,
        bool Burst,
        int MaxDim,
        int MaxCandidates,
        bool RetainCaptures)
    {
        /// <summary>Human-readable mode line for the run header.</summary>
        public string Mode => Live
            ? "live (real TypeSafe + DeepSeek; needs both keys)"
            : $"offline fake ES client ({FakeJevMode})" +
              (VisionNotFound ? " + fake Vision answering not-found" : string.Empty) +
              (Burst ? " + burst" : string.Empty);
    }
}
