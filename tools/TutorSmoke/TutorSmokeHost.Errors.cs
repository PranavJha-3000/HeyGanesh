using HeyGanesh.TutorEngine;

namespace HeyGanesh.Tools.TutorSmoke;

/// <summary>The error-family rows and the prompt-parity row.</summary>
internal static partial class TutorSmokeHost
{
    private static int Check(bool ok, string label, int failures)
    {
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {label}");
        return ok ? failures : failures + 1;
    }

    private static int Verdict(int failures, string okLabel)
    {
        Console.WriteLine(failures == 0 ? $"{okLabel} (exit 0)" : $"FAILED: {failures} check(s) (exit 1)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// The three-way error discrimination at the seam: typed client faults pass through UNRE-WRAPPED
    /// (so exit 3 stays 3 and a status survives), an empty completion is provider failure (never a
    /// blank lesson), and a malformed leg fault is translated into a failure that names the leg.
    /// </summary>
    private static async Task<int> RunAssertClientErrorAsync()
    {
        var failures = 0;
        var question = ScreenRequest("what is a deadlock");

        try
        {
            await new TutorService(new FakeTutorClient(FakeTutorClient.Mode.ClientError500)).ExplainAsync(question);
            failures = Check(false, "HTTP-500 fault was expected to throw", failures);
        }
        catch (TutorClientException ex) when (ex is not TutorAuthenticationException and not TutorRateLimitException)
        {
            failures = Check(ex.StatusCode == 500, "typed client fault passed through unre-wrapped, status 500 intact", failures);
            failures = Check(ex.InnerException is null, "no inner-exception chain was attached to the pass-through", failures);
        }

        try
        {
            await new TutorService(new FakeTutorClient(FakeTutorClient.Mode.EmptyText)).ExplainAsync(question);
            failures = Check(false, "empty completion was expected to throw", failures);
        }
        catch (TutorClientException ex)
        {
            failures = Check(ex.Message.Contains("empty completion"), "empty completion mapped to provider failure, never a blank lesson", failures);
        }

        try
        {
            await new TutorService(new FakeTutorClient(FakeTutorClient.Mode.Malformed)).ExplainAsync(question);
            failures = Check(false, "malformed leg fault was expected to throw", failures);
        }
        catch (TutorClientException ex)
        {
            failures = Check(ex.Message.Contains("tutor"), "malformed completion translated into a failure that names the leg: tutor", failures);
            failures = Check(ex.InnerException is null, "translated failure carries no inner chain (no second message channel)", failures);
        }

        return Verdict(failures, "CLIENT ERROR CONTRACT OK");
    }

    /// <summary>
    /// The auth end of TU-6: the seam's auth fault stays typed (this row EXITS 12 by design — the exit
    /// code is the proof), the caller receives the very instance the seam threw, and because the fake
    /// simulates a provider echoing a credential into its own message, the host prints type and length
    /// only — the sentinel never reaches the console.
    /// </summary>
    private static async Task<int> RunAssertAuthErrorAsync()
    {
        var client = new FakeTutorClient(FakeTutorClient.Mode.AuthEchoSecret);
        try
        {
            await new TutorService(client).ExplainAsync(ScreenRequest("explain quicksort"));
            Console.WriteLine("FAIL: auth fault was expected to throw (exit 1).");
            return 1;
        }
        catch (TutorAuthenticationException ex)
        {
            var failures = 0;
            failures = Check(ex.GetType() == typeof(TutorAuthenticationException),
                "auth fault kept its exact type — never re-wrapped into a generic client fault (12 stays 12)", failures);
            failures = Check(ReferenceEquals(ex, client.LastThrown),
                "the instance the seam threw IS the instance the host catches — the service added no copy", failures);
            failures = Check(ex.InnerException is null,
                $"no inner chain attached (message length {ex.Message.Length}, value suppressed so a provider-echoed credential never prints)", failures);
            Console.WriteLine(failures == 0
                ? "KEY ERROR verified — this row exits 12 on purpose"
                : $"FAILED: {failures} check(s) (exit 1)");
            return failures == 0 ? 12 : 1;
        }
    }

    /// <summary>
    /// Determinism proof (TU-10): two fresh services, identical text-only requests — byte-identical
    /// prompts. A live provider will therefore see exactly what fakes and recordings were verified on.
    /// </summary>
    private static async Task<int> RunAssertParityAsync()
    {
        var failures = 0;
        var request = ScreenRequest("why does latency spike at the end of a chunk");
        var first = new FakeTutorClient(FakeTutorClient.Mode.GenericLesson);
        var second = new FakeTutorClient(FakeTutorClient.Mode.GenericLesson);
        await new TutorService(first).ExplainAsync(request);
        await new TutorService(second).ExplainAsync(request);
        var a = first.Recorded[0];
        var b = second.Recorded[0];

        failures = Check(string.Equals(a.SystemText, b.SystemText, StringComparison.Ordinal), "system text byte-identical across two fresh services", failures);
        failures = Check(string.Equals(a.UserText, b.UserText, StringComparison.Ordinal), "user text byte-identical across two fresh services", failures);
        failures = Check(a.MaxOutputTokens == b.MaxOutputTokens && a.Visual is null && b.Visual is null, "budget identical; no visual on either leg", failures);
        return Verdict(failures, "PROMPT PARITY OK");
    }
}