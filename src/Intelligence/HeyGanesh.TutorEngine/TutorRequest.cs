namespace HeyGanesh.TutorEngine;

/// <summary>
/// One teaching turn: the question (the teaching task, always primary) plus supplemental screen
/// grounding that may be entirely absent.
/// </summary>
/// <remarks>
/// The ordering is the whole contract of this type: <see cref="Question"/> is what to teach, and
/// <see cref="Context"/> is what happened to be on screen when it was asked. A caller with nothing
/// resolved sends <see cref="TutorContext.Empty"/> and still gets a real academic/technical answer —
/// the Tutor is a tutor, not a caption engine (DEC-015).
/// </remarks>
public sealed record TutorRequest
{
    /// <summary>
    /// What the learner asked, verbatim from wherever it came (a committed Voice transcript, a typed
    /// line). Required; whitespace-only is refused at the service boundary as a caller bug, the same
    /// stance Voice takes toward an empty <c>SpeakRequest.Text</c>.
    /// </summary>
    public required string Question { get; init; }

    /// <summary>Supplemental grounding. Never null (use <see cref="TutorContext.Empty"/>), never load-bearing.</summary>
    public TutorContext Context { get; init; } = TutorContext.Empty;

    /// <summary>
    /// This session's earlier turns, oldest first (DEC-061), so a follow-up ("and the exhaust stroke?") can be
    /// understood. Untrusted: they are earlier questions and earlier model output, sent fenced as context, never as
    /// instructions. Only the last <see cref="TutorEarlierTurn.MaxTurns"/> are sent. Empty: the prompt is unchanged.
    /// </summary>
    public IReadOnlyList<TutorEarlierTurn> EarlierTurns { get; init; } = Array.Empty<TutorEarlierTurn>();

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Question))
        {
            throw new ArgumentException(
                "TutorRequest.Question is the teaching task and cannot be empty. " +
                "With no question there is nothing to teach and nothing to ground.",
                nameof(Question));
        }

        Context.ValidateStructure();
        Context.Visual?.ValidateStructure();
    }
}

/// <summary>One earlier turn of the session: what was asked, and what was taught in a line or two (DEC-061).</summary>
public sealed record TutorEarlierTurn(string Question, string Taught)
{
    /// <summary>How many earlier turns a prompt carries, the newest kept.</summary>
    public const int MaxTurns = 3;

    /// <summary>Caps per turn, in characters: a question, and what was taught.</summary>
    public const int MaxQuestionChars = 200;
    public const int MaxTaughtChars = 300;
}
