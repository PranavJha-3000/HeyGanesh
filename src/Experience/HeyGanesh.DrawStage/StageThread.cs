namespace HeyGanesh.DrawStage;

/// <summary>
/// The stage's own STA thread running a WinForms message loop. WebView2, the window and Windows.UI.Composition are
/// thread-affine (composition also needs the DispatcherQueue this loop pumps), and the callers (the lesson turn, the
/// hotkey worker) are not UI threads, so every window call is posted here. Owned by <see cref="DrawStageWindow"/>.
/// The same pattern as the deleted LessonSurface's UiThread (FEATURE-011 step 10 removed it, DEC-077).
/// </summary>
internal sealed class StageThread : IDisposable
{
    private readonly Thread _thread;
    private readonly TaskCompletionSource<SynchronizationContext> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SynchronizationContext? _context;
    private int _disposed;

    public StageThread()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "HeyGanesh.DrawStage.Ui" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _context = _ready.Task.GetAwaiter().GetResult();
    }

    public bool IsAlive => _thread.IsAlive;

    /// <summary>Runs <paramref name="work"/> on the stage thread and completes with its result.</summary>
    public Task<T> InvokeAsync<T>(Func<Task<T>> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = Volatile.Read(ref _context);
        if (context is null || Volatile.Read(ref _disposed) != 0)
        {
            tcs.SetException(new ObjectDisposedException(nameof(DrawStageWindow)));
            return tcs.Task;
        }

        context.Post(async _ =>
        {
            try
            {
                tcs.TrySetResult(await work());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }, null);
        return tcs.Task;
    }

    public Task InvokeAsync(Func<Task> work) => InvokeAsync<bool>(async () =>
    {
        await work();
        return true;
    });

    public Task RunAsync(Action work) => InvokeAsync<bool>(() =>
    {
        work();
        return Task.FromResult(true);
    });

    /// <summary>Runs <paramref name="cleanup"/> on the thread, then ends its loop and waits for it.</summary>
    public void Dispose(Action cleanup)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var context = Interlocked.Exchange(ref _context, null);
        context?.Post(_ =>
        {
            try
            {
                cleanup();
            }
            finally
            {
                Application.ExitThread();
            }
        }, null);
        _thread.Join(TimeSpan.FromSeconds(5));
    }

    public void Dispose() => Dispose(() => { });

    private void Run()
    {
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        _ready.SetResult(SynchronizationContext.Current!);
        Application.Run(new ApplicationContext());
    }
}
