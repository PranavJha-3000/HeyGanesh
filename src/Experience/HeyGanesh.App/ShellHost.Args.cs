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
        var retainCaptures = true; var expectDpi = true; var noSpeak = false; var lesson = false; var noRoute = false; var noOcr = false;
        var seconds = 0; var cancelMs = 0; var durationMs = 3000;
        var maxDim = 1280; var maxCandidates = 120;
        string? keyCommand = null; var keyName = "";

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
                case "--lesson": lesson = true; break;
                case "--no-route": noRoute = true; break;
                case "--no-ocr": noOcr = true; break;
                case "--no-dpi": expectDpi = false; break;
                case "--seconds": seconds = int.Parse(args[++i]); break;
                case "--cancel-ms": cancelMs = int.Parse(args[++i]); break;
                case "--duration-ms": durationMs = int.Parse(args[++i]); break;
                case "--max-dim": maxDim = int.Parse(args[++i]); break;
                case "--max-candidates": maxCandidates = int.Parse(args[++i]); break;
                case "--help": case "-h": case "/?": help = true; break;
                // Key commands (DEC-045) run on their own and exit; the name is checked by RunKeyCommand.
                case "--keys": keyCommand = "keys"; break;
                case "--set-key": keyCommand = "set"; keyName = i + 1 < args.Length ? args[++i] : ""; break;
                case "--remove-key": keyCommand = "remove"; keyName = i + 1 < args.Length ? args[++i] : ""; break;
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
            seconds, cancelMs, durationMs, maxDim, maxCandidates, noSpeak, lesson, noRoute, noOcr, keyCommand, keyName);
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
        Console.WriteLine("--lesson          teach with a drawn, spoken visual lesson (cards + voice) instead of the");
        Console.WriteLine("                  Tutor's prose. Needs DEEPSEEK_API_KEY for the lesson model; without it the");
        Console.WriteLine("                  lesson leg reports its skip (DEC-027). Combine with --no-speak to pace silently.");
        Console.WriteLine("                  Without it, and with TYPESAFE_API_KEY set, Jev routes each turn instead: point");
        Console.WriteLine("                  (the highlight is the answer), explain (the Tutor), or a 2D/3D lesson (DEC-036).");
        Console.WriteLine("--no-route        never ask Jev how to teach: the Tutor leg, as before the router existed.");
        Console.WriteLine("--no-ocr          do not read the screen's text. By default the cursor monitor's text is read");
        Console.WriteLine("                  locally, and up to 40 short lines inside the target window (none that look");
        Console.WriteLine("                  like a key or token) are offered to Jev with the UI controls (DEC-039).");
        Console.WriteLine("--foreground      walk the foreground window instead of the window under the cursor.");
        Console.WriteLine("--no-retain-captures  drop the captured frames from the verdict (RetainCaptures=false).");
        Console.WriteLine("--max-dim         capture downscale cap in px (default 1280).");
        Console.WriteLine("--max-candidates  candidate cap handed to Element Selection (default 120).");
        Console.WriteLine("--no-dpi          do not claim PerMonitorV2 awareness, so the overlay's refusal to draw on");
        Console.WriteLine("                  a non-aware host can be observed. Nothing in a normal run should use it.");
        Console.WriteLine("--keys            show each provider key's state (deepseek, typesafe, fish): where it was found and");
        Console.WriteLine("                  its length, never its value; then exit.");
        Console.WriteLine("--set-key <name>  store a key in Windows Credential Manager. It is typed or pasted with nothing");
        Console.WriteLine("                  shown, or piped on stdin; then exit. Every API client reads it from there");
        Console.WriteLine("                  first, then from the environment variable.");
        Console.WriteLine("--remove-key <name>  delete a stored key (the environment variable is not touched); then exit.");
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
        Console.WriteLine("fault in the shell (or a lesson card fault), 1 with --lesson means no usable lesson came");
        Console.WriteLine("back; the verdict is always reported (and drawn) before any of them can fire.");
        Console.WriteLine("Key commands (--keys, --set-key, --remove-key): 0 done | 1 nothing was entered, or the key");
        Console.WriteLine("was refused (a space or line break inside) | 2 unknown key name | 4 Credential Manager refused.");
        Console.WriteLine();
        Console.WriteLine("Live listens need HEYGANESH_QWEN_GATEWAY_URL (the qwen-audio-agent Gateway is a separate");
        Console.WriteLine("process that runs with its own provider key; no key crosses the protocol to HeyGanesh). The");
        Console.WriteLine("resolve legs need TYPESAFE_API_KEY (Element Selection) and DEEPSEEK_API_KEY (Vision, which");
        Console.WriteLine("only runs as the fallback), stored with --set-key or set in the environment. The shell prints");
        Console.WriteLine("key presence, length and source only, never a value.");
    }

    /// <summary>
    /// The one place the shell states what it is about to do, including the two facts an operator needs
    /// before blaming a module: which window will be walked, and whether this process is DPI aware
    /// enough for the overlay to draw at all.
    /// </summary>
    private static void PrintHeader(ShellOptions options)
    {
        // Keys come from the key ring (DEC-046): Credential Manager first, then the environment variable. The
        // same answers decide which legs CreateRouter() and CreateLessonPlanner() compose.
        var typesafeKey = HasKey("typesafe");
        var deepseekKey = HasKey("deepseek");
        var gatewayUrl = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HEYGANESH_QWEN_GATEWAY_URL"));

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
        Console.WriteLine($"  keys       : TYPESAFE_API_KEY={Keys.Describe("typesafe")}  DEEPSEEK_API_KEY={Keys.Describe("deepseek")}");
        Console.WriteLine($"  voice      : HEYGANESH_QWEN_GATEWAY_URL={(gatewayUrl ? "present (value suppressed)" : "MISSING")}  " +
                          $"FISH_AUDIO_API_KEY={Keys.Describe("fish")} (TTS speaks the lesson when one is produced)");
        Console.WriteLine($"  tutor      : {TutorHeader()}");
        Console.WriteLine($"  route      : {RouteHeader(options, typesafeKey)}");
        Console.WriteLine($"  ocr        : {OcrHeader(options.NoOcr)}");
        Console.WriteLine($"  lesson     : {LessonHeader(options.Lesson, RoutesLessons(options, typesafeKey), deepseekKey)}");
        Console.WriteLine($"  dpi        : {dpi}");
        Console.WriteLine();
    }

    /// <summary>The header's OCR line (DEC-039); keep in step with the capture options in RunAsync.</summary>
    private static string OcrHeader(bool off)
    {
        if (off)
        {
            return "off (--no-ocr): only UI controls are offered to Jev";
        }

        var reader = new HeyGanesh.ScreenCapture.ScreenTextReader();
        return reader.IsAvailable
            ? $"ON ({reader.Language}): the cursor monitor's text is read locally; up to 40 short lines in the target window join the Jev request"
            : "ON but no OCR language is installed for this user, so no text is read";
    }

    /// <summary>The header's lesson line; keep in step with CreateLessonPlanner() (DEC-027) and CreateRouter() (DEC-036).</summary>
    private static string LessonHeader(bool on, bool routed, bool key) =>
        routed ? (key
            ? "when the route chooses one: lesson model deepseek-flash, DEEPSEEK_API_KEY present (value suppressed); 2D, or 3D on the one model Jev picked"
            : "when the route chooses one: no lesson model, DEEPSEEK_API_KEY MISSING, so the leg reports its skip")
        : !on ? "off (the Tutor leg teaches; --lesson draws instead)"
        : key ? $"ON (--lesson): lesson model deepseek-flash, DEEPSEEK_API_KEY present (value suppressed); 3D: {Scene3dModelCount} models, click a part to ask, or one schematic for anything else"
        : "ON (--lesson): no lesson model, DEEPSEEK_API_KEY MISSING, so the leg reports its skip";

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
        bool NoSpeak,
        bool Lesson,
        bool NoRoute,
        bool NoOcr,
        string? KeyCommand,
        string KeyName);
}

