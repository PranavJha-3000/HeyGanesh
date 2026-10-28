using HeyGanesh.LessonSurface;
using HeyGanesh.ScreenOverlay;
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
