using static HeyGanesh.PartLibrary.ModelBuilder;

namespace HeyGanesh.PartLibrary.Models;

/// <summary>
/// The sodium–potassium pump, SCHEMATIC (FEATURE-007 §3.2): the shapes are illustrative, not protein
/// structure. What is a fact, and checked (PartLibrarySmoke --assert-physics): each cycle carries three
/// Na⁺ from inside the cell to outside and two K⁺ from outside to inside, powered by one ATP.
/// The membrane lies in the XZ plane; +Y is outside the cell, −Y is the cytoplasm.
/// </summary>
public static class SodiumPotassiumPump
{
    public const string Id = "na-k-pump";
    public const double MembraneHalfThickness = 0.4;
    public const double IonDiameter = 0.22;
    public const double IonTravel = 1.5;

    public static LibraryModel Build()
    {
        var parts = new List<LibraryPart>
        {
            Part("membrane", "Cell membrane", "The cell membrane, a lipid bilayer; its top face is outside the cell and its bottom face is the cytoplasm.",
                Vec3.Zero, V(0, 0, -0.8), Box(3.6, MembraneHalfThickness * 2, 2.0, Vec3.Zero, Finish.Membrane)),
            Part("pump-protein", "Na+/K+ pump", "The Na+/K+-ATPase: a protein spanning the membrane that moves ions against their concentration gradients.",
                Vec3.Zero, V(0, 0, 0.6),
                Capsule(0.6, 1.6, V(-0.32, 0, 0), Finish.Protein), Capsule(0.6, 1.6, V(0.32, 0, 0), Finish.Protein)),
            Part("binding-sites", "Binding sites", "Sites inside the pump that bind three sodium ions from inside, then two potassium ions from outside.",
                Vec3.Zero, V(0, 0, 0.9),
                Box(0.2, 0.1, 0.2, V(0, -0.25, 0), Finish.DarkMetal), Box(0.2, 0.1, 0.2, V(0, 0.25, 0), Finish.DarkMetal)),
            Part("sodium-ions", "Sodium ions (Na+)", "Three sodium ions (Na+), carried from inside the cell to outside in each cycle.",
                Vec3.Zero, V(0, 0, 0.5),
                Sphere(IonDiameter, V(-0.25, -0.75, 0.6), Finish.IonSodium),
                Sphere(IonDiameter, V(0, -0.75, 0.6), Finish.IonSodium),
                Sphere(IonDiameter, V(0.25, -0.75, 0.6), Finish.IonSodium)),
            Part("potassium-ions", "Potassium ions (K+)", "Two potassium ions (K+), carried from outside the cell to inside in each cycle.",
                Vec3.Zero, V(0, 0, -0.5),
                Sphere(IonDiameter, V(-0.15, 0.75, -0.6), Finish.IonPotassium),
                Sphere(IonDiameter, V(0.15, 0.75, -0.6), Finish.IonPotassium)),
            Part("atp", "ATP", "ATP, whose breakdown to ADP releases the energy for one pump cycle.",
                Vec3.Zero, V(0.6, -0.3, 0), Sphere(0.3, V(1.0, -0.8, 0), Finish.Atp)),
        };

        // Not looping: the cycle ends with the ions on the other side, which is the point of the lesson.
        var cycle = new LibraryAnimation("pump-cycle", "One pump cycle", 6.0, Loop: false, new[]
        {
            new Track("atp", TrackProperty.Position, new Keyframe[] { new(0, Vec3.Zero), new(0.25, V(-0.55, 0, 0)), new(1, V(-0.55, 0, 0)) }),
            new Track("sodium-ions", TrackProperty.Position, new Keyframe[] { new(0, Vec3.Zero), new(0.5, V(0, IonTravel, 0)), new(1, V(0, IonTravel, 0)) }),
            new Track("potassium-ions", TrackProperty.Position, new Keyframe[] { new(0, Vec3.Zero), new(0.5, Vec3.Zero), new(1, V(0, -IonTravel, 0)) }),
        });

        return new LibraryModel(
            Id,
            "Sodium-potassium pump (schematic)",
            "biology",
            Schematic: true,
            "Schematic: shapes are illustrative, not protein structure. Stated facts: each cycle moves 3 Na+ out of the cell "
                + "and 2 K+ into it, powered by one ATP.",
            parts,
            new[] { cycle });
    }
}
