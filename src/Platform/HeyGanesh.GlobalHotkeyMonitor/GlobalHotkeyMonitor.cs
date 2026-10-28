using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace HeyGanesh.GlobalHotkeyMonitor;

/// <summary>
/// Listen state published by the monitor. Deliberately named after the Context
/// Orchestrator's idle/listening vocabulary so the wiring can be a direct assignment later.
/// </summary>
public enum HotkeyState
{
    /// <summary>The chord is not held; nothing should be capturing.</summary>
    Idle = 0,

    /// <summary>The chord is held; the future push-to-talk pipeline should be capturing.</summary>
    Listening = 1,
}

/// <summary>Tuning for <see cref="GlobalHotkeyMonitor"/>.</summary>
public sealed class HotkeyMonitorOptions
{
    /// <summary>Defaults: left-Ctrl + left-Alt chord, injected events honoured.</summary>
    public static HotkeyMonitorOptions Default { get; } = new();

    /// <summary>
    /// When <see langword="true"/> (the default) the Alt half of the chord must be the left
    /// Alt key. AltGr on most layouts is reported as LCtrl + RAlt, so requiring the left side
    /// keeps ordinary AltGr typing from completing the chord.
    /// </summary>
    public bool RequireLeftAlt { get; init; } = true;

    /// <summary>
    /// When <see langword="true"/>, keyboard events synthesized by SendInput / SendKeys are
    /// ignored. Defaults to <see langword="false"/> so the chord can also be driven by
    /// automation (which is what a future automated integration test will need).
    /// </summary>
    public bool IgnoreInjectedEvents { get; init; }
}

/// <summary>
/// Global Hotkey Monitor — system-wide push-to-talk detection for a modifier-only chord
/// (left Ctrl + left Alt held, with nothing else typed in between).
/// </summary>
/// <remarks>
/// <para><b>Why a low-level hook.</b> <c>RegisterHotKey</c> cannot express this: it requires a
/// non-modifier virtual key, fires once on combo-down, and has no notion of "held" or
/// "released". A <c>WH_KEYBOARD_LL</c> hook is the only way to observe a modifier-only hold.</para>
///
/// <para><b>Listen-only contract.</b> Every notification — including the ones that produce
/// <see cref="Pressed"/> / <see cref="Released"/> — is passed to the next hook unchanged, and the
/// hook proc's return value is exactly <c>CallNextHookEx</c>'s. This module never suppresses,
/// rewrites, or delays user input, and it never calls or steals focus.</para>
///
/// <para><b>Threading.</b> <see cref="Start"/> spins up two background threads: a <i>pump</i>
/// thread that installs the hook and owns the message loop the OS dispatches hook callbacks into,
/// and a <i>worker</i> thread that runs the chord state machine and raises the events. The split
/// exists so a slow event handler can never stall the hook callback (see the timeout note below).
/// <see cref="Pressed"/> and <see cref="Released"/> therefore fire on the worker thread — a UI
/// subscriber must marshal to its own dispatcher.</para>
///
/// <para><b>The OS-side timeout.</b> Windows silently removes low-level hooks whose callbacks
/// exceed <c>LowLevelHooksTimeout</c> (HKCU\Control Panel\Desktop, ~300 ms default) — no
/// exception, no return code, and no API to ask whether the hook is still installed. The
/// mitigation here is that the hook proc does one struct read and one channel write
/// (microseconds) and never raises events; <see cref="HookCallbackCount"/> and
/// <see cref="MaxCallbackLatency"/> exist so a caller can watch for degraded callback health.
/// Deliberate non-goal: no automatic re-hook watchdog, because a re-hook window can drop real
/// keystrokes (see docs/ai/DECISIONS.md DEC-003).</para>
///
/// <para><b>Known limits (not fixable in this module).</b> A non-elevated process's hook receives
/// nothing while an elevated window has focus (UIPI), and nothing at all on the UAC Secure
/// Desktop. Such gaps show up as the chord simply not firing, never as a stuck
/// <see cref="HotkeyState.Listening"/>.</para>
/// </remarks>
public sealed class GlobalHotkeyMonitor : IDisposable
{
    private const int IdleState = 0;
    private const int ListeningState = 1;

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly HotkeyMonitorOptions _options;

