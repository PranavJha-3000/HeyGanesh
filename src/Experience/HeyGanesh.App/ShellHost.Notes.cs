using HeyGanesh.Abstractions;
using HeyGanesh.Notes;
using HeyGanesh.TutorEngine;
using HeyGanesh.Voice;

namespace HeyGanesh.App;

// Notes (DEC-058/DEC-059): after each turn that was taught from a produced verdict, one note goes into the local
// notes store: the question, the leg, the verdict in words, and what was taught. Never a screenshot or a key. The
// store is opened once per run; a store that cannot open, or a write that fails, is a line in the output and
// never fails the turn. --no-notes keeps the store closed.

internal static partial class ShellHost
{
    /// <summary>The run's store, or null when notes are off. Set once by RunAsync before the header.</summary>
    private static NotesStore? Notes;

    /// <summary>What the header's notes line says; kept in step with <see cref="OpenNotes"/>.</summary>
    private static string NotesState = "off";

    /// <summary>This run's last taught turns, in memory only (DEC-061): what the Tutor and the planner see as context.</summary>
    private static readonly SessionTurns Session = new();

    /// <summary>One id per run, so a run's turns can be listed together.</summary>
    private static readonly string NotesSession = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}";

    /// <summary>Opens the store at the default path, or says why notes are off. Never throws.</summary>
    private static NotesStore? OpenNotes(ShellOptions options)
    {
        if (options.NoNotes)
        {
            NotesState = "off (--no-notes)";
            return null;
        }

        try
        {
            var store = NotesStore.Open(NotesStore.DefaultPath());
            NotesState = store.MovedAside is { } aside
                ? $"ON ({store.Path}); an unreadable old file was kept as {Path.GetFileName(aside)}"
                : $"ON ({store.Path})";
            return store;
        }
        catch (Exception ex) when (ex is NotesException or IOException or UnauthorizedAccessException)
        {
            NotesState = $"off ({ex.Message})";
            return null;
        }
    }

    /// <summary>Teaches (TeachAsync, unchanged), then records what was taught. The exit code is TeachAsync's.</summary>
    private static async Task<int> TeachAndNoteAsync(
        TutorService? tutor,
        LessonLeg? lesson,
        Task<TurnRouter.TurnRoute?>? routing,
        VoiceService voice,
        ShellOptions options,
        GuidanceResult guidance,
        CancellationToken ct)
    {
        var record = new TurnRecord();
        var code = await TeachAsync(tutor, lesson, routing, voice, options, guidance, record, ct).ConfigureAwait(false);
        RecordNote(options, guidance, record, code);
        return code;
    }

    private static void RecordNote(ShellOptions options, GuidanceResult guidance, TurnRecord record, int code)
    {
        var note = TurnNotes.From(options.Question, NotesSession, DateTimeOffset.Now, guidance, record, code);
        if (!options.NoContext)
        {
            // Kept whether or not notes are on: context is this run's memory, notes are what is saved to disk.
            Session.Add(note);
        }

        if (Notes is null || note is null)
        {
            return;
        }

        try
        {
            Console.WriteLine($"notes       : saved #{Notes.Record(note)}");
        }
        catch (Exception ex) when (ex is NotesException or IOException or ObjectDisposedException)
        {
            Console.WriteLine($"notes       : not saved ({ex.GetType().Name}: {ex.Message}); the turn is unaffected");
        }
    }
}

/// <summary>What the teaching legs report for the turn's note. Filled as the turn goes; read once at the end.</summary>
internal sealed class TurnRecord
{
    public NoteLeg Leg { get; set; } = NoteLeg.Tutor;

    /// <summary>The leg choice in words (TurnLegs' reason).</summary>
    public string? Route { get; set; }

    public string? Answer { get; set; }

    public string? LessonTitle { get; set; }

    /// <summary>The first lesson's plan JSON (PlanResult.PlanJson), for replay.</summary>
    public string? PlanJson { get; set; }

    /// <summary>Follow-up clicks, as "instance.part".</summary>
    public List<string> Picks { get; } = new();

    public string? Outcome { get; set; }
}

/// <summary>
/// A finished turn as a note (DEC-059). Pure, so every case is checked offline (TraySmoke --assert-notes-map). No note
/// for a key error (12) or a cancelled turn (13), for a Failed verdict (never taught from), or for a turn with no
/// question.
/// </summary>
internal static class TurnNotes
{
    public static TurnNote? From(string question, string session, DateTimeOffset at, GuidanceResult guidance, TurnRecord record, int exitCode)
    {
        if (exitCode is 12 or 13 || guidance.Failed || string.IsNullOrWhiteSpace(question))
        {
            return null;
        }

        return new TurnNote
        {
            At = at,
            Session = session,
            Question = question.Trim(),
            Leg = record.Leg,
            Route = record.Route,
            VerdictSource = guidance.Source.ToString(),
            Found = guidance.Found,
            Element = guidance.Found ? guidance.Description : null,
            Confidence = guidance.Confidence.ToString(),
            Answer = record.Answer,
            LessonTitle = record.LessonTitle,
            LessonPlan = record.PlanJson,
            Picks = record.Picks.ToArray(),
            Outcome = record.Outcome ?? $"exit {exitCode}",
        };
    }
}

/// <summary>
/// This run's last <see cref="Keep"/> taught turns (DEC-061), so the Tutor and the planner can understand a follow-up
/// ("and the exhaust stroke?"). In memory only, gone when the run ends; a turn that taught nothing (a skipped leg) is
/// not kept. Pure apart from its lock, so every rule is checked offline (TraySmoke --assert-session).
/// </summary>
internal sealed class SessionTurns
{
    public const int Keep = 3;

    private readonly object _gate = new();
    private readonly List<(string Question, string Taught)> _turns = new();

    /// <summary>Keeps a finished turn's question and what it taught; ignores a turn with no note or nothing taught.</summary>
    public void Add(TurnNote? note)
    {
        if (note is null || TaughtLine(note) is not { } taught)
        {
            return;
        }

        lock (_gate)
        {
            _turns.Add((note.Question, taught));
            while (_turns.Count > Keep)
            {
                _turns.RemoveAt(0);
            }
        }
    }

    /// <summary>The kept turns, oldest first.</summary>
    public IReadOnlyList<(string Question, string Taught)> Snapshot()
    {
        lock (_gate)
        {
            return _turns.ToArray();
        }
    }

    /// <summary>What a turn taught, in a line; null when it taught nothing (a skipped Tutor or lesson).</summary>
    internal static string? TaughtLine(TurnNote note) => note.Leg switch
    {
        NoteLeg.Tutor => note.Answer,
        NoteLeg.Lesson => note.LessonTitle is { } title ? $"a drawn lesson: {title}" : null,
        _ => note.Element is { } element ? $"showed the {element} on screen" : "showed something on screen",
    };
}
