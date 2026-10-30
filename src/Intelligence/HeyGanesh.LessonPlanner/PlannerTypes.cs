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

    /// <summary>
    /// This session's earlier turns, oldest first (DEC-061), so a follow-up can be understood. Untrusted (earlier
    /// questions and earlier model output); only the last <see cref="EarlierTurn.MaxTurns"/> are sent, fenced.
    /// Empty: the prompt is unchanged.
    /// </summary>
    public IReadOnlyList<EarlierTurn> EarlierTurns { get; init; } = Array.Empty<EarlierTurn>();

    /// <summary>
    /// The geometry check for lesson-defined schematics (DEC-048/DEC-049): the Part Library's
    /// <c>Schematics.Check</c>, passed by a caller whose canvas draws schematics. With it and a
    /// <see cref="Scene3d"/> catalogue, the prompt offers one schematic for an object the catalogue lacks
    /// and the validator checks it; without it schematics are neither offered nor accepted.
    /// </summary>
    public Func<SchematicModel, IReadOnlyList<string>>? SchematicGeometry { get; init; }
}

/// <summary>
/// The planned lesson, already validated (it is the only thing a player may receive), plus how it
/// was obtained. Never contains a key or a URL. <c>PlanJson</c> is the exact JSON text <c>Lesson</c> was
/// validated from (DEC-059), so a stored lesson can be parsed and validated again and replayed with no
/// model call. Null only when no answer came back at all.
/// </summary>
/// <summary>One earlier turn of the session: what was asked, and what was taught in a line or two (DEC-061).</summary>
public sealed record EarlierTurn(string Question, string Taught)
{
    /// <summary>How many earlier turns a prompt carries, the newest kept.</summary>
    public const int MaxTurns = 3;

    /// <summary>Caps per turn, in characters: a question, and what was taught.</summary>
    public const int MaxQuestionChars = 200;
    public const int MaxTaughtChars = 300;
}

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
    string? RepairError = null,
    int ReleasedBeats = 0,
    TimeSpan? FirstBeatLatency = null,
    string? StreamNote = null,
    string? PlanJson = null);

/// <summary>
/// One beat released while the lesson was still streaming (DEC-041): <see cref="Beat"/> as the
/// validator left it inside <see cref="Prefix"/>, the header plus every beat up to this one.
/// </summary>
public sealed record StreamedBeat(int Index, LessonBeat Beat, LessonValidationResult Prefix, TimeSpan Elapsed)
{
    /// <summary>
    /// A beat's identity for comparing a released beat with the same beat in the finished plan: id,
    /// speech, whether it degraded, and every op's own record text.
    /// </summary>
    public static string Fingerprint(LessonBeat beat) =>
        $"{beat.Id}|{beat.Say}|{beat.Degraded}|{string.Join(";", beat.Ops.Select(o => o.ToString()))}";
}

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
