using HeyGanesh.Tools.ElementSelectionSmoke;
using HeyGanesh.Abstractions;

// DPI awareness is a process-global property owned by the host; set it first so GetCursorPos /
// GetWindowRect return physical pixels that line up with UIA's BoundingRectangle and the capture
// module's MonitorInfo.Bounds (the cross-check printed by the host proves this every run).
NativeFns.SetProcessDpiAwarenessContext(NativeFns.PerMonitorV2);

return await ElementSelectionSmokeHost.RunAsync(args);
