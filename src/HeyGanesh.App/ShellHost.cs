using HeyGanesh.ContextOrchestrator;
using HeyGanesh.ElementSelection;
using HeyGanesh.ScreenCapture;
using HeyGanesh.ScreenOverlay;
using HeyGanesh.TutorEngine;
using HeyGanesh.Vision;
using HeyGanesh.Voice;

// HeyGanesh.GlobalHotkeyMonitor is both a namespace and the class inside it, and this code lives under
// HeyGanesh.* — so the bare name resolves to the namespace, not the type. Aliased once here rather than
// fully qualified at every use (the module hosts can use the bare name only because they sit in the
// global namespace).
using HotkeyMonitor = HeyGanesh.GlobalHotkeyMonitor.GlobalHotkeyMonitor;

namespace HeyGanesh.App;

/// <summary>
/// The app shell: the composition root, the lifecycle owner, and the one place in the tree that maps a
/// global-hotkey trigger onto a guidance session.
/// </summary>
/// <remarks>
/// <para>
/// This assembly owns the four things no module is allowed to own:
/// </para>
/// <list type="number">
/// <item><description>
/// The single <see cref="ScreenCaptureService"/>. The orchestrator captures with it and the overlay asks
/// it for monitor topology, so sharing one instance means one <c>Windows.Graphics.Capture</c> stack
/// instead of two looking at the same desktop. The overlay disposes a capture service only when it
/// created one itself, so passing ours in is the supported arrangement and not a leak.
/// </description></item>
/// <item><description>
/// The hotkey subscription. DEC-010 records this as deliberately absent from the orchestrator — "the host
/// maps the event to <c>HandleHotkeyPressedAsync</c>" — because a subscription inside a module cannot be
/// exercised by any host that keeps <c>SendInput</c> out of its tooling.
/// </description></item>
/// <item><description>
/// The question source. The Voice pipeline exists, so in the product path the question arrives as
/// speech: a chord press opens the microphone, a chord release closes it, and the committed
/// transcript becomes the question. <c>--question</c> survives only for <c>--once</c>, the
/// deterministic scripted path, and is ignored (loudly) when it cannot take effect.
/// </description></item>
/// <item><description>
/// The verdict-to-lesson mapping. A non-Failed verdict is turned by the shell alone into a plain-data
/// <c>TutorContext</c> (plus a visual derived from an already-retained frame), and the Tutor's answer
/// is what the shell feeds to <c>SpeakAsync</c> — so no orchestrator or capture type ever reaches the
/// teacher, and Voice never learns what it is reading aloud (DEC-015/DEC-016).
/// </description></item>
/// </list>
/// <para>
/// What this class deliberately does <b>not</b> own: any confidence threshold, any fallback rule, or any
/// decision about what a failure means. Those live in the legs (DEC-008/DEC-009) and in the orchestrator
/// (DEC-010). The shell reports and draws; it never re-classifies a verdict.
/// </para>
/// </remarks>
internal static partial class ShellHost
{
    /// <summary>
    /// How long shutdown waits for a session that was in flight when Ctrl+C arrived. Bounded on purpose:
    /// cancellation has already been requested, so this is slack for the unwinding to finish, not a
    /// second provider timeout bolted on top of the modules' own.
    /// </summary>
    private static readonly TimeSpan DrainGrace = TimeSpan.FromSeconds(5);

