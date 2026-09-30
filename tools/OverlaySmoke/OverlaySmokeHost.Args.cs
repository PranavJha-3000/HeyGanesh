namespace HeyGanesh.Tools.OverlaySmoke;

// Argument parsing, usage text and the run header for the Screen Overlay smoke host.

internal static partial class OverlaySmokeHost
{
    internal enum RunMode
    {
        Found,
        NoMatch,
        Failed,
        Cancel,
        Burst,
        ClickThrough,
        Repeats,
    }

    private static HostOptions ParseArgs(string[] args)
    {
        var mode = RunMode.Found;
        var help = false;
        var durationMs = 1500;
        var cancelMs = 0;
        var repeats = 5;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--found": mode = RunMode.Found; break;
                case "--no-match": mode = RunMode.NoMatch; break;
                case "--failed": mode = RunMode.Failed; break;
                case "--cancel-ms": mode = RunMode.Cancel; cancelMs = int.Parse(args[++i]); break;
                case "--burst": mode = RunMode.Burst; break;
                case "--assert-clickthrough": mode = RunMode.ClickThrough; break;
                case "--repeats": mode = RunMode.Repeats; repeats = int.Parse(args[++i]); break;
                case "--duration-ms": durationMs = int.Parse(args[++i]); break;
                case "--help": case "-h": case "/?": help = true; break;
                default: Console.WriteLine($"ignoring unknown argument '{args[i]}'"); break;
            }
        }

        // The token must fire while the highlight is up: a show window shorter than the cancel delay
        // would complete normally and report the wrong outcome (mirrors the CO host's delay auto-raise).
        if (mode == RunMode.Cancel && durationMs <= cancelMs)
        {
            Console.WriteLine($"note: --duration-ms raised to {cancelMs + 2000} ms so the token fires mid-show.");
            durationMs = cancelMs + 2000;
        }

        // Burst needs the first show still in flight when the second ShowAsync arrives.
        if (mode == RunMode.Burst && durationMs < 600)
        {
            Console.WriteLine("note: --duration-ms raised to 600 ms so the first show is still active.");
            durationMs = 600;
        }

        // The click-through assertions need a live window to inspect for a few hundred milliseconds.
        if (mode == RunMode.ClickThrough && durationMs < 2000)
        {
            Console.WriteLine("note: --duration-ms raised to 2000 ms so the window can be inspected.");
            durationMs = 2000;
        }

        // Repeats cycle fast by design: 25 full-length shows would take a minute to prove a handle
        // count is flat. The duration under test is therefore capped here, and reported as such.
        if (mode == RunMode.Repeats && durationMs > 200)
        {
            durationMs = 200;
        }

        return new HostOptions(mode, help, durationMs, cancelMs, repeats);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Screen Overlay verification host");
        Console.WriteLine("Usage: OverlaySmoke [--found] [--no-match] [--failed] [--cancel-ms n]");
        Console.WriteLine("                    [--burst] [--assert-clickthrough] [--repeats n] [--duration-ms n]");
        Console.WriteLine();
        Console.WriteLine("All verdicts are canned offline — no capture, no API keys, no network.");
        Console.WriteLine();
        Console.WriteLine("--found               : show a canned found box for the full duration (SO-3; default).");
        Console.WriteLine("--no-match            : Found=false must create no window at all (SO-4).");
        Console.WriteLine("--failed              : Found=false + FailureReason (the CO-6 shape) must classify as");
        Console.WriteLine("                        'failure', not 'no-match', and create no window (SO-5).");
        Console.WriteLine("--cancel-ms n         : cancel the show token after n ms; windows must be removed and");
        Console.WriteLine("                        the result must say 'cancelled' (SO-6; exit 13 when it holds).");
        Console.WriteLine("--burst               : a second ShowAsync while one is active must be refused, not");
        Console.WriteLine("                        queued (SO-8).");
        Console.WriteLine("--assert-clickthrough : while showing, assert WS_EX_LAYERED|TRANSPARENT|NOACTIVATE|");
        Console.WriteLine("                        TOPMOST, WM_NCHITTEST -> HTTRANSPARENT, capture exclusion, and no");
        Console.WriteLine("                        foreground activation; then hide and assert removal (SO-7).");
        Console.WriteLine("--repeats n           : n show/hide cycles after a warmup; GDI and handle counts must");
        Console.WriteLine("                        return near baseline (SO-9; duration capped at 200 ms per cycle).");
        Console.WriteLine("--duration-ms n       : visible duration per show (default 1500; some modes auto-raise).");
        Console.WriteLine();
        Console.WriteLine("Exit codes: 0 = expectation held, 2 = expectation failed, 13 = cancelled as expected,");
        Console.WriteLine("            1 = unexpected error. No key is ever involved, so 12 cannot occur here.");
    }

    private static void PrintHeader(HostOptions options)
    {
        Console.WriteLine("Screen Overlay verification host (canned GuidanceResult -> click-through window)");
        Console.WriteLine($"  mode      : {options.ModeLine}");
        Console.WriteLine($"  duration  : {options.DurationMs} ms");
        if (options.Mode == RunMode.Repeats)
        {
            Console.WriteLine($"  repeats   : {options.Repeats} cycles (plus one warmup)");
        }

        Console.WriteLine("  keys      : none required (every verdict is constructed offline)");
        Console.WriteLine();
    }

    private sealed record HostOptions(
        RunMode Mode,
        bool Help,
        int DurationMs,
        int CancelMs,
        int Repeats)
    {
        public TimeSpan Duration => TimeSpan.FromMilliseconds(DurationMs);

        /// <summary>Human-readable mode line for the run header.</summary>
        public string ModeLine => Mode switch
        {
            RunMode.Found => "found (canned box on the primary monitor, full duration)",
            RunMode.NoMatch => "no-match (Found=false; expects no window)",
            RunMode.Failed => "failed (Found=false + FailureReason; expects no window)",
            RunMode.Cancel => $"cancel token after {CancelMs} ms",
            RunMode.Burst => "burst (second ShowAsync must be refused)",
            RunMode.ClickThrough => "click-through assertion while showing",
            RunMode.Repeats => $"repeats x{Repeats} (leak check)",
            _ => Mode.ToString(),
        };
    }
}