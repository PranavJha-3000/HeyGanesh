using HeyGanesh.TutorEngine;

namespace HeyGanesh.Tools.TutorSmoke;

/// <summary>The straight-through rows: recorded lesson and cancellation.</summary>
internal static partial class TutorSmokeHost
{
    private static TutorRequest ScreenRequest(string question) => new()
    {
        Question = question,
        Context = new TutorContext
        {
            Found = true,
            Description = "the Save button",
            BoxOnScreenX = 1240, BoxOnScreenY = 860, BoxOnScreenWidth = 96, BoxOnScreenHeight = 32,
            CenterOnScreenX = 1288, CenterOnScreenY = 876,
            ConfidenceTier = TutorConfidenceTier.High,
            SourceLeg = "ElementSelection",
            ModelId = "jev-1.13.0",
        },
    };

    /// <summary>One recorded lesson through the real service; the TutorResult is printed exactly as a host would report it.</summary>
    private static async Task<int> RunFakeExplainAsync(string question)
    {
        var client = new FakeTutorClient(FakeTutorClient.Mode.Canned,
            "Rebase rewrites your branch's history: it replays your commits one by one onto the new base, " +
            "producing new commit ids while keeping the changes.");
        var service = new TutorService(client);

        var result = await service.ExplainAsync(ScreenRequest(question));

        Console.WriteLine("=== Tutor result ===");
        Console.WriteLine($"  text     : \"{result.ExplanationText}\"");
        Console.WriteLine($"  model    : {result.ModelId}");
        Console.WriteLine($"  tokens   : in={result.InputTokens} out={result.OutputTokens}");
        Console.WriteLine($"  latency  : {result.Latency.TotalMilliseconds:F0} ms");
        Console.WriteLine("EXPLAIN OK (exit 0)");
        return 0;
    }

    /// <summary>
    /// The token contract: cancellation from the caller unwinds as OperationCanceledException (exit 13
    /// at the outer catch) and NEVER becomes explanation text — a half-taught lesson that reports
    /// success is the failure mode this row exists to catch.
    /// </summary>
    private static async Task<int> RunCancelAsync(int cancelMs)
    {
        var service = new TutorService(new FakeTutorClient(FakeTutorClient.Mode.Hang));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(cancelMs));

        Console.WriteLine($"starting a completion that never returns; cancelling after {cancelMs} ms...");
        var result = await service.ExplainAsync(ScreenRequest("why does my app flicker"), cts.Token);

        Console.WriteLine($"FAIL: the cancelled turn returned text instead of throwing: {result} (exit 1).");
        return 1;
    }
}