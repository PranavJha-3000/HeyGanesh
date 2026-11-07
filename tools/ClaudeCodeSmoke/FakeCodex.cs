using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeyGanesh.Tools.ClaudeCodeSmoke;

/// <summary>
/// A stand-in for <c>codex exec --json</c>: records what it was given (arguments, stdin, working folder, the image
/// files' bytes, whether key/home variables reached it), then writes the answer file and plays scripted events.
/// </summary>
internal static class FakeCodex
{
    public const string ModeVariable = "HEYGANESH_FAKE_CODEX";

    public static int Run(string mode, string[] args)
    {
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var stdin = Console.In.ReadToEnd();
        string? After(int i) => i + 1 < args.Length ? args[i + 1] : null;
        var images = new JsonArray();
        string? outputFile = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "-i" && After(i) is { } image) images.Add(File.Exists(image) ? Convert.ToBase64String(File.ReadAllBytes(image)) : "MISSING");
            if (args[i] == "-o") outputFile = After(i);
        }

        var log = new JsonObject
        {
            ["args"] = new JsonArray(args.Select(a => (JsonNode)a).ToArray()),
            ["stdin"] = stdin,
            ["cwd"] = Environment.CurrentDirectory,
            ["images"] = images,
            ["sawOpenAiKey"] = Environment.GetEnvironmentVariable("OPENAI_API_KEY") is not null,
            ["sawCodexHome"] = Environment.GetEnvironmentVariable("CODEX_HOME") is not null,
        };
        if (Environment.GetEnvironmentVariable(FakeClaude.LogVariable) is { Length: > 0 } path) File.WriteAllText(path, log.ToJsonString());

        switch (mode)
        {
            case "ok":
                Line(new { type = "thread.started", thread_id = "t1" });
                Line(new { type = "item.completed", item = new { type = "agent_message", text = "Codex answer" } });
                Line(new { type = "turn.completed", usage = new { input_tokens = 100, cached_input_tokens = 40, output_tokens = 20 } });
                if (outputFile is not null) File.WriteAllText(outputFile, "Codex answer\n");
                return 0;
            case "nofile":
                Line(new { type = "item.completed", item = new { type = "agent_message", text = "From the events" } });
                Line(new { type = "turn.completed", usage = new { input_tokens = 5, output_tokens = 3 } });
                return 0;
            case "signedout":
                Line(new { type = "error", message = "Not logged in. Run codex login." });
                return 1;
            case "ratelimit":
                Line(new { type = "turn.failed", error = new { message = "{\"type\":\"error\",\"status\":429,\"error\":{\"message\":\"You have hit your usage limit.\"}}" } });
                return 1;
            case "slow":
                Thread.Sleep(30_000);
                return 0;
            case "crash":
                Console.Error.WriteLine("error: unexpected argument '--frobnicate'");
                return 2;
            case "empty":
                Line(new { type = "turn.completed", usage = new { input_tokens = 1, output_tokens = 0 } });
                if (outputFile is not null) File.WriteAllText(outputFile, "  \n");
                return 0;
            default:
                Console.Error.WriteLine($"fake codex: unknown mode {mode}");
                return 2;
        }
    }

    private static void Line(object o) => Console.WriteLine(JsonSerializer.Serialize(o));
}
