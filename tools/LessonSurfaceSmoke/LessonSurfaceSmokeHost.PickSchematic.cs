using HeyGanesh.LessonSurface;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.Tools.LessonSurfaceSmoke;

/// <summary>
/// LSU-18 (DEC-057): click a part of a lesson's own schematic, with the real mouse, on the real scene window. As in
/// LSU-15, the clicks go where the page says each part is, so a pass means a real click there comes back as exactly
/// that schematic part. Moves the cursor; it is put back after. Needs WebGL and an uncovered scene window.
/// </summary>
internal static partial class LessonSurfaceSmokeHost
{
    private static async Task<int> RunAssertPickSchematicAsync()
    {
        var failures = 0;
        PickNative.GetCursorPos(out var homeCursor);
        try
        {
            await using var surface = await CardSurface.CreateAsync();
            await new LessonPlayer(surface, new PauseSpeaker(60)).PlayAsync(SchematicLesson());
            // The last beat explodes the jet back together over 400 ms, after the beat itself has ended: points read
            // before the parts come to rest are where they were mid-way, not where a click will land (BUG-002).
            var still = await UntilStillAsync(surface);
            var start = await surface.DescribeSceneAsync();
            var visible = start.PickPoints.Where(p => p.Key.StartsWith("jet.", StringComparison.Ordinal)).ToList();
            // The lesson ends on the top view, where the wings, nozzle and roll posts sit under the fuselage: the page
            // offers a point only for a part whose centre is not covered. The lift fan is the part the lesson labels.
            failures = Check(still && visible.Count >= 3 && visible.Any(v => v.Key == "jet.lift-fan"),
                $"the F-35B schematic drawn and at rest ({still}): {visible.Count} of its 7 parts have a visible point to click, the lift fan among them ({string.Join(", ", visible.Select(v => v.Key))})", failures);
            if (visible.Count == 0)
            {
                return Summarize(failures + 1, "PICK SCHEMATIC OK");
            }

            // Every visible schematic part picks itself, and only itself, as (jet, f35b, part).
            var wrong = new List<string>();
            foreach (var (key, point) in visible)
            {
                var pick = await ClickWhilePickingAsync(surface, point, TimeSpan.FromSeconds(5));
                if (pick is null || pick.InstanceId != "jet" || pick.ModelId != "f35b" || $"jet.{pick.PartId}" != key)
                {
                    wrong.Add($"{key}->{(pick is null ? "none" : $"{pick.ModelId}/{pick.PartId}")}");
                }
            }

            failures = Check(wrong.Count == 0, $"a real click on each visible schematic part -> PartPick(jet, f35b, that part) ({(wrong.Count == 0 ? "none wrong" : string.Join(", ", wrong))})", failures);

            // Forged page messages are still refused with a schematic on show: a ghost instance, a part the
            // schematic does not have ('flux-capacitor' on 'jet'), and non-string ids. The wait stays open.
            var (firstKey, firstPoint) = visible[0];
            var waiting = surface.PickAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            await UntilPickingAsync(surface);
            var refused = await surface.ProbePickGuardAsync();
            var stillWaiting = !waiting.IsCompleted;
            Click(firstPoint);
            var real = await waiting;
            failures = Check(refused == 3 && stillWaiting && real is not null && $"jet.{real.PartId}" == firstKey,
                $"3 forged clicks refused ({refused}), including an invented part on the schematic; the next real click was taken ({real?.PartId})", failures);

            var after = await surface.DescribeSceneAsync();
            failures = Check(!after.Picking && after.Bounds == start.Bounds && !surface.EverTookFocus, "picking off again, the window did not move, never foreground", failures);
        }
        finally
        {
            PickNative.SetCursorPos(homeCursor.X, homeCursor.Y);
        }

        return Summarize(failures, "PICK SCHEMATIC OK");
    }
}
