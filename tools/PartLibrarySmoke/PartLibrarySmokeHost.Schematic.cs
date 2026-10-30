using HeyGanesh.PartLibrary;
using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.Tools.PartLibrarySmoke;

/// <summary>
/// PL-6 (DEC-048): a lesson's schematic becomes a <see cref="LibraryModel"/> the renderers already draw —
/// marked schematic, never reviewed, no animations, every part with a finite explode offset — and is held to
/// the same <see cref="LibraryCheck"/> rules. Wired as the validator's geometry delegate, the real catalogue
/// plus the fixture's schematic is a valid lesson, and a shape outside the box drops the schematic.
/// </summary>
internal static partial class PartLibrarySmokeHost
{
    private static int RunAssertSchematic()
    {
        var failures = 0;
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "f35b-schematic.json"));
        var spec = VisualPlanParser.Parse(json).Plan!.Schematics.Single();
        var model = Schematics.ToModel(spec);

        failures = Check(model is { Id: "f35b", Schematic: true, Reviewed: false } && model.Basis == Schematics.Basis && model.Animations.Count == 0,
            "the model is the schematic's id, marked schematic, not reviewed, with the schematic basis and no animations", failures);
        failures = Check(model.Parts.Count == 7 && model.Parts.Sum(p => p.Primitives.Count) == 10
                         && model.Part("nozzle").Primitives[0] is { Shape: PrimitiveShape.Cylinder, Finish: Finish.Brass }
                         && model.Part("cockpit").Primitives[0].Finish == Finish.Glass && model.Part("lift-fan").Primitives[0].Finish == Finish.DarkMetal,
            "7 parts, 10 primitives; shapes and finishes map one to one (cylinder/brass, glass, dark-metal)", failures);
        failures = Check(model.Parts.All(p => double.IsFinite(p.ExplodeOffset.Length) && Math.Abs(p.ExplodeOffset.Length - Schematics.ExplodeDistance) < 1e-9),
            $"every part explodes outward by {Schematics.ExplodeDistance} model units", failures);

        var issues = Schematics.Check(spec);
        failures = Check(issues.Count == 0, $"the fixture's schematic passes LibraryCheck ({(issues.Count == 0 ? "no issues" : string.Join("; ", issues))})", failures);

        var outside = Retarget(spec, "nozzle", s => s with { At = new[] { -1.95, -0.15, 0 } });
        var flat = Retarget(spec, "cockpit", s => s with { Size = new[] { 0d, 0, 0 } });
        failures = Check(Schematics.Check(outside).Any(i => i.StartsWith(LibraryIssueCodes.Bounds, StringComparison.Ordinal))
                         && Schematics.Check(flat).Any(i => i.StartsWith(LibraryIssueCodes.SizeInvalid, StringComparison.Ordinal)),
            "a nozzle poking out of the ±2 box -> bounds; a zero-size sphere -> size.invalid", failures);

        // Wired as the engine's geometry delegate, with the shipped catalogue.
        var context = new ValidationContext(new HeyGanesh.Abstractions.PixelRect(0, 0, 2560, 1440)) { Scene3d = ModelLibrary.ToCatalog(), SchematicGeometry = Schematics.Check };
        var real = new VisualPlanValidator().Validate(VisualPlanParser.Parse(json), context);
        failures = Check(real.Outcome == LessonOutcome.Valid && real.Plan!.Schematics.Count == 1,
            $"f35b-schematic.json with the real catalogue and Schematics.Check -> Valid ({real.Outcome}{string.Concat(real.Issues.Take(2).Select(i => $"; {i.Code}: {i.Message}"))})", failures);
        var poking = json.Replace("\"at\": [-1.7, -0.15, 0]", "\"at\": [-1.95, -0.15, 0]", StringComparison.Ordinal);
        var dropped = new VisualPlanValidator().Validate(VisualPlanParser.Parse(poking), context);
        failures = Check(dropped.Issues.Any(i => i.Code == LessonIssueCodes.SchematicGeometry) && (dropped.Plan is null || dropped.Plan.Schematics.Count == 0),
            "the same lesson with the nozzle outside the box -> schematic.geometry, the schematic dropped", failures);

        return Summarize(failures, "SCHEMATIC OK");
    }

    private static SchematicModel Retarget(SchematicModel spec, string part, Func<SchematicShape, SchematicShape> change) =>
        spec with { Parts = spec.Parts.Select(p => p.Id == part ? p with { Shapes = p.Shapes.Select(change).ToList() } : p).ToList() };
}
