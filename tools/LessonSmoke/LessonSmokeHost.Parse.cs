using HeyGanesh.Abstractions;
using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.Tools.LessonSmoke;

/// <summary>
/// LS-2, the parser row: model output is untrusted text, so every malformed shape must come back as
/// an issue, never an exception, and the common real-model wrapping (prose + a json fence) must not
/// cost the lesson.
/// </summary>
internal static partial class LessonSmokeHost
{
    private const string MinimalPlanWithTeleport =
        "{\"schema\":\"heyganesh.visualplan/1\",\"lessonId\":\"x\",\"title\":\"t\",\"mode\":\"explain\"," +
        "\"surfaces\":[],\"beats\":[{\"id\":\"b1\",\"say\":\"hi\",\"ops\":[{\"op\":\"teleport\"}]}]}";

    private static int RunAssertParse()
    {
        var failures = 0;
        var json = Fixture("chain-rule.json");

        var parsed = VisualPlanParser.Parse(json);
        var plan = parsed.Plan;
        failures = Check(
            plan is not null
                && parsed.Issues.Count == 0
                && plan.Beats.Count == 5
                && plan.Beats[0].Ops[0] is WriteOp { Math: "3x^2 - 1" }
                && plan.Beats.SelectMany(b => b.Ops).OfType<ScreenMarkOp>().Any(m => m.Region == new PixelRect(1240, 860, 96, 32)),
            "chain-rule fixture parses: 5 beats, math carried, screen mark in physical pixels, zero issues",
            failures);

        var fenced = VisualPlanParser.Parse("Here is the lesson:\n```json\n" + json + "\n```");
        failures = Check(
            fenced.Plan is not null && plan is not null && fenced.Issues.Count == 0 && Canon(fenced.Plan) == Canon(plan),
            "prose + json fence around the plan yields the identical plan",
            failures);

        failures = Check(OnlyIssue(VisualPlanParser.Parse("{not json"), LessonIssueCodes.JsonMalformed), "'{not json' -> no plan, json.malformed", failures);
        failures = Check(OnlyIssue(VisualPlanParser.Parse(new string(' ', 65537)), LessonIssueCodes.JsonTooLarge), "65537-byte payload -> no plan, json.too-large", failures);

        var nested = string.Concat(Enumerable.Repeat("{\"a\":", 20)) + "1" + new string('}', 20);
        failures = Check(OnlyIssue(VisualPlanParser.Parse(nested), LessonIssueCodes.JsonMalformed), "20-level nesting (max 16) -> no plan, json.malformed", failures);

        var teleport = VisualPlanParser.Parse(MinimalPlanWithTeleport);
        failures = Check(
            teleport.Plan?.Beats[0].Ops[0] is UnknownOp { Name: "teleport" },
            "unrecognised op parses to UnknownOp (the validator decides its fate)",
            failures);

        var lecture = VisualPlanParser.Parse(json.Replace("\"mode\": \"explain\"", "\"mode\": \"lecture\"", StringComparison.Ordinal));
        failures = Check(
            lecture.Plan is { Mode: LessonMode.Explain } && lecture.Issues.Any(i => i.Code == LessonIssueCodes.ValueInvalid),
            "unknown mode 'lecture' -> defaults to Explain and is reported as value.invalid",
            failures);

        return Summarize(failures, "PARSE OK");
    }

    private static bool OnlyIssue(ParseResult result, string code) =>
        result.Plan is null && result.Issues.Count == 1 && result.Issues[0].Code == code;

    /// <summary>
    /// Value-comparable rendering of a plan. Records compare lists by reference, so two separately
    /// parsed plans are never <c>==</c>; their op records' ToString output is.
    /// </summary>
    private static string Canon(VisualPlan plan) =>
        string.Join("\n",
            new[] { $"{plan.Schema}|{plan.LessonId}|{plan.Title}|{plan.Mode}" }
                .Concat(plan.Surfaces.Select(s => s.ToString()))
                .Concat(plan.Beats.Select(b => $"{b.Id}|{b.Say}|{b.Degraded}|{string.Join(";", b.Ops)}")));
}
