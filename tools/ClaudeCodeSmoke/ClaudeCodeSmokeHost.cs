using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using HeyGanesh.Providers.ClaudeCode;
using HeyGanesh.Providers.Core;

namespace HeyGanesh.Tools.ClaudeCodeSmoke;

/// <summary>
/// CC-1..CC-3 run offline against <see cref="FakeClaude"/> (this exe); CC-4 (<c>--live</c>) runs the real CLI on
/// the owner's Claude plan. Exit codes: 0 pass, 1 a row failed, 2 usage, 12 Claude Code signed out.
/// </summary>
internal static class ClaudeCodeSmokeHost
{
    private const string System = "You are a careful teacher.";

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0) return Usage();
        return args[0] switch
        {
            "--assert-args" => Report(await AssertArgsAsync()),
            "--assert-stream" => Report(await AssertStreamAsync()),
            "--assert-errors" => Report(await AssertErrorsAsync()),
            "--live" when args.Length >= 2 => await LiveAsync(args[1], args.Length >= 4 && args[2] == "--model" ? args[3] : null),
            _ => Usage(),
        };
    }

    private static int Usage()
    {
        Console.WriteLine("Claude Code plan provider host (DEC-065)");
        Console.WriteLine("  --assert-args     CC-1: flags, system prompt file, stdin message, stripped variables, scratch folder");
        Console.WriteLine("  --assert-stream   CC-2: deltas, result, tokens, cost, no-partial fallback");
        Console.WriteLine("  --assert-errors   CC-3: signed out, rate limit, timeout, cancel, crash, empty, missing exe, tools");
        Console.WriteLine("  --live \"<question>\" [--model <m>]   CC-4: one real call on your Claude plan");
        return 2;
    }

    // CC-1
    private static async Task<List<string>> AssertArgsAsync()
    {
        var f = new List<string>();
        // Set in this process: the client must keep them from the CLI.
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-not-a-real-key");
        Environment.SetEnvironmentVariable("CLAUDECODE", "1");
        var (log, _) = await FakeRunAsync("ok", new LlmRequest
        {
            SystemPrompt = System,
            ModelOverride = "haiku",
            Messages =
            [
                new LlmMessage { Role = "system", Text = "Extra rule." },
                new LlmMessage { Role = "user", Text = "Teach me torque.", Images = [new LlmImagePart { MimeType = "image/png", Data = "iVBORw0KGgo=" }] },
                new LlmMessage { Role = "assistant", Text = "{\"bad\":1}" },
                new LlmMessage { Role = "user", Text = "Fix these issues." },
            ],
        });
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
        Environment.SetEnvironmentVariable("CLAUDECODE", null);

        var args = log["args"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
        string? After(string flag) => args.IndexOf(flag) is var i && i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
        Check(f, args[0] == "-p", "print mode (-p) first");
        Check(f, After("--input-format") == "stream-json" && After("--output-format") == "stream-json", "stream-json in and out");
        Check(f, args.Contains("--verbose") && args.Contains("--include-partial-messages"), "--verbose and partial messages");
        Check(f, After("--tools") == "", "--tools \"\" (no tools)");
        Check(f, new[] { "--no-session-persistence", "--safe-mode", "--strict-mcp-config", "--disable-slash-commands" }.All(args.Contains),
            "no session, safe mode, no MCP, no slash commands");
        Check(f, After("--permission-mode") == "dontAsk" && After("--permission-prompts") == "none", "never asks for permission");
        Check(f, After("--model") == "haiku", "ModelOverride becomes --model");
        Check(f, !args.Contains("--system-prompt") && log["systemPrompt"]?.GetValue<string>() == System + "\n\nExtra rule.",
            "system prompt (plus system messages) goes through --system-prompt-file, never the command line");

        var stdin = log["stdin"]!.GetValue<string>();
        var lines = stdin.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var msg = JsonNode.Parse(lines[0])!;
        var content = msg["message"]!["content"]!.AsArray();
        Check(f, lines.Length == 1 && msg["type"]!.GetValue<string>() == "user" && msg["message"]!["role"]!.GetValue<string>() == "user",
            "stdin is one stream-json user message");
        Check(f, content[0]!["type"]!.GetValue<string>() == "image" && content[0]!["source"]!["media_type"]!.GetValue<string>() == "image/png"
                 && content[0]!["source"]!["data"]!.GetValue<string>() == "iVBORw0KGgo=", "the image goes first, base64 with its media type");
        Check(f, content.Count == 4
                 && content[1]!["text"]!.GetValue<string>() == "Earlier message:\nTeach me torque."
                 && content[2]!["text"]!.GetValue<string>() == "Your earlier answer:\n{\"bad\":1}"
                 && content[3]!["text"]!.GetValue<string>() == "Fix these issues.",
            "earlier turns are labelled, the last is plain, system messages are left out");
        Check(f, !log["sawApiKey"]!.GetValue<bool>(), "ANTHROPIC_API_KEY never reaches the CLI (the plan's sign-in is used)");
        Check(f, !log["sawClaudeCode"]!.GetValue<bool>(), "CLAUDECODE never reaches the CLI (no nested-session talk)");
        var cwd = log["cwd"]!.GetValue<string>();
        Check(f, cwd.Contains("heyganesh-plan-cli", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(cwd),
            "runs in a fresh scratch folder, removed afterwards");

        using var client = new ClaudeCodeLlmClient();
        Check(f, client.Capabilities == (ProviderCapability.Text | ProviderCapability.Vision | ProviderCapability.Streaming) && client.ModelId == "default",
            "capabilities Text|Vision|Streaming, model 'default' when the CLI picks");
        return f;
    }

    // CC-2
    private static async Task<List<string>> AssertStreamAsync()
    {
        var f = new List<string>();
        using (var client = Fake("ok"))
        {
            var deltas = new List<string>();
            LlmStreamChunk? last = null;
            await foreach (var c in client.StreamAsync(Ask()))
            {
                if (c.IsDone) last = c;
                else deltas.Add(c.TextDelta);
            }

            Check(f, deltas.SequenceEqual(["Hello", " world"]), $"stream yields the text deltas in order ({string.Join("|", deltas)})");
            Check(f, last is { InputTokens: 18, OutputTokens: 7 }, "the last chunk is IsDone with tokens (input counts cache reads and writes)");
            Check(f, client.LastCostUsd == 0.0123, "the API-equivalent cost is kept");

            var r = await client.GenerateAsync(Ask());
            Check(f, r.Text == "Hello world" && r.InputTokens == 18 && r.OutputTokens == 7 && r.ModelId == "default" && r.Latency > TimeSpan.Zero,
                "GenerateAsync returns the result text, tokens, model and latency");
        }

        using (var client = Fake("nopartial"))
        {
            var chunks = new List<string>();
            await foreach (var c in client.StreamAsync(Ask()))
            {
                if (!c.IsDone) chunks.Add(c.TextDelta);
            }

            Check(f, chunks.SequenceEqual(["Whole answer"]), "with no partial messages the stream still yields the whole answer once");
        }

        var parser = new ClaudeCodeStreamParser();
        var split = "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"é\"}}}\n";
        var bytes = Encoding.UTF8.GetBytes(split);
        var got = parser.Consume(bytes[..40]).Concat(parser.Consume(bytes[40..])).ToList();
        Check(f, got.SequenceEqual(["é"]), "a line split across reads (mid UTF-8) is parsed once, whole");
        return f;
    }

    // CC-3
    private static async Task<List<string>> AssertErrorsAsync()
    {
        var f = new List<string>();
        await ExpectAsync(f, "signedout", ProviderErrorKind.AuthFailed, "signed out -> AuthFailed with a sign-in sentence", "sign in");
        await ExpectAsync(f, "expired", ProviderErrorKind.AuthFailed, "an expired plan sign-in -> AuthFailed (seen live, CLI 2.1.263)", "sign in");
        await ExpectAsync(f, "ratelimit", ProviderErrorKind.RateLimited, "usage limit -> RateLimited", "usage limit");
        await ExpectAsync(f, "crash", ProviderErrorKind.Transport, "non-zero exit -> the last stderr line", "boom");
        await ExpectAsync(f, "empty", ProviderErrorKind.Transport, "an empty answer is an error", "without an answer");

        var watch = Stopwatch.StartNew();
        await ExpectAsync(f, "slow", ProviderErrorKind.Timeout, "past the timeout -> Timeout, not retryable", "longer than", TimeSpan.FromSeconds(2));
        Check(f, watch.Elapsed < TimeSpan.FromSeconds(15), $"the timed-out CLI is killed ({watch.Elapsed.TotalSeconds:F1} s)");

        watch.Restart();
        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
        using (var client = Fake("slow"))
        {
            var cancelled = false;
            try
            {
                await client.GenerateAsync(Ask(), cts.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            Check(f, cancelled && watch.Elapsed < TimeSpan.FromSeconds(15), $"cancellation kills the CLI and throws OperationCanceledException ({watch.Elapsed.TotalSeconds:F1} s)");
        }

        using (var missing = new ClaudeCodeLlmClient(new ClaudeCodeOptions { ExecutablePath = Path.Combine(Path.GetTempPath(), "no-such-claude.exe") }))
        {
            Check(f, await KindAsync(missing, Ask()) == ProviderErrorKind.Transport, "no CLI -> Transport, says it is not installed");
        }

        using (var tools = Fake("ok"))
        {
            var req = Ask() with { Tools = [new LlmToolDefinition { Name = "x" }] };
            Check(f, await KindAsync(tools, req) == ProviderErrorKind.CapabilityMissing, "a request with tools -> CapabilityMissing, the CLI never starts");
        }

        return f;
    }

    // CC-4: the real CLI, on the owner's plan. Prints no secret: there is none here.
    private static async Task<int> LiveAsync(string question, string? model)
    {
        var exe = PlanCli.LocateClaude();
        Console.WriteLine($"claude: {exe ?? "NOT FOUND"}");
        if (exe is null) return 1;
        using var client = new ClaudeCodeLlmClient(new ClaudeCodeOptions { ModelId = model });
        var watch = Stopwatch.StartNew();
        double? firstMs = null;
        try
        {
            await foreach (var c in client.StreamAsync(new LlmRequest { SystemPrompt = "Answer briefly.", Messages = [new LlmMessage { Text = question }] }))
            {
                if (c.IsDone)
                {
                    Console.WriteLine();
                    Console.WriteLine($"LIVE OK: model {client.ModelId}, first text {firstMs:F0} ms, total {watch.ElapsedMilliseconds} ms, " +
                                      $"tokens {c.InputTokens} in / {c.OutputTokens} out, API-equivalent cost ${client.LastCostUsd:F4} (not billed on a plan)");
                }
                else
                {
                    firstMs ??= watch.Elapsed.TotalMilliseconds;
                    Console.Write(c.TextDelta);
                }
            }

            return 0;
        }
        catch (ProviderException e)
        {
            Console.WriteLine($"LIVE FAILED ({e.Kind}): {e.Message}");
            return e.Kind == ProviderErrorKind.AuthFailed ? 12 : 1;
        }
    }

    private static LlmRequest Ask() => new() { SystemPrompt = System, Messages = [new LlmMessage { Text = "hi" }] };

    private static ClaudeCodeLlmClient Fake(string mode, TimeSpan? timeout = null)
    {
        Environment.SetEnvironmentVariable(FakeClaude.ModeVariable, mode);
        return new ClaudeCodeLlmClient(new ClaudeCodeOptions
        {
            ExecutablePath = Environment.ProcessPath,
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
        });
    }

    private static async Task<(JsonNode Log, LlmResponse Response)> FakeRunAsync(string mode, LlmRequest request)
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"heyganesh-fake-claude-{Guid.NewGuid():N}.json");
        Environment.SetEnvironmentVariable(FakeClaude.LogVariable, logPath);
        try
        {
            using var client = Fake(mode);
            var response = await client.GenerateAsync(request);
            return (JsonNode.Parse(await File.ReadAllTextAsync(logPath))!, response);
        }
        finally
        {
            Environment.SetEnvironmentVariable(FakeClaude.LogVariable, null);
            File.Delete(logPath);
        }
    }

    private static async Task ExpectAsync(List<string> f, string mode, ProviderErrorKind kind, string what, string messagePart, TimeSpan? timeout = null)
    {
        using var client = Fake(mode, timeout);
        try
        {
            await client.GenerateAsync(Ask());
            Check(f, false, $"{what} (no error was thrown)");
        }
        catch (ProviderException e)
        {
            Check(f, e.Kind == kind && e.Message.Contains(messagePart, StringComparison.OrdinalIgnoreCase)
                     && e.IsRetryable == (kind == ProviderErrorKind.RateLimited), // only a usage limit is worth retrying later
                $"{what} (got {e.Kind}: {e.Message})");
        }
    }

    private static async Task<ProviderErrorKind?> KindAsync(ClaudeCodeLlmClient client, LlmRequest request)
    {
        try
        {
            await client.GenerateAsync(request);
            return null;
        }
        catch (ProviderException e)
        {
            return e.Kind;
        }
    }

    private static void Check(List<string> failures, bool ok, string what)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {what}");
        if (!ok) failures.Add(what);
    }

    private static int Report(List<string> failures)
    {
        Console.WriteLine(failures.Count == 0 ? "ALL PASS (exit 0)" : $"{failures.Count} FAILED (exit 1)");
        return failures.Count == 0 ? 0 : 1;
    }
}
