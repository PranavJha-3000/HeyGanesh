namespace HeyGanesh.LessonSurface;

/// <summary>
/// The surface's own STA thread running a WinForms message loop. WebView2 and its windows are
/// thread-affine, and the callers (the lesson player, the hotkey worker) are not UI threads, so all
/// window and WebView2 work is posted here. Owned by <see cref="CardSurface"/>; ends on Dispose.
/// </summary>
internal sealed class UiThread : IDisposable
{
    private readonly Thread _thread;
    private readonly TaskCompletionSource<SynchronizationContext> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SynchronizationContext? _context;
    private int _disposed;

    public UiThread()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "HeyGanesh.LessonSurface.Ui" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _context = _ready.Task.GetAwaiter().GetResult();
    }

    public int ManagedThreadId => _thread.ManagedThreadId;

    /// <summary>Runs <paramref name="work"/> on the UI thread and completes with its result.</summary>
    public Task<T> InvokeAsync<T>(Func<Task<T>> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = Volatile.Read(ref _context);
        if (context is null || Volatile.Read(ref _disposed) != 0)
        {
            tcs.SetException(new ObjectDisposedException(nameof(CardSurface)));
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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var context = Interlocked.Exchange(ref _context, null);
        context?.Post(_ => Application.ExitThread(), null);
        _thread.Join(TimeSpan.FromSeconds(5));
    }

    private void Run()
    {
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        _ready.SetResult(SynchronizationContext.Current!);
        Application.Run(new ApplicationContext());
    }
}
