using HeyGanesh.ContextOrchestrator;
using HeyGanesh.ElementSelection;
using HeyGanesh.Vision;
using HeyGanesh.Abstractions;

namespace HeyGanesh.Tools.ContextOrchestratorSmoke;

// Runner + result printing for the Context Orchestrator smoke host. The exit code is part of the
// contract: 0 = a verdict was produced (including an honest not-found), 1 = the verdict is a failure,
// 2 = the composition assertion for the selected mode did not hold, 3 = provider/HTTP error propagated,
// 12 = missing API key (fail fast, key never echoed), 13 = cancellation propagated.

internal static partial class ContextOrchestratorSmokeHost
{
    private static async Task<int> RunSingleAsync(
        ContextOrchestratorService orchestrator,
        HostOptions options,
        CountingVisionClient visionClient)
    {
        using var cts = new CancellationTokenSource();
        if (options.CancelMs > 0)
        {
            cts.CancelAfter(options.CancelMs);
            Console.WriteLine($"cancelling the session token after {options.CancelMs} ms...");
        }

        GuidanceResult result;
        try
        {
            result = await orchestrator.ResolveAsync(options.Question, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("CANCELLED: the caller's token aborted the session; no verdict was produced.");
            Console.WriteLine($"  phase       : {orchestrator.Phase} (cancellation unwinds to Idle, never a half-built verdict)");
            Console.WriteLine($"  vision calls: {visionClient.Count}");
            return 13;
        }
        catch (JevAuthenticationException ex)
        {
            Console.WriteLine($"KEY ERROR (Element Selection): {ex.Message}");
            return 12;
        }
        catch (JevClientException ex)
        {
            Console.WriteLine($"API ERROR (Element Selection, propagated not swallowed): {ex.Message}");
            return 3;
        }
        catch (VisionAuthenticationException ex)
        {
            Console.WriteLine($"KEY ERROR (Vision): {ex.Message}");
            return 12;
        }
        catch (VisionClientException ex)
        {
            Console.WriteLine($"API ERROR (Vision, propagated not swallowed): {ex.Message}");
            return 3;
        }

        PrintResult(result, orchestrator.Phase, visionClient.Count);

        if (options.Live)
        {
            return result.Failed ? 1 : 0;
        }

        var expectation = CheckExpectation(options.FakeJevMode!.Value, options.VisionNotFound, result, visionClient.Count, options.RetainCaptures);
        Console.WriteLine($"expectation  : {expectation}");
        return expectation.StartsWith("PASS", StringComparison.Ordinal) ? 0 : 2;
    }

    private static async Task<int> RunBurstAsync(
        ContextOrchestratorService orchestrator,
        HostOptions options,
        CountingVisionClient visionClient)
    {
        Console.WriteLine("burst mode: a second trigger arrives while the first session is still in flight.");
        Console.WriteLine("It must be refused, not queued and not run concurrently.");
        Console.WriteLine();

        var first = orchestrator.ResolveAsync(options.Question);

        var refused = false;
        var refusal = "(no exception -- guard failed)";
        try
        {
            await orchestrator.ResolveAsync(options.Question);
        }
        catch (InvalidOperationException ex)
        {
            refused = true;
            refusal = ex.Message;
        }

        var result = await first;

        Console.WriteLine($"  session 1     : {result}");
        Console.WriteLine($"  second trigger: {(refused ? "REFUSED" : "ACCEPTED")} -- {refusal}");
        Console.WriteLine($"  phase         : {orchestrator.Phase}");
        Console.WriteLine($"  session active: {orchestrator.IsSessionActive}");
        Console.WriteLine($"  vision calls  : {visionClient.Count} (one session in flight means one capture, one set of calls)");
        Console.WriteLine();
        Console.WriteLine(refused
            ? "PASS one hotkey trigger maps to one session"
            : "FAIL two sessions ran concurrently");
        return refused ? 0 : 2;
    }

    private static void PrintResult(GuidanceResult r, OrchestratorPhase phase, int visionCalls)
    {
        Console.WriteLine("=== Guidance result ===");
        Console.WriteLine($"  source      : {r.Source}  (ElementSelection = primary leg, Vision = fallback leg)");
        if (r.PrimaryFailureReason is not null)
        {
            Console.WriteLine($"  primary why : {r.PrimaryFailureReason}");
        }

        if (r.FailureReason is not null)
        {
            Console.WriteLine($"  FAILURE     : {r.FailureReason}");
        }

        Console.WriteLine($"  found       : {r.Found}");
        if (r.Choice is not null)
        {
            Console.WriteLine($"  choice      : {r.Choice}");
        }

        if (r.Found)
        {
            Console.WriteLine($"  description : {r.Description}");
            Console.WriteLine($"  boxOnScreen : {r.BoxOnScreen} (global physical px)");
            Console.WriteLine($"  center      : {r.CenterOnScreen} (global physical px)");
        }

        Console.WriteLine($"  confidence  : {r.Confidence}");
        Console.WriteLine($"  signals     : jev={r.JevConfidence} winner={r.WinnerProbability} none={r.NoneProbability} " +
                          $"plausible={r.PlausibleProbability} | vision raw={r.RawConfidence} consistency={r.Consistency}");
        Console.WriteLine($"  model       : {r.ModelId}");
        Console.WriteLine($"  frames      : {r.Captures.Count} retained" +
                          (r.Captures.Count > 0 ? $" (first = {r.Captures[0].Monitor})" : string.Empty));
        Console.WriteLine($"  timings     : latency={r.Latency.TotalMilliseconds:F0}ms inputTok={r.InputTokens} outputTok={r.OutputTokens}");
        Console.WriteLine($"  phase       : {phase}");
        Console.WriteLine($"  vision calls: {visionCalls}");
        Console.WriteLine();
        Console.WriteLine($"  summary     : {r}");
    }

    /// <summary>
    /// The load-bearing assertions, one per offline mode, so the exit code reflects the composition
    /// contract rather than merely "nothing threw".
    /// </summary>
    private static string CheckExpectation(
        FakeJevMode mode,
        bool visionNotFound,
        GuidanceResult r,
        int visionCalls,
        bool retainCaptures)
    {
        return mode switch
        {
            // Checked first: a retention leak would invalidate the frames line of every other row.
            _ when !retainCaptures && r.Captures.Count != 0
                => $"FAIL --no-retain-captures: {r.Captures.Count} frame(s) were still attached to the verdict",
            // Checked first: a not-found fallback is only meaningful once the primary leg has failed.
            // Vision reports a model-side not-found as a Failed result (Reconcile sets the failure
            // reason), so the orchestrator carries that verbatim: Failed, Low, no box. Asserting
            // "successful not-found" here would be asserting a contract the Vision module does not have.
            _ when visionNotFound && r.Source == GuidanceSource.Vision && !r.Found && r.Failed &&
                   r.BoxOnScreen is null && visionCalls == 1
                => "PASS fallback not-found carried verbatim (Vision reports it as a failure; no box introduced)",
            _ when visionNotFound
                => $"FAIL expected one Vision not-found verdict with no box; got source={r.Source} " +
                   $"found={r.Found} failed={r.Failed} box={r.BoxOnScreen?.ToString() ?? "none"} visionCalls={visionCalls}",

            FakeJevMode.TopCandidate when r.Found && r.Source == GuidanceSource.ElementSelection && visionCalls == 0
                => "PASS primary leg answered and Vision was never called",
            FakeJevMode.TopCandidate
                => $"FAIL expected a primary verdict with 0 Vision calls; got source={r.Source} found={r.Found} " +
                   $"visionCalls={visionCalls} failure={r.FailureReason ?? "none"}",

            FakeJevMode.None when !r.Found && !r.Failed && r.Source == GuidanceSource.ElementSelection &&
                                 r.Choice == "none" && visionCalls == 0
                => "PASS honest no-match stayed on the primary leg (Vision not called)",
            FakeJevMode.None
                => $"FAIL expected 'none' to be a successful primary no-match with 0 Vision calls; got " +
                   $"source={r.Source} found={r.Found} failed={r.Failed} choice={r.Choice} visionCalls={visionCalls}",

            FakeJevMode.Unmapped when r.Source == GuidanceSource.Vision && r.Found && visionCalls == 1 &&
                                      r.PrimaryFailureReason is not null
                => "PASS primary leg failed and Vision ran exactly once",
            FakeJevMode.Unmapped
                => $"FAIL expected exactly one Vision fallback after a primary failure; got source={r.Source} " +
                   $"found={r.Found} visionCalls={visionCalls} primaryFailure={r.PrimaryFailureReason ?? "none"}",

            _ => "SKIPPED no expectation is defined for this mode",
        };
    }

}
