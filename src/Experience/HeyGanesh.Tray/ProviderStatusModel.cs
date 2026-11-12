using HeyGanesh.App;
using HeyGanesh.Providers.ClaudeCode;
using HeyGanesh.Security;

namespace HeyGanesh.Tray;

/// <summary>How far a teaching provider is from working, in the order the window colours it.</summary>
internal enum ProviderState { Ready, NotSignedIn, NotInstalled, NeedsKey }

internal sealed record ProviderRow(ShellHost.Teacher Id, string Name, ProviderState State, string Detail, bool Active);

/// <summary>
/// What the settings window shows and does, with no UI (DEC-082), so every rule is checked offline. The three
/// teachers are the ones <see cref="ShellHost.Resolve"/> chooses between, and "Active" is that function's answer, so
/// the window can never name a different teacher than the shell uses.
/// </summary>
/// <remarks>
/// "Signed in" for a plan CLI is a hint: the CLI's own credential file exists. Nothing reads it (only existence is
/// asked), and a stale file fails at the first lesson with the CLI's sentence. The key goes through
/// <see cref="KeyScreenModel.Save"/> and so the one key ring; no text returned here contains a value.
/// </remarks>
internal sealed class ProviderStatusModel
{
    private readonly KeyScreenModel _keys;
    private readonly KeyRing _ring;
    private readonly Func<string?> _locateClaude;
    private readonly Func<string?> _locateCodex;
    private readonly Func<string, bool> _fileExists;
    private readonly string _home;
    private readonly string? _preferencePath;

    public ProviderStatusModel(KeyRing ring, Func<string?>? locateClaude = null, Func<string?>? locateCodex = null,
        Func<string, bool>? fileExists = null, string? home = null, string? preferencePath = null)
    {
        _ring = ring;
        _keys = new KeyScreenModel(ring);
        _locateClaude = locateClaude ?? PlanCli.LocateClaude;
        _locateCodex = locateCodex ?? PlanCli.LocateCodex;
        _fileExists = fileExists ?? File.Exists;
        _home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _preferencePath = preferencePath;
    }

    public IReadOnlyList<ProviderRow> Rows()
    {
        var deepSeek = !_keys.Missing("deepseek");
        var claude = _locateClaude() is not null;
        var codex = _locateCodex() is not null;
        var active = ShellHost.Resolve(TeacherPreference.Load(_preferencePath), deepSeek, claude, codex);

        return
        [
            new(ShellHost.Teacher.DeepSeek, "DeepSeek key", deepSeek ? ProviderState.Ready : ProviderState.NeedsKey,
                deepSeek ? "Fast, streamed drawings" : "Paste an API key to use it", active == ShellHost.Teacher.DeepSeek),
            Plan(ShellHost.Teacher.ClaudePlan, "Claude Code plan", claude, Path.Combine(_home, ".claude", ".credentials.json"), active),
            Plan(ShellHost.Teacher.CodexPlan, "ChatGPT plan (Codex)", codex, Path.Combine(_home, ".codex", "auth.json"), active),
        ];
    }

    private ProviderRow Plan(ShellHost.Teacher id, string name, bool installed, string credentialFile, ShellHost.Teacher active) =>
        !installed ? new(id, name, ProviderState.NotInstalled, "Not installed", false)
        : _fileExists(credentialFile) ? new(id, name, ProviderState.Ready, "Signed in, no key needed", active == id)
        // Active even when signed out: the shell would use it and fail at the first lesson, so the window says so honestly.
        : new(id, name, ProviderState.NotSignedIn, "Installed, not signed in", active == id);

    /// <summary>The active teacher's name for the window's headline; "Nothing yet" when none works.</summary>
    public string Headline() => Rows().FirstOrDefault(r => r.Active)?.Name ?? "Nothing yet";

    /// <summary>Saves the choice. The shell reads it at start, so the message says when it applies.</summary>
    public string Choose(ShellHost.Teacher teacher) =>
        TeacherPreference.Save(teacher, _preferencePath)
            ? "Saved. Hey Ganesh uses it the next time it starts."
            : "Could not save the choice (the data folder is not writable).";

    /// <summary>
    /// Typed-so-far check, run on every keystroke. Whitespace or a control character inside is what KeyRing refuses, so
    /// it is said before Save rather than after; a short key is allowed (a provider may issue one) with a nudge.
    /// The hint never contains any of what was typed.
    /// </summary>
    public static (bool CanSave, string? Hint) CheckKey(string typed)
    {
        var key = typed.Trim();
        if (key.Length == 0)
        {
            return (false, null);
        }

        if (key.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            return (false, "A key has no spaces or line breaks.");
        }

        return key.Length < 16 ? (true, "That looks short for an API key. Save still works.") : (true, null);
    }

    public string SaveKey(string typed) => _keys.Save("deepseek", typed);

    /// <summary>The CLI's own sign-in command, run in a console window so the person sees its prompts; null for DeepSeek.</summary>
    public static (string File, string Arguments)? SignIn(ShellHost.Teacher teacher) => teacher switch
    {
        ShellHost.Teacher.ClaudePlan => ("cmd.exe", "/c start \"Sign in to Claude\" cmd /k claude auth login"),
        ShellHost.Teacher.CodexPlan => ("cmd.exe", "/c start \"Sign in to ChatGPT\" cmd /k codex login"),
        _ => null,
    };

    /// <summary>Where to get a missing plan CLI.</summary>
    public static string? InstallPage(ShellHost.Teacher teacher) => teacher switch
    {
        ShellHost.Teacher.ClaudePlan => "https://claude.com/product/claude-code",
        ShellHost.Teacher.CodexPlan => "https://developers.openai.com/codex/cli",
        _ => null,
    };
}
