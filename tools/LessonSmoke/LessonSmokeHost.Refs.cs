using HeyGanesh.Abstractions;
using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.Tools.LessonSmoke;

/// <summary>
/// LS-4, the reference row: every arrow, highlight and op must point at something that exists at
/// that beat (declared surface, element already written and not cleared), and screen marks must land
/// on the virtual desktop. The desktop here has a monitor LEFT of primary, so valid marks can sit at
/// negative x — physical pixels, PerMonitorV2, exactly as Screen Capture reports them.
/// </summary>
internal static partial class LessonSmokeHost
{
    private static string MarkPlan(string markFields) =>
        "{\"schema\":\"heyganesh.visualplan/1\",\"lessonId\":\"m\",\"title\":\"t\",\"mode\":\"explain\"," +
        "\"surfaces\":[],\"beats\":[{\"id\":\"b1\",\"say\":\"look here\",\"ops\":[{\"op\":\"mark\",\"shape\":\"circle\"," + markFields + "}]}]}";

    private static bool MarkIssue(string markFields, string code)
    {
        var parsed = VisualPlanParser.Parse(MarkPlan(markFields));
        return new VisualPlanValidator().Validate(parsed, DesktopLeftMonitor).Issues.Any(i => i.Code == code && i.BeatId == "b1");
    }

    private static readonly PixelRect DesktopLeftMonitor = new(-1920, 0, 3840, 1080);

    private static int RunAssertRefs()
    {
        var failures = 0;
        var tensor = VisualPlanParser.Parse(Fixture("tensor-2d.json")).Plan!;
        LessonValidationResult V(VisualPlan p) => Validate(p, DesktopLeftMonitor);

        var ok = V(tensor);
        failures = Check(ok.Outcome == LessonOutcome.Valid && ok.Issues.Count == 0, "tensor-2d fixture -> Valid, zero issues", failures);

        failures = Check(
            Has(V(AppendOp(tensor, 0, new WriteOp("card-x", "w-new", "x", TextStyle.Body, null))), LessonIssueCodes.RefSurface, "b1"),
            "op on undeclared surface 'card-x' -> ref.surface",
            failures);
        failures = Check(
            Has(V(AppendOp(tensor, 0, new ArrowOp("card-tensor", "w-grid", "w-cell", null))), LessonIssueCodes.RefTarget, "b1"),
            "arrow to an element first written in a LATER beat -> ref.target",
            failures);
        failures = Check(
            Has(V(AppendOp(tensor, 0, new HighlightOp("nothing"))), LessonIssueCodes.RefTarget, "b1"),
            "highlight of an element that never exists -> ref.target",
            failures);

        var cleared = AppendOp(AppendOp(tensor, 2, new ClearOp("card-tensor")), 2, new HighlightOp("w-grid"));
        failures = Check(Has(V(cleared), LessonIssueCodes.RefTarget, "b3"), "highlight of an element wiped by a clear -> ref.target", failures);

        failures = Check(
            Has(V(AppendOp(tensor, 1, new WriteOp("card-tensor", "w-grid", "again", TextStyle.Body, null))), LessonIssueCodes.IdDuplicate, "b2"),
            "element id reused across beats -> id.duplicate",
            failures);

        var left = V(AppendOp(tensor, 0, new ScreenMarkOp(MarkShape.Circle, new PixelRect(-1800, 100, 50, 50))));
        failures = Check(left.Outcome == LessonOutcome.Valid && !Has(left, LessonIssueCodes.MarkOutOfBounds), "mark at x=-1800 on the left monitor -> valid", failures);
        failures = Check(
            Has(V(AppendOp(tensor, 0, new ScreenMarkOp(MarkShape.Circle, new PixelRect(1900, 100, 50, 50)))), LessonIssueCodes.MarkOutOfBounds, "b1"),
            "mark spilling past the right edge (1950 > 1920) -> mark.out-of-bounds",
            failures);
        failures = Check(
            Has(V(AppendOp(tensor, 0, new ScreenMarkOp(MarkShape.Underline, new PixelRect(10, 10, 0, 20)))), LessonIssueCodes.ValueInvalid, "b1"),
            "mark width 0 -> value.invalid",
            failures);

        // Final review I-2/I-3: mark coordinates are required integers and the bounds check cannot wrap.
        failures = Check(MarkIssue("\"y\": 5, \"width\": 10, \"height\": 10", LessonIssueCodes.ValueInvalid), "mark with no x -> value.invalid (never coerced to 0)", failures);
        failures = Check(MarkIssue("\"x\": \"300\", \"y\": 5, \"width\": 10, \"height\": 10", LessonIssueCodes.ValueInvalid), "mark x as a string -> value.invalid", failures);
        failures = Check(MarkIssue("\"x\": 99999999999, \"y\": 5, \"width\": 10, \"height\": 10", LessonIssueCodes.ValueInvalid), "mark x beyond int range -> value.invalid", failures);
        var integral = Validate(VisualPlanParser.Parse(MarkPlan("\"x\": 512.0, \"y\": 5, \"width\": 10, \"height\": 10")).Plan!, DesktopLeftMonitor);
        failures = Check(
            integral.Outcome == LessonOutcome.Valid && integral.Plan!.Beats[0].Ops.OfType<ScreenMarkOp>().Single().Region == new PixelRect(512, 5, 10, 10),
            "mark x 512.0 (integral number) -> accepted as 512",
            failures);
        failures = Check(
            Has(V(AppendOp(tensor, 0, new ScreenMarkOp(MarkShape.Circle, new PixelRect(2000000000, 2000000000, 2000000000, 2000000000)))), LessonIssueCodes.MarkOutOfBounds, "b1"),
            "mark whose X+Width overflows int -> mark.out-of-bounds (no wrap)",
            failures);

        // Final review M-7 (re-graded): an arrow lives on one card, so both ends must be on that card.
        var twoCards = tensor with { Surfaces = tensor.Surfaces.Append(new LessonSurface("card-two", SurfaceKind.Card, "two")).ToList() };
        failures = Check(
            Has(V(AppendOp(twoCards, 1, new ArrowOp("card-two", "w-row", "w-col", null))), LessonIssueCodes.RefTarget, "b2"),
            "arrow on card-two between elements on card-tensor -> ref.target",
            failures);

        return Summarize(failures, "REFS OK");
    }
}
