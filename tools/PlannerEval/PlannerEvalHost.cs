using System.Text.Json;
using System.Text.Json.Nodes;
using HeyGanesh.Abstractions;
using HeyGanesh.LessonPlanner;
using HeyGanesh.PartLibrary;
using HeyGanesh.PartLibrary.Models;
using HeyGanesh.Providers.Core;
using HeyGanesh.VisualLessonEngine;
using FakeLlm = HeyGanesh.Tools.LessonPlannerSmoke.FakeLlmClient;

namespace HeyGanesh.Tools.PlannerEval;

/// <summary>
/// The planner evaluation host (DEC-043/DEC-044). PE-1 checks the set itself; PE-2 runs golden and seed
/// answers through the real VisualPlanner (streaming path, recording fake model) and proves every check both
/// passes and fails; PE-4/PE-5 prove the recording path offline; <c>--record</c> is the one live mode.
/// Exit codes: 0 pass, 1 failed check or regression, 2 usage, 3 prompt changed since the baseline,
/// 12 key missing or refused, 13 cancelled.
/// </summary>
internal static partial class PlannerEvalHost
{
    private const ProviderCapability StreamingText = ProviderCapability.Text | ProviderCapability.Streaming;
    private static readonly string[] AllChecks = Enumerable.Range(1, 12).Select(i => $"K-{i}").ToArray();

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
                "--assert-checks" => await RunAssertChecksAsync(),
                "--assert-record-stub" => await RunAssertRecordStubAsync(),
                "--assert-drift" => RunAssertDrift(),
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
        Console.WriteLine("  --assert-set     30 cases, unique ids, follow-ups resolve, every named model is in the Part Library, golden files exist, exit 0");
        Console.WriteLine("  --assert-checks  golden lessons pass their cases; each seed fails exactly its own check; every check seen passing and failing, exit 0");
        Console.WriteLine("  --assert-record-stub  record through the real client against a loopback stub; no key in any file; replay reproduces it; the baseline gate fires, exit 0");
        Console.WriteLine("  --assert-drift   the live client's settings match the App Shell's lesson client, exit 0");
        Console.WriteLine("  --record [dir] [--cases E-01,M-02]  LIVE: plan the cases with DeepSeek (DEEPSEEK_API_KEY) and write a recording; no key -> exit 12");
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