    // SingleWriter = the pump thread's hook proc; SingleReader = the worker thread.
    private readonly Channel<KeyEvent> _queue = Channel.CreateUnbounded<KeyEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    // Held for the module's lifetime: if this delegate is collected, the hook callback dies.
    private NativeMethods.LowLevelKeyboardProc? _hookProc;

    private SafeHookHandle? _hookHandle;
    private Thread? _pumpThread;
    private Thread? _workerThread;
    private TaskCompletionSource? _pumpReady;
    private TaskCompletionSource? _pumpExited;
    private CancellationTokenSource? _workerCts;
    private uint _pumpThreadId;
    private int _installError;
    private bool _running;
    private bool _disposed;

    private int _state = IdleState;
    private long _hookCallbackCount;
    private long _maxCallbackTicks;
    private long _droppedEventCount;
    private Exception? _lastError;

    /// <summary>Creates a monitor using <see cref="HotkeyMonitorOptions.Default"/>.</summary>
    public GlobalHotkeyMonitor()
        : this(HotkeyMonitorOptions.Default)
    {
    }

    /// <summary>Creates a monitor with explicit options.</summary>
    public GlobalHotkeyMonitor(HotkeyMonitorOptions options)
        => _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>Raised when the chord becomes complete. Fires on the internal worker thread.</summary>
    public event EventHandler? Pressed;

    /// <summary>
    /// Raised when the chord is released — either because a modifier came up, or because a real
    /// key was pressed while the chord was held. Fires on the internal worker thread.
    /// </summary>
    public event EventHandler? Released;

    /// <summary>Current listen state; reading it never blocks on the hook.</summary>
    public HotkeyState State
        => Volatile.Read(ref _state) == ListeningState ? HotkeyState.Listening : HotkeyState.Idle;

