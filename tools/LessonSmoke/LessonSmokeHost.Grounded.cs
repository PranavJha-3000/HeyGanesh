using HeyGanesh.Abstractions;
using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.Tools.LessonSmoke;

/// <summary>
/// LS-12, the grounded-mark row: a model-planned lesson may only mark what grounding actually found
/// on the student's screen. With a target box, marks must sit inside it (plus a small margin); with
/// none, every mark is refused — a model can never invent coordinates and circle the wrong thing.
/// </summary>
internal static partial class LessonSmokeHost
{
    private static int RunAssertGrounded()
    {
        var failures = 0;
        var desktop = new PixelRect(0, 0, 2560, 1440);
        var target = new PixelRect(1200, 840, 160, 60);
        var basePlan = VisualPlanParser.Parse(Fixture("chain-rule.json")).Plan!;
        LessonValidationResult WithMark(PixelRect region, PixelRect? box, bool require = true) =>
            new VisualPlanValidator().Validate(
                new ParseResult(WithBeat(basePlan, 1, b => b with { Ops = b.Ops.Select(o => o is ScreenMarkOp m ? m with { Region = region } : o).ToList() }), Array.Empty<LessonIssue>()),
                new ValidationContext(desktop) { RequireGroundedMarks = require, GroundedTarget = box });

        failures = Check(WithMark(new PixelRect(1240, 860, 96, 32), target).Outcome == LessonOutcome.Valid, "mark inside the grounded box -> Valid", failures);
        failures = Check(WithMark(new PixelRect(1180, 820, 200, 100), target).Outcome == LessonOutcome.Valid, "mark within the 24 px margin around the box -> Valid", failures);
        failures = Check(Has(WithMark(new PixelRect(100, 100, 50, 50), target), LessonIssueCodes.MarkUngrounded, "b2"), "mark far from the grounded box -> mark.ungrounded on its beat", failures);
        failures = Check(Has(WithMark(new PixelRect(1240, 860, 96, 32), null), LessonIssueCodes.MarkUngrounded, "b2"), "grounding required but no box -> every mark is mark.ungrounded", failures);
        failures = Check(WithMark(new PixelRect(100, 100, 50, 50), null, require: false).Outcome == LessonOutcome.Valid, "grounding not required -> marks anywhere on the desktop, as before", failures);
        failures = Check(Validate(basePlan).Outcome == LessonOutcome.Valid, "the (ParseResult, PixelRect) overload is unchanged: fixture with a mark still Valid", failures);
        return Summarize(failures, "GROUNDED OK");
    }
}
