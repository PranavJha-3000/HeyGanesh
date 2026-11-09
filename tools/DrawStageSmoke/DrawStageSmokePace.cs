using System.Collections.Concurrent;
using System.Diagnostics;
using HeyGanesh.DrawStage;
using static HeyGanesh.Tools.DrawStageSmoke.DrawStageSmokeHost;

namespace HeyGanesh.Tools.DrawStageSmoke;

/// <summary>
/// DS-6 and DS-7 (FEATURE-011 step 4, DEC-071): <see cref="DrawStageController"/> and <see cref="DrawStageVoice"/> on the
/// real stage window. Local only, like DS-2. Neither moves the cursor or presses a key; DS-7 may speak aloud.
/// </summary>
internal static partial class DrawStageSmokeWindow
{
    private const string Painted = "document.querySelectorAll('#stage > g').length";

    private const string ThreeBeats = """
        step(1); say('First, a red dot.')
        dot(200, 200, 10, {color: 'red'})
        step(2); say('Then a blue line.')
        line([[200, 200], [600, 300]], {color: 'blue'})
        step(3); say('And a label.')
        text(600, 320, 'done')
        """;

    // DS-6: the voice paces the drawing; a failing voice and the close button end it cleanly.
    public static int RunAssertPace() => WithStage((stage, messages) =>
    {
        var failures = 0;
        var controller = new DrawStageController(stage);
        var clock = Stopwatch.StartNew();

        // A fake voice: 1.4 s a line, and halfway through it counts what the page has painted so far.
        var heard = new ConcurrentQueue<(string Text, double Start, double End, string Painted)>();
        async Task Speak(string text, CancellationToken ct)
        {
            var start = clock.Elapsed.TotalSeconds;
            await Task.Delay(700, ct);
            var painted = await stage.RunHostScriptAsync(Painted);
            await Task.Delay(700, ct);
            heard.Enqueue((text, start, clock.Elapsed.TotalSeconds, painted));
        }

        var paced = Drive(controller, ThreeBeats, Speak, clock, 1280, 720, expectFinish: true);
        var lines = heard.ToArray();
        failures = Check(paced.Voice == new DrawStageVoiceResult(3, false, false) && lines.Select(l => l.Text).SequenceEqual(new[] { "First, a red dot.", "Then a blue line.", "And a label." }),
            $"the voice speaks the program's 3 lines in order ({paced.Voice})", failures);
        failures = Check(lines.Select(l => l.Painted).SequenceEqual(new[] { "1", "2", "3" }),
            $"each beat's mark is drawn while its line is heard, not before (painted mid-line: {string.Join(", ", lines.Select(l => l.Painted))})", failures);
        failures = Check(paced.Due.SequenceEqual(new[] { 0, 1, 2 }), $"the page asks for each line in turn (lineDue {string.Join(", ", paced.Due)})", failures);
        // The page ends the last line at lineEnded or, if that has not come, at its estimated length (2.6 words a second),
        // so 'finished' can come a little before a slow voice ends: the shell waits for both (DEC-071).
        var last = lines.Length > 0 ? lines[^1] : (Text: "", Start: double.MaxValue, End: double.MaxValue, Painted: "");
        failures = Check(paced.FinishedAt > last.Start && paced.FinishedAt - last.End < 3 && paced.Errors.Count == 0,
            $"'finished' comes once the last line has started and been drawn ({paced.FinishedAt - last.Start:F2} s after its start, {paced.FinishedAt - last.End:F2} s from its end), with no page error", failures);

        // Only the close button is offered (DEC-071).
        var buttons = Run(stage.RunHostScriptAsync("['picture', 'video', 'pip', 'close'].map(c => getComputedStyle(document.querySelector('#tools button.' + c)).display).join(' ')")).Trim('"');
        failures = Check(buttons.StartsWith("none none none ", StringComparison.Ordinal) && !buttons.EndsWith(" none", StringComparison.Ordinal),
            $"Picture, Video and PiP are hidden and the close button is not (display: {buttons})", failures);

        // A voice that fails on its second line: the rest is drawn at once.
        var calls = 0;
        Task Failing(string text, CancellationToken ct) => Interlocked.Increment(ref calls) == 2 ? throw new InvalidOperationException("speaker gone") : Task.Delay(500, ct);
        var cut = Drive(controller, ThreeBeats, Failing, clock, 1280, 720, expectFinish: true);
        var cutPainted = Run(stage.RunHostScriptAsync(Painted));
        failures = Check(cut.Voice == new DrawStageVoiceResult(1, true, false) && cut.FinishedAt > 0 && cutPainted == "3" && cut.Errors.Count == 0,
            $"a voice cut short draws the rest at once and finishes ({cut.Voice}, painted {cutPainted})", failures);

        // The close button: a slow voice, closed during its first line.
        var started = 0;
        async Task Slow(string text, CancellationToken ct)
        {
            Interlocked.Exchange(ref started, 1);
            await Task.Delay(4_000, ct);
        }

        var closedRaised = 0;
        controller.Closed += () => Interlocked.Exchange(ref closedRaised, 1);
        var watch = Stopwatch.StartNew();
        var closed = Drive(controller, ThreeBeats, Slow, clock, 1280, 720, expectFinish: false, whileRunning: () =>
        {
            WaitFor(() => Volatile.Read(ref started) == 1, TimeSpan.FromSeconds(5));
            Run(stage.RunHostScriptAsync("chrome.webview.postMessage({type: 'close'}); 1"));
        });
        var closeSeconds = watch.Elapsed.TotalSeconds;
        var afterClose = Run(stage.RunHostScriptAsync(Painted));
        failures = Check(closed.Voice == new DrawStageVoiceResult(0, false, true) && closeSeconds < 4 && Volatile.Read(ref closedRaised) == 1,
            $"the close button stops the voice at once and raises Closed ({closed.Voice}, {closeSeconds:F1} s)", failures);
        failures = Check(!IsWindowVisible(stage.Handle) && afterClose == "0" && !controller.IsShowing,
            $"and clears the drawing and hides the window (painted {afterClose})", failures);

        return Summarize(failures, "PACE OK");
    });

