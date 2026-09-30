using System.Collections.Concurrent;
using HeyGanesh.ScreenCapture;

namespace HeyGanesh.ScreenOverlay;

/// <summary>
/// The message-pump thread every overlay window is created, shown and destroyed on.
/// </summary>
/// <remarks>
/// <para>
/// One pump per <see cref="ScreenOverlayService"/> instance, started lazily on the first show. Win32
/// windows belong to the thread that creates them, so all window work must be marshalled here; the
/// public service API is callable from any thread (including the hotkey monitor's worker thread) and
/// posts work to this pump instead of blocking its caller.
/// </para>
/// <para>
/// The pump owns a message-only window as the target for <c>WM_APP_DRAIN</c>, which is how queued
/// work is woken: <c>PostMessage</c> to that window, the pump drains the queue, replies when the
/// queue is empty. Disposal posts <c>WM_QUIT</c>.
/// </para>
/// </remarks>
internal sealed class OverlayPump : IDisposable
{
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Thread? _thread;
    private IntPtr _messageWindow;
    private volatile bool _disposed;

    internal Task Ready => _ready.Task;

    /// <summary>True once the pump thread exists — Dispose uses this to decide whether queued teardown can run at all.</summary>
    internal bool IsStarted => _thread is not null;

    /// <summary>
    /// Starts the pump thread (once) and waits until it has created its message window, so the first
    /// show can post work immediately.
    /// </summary>
    internal void EnsureStarted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "HeyGanesh.ScreenOverlay.Pump",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the pump thread and returns a task completing when it has
    /// finished (successfully or by faulting — faults are propagated to the caller).
    /// </summary>
    internal Task InvokeAsync(Action action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Enqueue(() =>
        {
            try
            {
                action();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        if (_messageWindow != IntPtr.Zero)
        {
            OverlayPInvoke.PostMessage(_messageWindow, OverlayNative.WM_APP_DRAIN, IntPtr.Zero, IntPtr.Zero);
        }

        return completion.Task;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_messageWindow != IntPtr.Zero)
        {
            OverlayPInvoke.PostMessage(_messageWindow, OverlayNative.WM_APP_DRAIN, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private void Run()
    {
        try
        {
            _messageWindow = OverlayPInvoke.CreateWindowEx(
                0,
                "STATIC",
                string.Empty,
                0,
                0,
                0,
                0,
                0,
                OverlayNative.HWND_MESSAGE,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            if (_messageWindow == IntPtr.Zero)
            {
                _ready.TrySetException(new InvalidOperationException(
                    "Overlay pump could not create its message-only window."));
                return;
            }

            _ready.TrySetResult();

            while (!_disposed)
            {
                var status = OverlayPInvoke.GetMessage(out var message, IntPtr.Zero, 0, 0);
                if (status <= 0)
                {
                    break; // WM_QUIT or error: leave the pump.
                }

                if (message.Message == OverlayNative.WM_APP_DRAIN)
                {
                    Drain();
                    continue;
                }

                OverlayPInvoke.TranslateMessage(ref message);
                OverlayPInvoke.DispatchMessage(ref message);
            }
        }
        catch (Exception ex)
        {
            _ready.TrySetException(ex);
        }
        finally
        {
            Drain(); // anything queued while shutting down completes rather than hangs
        }
    }

    private void Drain()
    {
        while (_work.TryDequeue(out var action))
        {
            action();
        }
    }
}
