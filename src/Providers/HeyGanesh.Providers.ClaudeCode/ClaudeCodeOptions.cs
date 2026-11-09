namespace HeyGanesh.Providers.ClaudeCode;

/// <summary>How <see cref="ClaudeCodeLlmClient"/> runs the CLI. Holds no secret: the CLI keeps its own sign-in.</summary>
public sealed record ClaudeCodeOptions
{
    /// <summary>The provider id reported in results and errors.</summary>
    public string ProviderId { get; init; } = "claude-code";

    /// <summary>
    /// Full path to <c>claude.exe</c>; null finds it (<c>%USERPROFILE%\.local\bin</c>, then PATH). The smoke host
    /// points this at its own fake CLI.
    /// </summary>
    public string? ExecutablePath { get; init; }

    /// <summary><c>--model</c>: an alias ("sonnet", "opus", "haiku") or a full model name; null = the CLI's default.</summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// One run's limit. A lesson plan is a few thousand tokens and each run starts the CLI cold, so this is
    /// longer than openclicky's 90 s for spoken answers.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(180);
}
