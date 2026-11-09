// Flags, event reading and error sentences ported from openclicky
// windows/src/OpenClicky.Core/Answers/CodexAnswerProvider.cs
// (MIT, Copyright (c) 2026 Prasanth Sasikumar and OpenClicky contributors).

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using HeyGanesh.Providers.Core;

namespace HeyGanesh.Providers.ClaudeCode;

/// <summary>How <see cref="CodexLlmClient"/> runs the CLI. Holds no secret: Codex keeps its own ChatGPT sign-in.</summary>
public sealed record CodexOptions
{
    public string ProviderId { get; init; } = "codex";

    /// <summary>Full path to <c>codex.cmd</c> / <c>codex.exe</c>; null finds it (npm's folder, then PATH).</summary>
    public string? ExecutablePath { get; init; }

    /// <summary><c>-m</c>: e.g. "gpt-5.5"; null = Codex's default for a ChatGPT sign-in.</summary>
    public string? ModelId { get; init; }

    /// <summary>One run's limit; Codex answers in one piece at the end, so this covers the whole lesson.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(180);
}

/// <summary>
/// An <see cref="ILlmClient"/> on the person's ChatGPT plan (DEC-066): each call runs <c>codex exec</c> once, in a
/// fresh scratch folder, read-only, ephemeral, with the person's config, rules, tools and web search off.
/// Images go as files (<c>-i</c>), the answer comes back in <c>-o last-message.txt</c>, and errors and usage come
/// from the <c>--json</c> events. Codex does not stream text, so <see cref="StreamAsync"/> yields the whole answer once.
/// </summary>
/// <remarks>
/// The system prompt goes at the top of stdin, not in <c>-c developer_instructions</c>: npm installs Codex as a
/// <c>.cmd</c> shim, and when no Node script is found cmd.exe caps the command line at 8 191 characters, while the
/// planner's prompt is longer.
/// shortcut: stdin instructions carry user-level, not developer-level, weight; move to an instructions file once a
/// Codex version's config key for one is verified live (CX-4).
/// </remarks>
public sealed class CodexLlmClient : ILlmClient
{
    /// <summary>Agent features that are on by default and have no place in a one-turn answer (openclicky's list).</summary>
    public static readonly IReadOnlyList<string> DisabledFeatures =
    [
        "shell_tool", "unified_exec", "plugins", "apps", "multi_agent", "image_generation",
        "browser_use", "computer_use", "skill_search", "hooks", "view_image",
    ];

    public const string OutputFileName = "last-message.txt";

    private readonly CodexOptions _options;

    public CodexLlmClient(CodexOptions? options = null) => _options = options ?? new CodexOptions();

    public string ProviderId => _options.ProviderId;

    public string ModelId => _options.ModelId ?? "default";

    /// <summary>No Streaming: the planner then takes its whole-answer path.</summary>
    public ProviderCapability Capabilities => ProviderCapability.Text | ProviderCapability.Vision;

