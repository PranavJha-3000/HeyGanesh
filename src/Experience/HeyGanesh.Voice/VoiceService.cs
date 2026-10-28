namespace HeyGanesh.Voice;

/// <summary>
/// Public API of the Voice pipeline: one utterance in, one spoken answer out.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this module is for.</b> It is a seam, not a teacher. It turns microphone audio into the user's
/// text and turns text into speech, and it is deliberately ignorant of what either means: it never
/// classifies intent, never generates an answer, and never decides what is worth saying. The future
/// Tutor Engine (and the Jev gate before it) sits between <see cref="ListenOnceAsync"/> and
/// <see cref="SpeakAsync"/>, which is why those two methods are shaped as a matched pair of plain
/// text-carrying calls.
/// </para>
/// <para>
/// <b>Nothing here answers the user.</b> The Qwen realtime frontend that recognises the speech is a
/// conversational model and generates its own reply to the same utterance; HeyGanesh cancels and discards
/// those replies and keeps only the user's transcript (DEC-013 records that as the unavoidable protocol
/// limitation, and <c>GatewaySuppressionLedger</c> counts what was suppressed). That is what makes the
/// future Tutor authoritative rather than decorative.
/// </para>
/// <para>
/// <b>One voice session at a time.</b> A second listen, or a speak, while either is in flight is refused
/// rather than queued — the same stance the Context Orchestrator and Screen Overlay take for their
/// sessions. Listen and speak are mutually exclusive as well: an open microphone pointed at a speaker
/// transcribes HeyGanesh's own voice, which would turn a spoken answer into the next question.
/// </para>
/// <para>
/// <b>Threading.</b> Safe to call from any thread, including the Global Hotkey Monitor's worker thread.
/// Both entry points return immediately with a task and do no work inline, so a hook callback is never blocked.
/// </para>
/// <para>
/// <b>Ownership.</b> Seams passed to the constructor are caller-owned and never disposed here; a seam this
/// service created itself is disposed with it. Cancellation follows the repo's rule: it propagates as
/// <see cref="OperationCanceledException"/> and never becomes a fabricated verdict.
/// </para>
/// </remarks>
public sealed partial class VoiceService : IDisposable
{
    /// <summary>How speech-in identifies itself in results. A stable label, never a URL and never a key.</summary>
    internal const string QwenFrontendId = "qwen-audio-agent-gateway";

    private readonly IVoiceListener _listener;
    private readonly ITtsClient _tts;
    private readonly IAudioOutput _output;
    private readonly VoiceOptions _options;

    private readonly bool _ownsListener;
    private readonly bool _ownsTts;
    private readonly bool _ownsOutput;

    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();

    // In-flight work, one slot each. Both are read and written under _gate; that is what makes the
    // single-session rule auditable instead of hopeful.
    private Task<UtteranceResult>? _listen;
    private Task<SpeakResult>? _speak;
    private bool _disposed;

    /// <summary>
    /// Creates the service. Every argument is optional: with none, this is the real pipeline — a
    /// <see cref="QwenGatewayClient"/> listening through the configured qwen-audio-agent Gateway, a
    /// <see cref="FishTtsClient"/> speaking, and <see cref="WinMmAudioOutput"/> playing. Pass a seam to
    /// redirect it (a proxy, a different provider, or a recording fake in a verification host).
    /// </summary>
    public VoiceService(
        IVoiceListener? listener = null,
        ITtsClient? tts = null,
        IAudioOutput? output = null,
        VoiceOptions? options = null)
    {
        _options = options ?? VoiceOptions.Default;
        _options.Validate();

        if (listener is null)
        {
            _listener = new QwenGatewayClient(voiceOptions: _options);
            _ownsListener = true;
        }
        else
        {
            _listener = listener;
        }

        if (tts is null)
        {
            _tts = new FishTtsClient();
            _ownsTts = true;
        }
        else
        {
            _tts = tts;
        }

        if (output is null)
        {
            _output = new WinMmAudioOutput();
            _ownsOutput = true;
        }
        else
        {
            _output = output;
        }

        // Re-raised here so a host has exactly one place to subscribe, whichever listener is in use.
        _listener.Partial += OnListenerPartial;
    }

    /// <summary>Interim user speech, forwarded from the listener. Diagnostics only — never the question.</summary>
    public event EventHandler<VoicePartialEventArgs>? Partial;

    /// <summary>
    /// The listener in use, exposed so a host can reach implementation-specific diagnostics (the
    /// suppression counters on <see cref="QwenGatewayClient"/>) without this service proxying each one.
    /// </summary>
    public IVoiceListener Listener => _listener;

    /// <summary>The options this service is operating under (budgets, timeouts).</summary>
    public VoiceOptions Options => _options;

    private void OnListenerPartial(object? sender, VoicePartialEventArgs e) => Partial?.Invoke(this, e);

    /// <summary>
    /// The refusal used whenever a second session is attempted. Kept in one place so every caller reads
    /// the same reason.
    /// </summary>
    private static InvalidOperationException Refuse(string requested, string inFlight) =>
        new($"A {inFlight} is already in flight, so this {requested} is refused rather than queued. " +
            "One voice session at a time: listen and speak are mutually exclusive because an open " +
            "microphone would transcribe HeyGanesh's own speech back into the next question.");

    /// <summary>
    /// Reserves the single session slot. Called inside the <c>_gate</c> lock by both entry points, so the
    /// one-session rule cannot be raced.
    /// </summary>
    private void EnterSession(string requested)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_listen is { IsCompleted: false })
        {
            throw Refuse(requested, "listen");
        }

        if (_speak is { IsCompleted: false })
        {
            throw Refuse(requested, "speak");
        }
    }

    /// <summary>
    /// Cancels any in-flight session and refuses further ones. Injected seams are NOT disposed
    /// (caller-owned — the same rule the Context Orchestrator applies to its legs); a session in flight is
    /// not awaited here because it may be unwinding on this very thread.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _listener.Partial -= OnListenerPartial;

        // Cancel first, so an in-flight listen/speak stops talking to its provider before devices close.
        _lifetime.Cancel();

        if (_ownsListener) _listener.Dispose();
        if (_ownsTts) _tts.Dispose();
        if (_ownsOutput) _output.Dispose();

        _lifetime.Dispose();
    }
}