    /// <summary>Whether the hook is currently installed and pumping.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
    }

    /// <summary>Number of keyboard notifications the hook proc has seen. Diagnostics only.</summary>
    public long HookCallbackCount => Interlocked.Read(ref _hookCallbackCount);

    /// <summary>
    /// Number of notifications the hook proc could not queue (non-zero only during shutdown).
    /// Diagnostics only.
    /// </summary>
    public long DroppedEventCount => Interlocked.Read(ref _droppedEventCount);

    /// <summary>
    /// Longest time spent inside the hook proc. Must stay orders of magnitude below the OS's
    /// ~300 ms <c>LowLevelHooksTimeout</c>, or Windows will silently remove the hook.
    /// </summary>
    public TimeSpan MaxCallbackLatency
        => TimeSpan.FromSeconds((double)Interlocked.Read(ref _maxCallbackTicks) / Stopwatch.Frequency);

    /// <summary>
    /// Most recent fault from the pump thread, the worker thread, or a subscriber of
    /// <see cref="Pressed"/>/<see cref="Released"/>. Subscriber exceptions are contained so a
    /// throwing handler cannot deafen the monitor; they surface here instead.
    /// </summary>
    public Exception? LastError => Volatile.Read(ref _lastError);

    /// <summary>
    /// Installs the hook and starts pumping. Idempotent while running; callable from any thread.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The monitor has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The OS refused the hook.</exception>
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_running)
            {
                return;
            }

            _pumpReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pumpExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _workerCts = new CancellationTokenSource();
            _installError = 0;

            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "HeyGanesh.HotkeyMonitor.Worker",
            };

            _pumpThread = new Thread(PumpLoop)
            {
                IsBackground = true,
                Name = "HeyGanesh.HotkeyMonitor.Pump",
            };

            _workerThread.Start();
            _pumpThread.Start();

            if (!_pumpReady.Task.Wait(StartTimeout))
            {
                StopCore();
                throw new InvalidOperationException(
                    $"The Global Hotkey Monitor pump thread did not report readiness within {StartTimeout}.");
            }

            if (_hookHandle is null)
            {
                var error = _installError;
                StopCore();
                throw new InvalidOperationException(
                    $"SetWindowsHookEx(WH_KEYBOARD_LL) failed. Win32 error {error}: {new Win32Exception(error).Message}");
            }

            Volatile.Write(ref _state, IdleState);
            _running = true;
        }
    }

    /// <summary>Unhooks and tears the background threads down. Idempotent; safe from a handler.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            StopCore();
        }
    }

    /// <summary>Stops the monitor and releases its resources. Idempotent.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            StopCore();
            _queue.Writer.TryComplete();
            _disposed = true;
        }
    }

    /// <summary>
    /// Installs the hook, then owns the message loop that keeps it alive. Must run on a thread of
    /// its own: low-level hook callbacks are dispatched to the installing thread while it pumps.
    /// </summary>
    private void PumpLoop()
    {
        _pumpThreadId = NativeMethods.GetCurrentThreadId();

        try
        {
            // Force this thread's message queue into existence so Stop() can always post WM_QUIT.
            _ = NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, NativeMethods.PM_NOREMOVE);

            // JIT the hook path before the hook exists, so its cost is not paid inline with input.
            WarmUpHookPath();

            _hookProc = HookProc;
            var rawHook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL,
                _hookProc,
                IntPtr.Zero,
                dwThreadId: 0);

            if (rawHook == IntPtr.Zero)
            {
                _installError = Marshal.GetLastWin32Error();
                _pumpReady?.TrySetResult();
                return;
            }

            _hookHandle = new SafeHookHandle(rawHook);
            _pumpReady?.TrySetResult();

            // No DispatchMessage needed: the OS invokes the hook proc during message retrieval.
            while (NativeMethods.GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
            {
                // Intentionally empty — the hook callback is the payload.
            }
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastError, ex);
            _pumpReady?.TrySetResult();
        }
        finally
        {
            // Order matters: release the hook before dropping the delegate that serves it.
            _hookHandle?.Dispose();
            _hookHandle = null;
            _hookProc = null;
            _pumpExited?.TrySetResult();
        }
    }

    /// <summary>
    /// Consumes queued notifications, runs the chord state machine, and raises the two events.
    /// Kept off the pump thread so subscriber latency can never reach the OS's hook timeout.
    /// </summary>
    private void WorkerLoop()
    {
        // Captured once: the source may be disposed by Stop() while this loop is unwinding.
        var token = _workerCts?.Token ?? CancellationToken.None;
        var tracker = new ChordTracker(_options);

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (!_queue.Reader.WaitToReadAsync(token).AsTask().GetAwaiter().GetResult())
                {
                    break;
                }

                while (_queue.Reader.TryRead(out var keyEvent))
                {
                    var transition = tracker.Feed(keyEvent.VirtualKey, keyEvent.IsDown, keyEvent.IsInjected);
                    if (transition != HotkeyTransition.None)
                    {
                        Publish(transition);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastError, ex);
        }
    }

    /// <summary>
    /// The hook callback. Runs on the pump thread, inline with system-wide input, so it must stay
    /// trivially fast: read the struct, enqueue, pass through. No events, no locks, no logging.
    /// </summary>
    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var started = Stopwatch.GetTimestamp();

            try
            {
                var message = (int)wParam;
                var isDown = message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;

                if (isDown || message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
                {
                    var data = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
                    var injected = (data.Flags & NativeMethods.LLKHF_INJECTED) != 0;

                    Interlocked.Increment(ref _hookCallbackCount);

                    if (!_queue.Writer.TryWrite(new KeyEvent(data.VirtualKey, isDown, injected)))
                    {
                        Interlocked.Increment(ref _droppedEventCount);
                    }
                }
            }
            catch
            {
                // An exception escaping a P/Invoke callback would tear the process down, and
                // anything thrown here must never stop the pass-through below.
            }
            finally
            {
                RecordCallbackLatency(Stopwatch.GetTimestamp() - started);
            }
        }

        // Listen-only contract: hand the event on unchanged and return exactly what the next hook
        // returned. This module never reports a key as handled.
        var handle = _hookHandle;
        return NativeMethods.CallNextHookEx(
            handle?.DangerousGetHandle() ?? IntPtr.Zero, nCode, wParam, lParam);
    }

    private void Publish(HotkeyTransition transition)
    {
        var pressed = transition == HotkeyTransition.Pressed;

        // State is updated before handlers run, so a handler reading State sees the new value.
        Volatile.Write(ref _state, pressed ? ListeningState : IdleState);

        var handler = pressed ? Pressed : Released;
        if (handler is null)
        {
            return;
        }

        // Isolated per subscriber: one throwing handler must not skip the others, and must not
        // kill the worker thread (which would silently deafen the monitor).
        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler)subscriber).Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _lastError, ex);
            }
        }
    }

    /// <summary>
    /// Runs the hook proc's dependencies once so their first use is not just-in-time compiled inside
    /// a real callback. Measured <see cref="MaxCallbackLatency"/> otherwise includes that
    /// compilation, since it happens inline with system-wide input.
    /// </summary>
    private static void WarmUpHookPath()
    {
        var size = Marshal.SizeOf<NativeMethods.KbdLlHookStruct>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(new NativeMethods.KbdLlHookStruct(), buffer, fDeleteOld: false);
            _ = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        // Same generic instantiation the hook proc writes to.
        var scratch = Channel.CreateUnbounded<KeyEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        _ = scratch.Writer.TryWrite(new KeyEvent(0, isDown: false, isInjected: false));
        scratch.Writer.TryComplete();
    }

    /// <summary>Lock-free running maximum of in-callback duration.</summary>
    private void RecordCallbackLatency(long elapsedTicks)
    {
        var observed = Interlocked.Read(ref _maxCallbackTicks);
        while (elapsedTicks > observed)
        {
            var previous = Interlocked.CompareExchange(ref _maxCallbackTicks, elapsedTicks, observed);
            if (previous == observed)
            {
                return;
            }

            observed = previous;
        }
    }

    /// <summary>Shared teardown for <see cref="Stop"/> and <see cref="Dispose"/>; caller holds _gate.</summary>
    private void StopCore()
    {
        _running = false;

        _workerCts?.Cancel();

        var pumpThreadId = _pumpThreadId;
        if (pumpThreadId != 0)
        {
            NativeMethods.PostThreadMessage(pumpThreadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        _pumpExited?.Task.Wait(StopTimeout);
        JoinUnlessCurrent(_pumpThread, StopTimeout);
        JoinUnlessCurrent(_workerThread, StopTimeout);

        _pumpThread = null;
        _workerThread = null;
        _pumpReady = null;
        _pumpExited = null;
        _workerCts?.Dispose();
        _workerCts = null;
        _pumpThreadId = 0;

        Volatile.Write(ref _state, IdleState);
    }

    /// <summary>
    /// Stop()/Dispose() are legal from an event handler, i.e. from the worker thread itself —
    /// joining the current thread would deadlock.
    /// </summary>
    private static void JoinUnlessCurrent(Thread? thread, TimeSpan timeout)
    {
        if (thread is null || thread == Thread.CurrentThread)
        {
            return;
        }

        thread.Join(timeout);
    }

    private readonly struct KeyEvent
    {
        internal KeyEvent(uint virtualKey, bool isDown, bool isInjected)
        {
            VirtualKey = virtualKey;
            IsDown = isDown;
            IsInjected = isInjected;
        }

        internal uint VirtualKey { get; }

        internal bool IsDown { get; }

        internal bool IsInjected { get; }
    }
}
