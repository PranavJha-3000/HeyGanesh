namespace HeyGanesh.Tray;

/// <summary>
/// One tray per user session (DEC-052). Two trays would install two keyboard hooks, and every Ctrl+Alt would
/// start two turns. A second launch finds the mutex taken, sets the first instance's "show" event (the first
/// answers with a balloon) and exits. <c>Local\</c> names are per session, so another signed-in user's tray
/// is not affected. The console exe does not take the mutex: running it next to the tray stays possible.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    public const string MutexName = @"Local\HeyGanesh.Tray";
    public const string ShowEventName = @"Local\HeyGanesh.Tray.Show";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;

    private SingleInstance(Mutex mutex, EventWaitHandle show)
    {
        _mutex = mutex;
        _show = show;
    }

    /// <summary>Set by a second launch.</summary>
    public WaitHandle ShowRequested => _show;

    /// <summary>The instance, or null when another tray in this session already holds it.</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
        if (!created)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstance(mutex, new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName));
    }

    /// <summary>Tells the running tray a second launch happened. Nothing to tell if it is just exiting.</summary>
    public static void SignalFirst()
    {
        if (EventWaitHandle.TryOpenExisting(ShowEventName, out var show))
        {
            using (show)
            {
                show.Set();
            }
        }
    }

    public void Dispose()
    {
        _show.Dispose();
        _mutex.ReleaseMutex(); // on the thread that took it: Main's
        _mutex.Dispose();
    }
}
