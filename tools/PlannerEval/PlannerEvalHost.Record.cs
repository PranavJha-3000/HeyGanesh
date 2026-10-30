using System.Text.Json;
using System.Text.Json.Nodes;
using HeyGanesh.Providers.Core;
using HeyGanesh.Security;
using HeyGanesh.VisualLessonEngine;
using FakeLlm = HeyGanesh.Tools.LessonPlannerSmoke.FakeLlmClient;

namespace HeyGanesh.Tools.PlannerEval;

/// <summary>
/// Step 7b (DEC-044): record live, replay offline, gate on a baseline. <c>--record</c> is the only mode that
/// reaches a real model, and only with <c>DEEPSEEK_API_KEY</c> set; everything else here is offline. PE-4
/// drives the same record and replay code through the real client against a loopback stub.
/// </summary>
internal static partial class PlannerEvalHost
{
    /// <summary>
    /// PE-4's ring: an empty store and an environment that knows only a dummy key, so the row never reads or
    /// writes the machine's Credential Manager or DEEPSEEK_API_KEY.
    /// </summary>
    private static KeyRing DummyRing(string dummy) =>
        new(new EmptyStore(), v => v == "DEEPSEEK_API_KEY" ? dummy : null);

    private sealed record ReplayOutcome(List<CaseRun> Runs, List<string> NotReplayed, List<string> Differences, List<string> Regressions, bool PromptChanged, JsonObject Summary);

