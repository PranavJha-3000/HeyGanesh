using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.Tools.LessonSmoke;

/// <summary>
/// LS-9, playback of the non-happy outcomes: a degraded beat is spoken but never drawn, a text
/// fallback shows one notice and speaks the whole text, a rejected plan touches neither seam, and
/// playing the same result twice (a replay) does exactly the same thing both times.
/// </summary>
internal static partial class LessonSmokeHost
{
    private static async Task<int> RunAssertPlayDegradedAsync()
    {
        var failures = 0;
        var basePlan = VisualPlanParser.Parse(Fixture("chain-rule.json")).Plan!;

        var partial = Validate(WithBeat(basePlan, 1, b => b with { Ops = b.Ops.Select(o => o is HighlightOp ? new HighlightOp("nope") : o).ToList() }));
        var (partialLog, partialReport) = await PlayInstantAsync(partial);
        failures = Check(
            partialLog.Contains($"say:{basePlan.Beats[1].Say}") && !partialLog.Contains("draw:b2") && partialReport.Beats[1].Degraded,
            "degraded beat b2 is spoken but never sent to the canvas; its record is marked Degraded",
            failures);

        var dropped = Validate(WithBeat(basePlan, 4, b => b with { Say = new string('a', 5000) }));
        var (_, droppedReport) = await PlayInstantAsync(dropped);
        failures = Check(
            dropped.Outcome == LessonOutcome.PartiallyDegraded && droppedReport.Beats.Count == 4,
            "a beat the validator dropped is not played: 4 records for the 4 remaining beats",
            failures);

        VisualPlan Break(VisualPlan p, int index) => AppendOp(p, index, new UnknownOp("teleport"));
        var fallback = Validate(Break(Break(Break(basePlan, 1), 3), 4));
        var (fallbackLog, fallbackReport) = await PlayInstantAsync(fallback);
        failures = Check(
            fallbackLog.SequenceEqual(new[] { $"notice:{LessonPlayer.FallbackNotice}", $"say:{fallback.FallbackText}" })
                && fallbackReport.SpokeFallback && fallbackReport.Beats.Count == 0 && fallbackReport.Status == PlaybackStatus.Completed,
            "TextFallback -> exactly one notice, then the whole fallback text spoken; no beat records",
            failures);

        var rejected = Validate(basePlan with { Beats = basePlan.Beats.Select(b => b with { Say = "" }).ToList() });
        var (rejectedLog, rejectedReport) = await PlayInstantAsync(rejected);
        failures = Check(
            rejectedLog.Count == 0 && rejectedReport.Status == PlaybackStatus.Rejected && rejectedReport.Beats.Count == 0,
            "Rejected -> neither canvas nor speaker is called",
            failures);

        var valid = Validate(basePlan);
        var log = new EventLog();
        var player = new LessonPlayer(new RecordingCanvas(log), new RecordingSpeaker(log, gated: false));
        var first = await player.PlayAsync(valid);
        var half = log.Snapshot().Count;
        var second = await player.PlayAsync(valid);
        var all = log.Snapshot();
        failures = Check(
            all.Count == 2 * half
                && all.Take(half).SequenceEqual(all.Skip(half))
                && first.Beats.Select(b => b.BeatId).SequenceEqual(second.Beats.Select(b => b.BeatId)),
            "replay: the same result played twice gives identical seam calls and beat order",
            failures);

        return Summarize(failures, "PLAY DEGRADED OK");
    }

    private static async Task<(IReadOnlyList<string> Log, PlaybackReport Report)> PlayInstantAsync(LessonValidationResult lesson)
    {
        var log = new EventLog();
        var report = await new LessonPlayer(new RecordingCanvas(log), new RecordingSpeaker(log, gated: false)).PlayAsync(lesson);
        return (log.Snapshot(), report);
    }
}
