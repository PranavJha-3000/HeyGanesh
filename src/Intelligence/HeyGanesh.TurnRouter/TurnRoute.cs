namespace HeyGanesh.TurnRouter;

/// <summary>How a turn is taught. Option ids on the wire are the lower-case names.</summary>
public enum TurnIntent
{
    /// <summary>The user wants a thing on screen found and shown: the highlight is the answer.</summary>
    Point,

    /// <summary>The user wants a spoken or written explanation: the Tutor's leg.</summary>
    Explain,

    /// <summary>The user wants to be taught with a spoken drawing: the lesson leg. The drawing picks 2D or 3D itself (DEC-075).</summary>
    Lesson,
}

/// <summary>What the router is asked about one turn.</summary>
public sealed record RouteRequest
{
    /// <summary>The user's committed question. Required; it is quoted into the instruction.</summary>
    public string Question { get; init; } = string.Empty;

    /// <summary>The title of the window the user is looking at, when known. Context only.</summary>
    public string? WindowTitle { get; init; }

    /// <summary>
    /// Whether the locate leg found a matching element on screen, when it has run. A fact computed in
    /// code and handed over as state — Jev is never asked to work it out.
    /// </summary>
    public bool? ScreenMatchFound { get; init; }
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

    /// <summary>Why the route was refused. Null when nothing needs saying.</summary>
    public string? Reason { get; init; }

    /// <summary>Jev's score for the intent (its confidence, or the chosen option's probability).</summary>
    public double? IntentScore { get; init; }

    /// <summary>The Jev model that answered, as the provider reported it.</summary>
    public string JevModel { get; init; } = string.Empty;

    public TimeSpan Latency { get; init; }

    public int InputTokens { get; init; }

    public int OutputTokens { get; init; }

    public override string ToString() => !Routed ? $"no route ({Reason})" : Intent!.Value.ToString().ToLowerInvariant();
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
