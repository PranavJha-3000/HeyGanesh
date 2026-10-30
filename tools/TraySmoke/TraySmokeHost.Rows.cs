using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using HeyGanesh.App;
using HeyGanesh.Security;
using HeyGanesh.Tray;

namespace HeyGanesh.Tools.TraySmoke;

internal static partial class TraySmokeHost
{
    // TY-2: one log per run, the newest five kept, and the shell's data-folder convention.
    private static int RunAssertLog()
    {
        var failures = 0;
        var folder = TempFolder("log");
        for (var i = 1; i <= 7; i++)
        {
            File.WriteAllText(Path.Combine(folder, $"shell-20260101-00000{i}.log"), "old");
        }

        File.WriteAllText(Path.Combine(folder, "notes.txt"), "not a log");
        var now = new DateTime(2026, 10, 30, 9, 15, 0);
        string firstPath;
        using (var log = TrayLog.Open(folder, now))
        {
            firstPath = log.Path;
            log.Writer.WriteLine("hook live: IsRunning=True");
            var whileOpen = ReadShared(log.Path);
            failures = Check(whileOpen.Contains("hook live", StringComparison.Ordinal), "a line written is on disk at once and readable while the log is open", failures);
            failures = Check(Path.GetFileName(log.Path) == "shell-20261030-091500.log" && log.Folder == folder, $"named by the run's start time ({Path.GetFileName(log.Path)})", failures);

            var logs = Directory.GetFiles(folder, "shell-*.log").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            failures = Check(logs.Length == TrayLog.Keep && logs[0] == "shell-20260101-000004.log" && logs.Contains("shell-20261030-091500.log"),
                $"7 old logs + this run -> the newest 5 kept ({string.Join(", ", logs)})", failures);
            failures = Check(File.Exists(Path.Combine(folder, "notes.txt")), "a file that is not a run log is left alone", failures);

            using var same = TrayLog.Open(folder, now);
            failures = Check(same.Path != log.Path && Path.GetFileName(same.Path) == "shell-20261030-091500-2.log", $"a second run in the same second gets its own file ({Path.GetFileName(same.Path)})", failures);
        }

        var bytes = File.ReadAllBytes(firstPath);
        failures = Check(bytes.Length > 3 && !(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF), "UTF-8 without a BOM", failures);

        var saved = Environment.GetEnvironmentVariable("HEYGANESH_DATA_DIR");
        try
        {
            Environment.SetEnvironmentVariable("HEYGANESH_DATA_DIR", folder);
            var custom = TrayLog.DefaultFolder();
            Environment.SetEnvironmentVariable("HEYGANESH_DATA_DIR", null);
            var standard = TrayLog.DefaultFolder();
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            failures = Check(custom == Path.Combine(folder, "logs") && standard == Path.Combine(local, "HeyGanesh", "logs"),
                "the folder is $HEYGANESH_DATA_DIR/logs, else %LOCALAPPDATA%/HeyGanesh/logs", failures);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HEYGANESH_DATA_DIR", saved);
        }

        Directory.Delete(folder, recursive: true);
        return Summarize(failures, "LOG OK");
    }

    // TY-3: the real shell, the real keyboard hook. The stop token ends hooked mode like Ctrl+C, with the same teardown.
    private static async Task<int> RunAssertStopAsync()
    {
        var failures = 0;

        var (code, live, stoppedIn, output) = await RunShellAsync(stopAfterLive: TimeSpan.FromMilliseconds(500));
        failures = Check(live, "hookLive was called once the hook was installed", failures);
        failures = Check(code == 0 && stoppedIn < TimeSpan.FromSeconds(3), $"the stop token ended hooked mode: exit {code} in {stoppedIn.TotalMilliseconds:F0} ms (no session in flight)", failures);
        failures = Check(output.Contains("hook live: IsRunning=True", StringComparison.Ordinal) && output.Contains("still running       : False", StringComparison.Ordinal),
            "the same ordered teardown as Ctrl+C: the hook was live and is released before the shell returns", failures);
        failures = Check(!output.Contains("auto-stop", StringComparison.Ordinal), "no --seconds was given, so only the token could have stopped it", failures);
        var notesLine = output.Split('\n').FirstOrDefault(l => l.Contains("notes      :", StringComparison.Ordinal))?.Trim() ?? "(no notes line)";
        failures = Check(notesLine.Contains(Path.Combine(Path.GetTempPath(), "heyganesh-traysmoke"), StringComparison.OrdinalIgnoreCase),
            $"the in-process shell's notes go to a temporary folder, never the real data folder ({notesLine})", failures);

        var (early, earlyLive, earlyIn, _) = await RunShellAsync(stopAfterLive: null);
        failures = Check(early == 0 && earlyLive && earlyIn < TimeSpan.FromSeconds(20), $"a token already cancelled before the run still ends cleanly: exit {early} in {earlyIn.TotalMilliseconds:F0} ms", failures);
        return Summarize(failures, "STOP OK");
    }

