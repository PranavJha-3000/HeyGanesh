namespace HeyGanesh.Voice;

/// <summary>Interim (not yet final) user speech, for diagnostics and future UI. Never used as the question.</summary>
public sealed class VoicePartialEventArgs : EventArgs
{
    public VoicePartialEventArgs(string text) => Text = text;

    /// <summary>The partial transcript so far. Recognised by the frontend, not yet committed.</summary>
    public string Text { get; }
}

/// <summary>
/// The speech-in seam — the single boundary between HeyGanesh and whatever recognises the user's voice.
/// </summary>
/// <remarks>
/// <para>
/// This is the swappable point: today <see cref="QwenGatewayClient"/> talks to a qwen-audio-agent
/// Gateway; a local speech-to-text engine behind the same interface would need no change above it, and
/// a fake implementation is how the whole module is verified offline.
/// </para>
/// <para>
/// The contract is one utterance per call, never a stream of sessions. Implementations must return
/// <see cref="UtteranceResult.IsNoSpeech"/> for silence and throw the typed
/// <see cref="VoiceClientException"/> family for transport/auth failures — the two must not be merged,
/// or a dead provider reads as a silent user.
/// </para>
/// </remarks>
public interface IVoiceListener : IDisposable
{
    /// <summary>
    /// Interim user speech while a listen is in flight. Raised on an arbitrary thread and never raised
    /// for assistant-role text; subscribers must marshal for UI.
    /// </summary>
    event EventHandler<VoicePartialEventArgs>? Partial;

    /// <summary>
    /// Listens for exactly one utterance and returns its verdict. Never returns null.
    /// </summary>
    /// <exception cref="OperationCanceledException">The caller's token fired; no verdict was produced.</exception>
    /// <exception cref="VoiceAuthenticationException">The frontend rejected our credentials.</exception>
    /// <exception cref="VoiceProtocolException">The peer broke the protocol, or ran a Backend Agent we refused.</exception>
    /// <exception cref="VoiceClientException">Any other transport/provider/device failure.</exception>
    Task<UtteranceResult> ListenOnceAsync(CancellationToken cancellationToken);
}
