using HeyGanesh.Voice;

namespace HeyGanesh.Tools.VoiceSmoke;

/// <summary>The fake-seam rows: listen verdicts, cancellation, burst refusal, and the real-speaker speak path.</summary>
internal static partial class VoiceSmokeHost
{
    /// <summary>Runs one listen through a fake listener and prints the verdict exactly as the host reports it.</summary>
    private static async Task<int> RunListenAsync(IVoiceListener listener)
    {
        using var service = new VoiceService(listener: listener);
        var result = await service.ListenOnceAsync();

        PrintResult(result);

        // A failure is a FAILED row (exit 1): silence and speech both pass, because both are verdicts.
        return result.Failed ? 1 : 0;
    }

    /// <summary>
    /// Cancels a hanging listen. The contract under test: the token propagates as OperationCanceledException
    /// (exit 13) and is NEVER re-shaped into a verdict — a fabricated "no speech" here would tell the host
    /// the user was quiet when actually we gave up.
    /// </summary>
    private static async Task<int> RunCancelAsync(int cancelMs)
    {
        using var service = new VoiceService(listener: new FakeListener(FakeListener.Mode.Hang));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(cancelMs));

        Console.WriteLine($"starting a listen that never completes; cancelling after {cancelMs} ms...");
        await service.ListenOnceAsync(cts.Token);

        Console.WriteLine("FAIL: the cancelled listen returned a verdict instead of throwing (exit 1).");
        return 1;
    }

    /// <summary>
    /// The single-session rule: while one listen is in flight a second is refused, not queued. Queuing
    /// would mean a stale question answering itself much later, against a screen that has changed.
    /// </summary>
    private static async Task<int> RunBurstAsync()
    {
        using var service = new VoiceService(
            listener: new FakeListener(FakeListener.Mode.Transcript, delay: TimeSpan.FromMilliseconds(400)));

        var first = service.ListenOnceAsync();
        Console.WriteLine("first listen started (400 ms fake delay); attempting a second immediately...");

        try
        {
            var second = service.ListenOnceAsync();
            Console.WriteLine("FAIL: the second listen was accepted instead of refused (exit 1).");
            _ = await second;
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"second ListenOnceAsync REFUSED (not queued): {ex.Message}");
        }

        var verdict = await first;
        Console.WriteLine($"first listen completed normally: {verdict}");
        Console.WriteLine("BURST OK (exit 0)");
        return 0;
    }

    /// <summary>
    /// The out-leg end to end: fake TTS (real PCM) through the REAL winmm speaker, with SpeakResult's
    /// duration computed from bytes that actually moved. Needs a working output device — an absent one is
    /// reported honestly as a provider/device error (exit 3), not as a pass.
    /// </summary>
    private static async Task<int> RunSpeakAsync(string text)
    {
        using var service = new VoiceService(
            listener: new FakeListener(FakeListener.Mode.NoSpeech),
            tts: new FakeTts(),
            output: new WinMmAudioOutput());

        Console.WriteLine($"speaking {text.Length} chars through fake TTS -> winmm speaker...");
        var result = await service.SpeakAsync(new SpeakRequest { Text = text });

        Console.WriteLine($"=== Speak result ===\n  {result}");
        Console.WriteLine("SPEAK OK (exit 0)");
        return 0;
    }

    private static void PrintResult(UtteranceResult r)
    {
        Console.WriteLine("=== Voice result ===");
        Console.WriteLine($"  frontend   : {r.FrontendId}");
        if (r.Failed)
        {
            Console.WriteLine($"  FAILURE    : {r.FailureReason}");
        }

        Console.WriteLine($"  no speech  : {r.IsNoSpeech}");
        Console.WriteLine($"  text       : {(r.FinalText is null ? "n/a" : $"\"{r.FinalText}\"")}");
        Console.WriteLine($"  latency    : {r.ListenLatency.TotalMilliseconds:F0} ms");
        Console.WriteLine($"  suppressed : {r.SuppressedAssistantEvents} assistant events neutralised");
    }
}
