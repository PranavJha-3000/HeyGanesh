using HeyGanesh.Abstractions;
using HeyGanesh.LessonSession;
using HeyGanesh.LessonSurface;
using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;
using HeyGanesh.Voice;

namespace HeyGanesh.App;

// Replay (DEC-060): a stored lesson (TurnNote.LessonPlan, the exact text the planner kept, DEC-059) is parsed and
// validated again, with the same 3D catalogue and schematic check the live lesson had, then drawn and spoken on the
// cards. No model is called. There is no screen grounding now, so screen marks are refused and those beats lose only
// their drawing, as a live lesson with no match would. Speech goes through the running shell's voice when a Fish key
// is set; otherwise each beat is paced silently, as --no-speak does.

internal static partial class ShellHost
{
    /// <summary>The running shell's voice, kept for replays from the tray. Null before RunAsync composes it.</summary>
    private static VoiceService? ReplayVoice;

    /// <summary>The stored plan, ready to play, or why it cannot be. Pure: nothing is drawn or said.</summary>
    internal static (LessonValidationResult? Lesson, string? Refusal) PrepareReplay(string? planJson, PixelRect virtualDesktop)
    {
        if (string.IsNullOrWhiteSpace(planJson))
        {
            return (null, "this turn has no stored lesson to replay");
        }

        var lesson = new VisualPlanValidator().Validate(
            VisualPlanParser.Parse(planJson),
            // Marks must be grounded, and nothing is: the screen the lesson marked is gone, so a mark would land on
            // whatever is there now. Each marked beat keeps its speech and loses only the mark.
            new ValidationContext(virtualDesktop)
            {
                RequireGroundedMarks = true,
                Scene3d = CardSurface.Scene3dCatalog,
                SchematicGeometry = PartLibrary.Schematics.Check,
            });
        if (lesson.Outcome == LessonOutcome.Rejected || lesson.Plan is null)
        {
            var why = lesson.Issues.FirstOrDefault() is { } issue ? $"{issue.Code}: {issue.Message}" : "it did not validate";
            return (null, $"the stored lesson can no longer be played ({why})");
        }

        return (lesson, null);
    }

    /// <summary>Replays on the real cards with the shell's voice (or silent pacing). What the tray calls.</summary>
    internal static Task<ReplayResult> ReplayAsync(string? planJson, CancellationToken ct) =>
        ReplayAsync(planJson, new CardSurfaceHost(), ReplaySpeaker(), NativeFns.VirtualDesktop(), ct);

    /// <summary>Validates, then plays on a canvas from <paramref name="host"/>; nothing is opened for a plan that cannot play.</summary>
    internal static async Task<ReplayResult> ReplayAsync(string? planJson, ILessonCanvasHost host, ILessonSpeaker speaker, PixelRect virtualDesktop, CancellationToken ct)
    {
        var (lesson, refusal) = PrepareReplay(planJson, virtualDesktop);
        if (lesson is null)
        {
            return new ReplayResult(false, refusal!, null);
        }

        var lease = await host.OpenAsync(ct).ConfigureAwait(false);
        try
        {
            var report = await new LessonPlayer(lease.Canvas, speaker).PlayAsync(lesson, ct).ConfigureAwait(false);
            return new ReplayResult(true, $"replayed \"{lesson.Plan!.Title}\": {report.Status}, {report.Beats.Count} beat(s)", report);
        }
        finally
        {
            await lease.Owner.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>The shell's voice when there is a Fish key and it can speak; otherwise a pause sized to the sentence.</summary>
    private static ILessonSpeaker ReplaySpeaker() => new DelegateLessonSpeaker(async (text, token) =>
    {
        if (ReplayVoice is { } voice && HasKey("fish"))
        {
            try
            {
                await voice.SpeakAsync(new SpeakRequest { Text = text }, token).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A live turn is speaking, or the voice failed: this sentence is paced instead, the replay goes on.
            }
        }

        await Task.Delay(Math.Clamp(text.Length * 60, 600, 8000), token).ConfigureAwait(false);
    });
}

/// <summary>How a replay went, in a sentence the tray shows. <see cref="Report"/> is null when nothing was played.</summary>
internal sealed record ReplayResult(bool Played, string Said, PlaybackReport? Report);
