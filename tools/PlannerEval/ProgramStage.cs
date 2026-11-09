using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeyGanesh.Abstractions;
using HeyGanesh.DrawStage;

namespace HeyGanesh.Tools.PlannerEval;

/// <summary>
/// The real drawing stage, hidden, as PlannerEval's run checks see it (DEC-073): a program goes in as one finished
/// stream at estimate pace, and what comes out is whether every statement ran, the page's errors and refused marks,
/// the lines it said and the marks it accepted. Local only: it needs WebView2 and a desktop session, so CI never
/// opens it. One stage serves a whole run; each program is cleared before the next.
/// </summary>
internal sealed class ProgramStage : IDisposable
{
    // The page gives a program 20 s of worker time in all (DEC-070); past this the run is reported as never finished.
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(40);

    private readonly DrawStageWindow _window;
    private readonly ConcurrentQueue<string> _messages = new();

    private ProgramStage(DrawStageWindow window)
    {
        _window = window;
        _window.MessageReceived += _messages.Enqueue;
    }

    /// <summary>Opens the stage off to one side and hides it; the worker runs programs while it is hidden.</summary>
    public static async Task<ProgramStage> OpenAsync()
    {
        var window = await DrawStageWindow.OpenAsync(new PixelRect(0, 0, 1280, 720), 1.0);
        await window.HideAsync();
        return new ProgramStage(window);
    }

    /// <summary>Runs <paramref name="program"/> on a <paramref name="width"/> × <paramref name="height"/> canvas.</summary>
    public async Task<StageRun> RunAsync(string program, int width, int height)
    {
        _messages.Clear();
        await _window.PostAsync(DrawStageMessages.BeginStream(width, height, DrawStagePace.Estimate));
        await _window.PostAsync(DrawStageMessages.Feed(program, done: true));
        var ran = false;
        for (var waited = TimeSpan.Zero; waited < RunTimeout && !(ran = Events().Any(e => e.Type == "programRan")); waited += TimeSpan.FromMilliseconds(100))
        {
            await Task.Delay(100);
        }

        var events = Events();
        // ExecuteScript answers with the script's value as JSON: here a JSON string that holds the marks' JSON.
        var marks = JsonNode.Parse(JsonSerializer.Deserialize<string>(await _window.RunHostScriptAsync("window.stageMarks()")) ?? "[]") as JsonArray ?? new JsonArray();
        await _window.PostAsync(DrawStageMessages.Simple("clear"));
        return new StageRun(
            ran,
            events.Where(e => e.Type == "error").Select(e => e.Text ?? string.Empty).ToList(),
            events.Where(e => e.Type == "log" && e.Text?.StartsWith("mark skipped: ", StringComparison.Ordinal) == true).Select(e => e.Text![14..]).ToList(),
            events.Where(e => e.Type == "line").Select(e => e.Text ?? string.Empty).ToList(),
            marks);
    }

    private List<DrawStageEvent> Events() => _messages.Select(DrawStageMessages.ParseEvent).OfType<DrawStageEvent>().ToList();

    public void Dispose() => _window.Dispose();
}
