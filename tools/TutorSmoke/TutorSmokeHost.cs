using HeyGanesh.TutorEngine;

namespace HeyGanesh.Tools.TutorSmoke;

/// <summary>
/// Verification host for the Tutor Engine. Every row runs offline against fake seams and recorded
/// completions — no provider, key, mic or network. Exit codes mirror the shell contract: 0 pass,
/// 1 failed assertion or failed verdict, 2 usage, 3 provider error, 12 key error, 13 cancelled.
/// </summary>
internal static partial class TutorSmokeHost
{
    private enum Mode
    {
        Help,
        FakeExplain,
        AssertPrompt,
        AssertGrounding,
        AssertParity,
        AssertSource,
        AssertClientError,
        AssertAuthError,
        AssertTutorPayload,
        LiveExplain,
        Cancel,
    }

    public static async Task<int> RunAsync(string[] args)
    {
        var options = ParseArgs(args);
        if (options.Mode == Mode.Help)
        {
            PrintUsage();
            return 0;
        }

        Console.WriteLine("Tutor Engine verification host (offline fakes + contract assertions)");
        // This module has no keys by construction — the header says so instead of pretending to
        // check. The provider rows (--assert-tutor-payload loopback stub, --live-explain) report
        // their own key state in their own output; no row ever prints a value.
        Console.WriteLine("  keys      : no field in HeyGanesh.TutorEngine can carry one (provider rows report their own state)");
        Console.WriteLine();

        try
        {
            return options.Mode switch
            {
                Mode.FakeExplain => await RunFakeExplainAsync(options.Text),
                Mode.AssertPrompt => await RunAssertPromptAsync(),
                Mode.AssertGrounding => await RunAssertGroundingAsync(),
                Mode.AssertParity => await RunAssertParityAsync(),
                Mode.AssertSource => RunAssertSource(),
                Mode.AssertClientError => await RunAssertClientErrorAsync(),
                Mode.AssertAuthError => await RunAssertAuthErrorAsync(),
                Mode.AssertTutorPayload => await RunAssertTutorPayloadAsync(),
                Mode.LiveExplain => await RunLiveExplainAsync(options.Text),
                Mode.Cancel => await RunCancelAsync(options.CancelMs),
                _ => PrintUsage(),
            };
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("CANCELLED: the token fired; no explanation was produced (exit 13).");
            return 13;
        }
        catch (TutorAuthenticationException ex)
        {
            Console.WriteLine($"KEY ERROR: {ex.Message}");
            return 12;
        }
        catch (TutorClientException ex)
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
        var text = "what does git rebase actually do to my commits";
        var cancelMs = 50;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--fake-explain":
                    mode = Mode.FakeExplain;
                    // The usage line shows a positional question; take it when present so the row
                    // says what it ran on instead of silently falling back to the default.
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        text = args[++i];
                    }

                    break;
                case "--assert-prompt": mode = Mode.AssertPrompt; break;
                case "--assert-grounding": mode = Mode.AssertGrounding; break;
                case "--assert-parity": mode = Mode.AssertParity; break;
                case "--assert-source": mode = Mode.AssertSource; break;
                case "--assert-client-error": mode = Mode.AssertClientError; break;
                case "--assert-auth-error": mode = Mode.AssertAuthError; break;
                case "--assert-tutor-payload": mode = Mode.AssertTutorPayload; break;
                case "--live-explain": mode = Mode.LiveExplain;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        text = args[++i];
                    }

                    break;
                case "--cancel-ms": mode = Mode.Cancel; cancelMs = int.Parse(args[++i]); break;
                case "--text": text = args[++i]; break;
                case "--help": case "-h": case "/?": mode = Mode.Help; break;
                default: Console.WriteLine($"ignoring unknown argument '{args[i]}'"); break;
            }
        }

        return new HostOptions(mode, text, cancelMs);
    }

    private static int PrintUsage()
    {
        Console.WriteLine("Tutor smoke host");
        Console.WriteLine("Usage: TutorSmoke <mode> [--text \"<question>\"]");
        Console.WriteLine();
        Console.WriteLine("Offline rows (no provider, no key, no network):");
        Console.WriteLine("  --fake-explain [\"<q>\"]        recorded lesson -> TutorResult, exit 0");
        Console.WriteLine("  --assert-prompt               prompt contract + visual carry + caps, exit 0");
        Console.WriteLine("  --assert-grounding            untrusted labeling + grounded lesson + generic");
        Console.WriteLine("                                lesson without screen data, exit 0");
        Console.WriteLine("  --assert-parity               text-only prompts byte-identical across runs, exit 0");
        Console.WriteLine("  --assert-source               no actuation surface + no-Dispose API audit, exit 0");
        Console.WriteLine("  --assert-client-error         pass-through + malformed-completion mapping, exit 0");
        Console.WriteLine("  --assert-auth-error           auth fault stays typed; secret stays unprinted, exit 12");
        Console.WriteLine("  --cancel-ms <n>               hang completion, cancel after n ms -> OCE, exit 13");
        Console.WriteLine("  --assert-tutor-payload        live client over a loopback stub: wire payload,");
        Console.WriteLine("                                thinking.disabled, error mapping, exit 0");
        Console.WriteLine();
        Console.WriteLine("Live rows (need the real provider; offline-safe by construction):");
        Console.WriteLine("  --live-explain [\"<q>\"]         one real DeepSeek lesson via the factory; without");
        Console.WriteLine("                                DEEPSEEK_API_KEY it prints KEY ERROR, exit 12");
        Console.WriteLine();
        Console.WriteLine("NOT claimed by any row: real-model prompt-injection resistance (live-only, open).");
        return 0;
    }

    private sealed record HostOptions(Mode Mode, string Text, int CancelMs);
}