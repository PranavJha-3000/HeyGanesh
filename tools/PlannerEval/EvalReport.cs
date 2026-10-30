using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeyGanesh.Tools.PlannerEval;

/// <summary>
/// What a recording leaves on disk (DEC-044): <c>cases/&lt;id&gt;.json</c> (each <see cref="CaseRun"/>, the
/// input a replay feeds back), <c>summary.json</c> (what the baseline gate compares) and <c>report.txt</c>
/// (what the owner reads). The key appears only as the caller's "present (N chars)" text.
/// </summary>
internal static class EvalReport
{
    /// <summary>The first-beat target the student's wait is measured against (DEC-041/042).</summary>
    public static readonly TimeSpan FirstBeatTarget = TimeSpan.FromSeconds(2.5);

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static JsonObject Summarize(string setVersion, string model, IReadOnlyList<CaseRun> runs)
    {
        var perCheck = new JsonObject();
        foreach (var id in runs.SelectMany(r => r.Checks.Keys).Distinct().OrderBy(k => int.Parse(k[2..])))
        {
            var applicable = runs.Where(r => r.Checks.ContainsKey(id)).ToList();
            perCheck[id] = new JsonObject { ["passed"] = applicable.Count(r => r.Checks[id]), ["of"] = applicable.Count };
        }

        var cases = new JsonObject();
        foreach (var r in runs)
        {
            var checks = new JsonObject();
            foreach (var (k, v) in r.Checks)
            {
                checks[k] = v;
            }

            cases[r.Id] = new JsonObject { ["checks"] = checks, ["error"] = r.Error };
        }

        var firstBeats = runs.Where(r => r.FirstBeatMs is not null).Select(r => r.FirstBeatMs!.Value).ToList();
        return new JsonObject
        {
            ["version"] = setVersion,
            ["model"] = model,
            ["promptHash"] = EvalRun.PromptHash(runs),
            ["runsPerCase"] = 1,
            ["passed"] = runs.Sum(r => r.Checks.Count(c => c.Value)),
            ["applicable"] = runs.Sum(r => r.Checks.Count),
            ["errors"] = runs.Count(r => r.Error is not null),
            ["perCheck"] = perCheck,
            ["firstBeatP50Ms"] = Percentile(firstBeats, 0.5),
            ["firstBeatP90Ms"] = Percentile(firstBeats, 0.9),
            ["latencyP50Ms"] = Percentile(runs.Where(r => r.Error is null).Select(r => r.LatencyMs).ToList(), 0.5),
            ["inputTokens"] = runs.Sum(r => r.InputTokens),
            ["outputTokens"] = runs.Sum(r => r.OutputTokens),
            ["libraryMisses"] = new JsonArray(runs.Where(r => r.LibraryMiss is not null).Select(r => (JsonNode)$"{r.Id}: {r.LibraryMiss}").ToArray()),
            ["cases"] = cases,
        };
    }

    /// <summary>Writes the case files (a recording only), summary.json and report.txt.</summary>
    public static void Write(string dir, JsonObject summary, IReadOnlyList<CaseRun> runs, string keyState, bool writeCases)
    {
        Directory.CreateDirectory(dir);
        if (writeCases)
        {
            var casesDir = Path.Combine(dir, "cases");
            Directory.CreateDirectory(casesDir);
            foreach (var r in runs)
            {
                File.WriteAllText(Path.Combine(casesDir, $"{r.Id}.json"), JsonSerializer.Serialize(r, Json));
            }
        }

        File.WriteAllText(Path.Combine(dir, "summary.json"), summary.ToJsonString(Json));
        File.WriteAllText(Path.Combine(dir, "report.txt"), Text(summary, runs, keyState));
    }

    public static List<CaseRun> LoadCases(string dir) =>
        Directory.GetFiles(Path.Combine(dir, "cases"), "*.json")
            .Select(f => JsonSerializer.Deserialize<CaseRun>(File.ReadAllText(f), Json) ?? throw new InvalidDataException($"{f}: empty"))
            .ToList();

    public static JsonObject LoadSummary(string dir) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "summary.json")))!.AsObject();

    /// <summary>Nearest-rank percentile; null with no samples.</summary>
    public static double? Percentile(IReadOnlyList<double> samples, double p)
    {
        if (samples.Count == 0)
        {
            return null;
        }

        var sorted = samples.Order().ToList();
        return sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];
    }

    public static string Text(JsonObject s, IReadOnlyList<CaseRun> runs, string keyState)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Planner evaluation: {s["version"]}, model {s["model"]}, key {keyState}");
        sb.AppendLine("One run per case; the model's sampling makes a single run noisy. The checks are rules on structure");
        sb.AppendLine("and words (DEC-043): they catch broken, off-topic, ungrounded and manipulated lessons, not subtle errors.");
        sb.AppendLine();
        sb.AppendLine($"checks passed : {s["passed"]} of {s["applicable"]}   cases with a provider error: {s["errors"]}");
        foreach (var (id, node) in s["perCheck"]!.AsObject())
        {
            sb.AppendLine($"  {id,-5} {node!["passed"]}/{node["of"]}");
        }

        sb.AppendLine($"first beat    : p50 {Ms(s["firstBeatP50Ms"])}, p90 {Ms(s["firstBeatP90Ms"])} (target {FirstBeatTarget.TotalSeconds:0.0} s; reported, never gated)");
        sb.AppendLine($"whole lesson  : p50 {Ms(s["latencyP50Ms"])}");
        sb.AppendLine($"tokens        : {Tokens(s)}");
        sb.AppendLine($"prompt hash   : {s["promptHash"]}");
        foreach (var miss in s["libraryMisses"]!.AsArray())
        {
            sb.AppendLine($"library miss  : {miss}");
        }

        sb.AppendLine();
        foreach (var r in runs)
        {
            var failed = r.Checks.Where(c => !c.Value).Select(c => c.Key).ToList();
            sb.AppendLine(r.Error is not null
                ? $"{r.Id}: ERROR {r.Error}"
                : $"{r.Id}: {(failed.Count == 0 ? "all pass" : "failed " + string.Join(", ", failed))}; attempts {r.Attempts}; first beat {Ms(r.FirstBeatMs)}{(r.StreamNote is null ? string.Empty : $"; stream: {r.StreamNote}")}{(r.Partial ? "; partial answer" : string.Empty)}");
            foreach (var d in r.Details.Where(d => d.StartsWith("fail", StringComparison.Ordinal)))
            {
                sb.AppendLine($"    {d}");
            }
        }

        return sb.ToString();
    }

    private static string Ms(JsonNode? n) => n is null ? "n/a" : $"{n.GetValue<double>():F0} ms";

    private static string Ms(double? v) => v is null ? "n/a" : $"{v:F0} ms";

    private static string Tokens(JsonObject s)
    {
        var total = s["inputTokens"]!.GetValue<int>() + s["outputTokens"]!.GetValue<int>();
        return total == 0 ? "not reported by the provider" : $"{s["inputTokens"]} in, {s["outputTokens"]} out";
    }
}
