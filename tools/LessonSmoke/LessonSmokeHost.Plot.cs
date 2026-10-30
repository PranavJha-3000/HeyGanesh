using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.Tools.LessonSmoke;

/// <summary>
/// LS-11, the plot row: the app — never the model, never the page — turns a plot expression into
/// points, so a drawn curve is mathematically right by construction. The parser is a closed grammar
/// (no names beyond x, pi, e and eight functions), so an expression can never be code.
/// </summary>
internal static partial class LessonSmokeHost
{
    private static int RunAssertPlot()
    {
        var failures = 0;
        bool At(string text, double x, double expected) =>
            PlotExpression.TryParse(text, out var e, out _) && Math.Abs(e!.Evaluate(x) - expected) < 1e-9;
        bool Refused(string text) => !PlotExpression.TryParse(text, out var e, out var error) && e is null && !string.IsNullOrEmpty(error);

        failures = Check(At("6*x/(3*x^2-1)", 1, 3), "6*x/(3*x^2-1) at x=1 -> 3 (the chain-rule fixture's curve)", failures);
        failures = Check(At("3x^2", 2, 12), "implicit multiplication: 3x^2 at x=2 -> 12", failures);
        failures = Check(At("2(x+1)", 1, 4) && At("2sin(x)", Math.PI / 2, 2), "implicit multiplication before ( and a function", failures);
        failures = Check(At("-2^2", 0, -4), "unary minus binds looser than ^: -2^2 -> -4", failures);
        failures = Check(At("2^3^2", 0, 512), "^ is right-associative: 2^3^2 -> 512", failures);
        failures = Check(At("sin(pi/2)", 0, 1) && At("ln(e)", 0, 1) && At("log(100)", 0, 2) && At("sqrt(abs(-16))", 0, 4) && At("exp(0)+cos(0)+tan(0)", 0, 2),
            "constants pi/e and functions sin cos tan exp ln log sqrt abs", failures);
        failures = Check(Refused("x+") && Refused("x**2") && Refused("import os") && Refused("foo(x)") && Refused("") && Refused("x)"),
            "x+, x**2, import os, foo(x), empty, unbalanced -> refused with an error", failures);
        failures = Check(Refused(new string('(', 200) + "x" + new string(')', 200)), "200-deep parentheses (max 64) -> refused, no stack overflow", failures);
        failures = Check(Refused(new string('1', 281)), "281-char expression (max 280) -> refused", failures);

        var samples = PlotExpression.Sample(new PlotOp("c", "p", "1/x", -1, 1));
        failures = Check(
            samples.Count == 121 && samples[60] is null && samples[0] is { X: -1, Y: -1 } && samples[120] is { X: 1, Y: 1 },
            "1/x on [-1, 1] -> 121 samples, a null gap at x=0, ends at (-1,-1) and (1,1)",
            failures);

        // Final review I-2: 2e3 must never quietly mean 2·e·3 — exponent notation is refused, plain e stays e.
        failures = Check(Refused("2e3") && Refused("1e-3x") && Refused("5E2"), "scientific notation (2e3, 1e-3x, 5E2) -> refused, never misread", failures);
        failures = Check(At("2e", 0, 2 * Math.E) && At("2e^x", 1, 2 * Math.E) && At("3e x", 1, 3 * Math.E), "a number then the constant e still works: 2e, 2e^x, 3e x", failures);

        // Final review I-4: a near-asymptote sample (tan at -pi/2 ~ 1.6e16) is finite but must not flatten the plot.
        var tan = PlotExpression.Sample(new PlotOp("c", "p", "tan(x)", -Math.PI, Math.PI));
        var finite = tan.Where(p => p is not null).Select(p => Math.Abs(p!.Y)).ToList();
        failures = Check(
            tan.Count(p => p is null) >= 2 && finite.Max() < 100 && finite.Count > 100,
            $"tan(x) on [-pi, pi] -> outliers become gaps near the asymptotes (max |y| {finite.Max():F1} < 100)",
            failures);
        var exp = PlotExpression.Sample(new PlotOp("c", "p", "exp(x)", 0, 10));
        failures = Check(exp.All(p => p is not null), "exp(x) on [0, 10] -> steep but real growth keeps every sample", failures);

        var basePlan = VisualPlanParser.Parse(Fixture("chain-rule.json")).Plan!;
        var badExpr = WithBeat(basePlan, 4, b => b with { Ops = new LessonOp[] { new PlotOp("card-main", "p1", "x+", 1, 4) } });
        failures = Check(Has(Validate(badExpr), LessonIssueCodes.ValueInvalid, "b5"), "validator: plot expr 'x+' -> value.invalid on its beat", failures);

        var desktop = new HeyGanesh.Abstractions.PixelRect(0, 0, 2560, 1440);
        failures = Check(
            GoldenFixtures.All(f => new VisualPlanValidator().Validate(VisualPlanParser.Parse(Fixture(f)), desktop).Outcome == LessonOutcome.Valid),
            "golden fixtures still Valid with expression checking on",
            failures);

        return Summarize(failures, "PLOT OK");
    }
}
