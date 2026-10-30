namespace HeyGanesh.VisualLessonEngine;

/// <summary>How the lesson teaches. Socratic and worked-example beats arrive in later schema minors.</summary>
public enum LessonMode
{
    Explain = 0,
    Socratic = 1,
    WorkedExample = 2,
}

/// <summary>
/// What a surface is. Cards are v1. <see cref="Scene3d"/> (schema 1.1, DEC-028) is a 3D scene built from
/// catalogue models; it is only accepted when the caller's canvas can draw it (ValidationContext.Scene3d).
/// </summary>
public enum SurfaceKind
{
    Card = 0,
    Scene3d = 1,
}

/// <summary>
/// One place drawing happens — a card the renderer lays out. Declared up front so every op can be
/// checked against a surface that exists.
/// </summary>
public sealed record LessonSurface(string Id, SurfaceKind Kind, string Title);

/// <summary>
/// One beat: the sentence spoken and the drawing that goes with it. Speech is the part that must
/// survive — a degraded beat keeps <see cref="Say"/> and loses <see cref="Ops"/>.
/// </summary>
public sealed record LessonBeat(string Id, string Say, IReadOnlyList<LessonOp> Ops)
{
    /// <summary>True when the validator dropped this beat's drawing; the speech is unchanged.</summary>
    public bool Degraded { get; init; }
}

/// <summary>
/// A whole model-authored lesson as parsed. Not trusted until it has been through
/// <c>VisualPlanValidator</c>: only a validated plan may reach a renderer or the voice.
/// </summary>
public sealed record VisualPlan(
    string Schema,
    string LessonId,
    string Title,
    LessonMode Mode,
    IReadOnlyList<LessonSurface> Surfaces,
    IReadOnlyList<LessonBeat> Beats)
{
    /// <summary>
    /// Schema 1.2 (DEC-048): models this lesson defines for itself, for objects the catalogue does not
    /// have. Header data, declared before the beats. In a validated plan only the schematics that passed
    /// remain; a refused one is gone, and the beats that used it lost their drawing.
    /// </summary>
    public IReadOnlyList<SchematicModel> Schematics { get; init; } = Array.Empty<SchematicModel>();
}

/// <summary>
/// One shape of a schematic part, in the Part Library's model space (metres-ish units, ±2 box). The
/// strings are kept raw so the validator can refuse an unknown shape or finish with a reason instead of
/// the parser guessing. Size follows <c>PrimitiveShape</c>'s per-shape meaning; At is the centre, Turn
/// the rotation in degrees about X, Y, Z. A missing or non-numeric component reads as NaN.
/// </summary>
public sealed record SchematicShape(string Shape, IReadOnlyList<double> Size, IReadOnlyList<double> At, IReadOnlyList<double> Turn, string Finish);

/// <summary>A named, labelable part of a schematic: what "&lt;instance&gt;.&lt;part&gt;" points at.</summary>
public sealed record SchematicPart(string Id, string Name, IReadOnlyList<SchematicShape> Shapes);

/// <summary>
/// A simplified 3D model a lesson draws for an object the catalogue lacks (FEATURE-007 §3.2 free-form
/// composition). Never reviewed; every renderer shows it with a "schematic" tag. It has no animations.
/// </summary>
public sealed record SchematicModel(string Id, string Title, IReadOnlyList<SchematicPart> Parts);
