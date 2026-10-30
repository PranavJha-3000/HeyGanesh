using HeyGanesh.LessonSurface;
using HeyGanesh.PartLibrary;
using HeyGanesh.PartLibrary.Models;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.Tools.LessonSurfaceSmoke;

/// <summary>
/// LSU-12 and LSU-16: the 3D lesson without WebGL is drawn as an isometric SVG (DEC-034). Every row runs
/// on a real scene window started with <c>--disable-webgl</c> in its own throwaway profile, so the
/// drawing path is exercised exactly as a PC without WebGL would see it.
/// </summary>
internal static partial class LessonSurfaceSmokeHost
{
    /// <summary>A scene surface with WebGL off, in a temp profile that is removed afterwards.</summary>
    private static async Task<int> WithoutWebGlAsync(string tag, Func<CardSurface, Task<int>> body)
    {
        var profile = Path.Combine(Path.GetTempPath(), $"hg-{tag}-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var surface = await CardSurface.CreateAsync(new CardSurfaceOptions { AdditionalBrowserArguments = "--disable-webgl", UserDataFolder = profile });
            return await body(surface);
        }
        finally
        {
            try
            {
                Directory.Delete(profile, recursive: true);
            }
            catch (IOException)
            {
                // WebView2 may still hold the temp profile for a moment; it is under %TEMP%.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    // ---- LSU-16: the drawing itself -------------------------------------------------------------

    private static Task<int> RunAssertSceneSvgAsync() => WithoutWebGlAsync("lsu16", async surface =>
    {
        var failures = 0;
        var probe = await surface.CheckOrientationAsync();
        Console.WriteLine($"          {probe.Detail}");
        failures = Check(probe.XRight && probe.YUp && probe.ZTurnMapsYToMinusX,
            "orientation from the drawing's real pixels: +X right, +Y up, a 90 deg Z turn maps +Y to -X (not mirrored)", failures);

        var player = new LessonPlayer(surface, new PauseSpeaker(60));
        await player.PlayAsync(Validated3d(SceneLesson(FourStrokeEngine.Id)));
        var engine = await surface.DescribeSceneAsync();
        var order = engine.Drawing?.Order ?? Array.Empty<string>();
        var head = order.ToList().IndexOf("m.cylinder-head");
        var valve = order.Select((k, i) => (k, i)).Where(x => x.k == "m.valve-intake").Select(x => x.i).ToList();
        failures = Check(head >= 0 && valve.Any(i => i < head) && valve.Any(i => i > head),
            $"depth order: the intake-valve stem is painted partly before and partly after the cylinder head it passes through (head at {head}, valve at {string.Join(",", valve)})", failures);

        const string Literal = "<b>rod</b>";
        await player.PlayAsync(Validated3d(SceneLesson(FourStrokeEngine.Id,
            $$"""{ "op": "label", "surface": "scene", "id": "l1", "target": "m.connecting-rod", "text": "{{Literal.Replace("\"", "\\\"", StringComparison.Ordinal)}}" }""")));
        var labelled = await surface.DescribeSceneAsync();
        failures = Check(labelled.Drawing is { LabelForeign: 0 } d && d.LabelTexts.SequenceEqual(new[] { Literal }),
            $"a label's text is drawn literally ('{string.Join("|", labelled.Drawing?.LabelTexts ?? Array.Empty<string>())}'), no markup inside labels ({labelled.Drawing?.LabelForeign})", failures);

        await player.PlayAsync(Validated3d(SceneLesson(FourStrokeEngine.Id, """{ "op": "highlight", "target": "m.connecting-rod" }""")));
        var lit = await surface.DescribeSceneAsync();
        var rodPieces = ModelLibrary.Find(FourStrokeEngine.Id)!.Part("connecting-rod").Primitives.Count;
        failures = Check(lit.Drawing is { Lit: > 0 } l && l.Lit >= rodPieces && lit.Highlights.SequenceEqual(new[] { "m.connecting-rod" }),
            $"highlight on m.connecting-rod outlines its solids ({lit.Drawing?.Lit} lit)", failures);

        await player.PlayAsync(Validated3d(SceneLesson(FourStrokeEngine.Id, """{ "op": "explode", "target": "m", "amount": 0.6 }""")));
        var apart = await surface.DescribeSceneAsync();
        failures = Check(apart.Explode.TryGetValue("m", out var amount) && Math.Abs(amount - 0.6) < 1e-9 && apart.Drawing!.Hash != engine.Drawing!.Hash && apart.DrawingSolids == engine.DrawingSolids,
            "explode 0.6 redraws the engine apart: same solids, a different picture", failures);

        var slow = new List<string>();
        foreach (var model in ModelLibrary.V1)
        {
            await player.PlayAsync(Validated3d(SceneLesson(model.Id)));
            var s = await surface.DescribeSceneAsync();
            Console.WriteLine($"          {model.Id}: {s.DrawingSolids} solids drawn in {s.DrawingBuildMs:F1} ms");
            if (s.DrawingSolids is not > 0 || s.DrawingBuildMs >= 100)
            {
                slow.Add(model.Id);
            }
        }

        failures = Check(slow.Count == 0, $"every library model draws in under 100 ms ({(slow.Count == 0 ? "all" : "not: " + string.Join(", ", slow))})", failures);
        var end = await surface.DescribeSceneAsync();
        failures = Check(end.WebGl == 0 && end.CspViolations == 0, $"no WebGL, and 0 CSP violations on the drawing page (got {end.CspViolations})", failures);
        failures = Check(end.ScriptElements == 4 && !surface.EverTookFocus, "page has exactly its 4 scripts; the window never became foreground", failures);
        return Summarize(failures, "SCENE SVG OK");
    });
}
