using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.Tools.LessonSmoke;

/// <summary>
/// LS-13, the scene3d contract (schema 1.1, DEC-028): a 3D scene is built only from catalogue models,
/// parts and animations, every reference is checked in beat order, and 3D stays off unless the caller
/// passes a catalogue — the gate that keeps a scene from reaching a canvas that would draw it as a card.
/// The catalogue here is a test one; the real part library is Phase 5 step 3.
/// </summary>
internal static partial class LessonSmokeHost
{
    private static readonly ModelCatalog TestCatalog = new(new[]
    {
        new CatalogModel(
            "engine-test",
            new HashSet<string>(StringComparer.Ordinal) { "piston", "connecting-rod", "crankshaft", "valve-intake" },
            new HashSet<string>(StringComparer.Ordinal) { "stroke-cycle" }),
    });

    private static int RunAssertScene3d()
    {
        var failures = 0;
        var json = Fixture("engine-3d.json");
        var engine = VisualPlanParser.Parse(json).Plan!;
        var with3d = new ValidationContext(Desktop1440) { Scene3d = TestCatalog };
        LessonValidationResult Check3d(VisualPlan plan) => new VisualPlanValidator().Validate(new ParseResult(plan, Array.Empty<LessonIssue>()), with3d);
        LessonValidationResult Parse3d(string text) => new VisualPlanValidator().Validate(VisualPlanParser.Parse(text), with3d);

        // 1. The golden lesson, and determinism.
        var first = Check3d(engine);
        var second = Check3d(engine);
        failures = Check(first.Outcome == LessonOutcome.Valid && first.Issues.Count == 0, "engine-3d.json with a catalogue -> Valid, zero issues", failures);
        failures = Check(first.Issues.Select(i => i.Code + i.BeatId).SequenceEqual(second.Issues.Select(i => i.Code + i.BeatId)), "two runs -> identical issue lists", failures);
        failures = Check(engine.Beats[0].Ops[0] is ModelOp { Model: "engine-test" } && engine.Beats[0].Ops[1] is CameraOp { View: CameraView.Iso }
            && engine.Beats[3].Ops[0] is AnimateOp { Animation: "stroke-cycle" } && engine.Beats[4].Ops[0] is ExplodeOp { Amount: 0.6 },
            "parser reads model/camera/animate/explode with their fields", failures);

        // 2. The gate: no catalogue -> no 3D, and the speech survives.
        var gated = Validate(engine);
        var says = string.Join(" ", engine.Beats.Select(b => b.Say));
        failures = Check(gated.Outcome == LessonOutcome.TextFallback && Has(gated, LessonIssueCodes.SurfaceUnsupported) && gated.Plan is null && gated.FallbackText == says,
            "no ValidationContext.Scene3d -> surface.unsupported, TextFallback, no plan, every say spoken", failures);

        // 3. Schema: a scene needs 1.1; 1.1 with cards only is fine, with or without a catalogue.
        failures = Check(Has(Parse3d(json.Replace("visualplan/1.1", "visualplan/1", StringComparison.Ordinal)), LessonIssueCodes.SchemaUnsupported), "scene3d declared under schema /1 -> schema.unsupported", failures);
        var chain11 = Fixture("chain-rule.json").Replace("visualplan/1\"", "visualplan/1.1\"", StringComparison.Ordinal);
        failures = Check(new VisualPlanValidator().Validate(VisualPlanParser.Parse(chain11), Desktop1440).Outcome == LessonOutcome.Valid, "a cards-only /1.1 plan with no catalogue -> Valid", failures);
        failures = Check(Parse3d(Fixture("chain-rule.json")).Outcome == LessonOutcome.Valid, "a v1 plan with a catalogue -> Valid (the catalogue changes nothing in 2D)", failures);

        // 4. Limits: one scene; three live models.
        var twoScenes = engine with { Surfaces = engine.Surfaces.Append(new LessonSurface("scene-two", SurfaceKind.Scene3d, "two")).ToList() };
        failures = Check(Has(Check3d(twoScenes), LessonIssueCodes.LimitScenes), "two scene3d surfaces -> limit.scenes", failures);
        var crowded = WithBeat(engine, 5, b => b with { Ops = b.Ops.Concat(new LessonOp[]
        {
            new ModelOp("scene-engine", "engine-two", "engine-test"),
            new ModelOp("scene-engine", "engine-three", "engine-test"),
            new ModelOp("scene-engine", "engine-four", "engine-test"),
        }).ToList() });
        var full = WithBeat(engine, 5, b => b with { Ops = b.Ops.Concat(new LessonOp[]
        {
            new ModelOp("scene-engine", "engine-two", "engine-test"),
            new ModelOp("scene-engine", "engine-three", "engine-test"),
        }).ToList() });
        failures = Check(Check3d(full).Outcome == LessonOutcome.Valid, "exactly three live models -> Valid (the limit is not off by one)", failures);
        var crowdedResult = Check3d(crowded);
        failures = Check(Has(crowdedResult, LessonIssueCodes.LimitModels, "b6") && crowdedResult.Issues.All(i => i.BeatId == "b6"), "a fourth live model -> limit.models on that beat only", failures);

        // 5. Unknown model: its beat degrades, so the instance never exists for later beats.
        var warp = Check3d(WithBeat(engine, 0, b => b with { Ops = b.Ops.Select(o => o is ModelOp m ? m with { Model = "warp-drive" } : o).ToList() }));
        failures = Check(Has(warp, LessonIssueCodes.ModelUnknown, "b1") && Has(warp, LessonIssueCodes.RefTarget, "b2"), "model not in the catalogue -> model.unknown on b1, later use -> ref.target", failures);

        // 6. Parts: invented, too early, after a clear.
        var flux = Check3d(WithBeat(engine, 1, b => b with { Ops = b.Ops.Select(o => o is LabelOp l ? l with { Target = "engine.flux-capacitor" } : o).ToList() }));
        failures = Check(Has(flux, LessonIssueCodes.PartUnknown, "b2") && !Has(flux, LessonIssueCodes.RefTarget, "b2"), "invented part -> part.unknown (not ref.target)", failures);
        var early = Check3d(WithBeat(engine, 0, b => b with { Ops = new LessonOp[] { new HighlightOp("engine.piston") }.Concat(b.Ops).ToList() }));
        failures = Check(Has(early, LessonIssueCodes.RefTarget, "b1"), "part ref before its model op -> ref.target", failures);
        var cleared = Check3d(AppendOp(engine, 4, new ClearOp("scene-engine")));
        failures = Check(Has(cleared, LessonIssueCodes.RefTarget, "b6"), "clear on the scene removes instances and labels -> later refs are ref.target", failures);

        // 7. Animation names and explode amounts.
        failures = Check(Has(Check3d(WithBeat(engine, 3, b => b with { Ops = b.Ops.Select(o => o is AnimateOp a ? a with { Animation = "warp-spin" } : o).ToList() })), LessonIssueCodes.AnimationUnknown, "b4"),
            "animation the model lacks -> animation.unknown", failures);
        failures = Check(Has(Check3d(AppendOp(engine, 4, new ExplodeOp("engine", 1.5))), LessonIssueCodes.ValueInvalid, "b5")
            && Has(Check3d(AppendOp(engine, 4, new ExplodeOp("engine", double.NaN))), LessonIssueCodes.ValueInvalid, "b5"),
            "explode amount 1.5 or NaN -> value.invalid", failures);
        failures = Check(Has(Check3d(AppendOp(engine, 4, new ExplodeOp("l-piston", 0.5))), LessonIssueCodes.RefTarget, "b5"), "explode on a label (not a model instance) -> ref.target", failures);

        // 8. Ops on the wrong kind of surface; closed camera views.
        failures = Check(Has(Check3d(AppendOp(engine, 0, new WriteOp("scene-engine", "w-stray", "text", TextStyle.Body, null))), LessonIssueCodes.RefSurfaceKind, "b1"), "write on the scene -> ref.surface-kind", failures);
        failures = Check(Has(Check3d(AppendOp(engine, 0, new ModelOp("card-strokes", "engine-card", "engine-test"))), LessonIssueCodes.RefSurfaceKind, "b1"), "model on a card -> ref.surface-kind", failures);
        failures = Check(Has(Parse3d(json.Replace("\"view\": \"iso\"", "\"view\": \"fisheye\"", StringComparison.Ordinal)), LessonIssueCodes.ValueInvalid, "b1"), "camera view 'fisheye' -> value.invalid", failures);

        // 9. A model beat degraded for any reason rolls back: its instance is never live.
        var badText = Check3d(WithBeat(engine, 0, b => b with { Ops = b.Ops.Select(o => o is WriteOp w ? w with { Text = "" } : o).ToList() }));
        failures = Check(Has(badText, LessonIssueCodes.FieldMissing, "b1") && Has(badText, LessonIssueCodes.RefTarget, "b2"), "b1 degraded by an empty write -> its model instance never exists for b2", failures);

        // The catalogue is built by code, so a malformed one is a programming error.
        failures = Check(Throws(() => new ModelCatalog(new[] { new CatalogModel("Bad Id", new HashSet<string>(), new HashSet<string>()) }))
            && Throws(() => new ModelCatalog(new[] { new CatalogModel("dup", new HashSet<string>(), new HashSet<string>()), new CatalogModel("dup", new HashSet<string>(), new HashSet<string>()) })),
            "catalogue with an invalid or duplicate model id -> ArgumentException", failures);

        return Summarize(failures, "SCENE3D OK");
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}
