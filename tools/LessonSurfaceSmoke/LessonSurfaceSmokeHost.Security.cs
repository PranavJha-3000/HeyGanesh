using HeyGanesh.LessonSurface;
using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.Tools.LessonSurfaceSmoke;

/// <summary>
/// LSU-4, the security row: model text (which may echo screen text) is shown as text, never parsed
/// as markup, and the page cannot leave its virtual host or reach the network.
/// </summary>
internal static partial class LessonSurfaceSmokeHost
{
    private static async Task<int> RunAssertSecurityAsync()
    {
        var failures = 0;
        const string Hostile = "<img src=x onerror=alert(1)><script>alert(2)</script>";
        var plan = AppendOp(Plan("chain-rule.json"), 0, new WriteOp("card-main", "w-evil", Hostile, TextStyle.Body, "\\text{<b>bold</b>}"));
        var lesson = Validated(plan);

        await using var surface = await CardSurface.CreateAsync();
        await new LessonPlayer(surface, new PauseSpeaker(60)).PlayAsync(lesson);
        var card = await surface.DescribeCardAsync("card-main");
        failures = Check(card.Texts.TryGetValue("w-evil", out var shown) && shown == Hostile, "HTML/script in model text is shown literally", failures);
        failures = Check(card.ImgElements == 0 && card.ScriptElements == 1 && card.BoldElements == 0,
            $"no element is created from model text (img={card.ImgElements}, script={card.ScriptElements}, b={card.BoldElements})", failures);

        var probe = await surface.ProbeIsolationAsync("card-main");
        failures = Check(probe.NavigationBlocked, "page navigation to https://example.com/ is cancelled; the lesson page stays", failures);
        failures = Check(probe.FetchBlocked, "page fetch() to an outside URL is refused (CSP connect-src 'none')", failures);
        return Summarize(failures, "SECURITY OK");
    }
}
