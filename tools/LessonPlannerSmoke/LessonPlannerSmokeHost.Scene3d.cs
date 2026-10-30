using System.Text.RegularExpressions;
using HeyGanesh.LessonPlanner;
using HeyGanesh.PartLibrary;
using HeyGanesh.PartLibrary.Models;
using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.Tools.LessonPlannerSmoke;

/// <summary>
/// LP-10: the planner offers 3D exactly when the caller passes a catalogue (DEC-032). Every name in the
/// 3D section is checked against the code it came from, and every op shape it shows is fed back
/// through the real parser, so the prompt cannot drift from what the validator accepts.
/// </summary>
internal static partial class LessonPlannerSmokeHost
{
    private static async Task<int> RunAssertPrompt3dAsync()
    {
        var failures = 0;
        var catalog = ModelLibrary.ToCatalog();
        var engine = Fixture("engine-3d.json").Replace("\"engine-test\"", $"\"{FourStrokeEngine.Id}\"", StringComparison.Ordinal);

        var flatClient = new FakeLlmClient(TextOnly, Fixture("chain-rule.json"));
        await new VisualPlanner(flatClient).PlanAsync(Request("why is the derivative of ln(3x^2-1) equal to 6x/(3x^2-1)?"));
        var flat = flatClient.Recorded[0].SystemPrompt ?? string.Empty;
        string[] words3d = { VisualPlanValidator.Schema11, "scene3d", "\"op\": \"model\"", "\"op\": \"explode\"", "\"op\": \"animate\"", "\"op\": \"camera\"", "CATALOGUE" };
        failures = Check(words3d.All(w => !flat.Contains(w, StringComparison.Ordinal)) && flat.Contains($"\"schema\": \"{VisualPlanValidator.SupportedSchema}\"", StringComparison.Ordinal),
            "no catalogue -> the v1 prompt: schema 1, no 3D words at all", failures);

        var client = new FakeLlmClient(TextOnly, engine);
        var planned = await new VisualPlanner(client).PlanAsync(Request3d());
        var system = client.Recorded[0].SystemPrompt ?? string.Empty;
        failures = Check(system.Contains($"\"schema\": \"{VisualPlanValidator.Schema11}\"", StringComparison.Ordinal) && system.StartsWith(flat[..flat.IndexOf("FORMAT", StringComparison.Ordinal)], StringComparison.Ordinal),
            "catalogue -> the same prompt, asking for schema 1.1", failures);
        string[] ops3d = { "model", "label", "explode", "animate", "camera" };
        failures = Check(ops3d.All(o => system.Contains($"{{ \"op\": \"{o}\"", StringComparison.Ordinal)) && system.Contains("{ \"op\": \"highlight\", \"target\": \"engine.piston\" }", StringComparison.Ordinal),
            "the shape of every 3D op is shown, and a highlight on \"<instance>.<part>\"", failures);
        var missing = catalog.Models.SelectMany(m => new[] { m.Id }.Concat(m.Parts).Concat(m.Animations)).Where(n => !system.Contains(n, StringComparison.Ordinal)).ToList();
        failures = Check(missing.Count == 0, $"every catalogue model, part and animation id is listed ({catalog.Models.Count} models; missing: {(missing.Count == 0 ? "none" : string.Join(", ", missing))})", failures);
        var views = Enum.GetNames<CameraView>().Select(v => $"\"{v.ToLowerInvariant()}\"").ToList();
        var limits = LessonLimits.Default;
        failures = Check(views.All(v => system.Contains(v, StringComparison.Ordinal))
                && system.Contains($"at most {limits.MaxScenes} surface of kind \"scene3d\"", StringComparison.Ordinal)
                && system.Contains($"at most {limits.MaxModelsPerScene} per scene", StringComparison.Ordinal),
            $"camera views ({string.Join(" ", views)}) and the 3D limits come from the code", failures);

        // Every op line the prompt shows, with its "a | b" alternatives narrowed to the first, parses as
        // that op through the real parser — a shape the parser would not take cannot be offered.
        var section = system[Math.Max(0, system.IndexOf("3D ops (", StringComparison.Ordinal))..];
        var shown = Regex.Matches(section, @"^\s*(\{ ""op"": ""(model|label|highlight|explode|animate|camera)"".*?\})(?=\s*(\(|$))", RegexOptions.Multiline)
            .Select(m => Regex.Replace(m.Groups[1].Value, @"(""[a-z]+"")( \| ""[a-z]+"")+", "$1")).ToList();
        var bad = shown.Where(line =>
        {
            var json = $$"""{ "schema": "{{VisualPlanValidator.Schema11}}", "lessonId": "x", "title": "x", "mode": "explain", "surfaces": [], "beats": [ { "id": "b1", "say": "x", "ops": [ {{line}} ] } ] }""";
            var parsed = VisualPlanParser.Parse(json);
            return parsed.Plan is null || parsed.Issues.Count > 0 || parsed.Plan.Beats[0].Ops[0] is UnknownOp;
        }).ToList();
        failures = Check(shown.Count == 6 && bad.Count == 0, $"all {shown.Count} 3D op lines in the prompt parse as their op ({(bad.Count == 0 ? "none refused" : string.Join(" / ", bad))})", failures);

        failures = Check(planned.Lesson.Outcome == LessonOutcome.Valid && planned.Attempts == 1 && planned.Lesson.Plan!.Surfaces.Any(s => s.Kind == SurfaceKind.Scene3d),
            $"the engine answer with the catalogue -> Valid on the first call, scene kept ({planned.Lesson.Outcome}, {planned.Attempts})", failures);
        var refused = await new VisualPlanner(new FakeLlmClient(TextOnly, engine, engine)).PlanAsync(Request("how does a four-stroke engine work?"));
        failures = Check(refused.Lesson.Outcome != LessonOutcome.Valid && refused.FirstAttemptIssues.Any(i => i.Code == LessonIssueCodes.SurfaceUnsupported),
            $"the same answer with NO catalogue is refused as {LessonIssueCodes.SurfaceUnsupported} ({refused.Lesson.Outcome})", failures);

        // An invented part: the repair turn names the issue, and the corrected answer is taken.
        var invented = engine.Replace("\"engine.connecting-rod\"", "\"engine.flux-capacitor\"", StringComparison.Ordinal);
        var repairClient = new FakeLlmClient(TextOnly, invented, engine);
        var repaired = await new VisualPlanner(repairClient).PlanAsync(Request3d());
        var repairText = repairClient.Recorded.Count == 2 ? UserText(repairClient.Recorded[1]) : string.Empty;
        failures = Check(invented != engine && repaired.Attempts == 2 && repaired.Lesson.Outcome == LessonOutcome.Valid
                && repaired.FirstAttemptIssues.Any(i => i.Code == LessonIssueCodes.PartUnknown) && repairText.Contains(LessonIssueCodes.PartUnknown, StringComparison.Ordinal),
            $"an invented part -> {LessonIssueCodes.PartUnknown} in the repair turn; the corrected answer is Valid", failures);

        Console.WriteLine($"          system prompt: {flat.Length} chars without 3D, {system.Length} with the {catalog.Models.Count}-model catalogue (+{system.Length - flat.Length})");
        failures = Check(system.Length - flat.Length is > 0 and < 6000, "the 3D section stays small (under 6000 characters)", failures);
        return Summarize(failures, "PROMPT 3D OK");
    }

    private static PlanRequest Request3d() => Request("how does a four-stroke engine work?") with { Scene3d = ModelLibrary.ToCatalog() };
}
