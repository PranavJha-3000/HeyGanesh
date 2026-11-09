using System.Text.Json.Nodes;
using HeyGanesh.Abstractions;
using HeyGanesh.LessonPlanner;
using FakeLlm = HeyGanesh.Tools.LessonPlannerSmoke.FakeLlmClient;

namespace HeyGanesh.Tools.PlannerEval;

/// <summary>
/// FEATURE-011 step 6 (DEC-073): drawing programs scored. PE-9 proves the static checks and the run-check rules
/// offline (CI); PE-11 proves the run checks on the real, hidden stage (local). Programs go through the real
/// DrawingPlanner over a recording fake model, as a live recording's do.
/// </summary>
internal static partial class PlannerEvalHost
{
    private static readonly string[] StaticChecks = { "K-2", "K-3", "K-4", "K-8", "K-9", "K-10", "K-11" };
    private static readonly string[] RunChecks = { "P-1", "P-2", "P-3", "P-4", "P-5" };

    /// <summary>
    /// Hand-written programs that answer their case well, in the set's order (E-02 follows up E-01). They are written
    /// for a 2560 × 1440 canvas, the eval desktop.
    /// </summary>
    private static readonly (string Case, string Program)[] GoodPrograms =
    {
        ("E-01", Lf("""
            step(1); say("The model can't read letters, so every character of the text is turned into a number first.")
            card(1300, 200, 1100, 900, {title: 'Text becomes numbers'})
            text(1850, 340, 'torch.tensor', {size: 26})
            step(2); say("Each character gets its own integer, like a code book, and torch.tensor stores that long list of numbers.")
            rect(1400, 420, 900, 120)
            step(3); say("Then the model learns to predict the next number from the ones before it, and with more text and a bigger model its guesses get better.")
            arrow([[1450, 700], [2200, 700]], {color: 'red'})
            """)),
        ("E-02", Lf("""
            keep()
            step(1); say("Predicting the next character over and over is all it learns, and that is enough to write new text.")
            arrow([[1450, 800], [2200, 800]], {color: 'teal'})
            step(2); say("Scale that up with far more data and a bigger network, and the same guessing starts to look like understanding.")
            step(3); say("So the torch.tensor of numbers is just the start: the model is a giant next-number guesser.")
            """)),
        ("E-03", Lf("""
            step(1); say("No, it is not magic: the jet points its engine thrust straight down, so the push up equals its weight.")
            card(1300, 200, 1000, 900, {title: 'Hovering'})
            arrow([[1800, 500], [1800, 900]], {color: 'red'})
            text(1900, 700, 'thrust', {size: 26})
            step(2); say("Gravity pulls the plane down, and the downward blast of air pushes it up just as hard.")
            arrow([[1700, 900], [1700, 600]], {color: 'teal'})
            step(3); say("It burns fuel very fast to do this, so it can only hover for a short time.")
            """)),
        ("M-01", Lf("""
            step(1); say("This is the chain rule: the inside part is 3x squared minus 1.")
            ellipse(1280, 870, 110, 45, {color: 'red'})
            card(1500, 150, 950, 650, {title: 'Chain rule'})
            step(2); say("The outside is ln, whose derivative is one over the inside.")
            step(3); say("Then you multiply by the inside's derivative, 6x, so you get 6x over 3x squared minus 1.")
            arrow([[1500, 500], [1300, 840]], {color: 'teal'})
            """)),
        ("B-02", Lf("""
            step(1); say("Let me build a 3D model of DNA for you: two twisting strands.")
            card(1300, 150, 1100, 1000, {title: 'DNA'})
            const f = figure(1330, 220, 1040, 900, {tilt: 15})
            for (let i = 0; i < 10; i++) f.cyl({at: [0, i * 0.4 - 2, 0], r: 0.05, h: 1.6, axis: 'x', rot: [0, i * 36, 0], color: 'teal'})
            step(2); say("Each rung is a pair of bases joined in the middle.")
            step(3); say("A always pairs with T, and C always pairs with G, so each strand copies the other.")
            """)),
        ("X-01", Lf("""
            step(1); say("The slide says force equals mass times acceleration.")
            card(1300, 200, 1000, 800, {title: "Newton's second law"})
            text(1800, 400, 'F = m a', {size: 28})
            step(2); say("A bigger force on the same mass gives a bigger acceleration.")
            arrow([[1500, 600], [1900, 600]], {color: 'red'})
            step(3); say("Twice the mass with the same force gives half the acceleration.")
            """)),
    };

