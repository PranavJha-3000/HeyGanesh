using System.Diagnostics;
using Microsoft.Win32;

namespace HeyGanesh.Tools.TraySmoke;

internal static partial class TraySmokeHost
{
    // IN-2 (DEC-055): the real install.ps1 / uninstall.ps1 from a built package, in Windows PowerShell 5.1 (what a
    // user's PC runs), into a temporary folder, a temporary Start menu and throwaway HKCU keys. The installed tray
    // is started once. The real install folder, shortcut, Apps & features entry and Run value are only read.
    private static int RunAssertInstall(string? packageArg)
    {
        var failures = 0;
        var package = Path.GetFullPath(packageArg ?? Path.Combine(RepoRoot(), "artifacts", "installer"));
        failures = Check(File.Exists(Path.Combine(package, "install.ps1")) && File.Exists(Path.Combine(package, "app", "HeyGanesh.Tray.exe")),
            $"a built package is there ({package}; build it with installer\\build.ps1)", failures);
        if (failures > 0)
        {
            return Summarize(failures, "INSTALL OK");
        }

        var realBefore = RealInstallState();
        var root = TempFolder("install");
        var target = Path.Combine(root, "Programs", "HeyGanesh");
        var shortcuts = Path.Combine(root, "StartMenu");
        var testKey = $@"{TestRoot}\Test-{Guid.NewGuid():N}";
        var uninstallKey = $@"HKCU:\{testKey}\Uninstall\HeyGanesh";
        var runKey = $@"HKCU:\{testKey}\Run";
        var exe = Path.Combine(target, "HeyGanesh.Tray.exe");
        string[] places = { "-Target", target, "-Shortcuts", shortcuts, "-UninstallKey", uninstallKey };

        try
        {
            var (code, said) = PowerShell(Path.Combine(package, "install.ps1"), places);
            failures = Check(code == 0 && said.Contains("INSTALLED: Hey Ganesh", StringComparison.Ordinal), $"install.ps1 installs (exit {code})", failures);
            failures = Check(File.Exists(exe) && File.Exists(Path.Combine(target, "uninstall.ps1")) && File.Exists(Path.Combine(target, ".heyganesh-install")),
                "the folder holds the tray, uninstall.ps1 and the install marker", failures);
            failures = Check(File.Exists(Path.Combine(shortcuts, "Hey Ganesh.lnk")), "a 'Hey Ganesh' Start-menu shortcut", failures);
            using (var entry = Registry.CurrentUser.OpenSubKey($@"{testKey}\Uninstall\HeyGanesh"))
            {
                var uninstall = entry?.GetValue("UninstallString") as string ?? string.Empty;
                failures = Check(entry?.GetValue("DisplayName") as string == "Hey Ganesh" && entry.GetValue("InstallLocation") as string == target
                    && uninstall.Contains(Path.Combine(target, "uninstall.ps1"), StringComparison.Ordinal) && entry.GetValue("DisplayVersion") is string { Length: > 0 },
                    $"an Apps & features entry: name, version, location, and an uninstall command that runs the installed uninstall.ps1", failures);
            }

            failures = Check(Registry.CurrentUser.OpenSubKey($@"{testKey}\Run") is null, "installing does not turn on start at login", failures);

            // The installed copy runs on its own: no repo, no SDK paths, the bundled runtime.
            var data = TempFolder("install-data");
            using (var tray = Start(exe, data, new Dictionary<string, string>(), "--seconds", "3"))
            {
                var ended = tray.WaitForExit(30000);
                StopIfRunning(tray);
                var log = Directory.GetFiles(Path.Combine(data, "logs"), "shell-*.log").Select(ReadShared).FirstOrDefault() ?? string.Empty;
                failures = Check(ended && tray.ExitCode == 0 && log.Contains("hook live: IsRunning=True", StringComparison.Ordinal) && log.Contains("tray: exit 0", StringComparison.Ordinal),
                    $"the installed tray starts, installs its hook and exits 0 after --seconds 3 ({(ended ? tray.ExitCode : -1)})", failures);
            }

            // While it runs, install and uninstall both refuse, and change nothing.
            using (var running = Start(exe, TempFolder("install-data"), new Dictionary<string, string>(), "--seconds", "12"))
            {
                Thread.Sleep(2500);
                var (busyInstall, busySaid) = PowerShell(Path.Combine(package, "install.ps1"), places);
                var (busyUninstall, _) = PowerShell(Path.Combine(target, "uninstall.ps1"), "-Shortcuts", shortcuts, "-UninstallKey", uninstallKey, "-RunKey", runKey);
                failures = Check(busyInstall == 1 && busySaid.Contains("is running", StringComparison.Ordinal) && busyUninstall == 1 && File.Exists(exe),
                    $"with the tray running, install and uninstall refuse (exits {busyInstall}, {busyUninstall}) and the files stay", failures);
                running.WaitForExit(30000);
                StopIfRunning(running);
            }

            // An upgrade replaces the old files.
            var stale = Path.Combine(target, "left-from-an-old-version.dll");
            File.WriteAllText(stale, "old");
            (code, said) = PowerShell(Path.Combine(package, "install.ps1"), places);
            failures = Check(code == 0 && said.Contains("upgrading", StringComparison.Ordinal) && !File.Exists(stale) && File.Exists(exe), $"installing again upgrades: old files are gone (exit {code})", failures);

            // A folder that is not ours is never touched.
            var foreign = Path.Combine(root, "SomeoneElse");
            Directory.CreateDirectory(foreign);
            File.WriteAllText(Path.Combine(foreign, "keep.txt"), "mine");
            var (foreignCode, foreignSaid) = PowerShell(Path.Combine(package, "install.ps1"), "-Target", foreign, "-Shortcuts", shortcuts, "-UninstallKey", uninstallKey);
            var (foreignUninstall, _) = PowerShell(Path.Combine(target, "uninstall.ps1"), "-Target", foreign, "-Shortcuts", shortcuts, "-UninstallKey", uninstallKey, "-RunKey", runKey);
            failures = Check(foreignCode == 1 && foreignSaid.Contains("not a Hey Ganesh install", StringComparison.Ordinal) && foreignUninstall == 1 && File.ReadAllText(Path.Combine(foreign, "keep.txt")) == "mine",
                $"a non-empty folder that is not a Hey Ganesh install is refused by both scripts and left as it was (exits {foreignCode}, {foreignUninstall})", failures);

            // Uninstall: start at login is removed when it starts this install.
            using (var run = Registry.CurrentUser.CreateSubKey($@"{testKey}\Run"))
            {
                run.SetValue("HeyGanesh", $"\"{exe}\"");
                run.SetValue("SomethingElse", "\"C:\\other.exe\"");
            }

            (code, said) = PowerShell(Path.Combine(target, "uninstall.ps1"), "-Shortcuts", shortcuts, "-UninstallKey", uninstallKey, "-RunKey", runKey);
            failures = Check(code == 0 && said.Contains("UNINSTALLED", StringComparison.Ordinal), $"the installed uninstall.ps1 runs from inside the folder it removes (exit {code})", failures);
            failures = Check(!Directory.Exists(target) && !File.Exists(Path.Combine(shortcuts, "Hey Ganesh.lnk")) && Registry.CurrentUser.OpenSubKey($@"{testKey}\Uninstall\HeyGanesh") is null,
                "the folder, the shortcut and the Apps & features entry are gone", failures);
            using (var run = Registry.CurrentUser.OpenSubKey($@"{testKey}\Run"))
            {
                failures = Check(run?.GetValue("HeyGanesh") is null && run?.GetValue("SomethingElse") as string == "\"C:\\other.exe\"",
                    "start at login for this install is removed; other Run values are untouched", failures);
            }

            failures = Check(said.Contains("keys (Credential Manager) and logs", StringComparison.Ordinal), "it says the keys and logs were kept", failures);
        }
        finally
        {
            foreach (var p in Process.GetProcessesByName("HeyGanesh.Tray").Where(p => (TryPath(p) ?? string.Empty).StartsWith(root, StringComparison.OrdinalIgnoreCase)))
            {
                p.Kill();
                p.WaitForExit(5000);
            }

            DeleteTestKey(testKey);
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        failures = Check(RealInstallState() == realBefore, $"the real install folder, shortcut, Apps & features entry and Run value are as they were ({realBefore})", failures);
        return Summarize(failures, "INSTALL OK");
    }

    /// <summary>Runs a script in Windows PowerShell 5.1, the one every Windows PC has, and returns its exit code and output.</summary>
    private static (int Code, string Output) PowerShell(string script, params string[] args)
    {
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script }.Concat(args))
        {
            info.ArgumentList.Add(a);
        }

        using var process = Process.Start(info)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + stderr.Result);
    }

    private static string? TryPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Whether the real per-user install exists, as four yes/no facts. Read only.</summary>
    private static string RealInstallState()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        using var entry = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\HeyGanesh");
        return $"folder={Directory.Exists(Path.Combine(local, "Programs", "HeyGanesh"))}, "
             + $"shortcut={File.Exists(Path.Combine(roaming, "Microsoft", "Windows", "Start Menu", "Programs", "Hey Ganesh.lnk"))}, "
             + $"entry={entry is not null}, run={RealRunValue() is not null}";
    }
}
