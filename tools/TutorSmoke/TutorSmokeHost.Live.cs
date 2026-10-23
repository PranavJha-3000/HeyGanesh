using HeyGanesh.TutorEngine;

namespace HeyGanesh.Tools.TutorSmoke;

/// <summary>
/// TU-12: the live-gated row — the factory, the real network, the real provider. Offline-safe by
/// construction: without DEEPSEEK_API_KEY it performs a missing-key turn and exits 12 (the typed
/// auth fault proves the fail-fast; it is a pass-shaped outcome, not a failure), so CI and offline
/// machines run it too. With the key it asks one real question through the unchanged TutorService
/// and reports the lesson's shape — never claiming anything about injection resistance or answer
/// quality (TU-5's open live-only half stays open).
/// </summary>
/// <remarks>
/// Construction is <see cref="TutorClientFactory.CreateDeepSeek"/> with default options — the exact
/// call the App Shell flip will make. The key is reported by presence and length only; provider
/// faults print type and status, never bodies (the TU-6 no-echo discipline applies to live output
/// too, because a live provider CAN echo).
/// </remarks>
internal static partial class TutorSmokeHost
{
    private static async Task<int> RunLiveExplainAsync(string question)
    {
        var key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        Console.WriteLine($"  key       : {(string.IsNullOrWhiteSpace(key) ? "MISSING — offline shape, expect KEY ERROR + exit 12" : $"present (length {key.Length}, value suppressed)")}");

        using var client = TutorClientFactory.CreateDeepSeek();
        var service = new TutorService(client);

        try
        {
            var result = await service.ExplainAsync(new TutorRequest { Question = question });

            Console.WriteLine("=== live Tutor result (real provider) ===");
            Console.WriteLine($"  text      : \"{result.ExplanationText}\"");
            Console.WriteLine($"  model     : {result.ModelId}");
            Console.WriteLine($"  tokens    : in={result.InputTokens} out={result.OutputTokens}");
            Console.WriteLine($"  latency   : {result.Latency.TotalMilliseconds:F0} ms");

            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(result.ExplanationText)) problems.Add("empty explanation text");
            if (result.ExplanationText.Length > TutorOptions.Default.MaxExplanationChars) problems.Add("text over the explanation cap");
            if (!result.ModelId.Contains("deepseek", StringComparison.OrdinalIgnoreCase)) problems.Add($"model echo '{result.ModelId}' does not name deepseek");
            if (problems.Count > 0)
            {
                Console.WriteLine($"LIVE ANSWER FAILED SHAPE CHECK: {string.Join("; ", problems)} (exit 1)");
                return 1;
            }

            Console.WriteLine("LIVE EXPLAIN OK (exit 0)");
            Console.WriteLine("NOTE: a real lesson from a real model is not evidence of injection");
            Console.WriteLine("      resistance or pedagogical quality — those rows stay open.");
            return 0;
        }
        catch (TutorAuthenticationException ex)
        {
            // With no key set this is the EXPECTED offline shape: fail-fast, typed 12, no request
            // sent. The outer catch would land here too — this row states the intent out loud.
            Console.WriteLine($"KEY ERROR: {ex.Message}");
            Console.WriteLine("(expected offline outcome without DEEPSEEK_API_KEY — this row exits 12 by design)");
            return 12;
        }
        catch (TutorRateLimitException ex)
        {
            Console.WriteLine($"RATE LIMITED (real provider, live-gated): {ex.Message}");
            return 3;
        }
        catch (TutorClientException ex)
        {
            // Type and status only — never an inner chain, never a body dump.
            Console.WriteLine($"PROVIDER ERROR (live): {ex.GetType().Name} status={ex.StatusCode?.ToString() ?? "-"} (message length {ex.Message.Length}, value suppressed)");
            return 3;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("CANCELLED: the provider call was abandoned; no explanation exists (exit 13).");
            return 13;
        }
    }
}