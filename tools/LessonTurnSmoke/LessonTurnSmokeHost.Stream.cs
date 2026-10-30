using System.Diagnostics;
using HeyGanesh.LessonPlanner;
using HeyGanesh.LessonSession;
using HeyGanesh.PartLibrary;
using HeyGanesh.Providers.Core;
using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;
using FakeLlm = HeyGanesh.Tools.LessonPlannerSmoke.FakeLlmClient;

namespace HeyGanesh.Tools.LessonTurnSmoke;

/// <summary>
/// LT-10 (DEC-042): with a streaming model the turn opens the cards on the first released beat and plays
/// each beat while the rest of the lesson is still arriving. A stream that drops stops after the beats
/// shown; a non-streaming model, a repaired answer, a fault before the first beat and cancellation all
/// behave as before.
/// </summary>
internal static partial class LessonTurnSmokeHost
{
    private const ProviderCapability StreamingText = ProviderCapability.Text | ProviderCapability.Streaming;

    private static async Task<int> RunAssertTurnStreamAsync()
    {
        var failures = 0;
        var json = Fixture("chain-rule.json");
        var says = Says("chain-rule.json");

        // 1. Streamed: the first beat is on the cards long before the lesson has finished arriving.
        {
            var host = new TimedCanvasHost();
            var speaker = new RecordingSpeaker();
            var planner = new VisualPlanner(new FakeLlm(StreamingText, json) { ChunkChars = 16, FirstTokenDelay = TimeSpan.FromMilliseconds(200), CharsPerSecond = 2000 });
            var report = await LessonTurn.RunAsync(planner, host, speaker, Request());
            var plan = report.Plan;
            var first = host.Canvas.AppliedAt.Count > 0 ? host.Canvas.AppliedAt[0] : TimeSpan.MaxValue;
            Console.WriteLine($"  streamed: first beat drawn at {first.TotalMilliseconds:F0} ms; whole lesson arrived at {plan?.Latency.TotalMilliseconds:F0} ms; FirstBeatShown {report.FirstBeatShown?.TotalMilliseconds:F0} ms");
            failures = Check(report.Outcome == LessonTurnOutcome.Played && plan?.ReleasedBeats == says.Count && plan.StreamNote is null,
                $"a streamed lesson plays to the end, every beat released while streaming ({report.Outcome}, released {plan?.ReleasedBeats})", failures);
            failures = Check(host.Canvas.Beats.SequenceEqual(new[] { "b1", "b2", "b3", "b4", "b5" }) && speaker.Said.SequenceEqual(says),
                "every beat is drawn and spoken in order", failures);
            failures = Check(plan is not null && first < plan.Latency - TimeSpan.FromMilliseconds(300),
                "the first beat is drawn while the rest of the lesson is still arriving", failures);
            failures = Check(report.FirstBeatShown is { } shown && shown < plan!.Latency, $"FirstBeatShown is reported and shorter than the whole plan", failures);
            failures = Check(host.Opened == 1 && host.Closed == 1 && host.Canvas.Resets == 1, $"cards opened once, reset once, closed once ({host.Opened}/{host.Canvas.Resets}/{host.Closed})", failures);
        }

        // 2. The stream drops after beat 2: those two are shown, the turn ends Stopped with the reason.
        {
            var host = new TimedCanvasHost();
            var cut = json.IndexOf("\"id\": \"b3\"", StringComparison.Ordinal);
            var planner = new VisualPlanner(new FakeLlm(StreamingText, json) { ChunkChars = 5, FailAfterChars = cut });
            var report = await LessonTurn.RunAsync(planner, host, new RecordingSpeaker(), Request());
            failures = Check(report.Outcome == LessonTurnOutcome.Stopped && host.Canvas.Beats.SequenceEqual(new[] { "b1", "b2" })
                             && report.Error!.Contains("after 2 beat", StringComparison.Ordinal) && host.Closed == 1,
                $"a dropped stream shows the 2 released beats, ends Stopped with the reason, closes the cards ({report.Outcome}: {report.Error})", failures);
        }

        // 3. A model that cannot stream: the whole-plan path, as before (and FirstBeatShown still reported).
        {
            var host = new TimedCanvasHost();
            var report = await LessonTurn.RunAsync(Planner(json), host, new RecordingSpeaker(), Request());
            failures = Check(report.Outcome == LessonTurnOutcome.Played && report.Plan!.ReleasedBeats == 0 && host.Canvas.Beats.Count == says.Count
                             && report.FirstBeatShown is not null && host.Opened == 1 && host.Closed == 1,
                "without streaming the turn plans the whole lesson first, as before", failures);
        }

        // 4. Nothing valid while streaming: the repaired whole lesson plays on cards opened once.
        {
            var host = new TimedCanvasHost();
            var wrong = json.Replace("heyganesh.visualplan/1", "heyganesh.visualplan/9", StringComparison.Ordinal);
            var report = await LessonTurn.RunAsync(new VisualPlanner(new FakeLlm(StreamingText, wrong, json)), host, new RecordingSpeaker(), Request());
            failures = Check(report.Outcome == LessonTurnOutcome.Played && report.Plan!.Attempts == 2 && host.Canvas.Beats.Count == says.Count && host.Opened == 1,
                $"an answer with nothing valid is repaired, then the whole lesson plays (attempts {report.Plan?.Attempts})", failures);
        }

        // 5. A fault before the first beat: ProviderError and no card ever opened.
        {
            var host = new TimedCanvasHost();
            var report = await LessonTurn.RunAsync(new VisualPlanner(new FakeLlm(StreamingText, json) { FailAfterChars = 10 }), host, new RecordingSpeaker(), Request());
            failures = Check(report.Outcome == LessonTurnOutcome.ProviderError && host.Opened == 0,
                $"a fault before any beat is a ProviderError and opens no card ({report.Outcome}, opened {host.Opened})", failures);
        }

        // 6. Cancelled after the first beat is drawn: Cancelled, cards closed, the stream stopped.
        {
            var host = new TimedCanvasHost();
            using var cts = new CancellationTokenSource();
            host.Canvas.OnApply = () => cts.Cancel();
            var model = new FakeLlm(StreamingText, json) { ChunkChars = 16, CharsPerSecond = 2000 };
            var clock = Stopwatch.StartNew();
            var report = await LessonTurn.RunAsync(new VisualPlanner(model), host, new RecordingSpeaker(), Request(), cts.Token);
            failures = Check(report.Outcome == LessonTurnOutcome.Cancelled && host.Closed == host.Opened && host.Canvas.Beats.Count == 1
                             && clock.ElapsedMilliseconds < 1000,
                $"cancelling after the first beat returns Cancelled at once, closes the cards and stops the stream ({report.Outcome}, {clock.ElapsedMilliseconds} ms)", failures);
        }

        // 7. A lesson with its own schematic (DEC-050): the streamed turn hands the header's schematic to the canvas.
        {
            var host = new TimedCanvasHost();
            var planner = new VisualPlanner(new FakeLlm(StreamingText, Fixture("f35b-schematic.json")) { ChunkChars = 16 });
            var report = await LessonTurn.RunAsync(planner, host, new RecordingSpeaker(),
                Request("can you draw the full jet in 3D?") with { Scene3d = ModelLibrary.ToCatalog(), SchematicGeometry = Schematics.Check });
            failures = Check(report.Outcome == LessonTurnOutcome.Played && report.Plan!.ReleasedBeats == 6 && host.Canvas.Schematics == "f35b" && host.Canvas.Beats.Count == 6,
                $"a streamed lesson with a schematic hands it to the canvas before drawing all 6 beats ({report.Outcome}, schematics '{host.Canvas.Schematics}')", failures);
        }

        return Summarize(failures, "TURN STREAM OK");
    }

