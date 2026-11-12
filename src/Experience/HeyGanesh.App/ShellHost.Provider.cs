using HeyGanesh.Providers.ClaudeCode;
using HeyGanesh.Providers.Core;
using HeyGanesh.TutorEngine;

namespace HeyGanesh.App;

// Who teaches (DEC-081): the lesson model and the Tutor used to need a DeepSeek key. Now a person with no key can
// teach on the Claude Code plan or the ChatGPT (Codex) plan they already pay for. One chooser, so the lesson leg,
// the Tutor and both header lines can never disagree about it.

internal static partial class ShellHost
{
    /// <summary>The model behind the teaching legs, in the order they are tried.</summary>
    internal enum Teacher { None, DeepSeek, ClaudePlan, CodexPlan }

    /// <summary>
    /// The teacher this machine uses now: the person's saved choice when it is available (DEC-082), else DeepSeek key
    /// first (fast, streamed, the path every row was verified on), then the Claude plan, then Codex. Pure, so the
    /// settings window and the tests ask the very same question the shell does.
    /// </summary>
    internal static Teacher Resolve(Teacher preferred, bool deepSeekKey, bool claudeInstalled, bool codexInstalled)
    {
        var available = preferred switch
        {
            Teacher.DeepSeek => deepSeekKey,
            Teacher.ClaudePlan => claudeInstalled,
            Teacher.CodexPlan => codexInstalled,
            _ => false,
        };
        return available ? preferred
            : deepSeekKey ? Teacher.DeepSeek
            : claudeInstalled ? Teacher.ClaudePlan
            : codexInstalled ? Teacher.CodexPlan
            : Teacher.None;
    }

    /// <summary>
    /// "Installed" is all this can know without a model call: a CLI that is not signed in fails at the first lesson
    /// with the CLI's own AuthFailed sentence, which the leg reports like a bad key. The settings window adds a
    /// signed-in hint from the CLI's own credential file.
    /// </summary>
    private static Teacher ChooseTeacher() =>
        Resolve(TeacherPreference.Load(), HasKey("deepseek"), PlanCli.LocateClaude() is not null, PlanCli.LocateCodex() is not null);

    /// <summary>The plan client for a plan teacher; null for DeepSeek (built where its key source is) and None.</summary>
    private static ILlmClient? CreatePlanClient(Teacher teacher) => teacher switch
    {
        Teacher.ClaudePlan => new ClaudeCodeLlmClient(),
        Teacher.CodexPlan => new CodexLlmClient(),
        _ => null,
    };

    /// <summary>The header's words for the teacher; the sentence about it keeps the old "(value suppressed)" key wording for DeepSeek.</summary>
    private static string TeacherName(Teacher teacher) => teacher switch
    {
        Teacher.DeepSeek => "deepseek-flash, DEEPSEEK_API_KEY present (value suppressed)",
        Teacher.ClaudePlan => "your Claude Code plan (the claude CLI, no key)",
        Teacher.CodexPlan => "your ChatGPT plan (the codex CLI, no key; answers arrive whole, so a drawing starts later)",
        _ => "none",
    };
}

/// <summary>
/// The Tutor seam over any <see cref="ILlmClient"/> (the two plan providers). It only maps the rendered prompt to a
/// request and the answer back; teaching policy stays in TutorService, and the caller owns and disposes the client.
/// </summary>
internal sealed class LlmTutorClient(ILlmClient llm) : ITutorClient
{
    public async Task<TutorCompletion> CompleteAsync(TutorPrompt prompt, CancellationToken cancellationToken)
    {
        var message = new LlmMessage
        {
            Role = "user",
            Text = prompt.UserText,
            Images = prompt.Visual is { } v
                ? [new LlmImagePart { MimeType = v.MimeType, Data = Convert.ToBase64String(v.JpegBytes) }]
                : null,
        };

        try
        {
            var response = await llm.GenerateAsync(new LlmRequest
            {
                SystemPrompt = prompt.SystemText,
                Messages = [message],
                MaxOutputTokens = prompt.MaxOutputTokens,
            }, cancellationToken).ConfigureAwait(false);
            return new TutorCompletion { Text = response.Text, InputTokens = response.InputTokens, OutputTokens = response.OutputTokens, ModelId = response.ModelId };
        }
        catch (ProviderException ex) when (ex.Kind == ProviderErrorKind.AuthFailed)
        {
            throw new TutorAuthenticationException(ex.Message);
        }
        catch (ProviderException ex) when (ex.Kind == ProviderErrorKind.RateLimited)
        {
            throw new TutorRateLimitException(ex.Message);
        }
        catch (ProviderException ex)
        {
            throw new TutorClientException(ex.Message);
        }
    }

    // The llm is owned by whoever built it (the shell's using), the same arrangement TutorService keeps.
    public void Dispose() { }
}

/// <summary>
/// The person's saved teacher (DEC-082): one word in <c>teacher.txt</c> under the data folder, so the tray's settings
/// window and the shell, two processes' worth of code, agree. Absent, unreadable or unknown reads as "no choice"
/// (the automatic order); the shell reads it once at start, so a change applies the next time Hey Ganesh starts.
/// </summary>
internal static class TeacherPreference
{
    /// <summary><c>$HEYGANESH_DATA_DIR/teacher.txt</c>, else <c>%LOCALAPPDATA%/HeyGanesh/teacher.txt</c>.</summary>
    public static string DefaultPath()
    {
        var root = Environment.GetEnvironmentVariable("HEYGANESH_DATA_DIR");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeyGanesh");
        }

        return Path.Combine(root, "teacher.txt");
    }

    public static ShellHost.Teacher Load(string? path = null)
    {
        try
        {
            return File.ReadAllText(path ?? DefaultPath()).Trim() switch
            {
                "deepseek" => ShellHost.Teacher.DeepSeek,
                "claude" => ShellHost.Teacher.ClaudePlan,
                "codex" => ShellHost.Teacher.CodexPlan,
                _ => ShellHost.Teacher.None,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ShellHost.Teacher.None;
        }
    }

    /// <summary>False when the file could not be written; the caller says so.</summary>
    public static bool Save(ShellHost.Teacher teacher, string? path = null)
    {
        try
        {
            path ??= DefaultPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, teacher switch
            {
                ShellHost.Teacher.DeepSeek => "deepseek",
                ShellHost.Teacher.ClaudePlan => "claude",
                ShellHost.Teacher.CodexPlan => "codex",
                _ => "auto",
            });
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
