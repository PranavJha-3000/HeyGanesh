using HeyGanesh.Voice;

namespace HeyGanesh.Tools.VoiceSmoke;

/// <summary>
/// Verification host for the Voice module. Offline rows use fake seams and recorded frames so every
/// verdict, policy and contract is provable without a Gateway, a key, or a microphone; live rows are
/// human-gated. Exit codes mirror the shell contract: 0 ok, 1 failed verdict, 2 usage, 3 provider error,
/// 12 key error, 13 cancelled.
/// </summary>
internal static partial class VoiceSmokeHost
{
    private enum Mode
    {
        Help,
        FakeTranscript,
        FakeNoSpeech,
        FakeAuthError,
        Cancel,
        Burst,
        SpeakFake,
        AssertGcpEvents,
        AssertTutorAuthority,
        AssertNoBackend,
        AssertFishFormat,
    }

    public static async Task<int> RunAsync(string[] args)
    {
        var options = ParseArgs(args);
        if (options.Mode == Mode.Help)
        {
            PrintUsage();
            return 0;
        }

        Console.WriteLine("Voice verification host (offline fakes + protocol assertions)");
        var fishKey = Environment.GetEnvironmentVariable("FISH_AUDIO_API_KEY");
        var gatewayUrl = Environment.GetEnvironmentVariable("HEYGANESH_QWEN_GATEWAY_URL");
        // Presence/length only — the same rule the shell applies (SH-4): a key never reaches a console.
        Console.WriteLine($"  fish key  : {(string.IsNullOrEmpty(fishKey) ? "MISSING -- set FISH_AUDIO_API_KEY" : $"present ({fishKey.Length} chars, value suppressed)")}");
        Console.WriteLine($"  gateway   : {(string.IsNullOrEmpty(gatewayUrl) ? "unset (endpoint configured at listen time)" : "configured (value suppressed)")}");
        Console.WriteLine();

        try
        {
            return options.Mode switch
            {
                Mode.FakeTranscript => await RunListenAsync(
                    new FakeListener(FakeListener.Mode.Transcript, options.Text)),
                Mode.FakeNoSpeech => await RunListenAsync(new FakeListener(FakeListener.Mode.NoSpeech)),
                Mode.FakeAuthError => await RunListenAsync(new FakeListener(FakeListener.Mode.AuthError)),
                Mode.Cancel => await RunCancelAsync(options.CancelMs),
                Mode.Burst => await RunBurstAsync(),
                Mode.SpeakFake => await RunSpeakAsync(options.Text),
                Mode.AssertGcpEvents => AssertGcpEvents(),
                Mode.AssertTutorAuthority => AssertTutorAuthority(),
                Mode.AssertNoBackend => AssertNoBackend(),
                Mode.AssertFishFormat => await AssertFishFormatAsync(),
                _ => PrintUsage(),
            };
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("CANCELLED: the token fired; no verdict was produced (exit 13).");
            return 13;
        }
        catch (VoiceAuthenticationException ex)
        {
            Console.WriteLine($"KEY ERROR: {ex.Message}");
            return 12;
        }
        catch (VoiceClientException ex)
        {
            Console.WriteLine($"PROVIDER ERROR: {ex.Message}");
            return 3;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"UNEXPECTED: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static HostOptions ParseArgs(string[] args)
    {
        var mode = Mode.Help;
        var text = "where is the Save button";
        var cancelMs = 50;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--fake-transcript":
                    mode = Mode.FakeTranscript;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) text = args[++i];
                    break;
                case "--fake-nospeech": mode = Mode.FakeNoSpeech; break;
                case "--fake-auth-error": mode = Mode.FakeAuthError; break;
                case "--cancel-ms": mode = Mode.Cancel; cancelMs = int.Parse(args[++i]); break;
                case "--burst-fake": mode = Mode.Burst; break;
                case "--speak-fake": mode = Mode.SpeakFake; break;
                case "--text": text = args[++i]; break;
                case "--assert-gcp-events": mode = Mode.AssertGcpEvents; break;
                case "--assert-tutor-authority": mode = Mode.AssertTutorAuthority; break;
                case "--assert-no-backend": mode = Mode.AssertNoBackend; break;
                case "--assert-fish-format": mode = Mode.AssertFishFormat; break;
                case "--help": case "-h": case "/?": mode = Mode.Help; break;
                default: Console.WriteLine($"ignoring unknown argument '{args[i]}'"); break;
            }
        }

        return new HostOptions(mode, text, cancelMs);
    }

    private static int PrintUsage()
    {
        Console.WriteLine("Voice smoke host");
        Console.WriteLine("Usage: VoiceSmoke <mode> [--text \"<speak text>\"]");
        Console.WriteLine();
        Console.WriteLine("Offline rows (no Gateway, no key, no network):");
        Console.WriteLine("  --fake-transcript [\"<text>\"]  fake committed transcript -> UtteranceResult, exit 0");
        Console.WriteLine("  --fake-nospeech               silence verdict (IsNoSpeech, NOT failure), exit 0");
        Console.WriteLine("  --fake-auth-error             typed auth failure from the in-leg, exit 12");
        Console.WriteLine("  --cancel-ms <n>               hang listen, cancel after n ms -> OCE, exit 13");
        Console.WriteLine("  --burst-fake                  second overlapping listen REFUSED, exit 0");
        Console.WriteLine("  --speak-fake --text \"<text>\"  fake TTS -> REAL winmm playback + SpeakResult, exit 0");
        Console.WriteLine("  --assert-gcp-events           message allowlist + frame classification, exit 0");
        Console.WriteLine("  --assert-tutor-authority      assistant frames never become text, exit 0");
        Console.WriteLine("  --assert-no-backend           task.* classified + counted (abort guard), exit 0");
        Console.WriteLine("  --assert-fish-format          wav/44100 body + WAV parser contract, exit 0");
        Console.WriteLine();
        Console.WriteLine("Env (names only here; values never echoed): FISH_AUDIO_API_KEY,");
        Console.WriteLine("HEYGANESH_QWEN_GATEWAY_URL. Live rows are human-run, not scripted.");
        return 0;
    }

    private sealed record HostOptions(Mode Mode, string Text, int CancelMs);
}
