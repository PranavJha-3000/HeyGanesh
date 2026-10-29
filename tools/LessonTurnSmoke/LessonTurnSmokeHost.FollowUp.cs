using HeyGanesh.LessonPlanner;
using HeyGanesh.LessonSession;
using HeyGanesh.LessonSurface;
using HeyGanesh.PartLibrary;
using HeyGanesh.PartLibrary.Models;
using HeyGanesh.Providers.Core;
using HeyGanesh.Tools.LessonPlannerSmoke;
using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.Tools.LessonTurnSmoke;

/// <summary>
/// LT-8: click-to-ask follow-ups (DEC-031) over a fake model and a fake canvas that "clicks" on cue.
/// Proves the loop's shape — the question is the library's, the previous lesson goes along, the same
/// cards are reused and always closed, and every stop condition ends it.
/// </summary>
internal static partial class LessonTurnSmokeHost
{
    private const string EngineFixture = "engine-3d.json";

    private static readonly PartPick RodPick = new("engine", FourStrokeEngine.Id, "connecting-rod");

    private static async Task<int> RunAssertFollowupPickAsync()
    {
        var failures = 0;
        // The step 2 fixture names a test-only model; the Part Library's real engine replaces it (as LSU-9 does).
        var engine = Fixture(EngineFixture).Replace("\"engine-test\"", $"\"{FourStrokeEngine.Id}\"", StringComparison.Ordinal);

        // One click, then the student walks away: two lessons on one set of cards.
        var client = new FakeLlmClient(ProviderCapability.Text, engine, engine);
        var host = new FakePickHost(RodPick, null);
        var report = await LessonTurn.RunWithFollowUpsAsync(new VisualPlanner(client), host, new RecordingSpeaker(), EngineRequest(), new FollowUpOptions { IdleTimeout = TimeSpan.FromSeconds(7) });
        failures = Check(report.Turns.Count == 2 && report.Turns.All(t => t.Outcome == LessonTurnOutcome.Played) && report.End == FollowUpEnd.NoPick && report.Picks.SequenceEqual(new[] { RodPick }),
            $"3D lesson, one click on the connecting rod, then no click -> 2 lessons played, ends NoPick ({report.Turns.Count}, {report.End})", failures);

        var rod = ModelLibrary.Find(FourStrokeEngine.Id)!.Part("connecting-rod");
        var second = client.Recorded.Count == 2 ? UserText(client.Recorded[1]) : string.Empty;
        failures = Check(second.Contains("Tell me more about the connecting rod in the single-cylinder four-stroke engine.", StringComparison.Ordinal)
            && second.Contains(rod.Description, StringComparison.Ordinal),
            "the follow-up question is written from the Part Library's name and description", failures);
        failures = Check(second.Contains("PREVIOUS LESSON (UNTRUSTED", StringComparison.Ordinal) && second.Contains(report.Turns[0].Plan!.Lesson.Plan!.Title, StringComparison.Ordinal),
            "the lesson just shown goes along as the PREVIOUS LESSON block", failures);
        failures = Check(UserText(client.Recorded[0]).Contains("an engine diagram", StringComparison.Ordinal) && !second.Contains("an engine diagram", StringComparison.Ordinal),
            "the follow-up carries no screen grounding (it may not mark the student's screen)", failures);
        failures = Check(host.Opened == 1 && host.Canvas.Resets == 2 && host.Canvas.Closed == 1,
            $"the same cards: opened once, reset per lesson, closed once (opened {host.Opened}, resets {host.Canvas.Resets}, closed {host.Canvas.Closed})", failures);
        failures = Check(host.Canvas.Idle.All(i => i == TimeSpan.FromSeconds(7)) && host.Canvas.Idle.Count == 2, "each wait for a click uses the configured idle timeout", failures);

        // The bound: clicks keep coming, but only MaxFollowUps are asked.
        var greedy = new FakePickHost(RodPick, RodPick, RodPick, RodPick);
        var bounded = await LessonTurn.RunWithFollowUpsAsync(Planner(engine, engine, engine, engine), greedy, new RecordingSpeaker(), EngineRequest(), new FollowUpOptions { MaxFollowUps = 2 });
        failures = Check(bounded.Turns.Count == 3 && bounded.End == FollowUpEnd.MaxFollowUps && greedy.Canvas.Idle.Count == 2 && greedy.Canvas.Closed == 1,
            $"MaxFollowUps 2 -> 3 lessons, no third click offered, cards closed ({bounded.Turns.Count}, {bounded.End})", failures);

        // A pick the library cannot name is never asked about.
        var oddClient = new FakeLlmClient(ProviderCapability.Text, engine, engine);
        var odd = new FakePickHost(new PartPick("engine", FourStrokeEngine.Id, "flux-capacitor"));
        var unknown = await LessonTurn.RunWithFollowUpsAsync(new VisualPlanner(oddClient), odd, new RecordingSpeaker(), EngineRequest());
        failures = Check(unknown.End == FollowUpEnd.UnknownPart && oddClient.Recorded.Count == 1 && unknown.Picks.Count == 0 && odd.Canvas.Closed == 1,
            "a pick for a part the library lacks -> UnknownPart, the model is never asked", failures);

        // Nothing to click: a 2D lesson, or a canvas that cannot take clicks.
        var flat = new FakePickHost(RodPick);
        var noScene = await LessonTurn.RunWithFollowUpsAsync(Planner(Fixture("chain-rule.json")), flat, new RecordingSpeaker(), Request());
        failures = Check(noScene.End == FollowUpEnd.NoScene && flat.Canvas.Idle.Count == 0 && noScene.Turns.Count == 1, "a 2D lesson -> NoScene, no click asked for", failures);
        var plain = new FakeCanvasHost();
        var notPickable = await LessonTurn.RunWithFollowUpsAsync(Planner(engine), plain, new RecordingSpeaker(), EngineRequest());
        failures = Check(notPickable.End == FollowUpEnd.NotPickable && plain.Closed == 1, "a canvas without IPartPicker -> NotPickable, cards closed", failures);

        // A follow-up the model botches ends the loop with the reason, cards still closed.
        var botch = new FakePickHost(RodPick);
        var rejected = await LessonTurn.RunWithFollowUpsAsync(Planner(engine, "no json here", "still none"), botch, new RecordingSpeaker(), EngineRequest());
        failures = Check(rejected.Turns.Count == 2 && rejected.Turns[1].Outcome == LessonTurnOutcome.Rejected && rejected.End == FollowUpEnd.TurnEnded && botch.Canvas.Closed == 1,
            "a rejected follow-up -> TurnEnded, the first lesson's cards closed", failures);

        // Cancel while the cards wait for a click.
        using (var cts = new CancellationTokenSource(300))
        {
            var waiting = new FakePickHost(FakePickCanvas.Hang);
            var cancelled = await LessonTurn.RunWithFollowUpsAsync(Planner(engine), waiting, new RecordingSpeaker(), EngineRequest(), null, cts.Token);
            failures = Check(cancelled.End == FollowUpEnd.Cancelled && waiting.Canvas.Closed == 1, "cancel while waiting for a click -> Cancelled, cards closed", failures);
        }

        return Summarize(failures, "FOLLOWUP PICK OK");
    }

