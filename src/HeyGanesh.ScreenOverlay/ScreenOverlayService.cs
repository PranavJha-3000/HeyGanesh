using System.Diagnostics;
using HeyGanesh.ContextOrchestrator;
using HeyGanesh.ScreenCapture;

namespace HeyGanesh.ScreenOverlay;

/// <summary>
/// The temporary, click-through pointer/highlight that shows where the Context Orchestrator resolved
/// an answer — the point-and-explain tutor's first visual act.
/// </summary>
/// <remarks>
/// <para>
/// <b>Render, never act.</b> This service puts pixels on screen for a bounded time and takes them
/// away again. It never clicks, types, moves the cursor, takes focus, or otherwise acts on behalf of
/// the user (CONSTRAINTS.md; DEC-011). Its windows report <c>WM_NCHITTEST → HTTRANSPARENT</c> and
/// carry <c>WS_EX_NOACTIVATE</c>, so input falls through to the user's own windows and activation is
/// impossible. No <c>SendInput</c>/<c>SetCursorPos</c>/<c>SetFocus</c>/<c>SetForegroundWindow</c>,
/// no injection, no UIA writes exist anywhere in this assembly.
/// </para>
/// <para>
/// <b>Consumes <see cref="GuidanceResult"/> directly</b> — no mapping type, no shared abstraction
/// assembly, no coordinate conversion (DEC-009/DEC-010). Box and center are global physical pixels
/// and are used verbatim; <c>Failed</c> or <c>Found=false</c> draws nothing at all.
/// </para>
/// <para>
/// <b>Never captures the screen.</b> The module reads monitor topology (to place windows) and calls
/// the capture-exclusion contract (so our own window can never appear in a capture), but it never
/// takes a frame: the highlight is drawn from the coordinates it was handed.
/// </para>
/// <para>
/// One show at a time: a second <see cref="ShowAsync"/> while one is active is refused, never queued —
/// two simultaneous pointers would be ambiguous, exactly as a second trigger is refused upstream.
/// </para>
/// </remarks>
public sealed class ScreenOverlayService : IDisposable
{
    private readonly ScreenOverlayOptions _options;
    private readonly ScreenCaptureService _monitors;
    private readonly bool _ownsMonitors;
    private readonly OverlayPump _pump = new();
    private readonly object _gate = new();

    // Per-show state; all mutated under _gate. The show's CTS carries both the caller's token and
    // CancelAfter(VisibleDuration) — it IS the duration timer. Callbacks capture that show's
    // *instance*, never these fields, so a stale callback can never cancel a later show.
    private CancellationTokenSource? _showCts;
    private CancellationTokenRegistration _callerRegistration;
    private TaskCompletionSource<OverlayShowResult> _shown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private OverlayWindow[] _windows = [];
    private bool _showActive;
    private bool _disposed;

    /// <summary>
    /// Creates the service. Pass <paramref name="monitors"/> when the caller already has a capture
    /// service (the orchestrator's host does — it is needed for the pipeline upstream); otherwise a
    /// private one is created for topology queries only and disposed with this service.
    /// </summary>
    public ScreenOverlayService(ScreenOverlayOptions? options = null, ScreenCaptureService? monitors = null)
    {
        _options = options ?? ScreenOverlayOptions.Default;
        _options.Validate();

        if (monitors is null)
        {
            _monitors = new ScreenCaptureService();
            _ownsMonitors = true;
        }
        else
        {
            _monitors = monitors;
        }
    }

    /// <summary>True while a highlight is (or is about to be) on screen.</summary>
    public bool IsShowing
    {
        get
        {
            lock (_gate)
            {
                return _showActive;
            }
        }
    }

    /// <summary>How long a highlight would remain visible if shown right now.</summary>
    public TimeSpan VisibleDuration => _options.VisibleDuration;