    private static HashSet<string> LibraryIds() => ModelLibrary.V1.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);

    // PE-1: the set is scoreable at all.
    private static int RunAssertSet()
    {
        var failures = 0;
        var set = LoadSet();
        var problems = set.Problems(LibraryIds());
        failures = Check(set.Cases.Count == EvalSet.ExpectedCount, $"{set.Cases.Count} cases in {set.Version}", failures);
        failures = Check(problems.Count == 0, $"no problems in the set{(problems.Count == 0 ? string.Empty : ": " + string.Join("; ", problems))}", failures);
        var bySubject = set.Cases.GroupBy(c => c.Subject).Select(g => $"{g.Key} {g.Count()}");
        failures = Check(set.Cases.GroupBy(c => c.Subject).Count() >= 6, $"subjects: {string.Join(", ", bySubject)}", failures);
        var video = set.Cases.Where(c => c.Id.StartsWith("E-", StringComparison.Ordinal)).ToList();
        failures = Check(video.Count == 6 && video.Count(c => c.Previous is not null) == 3,
            $"{video.Count} cases from the reference video, {video.Count(c => c.Previous is not null)} of them follow-ups", failures);
        var goldens = set.Cases.Where(c => c.Golden is not null).ToList();
        failures = Check(goldens.Count == 4 && goldens.All(c => File.Exists(Here("fixtures", c.Golden!))),
            $"{goldens.Count} cases answered by golden lessons, every file present", failures);
        failures = Check(set.Cases.Count(c => c.Canvas == ExpectedCanvas.ThreeD) >= 3 && set.Cases.Any(c => c.LibraryMiss is not null) && set.Cases.Any(c => c.Canary is not null),
            "3D cases, a library miss and an injection case are all present", failures);

        // The refusals themselves: a set with a duplicate id, a follow-up to a later case and an invented model is never scored.
        var broken = set with
        {
            Cases = set.Cases.Take(29).Append(set.Cases[0]).Select((c, i) => i switch
            {
                1 => c with { Previous = "X-01" },
                20 => c with { Model = "f35b-jet" },
                _ => c,
            }).ToList(),
        };
        var refused = broken.Problems(LibraryIds());
        failures = Check(refused.Count == 3 && refused.Any(p => p.Contains("duplicate", StringComparison.Ordinal))
                         && refused.Any(p => p.Contains("not an earlier case", StringComparison.Ordinal)) && refused.Any(p => p.Contains("not in the Part Library", StringComparison.Ordinal)),
            $"a broken set is refused ({refused.Count} problems: {string.Join("; ", refused)})", failures);
        return Summarize(failures, "EVAL SET OK");
    }

    // PE-2: every check is real. Goldens pass; a seed built to break one check fails that check and no other.
    private static async Task<int> RunAssertChecksAsync()
    {
        var failures = 0;
        var set = LoadSet();
        var seeds = JsonNode.Parse(File.ReadAllText(Here("seeds.json")), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!["seeds"]!.AsArray();
        var lessons = new Dictionary<string, VisualPlan>(StringComparer.Ordinal);
        var seenPass = new HashSet<string>(StringComparer.Ordinal);
        var seenFail = new HashSet<string>(StringComparer.Ordinal);

        var runs = set.Cases.Where(c => c.Golden is not null)
            .Select(c => (Case: c, Label: $"golden {c.Golden}", Expect: "pass", Answer: Golden(c.Golden!)))
            .Concat(seeds.Select(s => (Case: set[s!["case"]!.GetValue<string>()], Label: $"seed {s["note"]!.GetValue<string>()}",
                Expect: s["expect"]!.GetValue<string>(), Answer: SeedAnswer(s))))
            .ToList();

        foreach (var (c, label, expect, answer) in runs)
        {
            var run = await EvalRun.RunOneAsync(c, new FakeLlm(StreamingText, answer, answer), lessons);
            foreach (var (id, passed) in run.Checks)
            {
                (passed ? seenPass : seenFail).Add(id);
            }

            var failed = run.Checks.Where(r => !r.Value).Select(r => r.Key).ToList();
            var ok = run.Error is null && (expect == "pass" ? failed.Count == 0 : failed.SequenceEqual(new[] { expect }));
            var detail = string.Join(", ", run.Details.Where(d => d.StartsWith("fail", StringComparison.Ordinal) || d.StartsWith($"pass {expect} ", StringComparison.Ordinal)).Select(d => d[5..]));
            failures = Check(ok, $"{c.Id} {label}: expect {expect}; failed [{string.Join(", ", failed)}]{(detail.Length == 0 ? string.Empty : $" -- {detail}")}", failures);
        }

        var neverPassed = AllChecks.Where(k => !seenPass.Contains(k)).ToList();
        var neverFailed = AllChecks.Where(k => !seenFail.Contains(k)).ToList();
        failures = Check(neverPassed.Count == 0 && neverFailed.Count == 0,
            $"every check K-1..K-12 seen passing and failing (never passed: {Names(neverPassed)}; never failed: {Names(neverFailed)})", failures);
        return Summarize(failures, "EVAL CHECKS OK");
    }

    private static string Names(List<string> ids) => ids.Count == 0 ? "none" : string.Join(", ", ids);

    // The golden engine lesson names the stand-in model "engine-test"; with the real catalogue it is the
    // shipped four-stroke engine (LP-10 does the same).
    private static string Golden(string name) =>
        File.ReadAllText(Here("fixtures", name)).Replace("\"engine-test\"", $"\"{FourStrokeEngine.Id}\"", StringComparison.Ordinal);

    /// <summary>A seed is an inline plan, or a golden lesson with literal replacements (each must match exactly once).</summary>
    private static string SeedAnswer(JsonNode seed)
    {
        if (seed["answer"] is { } inline)
        {
            return inline.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        var text = Golden(seed["fixture"]!.GetValue<string>());
        foreach (var pair in seed["replace"]?.AsArray() ?? new JsonArray())
        {
            var from = pair![0]!.GetValue<string>();
            var to = pair[1]!.GetValue<string>();
            var at = text.IndexOf(from, StringComparison.Ordinal);
            if (at < 0 || text.IndexOf(from, at + 1, StringComparison.Ordinal) >= 0)
            {
                throw new InvalidDataException($"seed {seed["note"]}: '{from}' must occur exactly once");
            }

            text = text.Replace(from, to, StringComparison.Ordinal);
        }

        return text;
    }
}
