namespace HeyGanesh.PartLibrary;

/// <summary>
/// A point or vector in scene units. Y is up. The 3D renderer (Phase 5 step 4) maps this frame onto
/// Babylon's and must confirm it with a snapshot; the checks here use <see cref="Geometry"/>.
/// </summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 Zero { get; } = new(0, 0, 0);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));
}

/// <summary>The closed primitive kit: exactly what the renderer must implement.</summary>
public enum PrimitiveShape
{
    /// <summary>Size = width (X), height (Y), depth (Z).</summary>
    Box = 0,

    /// <summary>Size = top diameter, height (along local Y), bottom diameter — a cone when they differ.</summary>
    Cylinder = 1,

    /// <summary>Size = diameter, unused, unused.</summary>
    Sphere = 2,

    /// <summary>Size = diameter, tube thickness, unused; the ring lies in the local XZ plane.</summary>
    Torus = 3,

    /// <summary>Size = diameter, total height (along local Y), unused.</summary>
    Capsule = 4,
}

/// <summary>Closed colour/material roles; the renderer owns the actual colours.</summary>
public enum Finish
{
    Metal = 0,
    DarkMetal = 1,
    Brass = 2,
    Glass = 3,
    Membrane = 4,
    Protein = 5,
    IonSodium = 6,
    IonPotassium = 7,
    Atp = 8,
    BaseAdenine = 9,
    BaseThymine = 10,
    BaseGuanine = 11,
    BaseCytosine = 12,
    Backbone = 13,
}

/// <summary>
/// One shape inside a part, in the part's own frame. <see cref="RotationDeg"/> follows
/// <see cref="Geometry.Rotate"/>'s convention (Z, then X, then Y, about fixed axes).
/// </summary>
public sealed record Primitive(PrimitiveShape Shape, Vec3 Size, Vec3 Position, Vec3 RotationDeg, Finish Finish);

/// <summary>
/// A named, teachable piece of a model — what a label, highlight or camera may point at. Its rest pose
/// is <see cref="Position"/>/<see cref="RotationDeg"/> in model space; an animation's first key must
/// equal it. <see cref="ExplodeOffset"/> is where the part moves at explode amount 1.
/// </summary>
public sealed record LibraryPart(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<Primitive> Primitives,
    Vec3 Position,
    Vec3 RotationDeg,
    Vec3 ExplodeOffset);

/// <summary>One sample of a track; <paramref name="T"/> is the fraction of the animation's duration, 0..1.</summary>
public sealed record Keyframe(double T, Vec3 Value);

public enum TrackProperty
{
    Position = 0,
    RotationDeg = 1,
}

/// <summary>A part's position or rotation over time, as absolute model-space values (not offsets).</summary>
public sealed record Track(string Part, TrackProperty Property, IReadOnlyList<Keyframe> Keys);

public sealed record LibraryAnimation(string Id, string Name, double DurationSeconds, bool Loop, IReadOnlyList<Track> Tracks);

/// <summary>
/// One teaching model. <see cref="Schematic"/> marks free-form composition (FEATURE-007 §3.2): shapes are
/// illustrative, only what <see cref="Basis"/> states is a fact. <see cref="Reviewed"/> is false until a
/// person has read the parts and descriptions (PartLibrarySmoke --dump) and the owner records it.
/// </summary>
public sealed record LibraryModel(
    string Id,
    string Title,
    string Subject,
    bool Schematic,
    string Basis,
    IReadOnlyList<LibraryPart> Parts,
    IReadOnlyList<LibraryAnimation> Animations)
{
    public bool Reviewed { get; init; }

    public LibraryPart Part(string id) => Parts.Single(p => p.Id == id);
}
