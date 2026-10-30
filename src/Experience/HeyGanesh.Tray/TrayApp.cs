using System.Diagnostics;
using HeyGanesh.Security;

namespace HeyGanesh.Tray;

/// <summary>
/// The notification-area icon and its menu, for the life of one shell run (DEC-052).
/// </summary>
/// <remarks>
/// The shell runs on the thread pool (the same <c>RunAsync</c> the console uses); this context owns the UI
/// thread's message loop. Quit cancels the stop token, which ends hooked mode exactly as Ctrl+C does; the loop
/// ends only when the shell has returned, so the keyboard hook is always released before the process exits.
/// If the shell ends by itself with a failure, a message box says so and points at the log. Every UI change
/// is marshalled through <see cref="_ui"/>, a hidden control, because the shell calls back on its own threads.
/// The menu shows each key's presence only, never a value or a length.
/// </remarks>
internal sealed class TrayApp : ApplicationContext
{
    private readonly KeyRing _keys;
    private readonly string _logFolder;
    private readonly CancellationTokenSource _stop = new();
    private readonly Control _ui = new();
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _status;
    private readonly ToolStripMenuItem _quit;
    private readonly List<(string Name, ToolStripMenuItem Item)> _keyLines = new();
    private readonly RegisteredWaitHandle? _showWait;
    private bool _quitting;

    /// <param name="runShell">Runs the pipeline until the token fires; calls the action once the hook is live. The product passes ShellHost.RunAsync.</param>
    /// <param name="showRequested">Set by a second launch (SingleInstance); null when nothing can signal.</param>
    public TrayApp(Func<CancellationToken, Action, Task<int>> runShell, KeyRing keys, string logFolder, WaitHandle? showRequested)
    {
        _keys = keys;
        _logFolder = logFolder;
        _ = _ui.Handle; // creates the window BeginInvoke posts to, on this (the UI) thread

        _status = new ToolStripMenuItem("Hey Ganesh: starting…") { Enabled = false };
        var menu = new ContextMenuStrip();
        menu.Items.Add(_status);
        foreach (var key in KeyRing.Known)
        {
            var line = new ToolStripMenuItem { Enabled = false };
            _keyLines.Add((key.Name, line));
            menu.Items.Add(line);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open log folder", null, (_, _) => OpenLogFolder()));
        _quit = new ToolStripMenuItem("Quit", null, (_, _) => Quit());
        menu.Items.Add(_quit);
        menu.Opening += (_, _) => RefreshKeys();
        RefreshKeys();

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Hey Ganesh",
            ContextMenuStrip = menu,
            Visible = true,
        };

        if (showRequested is not null)
        {
            _showWait = ThreadPool.RegisterWaitForSingleObject(showRequested, (_, _) => Post(OnSecondLaunch), null, Timeout.Infinite, executeOnlyOnce: false);
        }

        Console.WriteLine("tray: icon shown; the shell is starting");
        var run = Task.Run(() => runShell(_stop.Token, () => Post(OnHookLive)));
        run.ContinueWith(t => Post(() => Finish(t)), TaskScheduler.Default);
    }

    /// <summary>The shell's exit code, or 1 if it threw. Set when the message loop ends.</summary>
    public int ExitCode { get; private set; }

    /// <summary>The menu's item texts, separators as "-". For TY-4.</summary>
    internal IReadOnlyList<string> MenuTexts =>
        _icon.ContextMenuStrip!.Items.Cast<ToolStripItem>().Select(i => i is ToolStripSeparator ? "-" : i.Text ?? string.Empty).ToList();

    internal NotifyIcon Icon => _icon;

    /// <summary>Runs <paramref name="read"/> on the UI thread. For TY-4, which drives the tray from another thread.</summary>
    internal T OnUi<T>(Func<T> read) => (T)_ui.Invoke(read);

    /// <summary>The menu's Quit: stop the shell; the loop ends when it has returned.</summary>
    internal void Quit()
    {
        if (_quitting)
        {
            return;
        }

        _quitting = true;
        _status.Text = "Hey Ganesh: stopping…";
        _quit.Enabled = false;
        Console.WriteLine("tray: Quit chosen; stopping the shell");
        _stop.Cancel();
    }

    private void OnHookLive() => _status.Text = "Listening: hold Ctrl+Alt to ask";

    private void OnSecondLaunch()
    {
        Console.WriteLine("tray: a second launch was refused; this instance keeps running");
        _icon.ShowBalloonTip(4000, "Hey Ganesh", "Hey Ganesh is already running. Its menu is here.", ToolTipIcon.Info);
    }

    private void RefreshKeys()
    {
        foreach (var (name, item) in _keyLines)
        {
            item.Text = $"{name} key: {(_keys.Describe(name).Source == KeySource.Missing ? "missing" : "present")}";
        }
    }

    private void OpenLogFolder()
    {
        Directory.CreateDirectory(_logFolder);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { _logFolder }, UseShellExecute = false })?.Dispose();
    }

    private void Finish(Task<int> run)
    {
        if (run.IsFaulted)
        {
            Console.WriteLine($"tray: the shell failed: {run.Exception!.GetBaseException().GetType().Name}: {run.Exception.GetBaseException().Message}");
            ExitCode = 1;
        }
        else
        {
            ExitCode = run.Result;
        }

        _icon.Visible = false;
        if (!_quitting && ExitCode != 0)
        {
            MessageBox.Show($"Hey Ganesh stopped (exit {ExitCode}). The details are in the log:\n{_logFolder}", "Hey Ganesh", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        ExitThread();
    }

    private void Post(Action action)
    {
        try
        {
            _ui.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The window is gone: the tray is already exiting.
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _showWait?.Unregister(null);
            _icon.Dispose();
            _ui.Dispose();
            _stop.Dispose();
        }

        base.Dispose(disposing);
    }
}
