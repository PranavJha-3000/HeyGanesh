using System.Diagnostics;
using HeyGanesh.Abstractions;
using HeyGanesh.ElementSelection;
using HeyGanesh.ScreenCapture;
using HeyGanesh.Vision;

namespace HeyGanesh.ContextOrchestrator;

/// <summary>
/// The phase of a guidance session. Diagnostics, not a gate: nothing in the pipeline branches on it.
/// </summary>
/// <remarks>
/// <see cref="Resolved"/> persists after a session finishes (it is the last thing the session did) and
/// is overwritten by the next session. <see cref="Idle"/> therefore means "no session has run yet",
/// while <see cref="ContextOrchestratorService.IsSessionActive"/> is the authoritative busy flag.
/// </remarks>
public enum OrchestratorPhase
{
    /// <summary>No session has run yet.</summary>
    Idle = 0,

    /// <summary>The session is acquiring its frames — before either coordinate leg runs.</summary>
    Capturing = 1,

    /// <summary>The PRIMARY leg (Element Selection) is enumerating and choosing a candidate.</summary>
    Selecting = 2,

    /// <summary>The primary leg failed; the FALLBACK leg (Vision) is grounding on the captured frame.</summary>
    VisionFallback = 3,

    /// <summary>The session produced a verdict (found, not found, or failed) and returned it.</summary>
    Resolved = 4,

    /// <summary>The primary leg found an element at Low confidence; Vision is checking it on the captured frame (DEC-037).</summary>
    CheckingConfidence = 5,
}

/// <summary>
/// Context Orchestrator — the top-level session state machine. One hotkey trigger becomes one
/// session: capture the screen once, try the PRIMARY coordinate path (Element Selection), fall back to
/// Vision only on a genuine failure, and return a single normalized verdict.
/// </summary>
/// <remarks>
/// <para><b>Composition (DEC-008).</b> Element Selection is primary, Vision is the fallback. The two
/// modules never reference each other; this service is the only place that holds both, and it decides
/// to fall back on the legs' own documented signal: <c>SelectionResult.Failed</c> (no resolvable
/// window, empty interactive tree, UIA access denied, Jev returned no choice, or Jev chose an id that
/// was never offered). A valid "nothing matches" answer is <b>not</b> a failure and does not fall
/// back — re-asking a pixel model cannot improve an honest no-match, and doing so would cost money,
/// time and a cloud round-trip of the user's screen for nothing.</para>
///
/// <para><b>Normalization (DEC-009).</b> <see cref="SelectionResult"/> and <see cref="VisionResult"/>
/// stay separate types; their verdicts are mapped into <see cref="GuidanceResult"/> here and nowhere
/// else. The mapping is total and lossless — Found, description, box/center in global physical pixels,
/// the 0.8/0.5 tier, and every raw signal both legs expose. No threshold is applied and no tier is
/// gated: that decision belongs to the future Jev confidence-gate.</para>
///
/// <para><b>One capture per session.</b> The frame is taken up front even though Element Selection does
/// not consume one (it answers from the live UIA tree): the fallback leg needs it, the overlay will
/// annotate it, and taking it once means a failed primary does not pay for a second capture of a
/// screen that has since moved.</para>
///
/// <para><b>Threading.</b> Safe to call from any thread, including the Global Hotkey Monitor's worker
/// thread. Overlapping sessions are refused rather than queued: two captures and two provider calls in
/// flight would fight over the same screen and the same keys.</para>
///
/// <para><b>Error contract (inherited, not rewritten).</b> Provider auth/HTTP failures thrown by the
/// legs propagate to the caller — this service does not swallow them into a fake verdict. Cancellation
/// propagates too, and <see cref="Dispose"/> cancels any in-flight session. DPI awareness stays the
/// host's decision, exactly as in the capture module.</para>
/// </remarks>
public sealed partial class ContextOrchestratorService : IDisposable
{
    private readonly ScreenCaptureService _capture;
    private readonly ElementSelectionService _selection;
    private readonly VisionService _vision;
    private readonly ContextOrchestratorOptions _options;

    /// <summary>Cancelled by <see cref="Dispose"/>; linked into every session's token so teardown aborts work in flight.</summary>
    private readonly CancellationTokenSource _lifetime = new();

    private readonly object _gate = new();
    private Task<GuidanceResult>? _session;
    private volatile OrchestratorPhase _phase = OrchestratorPhase.Idle;
    private bool _disposed;

    /// <summary>
    /// Creates the orchestrator over the three module services that do the actual work. The services
    /// are caller-owned: this class disposes none of them (they are cheap and stateless per call, and
    /// a host may share them with other callers).
    /// </summary>
    public ContextOrchestratorService(
        ScreenCaptureService capture,
        ElementSelectionService selection,
        VisionService vision,
        ContextOrchestratorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(vision);

        _capture = capture;
        _selection = selection;
        _vision = vision;
        _options = options ?? ContextOrchestratorOptions.Default;
    }

    /// <summary>The options this orchestrator was created with.</summary>
    public ContextOrchestratorOptions Options => _options;

    /// <summary>Phase of the most recent session; see <see cref="OrchestratorPhase"/> for what it does and does not mean.</summary>
    public OrchestratorPhase Phase => _phase;

    /// <summary>True while a session is in flight.</summary>
    public bool IsSessionActive
    {
        get
        {
            lock (_gate)
            {
                return _session is { IsCompleted: false };
            }
        }
    }

