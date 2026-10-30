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
/// The menu shows each key's presence only, never a value or a length. "Keys…" opens the key screen (DEC-053),
/// which also opens by itself at start when a required key is missing; while it is open the shell is told to
/// refuse Ctrl+Alt. "Start at login" (DEC-054) is shown only when the host hands in a <see cref="StartupEntry"/>;
/// its check mark is read from the registry each time the menu opens, and only a click changes it.
/// </remarks>
internal sealed class TrayApp : ApplicationContext
{
    private readonly KeyRing _keys;
    private readonly string _logFolder;
    private readonly CancellationTokenSource _stop = new();
    private readonly Icon _mark = TrayIcons.Load(SystemInformation.SmallIconSize);
    private readonly Control _ui = new();
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _status;
    private readonly ToolStripMenuItem _quit;
    private readonly List<(string Name, ToolStripMenuItem Item)> _keyLines = new();
    private readonly RegisteredWaitHandle? _showWait;
    private readonly StartupEntry? _startup;
    private readonly ToolStripMenuItem? _startupItem;
    private readonly RecentLessonsSource? _recentSource;
    private RecentLessons? _recent;
    private KeyScreen? _keyScreen;
    private volatile bool _keyScreenOpen;
    private bool _quitting;

    /// <param name="runShell">Runs the pipeline until the token fires; calls the action once the hook is live, and asks the
    /// function before each Ctrl+Alt turn whether the trigger is paused. The product passes ShellHost.RunAsync.</param>
    /// <param name="showRequested">Set by a second launch (SingleInstance); null when nothing can signal.</param>
    /// <param name="openKeysIfMissing">Open the key screen at start when a required key is missing (the product: true).</param>
    /// <param name="startup">The start-at-login entry; null shows no such item (the product passes this user's Run key).</param>
    public TrayApp(Func<CancellationToken, Action, Func<bool>, Task<int>> runShell, KeyRing keys, string logFolder, WaitHandle? showRequested, bool openKeysIfMissing = false, StartupEntry? startup = null, RecentLessonsSource? recent = null)
    {
        _keys = keys;
        _startup = startup;
        _recentSource = recent;
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

        menu.Items.Add(new ToolStripMenuItem("Keys…", null, (_, _) => OpenKeys()));
        if (_recentSource is not null)
        {
            menu.Items.Add(new ToolStripMenuItem("Recent lessons…", null, (_, _) => OpenRecent()));
        }

        if (_startup is not null)
        {
            _startupItem = new ToolStripMenuItem("Start at login", null, (_, _) => ToggleStartup());
            menu.Items.Add(_startupItem);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open log folder", null, (_, _) => OpenLogFolder()));
        _quit = new ToolStripMenuItem("Quit", null, (_, _) => Quit());
        menu.Items.Add(_quit);
        menu.Opening += (_, _) =>
        {
            RefreshKeys();
            RefreshStartup();
        };
        RefreshKeys();
        RefreshStartup();

        _icon = new NotifyIcon
        {
            Icon = _mark,
            Text = "Hey Ganesh",
            ContextMenuStrip = menu,
            Visible = true,
        };

        if (showRequested is not null)
        {
            _showWait = ThreadPool.RegisterWaitForSingleObject(showRequested, (_, _) => Post(OnSecondLaunch), null, Timeout.Infinite, executeOnlyOnce: false);
        }

        Console.WriteLine("tray: icon shown; the shell is starting");
        var run = Task.Run(() => runShell(_stop.Token, () => Post(OnHookLive), () => TriggerPaused));
        if (openKeysIfMissing && new KeyScreenModel(_keys).NeedsSetup)
        {
            Console.WriteLine("tray: a required key is missing; opening the key screen");
            Post(OpenKeys);
        }

        run.ContinueWith(t => Post(() => Finish(t)), TaskScheduler.Default);
    }

    /// <summary>The shell's exit code, or 1 if it threw. Set when the message loop ends.</summary>
    public int ExitCode { get; private set; }

    /// <summary>The menu's item texts, separators as "-". For TY-4.</summary>
    internal IReadOnlyList<string> MenuTexts =>
        _icon.ContextMenuStrip!.Items.Cast<ToolStripItem>().Select(i => i is ToolStripSeparator ? "-" : i.Text ?? string.Empty).ToList();

    internal NotifyIcon Icon => _icon;

    /// <summary>The open key screen, or null. For TK-2.</summary>
    internal KeyScreen? OpenKeyScreen => _keyScreen;

    /// <summary>What the shell is told before each Ctrl+Alt turn. For TK-2.</summary>
    internal bool TriggerPaused => _keyScreenOpen || (_recent?.Replaying ?? false);

    /// <summary>The open Recent lessons window, or null. For RL-4.</summary>
    internal RecentLessons? OpenRecentLessons => _recent;

    /// <summary>
    /// The menu's Recent lessons… (DEC-060): one window at a time; a second click brings it forward. The window opens
    /// its own connection to the notes store (WAL lets the shell keep writing). A store that cannot open is said in a
    /// balloon and the log, never thrown.
    /// </summary>
    internal void OpenRecent()
    {
        if (_quitting || _recentSource is null)
        {
            return;
        }

        if (_recent is not null)
        {
            _recent.Activate();
            return;
        }

        RecentLessonsModel model;
        try
        {
            model = new RecentLessonsModel(_recentSource.Open());
        }
        catch (Exception ex) when (ex is Notes.NotesException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"tray: recent lessons unavailable ({ex.Message})");
            _icon.ShowBalloonTip(4000, "Hey Ganesh", "Saved lessons cannot be opened right now. The details are in the log.", ToolTipIcon.Warning);
            return;
        }

        _recent = new RecentLessons(model, plan => _recentSource.Replay(plan, _stop.Token));
        _recent.FormClosed += (_, _) =>
        {
            _recent.Dispose();
            _recent = null;
        };
        Console.WriteLine("tray: recent lessons open");
        _recent.Show();
        _recent.Activate();
    }

