// Request building and error sentences ported from openclicky
// windows/src/OpenClicky.Core/Answers/ClaudeCodeAnswerProvider.cs
// (MIT, Copyright (c) 2026 Prasanth Sasikumar and OpenClicky contributors).

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using HeyGanesh.Providers.Core;

namespace HeyGanesh.Providers.ClaudeCode;

/// <summary>
/// An <see cref="ILlmClient"/> on the person's Claude plan (DEC-065): each call runs
/// <c>claude -p</c> once, in a fresh scratch folder, with no tools, no MCP, no skills and no saved session.
/// The request goes in as one stream-json user message on stdin and the answer streams back as stream-json.
/// No key passes through this class: the CLI uses its own sign-in, and <see cref="PlanCli.RemovedVariables"/>
/// keeps an API key in the environment from overriding the plan.
/// </summary>
/// <remarks>
/// Each call is one turn with no conversation to resume, so earlier messages (the planner's repair turn) are
/// folded into that one message as labelled text. Temperature and MaxOutputTokens have no CLI flag and are
/// ignored; a request with tools is refused.
/// </remarks>
public sealed class ClaudeCodeLlmClient : ILlmClient
{
    private const string FallbackSystemPrompt = "Answer the user.";

    private static readonly JsonSerializerOptions StdinJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private readonly ClaudeCodeOptions _options;

    public ClaudeCodeLlmClient(ClaudeCodeOptions? options = null) => _options = options ?? new ClaudeCodeOptions();

    public string ProviderId => _options.ProviderId;

    /// <summary>The <c>--model</c> value, or "default" when the CLI picks.</summary>
    public string ModelId => _options.ModelId ?? "default";

    public ProviderCapability Capabilities => ProviderCapability.Text | ProviderCapability.Vision | ProviderCapability.Streaming;

    /// <summary>The last finished call's API-equivalent cost (<c>total_cost_usd</c>); null before one or when not reported.</summary>
    public double? LastCostUsd { get; private set; }

