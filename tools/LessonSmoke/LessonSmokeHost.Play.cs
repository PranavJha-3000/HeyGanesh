using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.Tools.LessonSmoke;

/// <summary>
/// LS-8, the playback row: each beat is drawn, then spoken, and the next beat waits for the speech to
/// finish — narration and drawing stay together (temporal contiguity) and speech length is the clock.
/// </summary>
internal static partial class LessonSmokeHost
{
    private static async Task<int> RunAssertPlayAsync()
    {
        var failures = 0;
        var lesson = Validate(VisualPlanParser.Parse(Fixture("chain-rule.json")).Plan!);
        var log = new EventLog();
        var speaker = new RecordingSpeaker(log, gated: true);
        var player = new LessonPlayer(new RecordingCanvas(log), speaker);

        var play = player.PlayAsync(lesson);
        var waitedForSpeech = true;
        var playingMidLesson = false;
        for (var i = 0; i < lesson.Plan!.Beats.Count; i++)
        {
            waitedForSpeech &= await speaker.WaitStartedAsync();
            playingMidLesson |= player.State == PlayerState.Playing;
            // While beat i is still being spoken, beat i+1 must not have been drawn.
            if (i + 1 < lesson.Plan.Beats.Count)
            {
                await Task.Delay(30);
                waitedForSpeech &= !log.Snapshot().Contains($"draw:{lesson.Plan.Beats[i + 1].Id}");
            }

            speaker.ReleaseOne();
        }

        var report = await play.WaitAsync(TimeSpan.FromSeconds(5));
        var expected = new List<string> { "clear" };
        foreach (var beat in lesson.Plan.Beats)
        {
            expected.Add($"draw:{beat.Id}");
            expected.Add($"say:{beat.Say}");
        }

        failures = Check(log.Snapshot().SequenceEqual(expected), "log is clear, then draw:bN, say:bN for every beat in order", failures);
        failures = Check(waitedForSpeech, "beat N+1 is not drawn until beat N's speech has finished", failures);
        failures = Check(playingMidLesson && player.State == PlayerState.Idle, "State is Playing mid-lesson and Idle after", failures);
        failures = Check(
            report.Status == PlaybackStatus.Completed && report.Outcome == LessonOutcome.Valid && !report.SpokeFallback,
            "report: Completed, Valid, no fallback",
            failures);
        failures = Check(
            report.Beats.Count == 5
                && report.Beats.Select(b => b.Index).SequenceEqual(Enumerable.Range(0, 5))
                && report.Beats.Select(b => b.BeatId).SequenceEqual(lesson.Plan.Beats.Select(b => b.Id))
                && report.Beats.All(b => b.EndedAt >= b.StartedAt && !b.Degraded && !b.Skipped),
            "report: one record per beat, in order, EndedAt >= StartedAt, none degraded or skipped",
            failures);

        return Summarize(failures, "PLAY OK");
    }
}
