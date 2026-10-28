using static HeyGanesh.PartLibrary.ModelBuilder;

namespace HeyGanesh.PartLibrary.Models;

/// <summary>
/// Ten base pairs of B-DNA, placed from the standard parameters rather than by hand: 10.5 base pairs
/// per turn, 0.34 nm rise per pair, 2.0 nm diameter, at 1 scene unit per nanometre. Helix axis along Y.
/// The left strand reads <see cref="Sequence"/>; the right strand is its Watson–Crick complement.
/// </summary>
public static class DnaHelix
{
    public const string Id = "dna-helix";
    public const string Sequence = "ATGCGTACCA";
    public const double BasePairsPerTurn = 10.5;
    public const double RisePerPair = 0.34;
    public const double Radius = 1.0;

    /// <summary>Half-rung: from 0.15 to 0.95 out from the axis, leaving a gap for the hydrogen bonds.</summary>
    public const double HalfRungCentre = 0.55;
    public const double HalfRungLength = 0.8;

    public static double TwistPerPairDeg => 360 / BasePairsPerTurn;

    public static char Complement(char b) => b switch { 'A' => 'T', 'T' => 'A', 'G' => 'C', 'C' => 'G', _ => '?' };

    public static string PartFor(char b) => b switch { 'A' => "adenine", 'T' => "thymine", 'G' => "guanine", 'C' => "cytosine", _ => "?" };

    public static double PairY(int i) => (i - ((Sequence.Length - 1) / 2.0)) * RisePerPair;

    public static double PairAngleDeg(int i) => i * TwistPerPairDeg;

    /// <summary>Unit vector from the axis toward the left strand at pair <paramref name="i"/>.</summary>
    public static Vec3 Toward(int i) => V(Math.Cos(Geometry.Rad(PairAngleDeg(i))), 0, Math.Sin(Geometry.Rad(PairAngleDeg(i))));

    /// <summary>Lays a cylinder's local +Y along <see cref="Toward"/>: Z by −90° (+Y → +X), then Y by −θ.</summary>
    public static Vec3 RungRotation(int i) => V(0, -PairAngleDeg(i), -90);

    public static LibraryModel Build()
    {
        var left = new List<Primitive>();
        var right = new List<Primitive>();
        var bonds = new List<Primitive>();
        var bases = new Dictionary<string, List<Primitive>>(StringComparer.Ordinal)
        {
            ["adenine"] = new(), ["thymine"] = new(), ["guanine"] = new(), ["cytosine"] = new(),
        };

        for (var i = 0; i < Sequence.Length; i++)
        {
            var axis = V(0, PairY(i), 0);
            var u = Toward(i);
            var rot = RungRotation(i);
            left.Add(Sphere(0.3, axis + (u * Radius), Finish.Backbone));
            right.Add(Sphere(0.3, axis - (u * Radius), Finish.Backbone));
            var b = Sequence[i];
            var c = Complement(b);
            bases[PartFor(b)].Add(Cylinder(0.14, HalfRungLength, axis + (u * HalfRungCentre), FinishFor(b), rot));
            bases[PartFor(c)].Add(Cylinder(0.14, HalfRungLength, axis - (u * HalfRungCentre), FinishFor(c), rot));
            bonds.Add(Cylinder(0.04, 0.3, axis, Finish.Backbone, rot));
        }

        var parts = new List<LibraryPart>
        {
            Part("backbone-left", "Backbone strand 1", "One sugar-phosphate backbone strand; the bases hang off it like the rungs of a twisted ladder.",
                Vec3.Zero, Vec3.Zero, left.ToArray()),
            Part("backbone-right", "Backbone strand 2", "The second backbone strand, running the opposite way to the first (the strands are antiparallel).",
                Vec3.Zero, Vec3.Zero, right.ToArray()),
            Part("adenine", "Adenine (A)", "Adenine (A) pairs only with thymine (T), held by two hydrogen bonds.", Vec3.Zero, Vec3.Zero, bases["adenine"].ToArray()),
            Part("thymine", "Thymine (T)", "Thymine (T) pairs only with adenine (A), held by two hydrogen bonds.", Vec3.Zero, Vec3.Zero, bases["thymine"].ToArray()),
            Part("guanine", "Guanine (G)", "Guanine (G) pairs only with cytosine (C), held by three hydrogen bonds.", Vec3.Zero, Vec3.Zero, bases["guanine"].ToArray()),
            Part("cytosine", "Cytosine (C)", "Cytosine (C) pairs only with guanine (G), held by three hydrogen bonds.", Vec3.Zero, Vec3.Zero, bases["cytosine"].ToArray()),
            Part("hydrogen-bonds", "Hydrogen bonds", "Weak bonds between paired bases: two per A-T pair, three per G-C pair; drawn as one bar per pair.",
                Vec3.Zero, Vec3.Zero, bonds.ToArray()),
        };

        Keyframe[] spinKeys = { new(0, Vec3.Zero), new(0.5, V(0, 180, 0)), new(1, V(0, 360, 0)) };
        var spin = new LibraryAnimation("spin", "Slow turn", 12.0, Loop: true,
            parts.Select(p => new Track(p.Id, TrackProperty.RotationDeg, spinKeys)).ToList());

        return new LibraryModel(
            Id,
            "DNA double helix (B-DNA, 10 base pairs)",
            "biology",
            Schematic: false,
            "B-DNA parameters: 10.5 base pairs per turn, 0.34 nm rise per pair, 2.0 nm diameter (1 scene unit = 1 nm). "
                + "Simplified: the strands are drawn 180 degrees apart, so the major and minor grooves are not shown.",
            parts,
            new[] { spin });
    }

    private static Finish FinishFor(char b) => b switch
    {
        'A' => Finish.BaseAdenine,
        'T' => Finish.BaseThymine,
        'G' => Finish.BaseGuanine,
        _ => Finish.BaseCytosine,
    };
}
