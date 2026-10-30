using HeyGanesh.LessonSurface;
using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.Tools.LessonSurfaceSmoke;

/// <summary>
/// LSU-3, the render row: what the validated plan says is what the cards show — every written
/// element by id with its exact text, the plan's arrows and highlights, and the app-computed plot.
/// Expectations are derived from the plan itself, so the row checks the renderer, not a transcript.
/// </summary>
internal static partial class LessonSurfaceSmokeHost
{
    private static async Task<int> RunAssertRenderAsync()
    {
        var failures = 0;
        await using var surface = await CardSurface.CreateAsync();
        var player = new LessonPlayer(surface, new PauseSpeaker(120));

        foreach (var name in GoldenFixtures)
        {
            var lesson = Validated(Plan(name));
            await player.PlayAsync(lesson);
            failures = await CheckCardsMatchPlanAsync(surface, lesson.Plan!, name, failures);
        }

        // Cross-card highlight: b6 highlights w-title, which lives on card-inputs, not on b6's cards.
        var cross = Validated(AppendOp(Plan("photosynthesis.json"), 5, new HighlightOp("w-title")));
        await player.PlayAsync(cross);
        var inputs = await surface.DescribeCardAsync("card-inputs");
        var process = await surface.DescribeCardAsync("card-process");
        failures = Check(
            inputs.HighlightedIds.Contains("w-title") && !process.HighlightedIds.Contains("w-title"),
            "a highlight of an element on another card lands on the card that holds it",
            failures);

        return Summarize(failures, "RENDER OK");
    }

    private static async Task<int> CheckCardsMatchPlanAsync(CardSurface surface, VisualPlan plan, string name, int failures)
    {
        foreach (var card in plan.Surfaces)
        {
            var snap = await surface.DescribeCardAsync(card.Id);
            var ops = plan.Beats.SelectMany(b => b.Ops).ToList();
            var owned = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var op in ops)
            {
                switch (op)
                {
                    case WriteOp w: owned[w.Id] = w.Surface; break;
                    case StepOp s: owned[s.Id] = s.Surface; break;
                    case IconOp i: owned[i.Id] = i.Surface; break;
                    case PlotOp p: owned[p.Id] = p.Surface; break;
                }
            }

            var expectedIds = owned.Where(kv => kv.Value == card.Id).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
            var texts = ops.Select(o => o switch
            {
                WriteOp w when w.Surface == card.Id => (w.Id, w.Text),
                StepOp s when s.Surface == card.Id => (s.Id, s.Text),
                _ => (Id: (string?)null, Text: (string?)null),
            }).Where(t => t.Id is not null).ToList();
            var arrows = ops.OfType<ArrowOp>().Count(a => a.Surface == card.Id);
            var highlighted = ops.OfType<HighlightOp>().Where(h => owned.TryGetValue(h.Target, out var s) && s == card.Id).Select(h => h.Target).ToHashSet(StringComparer.Ordinal);
            var plotPoints = ops.OfType<PlotOp>().Where(p => p.Surface == card.Id).Sum(p => PlotExpression.Sample(p).Count(pt => pt is not null));

            failures = Check(snap.Title == card.Title && snap.ElementIds.ToHashSet(StringComparer.Ordinal).SetEquals(expectedIds),
                $"{name} / {card.Id}: title '{card.Title}' and exactly the plan's {expectedIds.Count} element ids", failures);
            failures = Check(texts.All(t => snap.Texts.TryGetValue(t.Id!, out var shown) && shown == t.Text),
                $"{name} / {card.Id}: every write/step text shown verbatim ({texts.Count})", failures);
            failures = Check(snap.Arrows == arrows && snap.HighlightedIds.ToHashSet(StringComparer.Ordinal).SetEquals(highlighted),
                $"{name} / {card.Id}: {arrows} arrow(s), highlights on [{string.Join(", ", highlighted)}]", failures);
            failures = Check(snap.PlotPoints == plotPoints, $"{name} / {card.Id}: {plotPoints} app-computed plot points drawn", failures);
        }

        return failures;
    }
}
