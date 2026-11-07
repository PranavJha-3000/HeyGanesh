using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HeyGanesh.Abstractions;
using HeyGanesh.App;
using HeyGanesh.DrawStage;
using HeyGanesh.LessonPlanner;
using HeyGanesh.Providers.Core;

namespace HeyGanesh.Tools.TraySmoke;

// SH-29 (FEATURE-011 step 7, DEC-074): the App Shell's drawing leg, offline. The shell's own turn
// (ShellHost.DrawOnStageAsync) and stage (DrawingStage) run against a scripted model and a fake page that answers like
// stage.js does (a line per say(), programRan, finished once the voice has ended), so the wiring is checked with no
// key, no WebView2 and no window: first lesson, keep(), redraw, NONE, silence, a voice fault, close, a click's
// question, another monitor, a page that never finishes, and the box in canvas units.
internal static partial class TraySmokeHost
{
    private static async Task<int> RunAssertDrawingLegAsync()
    {
        var failures = 0;
        var monitor = new MonitorInfo(1, "\\\\.\\DISPLAY1", new PixelRect(0, 0, 1920, 1080), true);
        var opened = new List<FakePage>();
        using var stage = new DrawingStage(m =>
        {
            var page = new FakePage();
            opened.Add(page);
            return Task.FromResult<IDrawStageSurface>(page);
        });
        var clicks = new List<string>();
        stage.PartClicked += clicks.Add;
        var spoken = new List<string>();
        Task Speak(string text, CancellationToken token)
        {
            spoken.Add(text);
            return Task.Delay(5, token);
        }

        DrawingRequest Ask(string question) => new() { Question = question, CanvasWidth = 1280, CanvasHeight = 720 };
        var wait = TimeSpan.FromSeconds(10);

        // 1. A first lesson: the stage opens, begins at the canvas size with the voice as its clock, every line is spoken.
        var model = new ScriptedModel(Js("step(1)\nsay('A rocket throws gas down.')\nsay('The gas pushes it up.')"));
        var first = await ShellHost.DrawOnStageAsync(new DrawingPlanner(model), stage, monitor, Ask("how does a rocket fly?"), Speak, wait, default);
        var page = opened.Single();
        failures = Check(first.Result.Outcome == DrawingOutcome.Program && first.Kept == false && !first.FollowUp && page.Types.FirstOrDefault() == "beginStream"
            && page.Begins.Single() == (1280, 720, "speech") && page.LastFeedDone,
            $"a first lesson begins the stage at the canvas size, paced by speech, and feeds the program to the end ({string.Join(",", page.Types.Distinct())})", failures);
        failures = Check(spoken.SequenceEqual(new[] { "A rocket throws gas down.", "The gas pushes it up." }) && first.Voice is { Spoken: 2, CutShort: false } && first.FirstLine == "A rocket throws gas down."
            && page.Count("lineStarted") == 2 && page.Count("lineEnded") == 2,
            "each line is spoken in order with lineStarted / lineEnded, and the first line is the lesson's title", failures);
        failures = Check(stage.FollowUpOn(monitor) is { Questions.Count: 1 } && stage.Drawing is { Width: 1280, Height: 720 },
            "the drawing stays up afterwards: the next question over it is a follow-up", failures);

        // 2. A follow-up that keeps the drawing: the model sees the earlier program, the stage continues, nothing is cleared.
        model.Next(Js("keep()\nstep(2)\nsay('The nozzle shapes the gas.')"));
        var kept = await ShellHost.DrawOnStageAsync(new DrawingPlanner(model), stage, monitor, Ask("and the nozzle?"), Speak, wait, default);
        failures = Check(kept.FollowUp && kept.Kept == true && page.Count("continueStream") == 1 && page.Count("beginStream") == 1 && page.Count("clear") == 0
            && model.LastPrompt.Contains("say('A rocket throws gas down.')", StringComparison.Ordinal),
            "a follow-up with keep() is asked over the earlier program and drawn after it (continueStream, no clear)", failures);
        failures = Check(stage.FollowUpOn(monitor) is { } both && both.Questions.Count == 2 && both.Program.Contains("rocket", StringComparison.Ordinal) && both.Program.Contains("nozzle", StringComparison.Ordinal),
            "the drawing now answers both questions and holds both programs", failures);

        // 3. A follow-up drawn afresh: a new beginning, and the drawing answers only the new question.
        model.Next(Js("step(1)\nsay('A jet breathes air instead.')"));
        var fresh = await ShellHost.DrawOnStageAsync(new DrawingPlanner(model), stage, monitor, Ask("what about a jet?"), Speak, wait, default);
        failures = Check(fresh.FollowUp && fresh.Kept == false && page.Count("beginStream") == 2 && stage.FollowUpOn(monitor) is { Questions.Count: 1 },
            "a follow-up without keep() redraws the stage and starts the drawing's questions afresh", failures);

        // 4. NONE over a drawing: nothing is fed, the drawing is cleared, and the next question is not a follow-up.
        var feeds = page.Count("feed");
        model.Next("NONE");
        var none = await ShellHost.DrawOnStageAsync(new DrawingPlanner(model), stage, monitor, Ask("what time is it?"), Speak, wait, default);
        failures = Check(none.Result.Outcome == DrawingOutcome.None && none.Kept is null && page.Count("feed") == feeds && page.Types[^2..].SequenceEqual(new[] { "clear", "hide" })
            && stage.FollowUpOn(monitor) is null,
            "NONE over a drawing draws nothing and clears the stage (the shell then hands the turn to the Tutor)", failures);

        // 5. Silent (--no-speak): the page paces by its estimate and nothing is spoken.
        spoken.Clear();
        model.Next(Js("say('Quietly drawn.')"));
        var silent = await ShellHost.DrawOnStageAsync(new DrawingPlanner(model), stage, monitor, Ask("draw a cell"), null, wait, default);
        failures = Check(silent.Kept == false && silent.Voice is null && spoken.Count == 0 && page.Begins[^1].Pace == "estimate",
            "without a voice the stage is paced by its own estimate and nothing is spoken", failures);

        // 6. A voice fault: the rest is drawn at once (finishNow), the drawing still finishes, the fault is reported.
        model.Next(Js("say('One.')\nsay('Two.')"));
        var broken = await ShellHost.DrawOnStageAsync(new DrawingPlanner(model), stage, monitor, Ask("count"), (_, _) => throw new InvalidOperationException("no speaker"), wait, default);
        failures = Check(broken.SpeechError is InvalidOperationException && broken.Voice is { CutShort: true } && page.Count("finishNow") == 1,
            "a speaker fault draws the rest at once and comes back as the turn's speech error", failures);

        // 7. A click on a part: raised for the shell, and its name cleaned before it can become a question.
        page.Send(new JsonObject { ["type"] = "figurePart", ["text"] = "lift_fan <img src=x onerror=alert(1)>" }.ToJsonString());
        failures = Check(clicks.SequenceEqual(new[] { "lift_fan <img src=x onerror=alert(1)>" })
            && ShellHost.PartQuestion(clicks[0]) == "Tell me more about the lift fan img src x onerror alert 1."
            && ShellHost.PartQuestion("<>{};") is null && ShellHost.PartQuestion(new string('a', 500))!.Length == "Tell me more about the .".Length + ShellHost.MaxPartChars,
            "a part click reaches the shell, and only letters, digits, spaces, hyphens and apostrophes (60 at most) reach the question", failures);

        // 8. The close button ends the drawing: cleared, hidden, forgotten.
        page.Send("{\"type\":\"close\"}");
        await Task.Delay(50);
        failures = Check(stage.FollowUpOn(monitor) is null && stage.Drawing is null && page.Types[^2..].SequenceEqual(new[] { "clear", "hide" }),
            "the close button clears and hides the stage, and the next question is not a follow-up", failures);

        // 9. A drawing on another monitor: the old window goes, a new one opens there; no follow-up across monitors.
        model.Next(Js("say('Over here.')"));
        var other = new MonitorInfo(2, "\\\\.\\DISPLAY2", new PixelRect(1920, 0, 2560, 1440), false);
        await ShellHost.DrawOnStageAsync(new DrawingPlanner(model), stage, monitor, Ask("again"), Speak, wait, default);
        model.Next(Js("say('Now there.')"));
        var moved = await ShellHost.DrawOnStageAsync(new DrawingPlanner(model), stage, other, Ask("and there?"), Speak, wait, default);
        failures = Check(opened.Count == 2 && opened[0].Disposed && !moved.FollowUp && stage.FollowUpOn(monitor) is null && stage.FollowUpOn(other) is not null,
            "a question on another monitor opens the stage there, closes the old one, and is not a follow-up", failures);

        // 10. A page that never finishes: the turn gives up after its wait, clears the stage, and says so.
        opened[1].NeverFinish = true;
        model.Next(Js("say('Stuck.')"));
        var timedOut = false;
        try
        {
            await ShellHost.DrawOnStageAsync(new DrawingPlanner(model), stage, other, Ask("stuck?"), null, TimeSpan.FromMilliseconds(300), default);
        }
        catch (TimeoutException)
        {
            timedOut = true;
        }

        failures = Check(timedOut && opened[1].Types[^2..].SequenceEqual(new[] { "clear", "hide" }) && stage.FollowUpOn(other) is null,
            "a stage that never reports finished ends the turn (TimeoutException) with the stage cleared", failures);

        // 11. The grounded box in canvas units: a 1920x1080 monitor at x=1920 drawn on a 1280x720 canvas.
        failures = Check(ShellHost.ToCanvas(new PixelRect(2880, 540, 192, 108), new PixelRect(1920, 0, 1920, 1080), 1280, 720) == new PixelRect(640, 360, 128, 72)
            && ShellHost.ToCanvas(null, new PixelRect(0, 0, 10, 10), 10, 10) is null,
            "the grounded box is moved into the screenshot's canvas units (no box, none)", failures);

        return Summarize(failures, "DRAWING LEG OK");
    }

