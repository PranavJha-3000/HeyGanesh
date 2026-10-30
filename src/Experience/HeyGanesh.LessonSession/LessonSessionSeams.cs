using HeyGanesh.LessonSurface;
using HeyGanesh.PartLibrary;
using HeyGanesh.ScreenOverlay;
using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.LessonSession;

/// <summary>
/// Adapts any "say this text, finish when it has been said" function to <see cref="ILessonSpeaker"/>.
/// The shell hands in Voice's SpeakAsync, so this assembly never references Voice (DEC-016: Voice
/// knows nothing but text) and the speaker's completion stays the lesson's clock.
/// </summary>
public sealed class DelegateLessonSpeaker : ILessonSpeaker
{
    private readonly Func<string, CancellationToken, Task> _speak;

    public DelegateLessonSpeaker(Func<string, CancellationToken, Task> speak) =>
        _speak = speak ?? throw new ArgumentNullException(nameof(speak));

    public Task SpeakAsync(string text, CancellationToken cancellationToken) => _speak(text, cancellationToken);
}

/// <summary>
/// The product canvas host: a fresh <see cref="CardSurface"/> per turn, closed when the turn ends, so
/// no card outlives the lesson that opened it. Screen marks go to <paramref name="marks"/> when given
/// (the shell passes its overlay, which is idle by the time a lesson plays).
/// </summary>
public sealed class CardSurfaceHost : ILessonCanvasHost
{
    private readonly CardSurfaceOptions? _options;
    private readonly ScreenOverlayService? _marks;

    public CardSurfaceHost(CardSurfaceOptions? options = null, ScreenOverlayService? marks = null)
    {
        _options = options;
        _marks = marks;
    }

    public async Task<LessonCanvasLease> OpenAsync(CancellationToken cancellationToken)
    {
        var surface = await CardSurface.CreateAsync(_options, _marks, cancellationToken).ConfigureAwait(false);
        return new LessonCanvasLease(surface, surface);
    }
}

/// <summary>Bounds on click-to-ask follow-ups (DEC-031).</summary>
public sealed class FollowUpOptions
{
    /// <summary>Follow-up lessons after the first; the canvas stops offering clicks after this many.</summary>
    public int MaxFollowUps { get; init; } = 3;

    /// <summary>How long a finished 3D lesson waits for a click before the cards close.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public static FollowUpOptions Default { get; } = new();
}

/// <summary>
/// The one place a clicked part becomes a question (DEC-031). A library part's question is written from the
/// Part Library's own vetted name and description, never from anything the page said. Since DEC-057 a part
/// of the lesson's own schematic is asked about too. Its name and title were written by the model, so they are
/// cleaned to letters, digits and plain punctuation, capped, and the question says the part is from a
/// simplified drawing, not the library. The page's ids never reach the question. Null for a part neither
/// the library nor the lesson's schematics have.
/// </summary>
public static class PartQuestion
{
    /// <summary>Longest cleaned name or title put into a question; the validator's own caps are 40 and 60.</summary>
    private const int MaxNameChars = 40;
    private const int MaxTitleChars = 60;

    public static string? For(PartPick pick) => For(pick, Array.Empty<SchematicModel>());

    /// <param name="schematics">The schematics of the lesson that was shown (its validated plan's).</param>
    public static string? For(PartPick pick, IReadOnlyList<SchematicModel> schematics)
    {
        ArgumentNullException.ThrowIfNull(pick);
        ArgumentNullException.ThrowIfNull(schematics);
        var model = ModelLibrary.Find(pick.ModelId);
        var part = model?.Parts.FirstOrDefault(p => p.Id == pick.PartId);
        if (model is not null && part is not null)
        {
            return $"Tell me more about the {Lower(part.Name)} in the {Lower(model.Title)}. The part library describes it as: {part.Description}";
        }

        var drawn = schematics.FirstOrDefault(s => s.Id == pick.ModelId);
        var drawnPart = drawn?.Parts.FirstOrDefault(p => p.Id == pick.PartId);
        var name = Clean(drawnPart?.Name, MaxNameChars);
        var title = Clean(drawn?.Title, MaxTitleChars);
        if (name is null || title is null)
        {
            return null;
        }

        return $"Tell me more about the {Lower(name)} in the {Lower(title)}. It is a part of the simplified drawing in the last lesson, " +
               "not of the part library, so explain what it is and what it does in general terms.";
    }

    /// <summary>Letters, digits, spaces and plain punctuation only, whitespace collapsed, capped; null when nothing is left.</summary>
    private static string? Clean(string? text, int max)
    {
        if (text is null)
        {
            return null;
        }

        var kept = new string(text.Select(c => char.IsLetterOrDigit(c) || " -'(),.".Contains(c) ? c : ' ').ToArray());
        var cleaned = string.Join(' ', kept.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length > max)
        {
            cleaned = cleaned[..max].TrimEnd();
        }

        return cleaned.Length == 0 ? null : cleaned;
    }

    // "Connecting rod" reads as "the connecting rod"; "DNA double helix" keeps its capitals.
    private static string Lower(string name) =>
        name.Length > 1 && char.IsUpper(name[0]) && char.IsLower(name[1]) ? char.ToLowerInvariant(name[0]) + name[1..] : name;
}
