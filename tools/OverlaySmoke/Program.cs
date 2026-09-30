using HeyGanesh.Tools.OverlaySmoke;

// DPI awareness is a process-global property owned by the host; set it first so the canned box this
// host builds and the monitor bounds the overlay reads are the same physical-pixel space (DEC-004 —
// the overlay checks awareness but never sets it).
NativeFns.SetProcessDpiAwarenessContext(NativeFns.PerMonitorV2);

return await OverlaySmokeHost.RunAsync(args);