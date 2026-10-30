using HeyGanesh.Abstractions;
using HeyGanesh.LessonSurface;
using HeyGanesh.ScreenOverlay;
using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.Tools.LessonSurfaceSmoke;

/// <summary>
/// LSU-5, the marks row: a mark on the student's own screen goes to the native, click-through
/// ScreenOverlay (DEC-020), and a second mark while the first is still up replaces it instead of
/// tripping the overlay's one-at-a-time refusal.
/// </summary>
internal static partial class LessonSurfaceSmokeHost
{
    private static async Task<int> RunAssertMarksAsync()
    {
        var failures = 0;
        using var overlay = new ScreenOverlayService(new ScreenOverlayOptions { VisibleDuration = TimeSpan.FromSeconds(2) });
        await using var surface = await CardSurface.CreateAsync(marks: overlay);
        var basePlan = Plan("chain-rule.json");
        var markOnB1 = new ScreenMarkOp(MarkShape.Circle, new PixelRect(400, 400, 120, 40));

        var showingDuringB2 = false;
        var b2Say = basePlan.Beats[1].Say;
        var speaker = new PauseSpeaker(300, text =>
        {
            if (text == b2Say)
            {
                // b2 carries the fixture's mark; the overlay shows it as the sentence starts.
                Thread.Sleep(150);
                showingDuringB2 = overlay.IsShowing;
            }
        });

        var report = await new LessonPlayer(surface, speaker).PlayAsync(Validated(basePlan));
        failures = Check(report.Status == PlaybackStatus.Completed && showingDuringB2, "the fixture's mark (b2) is on the native overlay while b2 is spoken", failures);

        await overlay.HideAsync();
        var twoMarks = Validated(AppendOp(basePlan, 0, markOnB1));
        var second = await new LessonPlayer(surface, new PauseSpeaker(300)).PlayAsync(twoMarks);
        failures = Check(
            second.Status == PlaybackStatus.Completed && surface.MarkFaults.Count == 0,
            $"marks on b1 then b2 while the first is still showing -> replaced, no fault ({surface.MarkFaults.Count} faults)",
            failures);
        await overlay.HideAsync();
        return Summarize(failures, "MARKS OK");
    }
}
