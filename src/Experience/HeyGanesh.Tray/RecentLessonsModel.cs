using System.Globalization;
using System.Text;
using HeyGanesh.Notes;

namespace HeyGanesh.Tray;

/// <summary>One line of the Recent lessons list.</summary>
internal sealed record LessonRow(long Id, string When, string Question, string Taught, bool CanReplay);

/// <summary>
/// What the Recent lessons window shows and does, with no UI (DEC-060), so every rule is checked offline (RL-1). It
/// reads the notes store; it never shows a plan's JSON, only what was asked and taught.
/// </summary>
internal sealed class RecentLessonsModel : IDisposable
{
    /// <summary>How many turns the list shows (the store keeps more).</summary>
    public const int Shown = 200;

    private readonly NotesStore _store;

    public RecentLessonsModel(NotesStore store) => _store = store;

    /// <summary>The newest turns first.</summary>
    public IReadOnlyList<LessonRow> Rows() => _store.Recent(Shown).Select(n => new LessonRow(
        n.Id,
        n.At.ToLocalTime().ToString("ddd d MMM, HH:mm", CultureInfo.CurrentCulture),
        n.Question,
        Taught(n),
        !string.IsNullOrWhiteSpace(n.LessonPlan))).ToList();

    /// <summary>The whole turn, in words, for the details pane. Empty for an id that is gone.</summary>
    public string Detail(long id)
    {
        if (_store.Get(id) is not { } n)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        text.AppendLine($"Asked: {n.Question}");
        text.AppendLine($"When: {n.At.ToLocalTime().ToString("dddd d MMMM yyyy, HH:mm", CultureInfo.CurrentCulture)}");
        text.AppendLine(n.Found == true
            ? $"On screen: {n.Element ?? "found"} ({n.Confidence} confidence, {n.VerdictSource})"
            : "On screen: nothing matched");
        switch (n.Leg)
        {
            case NoteLeg.Lesson:
                text.AppendLine($"Taught with a lesson: {n.LessonTitle ?? "(untitled)"}");
                text.AppendLine(string.IsNullOrWhiteSpace(n.LessonPlan) ? "This lesson was not stored, so it cannot be replayed." : "Replay draws and speaks it again, with no model call.");
                if (n.Picks.Count > 0)
                {
                    text.AppendLine($"Parts you clicked: {string.Join(", ", n.Picks)}");
                }

                break;
            case NoteLeg.Tutor:
                text.AppendLine("Explained:");
                text.AppendLine(n.Answer ?? "(no answer was produced)");
                break;
            default:
                text.AppendLine("Shown on screen: the highlight was the answer.");
                break;
        }

        text.Append($"Outcome: {n.Outcome}");
        return text.ToString();
    }

    /// <summary>The stored plan for <see cref="Rows"/>' replayable rows; null otherwise.</summary>
    public string? PlanJson(long id) => _store.Get(id)?.LessonPlan;

    public bool Delete(long id) => _store.Delete(id);

    public int Clear() => _store.Clear();

    public void Dispose() => _store.Dispose();

    private static string Taught(TurnNote n) => n.Leg switch
    {
        NoteLeg.Lesson => n.LessonTitle is { } title ? $"Lesson: {title}" : "Lesson",
        NoteLeg.Tutor => n.Answer is { } answer ? $"Explained: {Short(answer)}" : "Explained",
        _ => n.Element is { } element ? $"Shown: {element}" : "Shown on screen",
    };

    private static string Short(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= 60 ? line : line[..59].TrimEnd() + "…";
    }
}