    public static async Task<int> RunAsync(string[] args)
    {
        var options = ParseArgs(args);

        if (options.Help)
        {
            PrintUsage();
            PrintFooter();
            return 0;
        }

        // The question source is mode-dependent now that Voice exists (DEC-014): hooked mode is the
        // product path and listens to the microphone, so a flagged --question there is ignored (said
        // out loud, never silently defaulted); --once keeps --question as the deterministic scripted
        // path, and --once without one becomes a single live listen. Exit 2 remains "the shell could
        // not start" for the failures below (capture unsupported, hook not installable), not for a
        // missing flag.
        if (!options.Once && !string.IsNullOrWhiteSpace(options.Question))
        {
            Console.WriteLine("note: --question is legacy and is IGNORED in hooked mode; the microphone is the " +
                              "question source (use --once --question for the scripted path).");
            Console.WriteLine();
        }

        PrintHeader(options);

        if (!ScreenCaptureService.IsCaptureSupported())
        {
            Console.WriteLine("FATAL: GraphicsCaptureSession.IsSupported() == false on this device, so neither leg can run.");
            return 2;
        }

        // ---- composition root ---------------------------------------------------------------------
        // Declaration order sets teardown order (reverse): hotkey, overlay, orchestrator, vision,
        // selection, capture. That ordering is load-bearing — the trigger source stops first so no new
        // session can start against services being torn down, and the overlay is released before the
        // capture service it borrows for monitor topology.
        using var capture = new ScreenCaptureService(new ScreenCaptureOptions { MaxDimension = options.MaxDim });
        using var selection = new ElementSelectionService(
            options: new SelectionOptions { MaxCandidates = options.MaxCandidates });
        using var vision = new VisionService();
        using var orchestrator = new ContextOrchestratorService(
            capture,
            selection,
            vision,
            new ContextOrchestratorOptions
            {
                // Null means Element Selection defaults to the window under the cursor — the same place
                // the capture module's cursor-first monitor ordering is looking.
                Target = options.Foreground ? SelectionTarget.ForegroundWindow : null,
                RetainCaptures = options.RetainCaptures,
            });
        using var overlay = new ScreenOverlayService(
            new ScreenOverlayOptions { VisibleDuration = TimeSpan.FromMilliseconds(options.DurationMs) },
            monitors: capture);

        // The teaching seam, composed like the speech one — and, like it, harmless at rest.
        // CreateTutorClient() returns null today (DEC-015 keeps the provider decision the owner's),
        // so the tutor leg reports its skip after every verdict and costs nothing. When a live
        // client lands, only CreateTutorClient changes. The using covers the (currently absent)
        // client, which the shell owns because TutorService never disposes the seam it was handed;
        // declared before voice so in teardown the speaker outlives the teacher — a lesson can
        // always finish being read aloud after its call completed.
        using ITutorClient? tutorClient = CreateTutorClient();
        var tutor = tutorClient is null ? null : new TutorService(tutorClient);

        // The speech seam, composed with the default real legs (Qwen Gateway in, Fish/WAV out). The
        // constructor opens no devices: the microphone is taken per listen and released when the listen
        // ends, so sitting idle here costs nothing. Declaration order still sets teardown order — voice
        // is disposed after the hotkey (no new turn can start) and before the overlay (a turn that was
        // draining has already been awaited by RunHookedAsync, so nothing speaks into a dead service).
        using var voice = new VoiceService();

        using var hotkey = new HotkeyMonitor();

        // Cancelled at shutdown. Every session links it, so Ctrl+C aborts work in flight instead of
        // waiting out a capture or a provider call.
        using var lifetime = new CancellationTokenSource();

        if (options.Once)
        {
            if (!string.IsNullOrWhiteSpace(options.Question))
            {
                // The deterministic legacy trigger: no hook, no keystrokes, no human. Same pipeline the
                // chord's voice turn feeds, one question source swapped for the other.
                Console.WriteLine("resolving once at startup (the hook is never installed)...");
                Console.WriteLine();
                return await RunSessionAsync(orchestrator, overlay, voice, tutor, options, lifetime.Token).ConfigureAwait(false);
            }

            // --once without a question: one live listen instead of one canned text — the scripted
            // equivalent of press-and-release, still without installing the keyboard hook.
            Console.WriteLine("listening once at startup (the hook is never installed; no --question, so the " +
                              "microphone is the question source)...");
            Console.WriteLine("speak now — the turn ends on its own at the silence timeout; Ctrl+C cancels.");
            Console.WriteLine();
            var oncePump = new TriggerPump(voice, orchestrator, overlay, tutor, options, lifetime.Token);
            return await oncePump.RunDirectAsync().ConfigureAwait(false);
        }

        return await RunHookedAsync(hotkey, orchestrator, overlay, voice, tutor, options, lifetime).ConfigureAwait(false);
    }