    // PE-6 (live, owner): every case, or the listed ones plus the cases they follow up, through DeepSeek.
    private static async Task<int> RunRecordAsync(string[] args)
    {
        string? dir = null;
        string[]? only = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--cases" && i + 1 < args.Length)
            {
                only = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            else if (!args[i].StartsWith("--", StringComparison.Ordinal) && dir is null)
            {
                dir = args[i];
            }
            else
            {
                return Usage(args[i]);
            }
        }

        // The key comes from the app's key ring (DEC-046) and is described by presence, length and source only,
        // checked before anything is built: with no key no client exists and no connection is ever opened.
        using var ring = new KeyRing();
        var state = ring.Describe(LiveClient.KeyName);
        var key = state.ToString();
        Console.WriteLine($"{LiveClient.KeyName} key: {key}");
        if (state.Source == KeySource.Missing)
        {
            Console.WriteLine("KEY MISSING: store it with HeyGanesh.App --set-key deepseek, or set DEEPSEEK_API_KEY; nothing was sent (exit 12)");
            return 12;
        }

        var set = LoadSet();
        var cases = Select(set, only, out var unknown);
        if (unknown.Count > 0)
        {
            Console.WriteLine($"unknown case id(s): {string.Join(", ", unknown)} (exit 2)");
            return 2;
        }

        dir ??= DefaultDir();
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler stop = (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += stop;
        try
        {
            using var client = LiveClient.Create(ring);
            Console.WriteLine($"recording {cases.Count} case(s) with {LiveClient.ModelId} into {dir}");
            return await RecordAsync(set, cases, client, dir, key, cts.Token);
        }
        finally
        {
            Console.CancelKeyPress -= stop;
        }
    }

    // PE-7: re-score a recording offline against a baseline (by default the recording's own summary).
    private static async Task<int> RunReplayAsync(string[] args)
    {
        string? dir = null;
        string? baseline = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--baseline" && i + 1 < args.Length)
            {
                baseline = args[++i];
            }
            else if (!args[i].StartsWith("--", StringComparison.Ordinal) && dir is null)
            {
                dir = args[i];
            }
            else
            {
                return Usage(args[i]);
            }
        }

        baseline ??= dir;
        if (dir is null || !Directory.Exists(Path.Combine(dir, "cases")) || !File.Exists(Path.Combine(baseline!, "summary.json")))
        {
            Console.WriteLine("--replay needs a recording directory (with cases/) and a baseline with summary.json (exit 2)");
            return 2;
        }

        var outcome = await ReplayAsync(LoadSet(), dir, baseline!);
        Console.Write(EvalReport.Text(outcome.Summary, outcome.Runs, "not used (offline replay)"));
        Console.WriteLine();
        foreach (var line in outcome.NotReplayed)
        {
            Console.WriteLine($"not replayed: {line}");
        }

        foreach (var line in outcome.Differences)
        {
            Console.WriteLine($"DIFFERS from the recording: {line}");
        }

        foreach (var line in outcome.Regressions)
        {
            Console.WriteLine($"REGRESSION: {line}");
        }

        if (outcome.PromptChanged)
        {
            Console.WriteLine("PROMPT CHANGED since the baseline: frozen answers say nothing about the new prompt; re-record with --record and review (exit 3)");
            return 3;
        }

        Console.WriteLine(outcome.Regressions.Count == 0 ? "REPLAY OK: nothing that passed in the baseline fails now (exit 0)" : $"FAILED: {outcome.Regressions.Count} regression(s) (exit 1)");
        return outcome.Regressions.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// Plans the cases in order over <paramref name="client"/> and writes the recording. An authentication
    /// failure or cancellation stops the run, and what was planned so far is still written.
    /// </summary>
    private static async Task<int> RecordAsync(EvalSet set, IReadOnlyList<EvalCase> cases, ILlmClient client, string dir, string keyState, CancellationToken ct)
    {
        var runs = new List<CaseRun>();
        var lessons = new Dictionary<string, VisualPlan>(StringComparer.Ordinal);
        var code = 0;
        try
        {
            foreach (var c in cases)
            {
                var run = await EvalRun.RunOneAsync(c, client, lessons, ct);
                runs.Add(run);
                var failed = run.Checks.Where(k => !k.Value).Select(k => k.Key).ToList();
                Console.WriteLine($"  {c.Id}: {(run.Error is not null ? $"ERROR {run.Error}" : failed.Count == 0 ? "all pass" : "failed " + string.Join(", ", failed))}");
            }
        }
        catch (ProviderException ex) when (ex.Kind == ProviderErrorKind.AuthFailed)
        {
            Console.WriteLine("KEY ERROR: the provider refused the key (AuthFailed); the run stopped (exit 12)");
            code = 12;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("CANCELLED: the run stopped; the cases planned so far are written (exit 13)");
            code = 13;
        }

        var summary = EvalReport.Summarize(set.Version, client.ModelId, runs);
        EvalReport.Write(dir, summary, runs, keyState, writeCases: true);
        Console.WriteLine($"checks passed {summary["passed"]} of {summary["applicable"]}; written to {dir}");
        return code;
    }

    /// <summary>
    /// Feeds every recorded answer back through the current planner and checks, in set order. A case with a
    /// provider error or a stream cut short keeps its recorded result (its answer text cannot reproduce it).
    /// Regressions are checks that passed in the baseline and fail now, or a lower total.
    /// </summary>
    private static async Task<ReplayOutcome> ReplayAsync(EvalSet set, string dir, string baselineDir)
    {
        var recorded = EvalReport.LoadCases(dir).ToDictionary(r => r.Id, StringComparer.Ordinal);
        var lessons = new Dictionary<string, VisualPlan>(StringComparer.Ordinal);
        var runs = new List<CaseRun>();
        var notReplayed = recorded.Keys.Where(id => set.Cases.All(c => c.Id != id)).Select(id => $"{id}: not in {set.Version}").ToList();
        var differences = new List<string>();
        foreach (var c in set.Cases.Where(c => recorded.ContainsKey(c.Id)))
        {
            var rec = recorded[c.Id];
            if (rec.Error is not null || rec.Partial || rec.Answers.Count == 0)
            {
                notReplayed.Add($"{c.Id}: {(rec.Error is not null ? $"provider error {rec.Error}" : "the live stream was cut short")}; its recorded result is kept");
                runs.Add(rec);
                continue;
            }

            var run = await EvalRun.RunOneAsync(c, new FakeLlm(StreamingText, rec.Answers.ToArray()), lessons);
            run = run with { FirstBeatMs = rec.FirstBeatMs, LatencyMs = rec.LatencyMs, InputTokens = rec.InputTokens, OutputTokens = rec.OutputTokens };
            if (!rec.Checks.OrderBy(k => k.Key).SequenceEqual(run.Checks.OrderBy(k => k.Key)))
            {
                differences.Add($"{c.Id}: recorded [{Fmt(rec.Checks)}], replayed [{Fmt(run.Checks)}]");
            }

            runs.Add(run);
        }

        var summary = EvalReport.Summarize(set.Version, $"replay of {Path.GetFileName(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar))}", runs);
        var baseline = EvalReport.LoadSummary(baselineDir);
        var regressions = new List<string>();
        foreach (var (id, node) in baseline["cases"]!.AsObject())
        {
            var now = runs.FirstOrDefault(r => r.Id == id);
            foreach (var (check, passed) in node!["checks"]!.AsObject())
            {
                if (passed!.GetValue<bool>() && (now is null || !now.Checks.TryGetValue(check, out var p) || !p))
                {
                    regressions.Add($"{id} {check} passed in the baseline, {(now is null ? "the case is missing" : "fails now")}");
                }
            }
        }

        var before = baseline["passed"]!.GetValue<int>();
        var after = summary["passed"]!.GetValue<int>();
        if (after < before)
        {
            regressions.Add($"{after} checks pass, the baseline had {before}");
        }

        var promptChanged = baseline["promptHash"]!.GetValue<string>() != summary["promptHash"]!.GetValue<string>();
        return new ReplayOutcome(runs, notReplayed, differences, regressions, promptChanged, summary);
    }

    // PE-4: the whole recording path offline, through the real OpenAI-compatible client.
    private static async Task<int> RunAssertRecordStubAsync()
    {
        var failures = 0;
        var set = LoadSet();
        var answers = set.Cases.Where(c => c.Golden is not null).ToDictionary(c => c.Id, c => Golden(c.Golden!), StringComparer.Ordinal);
        foreach (var s in LoadSeeds().Where(s => s["expect"]!.GetValue<string>() == "pass"))
        {
            answers.TryAdd(s["case"]!.GetValue<string>(), SeedAnswer(s));
        }

        var cases = set.Cases.Where(c => answers.ContainsKey(c.Id)).ToList();
        var dummy = "pe4-dummy-" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(Path.GetTempPath(), "heyganesh-pe4-" + Guid.NewGuid().ToString("N")[..8]);
        var rec = Path.Combine(root, "rec");
        using var dummyRing = DummyRing(dummy);
        try
        {
            using var stub = StubServer.Start();
            foreach (var c in cases)
            {
                stub.Enqueue(answers[c.Id]);
            }

            int code;
            using (var client = LiveClient.Build(stub.Prefix, dummyRing))
            {
                code = await RecordAsync(set, cases, client, rec, dummyRing.Describe(LiveClient.KeyName).ToString(), CancellationToken.None);
            }

            var runs = EvalReport.LoadCases(rec);
            failures = Check(code == 0 && runs.Count == cases.Count && runs.All(r => r.Error is null && r.Checks.Count > 0 && r.Checks.Values.All(v => v)),
                $"{runs.Count} of {cases.Count} cases recorded through the real client, every check passing (exit {code})", failures);
            failures = Check(stub.Requests.Count == cases.Count && stub.Requests.All(r => r.Path == "/chat/completions" && r.Authorization == $"Bearer {dummy}"),
                $"one streamed request per case to /chat/completions, each carrying the key ({stub.Requests.Count} requests)", failures);
            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            failures = Check(File.Exists(Path.Combine(rec, "summary.json")) && File.ReadAllText(Path.Combine(rec, "report.txt")).Contains("present (", StringComparison.Ordinal),
                $"summary.json and report.txt written; the report names the key by presence and length only ({files.Length} files)", failures);
            failures = Check(files.All(f => !File.ReadAllText(f).Contains(dummy, StringComparison.Ordinal)), "the key is in no written file", failures);

            var same = await ReplayAsync(set, rec, rec);
            failures = Check(same.Differences.Count == 0 && same.Regressions.Count == 0 && !same.PromptChanged && same.NotReplayed.Count == 0,
                $"replay reproduces every recorded result ({same.Differences.Count} differences, {same.Regressions.Count} regressions, prompt {(same.PromptChanged ? "changed" : "same")})", failures);

            // The gate fires: a recorded answer that stops saying "shape" loses K-3 on S-01.
            var worse = Path.Combine(root, "worse");
            CopyDir(rec, worse);
            var s01 = Path.Combine(worse, "cases", "S-01.json");
            var run = JsonSerializer.Deserialize<CaseRun>(File.ReadAllText(s01), EvalReport.Json)!;
            File.WriteAllText(s01, JsonSerializer.Serialize(run with { Answers = run.Answers.Select(a => a.Replace("shape", "form", StringComparison.Ordinal)).ToList() }, EvalReport.Json));
            var regressed = await ReplayAsync(set, worse, rec);
            failures = Check(regressed.Regressions.Count == 2 && regressed.Regressions[0].StartsWith("S-01 K-3 passed in the baseline", StringComparison.Ordinal),
                $"a worse answer is a regression against the baseline ({string.Join("; ", regressed.Regressions)})", failures);

            // The prompt pin fires: a baseline recorded under another prompt.
            var other = Path.Combine(root, "other-prompt");
            CopyDir(rec, other);
            var summary = EvalReport.LoadSummary(other);
            summary["promptHash"] = new string('0', 64);
            File.WriteAllText(Path.Combine(other, "summary.json"), summary.ToJsonString(EvalReport.Json));
            var moved = await ReplayAsync(set, rec, other);
            failures = Check(moved.PromptChanged && moved.Regressions.Count == 0, "a baseline recorded under a different prompt is reported as a prompt change", failures);

            // The command line's verdicts: same 0, regression 1, prompt changed 3, no recording 2.
            var exits = new[]
            {
                await RunReplayAsync(new[] { rec }),
                await RunReplayAsync(new[] { worse, "--baseline", rec }),
                await RunReplayAsync(new[] { rec, "--baseline", other }),
                await RunReplayAsync(new[] { Path.Combine(root, "missing") }),
            };
            failures = Check(exits.SequenceEqual(new[] { 0, 1, 3, 2 }), $"--replay exits same 0, regression 1, prompt changed 3, no recording 2 ({string.Join(", ", exits)})", failures);

            var recorded = EvalReport.LoadSummary(rec);
            failures = Check(recorded["firstBeatP50Ms"] is not null && recorded["firstBeatP90Ms"] is not null && recorded["promptHash"]!.GetValue<string>().Length == 64,
                $"first beat p50 {recorded["firstBeatP50Ms"]?.GetValue<double>():F0} ms / p90 {recorded["firstBeatP90Ms"]?.GetValue<double>():F0} ms and the prompt hash are recorded", failures);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // A file still held open by a scanner; the temp folder is the OS's to clean.
            }
        }

        return Summarize(failures, "RECORD STUB OK");
    }

