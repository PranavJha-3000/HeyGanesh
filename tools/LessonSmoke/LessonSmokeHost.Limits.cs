using HeyGanesh.Abstractions;
using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.Tools.LessonSmoke;

/// <summary>
/// LS-3, the limits row: every v1 bound and closed vocabulary, each exercised by mutating one field
/// of the golden chain-rule plan so a failure names exactly one rule.
/// </summary>
internal static partial class LessonSmokeHost
{
    private static readonly PixelRect Desktop1440 = new(0, 0, 2560, 1440);

    private static int RunAssertLimits()
    {
        var failures = 0;
        var basePlan = VisualPlanParser.Parse(Fixture("chain-rule.json")).Plan!;

        var ok = Validate(basePlan);
        failures = Check(ok.Outcome == LessonOutcome.Valid && ok.Issues.Count == 0, "unmodified chain-rule plan -> Valid, zero issues", failures);

        var v2 = Validate(basePlan with { Schema = "heyganesh.visualplan/2" });
        failures = Check(Has(v2, LessonIssueCodes.SchemaUnsupported) && v2.Outcome == LessonOutcome.TextFallback, "schema visualplan/2 -> schema.unsupported, TextFallback", failures);

        var many = Validate(basePlan with { Beats = Enumerable.Range(1, 41).Select(i => new LessonBeat($"x{i}", "say", Array.Empty<LessonOp>())).ToList() });
        failures = Check(Has(many, LessonIssueCodes.LimitBeats) && many.Outcome == LessonOutcome.TextFallback, "41 beats (max 40) -> limit.beats, TextFallback", failures);

        var surfaces = Validate(basePlan with { Surfaces = Enumerable.Range(1, 5).Select(i => new LessonSurface($"s{i}", SurfaceKind.Card, "t")).Prepend(basePlan.Surfaces[0]).Take(5).ToList() });
        failures = Check(Has(surfaces, LessonIssueCodes.LimitSurfaces), "5 surfaces (max 4) -> limit.surfaces", failures);

        var crowded = new List<LessonOp> { basePlan.Beats[0].Ops[0] };
        crowded.AddRange(Enumerable.Repeat<LessonOp>(new HighlightOp("w1"), 12));
        failures = Check(Has(Validate(WithBeat(basePlan, 0, b => b with { Ops = crowded })), LessonIssueCodes.LimitOps, "b1"), "13 ops in a beat (max 12) -> limit.ops on that beat", failures);

        failures = Check(Has(Validate(WithBeat(basePlan, 0, b => b with { Say = new string('a', 401) })), LessonIssueCodes.FieldTooLong, "b1"), "401-char say (max 400) -> field.too-long", failures);
        failures = Check(Has(Validate(WithBeat(basePlan, 0, b => b with { Say = "" })), LessonIssueCodes.FieldMissing, "b1"), "empty say with valid ops -> field.missing (speech is mandatory)", failures);

        var longText = new WriteOp("card-main", "w1", new string('t', 281), TextStyle.Heading, null);
        failures = Check(Has(Validate(WithBeat(basePlan, 0, b => b with { Ops = new LessonOp[] { longText } })), LessonIssueCodes.FieldTooLong, "b1"), "281-char write text (max 280) -> field.too-long", failures);

        failures = Check(Has(Validate(WithBeat(basePlan, 0, b => b with { Id = "B 1" })), LessonIssueCodes.IdInvalid), "beat id 'B 1' -> id.invalid", failures);
        failures = Check(Has(Validate(WithBeat(basePlan, 1, b => b with { Id = "b1" })), LessonIssueCodes.IdDuplicate), "two beats 'b1' -> id.duplicate", failures);

        var badElementId = new WriteOp("card-main", "W1", "inside", TextStyle.Heading, null);
        failures = Check(Has(Validate(WithBeat(basePlan, 0, b => b with { Ops = new LessonOp[] { badElementId } })), LessonIssueCodes.IdInvalid, "b1"), "element id 'W1' -> id.invalid", failures);

        failures = Check(Has(Validate(AppendOp(basePlan, 0, new StepOp("card-main", "s1", 0, "x"))), LessonIssueCodes.ValueInvalid, "b1"), "step index 0 (1-based) -> value.invalid", failures);
        failures = Check(Has(Validate(AppendOp(basePlan, 0, new PlotOp("card-main", "p9", "x", 2, 2))), LessonIssueCodes.ValueInvalid, "b1"), "plot xMin == xMax -> value.invalid", failures);
        failures = Check(Has(Validate(AppendOp(basePlan, 0, new IconOp("card-main", "i1", "rocket"))), LessonIssueCodes.GlyphUnknown, "b1"), "glyph 'rocket' -> glyph.unknown", failures);
        failures = Check(Has(Validate(AppendOp(basePlan, 0, new UnknownOp("teleport"))), LessonIssueCodes.OpUnknown, "b1"), "UnknownOp -> op.unknown", failures);

        return Summarize(failures, "LIMITS OK");
    }

    private static LessonValidationResult Validate(VisualPlan plan, PixelRect? desktop = null) =>
        new VisualPlanValidator().Validate(new ParseResult(plan, Array.Empty<LessonIssue>()), desktop ?? Desktop1440);

    private static bool Has(LessonValidationResult result, string code, string? beatId = null) =>
        result.Issues.Any(i => i.Code == code && (beatId is null || i.BeatId == beatId));

    private static VisualPlan WithBeat(VisualPlan plan, int index, Func<LessonBeat, LessonBeat> change) =>
        plan with { Beats = plan.Beats.Select((b, i) => i == index ? change(b) : b).ToList() };

    private static VisualPlan AppendOp(VisualPlan plan, int index, LessonOp op) =>
        WithBeat(plan, index, b => b with { Ops = b.Ops.Append(op).ToList() });
}