    /// <summary>
    /// Install the hook, let it drive sessions, and shut down in a fixed order.
    /// </summary>
    /// <remarks>
    /// The setup order here mirrors <c>HotkeySmoke</c>: subscribe, then start, then report. The teardown
    /// order is stricter than that host's, because there is now work in flight to account for — stop the
    /// trigger source, cancel the lifetime token, wait (bounded) for the session to unwind, and only then
    /// let the <c>using</c> disposals run. Without the wait, a session could finish resolving and call
    /// into an overlay that had already been disposed.
    /// </remarks>
    private static async Task<int> RunHookedAsync(
        HotkeyMonitor hotkey,
        ContextOrchestratorService orchestrator,
        ScreenOverlayService overlay,
        VoiceService voice,
        TutorService? tutor,
        ShellOptions options,
        CancellationTokenSource lifetime)
    {
        var pump = new TriggerPump(voice, orchestrator, overlay, tutor, options, lifetime.Token);

        // Press starts the utterance; release ends it. Both handlers only flip state under the pump's
        // gate and hand work to the pool — the hook callback and the monitor's worker thread are never
        // blocked by a microphone, a Gateway socket, or a capture (DEC-012's non-blocking stance, now
        // spanning a whole voice turn instead of one session).
        hotkey.Pressed += (_, _) => pump.OnPressed();
        hotkey.Released += (_, _) => pump.OnReleased();

        try
        {
            hotkey.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FATAL: the keyboard hook could not be installed: {ex.GetType().Name}: {ex.Message}");
            return 2;
        }

        // IsRunning is the "hook installed" signal; State is the chord state, which is Idle here because
        // nothing is held yet. Printing both keeps a live-but-idle monitor from reading as a failed start.
        Console.WriteLine($"hook live: IsRunning={hotkey.IsRunning} (installed), chord state={hotkey.State} (nothing held).");
        Console.WriteLine("Hold Ctrl+Alt to speak, release to submit the utterance; Ctrl+C here to stop.");
        Console.WriteLine();

        var stop = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // stay alive long enough to tear down in order and report hook health
            stop.Set();
        };

        if (options.Seconds > 0)
        {
            Console.WriteLine($"auto-stop after {options.Seconds}s");
            stop.Wait(TimeSpan.FromSeconds(options.Seconds));
        }
        else
        {
            stop.Wait();
        }

        Console.WriteLine();
        Console.WriteLine("stopping...");

        hotkey.Stop();
        lifetime.Cancel();
        var sessionCode = await pump.DrainAsync().ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine("--- hotkey health ---");
        Console.WriteLine($"hook callbacks      : {hotkey.HookCallbackCount}");
        Console.WriteLine($"events not queued   : {hotkey.DroppedEventCount}");
        Console.WriteLine($"max callback latency: {hotkey.MaxCallbackLatency.TotalMilliseconds:F4} ms  (the OS unhooks above ~300 ms)");
        Console.WriteLine($"last error          : {hotkey.LastError?.Message ?? "none"}");
        Console.WriteLine($"still running       : {hotkey.IsRunning} (must be False: the hook is released before teardown)");

        if (sessionCode != 0)
        {
            // Reported, not propagated: a deliberate Ctrl+C is a clean stop, and returning the session's
            // code here would claim the session died of something the operator did not do.
            Console.WriteLine($"note: the session interrupted by shutdown reported exit {sessionCode} (see above).");
        }

        return 0;
    }
}
