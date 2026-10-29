using HeyGanesh.Abstractions;
using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.LessonPlanner;

/// <summary>
/// What grounding found on the student's screen, all of it untrusted: a description (UIA name or
/// Vision label — may contain any text the screen showed), the resolved target box in global physical
/// pixels, and an optional JPEG crop. The box is the only place a planned mark may land.
/// </summary>
public sealed record ScreenGrounding(string? Description, PixelRect? TargetBox, byte[]? Jpeg, int WidthPx, int HeightPx);

/// <summary>One planning request: the student's question plus optional grounding and follow-up context.</summary>
public sealed record PlanRequest
{
    /// <summary>The teaching task, verbatim (normally the committed voice transcript).</summary>
    public required string Question { get; init; }

    public ScreenGrounding? Screen { get; init; }

    /// <summary>The virtual desktop in physical pixels — the outer bound for any mark.</summary>
    public required PixelRect VirtualDesktop { get; init; }

    /// <summary>The lesson just shown, for follow-ups ("simpler", "just the combustion part"). DEC-022.</summary>
    public VisualPlan? PreviousPlan { get; init; }

    public LessonMode Mode { get; init; } = LessonMode.Explain;

    /// <summary>
    /// The 3D vocabulary the caller's canvas can draw (DEC-028), passed straight to the validator. Null
    /// (the default) refuses every 3D surface. Validation only: the prompt does not teach the 3D ops
    /// yet (FEATURE-007 Phase 5 step 6).
    /// </summary>
    public ModelCatalog? Scene3d { get; init; }
}

/// <summary>
/// The planned lesson, already validated (it is the only thing a player may receive), plus how it
/// was obtained. Never contains a key or a URL.
/// </summary>
public sealed record PlanResult(
    LessonValidationResult Lesson,
    int Attempts,
    bool UsedImage,
    string? ImageNote,
    string ModelId,
    int InputTokens,
    int OutputTokens,
    TimeSpan Latency,
    IReadOnlyList<LessonIssue> FirstAttemptIssues,
    string? RepairError = null);

public sealed class LessonPlannerOptions
{
    public int MaxOutputTokens { get; init; } = 4000;

    /// <summary>Low by default: a lesson should be the same lesson when asked twice.</summary>
    public double Temperature { get; init; } = 0.2;

    /// <summary>Model calls per plan: the first answer plus at most one repair.</summary>
    public int MaxAttempts { get; init; } = 2;

    public int MaxImageBytes { get; init; } = 1_500_000;

    public int MaxDescriptionChars { get; init; } = 1000;

    public int MaxPreviousLessonChars { get; init; } = 2000;

    public int MarkMarginPx { get; init; } = 24;

    public static LessonPlannerOptions Default { get; } = new();
}
