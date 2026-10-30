namespace HeyGanesh.TurnRouter;

/// <summary>How a turn is taught. Option ids on the wire are the lower-case names.</summary>
public enum TurnIntent
{
    /// <summary>The user wants a thing on screen found and shown: the highlight is the answer.</summary>
    Point,

    /// <summary>The user wants a spoken or written explanation: the Tutor's leg.</summary>
    Explain,

    /// <summary>The user wants to be taught with drawn cards: the lesson leg.</summary>
    Lesson,
}

/// <summary>Which canvas a lesson draws on.</summary>
public enum LessonDimension
{
    /// <summary>The 2D cards (VisualPlan 1.0 ops). Also the safe answer whenever the 3D choice is unsure.</summary>
    Flat,

    /// <summary>The 3D scene window, with <see cref="TurnRoute.ModelId"/> naming the catalogue model.</summary>
    Scene3d,
}

/// <summary>
/// One catalogue model the router may offer. The caller builds these from the Part Library (title and
/// subject are what Jev reads); the router never references the library itself, so it cannot drift
/// into owning 3D content.
/// </summary>
public sealed record RouteModel(string Id, string Title, string Subject);

/// <summary>What the router is asked about one turn.</summary>
public sealed record RouteRequest
{
    /// <summary>The user's committed question. Required; it is quoted into every instruction.</summary>
    public string Question { get; init; } = string.Empty;

    /// <summary>The title of the window the user is looking at, when known. Context only.</summary>
    public string? WindowTitle { get; init; }

    /// <summary>
    /// Whether the locate leg found a matching element on screen, when it has run. A fact computed in
    /// code and handed over as state — Jev is never asked to work it out.
    /// </summary>
    public bool? ScreenMatchFound { get; init; }

    /// <summary>The 3D models a lesson may use. Empty means the dimension and model questions are not asked.</summary>
    public IReadOnlyList<RouteModel> Models { get; init; } = Array.Empty<RouteModel>();

    /// <summary>
    /// Whether the caller can draw a lesson's own simplified 3D model when no listed one fits (DEC-051;
    /// schematics, DEC-048/DEC-050). Off by default: then "3D, but no listed model" is a 2D lesson, as
    /// before. Only asked about when <see cref="Models"/> is not empty.
    /// </summary>
    public bool Schematics { get; init; }
}

/// <summary>
/// The router's verdict. <see cref="Routed"/> false means "no route": the caller keeps the behaviour it
/// had without a router (today, the <c>--lesson</c> flag), and <see cref="Reason"/> says why.
/// </summary>
public sealed record TurnRoute
{
    public bool Routed { get; init; }

    /// <summary>Set when <see cref="Routed"/>.</summary>
    public TurnIntent? Intent { get; init; }

    /// <summary>Set only for <see cref="TurnIntent.Lesson"/>.</summary>
    public LessonDimension? Dimension { get; init; }

    /// <summary>Set only for <see cref="LessonDimension.Scene3d"/>: an id the caller offered, never anything else.</summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// A 3D lesson with no listed model: the lesson draws its own simplified one (DEC-051). Only ever set
    /// with <see cref="LessonDimension.Scene3d"/>, a null <see cref="ModelId"/>, and a request that allowed it.
    /// </summary>
    public bool Schematic { get; init; }

    /// <summary>Why the route is what it is: the refusal, or a downgrade from 3D to 2D. Null when nothing needs saying.</summary>
    public string? Reason { get; init; }

    /// <summary>Scores Jev gave each answer (its confidence, or the chosen option's probability). Null when not asked.</summary>
    public double? IntentScore { get; init; }

    public double? DimensionScore { get; init; }

    public double? ModelScore { get; init; }

    /// <summary>The Jev model that answered, as the provider reported it.</summary>
    public string JevModel { get; init; } = string.Empty;

    public TimeSpan Latency { get; init; }

    public int InputTokens { get; init; }

    public int OutputTokens { get; init; }

    public override string ToString() => !Routed
        ? $"no route ({Reason})"
        : Intent == TurnIntent.Lesson
            ? $"lesson {(Dimension == LessonDimension.Scene3d ? (Schematic ? "3D schematic" : $"3D {ModelId}") : "2D")}"
            : Intent!.Value.ToString().ToLowerInvariant();
}

/// <summary>Router settings.</summary>
public sealed record TurnRouterOptions
{
    /// <summary>The Jev model id sent to the provider. Same pin as Element Selection's (DEC-008).</summary>
    public string ModelId { get; init; } = "jev-1.13.0";

    /// <summary>
    /// Answers scored below this are not acted on. 0.5 is the Medium tier boundary Element Selection and
    /// Vision already share (DEC-009), so "unsure" means the same thing everywhere.
    /// </summary>
    public double MinScore { get; init; } = 0.5;
}
