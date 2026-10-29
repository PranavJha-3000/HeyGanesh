using System.Text.Json.Nodes;
using HeyGanesh.Abstractions;
using HeyGanesh.LessonPlanner;
using HeyGanesh.LessonSession;
using HeyGanesh.Providers.Core;
using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.Tools.LessonTurnSmoke;

/// <summary>
/// Verification host for one lesson turn (plan → cards → play → close). Rows run offline against the
/// recording fake model and recorded answers; LT-6/LT-7 also open real cards and need an interactive
/// desktop session. Exit codes: 0 pass, 1 failed check, 2 usage.
/// </summary>
internal static partial class LessonTurnSmokeHost
{
    private static readonly PixelRect Desktop = new(0, 0, 2560, 1440);

    // The box the chain-rule fixture's mark sits inside — the same target LessonPlannerSmoke uses.
    private static readonly PixelRect Target = new(1200, 840, 160, 60);

    public static async Task<int> RunAsync(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "--help";
        Console.WriteLine("Lesson turn verification host (fake model, fake or real cards, no key)");
        Console.WriteLine();
        try
        {
            return mode switch
            {
                "--assert-source" => RunAssertSource(),
                "--assert-turn" => await RunAssertTurnAsync(),
                "--assert-turn-degraded" => await RunAssertTurnDegradedAsync(),
                "--assert-turn-rejected" => await RunAssertTurnRejectedAsync(),
                "--assert-turn-errors" => await RunAssertTurnErrorsAsync(),
                "--assert-turn-surface" => await RunAssertTurnSurfaceAsync(),
                "--assert-followup-pick" => await RunAssertFollowupPickAsync(),
                "--demo-turn" => await RunDemoTurnAsync(args.Skip(1).ToArray()),
                "--demo-pick" => await RunDemoPickAsync(args.Skip(1).ToArray()),
                "--help" or "-h" or "/?" => PrintUsage(),
                _ => Usage(mode),
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"UNEXPECTED: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static int Usage(string bad)
    {
        Console.WriteLine($"unknown argument '{bad}' (exit 2)");
        return 2;
    }

    private static int PrintUsage()
    {
        Console.WriteLine("Usage: LessonTurnSmoke <mode>");
        Console.WriteLine("  --assert-source        LessonSession: no direct NuGet, no Voice/Orchestrator ref, no env/file/network, exit 0");
        Console.WriteLine("  --assert-turn          plan -> cards -> every beat drawn and spoken in order -> cards closed, exit 0");
        Console.WriteLine("  --assert-turn-degraded mostly-broken answer -> fallback notice + spoken text, exit 0");
        Console.WriteLine("  --assert-turn-rejected nothing usable -> Rejected and no card is ever opened, exit 0");
        Console.WriteLine("  --assert-turn-errors   auth/provider faults, cancellation and a speaker fault -> outcomes; cards closed, exit 0");
        Console.WriteLine("  --assert-turn-surface  the same turn on REAL cards: capture-excluded, never foreground, none left after, exit 0");
        Console.WriteLine("  --assert-followup-pick click a 3D part -> library-written follow-up on the same cards; every stop path, exit 0");
        Console.WriteLine("  --demo-turn [--voice sapi|timed]   manual: fake model + real cards + voice, the whole turn");
        Console.WriteLine("  --demo-pick [--voice sapi|timed]   manual: the engine lesson, then click parts to ask about them (same window)");
        return 0;
    }

    private static int Check(bool ok, string label, int failures)
    {
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {label}");
        return ok ? failures : failures + 1;
    }

    private static int Summarize(int failures, string okLabel)
    {
        Console.WriteLine(failures == 0 ? $"{okLabel} (exit 0)" : $"FAILED: {failures} check(s) (exit 1)");
        return failures == 0 ? 0 : 1;
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    /// <summary>A grounded chain-rule request: the fixture's one screen mark lies inside <see cref="Target"/>.</summary>
    private static PlanRequest Request(string question = "why is the derivative of ln(3x^2-1) equal to 6x/(3x^2-1)?") =>
        new() { Question = question, Screen = new ScreenGrounding("a calculus slide", Target, null, 0, 0), VirtualDesktop = Desktop };

    private static VisualPlanner Planner(params string[] answers) =>
        new(new HeyGanesh.Tools.LessonPlannerSmoke.FakeLlmClient(ProviderCapability.Text, answers));

    private static IReadOnlyList<string> Says(string fixture) =>
        VisualPlanParser.Parse(Fixture(fixture)).Plan!.Beats.Select(b => b.Say).ToList();

    /// <summary>The chain-rule fixture with every op replaced by one the validator does not know.</summary>
    private static string AllOpsUnknown(string fixture)
    {
        var root = JsonNode.Parse(Fixture(fixture))!;
        foreach (var beat in root["beats"]!.AsArray())
        {
            beat!["ops"] = new JsonArray(new JsonObject { ["op"] = "teleport", ["surface"] = "card-main" });
        }

        return root.ToJsonString();
    }

    /// <summary>Records what the turn did to its cards: how often they were opened and closed, and what was drawn.</summary>
    private sealed class FakeCanvasHost : ILessonCanvasHost
    {
        public int Opened { get; private set; }
        public int Closed => Canvas.Closed;
        public FakeCanvas Canvas { get; } = new();

        public Task<LessonCanvasLease> OpenAsync(CancellationToken cancellationToken)
        {
            Opened++;
            return Task.FromResult(new LessonCanvasLease(Canvas, Canvas));
        }
    }

    private sealed class FakeCanvas : ILessonCanvas, IAsyncDisposable
    {
        public List<string> Beats { get; } = new();
        public List<string> Notices { get; } = new();
        public int Resets { get; private set; }
        public int Closed { get; private set; }

        public Task ApplyBeatAsync(LessonBeat beat, CancellationToken cancellationToken)
        {
            Beats.Add(beat.Id);
            return Task.CompletedTask;
        }

        public Task ShowNoticeAsync(string notice, CancellationToken cancellationToken)
        {
            Notices.Add(notice);
            return Task.CompletedTask;
        }

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

    /// <summary>Records every sentence; optionally waits per sentence or faults on one.</summary>
    private sealed class RecordingSpeaker : ILessonSpeaker
    {
        private readonly int _delayMs;
        private readonly int _faultOn;

        public RecordingSpeaker(int delayMs = 0, int faultOn = -1)
        {
            _delayMs = delayMs;
            _faultOn = faultOn;
        }

        public List<string> Said { get; } = new();

        public async Task SpeakAsync(string text, CancellationToken cancellationToken)
        {
            if (Said.Count == _faultOn)
            {
                throw new IOException("speaker device lost");
            }

            Said.Add(text);
            if (_delayMs > 0)
            {
                await Task.Delay(_delayMs, cancellationToken);
            }
        }
    }
}