    // PE-5: the live client cannot drift from the App Shell's lesson client.
    private static int RunAssertDrift()
    {
        var failures = 0;
        var root = RepoRoot();
        if (root is null)
        {
            return Summarize(Check(false, "repository root (HeyGanesh.sln) found above the host", failures), "DRIFT OK");
        }

        var shell = File.ReadAllText(Path.Combine(root, "src", "Experience", "HeyGanesh.App", "ShellHost.Lesson.cs"));
        var caps = string.Join(" | ", Enum.GetValues<ProviderCapability>()
            .Where(f => (int)f != 0 && ((int)f & ((int)f - 1)) == 0 && LiveClient.Capabilities.HasFlag(f))
            .Select(f => $"ProviderCapability.{f}"));
        failures = Check(shell.Contains($"BaseUrl = \"{LiveClient.BaseUrl}\"", StringComparison.Ordinal) && LiveClient.BaseUrl.StartsWith("https://", StringComparison.Ordinal),
            $"base URL {LiveClient.BaseUrl} is the shell's, over https", failures);
        failures = Check(shell.Contains($"ModelId = \"{LiveClient.ModelId}\"", StringComparison.Ordinal), $"model {LiveClient.ModelId} is the shell's", failures);
        failures = Check(shell.Contains($"Capabilities = {caps},", StringComparison.Ordinal), $"capabilities {caps} are the shell's", failures);
        failures = Check(shell.Contains($"new KeyRingSecretStore(Keys, \"{LiveClient.KeyName}\")", StringComparison.Ordinal),
            $"the key is the shell's key ring entry '{LiveClient.KeyName}'", failures);

        // Only PE-4 may aim the client elsewhere, and it does so with its own dummy key variable.
        var builds = Directory.GetFiles(Path.Combine(root, "tools", "PlannerEval"), "*.cs")
            .Where(f => Path.GetFileName(f) != "LiveClient.cs")
            .Sum(f => CountOf(File.ReadAllText(f), "LiveClient." + "Build(")); // split so this line does not count itself
        failures = Check(builds == 1, $"LiveClient.Build is called once outside LiveClient (PE-4's stub), never from argument handling ({builds})", failures);
        return Summarize(failures, "DRIFT OK");
    }

