namespace HeyGanesh.App;

// Invocation surface for the shell: flag parsing and usage text.
//
// Kept to flags that map one-to-one onto an option some module already owns. The shell invents no
// policy of its own — no confidence threshold, no fallback rule, no display gate — because DEC-010 and
// DEC-011 put those decisions elsewhere on purpose.

internal static partial class ShellHost
{
    private static ShellOptions ParseArgs(string[] args)
    {
        var question = "";
        var help = false; var once = false; var overlay = true; var foreground = false;
        var retainCaptures = true; var expectDpi = true; var noSpeak = false;
        var seconds = 0; var cancelMs = 0; var durationMs = 3000;
        var maxDim = 1280; var maxCandidates = 120;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--question": question = args[++i]; break;
                case "--once": once = true; break;
                case "--no-overlay": overlay = false; break;
                case "--foreground": foreground = true; break;
                case "--no-retain-captures": retainCaptures = false; break;
                case "--no-speak": noSpeak = true; break;
                case "--no-dpi": expectDpi = false; break;
                case "--seconds": seconds = int.Parse(args[++i]); break;
                case "--cancel-ms": cancelMs = int.Parse(args[++i]); break;
                case "--duration-ms": durationMs = int.Parse(args[++i]); break;
                case "--max-dim": maxDim = int.Parse(args[++i]); break;
                case "--max-candidates": maxCandidates = int.Parse(args[++i]); break;
                case "--help": case "-h": case "/?": help = true; break;
                default: Console.WriteLine($"ignoring unknown argument '{args[i]}'"); break;
            }
        }

        // A canned question was the stand-in for the Voice pipeline, which now exists. So --question
        // is optional and mode-dependent: with --once it drives the deterministic scripted path (no
        // microphone, no keystrokes); without --once it is ignored in favour of the microphone, and
        // RunAsync says so out loud. An empty question with --once means "listen once instead" — a
        // mode decision, never a defaulted fake question.
        return new ShellOptions(
            question, help, once, overlay, foreground, retainCaptures, expectDpi,
            seconds, cancelMs, durationMs, maxDim, maxCandidates, noSpeak);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Hey Ganesh — app shell (speak -> Voice -> Context Orchestrator -> Screen Overlay -> Tutor -> spoken answer)");
        Console.WriteLine();
        Console.WriteLine("Usage: HeyGanesh.App.exe [--once [--question \"<text>\"]] [options]");
        Console.WriteLine();
        Console.WriteLine("--question        LEGACY / verification: a canned question text. With --once it drives the");
        Console.WriteLine("                  deterministic scripted path (no microphone, no keystrokes). Without");
        Console.WriteLine("                  --once it is IGNORED — the microphone is the question source.");
        Console.WriteLine("--once            run exactly one turn at startup and exit, without installing the keyboard");
        Console.WriteLine("                  hook: with --question, the legacy text path; without it, one live listen");
        Console.WriteLine("                  (speak now). Default is to install the hook: press Ctrl+Alt to speak,");
        Console.WriteLine("                  release to submit the utterance.");
        Console.WriteLine("--seconds n       stop a hooked run after n seconds (otherwise: until Ctrl+C).");
        Console.WriteLine("--cancel-ms n     cancel the current turn's token after n ms, to watch a turn unwind.");
        Console.WriteLine("--duration-ms n   how long the overlay stays on screen (default 3000).");
        Console.WriteLine("--no-overlay      resolve and report, but draw nothing.");
        Console.WriteLine("--no-speak        teach but stay silent: skip the speech-out leg. The lesson (when one is");
        Console.WriteLine("                  produced) is still reported as text — headless scripted runs and anyone");
        Console.WriteLine("                  who does not want the desk speaking should use it.");
        Console.WriteLine("--foreground      walk the foreground window instead of the window under the cursor.");
        Console.WriteLine("--no-retain-captures  drop the captured frames from the verdict (RetainCaptures=false).");
        Console.WriteLine("--max-dim         capture downscale cap in px (default 1280).");
        Console.WriteLine("--max-candidates  candidate cap handed to Element Selection (default 120).");
        Console.WriteLine("--no-dpi          do not claim PerMonitorV2 awareness, so the overlay's refusal to draw on");
        Console.WriteLine("                  a non-aware host can be observed. Nothing in a normal run should use it.");
    }

    private static void PrintFooter()
    {
        Console.WriteLine();
        Console.WriteLine("Exit codes: 0 a verdict was produced (an honest no-match counts, and so does a silent or");
        Console.WriteLine("release-abandoned voice turn, which resolves nothing on purpose) | 1 the verdict is a");
        Console.WriteLine("failure, or no transcript was produced | 2 the shell could not start | 3 provider/HTTP");
        Console.WriteLine("error propagated rather than swallowed | 4 an unexpected fault | 12 a required API key");
        Console.WriteLine("is missing | 13 cancelled. The teach/speak leg after a non-Failed verdict reuses these");
        Console.WriteLine("codes: 12/3 are a key/provider error from the teaching seam or the speaker, 4 is a mapping");
        Console.WriteLine("fault in the shell; the verdict is always reported (and drawn) before either can fire.");
        Console.WriteLine();
        Console.WriteLine("Live listens need HEYGANESH_QWEN_GATEWAY_URL (the qwen-audio-agent Gateway is a separate");
        Console.WriteLine("process that runs with its own provider key; no key crosses the protocol to HeyGanesh). The");
        Console.WriteLine("resolve legs need TYPESAFE_API_KEY (Element Selection) and DEEPSEEK_API_KEY (Vision, which");
        Console.WriteLine("only runs as the fallback). The shell prints key presence only, never a value.");
    }

    /// <summary>
    /// The one place the shell states what it is about to do, including the two facts an operator needs
    /// before blaming a module: which window will be walked, and whether this process is DPI aware
    /// enough for the overlay to draw at all.
    /// </summary>
    private static void PrintHeader(ShellOptions options)
    {
        var typesafeKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY"));
        var deepseekKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY"));
        var gatewayUrl = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HEYGANESH_QWEN_GATEWAY_URL"));
        var fishKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FISH_AUDIO_API_KEY"));

        var trigger = options.Once
            ? "one turn at startup, hook never installed"
            : "every Ctrl+Alt hold (press to speak, release to submit)" +
              (options.Seconds > 0 ? $", auto-stop after {options.Seconds}s" : ", until Ctrl+C");

        // The question line states the mode honestly: legacy text with --once, the microphone in the
        // product path, and an explicit "ignored" marker when a flagged question cannot take effect.
        var question = string.IsNullOrWhiteSpace(options.Question)
            ? "<voice: hold Ctrl+Alt to speak>"
            : options.Once
                ? options.Question
                : options.Question + " (ignored: hooked mode listens to the microphone)";

        var dpi = options.ExpectDpi
            ? (NativeFns.IsCurrentThreadPerMonitorAware()
                ? "per-monitor-v2 aware"
                : "NOT AWARE — the overlay will refuse to draw")
            : "awareness not claimed (--no-dpi) — the overlay must refuse to draw";

        Console.WriteLine("Hey Ganesh app shell");
        Console.WriteLine($"  question   : {question}");
        Console.WriteLine($"  trigger    : {trigger}");
        Console.WriteLine($"  target     : {(options.Foreground ? "foreground window" : "window under the cursor")}");
        Console.WriteLine($"  overlay    : {(options.Overlay ? $"{options.DurationMs} ms per found verdict" : "disabled (--no-overlay)")}");
        Console.WriteLine($"  keys       : TYPESAFE_API_KEY={(typesafeKey ? "present (value suppressed)" : "MISSING")}  " +
                          $"DEEPSEEK_API_KEY={(deepseekKey ? "present (value suppressed)" : "MISSING")}");
        Console.WriteLine($"  voice      : HEYGANESH_QWEN_GATEWAY_URL={(gatewayUrl ? "present (value suppressed)" : "MISSING")}  " +
                          $"FISH_AUDIO_API_KEY={(fishKey ? "present (value suppressed)" : "MISSING")} (TTS speaks the lesson when one is produced)");
        // Keep this line in step with CreateTutorClient(): DEC-015 ships the teaching seam with no
        // client, so until the owner decides a provider, the teach/speak leg reports its skip per turn.
        Console.WriteLine("  tutor      : seam wired, no client yet (DEC-015 — the provider is the owner's call)");
        Console.WriteLine($"  dpi        : {dpi}");
        Console.WriteLine();
    }

    private sealed record ShellOptions(
        string Question,
        bool Help,
        bool Once,
        bool Overlay,
        bool Foreground,
        bool RetainCaptures,
        bool ExpectDpi,
        int Seconds,
        int CancelMs,
        int DurationMs,
        int MaxDim,
        int MaxCandidates,
        bool NoSpeak);
}

