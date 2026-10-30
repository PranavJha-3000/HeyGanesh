using Microsoft.Win32;

namespace HeyGanesh.Tray;

/// <summary>Whether the tray starts at login, as the registry says.</summary>
internal enum StartupState
{
    /// <summary>No entry: the tray does not start at login.</summary>
    Off,

    /// <summary>The entry starts this copy of the tray.</summary>
    On,

    /// <summary>The entry starts another copy (an older build or install folder). Choosing the item points it here.</summary>
    Elsewhere,
}

/// <summary>
/// Start at login (DEC-054): the per-user <c>Run</c> value <c>HeyGanesh</c>, holding this exe's quoted path. It is
/// written or removed only when the user chooses the menu item; nothing turns it on by default. Per user (HKCU), so it
/// needs no admin rights and affects no other account. The key path is injectable so TS-1 can use a throwaway key
/// instead of the real one. This is the only class in the tray that touches the registry (TY-1).
/// </summary>
internal sealed class StartupEntry
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "HeyGanesh";

    private readonly string _keyPath;

    public StartupEntry(string keyPath, string exePath)
    {
        _keyPath = keyPath;
        Command = $"\"{exePath}\"";
    }

    /// <summary>The entry for this user's real Run key.</summary>
    public static StartupEntry ForThisUser(string exePath) => new(RunKeyPath, exePath);

    public string KeyPath => _keyPath;

    /// <summary>What the entry holds when it is on: the quoted path, so a folder with spaces still starts the right file.</summary>
    public string Command { get; }

    /// <summary>The value now in the registry, or null. For TS-1.</summary>
    public string? CurrentCommand
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
            return key?.GetValue(ValueName) as string;
        }
    }

    public StartupState State => CurrentCommand switch
    {
        null => StartupState.Off,
        var value when string.Equals(value, Command, StringComparison.OrdinalIgnoreCase) => StartupState.On,
        _ => StartupState.Elsewhere,
    };

    public void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);
        key.SetValue(ValueName, Command, RegistryValueKind.String);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
