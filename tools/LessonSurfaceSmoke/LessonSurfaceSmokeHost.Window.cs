using HeyGanesh.LessonSurface;
using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.Tools.LessonSurfaceSmoke;

/// <summary>
/// LSU-2, the window row: cards behave like lesson paper, not app windows — they never take focus,
/// stay on top, are invisible to our own screen capture, land on the desktop — and a wedged or
/// disposed renderer can never hang a lesson.
/// </summary>
internal static partial class LessonSurfaceSmokeHost
{
    private const int WsExNoActivate = 0x08000000, WsExToolWindow = 0x00000080, WsExTopmost = 0x00000008;
    private const uint WdaExcludeFromCapture = 0x11;

    private static async Task<int> RunAssertWindowAsync()
    {
        var failures = 0;
        var lesson = Validated(Plan("chain-rule.json"));

        await using (var surface = await CardSurface.CreateAsync())
        {
            var report = await new LessonPlayer(surface, new PauseSpeaker(150)).PlayAsync(lesson);
            var card = await surface.DescribeCardAsync("card-main");
            failures = Check(report.Status == PlaybackStatus.Completed && card.Visible, "chain-rule plays to Completed on a visible card", failures);
            failures = Check(
                (card.ExStyle & WsExNoActivate) != 0 && (card.ExStyle & WsExToolWindow) != 0 && (card.ExStyle & WsExTopmost) != 0,
                $"card ex-styles include NOACTIVATE | TOOLWINDOW | TOPMOST (0x{card.ExStyle:X8})",
                failures);
            failures = Check(card.DisplayAffinity == WdaExcludeFromCapture, $"card display affinity is WDA_EXCLUDEFROMCAPTURE (0x{card.DisplayAffinity:X})", failures);
            Console.WriteLine($"        (WinForms Activated seen on a card: {surface.ActivationSeen})");
            failures = Check(!surface.EverTookFocus, "no card ever became the foreground window", failures);
            var d = VirtualDesktop();
            failures = Check(
                card.Bounds.X >= d.X && card.Bounds.Y >= d.Y && card.Bounds.Right <= d.Right && card.Bounds.Bottom <= d.Bottom && card.Bounds.Width > 0,
                $"card bounds {card.Bounds} lie on the virtual desktop {d}",
                failures);
        }

        // A render not acknowledged within budget fails the beat, never hangs it. A zero budget is the
        // deterministic form: the page answers in ~2.5 ms but no answer can exist synchronously.
        await using (var surface = await CardSurface.CreateAsync(new CardSurfaceOptions { ApplyTimeout = TimeSpan.Zero }))
        {
            // Final review I-3: the timeout must come from ApplyBeatAsync (reset has its own budget),
            // and a real hang must fail this row rather than masquerade as the expected timeout.
            var play = new LessonPlayer(surface, new PauseSpeaker(10)).PlayAsync(lesson);
            var finished = await Task.WhenAny(play, Task.Delay(TimeSpan.FromSeconds(10))) == play;
            var fromBeat = finished && play.Exception?.InnerException is TimeoutException t && t.Message.Contains("'beat'", StringComparison.Ordinal);
            failures = Check(fromBeat, $"an unacknowledged BEAT -> TimeoutException from ApplyBeatAsync, well inside the 10 s hang guard{(finished ? "" : " (HUNG)")}", failures);
        }

        // Final review I-1: four cards never spill off the working area, even when they are asked to be huge.
        await using (var surface = await CardSurface.CreateAsync(new CardSurfaceOptions { CardHeightDip = 2000 }))
        {
            var four = Plan("chain-rule.json") with
            {
                Surfaces = Enumerable.Range(1, 4).Select(i => new VisualLessonEngine.LessonSurface(i == 1 ? "card-main" : $"card-{i}", SurfaceKind.Card, $"card {i}")).ToList(),
            };
            await new LessonPlayer(surface, new PauseSpeaker(20)).PlayAsync(Validated(four));
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            var all = new List<CardSnapshot>();
            foreach (var s in four.Surfaces)
            {
                all.Add(await surface.DescribeCardAsync(s.Id));
            }

            failures = Check(
                all.All(c => c.Bounds.X >= area.Left && c.Bounds.Y >= area.Top && c.Bounds.Right <= area.Right && c.Bounds.Bottom <= area.Bottom),
                $"4 cards with an oversized height all fit the working area {area.Width}x{area.Height} ({string.Join(" ", all.Select(c => c.Bounds))})",
                failures);
            failures = Check(!all[0].BrowserAcceleratorKeys, "browser accelerator keys (F5/Ctrl+R reload, Ctrl+P, Ctrl+F) are off on cards", failures);
        }

        // Disposing mid-lesson closes the cards and ends the play promptly.
        {
            var surface = await CardSurface.CreateAsync();
            var play = new LessonPlayer(surface, new PauseSpeaker(400)).PlayAsync(lesson);
            await Task.Delay(600);
            var disposed = surface.DisposeAsync().AsTask();
            var ended = await Task.WhenAny(Task.WhenAll(disposed, play.ContinueWith(_ => { }, TaskScheduler.Default)), Task.Delay(5000));
            failures = Check(disposed.IsCompleted && play.IsCompleted, "DisposeAsync mid-lesson -> cards closed and the play ends within 5 s", failures);
        }

        return Summarize(failures, "WINDOW OK");
    }
}