    /// <summary>A good program with every occurrence of From replaced by To: it must fail exactly Expect.</summary>
    private static readonly (string Case, string From, string To, string Expect)[] ProgramSeeds =
    {
        ("E-01", "its guesses get better.\")", "its guesses get better and better, until the numbers it predicts turn back into characters that read like real sentences written by a person who knows the topic quite well, which is the whole trick.\")", "K-2"),
        ("X-01", "acceleration", "speed-up", "K-3"),
        ("E-03", "It burns fuel very fast to do this, so it can only hover for a short time.", "An anti-gravity field holds it up for a short time.", "K-4"),
        ("E-01", "torch.tensor", "a list", "K-8"),
        ("X-01", "'F = m a'", "'BANANA'", "K-9"),
        ("E-03", "No, it is not magic", "Well, it is not magic", "K-10"),
    };

    // PE-9: every static check is real on programs, and the run-check rules are right on stage runs made by hand.
    private static async Task<int> RunAssertProgramChecksAsync()
    {
        var failures = 0;
        var set = LoadSet();
        var seenPass = new HashSet<string>(StringComparer.Ordinal);
        var seenFail = new HashSet<string>(StringComparer.Ordinal);
        void Seen(CaseRun run)
        {
            foreach (var (id, passed) in run.Checks)
            {
                (passed ? seenPass : seenFail).Add(id);
            }
        }

        // The static reading of a program: quotes, escapes, templates, two calls on one line.
        const string sample = "step(1); say('it\\'s one') ; say(\"two \\\"quoted\\\"\")\nsay(`three ${x}`)\ntext(1, 2, 'label')\n// say('in a comment')";
        var said = ProgramChecks.SayLines(sample);
        failures = Check(said.SequenceEqual(new[] { "it's one", "two \"quoted\"", "three ${x}", "in a comment" }) && ProgramChecks.Strings(sample).Contains("label"),
            $"say() lines and strings are read from the text, quotes and escapes undone ({string.Join(" | ", said)})", failures);

        // Every good program, through the real DrawingPlanner, passes every static check its case asks for.
        var drawings = new Dictionary<string, DrawingFollowUp>(StringComparer.Ordinal);
        foreach (var (id, program) in GoodPrograms)
        {
            var run = await EvalRun.RunDrawingAsync(set[id], Fenced(program), drawings, null);
            Seen(run);
            var failed = run.Checks.Where(k => !k.Value).Select(k => k.Key).ToList();
            // The block's text is handed over with its last newline, as upstream's ProgramIn does.
            failures = Check(run.Outcome == "Program" && run.Program == program + "\n" && failed.Count == 0 && run.Checks.Keys.All(k => k.StartsWith("K-", StringComparison.Ordinal)),
                $"{id} good program: {run.Checks.Count} static checks ({string.Join(", ", run.Checks.Keys)}), failed [{string.Join(", ", failed)}]", failures);
        }

        // Each seed breaks exactly its own check.
        foreach (var (id, from, to, expect) in ProgramSeeds)
        {
            var good = GoodPrograms.Single(g => g.Case == id).Program;
            if (!good.Contains(from, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"program seed {expect}: '{from}' is not in {id}'s program");
            }

            var run = await EvalRun.RunDrawingAsync(set[id], Fenced(good.Replace(from, to, StringComparison.Ordinal)), new Dictionary<string, DrawingFollowUp>(drawings), null);
            Seen(run);
            var failed = run.Checks.Where(k => !k.Value).Select(k => k.Key).ToList();
            failures = Check(failed.SequenceEqual(new[] { expect }),
                $"{id} seed for {expect}: failed [{string.Join(", ", failed)}] -- {string.Join("; ", run.Details.Where(d => d.StartsWith("fail", StringComparison.Ordinal)))}", failures);
        }

        // K-11: a follow-up that repeats the drawing it was asked about, word for word.
        var repeat = await EvalRun.RunDrawingAsync(set["E-02"], Fenced(GoodPrograms[0].Program), new Dictionary<string, DrawingFollowUp>(drawings), null);
        Seen(repeat);
        var repeatFailed = repeat.Checks.Where(k => !k.Value).Select(k => k.Key).ToList();
        failures = Check(repeatFailed.SequenceEqual(new[] { "K-11" }), $"E-02 answered with E-01's own drawing fails K-11 alone ({string.Join("; ", repeat.Details.Where(d => d.Contains("K-11", StringComparison.Ordinal)))})", failures);

        // NONE is no drawing: nothing reaches a stage, and K-2 says so.
        var none = await EvalRun.RunDrawingAsync(set["U-01"], new FakeLlm(StreamingText, "NONE"), drawings, null);
        failures = Check(none.Outcome == "None" && none.Program == string.Empty && !none.Checks["K-2"] && !none.Checks.Keys.Any(k => k.StartsWith("P-", StringComparison.Ordinal))
                && none.Details.Any(d => d.Contains("None: nothing drawn or said", StringComparison.Ordinal)),
            $"a NONE reply is no drawing: K-2 fails with the outcome named, and no run check is reported ({string.Join("; ", none.Details.Take(1))})", failures);

        // The run-check rules on stage runs made by hand (PE-11 proves the real stage feeds them).
        var dot = new JsonObject { ["do"] = "dot", ["at"] = new JsonArray(1, 1), ["r"] = 4 };
        StageRun Made(bool ran = true, string[]? errors = null, string[]? skipped = null, params JsonObject[] marks) =>
            new(ran, errors ?? Array.Empty<string>(), skipped ?? Array.Empty<string>(), new[] { "This is the chain rule: the inside part.", "Then multiply.", "That is it." },
                new JsonArray(marks.Select(m => (JsonNode)m.DeepClone()).ToArray()));
        var target = set["M-01"].Screen!.TargetBox!.Value;
        var arrowIn = new JsonObject { ["do"] = "arrow", ["pts"] = new JsonArray(new JsonArray(1500, 500), new JsonArray(target.X + 10, target.Y + 10)) };
        var figure = new JsonObject { ["do"] = "figure", ["at"] = new JsonArray(10, 10), ["w"] = 400, ["h"] = 300 };
        JsonObject Part() => new() { ["do"] = "part3d", ["kind"] = "box" };
        var runs = new (string Label, EvalCase Case, StageRun Run, string? Expect)[]
        {
            ("a clean 2D run pointing at the box", set["M-01"], Made(marks: new[] { dot, arrowIn }), null),
            ("a statement error", set["M-01"], Made(errors: new[] { "ReferenceError: nope is not defined" }, marks: new[] { arrowIn }), "P-1"),
            ("a program that never finished", set["M-01"], Made(ran: false, marks: new[] { arrowIn }), "P-1"),
            ("a refused mark", set["M-01"], Made(skipped: new[] { "its position is missing or far off the canvas" }, marks: new[] { arrowIn }), "P-2"),
            ("a 3D figure in a 2D case", set["M-01"], Made(marks: new[] { arrowIn, figure }), "P-3"),
            ("nothing pointing at the box", set["M-01"], Made(marks: new[] { dot }), "P-4"),
            ("a 3D case with a figure of 50 parts", set["B-02"], Made(marks: new[] { figure }.Concat(Enumerable.Range(0, 50).Select(_ => Part())).ToArray()), null),
            ("no figure in a 3D case", set["B-02"], Made(marks: new[] { dot }), "P-3"),
            ("51 parts", set["B-02"], Made(marks: new[] { figure }.Concat(Enumerable.Range(0, 51).Select(_ => Part())).ToArray()), "P-5"),
        };
        foreach (var (label, c, stageRun, expect) in runs)
        {
            var checks = ProgramChecks.Score(c, DrawingOutcome.Program, "step(1); say('x')", null, null, stageRun).Where(r => r.Id.StartsWith("P-", StringComparison.Ordinal)).ToList();
            foreach (var r in checks)
            {
                (r.Passed ? seenPass : seenFail).Add(r.Id);
            }

            var failed = checks.Where(r => !r.Passed).Select(r => r.Id).ToList();
            failures = Check(expect is null ? failed.Count == 0 : failed.SequenceEqual(new[] { expect }),
                $"{c.Id} {label}: expect {expect ?? "pass"}; failed [{string.Join(", ", failed)}] -- {string.Join("; ", checks.Where(r => !r.Passed || r.Id == (expect ?? "P-4")).Select(r => $"{r.Id} {r.Detail}"))}", failures);
        }

        // What counts as pointing at the box: an arrow ending in it, a ring round it; not a card over it, not a mark elsewhere.
        var ring = new JsonObject { ["do"] = "ellipse", ["at"] = new JsonArray(target.X + target.Width / 2, target.Y + target.Height / 2), ["rx"] = 110, ["ry"] = 45 };
        var card = new JsonObject { ["do"] = "rect", ["at"] = new JsonArray(target.X - 400, target.Y - 400), ["w"] = target.Width + 800, ["h"] = target.Height + 800 };
        var far = new JsonObject { ["do"] = "arrow", ["pts"] = new JsonArray(new JsonArray(0, 0), new JsonArray(100, 100)) };
        var verdicts = new[] { arrowIn, ring, card, far }.Select(m => ProgramChecks.PointsAt(m, target)).ToArray();
        failures = Check(verdicts.SequenceEqual(new[] { true, true, false, false }),
            $"P-4 counts an arrow ending in the box and a ring round it, not a big shape over it or a mark elsewhere ({string.Join(", ", verdicts)})", failures);

        // The drawing recording path, offline: the good programs recorded as --record --drawing does (no stage here),
        // replayed with nothing changed; a run check the stage gave at recording time survives the offline replay.
        var root = Path.Combine(Path.GetTempPath(), "heyganesh-pe9-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var rec = Path.Combine(root, "rec");
            var replies = GoodPrograms.Select(g => "```js\n" + g.Program + "\n```").ToArray();
            var code = await RecordAsync(set, GoodPrograms.Select(g => set[g.Case]).ToList(), new FakeLlm(StreamingText, replies), rec, "not used (fake model)", CancellationToken.None);
            var recorded = EvalReport.LoadCases(rec);
            var same = await ReplayAsync(set, rec, rec);
            failures = Check(code == 0 && recorded.Count == GoodPrograms.Length && recorded.All(r => r.Drawing && r.Outcome == "Program" && r.Checks.Values.All(v => v))
                    && same.Differences.Count == 0 && same.Regressions.Count == 0 && !same.PromptChanged && same.NotReplayed.Count == 0,
                $"{recorded.Count} drawings recorded and replayed with no difference ({same.Differences.Count} differences, {same.Regressions.Count} regressions, prompt {(same.PromptChanged ? "changed" : "same")})", failures);

            var m01 = Path.Combine(rec, "cases", "M-01.json");
            var stageSaid = System.Text.Json.JsonSerializer.Deserialize<CaseRun>(File.ReadAllText(m01), EvalReport.Json)!;
            stageSaid.Checks["P-4"] = false;
            File.WriteAllText(m01, System.Text.Json.JsonSerializer.Serialize(stageSaid, EvalReport.Json));
            var kept = await ReplayAsync(set, rec, rec);
            var replayed = kept.Runs.Single(r => r.Id == "M-01");
            failures = Check(replayed.Checks.TryGetValue("P-4", out var p4) && !p4 && kept.Differences.Count == 0,
                $"a run check recorded on the stage (M-01 P-4 failed) is kept by the offline replay ({Fmt(replayed.Checks)})", failures);
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

        var all = StaticChecks.Concat(RunChecks).ToList();
        var neverPassed = all.Where(k => !seenPass.Contains(k)).ToList();
        var neverFailed = all.Where(k => !seenFail.Contains(k)).ToList();
        failures = Check(neverPassed.Count == 0 && neverFailed.Count == 0,
            $"every program check ({string.Join(", ", all)}) seen passing and failing (never passed: {Names(neverPassed)}; never failed: {Names(neverFailed)})", failures);
        return Summarize(failures, "PROGRAM CHECKS OK");
    }

    // PE-11 (local): the run checks on the real stage, hidden. The good programs run cleanly; each bad one fails its check.
    private static async Task<int> RunAssertProgramRunAsync()
    {
        var failures = 0;
        var set = LoadSet();
        using var stage = await ProgramStage.OpenAsync();
        var drawings = new Dictionary<string, DrawingFollowUp>(StringComparer.Ordinal);
        foreach (var (id, program) in GoodPrograms)
        {
            var run = await EvalRun.RunDrawingAsync(set[id], Fenced(program), drawings, stage);
            var failed = run.Checks.Where(k => !k.Value).Select(k => k.Key).ToList();
            var ran = run.Checks.Keys.Where(k => k.StartsWith("P-", StringComparison.Ordinal)).ToList();
            failures = Check(failed.Count == 0 && ran.Contains("P-1") && ran.Contains("P-2") && ran.Contains("P-5"),
                $"{id} good program on the stage: [{string.Join(", ", ran)}] all pass; failed [{string.Join(", ", failed)}] -- {string.Join("; ", run.Details.Where(d => d.StartsWith("fail", StringComparison.Ordinal) || d.Contains(" P-", StringComparison.Ordinal)).Take(5))}", failures);
        }

        // The stage's own lines are the static ones for a program without loops or templates.
        var lines = (await stage.RunAsync(GoodPrograms[0].Program, EvalRun.Desktop.Width, EvalRun.Desktop.Height)).Lines;
        failures = Check(lines.SequenceEqual(ProgramChecks.SayLines(GoodPrograms[0].Program)), $"the stage says the lines the text has ({lines.Count})", failures);

        var x01 = GoodPrograms.Single(g => g.Case == "X-01").Program;
        var m01 = GoodPrograms.Single(g => g.Case == "M-01").Program;
        var b02 = GoodPrograms.Single(g => g.Case == "B-02").Program;
        var bad = new (string Label, string Case, string Program, string Expect)[]
        {
            ("a statement that throws", "X-01", x01 + "\nnoSuchFunction(1)", "P-1"),
            ("a mark far off the canvas", "X-01", x01 + "\ndot(1e9, 10, 5)", "P-2"),
            ("a 3D figure in a 2D case", "X-01", x01 + "\nconst g = figure(100, 100, 400, 300)\ng.box({size: [1, 1, 1]})", "P-3"),
            ("nothing pointing at the box", "M-01", m01.Replace("ellipse(1280, 870, 110, 45, {color: 'red'})\n", string.Empty, StringComparison.Ordinal).Replace("[1300, 840]", "[1700, 700]", StringComparison.Ordinal), "P-4"),
            ("60 parts", "B-02", b02.Replace("i < 10", "i < 60", StringComparison.Ordinal), "P-5"),
        };
        foreach (var (label, id, program, expect) in bad)
        {
            var run = await EvalRun.RunDrawingAsync(set[id], Fenced(program), new Dictionary<string, DrawingFollowUp>(drawings), stage);
            var failed = run.Checks.Where(k => !k.Value).Select(k => k.Key).ToList();
            failures = Check(failed.SequenceEqual(new[] { expect }),
                $"{id} {label}: failed [{string.Join(", ", failed)}] -- {string.Join("; ", run.Details.Where(d => d.StartsWith("fail", StringComparison.Ordinal)))}", failures);
        }

        return Summarize(failures, "PROGRAM RUN OK");
    }

    /// <summary>A model reply that is the program in a js block, as the drawing prompt asks for.</summary>
    private static FakeLlm Fenced(string program) => new(StreamingText, "```js\n" + program + "\n```");

    // This file is CRLF; a model's reply is LF.
    private static string Lf(string program) => program.ReplaceLineEndings("\n").Trim();
}
