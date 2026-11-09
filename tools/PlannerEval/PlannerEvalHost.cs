using HeyGanesh.Providers.Core;

namespace HeyGanesh.Tools.PlannerEval;

/// <summary>
/// The planner evaluation host (DEC-043/DEC-044, drawings since DEC-073). PE-1 checks the set itself; PE-9 proves
/// every program check both passes and fails; PE-4/PE-5 prove the recording path offline; <c>--record</c> is the
/// one live mode. PE-2 and PE-3 (the VisualPlan checks) were retired with the VisualPlan stack (DEC-077).
/// Exit codes: 0 pass, 1 failed check or regression, 2 usage, 3 prompt changed since the baseline,
/// 12 key missing or refused, 13 cancelled.
/// </summary>
internal static partial class PlannerEvalHost
{
    private const ProviderCapability StreamingText = ProviderCapability.Text | ProviderCapability.Streaming;

    public static async Task<int> RunAsync(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "--help";
        Console.WriteLine("Planner evaluation host (30 questions, automatic checks; only --record calls a real model)");
        Console.WriteLine();
        try
        {
            return mode switch
            {
                "--assert-set" => RunAssertSet(),
                "--assert-record-stub" => await RunAssertRecordStubAsync(),
                "--assert-drift" => RunAssertDrift(),
                "--assert-program-checks" => await RunAssertProgramChecksAsync(),
                "--assert-program-run" => await RunAssertProgramRunAsync(),
                "--record" => await RunRecordAsync(args[1..]),
                "--replay" => await RunReplayAsync(args[1..]),
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
        Console.WriteLine("Usage: PlannerEval <mode>");
        Console.WriteLine("  --assert-set     30 cases, unique ids, follow-ups resolve, a broken set is refused, exit 0");
        Console.WriteLine("  --assert-record-stub  record drawings through the real client against a loopback stub; no key in any file; replay reproduces it; the baseline gate fires, exit 0");
        Console.WriteLine("  --assert-drift   the live client's settings match the App Shell's lesson client, exit 0");
        Console.WriteLine("  --record [dir] [--cases E-01,M-02]  LIVE: draw the cases with DeepSeek (DEEPSEEK_API_KEY) and write a recording; no key -> exit 12");
        Console.WriteLine("        [--provider claude-code|codex [--model <m>]]  instead plan on your Claude or ChatGPT plan through its CLI (DEC-065/DEC-066); signed out -> exit 12");
        Console.WriteLine("        [--drawing]  accepted for older command lines; every recording is drawings since DEC-077 (run checks P-1..P-5 on a hidden stage when one opens)");
        Console.WriteLine("  --assert-program-checks  the program checks are real: good programs pass, each seed fails its own check, the run-check rules hold, exit 0");
        Console.WriteLine("  --assert-program-run     LOCAL: the run checks on the real stage, hidden (WebView2), exit 0");
        Console.WriteLine("  --replay <dir> [--baseline <dir>]   re-score a recording offline; with a baseline: exit 1 on a regression, 3 when the prompt changed");
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

    private static string Here(params string[] parts) => Path.Combine(new[] { AppContext.BaseDirectory }.Concat(parts).ToArray());

    private static EvalSet LoadSet() => EvalSet.Load(Here("eval-set-v1.json"));

    // PE-1: the set is scoreable at all.
    private static int RunAssertSet()
    {
        var failures = 0;
        var set = LoadSet();
        var problems = set.Problems();
        failures = Check(set.Cases.Count == EvalSet.ExpectedCount, $"{set.Cases.Count} cases in {set.Version}", failures);
        failures = Check(problems.Count == 0, $"no problems in the set{(problems.Count == 0 ? string.Empty : ": " + string.Join("; ", problems))}", failures);
        var bySubject = set.Cases.GroupBy(c => c.Subject).Select(g => $"{g.Key} {g.Count()}");
        failures = Check(set.Cases.GroupBy(c => c.Subject).Count() >= 6, $"subjects: {string.Join(", ", bySubject)}", failures);
        var video = set.Cases.Where(c => c.Id.StartsWith("E-", StringComparison.Ordinal)).ToList();
        failures = Check(video.Count == 6 && video.Count(c => c.Previous is not null) == 3,
            $"{video.Count} cases from the reference video, {video.Count(c => c.Previous is not null)} of them follow-ups", failures);
        failures = Check(set.Cases.Count(c => c.Canvas == ExpectedCanvas.ThreeD) >= 3 && set.Cases.Any(c => c.LibraryMiss is not null) && set.Cases.Any(c => c.Canary is not null),
            "3D cases, a library miss and an injection case are all present", failures);

        // The refusals themselves: a set with a duplicate id and a follow-up to a later case is never scored.
        var broken = set with
        {
            Cases = set.Cases.Take(29).Append(set.Cases[0]).Select((c, i) => i switch
            {
                1 => c with { Previous = "X-01" },
                _ => c,
            }).ToList(),
        };
        var refused = broken.Problems();
        failures = Check(refused.Count == 2 && refused.Any(p => p.Contains("duplicate", StringComparison.Ordinal))
                         && refused.Any(p => p.Contains("not an earlier case", StringComparison.Ordinal)),
            $"a broken set is refused ({refused.Count} problems: {string.Join("; ", refused)})", failures);
        return Summarize(failures, "EVAL SET OK");
    }

    private static string Names(List<string> ids) => ids.Count == 0 ? "none" : string.Join(", ", ids);
}
