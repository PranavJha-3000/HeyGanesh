namespace HeyGanesh.Voice;

/// <summary>
/// What to say. Built by whoever holds the text — today that is the host, tomorrow the Tutor Engine —
/// and handed to <see cref="VoiceService.SpeakAsync"/>.
/// </summary>
/// <remarks>
/// Deliberately just text plus a voice choice: the module has no opinion about what should be spoken,
/// and no field here could carry an API key or a credential even by accident.
/// </remarks>
public sealed record SpeakRequest
{
    /// <summary>The text to speak. Required; empty text is refused rather than sent.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Provider voice/model reference (Fish Audio calls it <c>reference_id</c>). Null keeps the
    /// provider default configured in <see cref="FishTtsOptions"/>.
    /// </summary>
    public string? VoiceId { get; init; }
}