    /// <summary>Runs ShellHost.RunAsync in hooked mode with console output captured; null means the token is cancelled up front.</summary>
    private static async Task<(int Code, bool Live, TimeSpan StoppedIn, string Output)> RunShellAsync(TimeSpan? stopAfterLive)
    {
        // The shell opens its notes store (DEC-059): never let a test run reach the real data folder.
        var savedData = Environment.GetEnvironmentVariable("HEYGANESH_DATA_DIR");
        Environment.SetEnvironmentVariable("HEYGANESH_DATA_DIR", TempFolder("shell-data"));
        var original = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(TextWriter.Synchronized(captured));
        using var stop = new CancellationTokenSource();
        var live = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = Stopwatch.StartNew();
        try
        {
            if (stopAfterLive is null)
            {
                stop.Cancel();
            }

            // On the pool, as the tray runs it: hooked mode blocks its thread until stopped, before any await.
            var run = Task.Run(() => ShellHost.RunAsync(Array.Empty<string>(), stop.Token, () => live.TrySetResult()));
            if (stopAfterLive is { } wait)
            {
                await Task.WhenAny(live.Task, run, Task.Delay(TimeSpan.FromSeconds(20)));
                await Task.Delay(wait);
                clock.Restart();
                stop.Cancel();
            }

            var code = await run.WaitAsync(TimeSpan.FromSeconds(30));
            return (code, live.Task.IsCompleted, clock.Elapsed, captured.ToString());
        }
        finally
        {
            Console.SetOut(original);
            Environment.SetEnvironmentVariable("HEYGANESH_DATA_DIR", savedData);
        }
    }