    public async Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        var summary = await RunAsync(request, null, ct).ConfigureAwait(false);
        return new LlmResponse
        {
            Text = AnswerText(summary),
            ModelId = ModelId,
            InputTokens = summary.InputTokens,
            OutputTokens = summary.OutputTokens,
            Latency = watch.Elapsed,
        };
    }

    public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(LlmRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var deltas = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        var run = Task.Run(async () =>
        {
            try
            {
                return await RunAsync(request, d => deltas.Writer.TryWrite(d), ct).ConfigureAwait(false);
            }
            finally
            {
                deltas.Writer.TryComplete();
            }
        }, CancellationToken.None);

        var streamedAny = false;
        await foreach (var delta in deltas.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            streamedAny = true;
            yield return new LlmStreamChunk { TextDelta = delta };
        }

        var summary = await run.ConfigureAwait(false); // rethrows the run's ProviderException or cancellation
        var text = AnswerText(summary);
        if (!streamedAny) yield return new LlmStreamChunk { TextDelta = text }; // no partial messages came: the result is the only copy
        yield return new LlmStreamChunk { IsDone = true, InputTokens = summary.InputTokens, OutputTokens = summary.OutputTokens };
    }

    public void Dispose()
    {
        // Nothing held between calls: each run owns its process and scratch folder.
    }

    private async Task<ClaudeCodeStreamSummary> RunAsync(LlmRequest request, Action<string>? onDelta, CancellationToken ct)
    {
        if (request.Tools is { Count: > 0 })
        {
            throw new ProviderException(ProviderErrorKind.CapabilityMissing, ProviderId, ModelId, "The Claude Code plan provider runs with no tools.");
        }

        var exe = _options.ExecutablePath ?? PlanCli.LocateClaude();
        if (exe is null || !File.Exists(exe))
        {
            throw new ProviderException(ProviderErrorKind.Transport, ProviderId, ModelId,
                "Claude Code is not installed: no claude.exe in %USERPROFILE%\\.local\\bin or on PATH.", isRetryable: false);
        }

        var scratch = PlanCli.MakeScratch();
        try
        {
            // A file, not --system-prompt: the planner's prompt can pass Windows' 32 767-character command line.
            var systemFile = Path.Combine(scratch, "system-prompt.txt");
            await File.WriteAllTextAsync(systemFile, SystemPrompt(request), new UTF8Encoding(false), ct).ConfigureAwait(false);

            var parser = new ClaudeCodeStreamParser();
            PlanCliOutput output;
            try
            {
                output = await PlanCli.RunAsync(
                    exe,
                    Arguments(request.ModelOverride ?? _options.ModelId, systemFile),
                    StandardInput(request),
                    scratch,
                    PlanCli.ProcessEnvironment(exe),
                    _options.Timeout,
                    chunk =>
                    {
                        foreach (var d in parser.Consume(chunk)) onDelta?.Invoke(d);
                    },
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new ProviderException(ProviderErrorKind.Transport, ProviderId, ModelId, $"Couldn't start Claude Code at {exe}.", e, isRetryable: false);
            }

            foreach (var d in parser.Finish()) onDelta?.Invoke(d);
            var summary = parser.Summary;
            LastCostUsd = summary.CostUsd;
            ThrowIfFailed(summary, output);
            return summary;
        }
        finally
        {
            PlanCli.RemoveScratch(scratch);
        }
    }

    /// <summary>The CLI's flags for one tool-less, session-less print-mode turn. Public for the smoke host's row.</summary>
    public static IReadOnlyList<string> Arguments(string? model, string systemPromptFile)
    {
        var args = new List<string>
        {
            "-p",
            "--input-format", "stream-json",
            "--output-format", "stream-json",
            "--verbose", // stream-json output requires it in print mode
            "--include-partial-messages",
            "--tools", "",
            "--permission-mode", "dontAsk",
            "--permission-prompts", "none",
            "--system-prompt-file", systemPromptFile,
            "--no-session-persistence",
            "--safe-mode",
            "--strict-mcp-config",
            "--disable-slash-commands",
        };
        if (model?.Trim() is { Length: > 0 } m) args.AddRange(["--model", m]);
        return args;
    }

    /// <summary>The request's system prompt, plus any "system" messages, or a one-line fallback (never the CLI's own coding prompt).</summary>
    public static string SystemPrompt(LlmRequest request)
    {
        var parts = new[] { request.SystemPrompt }
            .Concat(request.Messages.Where(m => m.Role == "system").Select(m => m.Text))
            .Where(t => !string.IsNullOrWhiteSpace(t));
        var joined = string.Join("\n\n", parts);
        return joined.Length == 0 ? FallbackSystemPrompt : joined;
    }

    /// <summary>
    /// One stream-json user message: every non-system message in order, each one's images then its text; all but
    /// the last are labelled, since a single <c>-p</c> turn has no history of its own.
    /// </summary>
    public static byte[] StandardInput(LlmRequest request)
    {
        var turns = request.Messages.Where(m => m.Role != "system").ToList();
        var blocks = new JsonArray();
        for (var i = 0; i < turns.Count; i++)
        {
            var m = turns[i];
            foreach (var image in m.Images ?? [])
            {
                blocks.Add(new JsonObject
                {
                    ["type"] = "image",
                    ["source"] = image.Data.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? new JsonObject { ["type"] = "url", ["url"] = image.Data }
                        : new JsonObject { ["type"] = "base64", ["media_type"] = image.MimeType, ["data"] = image.Data },
                });
            }

            var label = i == turns.Count - 1 ? null : m.Role == "assistant" ? "Your earlier answer:" : "Earlier message:";
            var text = label is null ? m.Text : $"{label}\n{m.Text}";
            if (!string.IsNullOrEmpty(text)) blocks.Add(new JsonObject { ["type"] = "text", ["text"] = text });
        }

        var message = new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject { ["role"] = "user", ["content"] = blocks },
        };
        return Encoding.UTF8.GetBytes(message.ToJsonString(StdinJson) + "\n");
    }

    private void ThrowIfFailed(ClaudeCodeStreamSummary summary, PlanCliOutput output)
    {
        if (output.TimedOut)
        {
            throw new ProviderException(ProviderErrorKind.Timeout, ProviderId, ModelId,
                $"Claude Code took longer than {(int)_options.Timeout.TotalSeconds} seconds to answer.", isRetryable: false);
        }

        var reason = summary.FailureText ?? (output.ExitCode != 0 ? PlanCli.LastLine(output.StandardError) ?? $"exit status {output.ExitCode}" : null);
        if (reason is not null)
        {
            var lower = reason.ToLowerInvariant();
            // "failed to authenticate" is the CLI 2.1.263 wording for an expired, unrefreshable OAuth session.
            if (lower.Contains("not logged in") || lower.Contains("/login") || lower.Contains("authentication_failed") || lower.Contains("failed to authenticate"))
            {
                throw new ProviderException(ProviderErrorKind.AuthFailed, ProviderId, ModelId,
                    "Claude Code is signed out. Open a terminal, run claude, and sign in with your Claude account.");
            }

            if (lower.Contains("rate_limit") || lower.Contains("usage limit"))
            {
                throw new ProviderException(ProviderErrorKind.RateLimited, ProviderId, ModelId, "Your Claude plan has hit its usage limit for now.");
            }

            throw new ProviderException(ProviderErrorKind.Transport, ProviderId, ModelId, $"Claude Code couldn't answer: {reason.Trim()}", isRetryable: false);
        }

        if (AnswerText(summary).Length == 0)
        {
            throw new ProviderException(ProviderErrorKind.Transport, ProviderId, ModelId, "Claude Code finished without an answer.", isRetryable: false);
        }
    }

    private static string AnswerText(ClaudeCodeStreamSummary summary) => (summary.ResultText ?? summary.StreamedText).Trim();
}