    private static string Js(string program) => "```js\n" + program + "\n```";

    /// <summary>A model that streams scripted replies (in 7-character pieces) and keeps the last prompt.</summary>
    private sealed class ScriptedModel : ILlmClient
    {
        private readonly Queue<string> _replies = new();

        public ScriptedModel(string reply) => _replies.Enqueue(reply);

        public string LastPrompt { get; private set; } = string.Empty;

        public string ProviderId => "scripted";

        public string ModelId => "scripted-model";

        public ProviderCapability Capabilities => ProviderCapability.Text | ProviderCapability.Streaming;

        public void Next(string reply) => _replies.Enqueue(reply);

        public Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken ct = default) => throw new NotSupportedException("the shell's lesson model streams");

        public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(LlmRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            LastPrompt = request.Messages[^1].Text ?? string.Empty;
            var reply = _replies.Dequeue();
            for (var i = 0; i < reply.Length; i += 7)
            {
                await Task.Yield();
                ct.ThrowIfCancellationRequested();
                yield return new LlmStreamChunk { TextDelta = reply.Substring(i, Math.Min(7, reply.Length - i)) };
            }

            yield return new LlmStreamChunk { IsDone = true, InputTokens = 10, OutputTokens = reply.Length };
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// The page as the shell sees it: answers a finished feed like stage.js (a line per say(), then programRan), and
    /// reports finished after the last line's lineEnded (pace speech), at once (pace estimate) or on finishNow.
    /// </summary>
    private sealed class FakePage : IDrawStageSurface, IDisposable
    {
        private string _pace = "estimate";
        private int _lines;

        public event Action<string>? MessageReceived;

        public List<string> Types { get; } = new();

        public List<(int W, int H, string Pace)> Begins { get; } = new();

        public bool LastFeedDone { get; private set; }

        public bool NeverFinish { get; set; }

        public bool Disposed { get; private set; }

        public int Count(string type) => Types.Count(t => t == type);

        public void Send(string json) => MessageReceived?.Invoke(json);

        public Task PostAsync(string json)
        {
            var message = JsonNode.Parse(json)!.AsObject();
            var type = (string)message["type"]!;
            Types.Add(type);
            switch (type)
            {
                case "beginStream":
                    _pace = (string)message["pace"]!;
                    Begins.Add(((int)message["w"]!, (int)message["h"]!, _pace));
                    break;
                case "continueStream":
                    _pace = (string)message["pace"]!;
                    break;
                case "feed":
                    LastFeedDone = (bool)message["done"]!;
                    if (LastFeedDone)
                    {
                        var says = Regex.Matches((string)message["code"]!, @"say\('([^']*)'\)").Select(m => m.Groups[1].Value).ToArray();
                        _lines = says.Length;
                        for (var k = 0; k < says.Length; k++)
                        {
                            Send(new JsonObject { ["type"] = "line", ["k"] = k, ["text"] = says[k] }.ToJsonString());
                        }

                        Send(new JsonObject { ["type"] = "programRan", ["lines"] = says.Length }.ToJsonString());
                        if (_pace == "estimate" || _lines == 0)
                        {
                            Finish();
                        }
                    }

                    break;
                case "lineEnded" when (int)message["k"]! == _lines - 1:
                case "finishNow":
                    Finish();
                    break;
            }

            return Task.CompletedTask;
        }

        public Task ShowAsync()
        {
            Types.Add("show");
            return Task.CompletedTask;
        }

        public Task HideAsync()
        {
            Types.Add("hide");
            return Task.CompletedTask;
        }

        public void Dispose() => Disposed = true;

        private void Finish()
        {
            if (!NeverFinish)
            {
                Send("{\"type\":\"finished\"}");
            }
        }
    }
}