    // TY-4: the real NotifyIcon on the real desktop, driven from this thread while its loop runs on its own STA thread.
    private static int RunAssertTray()
    {
        var failures = 0;
        var original = Console.Out;
        var captured = new Tee(original);
        Console.SetOut(TextWriter.Synchronized(captured));
        var foregroundBefore = GetForegroundWindow();
        using var keys = new KeyRing(new EmptyStore(), name => name == "DEEPSEEK_API_KEY" ? "made-up-not-a-key" : null);
        using var show = new AutoResetEvent(false);
        var stopSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new ManualResetEventSlim(false);
        TrayApp? tray = null;

        var loop = new Thread(() =>
        {
            Application.EnableVisualStyles();
            tray = new TrayApp(async (stop, hookLive, _) =>
            {
                hookLive();
                try
                {
                    await Task.Delay(Timeout.Infinite, stop);
                }
                catch (OperationCanceledException)
                {
                    stopSeen.TrySetResult();
                }

                return 7;
            }, keys, TempFolder("tray"), show);
            ready.Set();
            Application.Run(tray);
        });
        loop.SetApartmentState(ApartmentState.STA);
        loop.IsBackground = true; // a tray that never ends (a failing run) must not keep this host alive
        loop.Start();

        try
        {
            failures = Check(ready.Wait(TimeSpan.FromSeconds(10)), "the tray started its message loop", failures);
            Thread.Sleep(500);
            var app = tray!;

            var registered = app.OnUi(() => IconRegistered(app.Icon));
            failures = Check(app.OnUi(() => app.Icon.Icon is { } shown && IsMark(shown)), "the notification area shows the Hey Ganesh mark, not the generic icon (DEC-056)", failures);
            failures = Check(app.OnUi(() => app.Icon.Visible) && registered, $"the icon is visible and registered with the notification area ({(registered ? "Shell_NotifyIconGetRect S_OK" : "not found")})", failures);

            var texts = app.OnUi(() => app.MenuTexts);
            var expected = new[] { "Listening: hold Ctrl+Alt to ask", "deepseek key: present", "typesafe key: missing", "fish key: missing", "Keys…", "-", "Open log folder", "Quit" };
            failures = Check(texts.SequenceEqual(expected), $"the menu: status, one presence line per key, Keys…, Open log folder, Quit ({string.Join(" | ", texts)})", failures);
            failures = Check(!texts.Any(t => t.Contains("made-up", StringComparison.Ordinal) || t.Contains("chars", StringComparison.Ordinal)), "no key value, and not even a length, is on the menu", failures);
            failures = Check(GetForegroundWindow() == foregroundBefore, "showing the icon did not take the foreground", failures);

            show.Set();
            failures = Check(SpinUntil(() => captured.ToString().Contains("a second launch was refused", StringComparison.Ordinal), 3000), "a second launch's signal is answered (logged, with a balloon)", failures);

            var clock = Stopwatch.StartNew();
            app.OnUi(() => { app.Quit(); return 0; });
            var ended = loop.Join(TimeSpan.FromSeconds(5));
            failures = Check(stopSeen.Task.IsCompleted && ended && clock.Elapsed < TimeSpan.FromSeconds(3), $"Quit cancels the shell's token and the loop ends once the shell returns ({clock.Elapsed.TotalMilliseconds:F0} ms)", failures);
            failures = Check(app.ExitCode == 7, $"the tray's exit code is the shell's ({app.ExitCode})", failures);
            app.Dispose();
        }
        finally
        {
            Console.SetOut(original);
        }

        // A shell that ends on its own (exit 0, as after --seconds) ends the loop too, with no message box.
        var selfEnded = new Thread(() =>
        {
            using var second = new TrayApp(async (_, hookLive, _) => { hookLive(); await Task.Delay(200); return 0; }, keys, TempFolder("tray"), null);
            Application.Run(second);
        });
        selfEnded.SetApartmentState(ApartmentState.STA);
        selfEnded.IsBackground = true;
        selfEnded.Start();
        failures = Check(selfEnded.Join(TimeSpan.FromSeconds(5)), "a shell that returns by itself ends the tray", failures);
        return Summarize(failures, "TRAY OK");
    }

    // TY-5: the real HeyGanesh.Tray.exe, twice. Made-up keys are planted in its environment; none may reach the log.
    private static async Task<int> RunAssertRunAsync()
    {
        var failures = 0;
        var exe = Path.Combine(RepoRoot(), "src", "Experience", "HeyGanesh.Tray", "bin", "Debug", "net8.0-windows10.0.22621.0", "HeyGanesh.Tray.exe");
        failures = Check(File.Exists(exe), "HeyGanesh.Tray.exe is built", failures);
        if (failures > 0)
        {
            return Summarize(failures, "RUN OK");
        }

        var data = TempFolder("run");
        var planted = new Dictionary<string, string>
        {
            ["DEEPSEEK_API_KEY"] = "hgplanted-deepseek-" + Guid.NewGuid().ToString("N"),
            ["TYPESAFE_API_KEY"] = "hgplanted-typesafe-" + Guid.NewGuid().ToString("N"),
            ["FISH_AUDIO_API_KEY"] = "hgplanted-fish-" + Guid.NewGuid().ToString("N"),
        };

        var clock = Stopwatch.StartNew();
        using var first = Start(exe, data, planted, "--seconds", "6");
        await Task.Delay(2500);
        using var second = Start(exe, data, planted);
        var secondEnded = second.WaitForExit(5000);
        failures = Check(secondEnded && second.ExitCode == 3 && !first.HasExited, $"a second launch exits 3 at once and the first keeps running ({(secondEnded ? second.ExitCode : -1)})", failures);

        var firstEnded = first.WaitForExit(30000);
        // A failing run must not leave a tray (and its keyboard hook) behind.
        StopIfRunning(first);
        StopIfRunning(second);
        failures = Check(firstEnded && first.ExitCode == 0, $"the first ends by itself after --seconds 6, exit {(firstEnded ? first.ExitCode : -1)} ({clock.Elapsed.TotalSeconds:F1} s)", failures);

        var logs = Directory.GetFiles(Path.Combine(data, "logs"), "shell-*.log");
        var text = logs.Length == 1 ? ReadShared(logs[0]) : string.Empty;
        failures = Check(logs.Length == 1, $"one log for one run: the refused second launch writes none ({logs.Length})", failures);
        failures = Check(text.Contains("hook live: IsRunning=True", StringComparison.Ordinal) && text.Contains("still running       : False", StringComparison.Ordinal) && text.Contains("tray: exit 0", StringComparison.Ordinal),
            "the log holds the shell's own lines: hook live, the hook released, exit 0", failures);
        failures = Check(text.Contains("a second launch was refused", StringComparison.Ordinal), "the first instance logged the second launch", failures);
        failures = Check(planted.Values.All(v => !text.Contains(v, StringComparison.Ordinal) && !text.Contains(v[^12..], StringComparison.Ordinal)), "no planted key value (or its tail) is in the log", failures);

        var refusedData = TempFolder("refused");
        using var console = Start(exe, refusedData, planted, "--set-key", "deepseek");
        var consoleEnded = console.WaitForExit(10000);
        var refusedLog = Directory.GetFiles(Path.Combine(refusedData, "logs"), "shell-*.log").Select(ReadShared).FirstOrDefault() ?? string.Empty;
        failures = Check(consoleEnded && console.ExitCode == 2 && refusedLog.Contains("needs a console", StringComparison.Ordinal), $"a console-only flag (--set-key) is refused with exit 2 and a log line ({(consoleEnded ? console.ExitCode : -1)})", failures);

        Directory.Delete(data, recursive: true);
        Directory.Delete(refusedData, recursive: true);
        return Summarize(failures, "RUN OK");
    }

