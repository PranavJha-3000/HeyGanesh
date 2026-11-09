using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeyGanesh.Tools.ClaudeCodeSmoke;

/// <summary>
/// A stand-in for <c>claude -p</c>: records what it was given (arguments, stdin, working folder, the system
/// prompt file, whether key/nesting variables reached it) to the log file, then plays one scripted stream.
/// </summary>
internal static class FakeClaude
{
    public const string ModeVariable = "HEYGANESH_FAKE_CLAUDE";
    public const string LogVariable = "HEYGANESH_FAKE_CLAUDE_LOG";

    public static int Run(string mode, string[] args)
    {
        // The real CLI speaks UTF-8 both ways; the console default here is the OEM code page.
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var stdin = Console.In.ReadToEnd();
        var i = Array.IndexOf(args, "--system-prompt-file");
        var log = new JsonObject
        {
            ["args"] = new JsonArray(args.Select(a => (JsonNode)a).ToArray()),
            ["stdin"] = stdin,
            ["cwd"] = Environment.CurrentDirectory,
            ["systemPrompt"] = i >= 0 && i + 1 < args.Length && File.Exists(args[i + 1]) ? File.ReadAllText(args[i + 1]) : null,
            ["sawApiKey"] = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") is not null,
            ["sawClaudeCode"] = Environment.GetEnvironmentVariable("CLAUDECODE") is not null,
        };
        if (Environment.GetEnvironmentVariable(LogVariable) is { Length: > 0 } path) File.WriteAllText(path, log.ToJsonString());

        switch (mode)
        {
            case "ok":
                Line(new { type = "system", subtype = "init" });
                Console.WriteLine("a warning that is not JSON");
                Line(Delta("Hello"));
                Line(Delta(" world"));
                Line(Assistant("Hello world"));
                Line(Result("Hello world"));
                return 0;
            case "nopartial":
                Line(Assistant("Whole answer"));
                Line(Result("Whole answer"));
                return 0;
            case "signedout":
                Line(new { type = "assistant", error = "authentication_failed", message = new { content = new[] { new { type = "text", text = "Not logged in · Please run /login" } } } });
                Line(new { type = "result", subtype = "success", is_error = true, result = "Not logged in · Please run /login" });
                return 1;
            case "expired":
                Line(new { type = "result", subtype = "success", is_error = true, result = "Failed to authenticate: OAuth session expired and could not be refreshed" });
                return 1;
            case "ratelimit":
                Line(new { type = "result", subtype = "error_during_execution", is_error = true, result = "rate_limit_error: usage limit reached" });
                return 1;
            case "slow":
                Thread.Sleep(30_000);
                return 0;
            case "crash":
                Console.Error.WriteLine("starting...");
                Console.Error.WriteLine("error: boom, unknown option");
                return 1;
            case "empty":
                Line(Result(""));
                return 0;
            default:
                Console.Error.WriteLine($"fake claude: unknown mode {mode}");
                return 2;
        }
    }

    private static object Delta(string text) =>
        new { type = "stream_event", @event = new { type = "content_block_delta", delta = new { type = "text_delta", text } } };

    private static object Assistant(string text) =>
        new { type = "assistant", message = new { content = new[] { new { type = "text", text } } } };

    private static object Result(string text) => new
    {
        type = "result",
        subtype = "success",
        is_error = false,
        result = text,
        total_cost_usd = 0.0123,
        usage = new { input_tokens = 10, cache_creation_input_tokens = 5, cache_read_input_tokens = 3, output_tokens = 7 },
    };

    private static void Line(object o) => Console.WriteLine(JsonSerializer.Serialize(o));
}