    /// <summary>
    /// LT-9, manual: the engine lesson on real cards; when it ends, click any part and a short canned
    /// follow-up plays in the same window (the fake model cannot know which part — the question it was
    /// sent is printed so a human can check it). Up to 3 clicks; close the window or wait 60 s to end.
    /// </summary>
    private static async Task<int> RunDemoPickAsync(string[] args)
    {
        var voice = args.SkipWhile(a => a != "--voice").Skip(1).FirstOrDefault() ?? "sapi";
        ILessonSpeaker speaker = voice == "sapi" && SapiSpeaker.TryCreate(out var sapi) ? sapi! : new TimedSpeaker();
        var engine = Fixture(EngineFixture).Replace("\"engine-test\"", $"\"{FourStrokeEngine.Id}\"", StringComparison.Ordinal);
        const string followUp = """
            { "schema": "heyganesh.visualplan/1.1", "lessonId": "part-follow-up", "title": "a closer look", "mode": "explain",
              "surfaces": [ { "id": "scene", "kind": "scene3d", "title": "a closer look" } ],
              "beats": [
                { "id": "b1", "say": "Here is the engine again, so we can look at the part you picked.", "ops": [ { "op": "model", "surface": "scene", "id": "engine", "model": "four-stroke-engine" } ] },
                { "id": "b2", "say": "Watch how it moves through one full cycle.", "ops": [ { "op": "animate", "target": "engine", "animation": "stroke-cycle" } ] } ] }
            """;
        var client = new FakeLlmClient(ProviderCapability.Text, engine, followUp, followUp, followUp);
        Console.WriteLine($"demo: fake model; real cards; voice={(speaker is SapiSpeaker ? "sapi" : "timed")}. When the lesson ends, click a part (up to 3 times).");
        var report = await LessonTurn.RunWithFollowUpsAsync(new VisualPlanner(client), new CardSurfaceHost(), speaker, EngineRequest());
        for (var i = 0; i < report.Turns.Count; i++)
        {
            Console.WriteLine($"  turn {i + 1}: {report.Turns[i].Outcome}");
            if (i > 0)
            {
                var asked = UserText(client.Recorded[i]);
                var start = asked.IndexOf("Tell me more", StringComparison.Ordinal);
                Console.WriteLine($"           asked: {(start < 0 ? "(question not found)" : asked[start..asked.IndexOf('\n', start)])}");
            }
        }

        Console.WriteLine($"  clicked : {string.Join(", ", report.Picks.Select(p => p.PartId))}");
        Console.WriteLine($"  ended   : {report.End}");
        Console.WriteLine("DEMO DONE (exit 0) -- a human judges what was drawn, said and asked");
        return 0;
    }