    /// <summary>
    /// Puts the resolved target on screen for <see cref="VisibleDuration"/> and then removes it, or
    /// returns immediately with an <see cref="OverlayShowResult"/> saying why nothing was drawn.
    /// Cancellation removes any window that did appear. Callable from any thread.
    /// </summary>
    /// <remarks>
    /// Awaiting the returned task waits out the full visible duration. A second call while one is in
    /// flight throws <see cref="InvalidOperationException"/> — refused, never queued.
    /// </remarks>
    public Task<OverlayShowResult> ShowAsync(GuidanceResult guidance, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(guidance);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_showActive)
            {
                throw new InvalidOperationException(
                    "An overlay is already showing; a second highlight would be ambiguous. " +
                    "Await the previous ShowAsync (or HideAsync) before starting another — refused, not queued.");
            }

            _showActive = true;
            _shown = new TaskCompletionSource<OverlayShowResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // ---- verdict checks: anything short of a real verdict renders nothing ----
            // Failed is checked BEFORE Found: a fallback not-found arrives as Found=false AND
            // Failed=true (the CO-6 asymmetry), and that is "no verdict produced", not a no-match.
            if (guidance.Failed)
            {
                return Task.FromResult(End(new OverlayShowResult
                {
                    Reason = OverlayShowResult.ReasonFailure,
                    Detail = guidance.FailureReason,
                }));
            }

            if (!guidance.Found)
            {
                // Honest no-match: drawing a pointer for "not found" would teach the user that a
                // highlight means "maybe here". No window is ever created for this path.
                return Task.FromResult(End(new OverlayShowResult
                {
                    Reason = OverlayShowResult.ReasonNoMatch,
                }));
            }

            if (guidance.BoxOnScreen is not { } box || box.Width <= 0 || box.Height <= 0)
            {
                return Task.FromResult(End(new OverlayShowResult
                {
                    Reason = OverlayShowResult.ReasonNoBox,
                }));
            }

            if (_options.RequirePerMonitorDpi && !IsPerMonitorDpiAware())
            {
                // Refusing beats drawing at a confidently wrong place: without per-monitor awareness
                // Windows virtualises coordinates, so physical px and window units diverge. This
                // checks awareness; it never sets it (DEC-004 keeps that the host's decision).
                return Task.FromResult(End(new OverlayShowResult
                {
                    Reason = OverlayShowResult.ReasonNotPerMonitorDpiAware,
                }));
            }

            if (ct.IsCancellationRequested)
            {
                return Task.FromResult(End(new OverlayShowResult
                {
                    Reason = OverlayShowResult.ReasonCancelled,
                }));
            }

