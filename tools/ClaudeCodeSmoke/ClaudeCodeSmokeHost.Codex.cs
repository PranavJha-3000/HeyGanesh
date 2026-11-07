using System.Diagnostics;
using System.Text.Json.Nodes;
using HeyGanesh.Providers.ClaudeCode;
using HeyGanesh.Providers.Core;

namespace HeyGanesh.Tools.ClaudeCodeSmoke;

/// <summary>CX-1..CX-3 and CX-5 offline against <see cref="FakeCodex"/>; CX-4 (<c>--live-codex</c>) on the owner's ChatGPT plan.</summary>
internal static partial class ClaudeCodeSmokeHost
{
    private const string Png = "iVBORw0KGgo=";

    // CX-1
    private static async Task<List<string>> AssertCodexArgsAsync()
    {
        var f = new List<string>();
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-not-a-real-key");
        Environment.SetEnvironmentVariable("CODEX_HOME", Path.GetTempPath());
        var log = await CodexLogAsync(Fake2("ok"), new LlmRequest
        {
            SystemPrompt = System,
            ModelOverride = "gpt-5.5",
            Messages =
            [
                new LlmMessage { Role = "user", Text = "Teach me torque.", Images = [new LlmImagePart { MimeType = "image/png", Data = Png }] },
                new LlmMessage { Role = "assistant", Text = "{\"bad\":1}" },
                new LlmMessage { Role = "user", Text = "Fix these issues." },
            ],
        });
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
        Environment.SetEnvironmentVariable("CODEX_HOME", null);

        var args = log["args"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
        string? After(string flag) => args.IndexOf(flag) is var i && i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
        Check(f, args[0] == "exec" && args[^1] == "-", "codex exec, prompt read from stdin (-)");
        Check(f, new[] { "--skip-git-repo-check", "--ephemeral", "--ignore-user-config", "--ignore-rules", "--json" }.All(args.Contains)
                 && After("--sandbox") == "read-only", "ephemeral, read-only, the person's config and rules ignored, --json events");
        Check(f, CodexLlmClient.DisabledFeatures.All(d => args.Where((a, i) => a == "--disable" && i + 1 < args.Count && args[i + 1] == d).Any())
                 && args.Contains("web_search=\"disabled\"") && args.Contains("model_reasoning_effort=\"low\""),
            "shell, plugins, apps, browser/computer use, image generation and web search are off; low reasoning");
        Check(f, After("-m") == "gpt-5.5", "ModelOverride becomes -m");
        Check(f, !args.Any(a => a.Contains(System, StringComparison.Ordinal)) && !args.Contains("developer_instructions"),
            "the system prompt is never on the command line (cmd.exe's 8 191-character cap)");
        var stdin = log["stdin"]!.GetValue<string>();
        Check(f, stdin.StartsWith("INSTRUCTIONS (follow them exactly):\n" + System, StringComparison.Ordinal)
                 && stdin.Contains("Earlier message:\nTeach me torque.") && stdin.Contains("Your earlier answer:\n{\"bad\":1}")
                 && stdin.Contains("Attached: 1 image(s)") && stdin.EndsWith("Fix these issues.", StringComparison.Ordinal),
            "stdin: instructions first, earlier turns labelled, the image named, the last message last");
        Check(f, log["images"]!.AsArray().Select(i => i!.GetValue<string>()).SequenceEqual([Png]) && (After("-i") ?? "").EndsWith("image-1.png"),
            "the image is a .png file passed with -i, byte for byte");
        Check(f, (After("-o") ?? "").EndsWith(CodexLlmClient.OutputFileName) && After("-C") == log["cwd"]!.GetValue<string>(),
            "-o last-message.txt and -C in the scratch folder");
        Check(f, !log["sawOpenAiKey"]!.GetValue<bool>() && !log["sawCodexHome"]!.GetValue<bool>(),
            "OPENAI_API_KEY and CODEX_HOME never reach Codex (the person's own ChatGPT login is used)");
        var cwd = log["cwd"]!.GetValue<string>();
        Check(f, cwd.Contains("heyganesh-plan-cli", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(cwd), "scratch folder removed afterwards, image included");
        using var c = new CodexLlmClient();
        Check(f, c.Capabilities == (ProviderCapability.Text | ProviderCapability.Vision) && c.ModelId == "default",
            "capabilities Text|Vision (no streaming: the planner takes its whole-answer path), model 'default'");
        return f;
    }

    // CX-2
    private static async Task<List<string>> AssertCodexRunAsync()
    {
        var f = new List<string>();
        using (var c = Fake2("ok"))
        {
            var r = await c.GenerateAsync(Ask());
            Check(f, r.Text == "Codex answer" && r.InputTokens == 100 && r.OutputTokens == 20 && r.Latency > TimeSpan.Zero,
                "the answer comes from -o, tokens from turn.completed");
            var chunks = new List<LlmStreamChunk>();
            await foreach (var ch in c.StreamAsync(Ask())) chunks.Add(ch);
            Check(f, chunks.Count == 2 && chunks[0].TextDelta == "Codex answer" && chunks[1] is { IsDone: true, InputTokens: 100, OutputTokens: 20 },
                "StreamAsync yields the whole answer once, then IsDone with tokens");
        }

        using (var c = Fake2("nofile"))
        {
            Check(f, (await c.GenerateAsync(Ask())).Text == "From the events", "with no answer file, the last agent_message event is the answer");
        }

        var urlOnly = new LlmRequest { Messages = [new LlmMessage { Text = "x", Images = [new LlmImagePart { Data = "https://example.org/a.png" }] }] };
        var dir = PlanCli.MakeScratch();
        Check(f, CodexLlmClient.WriteImages(urlOnly, dir).Count == 0, "a URL image is skipped (Codex reads local files)");
        PlanCli.RemoveScratch(dir);
        return f;
    }

    // CX-3
    private static async Task<List<string>> AssertCodexErrorsAsync()
    {
        var f = new List<string>();
        await ExpectCodexAsync(f, "signedout", ProviderErrorKind.AuthFailed, "signed out -> AuthFailed with a codex login sentence", "codex login");
        await ExpectCodexAsync(f, "ratelimit", ProviderErrorKind.RateLimited, "a 429 body in turn.failed -> RateLimited (inner message read)", "usage limit");
        await ExpectCodexAsync(f, "crash", ProviderErrorKind.Transport, "non-zero exit -> the last stderr line", "frobnicate");
        await ExpectCodexAsync(f, "empty", ProviderErrorKind.Transport, "a blank answer file and no message is an error", "without an answer");
        var watch = Stopwatch.StartNew();
        await ExpectCodexAsync(f, "slow", ProviderErrorKind.Timeout, "past the timeout -> Timeout", "longer than", TimeSpan.FromSeconds(2));
        Check(f, watch.Elapsed < TimeSpan.FromSeconds(15), $"the timed-out CLI is killed ({watch.Elapsed.TotalSeconds:F1} s)");

        watch.Restart();
        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
        using (var c = Fake2("slow"))
        {
            var cancelled = false;
            try
            {
                await c.GenerateAsync(Ask(), cts.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            Check(f, cancelled && watch.Elapsed < TimeSpan.FromSeconds(15), $"cancellation kills Codex and throws ({watch.Elapsed.TotalSeconds:F1} s)");
        }

        using (var missing = new CodexLlmClient(new CodexOptions { ExecutablePath = Path.Combine(Path.GetTempPath(), "no-such-codex.cmd") }))
        {
            try
            {
                await missing.GenerateAsync(Ask());
                Check(f, false, "no CLI -> Transport (nothing thrown)");
            }
            catch (ProviderException e)
            {
                Check(f, e.Kind == ProviderErrorKind.Transport && e.Message.Contains("npm i -g @openai/codex"), "no CLI -> Transport, says how to install");
            }
        }

        using (var tools = Fake2("ok"))
        {
            try
            {
                await tools.GenerateAsync(Ask() with { Tools = [new LlmToolDefinition { Name = "x" }] });
                Check(f, false, "tools -> CapabilityMissing (nothing thrown)");
            }
            catch (ProviderException e)
            {
                Check(f, e.Kind == ProviderErrorKind.CapabilityMissing, "a request with tools -> CapabilityMissing");
            }
        }

        return f;
    }

    // CX-5: the .cmd launch an npm install needs (codex always, claude sometimes).
    private static async Task<List<string>> AssertLaunchAsync()
    {
        var f = new List<string>();
        const string shim = "@ECHO off\r\nGOTO start\r\n:find_dp0\r\nSET dp0=%~dp0\r\nEXIT /b\r\n:start\r\nSETLOCAL\r\nCALL :find_dp0\r\n" +
                            "endLocal & goto #_undefined_# 2>NUL || title %COMSPEC% & \"%_prog%\"  \"%dp0%\\node_modules\\@openai\\codex\\bin\\codex.js\" %*\r\n";
        var script = PlanCli.NpmShimScript(@"C:\npm\codex.cmd", _ => shim, _ => true);
        Check(f, script == @"C:\npm\node_modules\@openai\codex\bin\codex.js", $"an npm shim's Node script is found ({script})");
        Check(f, PlanCli.NpmShimScript(@"C:\npm\codex.cmd", _ => shim, _ => false) is null, "a shim whose script is missing is not used");

        var dir = PlanCli.MakeScratch();
        try
        {
            // A plain .cmd (no Node script) goes through cmd.exe: every argument must arrive as it was sent.
            var cmd = Path.Combine(dir, "codex.cmd");
            await File.WriteAllTextAsync(cmd, $"@\"{Environment.ProcessPath}\" %*\r\n");
            Check(f, PlanCli.Locate("codex", dir) == cmd, "Locate finds codex.cmd when there is no codex.exe");
            const string tricky = "gpt \"5\" & | ^ %PATH% ! (x) \\";
            var log = await CodexLogAsync(Fake2("ok", cmd), Ask() with { ModelOverride = tricky });
            var args = log["args"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
            var expected = CodexLlmClient.Arguments(tricky, [], "X", "Y");
            Check(f, args.Count == expected.Count && args[args.IndexOf("-m") + 1] == tricky && args.Contains("web_search=\"disabled\""),
                $"through cmd.exe every argument survives, quotes, & | ^ %PATH% (not expanded) ! ( ) and a trailing backslash included ({args.Count} args)");
        }
        finally
        {
            PlanCli.RemoveScratch(dir);
        }

        return f;
    }

    // CX-4
    private static async Task<int> LiveCodexAsync(string question, string? model)
    {
        var exe = PlanCli.LocateCodex();
        Console.WriteLine($"codex: {exe ?? "NOT FOUND (npm i -g @openai/codex, then codex login)"}");
        if (exe is null) return 1;
        using var client = new CodexLlmClient(new CodexOptions { ModelId = model });
        var watch = Stopwatch.StartNew();
        try
        {
            var r = await client.GenerateAsync(new LlmRequest { SystemPrompt = "Answer briefly.", Messages = [new LlmMessage { Text = question }] });
            Console.WriteLine(r.Text);
            Console.WriteLine($"LIVE OK: model {client.ModelId}, total {watch.ElapsedMilliseconds} ms (no streaming), tokens {r.InputTokens} in / {r.OutputTokens} out");
            return 0;
        }
        catch (ProviderException e)
        {
            Console.WriteLine($"LIVE FAILED ({e.Kind}): {e.Message}");
            return e.Kind == ProviderErrorKind.AuthFailed ? 12 : 1;
        }
    }

    private static CodexLlmClient Fake2(string mode, string? exe = null, TimeSpan? timeout = null)
    {
        Environment.SetEnvironmentVariable(FakeCodex.ModeVariable, mode);
        return new CodexLlmClient(new CodexOptions { ExecutablePath = exe ?? Environment.ProcessPath, Timeout = timeout ?? TimeSpan.FromSeconds(30) });
    }

    private static async Task<JsonNode> CodexLogAsync(CodexLlmClient client, LlmRequest request)
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"heyganesh-fake-codex-{Guid.NewGuid():N}.json");
        Environment.SetEnvironmentVariable(FakeClaude.LogVariable, logPath);
        try
        {
            using (client)
            {
                await client.GenerateAsync(request);
            }

            return JsonNode.Parse(await File.ReadAllTextAsync(logPath))!;
        }
        finally
        {
            Environment.SetEnvironmentVariable(FakeClaude.LogVariable, null);
            File.Delete(logPath);
        }
    }

    private static async Task ExpectCodexAsync(List<string> f, string mode, ProviderErrorKind kind, string what, string messagePart, TimeSpan? timeout = null)
    {
        using var client = Fake2(mode, timeout: timeout);
        try
        {
            await client.GenerateAsync(Ask());
            Check(f, false, $"{what} (no error was thrown)");
        }
        catch (ProviderException e)
        {
            Check(f, e.Kind == kind && e.Message.Contains(messagePart, StringComparison.OrdinalIgnoreCase)
                     && e.IsRetryable == (kind == ProviderErrorKind.RateLimited), $"{what} (got {e.Kind}: {e.Message})");
        }
    }
}
