using HeyGanesh.Abstractions;
using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.Tools.LessonSmoke;

/// <summary>
/// LS-6, the golden-fixture row: each committed lesson validates clean, and two fresh
/// parser/validator passes over the same text agree exactly (outcome, issue sequence, fallback) —
/// the determinism a renderer and a replay log will both depend on (the TU-10 analogue).
/// </summary>
internal static partial class LessonSmokeHost
{
    private static readonly string[] GoldenFixtures = { "chain-rule.json", "tensor-2d.json", "photosynthesis.json" };

    private static int RunAssertFixtures()
    {
        var failures = 0;
        var desktop = new PixelRect(0, 0, 2560, 1440);
        foreach (var name in GoldenFixtures)
        {
            var json = Fixture(name);
            var first = new VisualPlanValidator().Validate(VisualPlanParser.Parse(json), desktop);
            var second = new VisualPlanValidator().Validate(VisualPlanParser.Parse(json), desktop);

            failures = Check(first.Outcome == LessonOutcome.Valid && first.Issues.Count == 0, $"{name} -> Valid, zero issues", failures);
            failures = Check(
                first.Outcome == second.Outcome
                    && first.Issues.SequenceEqual(second.Issues)
                    && first.FallbackText == second.FallbackText
                    && (first.Plan is null) == (second.Plan is null)
                    && (first.Plan is null || Canon(first.Plan) == Canon(second.Plan!)),
                $"{name} -> two fresh passes agree exactly",
                failures);
        }

        return Summarize(failures, "FIXTURES OK");
    }
}
