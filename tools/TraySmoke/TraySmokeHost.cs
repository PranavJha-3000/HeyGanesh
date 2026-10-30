using System.Text.RegularExpressions;

namespace HeyGanesh.Tools.TraySmoke;

/// <summary>
/// Verification host for the tray (DEC-052). Exit codes: 0 pass, 1 failed check, 2 usage.
/// </summary>
internal static partial class TraySmokeHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "--help";
        Console.WriteLine("Tray verification host (no key, no network)");
        Console.WriteLine();
        try
        {
            return mode switch
            {
                "--assert-source" => RunAssertSource(),
                "--assert-log" => RunAssertLog(),
                "--assert-stop" => await RunAssertStopAsync(),
                "--assert-tray" => RunAssertTray(),
                "--assert-run" => await RunAssertRunAsync(),
                "--assert-keys-model" => RunAssertKeysModel(),
                "--assert-keys-window" => RunAssertKeysWindow(),
                "--assert-keys-pause" => await RunAssertKeysPauseAsync(),
                "--assert-startup" => RunAssertStartup(),
                "--assert-startup-menu" => RunAssertStartupMenu(),
                "--assert-install" => RunAssertInstall(args.Length > 1 ? args[1] : null),
                "--assert-notes-map" => RunAssertNotesMap(),
                "--assert-session" => RunAssertSession(),
                "--assert-notes-shell" => RunAssertNotesShell(),
                "--assert-recent-model" => RunAssertRecentModel(),
                "--assert-replay-prepare" => RunAssertReplayPrepare(),
                "--assert-replay" => await RunAssertReplayAsync(),
                "--assert-recent-window" => RunAssertRecentWindow(),
                "--help" or "-h" or "/?" => PrintUsage(),
                _ => Usage(mode),
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"UNEXPECTED: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static int Usage(string bad)
    {
        Console.WriteLine($"unknown argument '{bad}' (exit 2)");
        return 2;
    }

    private static int PrintUsage()
    {
        Console.WriteLine("Usage: TraySmoke <mode>");
        Console.WriteLine("  --assert-source  WinExe, no PackageReference, App + Security only, no key or console input in the tray; exit 0");
        Console.WriteLine("  --assert-log     one file per run, the newest 5 kept, readable while open, HEYGANESH_DATA_DIR honoured; exit 0");
        Console.WriteLine("  --assert-stop    LOCAL: the real shell's hooked mode ends on the stop token with the hook released; exit 0");
        Console.WriteLine("  --assert-tray    LOCAL: the real icon and menu; a second launch is answered; Quit ends the loop; exit 0");
        Console.WriteLine("  --assert-run     LOCAL: HeyGanesh.Tray.exe end to end: log, no planted key in it, one instance, console flags refused; exit 0");
        Console.WriteLine("  --assert-keys-model   the key screen's rules over an in-memory store: save, refuse, remove, no value in any text; exit 0");
        Console.WriteLine("  --assert-keys-window  LOCAL: the real key screen (masked, writes only on Save) and the tray opening it; exit 0");
        Console.WriteLine("  --assert-keys-pause   LOCAL: PRESSES Ctrl+Alt once: while paused the real shell starts no turn; exit 0");
        Console.WriteLine("  --assert-startup      start at login against a throwaway HKCU key (deleted after); the real Run key only read; exit 0");
        Console.WriteLine("  --assert-startup-menu LOCAL: the tray's Start at login item over a throwaway key; exit 0");
        Console.WriteLine("  --assert-install [package]  LOCAL: install.ps1 / uninstall.ps1 from a built package into temp folders and throwaway HKCU keys; exit 0");
        Console.WriteLine("  --assert-notes-map    the shell's turn-to-note mapping, every case, through a real temporary store (DEC-059); exit 0");
        Console.WriteLine("  --assert-session      the shell's memory of this run: only taught turns, the newest 3, a line each (DEC-061); exit 0");
        Console.WriteLine("  --assert-recent-model   what Recent lessons lists and says: newest first, replayable only with a stored plan, details, delete, clear (DEC-060); exit 0");
        Console.WriteLine("  --assert-replay-prepare a stored plan is validated again before it may play; bad ones refused with a reason; exit 0");
        Console.WriteLine("  --assert-replay         LOCAL: a stored lesson replays on the real cards with no model; a bad one opens nothing; exit 0");
        Console.WriteLine("  --assert-recent-window  LOCAL: the real Recent lessons window from the tray: list, replay pauses Ctrl+Alt, delete, clear; exit 0");
        Console.WriteLine("  --assert-notes-shell  LOCAL: the real HeyGanesh.App.exe with temporary data folders: header, --no-notes, no note on a key error, a broken db kept; refuses to run with keys; exit 0");
        return 0;
    }

    private static int Check(bool ok, string label, int failures)
    {
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {label}");
        return ok ? failures : failures + 1;
    }

    private static int Summarize(int failures, string okLabel)
    {
        Console.WriteLine(failures == 0 ? $"{okLabel} (exit 0)" : $"FAILED: {failures} check(s) (exit 1)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// True when an icon is the Hey Ganesh mark: right of the trunk, halfway down, is bare kraft paper (DEC-056).
    /// Windows' generic application icon has no such colour there.
    /// </summary>
    private static bool IsMark(Icon icon)
    {
        using var bitmap = icon.ToBitmap();
        var c = bitmap.GetPixel(bitmap.Width * 85 / 100, bitmap.Height / 2);
        return Math.Abs(c.R - 214) < 40 && Math.Abs(c.G - 196) < 40 && Math.Abs(c.B - 162) < 40 && c.A > 200;
    }

    private static string RepoRoot()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "HeyGanesh.sln")))
        {
            root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        }

        return root ?? throw new InvalidOperationException("HeyGanesh.sln not found above the host");
    }

    private static string TempFolder(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "heyganesh-traysmoke", name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    // TY-1: the tray is a thin host. It builds no pipeline of its own and never touches a key value.
    private static int RunAssertSource()
    {
        var dir = Path.Combine(RepoRoot(), "src", "Experience", "HeyGanesh.Tray");
        var csproj = File.ReadAllText(Path.Combine(dir, "HeyGanesh.Tray.csproj"));
        var sources = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p => Path.GetFileName(p), File.ReadAllText);
        var refs = Regex.Matches(csproj, "<ProjectReference Include=\"[^\"]*\\\\([^\\\\\"]+)\\.csproj\"").Select(m => m.Groups[1].Value).ToArray();
        string[] needles = { "API_KEY", ".Read(", "Reader(", "Console.Read", "DllImport", "LibraryImport", "HttpClient", "SetForegroundWindow", "new ElementSelectionService", "new VoiceService" };
        var found = needles.Where(n => sources.Values.Any(s => s.Contains(n, StringComparison.Ordinal))).ToArray();
        var shell = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Experience", "HeyGanesh.App", "ShellHost.cs"));

        var failures = 0;
        failures = Check(csproj.Contains("<OutputType>WinExe</OutputType>", StringComparison.Ordinal), "WinExe: Windows opens no console for it", failures);
        failures = Check(!csproj.Contains("<PackageReference", StringComparison.Ordinal), "zero PackageReference (WinForms is a framework reference)", failures);
        failures = Check(refs.SequenceEqual(new[] { "HeyGanesh.App", "HeyGanesh.Security", "HeyGanesh.Notes" }), $"references the App Shell, Security and Notes only ({string.Join(", ", refs)})", failures);
        failures = Check(sources["Program.cs"].Contains("ShellHost.RunAsync(args, stop, hookLive, paused)", StringComparison.Ordinal), "runs the App Shell's own composition through RunAsync(args, stop, hookLive, paused)", failures);
        failures = Check(found.Length == 0, $"no key value read, no console input, no P/Invoke, network, focus stealing or pipeline built here ({(found.Length == 0 ? "none" : string.Join(", ", found))})", failures);
        failures = Check(shell.Contains("RunAsync(string[] args) => RunAsync(args, CancellationToken.None, null);", StringComparison.Ordinal), "the console's RunAsync(args) passes no stop token and no callback, so it is unchanged", failures);
        // DEC-056: the mark, not Windows' generic icon, in every size the shell asks for.
        var ico = File.ReadAllBytes(Path.Combine(dir, "heyganesh.ico"));
        var sizes = Enumerable.Range(0, BitConverter.ToUInt16(ico, 4)).Select(i => ico[6 + i * 16] == 0 ? 256 : ico[6 + i * 16]).ToArray();
        failures = Check(csproj.Contains("<ApplicationIcon>heyganesh.ico</ApplicationIcon>", StringComparison.Ordinal) && csproj.Contains("LogicalName=\"HeyGanesh.Tray.heyganesh.ico\"", StringComparison.Ordinal)
            && new[] { 16, 20, 24, 32, 48, 256 }.All(sizes.Contains) && !sources.Values.Any(s => s.Contains("SystemIcons.Application", StringComparison.Ordinal)),
            $"the Hey Ganesh icon is the exe's icon and an embedded resource, {sizes.Length} sizes ({string.Join(", ", sizes)}); no generic icon (DEC-056)", failures);
        var registry = sources.Where(s => s.Value.Contains("Registry", StringComparison.Ordinal)).Select(s => s.Key).ToArray();
        failures = Check(registry.SequenceEqual(new[] { "StartupEntry.cs" }), $"the registry is touched only by StartupEntry (DEC-054) ({string.Join(", ", registry)})", failures);
        return Summarize(failures, "SOURCE AUDIT OK");
    }
}