    private static List<EvalCase> Select(EvalSet set, string[]? only, out List<string> unknown)
    {
        unknown = new List<string>();
        if (only is null)
        {
            return set.Cases.ToList();
        }

        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in only)
        {
            var c = set.Cases.FirstOrDefault(x => x.Id == id);
            if (c is null)
            {
                unknown.Add(id);
                continue;
            }

            // A follow-up is only scored fairly with the lesson it follows, so that case is recorded too.
            for (var cur = c; cur is not null; cur = cur.Previous is { } p ? set.Cases.FirstOrDefault(x => x.Id == p) : null)
            {
                wanted.Add(cur.Id);
            }
        }

        return set.Cases.Where(c => wanted.Contains(c.Id)).ToList();
    }

    private static string DefaultDir()
    {
        var data = Environment.GetEnvironmentVariable("HEYGANESH_DATA_DIR");
        var baseDir = string.IsNullOrWhiteSpace(data)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeyGanesh")
            : data;
        return Path.Combine(baseDir, "eval", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
    }

    private static List<JsonNode> LoadSeeds() =>
        JsonNode.Parse(File.ReadAllText(Here("seeds.json")), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!["seeds"]!
            .AsArray().Select(s => s!).ToList();

    private static string? RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HeyGanesh.sln")))
            {
                return dir.FullName;
            }
        }

        return null;
    }

    private static void CopyDir(string from, string to)
    {
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static int CountOf(string text, string needle)
    {
        var n = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + 1, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }

    /// <summary>A store with nothing in it: PE-4's ring falls through to its dummy environment.</summary>
    private sealed class EmptyStore : ISecretStore
    {
        public SecretHandle? Get(string providerId, string keyAlias) => null;

        public void Set(string providerId, string keyAlias, ReadOnlyMemory<byte> secret) => throw new NotSupportedException("PE-4 stores nothing");

        public bool Exists(string providerId, string keyAlias) => false;

        public void Delete(string providerId, string keyAlias)
        {
        }

        public void Dispose()
        {
        }
    }

    private static string Fmt(Dictionary<string, bool> checks) =>
        string.Join(" ", checks.OrderBy(k => int.Parse(k.Key[2..])).Select(k => $"{k.Key}{(k.Value ? "+" : "-")}"));
}
