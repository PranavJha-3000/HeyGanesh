using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.PartLibrary;

/// <summary>
/// Turns a lesson's schematic (VisualPlan 1.2, DEC-048) into a <see cref="LibraryModel"/>, so it is drawn,
/// labelled, exploded and picked by exactly the code that handles shipped models, and checks it with the
/// same <see cref="LibraryCheck"/> rules. The model is marked <see cref="LibraryModel.Schematic"/> (every
/// renderer then shows the "schematic" tag) and is never <c>Reviewed</c>.
/// </summary>
/// <remarks>
/// Called by the lesson's validator through <c>ValidationContext.SchematicGeometry</c> (the engine never
/// references this assembly) and later by the scene surface to draw it. <see cref="ToModel"/> expects a
/// schematic that passed the engine's structural rules; an unknown shape or finish is a caller bug and
/// throws.
/// </remarks>
public static class Schematics
{
    /// <summary>What a schematic is, in the words the dump and the scene's tag stand behind.</summary>
    public const string Basis = "Drawn by the lesson model for this lesson only: simplified, not to scale, not reviewed.";

    /// <summary>How far each part moves out when the whole model is exploded (amount 1), in model units.</summary>
    public const double ExplodeDistance = 0.6;

    public static LibraryModel ToModel(SchematicModel schematic)
    {
        ArgumentNullException.ThrowIfNull(schematic);
        var parts = schematic.Parts.Select(p => (Part: p, Primitives: p.Shapes.Select(ToPrimitive).ToList())).ToList();
        var all = parts.SelectMany(p => p.Primitives).Select(x => x.Position).ToList();
        var centre = all.Count == 0 ? Vec3.Zero : Mean(all);
        return new LibraryModel(
            schematic.Id,
            schematic.Title,
            "schematic",
            Schematic: true,
            Basis,
            parts.Select(p => new LibraryPart(
                p.Part.Id,
                p.Part.Name,
                $"{p.Part.Name}: a simplified shape drawn for this lesson.",
                p.Primitives,
                Vec3.Zero,
                Vec3.Zero,
                Explode(p.Primitives, centre))).ToList(),
            Array.Empty<LibraryAnimation>());
    }

    /// <summary>
    /// The reasons the schematic cannot be drawn: the <see cref="LibraryCheck"/> issues of its model, as
    /// "code at where: message". Empty when it can. This is the delegate a caller passes as
    /// <c>ValidationContext.SchematicGeometry</c>.
    /// </summary>
    public static IReadOnlyList<string> Check(SchematicModel schematic) =>
        LibraryCheck.Check(ToModel(schematic)).Select(i => $"{i.Code} at {i.Where}: {i.Message}").ToList();

    private static Primitive ToPrimitive(SchematicShape s) => new(
        s.Shape switch
        {
            "box" => PrimitiveShape.Box,
            "cylinder" => PrimitiveShape.Cylinder,
            "sphere" => PrimitiveShape.Sphere,
            "torus" => PrimitiveShape.Torus,
            "capsule" => PrimitiveShape.Capsule,
            _ => throw new ArgumentException($"shape '{s.Shape}' is not a schematic shape", nameof(s)),
        },
        Vec(s.Size),
        Vec(s.At),
        Vec(s.Turn),
        s.Finish switch
        {
            "metal" => Finish.Metal,
            "dark-metal" => Finish.DarkMetal,
            "brass" => Finish.Brass,
            "glass" => Finish.Glass,
            _ => throw new ArgumentException($"finish '{s.Finish}' is not a schematic finish", nameof(s)),
        });

    private static Vec3 Vec(IReadOnlyList<double> v) => v.Count == 3 ? new Vec3(v[0], v[1], v[2]) : new Vec3(double.NaN, double.NaN, double.NaN);

    private static Vec3 Mean(IReadOnlyList<Vec3> points) =>
        points.Aggregate(Vec3.Zero, (a, b) => a + b) * (1.0 / points.Count);

    /// <summary>
    /// Out from the model's centre along the line through the part's centre; a part sitting on the centre
    /// moves straight up, so an exploded schematic never leaves two parts stacked in place.
    /// </summary>
    private static Vec3 Explode(IReadOnlyList<Primitive> primitives, Vec3 centre)
    {
        if (primitives.Count == 0)
        {
            return Vec3.Zero;
        }

        var away = Mean(primitives.Select(p => p.Position).ToList()) - centre;
        return away.Length < 1e-6 ? new Vec3(0, ExplodeDistance, 0) : away * (ExplodeDistance / away.Length);
    }
}
