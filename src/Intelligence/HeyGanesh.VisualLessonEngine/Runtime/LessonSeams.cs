namespace HeyGanesh.VisualLessonEngine.Runtime;

/// <summary>
/// Where a lesson is drawn. Phase 3's WebView2 card surface implements it (DEC-020), routing screen
/// marks to the native ScreenOverlay. It only ever receives beats from a validated plan (DEC-023), so
/// an implementation can trust every id it is given.
/// </summary>
public interface ILessonCanvas
{
    /// <summary>Draws one beat's ops. Expected to take milliseconds; it is never handed a beat with no ops.</summary>
    Task ApplyBeatAsync(LessonBeat beat, CancellationToken cancellationToken);

    /// <summary>Shows a short notice instead of a drawing (the text-fallback path).</summary>
    Task ShowNoticeAsync(string notice, CancellationToken cancellationToken);

    /// <summary>Wipes the cards so a play or replay starts blank.</summary>
    Task ClearAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Who says a beat's sentence. The App Shell adapts Voice's TTS to it. The returned task completes
/// when the sentence has finished playing — that completion is the lesson's clock.
/// </summary>
public interface ILessonSpeaker
{
    Task SpeakAsync(string text, CancellationToken cancellationToken);
}
