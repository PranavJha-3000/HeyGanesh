using System.Runtime.InteropServices;
using HeyGanesh.ContextOrchestrator;
using HeyGanesh.ElementSelection;
using HeyGanesh.ScreenCapture;
using HeyGanesh.Vision;

namespace HeyGanesh.Tools.ContextOrchestratorSmoke;

/// <summary>Per-monitor-v2 DPI awareness must be enabled by the host before any coordinate work (physical px).</summary>
internal static class NativeFns
{
    internal static readonly IntPtr PerMonitorV2 = new(-4); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

    [DllImport("user32.dll")]
    internal static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
}

/// <summary>
/// Verification host for the Context Orchestrator. Its job is the composition, not the legs: one
/// capture, Element Selection answering first, Vision reached only on a genuine primary failure, and
/// the honest no-match ("none") never reaching Vision at all. The legs themselves are verified by
/// ElementSelectionSmoke and VisionSmoke; here they are driven through offline clients so the
/// orchestration can be exercised deterministically.
/// </summary>
internal static partial class ContextOrchestratorSmokeHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length > 0 && args[0] == "--assert-check")
        {
            return RunAssertCheck();
        }

        var options = ParseArgs(args);
        if (options.Help || string.IsNullOrWhiteSpace(options.Question))
        {
            PrintUsage();
            return 0;
        }

        PrintHeader(options);

        if (!ScreenCaptureService.IsCaptureSupported())
        {
            Console.WriteLine("RESULT: GraphicsCaptureSession.IsSupported() == false on this device; cannot capture.");
            return 0;
        }

        // The fallback leg's client counts its calls. That count is the assertion this host exists for:
        // "none" must never reach Vision, and a genuine primary failure must reach it exactly once.
        var visionClient = new CountingVisionClient(options.VisionNotFound);

        using var captureService = new ScreenCaptureService(new ScreenCaptureOptions { MaxDimension = options.MaxDim });
        using var selectionService = new ElementSelectionService(
            client: options.FakeJevMode is { } mode ? new FakeJevClient(mode, options.FakeDelayMs) : null,
            options: new SelectionOptions { MaxCandidates = options.MaxCandidates });
        using var visionService = new VisionService(
            client: options.Live ? null : visionClient,
            // One sample keeps the call count exact (the consistency signal is not what this host tests).
            options: new VisionOptions { MaxSamples = 1 });
        using var orchestrator = new ContextOrchestratorService(
            captureService,
            selectionService,
            visionService,
            new ContextOrchestratorOptions
            {
                Target = options.Foreground ? SelectionTarget.ForegroundWindow : null,
                RetainCaptures = options.RetainCaptures,
            });

        return options.Burst
            ? await RunBurstAsync(orchestrator, options, visionClient)
            : await RunSingleAsync(orchestrator, options, visionClient);
    }
}
