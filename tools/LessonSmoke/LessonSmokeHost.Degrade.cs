using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.Tools.LessonSmoke;

/// <summary>
/// LS-5, the degradation row: a bad beat keeps its sentence and loses its drawing; a plan that is
/// mostly bad becomes plain spoken text; only a plan with nothing to say is rejected. The student
/// always hears something unless there is genuinely nothing.
/// </summary>
internal static partial class LessonSmokeHost
{
    private static int RunAssertDegrade()
    {
        var failures = 0;
        var basePlan = VisualPlanParser.Parse(Fixture("chain-rule.json")).Plan!;
        var allSays = string.Join(" ", basePlan.Beats.Select(b => b.Say));
        VisualPlan Break(VisualPlan p, int index) => AppendOp(p, index, new UnknownOp("teleport"));

        var badHighlight = WithBeat(basePlan, 1, b => b with { Ops = b.Ops.Select(o => o is HighlightOp ? new HighlightOp("nope") : o).ToList() });
        var partial = Validate(badHighlight);
        var p = partial.Plan;
        failures = Check(
            partial.Outcome == LessonOutcome.PartiallyDegraded
                && p is not null
                && p.Beats[1].Degraded && p.Beats[1].Ops.Count == 0 && p.Beats[1].Say == basePlan.Beats[1].Say
                && p.Beats.Where((_, i) => i != 1).All(b => !b.Degraded)
                && p.Beats.Where((_, i) => i != 1).Select(b => b.Ops.Count).SequenceEqual(basePlan.Beats.Where((_, i) => i != 1).Select(b => b.Ops.Count)),
            "one bad highlight -> PartiallyDegraded: that beat keeps its say, loses its ops; others intact",
            failures);

        var cascade = Validate(Break(basePlan, 0));
        failures = Check(
            cascade.Outcome == LessonOutcome.TextFallback
                && Has(cascade, LessonIssueCodes.RefTarget, "b2") && Has(cascade, LessonIssueCodes.RefTarget, "b3"),
            "a degraded beat's elements never exist: b2/b3 pointing at b1's w1 degrade too (3/5 -> TextFallback)",
            failures);

        var three = Validate(Break(Break(Break(basePlan, 1), 3), 4));
        failures = Check(
            three.Outcome == LessonOutcome.TextFallback && three.Plan is null && three.FallbackText == allSays,
            "3 of 5 beats broken -> TextFallback, no plan, all five says joined",
            failures);

        var all = Validate(Enumerable.Range(0, 5).Aggregate(basePlan, Break));
        failures = Check(all.Outcome == LessonOutcome.TextFallback && all.FallbackText == allSays, "every beat broken -> TextFallback with all five says", failures);

        var silent = Validate(basePlan with { Beats = basePlan.Beats.Select(b => b with { Say = "" }).ToList() });
        failures = Check(silent.Outcome == LessonOutcome.Rejected && silent.FallbackText == string.Empty, "every say empty -> Rejected, empty fallback", failures);

        var malformed = new VisualPlanValidator().Validate(VisualPlanParser.Parse("{not json"), Desktop1440);
        failures = Check(
            malformed.Outcome == LessonOutcome.Rejected && Has(malformed, LessonIssueCodes.JsonMalformed),
            "unparseable text -> Rejected, carrying json.malformed",
            failures);

        // Final review I-5: the say limit bounds what is SPOKEN too — an over-long say is not speech.
        var monologue = Validate(WithBeat(basePlan, 4, b => b with { Say = new string('a', 5000) }));
        failures = Check(
            monologue.Outcome == LessonOutcome.PartiallyDegraded
                && monologue.Plan!.Beats.Count == 4
                && monologue.Plan.Beats.All(b => b.Say.Length <= 400)
                && !monologue.FallbackText.Contains(new string('a', 401), StringComparison.Ordinal),
            "5000-char say -> that beat is dropped, never spoken; fallback excludes it",
            failures);

        return Summarize(failures, "DEGRADE OK");
    }
}
