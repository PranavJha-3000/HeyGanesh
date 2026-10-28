using System.Runtime.InteropServices;
using HeyGanesh.LessonSession;
using HeyGanesh.LessonSurface;
using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.Tools.LessonTurnSmoke;

/// <summary>
/// LT-6/LT-7: the same turn on REAL cards. This is the visual check the App Shell cannot carry
/// (DEC-012 keeps fakes out of it): a fake model, the product CardSurface, and the window contract
/// LSU-2 pins — capture-excluded, never foreground — plus "no card outlives the turn".
/// </summary>
internal static partial class LessonTurnSmokeHost
{
    private const uint WsExNoActivate = 0x08000000, WsExToolWindow = 0x00000080, WsExTopmost = 0x00000008;
    private const uint WdaExcludeFromCapture = 0x11;

    private static async Task<int> RunAssertTurnSurfaceAsync()
    {
        var failures = 0;
        var before = VisibleWindowsOfThisProcess();
        var host = new ProbingCardHost();
        var says = Says("chain-rule.json");
        var speaker = new RecordingSpeaker(delayMs: 150);
        var report = await LessonTurn.RunAsync(Planner(Fixture("chain-rule.json")), host, speaker, Request());

        failures = Check(report.Outcome == LessonTurnOutcome.Played && speaker.Said.SequenceEqual(says), $"chain-rule turn on real cards -> Played, all {says.Count} sentences spoken ({report.Outcome})", failures);
        var card = host.Snapshot;
        failures = Check(card is { Visible: true }, "the main card was visible while the lesson played", failures);
        failures = Check(
            card is not null && (card.ExStyle & WsExNoActivate) != 0 && (card.ExStyle & WsExToolWindow) != 0 && (card.ExStyle & WsExTopmost) != 0,
            $"card ex-styles include NOACTIVATE | TOOLWINDOW | TOPMOST (0x{card?.ExStyle ?? 0:X8})", failures);
        failures = Check(card?.DisplayAffinity == WdaExcludeFromCapture, $"card display affinity is WDA_EXCLUDEFROMCAPTURE (0x{card?.DisplayAffinity ?? 0:X})", failures);
        failures = Check(host.Surface is { EverTookFocus: false }, "no card ever became the foreground window", failures);
        var after = VisibleWindowsOfThisProcess();
        failures = Check(host.VisibleDuring > before && after <= before,
            $"no card outlives the turn ({before} visible window(s) before, {host.VisibleDuring} during, {after} after)", failures);
        return Summarize(failures, "TURN SURFACE OK");
    }

    private static async Task<int> RunDemoTurnAsync(string[] args)
    {
        var voice = args.SkipWhile(a => a != "--voice").Skip(1).FirstOrDefault() ?? "sapi";
        ILessonSpeaker speaker = voice == "sapi" && SapiSpeaker.TryCreate(out var sapi) ? sapi! : new TimedSpeaker();
        Console.WriteLine($"demo: fake model answers the chain-rule lesson; real cards; voice={(speaker is SapiSpeaker ? "sapi" : "timed")}");
        var report = await LessonTurn.RunAsync(Planner(Fixture("chain-rule.json")), new CardSurfaceHost(), speaker, Request());
        Console.WriteLine($"  outcome : {report.Outcome}  plan={report.Plan?.Lesson.Outcome}  beats={report.Playback?.Beats.Count ?? 0}");
        Console.WriteLine("DEMO DONE (exit 0) — a human judges what was drawn and said");
        return 0;
    }

    /// <summary>Opens a real CardSurface and snapshots the main card once the first beat is on it.</summary>
    private sealed class ProbingCardHost : ILessonCanvasHost
    {
        public CardSurface? Surface { get; private set; }
        public CardSnapshot? Snapshot { get; set; }
        public int VisibleDuring { get; set; }

        public async Task<LessonCanvasLease> OpenAsync(CancellationToken cancellationToken)
        {
            Surface = await CardSurface.CreateAsync(cancellationToken: cancellationToken);
            return new LessonCanvasLease(new ProbingCanvas(Surface, this), Surface);
        }
    }

    private sealed class ProbingCanvas : ILessonCanvas
    {
        private readonly CardSurface _surface;
        private readonly ProbingCardHost _host;

        public ProbingCanvas(CardSurface surface, ProbingCardHost host)
        {
            _surface = surface;
            _host = host;
        }

        public async Task ApplyBeatAsync(LessonBeat beat, CancellationToken cancellationToken)
        {
            await _surface.ApplyBeatAsync(beat, cancellationToken);
            if (_host.Snapshot is null)
            {
                _host.Snapshot = await _surface.DescribeCardAsync("card-main");
                _host.VisibleDuring = VisibleWindowsOfThisProcess();
            }
        }

        public Task ShowNoticeAsync(string notice, CancellationToken cancellationToken) => _surface.ShowNoticeAsync(notice, cancellationToken);

        public Task ResetAsync(IReadOnlyList<HeyGanesh.VisualLessonEngine.LessonSurface> surfaces, CancellationToken cancellationToken) =>
            _surface.ResetAsync(surfaces, cancellationToken);
    }

    /// <summary>Silent stand-in: waits roughly as long as the sentence would take to say.</summary>
    private sealed class TimedSpeaker : ILessonSpeaker
    {
        public Task SpeakAsync(string text, CancellationToken cancellationToken) =>
            Task.Delay(Math.Clamp(text.Length * 60, 600, 8000), cancellationToken);
    }

    /// <summary>The Windows SAPI voice via COM late binding (demo only; product speech is Voice).</summary>
    private sealed class SapiSpeaker : ILessonSpeaker
    {
        private const int SvsfAsync = 1, SvsfPurgeBeforeSpeak = 2;
        private readonly dynamic _voice;

        private SapiSpeaker(object voice) => _voice = voice;

        public static bool TryCreate(out SapiSpeaker? speaker)
        {
            speaker = null;
            var type = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (type is null)
            {
                return false;
            }

            speaker = new SapiSpeaker(Activator.CreateInstance(type)!);
            return true;
        }

        public async Task SpeakAsync(string text, CancellationToken cancellationToken)
        {
            _voice.Speak(text, SvsfAsync);
            try
            {
                while (!(bool)_voice.WaitUntilDone(0))
                {
                    await Task.Delay(40, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                _voice.Speak(string.Empty, SvsfAsync | SvsfPurgeBeforeSpeak);
                throw;
            }
        }
    }

    private static int VisibleWindowsOfThisProcess()
    {
        var pid = (uint)Environment.ProcessId;
        var count = 0;
        EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) && GetWindowThreadProcessId(hwnd, out var owner) != 0 && owner == pid)
            {
                count++;
            }

            return true;
        }, IntPtr.Zero);
        return count;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
