using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.Tools.LessonSmoke;

/// <summary>
/// LS-14, the schematic contract (schema 1.2, DEC-048): a lesson may define one simplified model for an
/// object the catalogue lacks. Every structural rule fires alone, a refused schematic is dropped and costs
/// only the beats that use it, and the geometry check is the caller's delegate (here a fake; the Part
/// Library's real one is PL-6).
/// </summary>
internal static partial class LessonSmokeHost
{
    private static async Task<int> RunAssertSchematicAsync()
    {
        var failures = 0;
        var json = Fixture("f35b-schematic.json");
        var plan = VisualPlanParser.Parse(json).Plan!;
        var ctx = new ValidationContext(Desktop1440) { Scene3d = TestCatalog, SchematicGeometry = _ => Array.Empty<string>() };
        LessonValidationResult Run(VisualPlan p, ValidationContext? c = null) =>
            new VisualPlanValidator().Validate(new ParseResult(p, Array.Empty<LessonIssue>()), c ?? ctx);

        // 1. Parsing: every field read as written.
        var jet = plan.Schematics.Single();
        var nozzle = jet.Parts.Single(p => p.Id == "nozzle").Shapes.Single();
        failures = Check(jet.Id == "f35b" && jet.Parts.Count == 7 && jet.Parts.Sum(p => p.Shapes.Count) == 10
                         && nozzle is { Shape: "cylinder", Finish: "brass" } && nozzle.Size.SequenceEqual(new[] { 0.32, 0.4, 0.26 })
                         && nozzle.At.SequenceEqual(new[] { -1.7, -0.15, 0 }) && nozzle.Turn.SequenceEqual(new[] { 0d, 0, 0 }),
            "f35b-schematic.json parses: 1 schematic, 7 parts, 10 shapes, shape/size/at/turn/finish as written", failures);

        // 2. The golden lesson: valid, and the schematic is placed, labelled, highlighted, exploded and framed.
        var golden = Run(plan);
        failures = Check(golden.Outcome == LessonOutcome.Valid && golden.Issues.Count == 0 && golden.Plan!.Schematics.Single().Id == "f35b",
            $"with a catalogue -> Valid, zero issues, the schematic kept in the validated plan ({golden.Outcome}, {golden.Issues.Count} issues{string.Concat(golden.Issues.Take(3).Select(i => $"; {i.Code} {i.BeatId}: {i.Message}"))})", failures);
        var unchecked3d = Run(plan, ctx with { SchematicGeometry = null });
        failures = Check(Has(unchecked3d, LessonIssueCodes.SchematicUnavailable) && (unchecked3d.Plan is null || unchecked3d.Plan.Schematics.Count == 0),
            "no geometry check (a canvas that does not draw schematics) -> schematic.unavailable, never accepted (DEC-049)", failures);

        // 3. A refused schematic costs only the beats that use it: here 1 of 3 beats, so the lesson still plays.
        var oneUse = plan with { Schema = VisualPlanValidator.Schema11, Beats = new[] { plan.Beats[0], CardBeat("b7"), CardBeat("b8") } };
        var cost = Run(oneUse);
        failures = Check(cost.Outcome == LessonOutcome.PartiallyDegraded && Has(cost, LessonIssueCodes.SchematicUnavailable) && Has(cost, LessonIssueCodes.ModelUnknown, "b1")
                         && cost.Plan!.Schematics.Count == 0 && cost.Plan.Beats.Count(b => b.Degraded) == 1,
            $"under schema 1.1 -> schematic.unavailable; only b1 degrades, the lesson plays without it ({cost.Outcome})", failures);

        // 4. Each rule alone refuses the schematic (and nothing else is reported at plan level).
        (string Label, VisualPlan Plan, string Code, ValidationContext? Ctx)[] refusals =
        {
            ("no 3D on the canvas", plan, LessonIssueCodes.SchematicUnavailable, new ValidationContext(Desktop1440)),
            ("an unknown shape (cone)", Shape(plan, s => s with { Shape = "cone" }), LessonIssueCodes.SchematicInvalid, null),
            ("an unknown finish (gold)", Shape(plan, s => s with { Finish = "gold" }), LessonIssueCodes.SchematicInvalid, null),
            ("a size above 4 (wider than the ±2 box)", Shape(plan, s => s with { Size = new[] { 4.5, 0.4, 0.26 } }), LessonIssueCodes.SchematicInvalid, null),
            ("a centre outside ±2", Shape(plan, s => s with { At = new[] { -2.4, 0, 0 } }), LessonIssueCodes.SchematicInvalid, null),
            ("a missing number (two of three)", Shape(plan, s => s with { Turn = new[] { 0d, 0 } }), LessonIssueCodes.SchematicInvalid, null),
            ("a non-number (NaN)", Shape(plan, s => s with { At = new[] { double.NaN, 0, 0 } }), LessonIssueCodes.SchematicInvalid, null),
            ("13 parts", Model(plan, m => m with { Parts = Enumerable.Range(1, 13).Select(i => m.Parts[0] with { Id = $"p{i}" }).ToList() }), LessonIssueCodes.SchematicInvalid, null),
            ("41 shapes", Model(plan, m => m with { Parts = new[] { m.Parts[0] with { Shapes = Enumerable.Repeat(m.Parts[0].Shapes[0], 41).ToList() } } }), LessonIssueCodes.SchematicInvalid, null),
            ("a part id used twice", Model(plan, m => m with { Parts = m.Parts.Select(p => p.Id == "wings" ? p with { Id = "fuselage" } : p).ToList() }), LessonIssueCodes.SchematicInvalid, null),
            ("a part with no shapes", Model(plan, m => m with { Parts = m.Parts.Select(p => p.Id == "tail" ? p with { Shapes = Array.Empty<SchematicShape>() } : p).ToList() }), LessonIssueCodes.SchematicInvalid, null),
            ("an empty title", Model(plan, m => m with { Title = " " }), LessonIssueCodes.SchematicInvalid, null),
            ("the id of a catalogue model", Model(plan, m => m with { Id = "engine-test" }), LessonIssueCodes.SchematicCollision, null),
            ("a failed geometry check", plan, LessonIssueCodes.SchematicGeometry, ctx with { SchematicGeometry = _ => new[] { "bounds at nozzle: outside the box" } }),
        };
        foreach (var (label, variant, code, c) in refusals)
        {
            var r = Run(variant, c);
            var planLevel = r.Issues.Where(i => i.BeatId is null).Select(i => i.Code).Distinct().ToList();
            var onlyThis = planLevel.All(x => x == code || (c is { Scene3d: null } && x == LessonIssueCodes.SurfaceUnsupported));
            failures = Check(Has(r, code) && onlyThis && (r.Plan is null || r.Plan.Schematics.Count == 0),
                $"{label} -> {code}, the schematic dropped ({string.Join(", ", planLevel)})", failures);
        }

        // 5. Two schematics: the second is refused, the first is kept.
        var two = plan with { Schematics = new[] { jet, jet with { Id = "f35b-two" } } };
        var twoResult = Run(two);
        failures = Check(Has(twoResult, LessonIssueCodes.SchematicInvalid) && twoResult.Plan!.Schematics.Select(s => s.Id).SequenceEqual(new[] { "f35b" }),
            "a second schematic -> schematic.invalid (max 1); the first is kept and the lesson stays valid", failures);

        // 6. A schematic is placed like a catalogue model, but it has no animations and only its own parts.
        var animated = WithBeat(plan, 4, b => b with { Ops = b.Ops.Append(new AnimateOp("jet", "spin")).ToList() });
        var invented = WithBeat(plan, 1, b => b with { Ops = b.Ops.Select(o => o is HighlightOp ? new HighlightOp("jet.afterburner") : o).ToList() });
        failures = Check(Has(Run(animated), LessonIssueCodes.AnimationUnknown, "b5") && Has(Run(invented), LessonIssueCodes.PartUnknown, "b2"),
            "animate on a schematic -> animation.unknown; a part it does not have -> part.unknown", failures);

        // 7. The player hands a schematic canvas the lesson's schematics before the reset, on both paths (DEC-050).
        var canvas = new SchematicCanvas();
        var player = new LessonPlayer(canvas, new SilentSpeaker());
        await player.PlayAsync(golden);
        var afterPlay = string.Join(" ", canvas.Log);
        await player.PlayAsync(Run(VisualPlanParser.Parse(Fixture("chain-rule.json")).Plan!));
        var afterFlat = string.Join(" ", canvas.Log);
        failures = Check(afterPlay.StartsWith("schematics:f35b clear draw:b1", StringComparison.Ordinal) && afterFlat.Contains("schematics: clear", StringComparison.Ordinal),
            "PlayAsync hands the lesson's schematics before the reset, and the next lesson replaces them (here with none)", failures);
        var streamed = new SchematicCanvas();
        await new LessonPlayer(streamed, new SilentSpeaker()).PlayStreamAsync(golden.Plan!.Surfaces, Each(golden.Plan.Beats), LessonOutcome.Valid, CancellationToken.None, golden.Plan.Schematics);
        failures = Check(string.Join(" ", streamed.Log).StartsWith("schematics:f35b clear draw:b1", StringComparison.Ordinal) && streamed.Log.Count(e => e.StartsWith("draw:", StringComparison.Ordinal)) == 6,
            "PlayStreamAsync hands the header's schematics the same way, then draws all 6 beats", failures);

        return Summarize(failures, "SCHEMATIC OK");
    }

