using HeyGanesh.ElementSelection;
using HeyGanesh.ScreenCapture;

namespace HeyGanesh.Tools.ElementSelectionSmoke;

// Helpers for the Element Selection smoke host, part 2: result printing and annotation artifact.

internal static partial class ElementSelectionSmokeHost
{
    private static void PrintResult(SelectionResult r, HostOptions options)
    {
        Console.WriteLine("=== Element Selection result ===");
        Console.WriteLine($"  question    : {options.Question}");
        if (r.Target is { } target)
        {
            Console.WriteLine($"  window      : '{target.Title}' hwnd=0x{target.Handle:X}");
        }
        Console.WriteLine($"  enumerated  : total={r.TotalEnumerated} interactive={r.TotalAfterFilter} capped={r.Candidates.Count} (cap={options.MaxCandidates})");
        if (r.Failed)
        {
            Console.WriteLine($"  FAILURE     : {r.FailureReason}");
        }
        Console.WriteLine($"  found       : {r.Found}");
        // Printed whenever the model made a choice, NOT only when Found: the honest no-match
        // ("none") and the hallucination-guard refusal both carry a raw id worth seeing, and those
        // are exactly the paths a future confidence-gate debugger needs to tell apart.
        if (r.Choice is not null)
        {
            Console.WriteLine($"  choice      : {r.Choice}");
        }
        if (r.Found)
        {
            Console.WriteLine($"  selected    : {r.Selected}");
            Console.WriteLine($"  boxOnScreen : {r.BoxOnScreen} (global physical px)");
            Console.WriteLine($"  center      : {r.CenterOnScreen} (global physical px)");
        }
        Console.WriteLine($"  confidence  : {r.Confidence}  (jev={r.JevConfidence}, winner={r.WinnerProbability}, none={r.NoneProbability}, plausible(noul)={r.PlausibleProbability})");
        if (r.Probabilities is { } probs && probs.Count > 0)
        {
            var top = string.Join(", ", probs.OrderByDescending(p => p.Value).Take(6).Select(p => $"{p.Key}={p.Value:F2}"));
            Console.WriteLine($"  probabilities: {top}");
        }
        Console.WriteLine($"  model       : {r.ModelId}");
        Console.WriteLine($"  timings     : latency={r.Latency.TotalMilliseconds:F0}ms  inputTok={r.InputTokens} outputTok={r.OutputTokens}");
        Console.WriteLine("  candidates (capped manifest, ranked):");
        var shown = 0;
        foreach (var candidate in r.Candidates)
        {
            if (shown++ >= 8)
            {
                Console.WriteLine($"    ... (+{r.Candidates.Count - 8} more)");
                break;
            }
            Console.WriteLine($"    {candidate.Id} {candidate}");
        }
    }

    private static async Task AnnotateAsync(SelectionResult result, string outDir)
    {
        using var captureSvc = new ScreenCaptureService();
        var monitors = captureSvc.EnumerateMonitors();

        // The chosen box lives on some monitor; capture that one (cursor's monitor as fallback).
        var center = result.CenterOnScreen!.Value;
        var monitor = monitors.FirstOrDefault(m =>
                center.X >= m.Bounds.X && center.X < m.Bounds.Right &&
                center.Y >= m.Bounds.Y && center.Y < m.Bounds.Bottom)
            ?? captureSvc.GetCursorMonitor()
            ?? monitors.FirstOrDefault();
        if (monitor is null)
        {
            Console.WriteLine("(cannot annotate: no monitor was enumerated)");
            return;
        }

        var capture = await captureSvc.CaptureMonitorAsync(monitor);
        var capturePath = Path.Combine(outDir, "capture.jpg");
        await File.WriteAllBytesAsync(capturePath, capture.Jpeg);

        // Screen px -> capture-local px: both sides are the monitor's physical-pixel space.
        var box = result.BoxOnScreen!.Value;
        var annotated = await Annotator.AnnotateAsync(
            capture.Jpeg,
            box.X - monitor.Bounds.X, box.Y - monitor.Bounds.Y, box.Width, box.Height,
            capture.SourceWidth, capture.SourceHeight);
        var annotatedPath = Path.Combine(outDir, "annotated.jpg");
        await File.WriteAllBytesAsync(annotatedPath, annotated);
        Console.WriteLine($"wrote {annotatedPath} (box + crosshair on the capture of {monitor.DeviceName}); also {capturePath}");
    }
}
