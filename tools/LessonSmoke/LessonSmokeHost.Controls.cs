using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.Tools.LessonSmoke;

/// <summary>
/// LS-10, the controls row: what the student can do to a playing lesson (pause, skip, stop), what
/// the shell can do (cancel), and what must never happen (a second lesson queuing up, a TTS outage
/// wedging the player, a control pressed while idle poisoning the next lesson).
/// </summary>
internal static partial class LessonSmokeHost
{
    private static async Task<int> RunAssertControlsAsync()
    {
        var failures = 0;
        var lesson = Validate(VisualPlanParser.Parse(Fixture("chain-rule.json")).Plan!);
        var beats = lesson.Plan!.Beats.Count;

        // Pause during beat 1: beat 2 must wait at the boundary until Resume.
        {
            var (log, speaker, player) = Gated();
            var play = player.PlayAsync(lesson);
            await speaker.WaitStartedAsync();
            player.Pause();
            speaker.ReleaseOne();
            await Task.Delay(100);
            var held = !log.Snapshot().Contains("draw:b2") && player.State == PlayerState.Paused;
            player.Resume();
            var report = await ReleaseRestAsync(speaker, play, beats - 1);
            failures = Check(held && report.Status == PlaybackStatus.Completed, "Pause holds the next beat at the boundary (State Paused); Resume finishes the lesson", failures);
        }

        // Skip beat 2 mid-speech: it is recorded Skipped and beat 3 follows.
        {
            var (log, speaker, player) = Gated();
            var play = player.PlayAsync(lesson);
            await speaker.WaitStartedAsync();
            speaker.ReleaseOne();
            await speaker.WaitStartedAsync();
            player.SkipBeat();
            var report = await ReleaseRestAsync(speaker, play, beats - 2);
            failures = Check(
                report.Status == PlaybackStatus.Completed && report.Beats[1].Skipped && !report.Beats[0].Skipped && log.Snapshot().Contains("draw:b3")
                    && speaker.CancelledCalls == 1,
                "SkipBeat during beat 2 -> the speaker's token is cancelled, b2 recorded Skipped, b3 plays next, lesson Completed",
                failures);
        }

        // Stop during beat 3: play ends Stopped, nothing after it is drawn.
        {
            var (log, speaker, player) = Gated();
            var play = player.PlayAsync(lesson);
            for (var i = 0; i < 2; i++)
            {
                await speaker.WaitStartedAsync();
                speaker.ReleaseOne();
            }

            await speaker.WaitStartedAsync();
            player.Stop();
            var report = await play.WaitAsync(TimeSpan.FromSeconds(5));
            failures = Check(
                report.Status == PlaybackStatus.Stopped && !log.Snapshot().Contains("draw:b4") && player.State == PlayerState.Idle,
                "Stop during beat 3 -> Stopped, b4/b5 never drawn, State Idle",
                failures);
        }

        // Stop while paused must not hang.
        {
            var (_, speaker, player) = Gated();
            var play = player.PlayAsync(lesson);
            await speaker.WaitStartedAsync();
            player.Pause();
            speaker.ReleaseOne();
            var reachedPause = await WaitForStateAsync(player, PlayerState.Paused);
            player.Stop();
            var finished = await Task.WhenAny(play, Task.Delay(1000)) == play;
            failures = Check(
                reachedPause && finished && play.Result.Status == PlaybackStatus.Stopped,
                "Stop while paused (State observed Paused first) -> Stopped within 1 s",
                failures);
        }

        // Final review I-1: a control must never run the lesson on the caller's thread. A speaker
        // that completes through a token callback (the usual TTS-adapter shape) plus a slow
        // synchronous draw would otherwise hold the hotkey thread for the whole next draw.
        {
            var canvas = new SlowCanvas();
            var player = new LessonPlayer(canvas, new CallbackSpeaker());
            var play = player.PlayAsync(lesson);
            await Task.Delay(100);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            player.SkipBeat();
            var skipMs = watch.ElapsedMilliseconds;
            player.Stop();
            try { await play.WaitAsync(TimeSpan.FromSeconds(5)); } catch (OperationCanceledException) { }
            failures = Check(
                skipMs < 100 && canvas.DrawThreads.Count >= 1,
                $"SkipBeat returns at once ({skipMs} ms < 100) even when the next draw is slow and synchronous",
                failures);
        }

        // Final review I-2: cancellation is honoured even when the seams ignore their tokens.
        {
            var log = new EventLog();
            var player = new LessonPlayer(new RecordingCanvas(log), new DeafSpeaker(log));
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var threw = false;
            try { await player.PlayAsync(lesson, cts.Token); }
            catch (OperationCanceledException) { threw = true; }
            failures = Check(threw && log.Snapshot().Count == 0, "caller token cancelled BEFORE play -> OperationCanceledException, no seam called", failures);
        }

        {
            var log = new EventLog();
            var deaf = new DeafSpeaker(log);
            var player = new LessonPlayer(new DeafCanvas(log), deaf);
            var play = player.PlayAsync(lesson);
            await deaf.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            player.Stop();
            var report = await play.WaitAsync(TimeSpan.FromSeconds(5));
            failures = Check(
                report.Status == PlaybackStatus.Stopped && !log.Snapshot().Contains("draw:b2"),
                "Stop during beat 1 with seams that ignore their tokens -> Stopped, b2 never drawn",
                failures);
        }

        // A second lesson while one plays is refused, not queued.
        {
            var (_, speaker, player) = Gated();
            var play = player.PlayAsync(lesson);
            await speaker.WaitStartedAsync();
            var second = await player.PlayAsync(lesson).WaitAsync(TimeSpan.FromSeconds(1));
            speaker.ReleaseOne();
            var first = await ReleaseRestAsync(speaker, play, beats - 1);
            failures = Check(
                second.Status == PlaybackStatus.Refused && second.Beats.Count == 0 && first.Status == PlaybackStatus.Completed,
                "second PlayAsync during play -> Refused at once; the first lesson completes",
                failures);
        }

        // The caller's token propagates as OperationCanceledException.
        {
            var (_, speaker, player) = Gated();
            using var cts = new CancellationTokenSource();
            var play = player.PlayAsync(lesson, cts.Token);
            await speaker.WaitStartedAsync();
            cts.Cancel();
            var threw = false;
            try { await play.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { threw = true; }
            failures = Check(threw && player.State == PlayerState.Idle, "caller token cancelled mid-speech -> OperationCanceledException, State Idle", failures);
        }

        // A TTS outage propagates, and the player is usable afterwards.
        {
            var log = new EventLog();
            var speaker = new RecordingSpeaker(log, gated: false) { ThrowAtCall = 1 };
            var player = new LessonPlayer(new RecordingCanvas(log), speaker);
            var faulted = false;
            try { await player.PlayAsync(lesson); }
            catch (InvalidOperationException ex) when (ex.Message == "tts down") { faulted = true; }
            var stateAfter = player.State;
            var again = await player.PlayAsync(lesson);
            failures = Check(
                faulted && stateAfter == PlayerState.Idle && again.Status == PlaybackStatus.Completed,
                "speaker throws on beat 2 -> exception propagates, State Idle, next play Completed",
                failures);
        }

        // Controls pressed while idle do nothing and do not leak into the next lesson.
        {
            var log = new EventLog();
            var player = new LessonPlayer(new RecordingCanvas(log), new RecordingSpeaker(log, gated: false));
            player.SkipBeat();
            player.Pause();
            player.Stop();
            player.Resume();
            var report = await player.PlayAsync(lesson).WaitAsync(TimeSpan.FromSeconds(5));
            failures = Check(
                report.Status == PlaybackStatus.Completed && report.Beats.Count == beats && report.Beats.All(b => !b.Skipped),
                "SkipBeat/Pause/Stop/Resume while idle -> no effect; next lesson plays every beat",
                failures);
        }

        return Summarize(failures, "CONTROLS OK");
    }

    private static async Task<bool> WaitForStateAsync(LessonPlayer player, PlayerState state)
    {
        for (var i = 0; i < 100 && player.State != state; i++)
        {
            await Task.Delay(10);
        }

        return player.State == state;
    }

    /// <summary>Completes its sentence only through a token callback on a plain (synchronous-continuation) TCS.</summary>
    private sealed class CallbackSpeaker : ILessonSpeaker
    {
        public Task SpeakAsync(string text, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource();
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            return tcs.Task;
        }
    }

    /// <summary>Draws synchronously and slowly — what a renderer marshalling to a UI thread can look like.</summary>
    private sealed class SlowCanvas : ILessonCanvas
    {
        public List<int> DrawThreads { get; } = new();

        public Task ApplyBeatAsync(LessonBeat beat, CancellationToken cancellationToken)
        {
            lock (DrawThreads) DrawThreads.Add(Environment.CurrentManagedThreadId);
            Thread.Sleep(250);
            return Task.CompletedTask;
        }

        public Task ShowNoticeAsync(string notice, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResetAsync(IReadOnlyList<LessonSurface> surfaces, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Speaks for 30 ms and ignores its token entirely.</summary>
    private sealed class DeafSpeaker : ILessonSpeaker
    {
        private readonly EventLog _log;

        public DeafSpeaker(EventLog log) => _log = log;

        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task SpeakAsync(string text, CancellationToken cancellationToken)
        {
            _log.Add($"say:{text}");
            FirstStarted.TrySetResult();
            await Task.Delay(30, CancellationToken.None);
        }
    }

    /// <summary>Records draws and ignores its token entirely.</summary>
    private sealed class DeafCanvas : ILessonCanvas
    {
        private readonly EventLog _log;

        public DeafCanvas(EventLog log) => _log = log;

        public Task ApplyBeatAsync(LessonBeat beat, CancellationToken cancellationToken)
        {
            _log.Add($"draw:{beat.Id}");
            return Task.CompletedTask;
        }

        public Task ShowNoticeAsync(string notice, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResetAsync(IReadOnlyList<LessonSurface> surfaces, CancellationToken cancellationToken)
        {
            _log.Add("clear");
            return Task.CompletedTask;
        }
    }

    private static (EventLog Log, RecordingSpeaker Speaker, LessonPlayer Player) Gated()
    {
        var log = new EventLog();
        var speaker = new RecordingSpeaker(log, gated: true);
        return (log, speaker, new LessonPlayer(new RecordingCanvas(log), speaker));
    }

    /// <summary>Lets the remaining <paramref name="count"/> sentences start and finish, then awaits the play.</summary>
    private static async Task<PlaybackReport> ReleaseRestAsync(RecordingSpeaker speaker, Task<PlaybackReport> play, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await speaker.WaitStartedAsync();
            speaker.ReleaseOne();
        }

        return await play.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
