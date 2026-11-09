namespace HeyGanesh.LessonPlanner;

/// <summary>One earlier turn of the session: what was asked, and what was taught in a line or two (DEC-061).</summary>
public sealed record EarlierTurn(string Question, string Taught)
{
    /// <summary>How many earlier turns a prompt carries, the newest kept.</summary>
    public const int MaxTurns = 3;

    /// <summary>Caps per turn, in characters: a question, and what was taught.</summary>
    public const int MaxQuestionChars = 200;
    public const int MaxTaughtChars = 300;
}