    private static async IAsyncEnumerable<LessonBeat> Each(IEnumerable<LessonBeat> beats)
    {
        foreach (var beat in beats)
        {
            await Task.Yield();
            yield return beat;
        }
    }

    /// <summary>A canvas that also draws schematics: it logs what it was handed, then each reset and beat.</summary>
    private sealed class SchematicCanvas : ILessonCanvas, ISchematicCanvas
    {
        public List<string> Log { get; } = new();

        public void UseSchematics(IReadOnlyList<SchematicModel> schematics) => Log.Add("schematics:" + string.Join(",", schematics.Select(s => s.Id)));

        public Task ApplyBeatAsync(LessonBeat beat, CancellationToken cancellationToken)
        {
            Log.Add("draw:" + beat.Id);
            return Task.CompletedTask;
        }

        public Task ShowNoticeAsync(string notice, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResetAsync(IReadOnlyList<LessonSurface> surfaces, CancellationToken cancellationToken)
        {
            Log.Add("clear");
            return Task.CompletedTask;
        }
    }

    private sealed class SilentSpeaker : ILessonSpeaker
    {
        public Task SpeakAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static LessonBeat CardBeat(string id) =>
        new(id, "That is the whole idea.", new LessonOp[] { new StepOp("card-hover", "s-" + id, 9, "thrust up, weight down") });

    private static VisualPlan Model(VisualPlan plan, Func<SchematicModel, SchematicModel> change) =>
        plan with { Schematics = plan.Schematics.Select(change).ToList() };

    private static VisualPlan Shape(VisualPlan plan, Func<SchematicShape, SchematicShape> change) =>
        Model(plan, m => m with { Parts = m.Parts.Select(p => p.Id == "nozzle" ? p with { Shapes = p.Shapes.Select(change).ToList() } : p).ToList() });
}
