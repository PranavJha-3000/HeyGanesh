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
        failures = Check(refs.SequenceEqual(new[] { "HeyGanesh.App", "HeyGanesh.Security" }), $"references the App Shell and Security only ({string.Join(", ", refs)})", failures);
        failures = Check(sources["Program.cs"].Contains("ShellHost.RunAsync(args, stop, hookLive, paused)", StringComparison.Ordinal), "runs the App Shell's own composition through RunAsync(args, stop, hookLive, paused)", failures);
        failures = Check(found.Length == 0, $"no key value read, no console input, no P/Invoke, network, focus stealing or pipeline built here ({(found.Length == 0 ? "none" : string.Join(", ", found))})", failures);
        failures = Check(shell.Contains("RunAsync(string[] args) => RunAsync(args, CancellationToken.None, null);", StringComparison.Ordinal), "the console's RunAsync(args) passes no stop token and no callback, so it is unchanged", failures);
        return Summarize(failures, "SOURCE AUDIT OK");
    }
}
