using System.Diagnostics;
using HeyGanesh.LessonPlanner;
using HeyGanesh.PartLibrary;
using HeyGanesh.PartLibrary.Models;
using HeyGanesh.Providers.Core;
using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.Tools.LessonPlannerSmoke;

/// <summary>
/// LP-11 (DEC-041): the streaming planner releases each beat once header + beats-so-far validate, and a
/// released beat is exactly the beat the finished plan contains — for every golden lesson, however the
/// text is chunked. Plus the edges: braces inside strings, a dropped stream, repair before anything was
/// shown, a fault before the first beat, no streaming capability, cancellation, and a timing simulation.
/// </summary>
internal static partial class LessonPlannerSmokeHost
{
    private const ProviderCapability Streaming = ProviderCapability.Text | ProviderCapability.Streaming;

    private static async Task<int> RunAssertStreamAsync()
    {
        var failures = 0;
        var screen = new ScreenGrounding("the Save button", Target, null, 0, 0);
        (string Name, string Json, PlanRequest Request)[] goldens =
        {
            ("chain-rule", Fixture("chain-rule.json"), Request("why is the derivative of ln(3x^2-1) equal to 6x/(3x^2-1)?", screen)),
            ("photosynthesis", Fixture("photosynthesis.json"), Request("how does photosynthesis work?", screen)),
            ("tensor-2d", Fixture("tensor-2d.json"), Request("what is a tensor?", screen)),
            ("engine-3d", Fixture("engine-3d.json").Replace("\"engine-test\"", $"\"{FourStrokeEngine.Id}\"", StringComparison.Ordinal),
                Request("how does a four-stroke engine work?", screen) with { Scene3d = ModelLibrary.ToCatalog() }),
        };

        // 1. Every golden lesson, at every chunk size: all beats released, each identical to the finished
        //    plan's beat, and the outcome the same as PlanAsync's on the same text.
        foreach (var (name, json, request) in goldens)
        {
            var whole = await new VisualPlanner(new FakeLlmClient(TextOnly, json)).PlanAsync(request);
            var expected = whole.Lesson.Plan!.Beats.Select(StreamedBeat.Fingerprint).ToArray();
            var allSizes = true;
            var detail = "";
            foreach (var size in new[] { 1, 2, 7, 50 })
            {
                var seen = new List<StreamedBeat>();
                var result = await new VisualPlanner(new FakeLlmClient(Streaming, json) { ChunkChars = size }).PlanStreamingAsync(request, seen.Add);
                var ok = result.Lesson.Outcome == whole.Lesson.Outcome
                    && result.ReleasedBeats == expected.Length
                    && seen.Select(b => StreamedBeat.Fingerprint(b.Beat)).SequenceEqual(expected)
                    && seen.Select(b => b.Index).SequenceEqual(Enumerable.Range(0, expected.Length))
                    && result.StreamNote is null && result.Attempts == 1;
                allSizes &= ok;
                detail += $"{size}:{seen.Count}/{expected.Length}{(ok ? "" : "!")} ";
            }

            failures = Check(allSizes, $"{name} ({whole.Lesson.Outcome}): every beat released in order, identical to the finished plan's, at chunk sizes 1/2/7/50 [{detail.Trim()}]", failures);
        }

        // 2. Braces, brackets, quotes and backslashes inside strings never cut a beat.
        var tricky = """
            { "schema": "heyganesh.visualplan/1", "lessonId": "braces", "title": "a {tricky} [title]", "mode": "explain",
              "surfaces": [ { "id": "card-main", "kind": "card", "title": "x" } ],
              "beats": [
                { "id": "b1", "say": "A set is written {1, 2} and a list [3, 4].", "ops": [ { "op": "write", "surface": "card-main", "id": "w1", "text": "say \"hi\" \\ then }", "style": "body" } ] },
                { "id": "b2", "say": "Done: \"] }\" { [", "ops": [] }
              ] }
            """;
        var trickySeen = new List<StreamedBeat>();
        await new VisualPlanner(new FakeLlmClient(Streaming, tricky) { ChunkChars = 1 }).PlanStreamingAsync(Request("sets"), trickySeen.Add);
        failures = Check(trickySeen.Count == 2 && trickySeen[0].Beat.Say == "A set is written {1, 2} and a list [3, 4]." && trickySeen[1].Beat.Say == "Done: \"] }\" { [",
            $"braces, brackets and escaped quotes inside strings do not cut a beat ({trickySeen.Count} released)", failures);

        var fenced = new PlanStreamScanner();
        fenced.Append("Here you go:\n```json\n" + goldens[0].Json + "\n```");
        failures = Check(fenced.Beats.Count == 5 && fenced.BeatsClosed && fenced.Header!.Contains("\"surfaces\"", StringComparison.Ordinal),
            "prose and a code fence before the object are skipped; the header holds the surfaces", failures);

        // 3. The stream drops after the second beat: two beats out, the lesson stops there, no repair.
        var cut = goldens[0].Json.IndexOf("\"id\": \"b3\"", StringComparison.Ordinal);
        var dropped = new List<StreamedBeat>();
        var dropClient = new FakeLlmClient(Streaming, goldens[0].Json) { ChunkChars = 5, FailAfterChars = cut };
        var dropResult = await new VisualPlanner(dropClient).PlanStreamingAsync(goldens[0].Request, dropped.Add);
        failures = Check(dropped.Count == 2 && dropResult.ReleasedBeats == 2 && dropResult.Lesson.Plan!.Beats.Count == 2
                         && dropResult.StreamNote!.Contains("after 2 beat", StringComparison.Ordinal) && dropClient.Recorded.Count == 1,
            $"a stream that drops after beat 2 keeps those 2 and stops, with no repair call ({dropResult.StreamNote})", failures);

        // 4. Nothing valid while streaming (a wrong schema): nothing released, then the usual repair.
        var wrong = goldens[0].Json.Replace("heyganesh.visualplan/1", "heyganesh.visualplan/9", StringComparison.Ordinal);
        var repairSeen = new List<StreamedBeat>();
        var repairClient = new FakeLlmClient(Streaming, wrong, goldens[0].Json);
        var repaired = await new VisualPlanner(repairClient).PlanStreamingAsync(goldens[0].Request, repairSeen.Add);
        failures = Check(repairSeen.Count == 0 && repaired.Attempts == 2 && repaired.Lesson.Outcome == LessonOutcome.Valid
                         && repairClient.StreamCalls == 1 && repairClient.Recorded.Count == 2 && repaired.StreamNote is not null,
            $"an answer with nothing valid releases nothing, then gets one repair call like PlanAsync (attempts={repaired.Attempts}, {repaired.Lesson.Outcome})", failures);

        // 5. A fault before the first beat propagates, as in PlanAsync.
        var propagated = false;
        try
        {
            await new VisualPlanner(new FakeLlmClient(Streaming, goldens[0].Json) { FailAfterChars = 10 }).PlanStreamingAsync(goldens[0].Request);
        }
        catch (ProviderException)
        {
            propagated = true;
        }

        failures = Check(propagated, "a provider fault before any beat propagates (nothing was taught)", failures);

        // 6. A client that cannot stream gets exactly PlanAsync.
        var plain = new FakeLlmClient(TextOnly, goldens[0].Json);
        var plainSeen = new List<StreamedBeat>();
        var plainResult = await new VisualPlanner(plain).PlanStreamingAsync(goldens[0].Request, plainSeen.Add);
        failures = Check(plain.StreamCalls == 0 && plain.Recorded.Count == 1 && plainSeen.Count == 0 && plainResult.ReleasedBeats == 0
                         && plainResult.StreamNote is null && plainResult.Lesson.Outcome == LessonOutcome.Valid,
            "without the Streaming capability the planner calls GenerateAsync and releases nothing early", failures);

        // 7. Cancellation mid-stream propagates.
        using var cts = new CancellationTokenSource();
        var cancelled = false;
        try
        {
            await new VisualPlanner(new FakeLlmClient(Streaming, goldens[0].Json) { ChunkChars = 5 })
                .PlanStreamingAsync(goldens[0].Request, b => { if (b.Index == 0) { cts.Cancel(); } }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        failures = Check(cancelled, "cancelling after the first beat throws OperationCanceledException", failures);

        // 8. Timing, SIMULATED: DeepSeek V4.1 Flash's published figures (about 1.1 s to the first token,
        //    206 tokens/s ≈ 824 characters/s at ~4 characters a token). Not a measurement of any model.
        var timed = await new VisualPlanner(new FakeLlmClient(Streaming, goldens[1].Json)
        {
            ChunkChars = 16,
            FirstTokenDelay = TimeSpan.FromMilliseconds(1100),
            CharsPerSecond = 824,
        }).PlanStreamingAsync(goldens[1].Request);
        var first = timed.FirstBeatLatency?.TotalMilliseconds ?? -1;
        var all = timed.Latency.TotalMilliseconds;
        Console.WriteLine($"  SIMULATED: first beat at {first:F0} ms, whole lesson at {all:F0} ms ({goldens[1].Json.Length} characters, photosynthesis)");
        failures = Check(first > 0 && first < all * 0.6, "in the simulation the first beat is released well before the whole lesson has arrived", failures);

        return Summarize(failures, "STREAM OK");
    }
}