    private sealed class TimedCanvasHost : ILessonCanvasHost
    {
        public int Opened { get; private set; }

        public int Closed => Canvas.Closed;

        public TimedCanvas Canvas { get; } = new();

        public Task<LessonCanvasLease> OpenAsync(CancellationToken cancellationToken)
        {
            Opened++;
            Canvas.Clock.Restart();
            return Task.FromResult(new LessonCanvasLease(Canvas, Canvas));
        }
    }

    /// <summary>Records each beat it draws and when, measured from the start of the turn's planning.</summary>
    private sealed class TimedCanvas : ILessonCanvas, ISchematicCanvas, IAsyncDisposable
    {
        /// <summary>The ids of the schematics the player last handed over (DEC-050), comma-joined.</summary>
        public string? Schematics { get; private set; }

        public void UseSchematics(IReadOnlyList<SchematicModel> schematics) => Schematics = string.Join(",", schematics.Select(s => s.Id));

        private readonly Stopwatch _sinceCreated = Stopwatch.StartNew();

        public Stopwatch Clock { get; } = new();

        public List<string> Beats { get; } = new();

        public List<TimeSpan> AppliedAt { get; } = new();

        public int Resets { get; private set; }

        public int Closed { get; private set; }

        public Action? OnApply { get; set; }

        public Task ApplyBeatAsync(LessonBeat beat, CancellationToken cancellationToken)
        {
            Beats.Add(beat.Id);
            AppliedAt.Add(_sinceCreated.Elapsed);
            OnApply?.Invoke();
            return Task.CompletedTask;
        }

        public Task ShowNoticeAsync(string notice, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResetAsync(IReadOnlyList<HeyGanesh.VisualLessonEngine.LessonSurface> surfaces, CancellationToken cancellationToken)
        {
            Resets++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Closed++;
            return ValueTask.CompletedTask;
        }
    }
}