    // DS-7, by hand: a sample drawn and spoken, by the Windows voice (--voice sapi) or silently at reading pace.
    public static int RunDemo(string sample, bool sapi) => WithStage((stage, messages) =>
    {
        if (Path.GetFileName(sample) != sample || !File.Exists(Path.Combine(DrawStagePage.Directory, "samples", sample)))
        {
            Console.WriteLine($"no sample '{sample}' in {DrawStagePage.Folder}/samples (exit 2)");
            return 2;
        }

        Func<string, CancellationToken, Task> speak = (text, ct) => Task.Delay(TimeSpan.FromSeconds(Math.Max(0.8, text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length / 2.6)), ct);
        if (sapi && SapiVoice.TryCreate(out var voice))
        {
            speak = voice!.SpeakAsync;
        }

        var controller = new DrawStageController(stage);
        controller.LineAdded += (k, text) => Console.WriteLine($"  line {k}: {text}");
        controller.Logged += line => Console.WriteLine($"  page: {line}");
        var code = File.ReadAllText(Path.Combine(DrawStagePage.Directory, "samples", sample));
        var run = Drive(controller, code, speak, Stopwatch.StartNew(), stage.Frame.Width, stage.Frame.Height, expectFinish: true);
        Thread.Sleep(3000);
        Run(controller.ClearAsync("demo done"));
        Console.WriteLine($"  {run.Voice}, finished at {run.FinishedAt:F1} s");
        return Summarize(run.Errors.Count == 0 && run.FinishedAt > 0 ? 0 : 1, "DEMO DONE (judge it by eye and ear)");
    });

    // One program through the controller and the voice, as the shell will run it (pace speech).
    private static (DrawStageVoiceResult Voice, double FinishedAt, List<int> Due, List<string> Errors) Drive(DrawStageController controller, string program,
        Func<string, CancellationToken, Task> speak, Stopwatch clock, int width, int height, bool expectFinish, Action? whileRunning = null)
    {
        var finishedAt = -1.0;
        var due = new ConcurrentQueue<int>();
        var errors = new ConcurrentQueue<string>();
        void Finished() => Interlocked.CompareExchange(ref finishedAt, clock.Elapsed.TotalSeconds, -1.0);
        void Due(int k, string text) => due.Enqueue(k);
        void Logged(string line)
        {
            if (line.StartsWith("error: ", StringComparison.Ordinal))
            {
                errors.Enqueue(line);
            }
        }

        controller.Finished += Finished;
        controller.LineDue += Due;
        controller.Logged += Logged;
        try
        {
            var voice = DrawStageVoice.RunAsync(controller, speak, CancellationToken.None);
            Run(controller.BeginAsync(width, height, DrawStagePace.Speech));
            Run(controller.FeedAsync(program, done: true));
            whileRunning?.Invoke();
            var result = Run(voice);
            if (expectFinish)
            {
                WaitFor(() => Volatile.Read(ref finishedAt) > 0, TimeSpan.FromSeconds(10));
            }

            return (result, Volatile.Read(ref finishedAt), due.ToList(), errors.ToList());
        }
        finally
        {
            controller.Finished -= Finished;
            controller.LineDue -= Due;
            controller.Logged -= Logged;
        }
    }

    /// <summary>The Windows SAPI voice by COM late binding, as the deleted LessonSurfaceSmoke's demo had it. Demo-host only.</summary>
    private sealed class SapiVoice
    {
        private const int SvsfAsync = 1, SvsfPurgeBeforeSpeak = 2;
        private readonly dynamic _voice;

        private SapiVoice(object voice) => _voice = voice;

        public static bool TryCreate(out SapiVoice? voice)
        {
            var type = Type.GetTypeFromProgID("SAPI.SpVoice");
            voice = type is null ? null : new SapiVoice(Activator.CreateInstance(type)!);
            return voice is not null;
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
}