            // ---- geometry: one window per monitor the box touches; nothing invented ----
            OverlayWindow[] windows;
            try
            {
                var touching = _monitors.EnumerateMonitors()
                    .Where(m => Intersects(m.Bounds, box))
                    .ToArray();

                if (touching.Length == 0)
                {
                    // Topology changed between resolution and display; guess nothing.
                    return Task.FromResult(End(new OverlayShowResult
                    {
                        Reason = OverlayShowResult.ReasonOffScreen,
                    }));
                }

                windows = new OverlayWindow[touching.Length];
                for (var i = 0; i < touching.Length; i++)
                {
                    var bounds = touching[i].Bounds;
                    var center = guidance.CenterOnScreen is { } c
                        ? new PixelRect(c.X, c.Y, 1, 1)
                        : (PixelRect?)null;
                    windows[i] = new OverlayWindow(_options, bounds, new OverlayDraw(
                        bounds,
                        ToLocal(box, bounds),
                        center is { } cc ? ToLocal(cc, bounds) : null));
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(End(new OverlayShowResult
                {
                    Reason = OverlayShowResult.ReasonWindowCreationFailed,
                    Detail = $"monitor enumeration failed: {ex.Message}",
                }));
            }

            _windows = windows;

            // ---- arm the show: one linked token carries caller-cancel AND the duration timer ----
            var showCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            showCts.CancelAfter(_options.VisibleDuration);
            _showCts = showCts;

            if (ct.CanBeCanceled)
            {
                // Captures this show's CTS *instance*, never the field: a stale callback must never
                // cancel a later show (End disposes the registration before the guard drops).
                var armed = showCts;
                _callerRegistration = ct.Register(static state =>
                {
                    var cts = (CancellationTokenSource)state!;
                    try
                    {
                        cts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // The show ended between queuing and running this callback; nothing to cancel.
                    }
                }, armed);
            }

            var shown = _shown;
            try
            {
                _pump.EnsureStarted();
            }
            catch (Exception ex)
            {
                // A pump that cannot start means no windows can ever be created; fail the show through
                // the same single End path rather than throwing out of ShowAsync's synchronous section.
                return Task.FromResult(End(new OverlayShowResult
                {
                    Reason = OverlayShowResult.ReasonWindowCreationFailed,
                    Detail = $"overlay pump failed to start: {ex.Message}",
                }));
            }

            _ = ShowCoreAsync(windows, showCts, ct);
            return shown.Task;
        }
    }

    /// <summary>
    /// The show itself, executed as one tracked task: create the windows on the pump thread, exclude
    /// them from capture (hard gate — an overlay capture could photograph would feed HeyGanesh its own
    /// output on the next frame, EX-5), show without activating, wait for duration / hide / cancel,
    /// then destroy. Exactly one <see cref="End"/> runs, so every way a show can finish releases the
    /// one-show-at-a-time guard.
    /// </summary>
    private async Task ShowCoreAsync(
        OverlayWindow[] windows,
        CancellationTokenSource showCts,
        CancellationToken callerToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var reachedShow = false;
        OverlayShowResult result;

        try
        {
            await _pump.InvokeAsync(() =>
            {
                foreach (var window in windows)
                {
                    window.Create();
                    if (window.CreationError is not null)
                    {
                        throw new InvalidOperationException(window.CreationError);
                    }
                }
            }).ConfigureAwait(false);

            await _pump.InvokeAsync(() =>
            {
                foreach (var window in windows)
                {
                    if (!CaptureExclusion.TryExcludeFromCapture(window.Handle, out var error))
                    {
                        throw new InvalidOperationException(
                            $"CaptureExclusion failed for HWND 0x{window.Handle:X} (Win32 error {error}).");
                    }
                }
            }).ConfigureAwait(false);

            await _pump.InvokeAsync(() =>
            {
                foreach (var window in windows)
                {
                    window.Show();
                }

                reachedShow = true;
            }).ConfigureAwait(false);

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, showCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Duration elapsed, HideAsync, Dispose, or the caller's token — all end the wait here.
            }

            stopwatch.Stop();
            var elapsed = stopwatch.Elapsed;
            var fullDuration = elapsed + TimeSpan.FromMilliseconds(150) >= _options.VisibleDuration;
            var cancelled = callerToken.IsCancellationRequested;
            var displayed = reachedShow && !cancelled && fullDuration;

            result = new OverlayShowResult
            {
                Displayed = displayed,
                // !displayed here means the caller cancelled or the show was ended early (HideAsync /
                // Dispose): windows were removed before this returns, which is what "cancelled" says.
                Reason = displayed ? null : OverlayShowResult.ReasonCancelled,
                ShownFor = reachedShow ? elapsed : TimeSpan.Zero,
                WindowCount = windows.Length,
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result = new OverlayShowResult
            {
                Displayed = false,
                Reason = ex.Message.Contains("CaptureExclusion", StringComparison.Ordinal)
                    ? OverlayShowResult.ReasonExclusionFailed
                    : OverlayShowResult.ReasonWindowCreationFailed,
                ShownFor = TimeSpan.Zero,
                WindowCount = 0,
                Detail = ex.Message,
            };
        }
        finally
        {
            // Teardown is best-effort: the service may already be disposing the pump, and a teardown
            // problem must never mask the result or leak the guard (End runs after this, unconditionally).
            try
            {
                if (_pump.IsStarted)
                {
                    await _pump.InvokeAsync(() =>
                    {
                        foreach (var window in windows)
                        {
                            window.Destroy();
                        }
                    }).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // Destroy() is idempotent and Dispose queues its own teardown; nothing to recover here.
            }
        }

        End(result);
    }

    /// <summary>
    /// Ends the current show early and waits until the windows are gone; no-op when nothing shows.
    /// Safe from any thread, safe twice, safe while the duration timer is racing it.
    /// </summary>
    public async Task HideAsync()
    {
        CancellationTokenSource? cts;
        Task shown;
        lock (_gate)
        {
            if (!_showActive)
            {
                return;
            }

            cts = _showCts;
            shown = _shown.Task;
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The show ended between taking the lock and cancelling; the await below returns instantly.
        }

        await shown.ConfigureAwait(false);
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            cts = _showCts;
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A show ended between reading the field and cancelling; nothing to tear down.
        }

        // Best-effort window teardown before the pump dies; ShowCoreAsync's finally may race us here
        // and Destroy() is idempotent, so both paths converging is fine. The wait is bounded: a wedged
        // pump must never wedge Dispose. Disposal of the CTS itself is End()'s job — it owns the show.
        if (_pump.IsStarted)
        {
            try
            {
                _pump.InvokeAsync(() =>
                {
                    foreach (var window in _windows)
                    {
                        window.Destroy();
                    }
                }).Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
                // Pump failing or already gone — the windows die with the process either way.
            }
        }

        _pump.Dispose();
        _callerRegistration.Dispose();
        _callerRegistration = default;

        // A ShowAsync caller must never hang on a disposed service; if a show was in flight its own
        // End() may already have completed this, and TrySetResult is safe to race.
        _shown.TrySetResult(new OverlayShowResult { Reason = OverlayShowResult.ReasonCancelled });

        if (_ownsMonitors)
        {
            _monitors.Dispose();
        }
    }

    /// <summary>
    /// Single place a show finishes: releases the one-show-at-a-time guard, disposes this show's
    /// cancellation state, and completes the awaiting caller exactly once. Both the synchronous
    /// refusal paths and <see cref="ShowCoreAsync"/> funnel through here — that is what guarantees a
    /// guard can never stay stuck closed.
    /// </summary>
    private OverlayShowResult End(OverlayShowResult result)
    {
        lock (_gate)
        {
            _showActive = false;
            _callerRegistration.Dispose();
            _callerRegistration = default;
            _showCts?.Dispose();
            _showCts = null;
        }

        _shown.TrySetResult(result);
        return result;
    }

    /// <summary>
    /// Global physical rectangle → this monitor's window-local physical pixels (clipped to the
    /// monitor), or null when the rect does not reach this monitor. The only coordinate conversion in
    /// the module, and it is a translation — never a scaling — so DPI semantics are unchanged.
    /// </summary>
    private static PixelRect? ToLocal(PixelRect global, PixelRect monitorBounds)
    {
        if (!Intersects(global, monitorBounds))
        {
            return null;
        }

        var left = Math.Max(global.X, monitorBounds.X);
        var top = Math.Max(global.Y, monitorBounds.Y);
        var right = Math.Min(global.Right, monitorBounds.Right);
        var bottom = Math.Min(global.Bottom, monitorBounds.Bottom);

        if (right <= left || bottom <= top)
        {
            return null;
        }

        return new PixelRect(left - monitorBounds.X, top - monitorBounds.Y, right - left, bottom - top);
    }

    private static bool Intersects(PixelRect a, PixelRect b)
        => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    /// <summary>
    /// True when this process is per-monitor DPI aware — i.e. physical pixels and window coordinates
    /// are the same space. Read-only: it never sets awareness (DEC-004 keeps that the host's
    /// decision), and when the OS cannot answer it says "aware" rather than refusing guidance —
    /// blocking a resolved target over an unanswerable probe would be the worse failure.
    /// </summary>
    private static bool IsPerMonitorDpiAware()
    {
        try
        {
            var context = OverlayPInvoke.GetThreadDpiAwarenessContext();
            return OverlayPInvoke.GetAwarenessFromDpiAwarenessContext(context)
                >= OverlayNative.PROCESS_PER_MONITOR_DPI_AWARE;
        }
        catch (EntryPointNotFoundException)
        {
            return true;
        }
        catch (DllNotFoundException)
        {
            return true;
        }
    }
}