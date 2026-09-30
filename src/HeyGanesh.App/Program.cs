using HeyGanesh.App;

// DPI awareness is process-global, irreversible, and owned by the host — so it is set here, before
// anything else runs. Element Selection reports UIA rectangles, Vision reports back-mapped boxes and
// Screen Capture reports monitor bounds, all in global physical pixels; those three only line up with
// each other (and with where the overlay places its windows) under PerMonitorV2.
//
// --no-dpi deliberately skips this. Awareness cannot be unset later, and the Screen Overlay refuses to
// draw rather than place a window in virtualised coordinates, so skipping the call is the only way to
// reach the overlay's "not-per-monitor-dpi-aware" gate from a real host and prove it is not a branch
// that only looks right.
if (!args.Contains("--no-dpi", StringComparer.Ordinal))
{
    NativeFns.SetProcessDpiAwarenessContext(NativeFns.PerMonitorV2);
}

return await ShellHost.RunAsync(args);