    private static void StopIfRunning(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill();
            process.WaitForExit(5000);
        }
    }

    private static Process Start(string exe, string data, IReadOnlyDictionary<string, string> env, params string[] args)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var a in args)
        {
            info.ArgumentList.Add(a);
        }

        info.Environment["HEYGANESH_DATA_DIR"] = data;
        foreach (var (name, value) in env)
        {
            info.Environment[name] = value;
        }

        return Process.Start(info)!;
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static bool SpinUntil(Func<bool> condition, int milliseconds) => SpinWait.SpinUntil(() => { Thread.Sleep(50); return condition(); }, milliseconds);

    /// <summary>
    /// Asks the shell for the icon's rectangle: S_OK only when an icon with that window and id is registered.
    /// NotifyIcon keeps both private, so they are read by type, not by name, to survive field renames.
    /// </summary>
    private static bool IconRegistered(NotifyIcon icon)
    {
        var fields = typeof(NotifyIcon).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var window = fields.Select(f => f.GetValue(icon)).OfType<NativeWindow>().FirstOrDefault();
        var id = fields.FirstOrDefault(f => f.FieldType == typeof(uint) && f.Name.Contains("id", StringComparison.OrdinalIgnoreCase))?.GetValue(icon);
        if (window is null || id is null)
        {
            return false;
        }

        var identifier = new NotifyIconIdentifier { Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(), Window = window.Handle, Id = (uint)id };
        return Shell_NotifyIconGetRect(ref identifier, out _) == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public Guid Item;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out Rect location);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>Keeps what is written (to search it) and still shows it, so the checks stay on screen.</summary>
    private sealed class Tee : TextWriter
    {
        private readonly TextWriter _shown;
        private readonly StringBuilder _kept = new();

        public Tee(TextWriter shown) => _shown = shown;

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            _kept.Append(value);
            _shown.Write(value);
        }

        public override void Write(string? value)
        {
            _kept.Append(value);
            _shown.Write(value);
        }

        public override string ToString() => _kept.ToString();
    }

    /// <summary>A Credential Manager with nothing in it, so the menu's key lines come only from the made-up environment.</summary>
    private sealed class EmptyStore : ISecretStore
    {
        public SecretHandle? Get(string providerId, string keyAlias) => null;

        public void Set(string providerId, string keyAlias, ReadOnlyMemory<byte> secret) => throw new InvalidOperationException("TY-4 never stores a key");

        public bool Exists(string providerId, string keyAlias) => false;

        public void Delete(string providerId, string keyAlias)
        {
        }

        public void Dispose()
        {
        }
    }
}
