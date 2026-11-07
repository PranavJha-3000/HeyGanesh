namespace HeyGanesh.Notes;

/// <summary>Which teaching leg a turn ran (the App Shell's TurnLeg, kept as text so this module references nothing).</summary>
public enum NoteLeg
{
    /// <summary>Only the highlight: the answer was where something is on screen.</summary>
    Highlight,

    /// <summary>The Tutor's spoken explanation.</summary>
    Tutor,

    /// <summary>A drawn, spoken lesson.</summary>
    Lesson,
}

/// <summary>
/// One turn, as kept for review and replay (DEC-058). Only what was asked and taught: no screenshot, no screen text
/// and no key is ever part of a note. <see cref="Id"/> is assigned by the store; a note being recorded leaves it 0.
/// </summary>
public sealed record TurnNote
{
    public long Id { get; init; }

    /// <summary>When the turn ended (stored as UTC).</summary>
    public DateTimeOffset At { get; init; }

    /// <summary>The run of the app the turn belongs to, so a session's turns can be listed together.</summary>
    public string Session { get; init; } = string.Empty;

    /// <summary>The transcribed (or scripted) question. Required.</summary>
    public string Question { get; init; } = string.Empty;

    public NoteLeg Leg { get; init; }

    /// <summary>The route in words (for example "lesson 3D four-stroke-engine"), or null when no router ran.</summary>
    public string? Route { get; init; }

    /// <summary>Which leg answered the locate question (ElementSelection or Vision).</summary>
    public string? VerdictSource { get; init; }

    public bool? Found { get; init; }

    /// <summary>The located element's description, when one was found.</summary>
    public string? Element { get; init; }

    public string? Confidence { get; init; }

    /// <summary>The Tutor's answer text, for a Tutor turn.</summary>
    public string? Answer { get; init; }

    public string? LessonTitle { get; init; }

    /// <summary>
    /// What a lesson drew, so it can be replayed without the model. Since DEC-076 a drawing program in
    /// <see cref="StoredDrawing"/>'s form; older rows hold a VisualPlan's JSON, which can no longer be replayed.
    /// </summary>
    public string? LessonPlan { get; init; }

    /// <summary>The parts clicked for follow-ups, as "instance.part".</summary>
    public IReadOnlyList<string> Picks { get; init; } = Array.Empty<string>();

    /// <summary>How the turn ended, in words (for example "Played" or "NoPick").</summary>
    public string Outcome { get; init; } = string.Empty;
}

/// <summary>
/// How a drawing program is kept in <see cref="TurnNote.LessonPlan"/> (DEC-076): a first line <c>drawing:WxH</c>
/// (the canvas the program was written for, in pixels), then the program exactly as drawn. No schema change: anything
/// without the marker is a lesson from before the drawing stage (VisualPlan JSON) and is not a drawing.
/// </summary>
public static class StoredDrawing
{
    public const string Marker = "drawing:";

    /// <summary>The largest canvas side accepted when reading back (a stored size is data, not trusted).</summary>
    public const int MaxSide = 16384;

    public static string Store(int width, int height, string program)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(program);
        if (width is < 1 or > MaxSide || height is < 1 or > MaxSide)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"a canvas side must be 1..{MaxSide}");
        }

        return $"{Marker}{width}x{height}\n{program}";
    }

    /// <summary>True for a stored drawing with a sane canvas and a program; false for anything else, older rows included.</summary>
    public static bool TryRead(string? stored, out int width, out int height, out string program)
    {
        width = height = 0;
        program = string.Empty;
        if (stored is null || !stored.StartsWith(Marker, StringComparison.Ordinal))
        {
            return false;
        }

        var end = stored.IndexOf('\n');
        var size = (end < 0 ? stored : stored[..end])[Marker.Length..].Split('x');
        if (end < 0 || size.Length != 2
            || !int.TryParse(size[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out width)
            || !int.TryParse(size[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out height)
            || width is < 1 or > MaxSide || height is < 1 or > MaxSide
            || string.IsNullOrWhiteSpace(stored[(end + 1)..]))
        {
            width = height = 0;
            return false;
        }

        program = stored[(end + 1)..];
        return true;
    }
}

/// <summary>Store settings.</summary>
public sealed record NotesOptions
{
    /// <summary>How many turns are kept; the oldest go first. 1,000 is the plan's placeholder until the owner picks.</summary>
    public int Keep { get; init; } = 1000;

    /// <summary>How long a write waits while another process (the tray, the console exe) holds the database.</summary>
    public int BusyTimeoutMs { get; init; } = 5000;
}

/// <summary>A store failure: the database could not be opened, read or written. Never carries a note's text.</summary>
public sealed class NotesException : Exception
{
    public NotesException(string message, int code)
        : base(message) => Code = code;

    /// <summary>The SQLite result code, or -1 when the failure was not SQLite's.</summary>
    public int Code { get; }
}