    public async Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        var (text, input, output) = await RunAsync(request, ct).ConfigureAwait(false);
        return new LlmResponse { Text = text, ModelId = ModelId, InputTokens = input, OutputTokens = output, Latency = watch.Elapsed };
    }

    public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(LlmRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (text, input, output) = await RunAsync(request, ct).ConfigureAwait(false);
        yield return new LlmStreamChunk { TextDelta = text };
        yield return new LlmStreamChunk { IsDone = true, InputTokens = input, OutputTokens = output };
    }

    public void Dispose()
    {
        // Nothing held between calls: each run owns its process and scratch folder.
    }

    private async Task<(string Text, int Input, int Output)> RunAsync(LlmRequest request, CancellationToken ct)
    {
        if (request.Tools is { Count: > 0 })
        {
            throw new ProviderException(ProviderErrorKind.CapabilityMissing, ProviderId, ModelId, "The Codex plan provider runs with no tools.");
        }

        var exe = _options.ExecutablePath ?? PlanCli.LocateCodex();
        if (exe is null || !File.Exists(exe))
        {
            throw new ProviderException(ProviderErrorKind.Transport, ProviderId, ModelId,
                "Codex is not installed: run npm i -g @openai/codex, then codex login.", isRetryable: false);
        }

        var scratch = PlanCli.MakeScratch();
        try
        {
            var images = WriteImages(request, scratch);
            var outputFile = Path.Combine(scratch, OutputFileName);
            var stdout = new MemoryStream();
            PlanCliOutput output;
            try
            {
                output = await PlanCli.RunAsync(
                    exe,
                    Arguments(request.ModelOverride ?? _options.ModelId, images, outputFile, scratch),
                    Encoding.UTF8.GetBytes(PromptText(request, images.Count)),
                    scratch,
                    PlanCli.ProcessEnvironment(exe),
                    _options.Timeout,
                    chunk =>
                    {
                        lock (stdout) stdout.Write(chunk);
                    },
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new ProviderException(ProviderErrorKind.Transport, ProviderId, ModelId, $"Couldn't start Codex at {exe}.", e, isRetryable: false);
            }

            var events = Encoding.UTF8.GetString(stdout.ToArray());
            var fileAnswer = File.Exists(outputFile) ? (await File.ReadAllTextAsync(outputFile, Encoding.UTF8, CancellationToken.None).ConfigureAwait(false)).Trim() : "";
            var text = ThrowIfFailed(fileAnswer, events, output);
            var (input, outputTokens) = Usage(events);
            return (text, input, outputTokens);
        }
        finally
        {
            PlanCli.RemoveScratch(scratch);
        }
    }

    /// <summary>The <c>codex exec</c> flags for one read-only, tool-less, ephemeral turn. Public for the smoke host's row.</summary>
    public static IReadOnlyList<string> Arguments(string? model, IReadOnlyList<string> imageFiles, string outputFile, string workingDirectory)
    {
        var args = new List<string>
        {
            "exec",
            "--skip-git-repo-check",
            "--ephemeral",
            "--ignore-user-config", // the person's plugins and skills made a one-word answer take 34 s (openclicky)
            "--ignore-rules",
            "--sandbox", "read-only",
            "--color", "never",
            "--json",
        };
        foreach (var feature in DisabledFeatures) args.AddRange(["--disable", feature]);
        args.AddRange(["-c", "web_search=\"disabled\"", "-c", "model_reasoning_effort=\"low\""]);
        if (model?.Trim() is { Length: > 0 } m) args.AddRange(["-m", m]);
        args.AddRange(["-C", workingDirectory]);
        foreach (var image in imageFiles) args.AddRange(["-i", image]);
        args.AddRange(["-o", outputFile]);
        args.Add("-"); // the prompt from stdin: no length limit, never in a process list
        return args;
    }

    /// <summary>
    /// The stdin prompt: the system prompt as instructions, earlier turns labelled, what the attached images are
    /// (Codex attaches them in <c>-i</c> order without labels), then the last message.
    /// </summary>
    public static string PromptText(LlmRequest request, int imageCount)
    {
        var sections = new List<string> { "INSTRUCTIONS (follow them exactly):\n" + ClaudeCodeLlmClient.SystemPrompt(request) };
        var turns = request.Messages.Where(m => m.Role != "system").ToList();
        for (var i = 0; i < turns.Count - 1; i++)
        {
            sections.Add((turns[i].Role == "assistant" ? "Your earlier answer:\n" : "Earlier message:\n") + turns[i].Text);
        }

        if (imageCount > 0)
        {
            sections.Add($"Attached: {imageCount} image(s), the student's screen.");
        }

        if (turns.Count > 0) sections.Add(turns[^1].Text ?? "");
        return string.Join("\n\n", sections);
    }

    /// <summary>
    /// Each image to the scratch folder with an extension matching its type (how Codex decides how to read it).
    /// A URL image is skipped: Codex reads local files only.
    /// </summary>
    public static IReadOnlyList<string> WriteImages(LlmRequest request, string directory)
    {
        var paths = new List<string>();
        foreach (var image in request.Messages.SelectMany(m => m.Images ?? []))
        {
            if (image.Data.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
            var path = Path.Combine(directory, $"image-{paths.Count + 1}.{(image.MimeType.Contains("png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg")}");
            File.WriteAllBytes(path, Convert.FromBase64String(image.Data));
            paths.Add(path);
        }

        return paths;
    }

    private string ThrowIfFailed(string fileAnswer, string events, PlanCliOutput output)
    {
        if (output.TimedOut)
        {
            throw new ProviderException(ProviderErrorKind.Timeout, ProviderId, ModelId,
                $"Codex took longer than {(int)_options.Timeout.TotalSeconds} seconds to answer.", isRetryable: false);
        }

        if (output.ExitCode != 0)
        {
            var reason = ErrorMessage(events) ?? PlanCli.LastLine(output.StandardError) ?? $"exit status {output.ExitCode}";
            var lower = reason.ToLowerInvariant();
            if (lower.Contains("not logged in") || lower.Contains("codex login") || lower.Contains("401") || lower.Contains("unauthorized"))
            {
                throw new ProviderException(ProviderErrorKind.AuthFailed, ProviderId, ModelId,
                    "Codex is signed out. Open a terminal, run codex login, and sign in with ChatGPT.");
            }

            if (lower.Contains("usage limit") || lower.Contains("rate limit"))
            {
                throw new ProviderException(ProviderErrorKind.RateLimited, ProviderId, ModelId, "Your ChatGPT plan has hit its Codex usage limit for now.");
            }

            throw new ProviderException(ProviderErrorKind.Transport, ProviderId, ModelId, $"Codex couldn't answer: {reason.Trim()}", isRetryable: false);
        }

        var answer = fileAnswer.Length > 0 ? fileAnswer : LastAgentMessage(events) ?? "";
        if (answer.Length == 0)
        {
            throw new ProviderException(ProviderErrorKind.Transport, ProviderId, ModelId, "Codex finished without an answer.", isRetryable: false);
        }

        return answer;
    }

    /// <summary>
    /// The last failure in the events: a <c>turn.failed</c> error, else the last <c>error</c> event. The message is often
    /// the API's JSON error body, so its inner message is used.
    /// </summary>
    public static string? ErrorMessage(string events)
    {
        string? turnFailed = null, lastError = null;
        foreach (var e in Events(events))
        {
            switch (Str(e["type"]))
            {
                case "turn.failed" when e["error"] is JsonObject error && Str(error["message"]) is { } m:
                    turnFailed = m;
                    break;
                case "error" when Str(e["message"]) is { } m:
                    lastError = m;
                    break;
            }
        }

        if ((turnFailed ?? lastError) is not { } raw) return null;
        return Parse(raw) is JsonObject api ? Str((api["error"] as JsonObject)?["message"]) ?? Str(api["message"]) ?? raw : raw;
    }

    /// <summary>The last <c>agent_message</c> item's text, for when the output file is missing.</summary>
    public static string? LastAgentMessage(string events) =>
        Events(events)
            .Where(e => Str(e["type"]) == "item.completed" && e["item"] is JsonObject item && Str(item["type"]) == "agent_message")
            .Select(e => Str(e["item"]!["text"]))
            .LastOrDefault(t => !string.IsNullOrEmpty(t))?.Trim();

    /// <summary>Tokens from the last <c>turn.completed</c> event's usage. Assumed: <c>cached_input_tokens</c> is part of <c>input_tokens</c>, not extra.</summary>
    public static (int Input, int Output) Usage(string events)
    {
        var usage = Events(events).LastOrDefault(e => Str(e["type"]) == "turn.completed")?["usage"] as JsonObject;
        return (Int(usage?["input_tokens"]), Int(usage?["output_tokens"]));
    }

    private static IEnumerable<JsonObject> Events(string lines) =>
        lines.Split('\n', '\r').Where(l => l.Length > 0).Select(Parse).OfType<JsonObject>();

    private static JsonNode? Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int Int(JsonNode? node) => node is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;
}
