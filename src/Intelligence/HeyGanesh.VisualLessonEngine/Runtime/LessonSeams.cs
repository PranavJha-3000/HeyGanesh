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

    /// <summary>
    /// Wipes the cards and lays out one per declared surface (id + title), so a play or replay starts
    /// blank. Called once before the first beat; a beat alone does not carry its card's title.
    /// </summary>
    Task ResetAsync(IReadOnlyList<LessonSurface> surfaces, CancellationToken cancellationToken);
}

/// <summary>
/// Who says a beat's sentence. The App Shell adapts Voice's TTS to it. The returned task completes
/// when the sentence has finished playing — that completion is the lesson's clock.
/// </summary>
public interface ILessonSpeaker
{
    Task SpeakAsync(string text, CancellationToken cancellationToken);
}

/// <summary>
/// One part the student clicked in a 3D scene: the scene instance id the lesson placed, the catalogue
/// model it is, and the part id within that model. Every id was checked by the canvas against what it
/// drew and the catalogue before this record exists (DEC-031).
/// </summary>
public sealed record PartPick(string InstanceId, string ModelId, string PartId);

/// <summary>
/// Optional: a canvas that can let the student click a part of the 3D scene it drew (FEATURE-007
/// Phase 5 step 5). The lesson session asks for one pick after a lesson has finished playing and turns
/// it into a follow-up question; a canvas without a scene, or without this interface, is never asked.
/// </summary>
public interface IPartPicker
{
    /// <summary>
    /// Turns picking on, waits for one valid click, and turns it off again. Returns null when the
    /// student closed the lesson, when <paramref name="idle"/> passed with no click, or when the canvas
    /// has no scene on show. Cancelling <paramref name="cancellationToken"/> throws.
    /// </summary>
    Task<PartPick?> PickAsync(TimeSpan idle, CancellationToken cancellationToken);
}