    /// <summary>The "Start at login" item's text and check mark. For TS-2.</summary>
    internal (string Text, bool Checked)? StartupItem => _startupItem is null ? null : (_startupItem.Text ?? string.Empty, _startupItem.Checked);

    /// <summary>The menu's "Start at login": on turns it off; off, or set for another copy, points it at this one.</summary>
    internal void ToggleStartup()
    {
        if (_startup is null)
        {
            return;
        }

        try
        {
            var turnOn = _startup.State != StartupState.On;
            if (turnOn)
            {
                _startup.Enable();
            }
            else
            {
                _startup.Disable();
            }

            Console.WriteLine($"tray: start at login turned {(turnOn ? "on" : "off")}");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Console.WriteLine($"tray: start at login could not be changed ({ex.GetType().Name})");
            _icon.ShowBalloonTip(4000, "Hey Ganesh", "Start at login could not be changed. The details are in the log.", ToolTipIcon.Warning);
        }

        RefreshStartup();
    }

    /// <summary>The menu's Keys…: one screen at a time; a second click brings it forward.</summary>
    internal void OpenKeys()
    {
        if (_quitting)
        {
            return;
        }

        if (_keyScreen is not null)
        {
            _keyScreen.Activate();
            return;
        }

        _keyScreen = new KeyScreen(new KeyScreenModel(_keys));
        _keyScreen.FormClosed += (_, _) =>
        {
            _keyScreenOpen = false;
            _keyScreen.Dispose();
            _keyScreen = null;
            RefreshKeys();
            Console.WriteLine("tray: key screen closed; Ctrl+Alt asks again");
        };
        _keyScreenOpen = true;
        Console.WriteLine("tray: key screen open; Ctrl+Alt is paused until it closes");
        _keyScreen.Show();
        _keyScreen.Activate();
    }

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

    private void RefreshStartup()
    {
        if (_startup is null || _startupItem is null)
        {
            return;
        }

        var state = _startup.State;
        _startupItem.Checked = state == StartupState.On;
        _startupItem.Text = state == StartupState.Elsewhere ? "Start at login (set for another copy)" : "Start at login";
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
        _keyScreen?.Close();
        _recent?.Close();
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
            _mark.Dispose();
            _ui.Dispose();
            _stop.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>Where the Recent lessons window gets its notes and how it replays one (DEC-060). Null: no menu item.</summary>
internal sealed record RecentLessonsSource(Func<Notes.NotesStore> Open, Func<string, CancellationToken, Task<string>> Replay);