    /// <summary>The engine lesson, planned for a canvas that can draw the Part Library, with a screen description to prove it is dropped.</summary>
    private static PlanRequest EngineRequest() => new()
    {
        Question = "how does a four-stroke engine work?",
        Screen = new ScreenGrounding("an engine diagram", null, null, 0, 0),
        VirtualDesktop = Desktop,
        Scene3d = CardSurface.Scene3dCatalog,
    };

    private static string UserText(LlmRequest r) => r.Messages.Last(m => m.Role == "user").Text ?? string.Empty;

    private sealed class FakePickHost : ILessonCanvasHost
    {
        public FakePickHost(params PartPick?[] picks) => Canvas = new FakePickCanvas(picks);

        public int Opened { get; private set; }
        public FakePickCanvas Canvas { get; }

        public Task<LessonCanvasLease> OpenAsync(CancellationToken cancellationToken)
        {
            Opened++;
            return Task.FromResult(new LessonCanvasLease(Canvas, Canvas));
        }
    }

    /// <summary>Answers each <see cref="PickAsync"/> with the next scripted pick; <see cref="Hang"/> waits until cancelled; past the script, null.</summary>
    private sealed class FakePickCanvas : ILessonCanvas, IPartPicker, IAsyncDisposable
    {
        public static readonly PartPick Hang = new("hang", "hang", "hang");

        private readonly Queue<PartPick?> _picks;

        public FakePickCanvas(PartPick?[] picks) => _picks = new Queue<PartPick?>(picks);

        public int Resets { get; private set; }
        public int Closed { get; private set; }
        public List<TimeSpan> Idle { get; } = new();

        public async Task<PartPick?> PickAsync(TimeSpan idle, CancellationToken cancellationToken)
        {
            Idle.Add(idle);
            var next = _picks.Count > 0 ? _picks.Dequeue() : null;
            if (ReferenceEquals(next, Hang))
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return next;
        }

        public Task ApplyBeatAsync(LessonBeat beat, CancellationToken cancellationToken) => Task.CompletedTask;

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
