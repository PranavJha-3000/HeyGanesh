using HeyGanesh.App;
using HeyGanesh.Security;

namespace HeyGanesh.Tray;

/// <summary>
/// The tray's entry point (DEC-052). Order matters: DPI first (process-global, irreversible, the same call the
/// console's Program.cs makes), then the one-instance check (a second hook would answer every Ctrl+Alt twice),
/// then the log, then the icon. Exit codes: the shell's own (0 after Quit), 2 for a console-only flag, 3 when
/// another tray already runs in this session.
/// </summary>
internal static class Program
{
    /// <summary>Exit code of a second launch: the first instance keeps running and shows a balloon.</summary>
    public const int AlreadyRunning = 3;

    /// <summary>
    /// Flags that need a console the tray does not have: key entry reads hidden keystrokes, and --once and --help
    /// are for scripted and terminal use. They stay on HeyGanesh.App.exe.
    /// </summary>
    private static readonly string[] ConsoleOnly = { "--help", "-h", "/?", "--keys", "--set-key", "--remove-key", "--once" };

    [STAThread]
    private static int Main(string[] args)
    {
        NativeFns.SetProcessDpiAwarenessContext(NativeFns.PerMonitorV2);

        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            SingleInstance.SignalFirst();
            return AlreadyRunning;
        }

        using var log = TrayLog.Open(TrayLog.DefaultFolder(), DateTime.Now);
        Console.SetOut(log.Writer);
        Console.SetError(log.Writer);

        var refused = args.FirstOrDefault(a => ConsoleOnly.Contains(a, StringComparer.Ordinal));
        if (refused is not null)
        {
            Console.WriteLine($"tray: '{refused}' needs a console; run HeyGanesh.App.exe {refused} instead (exit 2)");
            return 2;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var keys = new KeyRing();
        using var app = new TrayApp((stop, hookLive, paused) => ShellHost.RunAsync(args, stop, hookLive, paused), keys, log.Folder, instance.ShowRequested, openKeysIfMissing: true,
            startup: StartupEntry.ForThisUser(Environment.ProcessPath!),
            recent: new RecentLessonsSource(
                () => Notes.NotesStore.Open(Notes.NotesStore.DefaultPath()),
                async (plan, ct) => (await ShellHost.ReplayAsync(plan, ct)).Said));
        Application.Run(app);
        Console.WriteLine($"tray: exit {app.ExitCode}");
        return app.ExitCode;
    }
}
