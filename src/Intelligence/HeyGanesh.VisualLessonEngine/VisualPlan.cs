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
    IReadOnlyList<LessonBeat> Beats);
