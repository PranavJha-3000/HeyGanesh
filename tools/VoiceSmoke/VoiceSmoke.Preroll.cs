using System.Diagnostics;
using HeyGanesh.Voice;

namespace HeyGanesh.Tools.VoiceSmoke;

/// <summary>
/// VO-11 (DEC-040): what the user says between the press and the Gateway's <c>session.ready</c> must
/// reach the Gateway. Runs the real <see cref="QwenGatewayClient"/> against <see cref="GatewayStub"/> on
/// loopback, with a <see cref="NumberedMicrophone"/> whose frame numbers are time since the press.
/// </summary>
internal static partial class VoiceSmokeHost
{
    private const int StubReadyDelayMs = 600;

    private static async Task<int> AssertPrerollAsync()
    {
        Console.WriteLine("VO-11: pre-roll against a loopback Gateway stub (ready after 600 ms; no microphone, no network)");
        var failures = 0;

        // 1. Speech during the connect wait is kept, in order, from the press.
        await using (var stub = new GatewayStub(TimeSpan.FromMilliseconds(StubReadyDelayMs), framesBeforeFinal: 12))
        {
            try
            {
                stub.Start();
            }
            catch (System.Net.HttpListenerException ex)
            {
                Console.WriteLine($"  FAIL  the loopback stub could not listen on localhost:{stub.Port} ({ex.Message}); this row cannot run here");
                return 1;
            }

            var pressedAt = DateTimeOffset.UtcNow;
            var mic = new NumberedMicrophone(pressedAt);
            using var client = new QwenGatewayClient(Options(stub.Url), VoiceOptions.Default, mic);
            var clock = Stopwatch.StartNew();
            var verdict = await client.ListenOnceAsync(CancellationToken.None);
            clock.Stop();

            var frames = stub.Frames;
            Console.WriteLine($"  verdict: {verdict}; {frames.Count} frames reached the stub, first #{(frames.Count > 0 ? frames[0] : -1)}, {clock.ElapsedMilliseconds} ms");
            failures += Check("the committed transcript is the verdict", verdict.FinalText == GatewayStub.FinalText, verdict.ToString());
            failures += Check("the first frame the Gateway gets is from the press (#0 or #1), not after the connect wait",
                frames.Count > 0 && frames[0] <= 1, $"first frame #{(frames.Count > 0 ? frames[0] : -1)}; #{StubReadyDelayMs / 100} would mean {StubReadyDelayMs} ms of speech lost");
            failures += Check("frames arrive in order with no gap and no duplicate",
                frames.Count > 0 && frames.Zip(frames.Skip(1)).All(p => p.Second == p.First + 1), string.Join(",", frames.Take(16)));
            failures += Check("the microphone was started once and stopped once", mic.Starts == 1 && mic.Stops == 1, $"starts={mic.Starts} stops={mic.Stops}");
            failures += Check("ConnectDuration measures the wait for session.ready",
                verdict.ConnectDuration is { } c && c.TotalMilliseconds >= StubReadyDelayMs, $"{verdict.ConnectDuration?.TotalMilliseconds:F0} ms");
            failures += Check("BufferedFrames counts the speech queued meanwhile (about one per 100 ms of the wait)",
                verdict.BufferedFrames >= (StubReadyDelayMs / 100) - 2, $"{verdict.BufferedFrames} frames");
        }

        // 1b. The wire-version check runs against upstream's field (BUG-001): a major-8 Gateway is refused.
        await using (var stub = new GatewayStub(TimeSpan.FromMilliseconds(50), framesBeforeFinal: 3, wireVersion: "8.0.0"))
        {
            stub.Start();
            var mic = new NumberedMicrophone(DateTimeOffset.UtcNow);
            using var client = new QwenGatewayClient(Options(stub.Url), VoiceOptions.Default, mic);
            var verdict = await client.ListenOnceAsync(CancellationToken.None);
            failures += Check("a Gateway stating protocol_version 8.0.0 is refused, not listened to",
                verdict.Failed && verdict.FailureReason!.Contains("8.0.0", StringComparison.Ordinal) && stub.Frames.Count == 0,
                $"{verdict.FailureReason ?? verdict.ToString()}; frames sent={stub.Frames.Count}");
            failures += Check("the version the client recorded is the stated one", client.GatewayProtocolVersion == "8.0.0", client.GatewayProtocolVersion ?? "null");
        }

        // 2. A Gateway that cannot be reached: the microphone is released and nothing is sent anywhere.
        {
            var mic = new NumberedMicrophone(DateTimeOffset.UtcNow);
            using var client = new QwenGatewayClient(
                Options($"ws://localhost:{GatewayStub.FreePort()}/api/realtime"),
                new VoiceOptions { MaxReconnectAttempts = 1 },
                mic);
            var verdict = await client.ListenOnceAsync(CancellationToken.None);
            failures += Check("an unreachable Gateway is a failure verdict", verdict.Failed, verdict.FailureReason ?? "none");
            failures += Check("the microphone is not left running after a failed listen", mic.Starts == mic.Stops, $"starts={mic.Starts} stops={mic.Stops}");
            failures += Check("a failed listen has no ConnectDuration", verdict.ConnectDuration is null && verdict.BufferedFrames == 0,
                $"{verdict.ConnectDuration?.TotalMilliseconds:F0} ms, {verdict.BufferedFrames} frames");
        }

        // 3. No endpoint configured: the microphone is never opened at all.
        {
            var mic = new NumberedMicrophone(DateTimeOffset.UtcNow);
            using var client = new QwenGatewayClient(
                new QwenGatewayOptions { GatewayUrl = null, GatewayUrlEnvVar = "HEYGANESH_VO11_NO_SUCH_VARIABLE" },
                VoiceOptions.Default,
                mic);
            var verdict = await client.ListenOnceAsync(CancellationToken.None);
            failures += Check("no Gateway URL is a failure verdict naming the variable",
                verdict.Failed && verdict.FailureReason!.Contains("HEYGANESH_VO11_NO_SUCH_VARIABLE", StringComparison.Ordinal), verdict.FailureReason ?? "none");
            failures += Check("with no Gateway URL the microphone is never opened", mic.Starts == 0, $"starts={mic.Starts}");
        }

        Console.WriteLine(failures == 0 ? "PREROLL OK (exit 0)" : $"FAILED: {failures} check(s) (exit 1)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>The stub reports no capabilities, so the health checks are off for these rows.</summary>
    private static QwenGatewayOptions Options(string url) => new()
    {
        GatewayUrl = url,
        RequireConversationClientCapability = false,
        RequireBackendDisabled = false,
    };
}
