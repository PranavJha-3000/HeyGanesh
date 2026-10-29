using System.Text.RegularExpressions;
using HeyGanesh.LessonSession;
using HeyGanesh.Providers.Core;
using HeyGanesh.Tools.LessonPlannerSmoke;
using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.Tools.LessonTurnSmoke;

/// <summary>LT-1..LT-5: source audit and the turn's outcomes over a fake model and a fake canvas.</summary>
internal static partial class LessonTurnSmokeHost
{
    private static int RunAssertSource()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "HeyGanesh.sln")))
        {
            root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        }

        var dir = Path.Combine(root!, "src", "Experience", "HeyGanesh.LessonSession");
        var csproj = File.ReadAllText(Path.Combine(dir, "HeyGanesh.LessonSession.csproj"));
        var sources = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText).ToList();
        string[] needles = { "DllImport", "LibraryImport", "HttpClient", "System.Net", "File.", "Directory.", "Environment.", "Process", "Microsoft.Win32", "Socket" };
        var found = needles.Where(n => sources.Any(s => s.Contains(n, StringComparison.Ordinal))).ToArray();
        var refs = Regex.Matches(csproj, "<ProjectReference Include=\"[^\"]*\\\\([^\\\\\"]+)\\.csproj\"").Select(m => m.Groups[1].Value).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var layering = sources.Any(s => s.Contains("HeyGanesh.Voice", StringComparison.Ordinal) || s.Contains("HeyGanesh.ContextOrchestrator", StringComparison.Ordinal));

        var failures = 0;
        failures = Check(!csproj.Contains("<PackageReference", StringComparison.Ordinal), "no direct PackageReference (WebView2 arrives only through LessonSurface)", failures);
        failures = Check(
            refs.SequenceEqual(new[] { "HeyGanesh.LessonPlanner", "HeyGanesh.LessonSurface", "HeyGanesh.PartLibrary", "HeyGanesh.VisualLessonEngine" }),
            $"references are LessonPlanner + LessonSurface + PartLibrary + VisualLessonEngine only ({string.Join(", ", refs)})", failures);
        failures = Check(!layering, "no Voice or ContextOrchestrator use: the shell maps the verdict and owns speech (DEC-015/016)", failures);
        failures = Check(found.Length == 0, $"no P/Invoke, network, file, environment, process or registry use ({(found.Length == 0 ? "none" : string.Join(", ", found))})", failures);
        return Summarize(failures, "SOURCE AUDIT OK");
    }

    private static async Task<int> RunAssertTurnAsync()
    {
        var failures = 0;
        var host = new FakeCanvasHost();
        var speaker = new RecordingSpeaker();
        var report = await LessonTurn.RunAsync(Planner(Fixture("chain-rule.json")), host, speaker, Request());
        var says = Says("chain-rule.json");

        failures = Check(report.Outcome == LessonTurnOutcome.Played && report.Plan!.Lesson.Outcome == LessonOutcome.Valid, $"recorded chain-rule answer -> Played, plan Valid ({report.Outcome})", failures);
        failures = Check(report.Playback!.Beats.Count == says.Count && host.Canvas.Beats.Count > 0, $"every beat played ({report.Playback.Beats.Count}/{says.Count}), {host.Canvas.Beats.Count} drawn", failures);
        failures = Check(speaker.Said.SequenceEqual(says), "every sentence spoken, in beat order, verbatim", failures);
        failures = Check(host.Opened == 1 && host.Canvas.Resets == 1 && host.Closed == 1, "cards opened once, reset once, closed once", failures);
        return Summarize(failures, "TURN OK");
    }

    private static async Task<int> RunAssertTurnDegradedAsync()
    {
        var failures = 0;
        var broken = AllOpsUnknown("chain-rule.json");
        var host = new FakeCanvasHost();
        var speaker = new RecordingSpeaker();
        var report = await LessonTurn.RunAsync(Planner(broken, broken), host, speaker, Request());

        failures = Check(report.Outcome == LessonTurnOutcome.PlayedFallback && report.Plan!.Lesson.Outcome == LessonOutcome.TextFallback,
            $"every beat's drawing unknown -> PlayedFallback over a TextFallback plan ({report.Outcome})", failures);
        failures = Check(host.Canvas.Notices.SequenceEqual(new[] { LessonPlayer.FallbackNotice }) && host.Canvas.Beats.Count == 0, "the fallback notice shown once, nothing drawn", failures);
        failures = Check(speaker.Said.Count == 1 && speaker.Said[0] == report.Plan!.Lesson.FallbackText && speaker.Said[0].Length > 0, "the lesson still spoken, as its fallback text", failures);
        failures = Check(host.Closed == 1, "cards closed", failures);
        return Summarize(failures, "TURN DEGRADED OK");
    }

    private static async Task<int> RunAssertTurnRejectedAsync()
    {
        var failures = 0;
        var host = new FakeCanvasHost();
        var speaker = new RecordingSpeaker();
        var report = await LessonTurn.RunAsync(Planner("I would love to explain that!", "Sure, here is a lesson about it."), host, speaker, Request());

        failures = Check(report.Outcome == LessonTurnOutcome.Rejected && report.Plan!.Attempts == 2, $"prose with no JSON, twice -> Rejected after the repair turn ({report.Outcome})", failures);
        failures = Check(host.Opened == 0 && speaker.Said.Count == 0, "no card opened and nothing said for a lesson that cannot exist", failures);
        return Summarize(failures, "TURN REJECTED OK");
    }

    private static async Task<int> RunAssertTurnErrorsAsync()
    {
        var failures = 0;

        // The fake answers with a message carrying a key-shaped string: the report must not echo it.
        var authHost = new FakeCanvasHost();
        var auth = await LessonTurn.RunAsync(
            new(new FakeLlmClient(ProviderCapability.Text) { Throw = new ProviderException(ProviderErrorKind.AuthFailed, "fake", "fake-model", "bad key sk-ECHOED-SECRET") }),
            authHost, new RecordingSpeaker(), Request());
        failures = Check(auth.Outcome == LessonTurnOutcome.AuthError && authHost.Opened == 0, "AuthFailed while planning -> AuthError, no card opened", failures);
        failures = Check(auth.Error is { } e && e.Contains("AuthFailed", StringComparison.Ordinal) && !e.Contains("sk-ECHOED", StringComparison.Ordinal), $"error is the provider's secret-free form ({auth.Error})", failures);

        var limited = await LessonTurn.RunAsync(
            new(new FakeLlmClient(ProviderCapability.Text) { Throw = new ProviderException(ProviderErrorKind.RateLimited, "fake", "fake-model") }),
            new FakeCanvasHost(), new RecordingSpeaker(), Request());
        failures = Check(limited.Outcome == LessonTurnOutcome.ProviderError, "RateLimited while planning -> ProviderError", failures);

        using (var cts = new CancellationTokenSource(150))
        {
            var hangHost = new FakeCanvasHost();
            var hung = await LessonTurn.RunAsync(new(new FakeLlmClient(ProviderCapability.Text) { Hang = true }), hangHost, new RecordingSpeaker(), Request(), cts.Token);
            failures = Check(hung.Outcome == LessonTurnOutcome.Cancelled && hung.Plan is null && hangHost.Opened == 0, "cancel while the model is thinking -> Cancelled, no card opened", failures);
        }

        using (var cts = new CancellationTokenSource(500))
        {
            var playHost = new FakeCanvasHost();
            var slow = new RecordingSpeaker(delayMs: 300);
            var mid = await LessonTurn.RunAsync(Planner(Fixture("chain-rule.json")), playHost, slow, Request(), cts.Token);
            failures = Check(mid.Outcome == LessonTurnOutcome.Cancelled && mid.Plan is not null && slow.Said.Count is >= 1 and < 5 && playHost.Closed == 1,
                $"cancel mid-lesson -> Cancelled after {slow.Said.Count} sentence(s), cards closed", failures);
        }

        var faultHost = new FakeCanvasHost();
        var escaped = false;
        try
        {
            await LessonTurn.RunAsync(Planner(Fixture("chain-rule.json")), faultHost, new RecordingSpeaker(faultOn: 1), Request());
        }
        catch (IOException)
        {
            escaped = true;
        }

        failures = Check(escaped && faultHost.Closed == 1, "a speaker fault propagates (the shell maps Voice's errors) and the cards are still closed", failures);
        return Summarize(failures, "TURN ERRORS OK");
    }
}