    /// <summary>
    /// The global-hotkey receive point: one press, one question, one session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The host wires the monitor's trigger to this method, because the host also owns where the
    /// question comes from:
    /// </para>
    /// <code>
    /// hotkey.Pressed += async (_, _) =&gt;
    /// {
    ///     var question = /* Voice pipeline output -- not built yet */ "";
    ///     if (!string.IsNullOrWhiteSpace(question)) await orchestrator.HandleHotkeyPressedAsync(question);
    /// };
    /// </code>
    /// <para>
    /// Deliberately not a subscription inside this module. <c>GlobalHotkeyMonitor.Pressed</c> fires on
    /// that module's worker thread and cannot be raised by a verification host without injecting real
    /// keystrokes into the user's session, so a subscription here would ship as unverified code; and no
    /// module that exists yet produces the question, so subscribing would also mean inventing a
    /// question source. Both gaps close in the app shell (Voice pipeline + this wiring), not here.
    /// </para>
    /// <para>
    /// Safe to call from the monitor's worker thread: it returns a task and does no work inline beyond
    /// the re-entrancy check, so a hook callback is never delayed by a capture or a provider call.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">A session is already in flight.</exception>
    /// <exception cref="ObjectDisposedException">The orchestrator was disposed.</exception>
    public Task<GuidanceResult> HandleHotkeyPressedAsync(string question, CancellationToken ct = default)
        => ResolveAsync(question, ct);

    /// <summary>Runs one full guidance session for <paramref name="question"/> and returns its normalized verdict.</summary>
    /// <exception cref="ArgumentException"><paramref name="question"/> is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">A session is already in flight — refused, never queued.</exception>
    /// <exception cref="ObjectDisposedException">The orchestrator was disposed.</exception>
    /// <exception cref="OperationCanceledException">The caller's token or the service lifetime was cancelled.</exception>
    /// <exception cref="JevAuthenticationException">Live Element Selection has no usable key.</exception>
    /// <exception cref="JevClientException">Element Selection provider/HTTP failure.</exception>
    /// <exception cref="VisionAuthenticationException">Live Vision has no usable key.</exception>
    /// <exception cref="VisionClientException">Vision provider/HTTP failure.</exception>
    public Task<GuidanceResult> ResolveAsync(string question, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_session is { IsCompleted: false })
            {
                // Refusing beats queueing: a second capture would race the first over the same screen,
                // and the second question would be answered against a frame the user has moved past.
                throw new InvalidOperationException(
                    "A guidance session is already in flight. One hotkey trigger maps to one session; " +
                    "wait for the current session to finish, or dispose the orchestrator to abort it.");
            }

            // Started inside the lock so the session's linked lifetime token exists before any Dispose
            // can run, and so the busy flag is set before the pipeline can yield.
            _session = ResolveCoreAsync(question, ct);
            return _session;
        }
    }

    /// <summary>
    /// The pipeline: capture → PRIMARY leg → (genuine failure only) FALLBACK leg → verdict. Every await
    /// takes the same linked token, so one cancellation unwinds the whole session.
    /// </summary>
    private async Task<GuidanceResult> ResolveCoreAsync(string question, CancellationToken ct)
    {
        // Link the caller's token with the service lifetime: Dispose() aborts in-flight work too.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        var token = linked.Token;
        var started = Stopwatch.GetTimestamp();

        try
        {
            _phase = OrchestratorPhase.Capturing;
            var captures = await _capture.CaptureAllMonitorsAsync(token).ConfigureAwait(false);
            if (captures.Count == 0)
            {
                // Defensive: CaptureAllMonitorsAsync already throws when no display is enumerated.
                throw new InvalidOperationException(
                    "Screen capture returned no frames, so neither coordinate leg can run.");
            }

            _phase = OrchestratorPhase.Selecting;
            // captures[0] is the cursor's display; its TextLines are empty unless the host turned
            // ScreenCaptureOptions.ReadText on (DEC-038/DEC-039).
            var selection = await _selection.SelectAsync(question, _options.Target, captures[0].TextLines, token).ConfigureAwait(false);

            if (!selection.Failed && _options.CheckLowConfidence && LowConfidenceCheck.Applies(selection))
            {
                // DEC-037: found, but Jev was unsure. Vision gives a second opinion on the same frame;
                // Element Selection's answer is kept unless Vision is sure and points elsewhere.
                var checkedVerdict = await CheckLowConfidenceAsync(question, selection, captures, started, token).ConfigureAwait(false);
                _phase = OrchestratorPhase.Resolved;
                return checkedVerdict;
            }

            if (!selection.Failed)
            {
                // Covers Found = true AND the honest no-match (Choice == "none": Found = false WITH no
                // FailureReason). The no-match must NOT fall through: it is a verdict, not a failure.
                var answered = FromSelection(selection, captures, Stopwatch.GetElapsedTime(started));
                _phase = OrchestratorPhase.Resolved;
                return answered;
            }

            // Genuine primary failure -> the documented fallback. captures[0] is the cursor's display
            // (CaptureAllMonitorsAsync orders it first), i.e. where the user is looking.
            _phase = OrchestratorPhase.VisionFallback;
            var vision = await _vision.LocateAsync(captures[0], question, token).ConfigureAwait(false);
            var fellBack = FromVision(vision, selection, captures, Stopwatch.GetElapsedTime(started));
            _phase = OrchestratorPhase.Resolved;
            return fellBack;
        }
        catch
        {
            // No verdict: leave the phase readable as "not inside a session" instead of stuck mid-pipeline.
            _phase = OrchestratorPhase.Idle;
            throw;
        }
    }

    /// <summary>
    /// Cancels any in-flight session and refuses further ones. The injected module services are NOT
    /// disposed (caller-owned), and an active session is not awaited (it may be completing on this very
    /// thread).
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

        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}


