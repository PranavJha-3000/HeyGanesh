using HeyGanesh.LessonSurface;
using HeyGanesh.PartLibrary;
using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.Tools.LessonSurfaceSmoke;

/// <summary>
/// LSU-17 (DEC-050): a lesson's own schematic is drawn by the real scene window, with WebGL and without it
/// (the isometric SVG), from the same model message as a library model: placed with its 7 parts, tagged
/// "schematic" and "not yet reviewed", labelled, and exploded back together by the end of the lesson. Needs
/// an interactive desktop; no key.
/// </summary>
internal static partial class LessonSurfaceSmokeHost
{
    private static LessonValidationResult SchematicLesson() =>
        new VisualPlanValidator().Validate(
            VisualPlanParser.Parse(Fixture("f35b-schematic.json")),
            new ValidationContext(VirtualDesktop()) { Scene3d = CardSurface.Scene3dCatalog, SchematicGeometry = Schematics.Check });

    private static async Task<int> RunAssertSceneSchematicAsync()
    {
        var failures = 0;
        var lesson = SchematicLesson();
        failures = Check(lesson.Outcome == LessonOutcome.Valid && lesson.Plan!.Schematics.Count == 1, $"f35b-schematic.json validates with the cards' catalogue and Schematics.Check ({lesson.Outcome})", failures);

        await using (var webgl = await CardSurface.CreateAsync())
        {
            failures = await DrawSchematicAsync(webgl, lesson, "WebGL", failures);
        }

        failures = await WithoutWebGlAsync("lsu17", async svg =>
        {
            var f = await DrawSchematicAsync(svg, lesson, "SVG", 0);
            var s = await svg.DescribeSceneAsync();
            // At least one solid per shape: the drawing splits a tall cylinder into pieces so depth order holds (DEC-034).
            f = Check(s.WebGl == 0 && s.DrawingSolids >= 10, $"SVG: no WebGL, and every one of the 10 schematic shapes drawn ({s.DrawingSolids} solids; tall cylinders are split for depth order)", f);
            return f;
        }) + failures;

        return Summarize(failures, "SCENE SCHEMATIC OK");
    }

    private static async Task<int> DrawSchematicAsync(CardSurface surface, LessonValidationResult lesson, string how, int failures)
    {
        await new LessonPlayer(surface, new PauseSpeaker(60)).PlayAsync(lesson);
        var s = await surface.DescribeSceneAsync();
        failures = Check(s.Instances.Count == 1 && s.Instances[0] is { Id: "jet", Model: "f35b", Parts: 7 },
            $"{how}: the schematic is placed as instance 'jet' of 'f35b' with its 7 parts ({string.Join(", ", s.Instances.Select(i => $"{i.Id}:{i.Model}/{i.Parts}"))})", failures);
        failures = Check(s.Tags.Contains("schematic") && s.Tags.Contains("not yet reviewed"),
            $"{how}: the scene is tagged schematic and not yet reviewed ({string.Join(", ", s.Tags)})", failures);
        failures = Check(s.Labels.Select(l => l.Text).SequenceEqual(new[] { "lift fan", "swivel nozzle", "roll posts" }) && s.Labels.All(l => l.Target.StartsWith("jet.", StringComparison.Ordinal)),
            $"{how}: the three labels point at the schematic's parts ({string.Join(", ", s.Labels.Select(l => $"{l.Target}={l.Text}"))})", failures);
        failures = Check(s.Explode.TryGetValue("jet", out var amount) && amount == 0 && s.CspViolations == 0 && !surface.EverTookFocus,
            $"{how}: exploded and put back together (explode {amount}); 0 CSP violations; the window never took focus", failures);
        return failures;
    }
}
