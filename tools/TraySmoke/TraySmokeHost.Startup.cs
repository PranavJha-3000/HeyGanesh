using HeyGanesh.Security;
using HeyGanesh.Tray;
using Microsoft.Win32;

namespace HeyGanesh.Tools.TraySmoke;

internal static partial class TraySmokeHost
{
    private const string TestRoot = @"Software\HeyGanesh";

    /// <summary>A throwaway <c>HKCU\Software\HeyGanesh\Test-&lt;id&gt;\Run</c>: never the real Run key, deleted after.</summary>
    private static string TestRunKey(out string testKey)
    {
        testKey = $@"{TestRoot}\Test-{Guid.NewGuid():N}";
        return testKey + @"\Run";
    }

    private static void DeleteTestKey(string testKey)
    {
        Registry.CurrentUser.DeleteSubKeyTree(testKey, throwOnMissingSubKey: false);
        using var root = Registry.CurrentUser.OpenSubKey(TestRoot);
        if (root is { SubKeyCount: 0, ValueCount: 0 })
        {
            Registry.CurrentUser.DeleteSubKey(TestRoot, throwOnMissingSubKey: false);
        }
    }

    private static string? RealRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupEntry.RunKeyPath);
        return key?.GetValue(StartupEntry.ValueName) as string;
    }

    // TS-1: the entry's rules against a throwaway key. The real Run key is only read, before and after.
    private static int RunAssertStartup()
    {
        var failures = 0;
        var realBefore = RealRunValue();
        var run = TestRunKey(out var testKey);
        try
        {
            const string Exe = @"C:\Program Files\Hey Ganesh\HeyGanesh.Tray.exe";
            var entry = new StartupEntry(run, Exe);
            failures = Check(entry.State == StartupState.Off && entry.CurrentCommand is null, "a fresh key: off, nothing written", failures);

            entry.Enable();
            failures = Check(entry.State == StartupState.On && entry.CurrentCommand == "\"" + Exe + "\"", $"on: the value holds the quoted path, so a folder with spaces works ({entry.CurrentCommand})", failures);

            var older = new StartupEntry(run, @"D:\old build\HeyGanesh.Tray.exe");
            failures = Check(older.State == StartupState.Elsewhere, "another copy sees the entry as set for someone else", failures);
            older.Enable();
            failures = Check(entry.State == StartupState.Elsewhere && older.State == StartupState.On, "the other copy can point it at itself; this copy then sees 'elsewhere'", failures);
            entry.Enable();
            failures = Check(entry.State == StartupState.On && entry.CurrentCommand == "\"" + Exe + "\"", "choosing it again here points it back at this copy, one value overwritten", failures);

            entry.Disable();
            failures = Check(entry.State == StartupState.Off && entry.CurrentCommand is null, "off: the value is removed", failures);
            entry.Disable();
            failures = Check(entry.State == StartupState.Off, "turning it off twice does nothing and does not throw", failures);

            var product = StartupEntry.ForThisUser(Exe);
            failures = Check(product.KeyPath == @"Software\Microsoft\Windows\CurrentVersion\Run" && StartupEntry.ValueName == "HeyGanesh",
                "the product's entry is this user's Run key, value HeyGanesh (read here, never written)", failures);
        }
        finally
        {
            DeleteTestKey(testKey);
        }

        using (var leftover = Registry.CurrentUser.OpenSubKey(testKey))
        {
            failures = Check(leftover is null, "the throwaway key is gone afterwards", failures);
        }

        failures = Check(RealRunValue() == realBefore, $"the real Run entry is unchanged ({(realBefore is null ? "absent" : "present")} before and after)", failures);
        return Summarize(failures, "STARTUP OK");
    }

    // TS-2: the menu item on the real tray, over a throwaway key: unchecked until clicked, and every click is one change.
    private static int RunAssertStartupMenu()
    {
        var failures = 0;
        var realBefore = RealRunValue();
        var run = TestRunKey(out var testKey);
        var results = new List<(bool Ok, string Label)>();
        try
        {
            var entry = new StartupEntry(run, @"C:\Program Files\Hey Ganesh\HeyGanesh.Tray.exe");
            using var keys = new KeyRing(new EmptyStore(), _ => "made-up-not-a-key");
            RunSta(() =>
            {
                using var tray = new TrayApp(async (stop, hookLive, _) => { hookLive(); try { await Task.Delay(Timeout.Infinite, stop); } catch (OperationCanceledException) { } return 0; },
                    keys, TempFolder("startup"), null, startup: entry);
                Application.DoEvents();

                // The first line is the status, which turns to "Listening…" whenever the posted hookLive runs; the rest is fixed.
                results.Add((tray.MenuTexts.Skip(1).SequenceEqual(new[] { "deepseek key: present", "typesafe key: present", "fish key: present", "Keys…", "Start at login", "-", "Open log folder", "Quit" }),
                    $"the menu has Start at login after Keys… ({string.Join(" | ", tray.MenuTexts)})"));
                results.Add((tray.StartupItem == ("Start at login", false) && entry.State == StartupState.Off, "unchecked, and nothing written, until it is chosen"));

                tray.ToggleStartup();
                results.Add((tray.StartupItem == ("Start at login", true) && entry.State == StartupState.On, "one click: on, checked"));

                new StartupEntry(run, @"D:\old build\HeyGanesh.Tray.exe").Enable();
                tray.ToggleStartup(); // as if the menu were opened and clicked after another copy took the entry
                results.Add((tray.StartupItem == ("Start at login", true) && entry.State == StartupState.On, "set for another copy, a click points it at this one"));

                tray.ToggleStartup();
                results.Add((tray.StartupItem == ("Start at login", false) && entry.State == StartupState.Off, "another click: off, unchecked, the value removed"));

                new StartupEntry(run, @"D:\old build\HeyGanesh.Tray.exe").Enable();
                using var reopened = new TrayApp(async (stop, hookLive, _) => { hookLive(); try { await Task.Delay(Timeout.Infinite, stop); } catch (OperationCanceledException) { } return 0; },
                    keys, TempFolder("startup"), null, startup: entry);
                results.Add((reopened.StartupItem == ("Start at login (set for another copy)", false), $"an entry for another copy is said on the menu ({reopened.StartupItem})"));
                tray.Quit();
                reopened.Quit();
            });
        }
        finally
        {
            DeleteTestKey(testKey);
        }

        results.Add((RealRunValue() == realBefore, "the real Run entry is unchanged"));
        foreach (var (ok, label) in results)
        {
            failures = Check(ok, label, failures);
        }

        return Summarize(failures, "STARTUP MENU OK");
    }
}
