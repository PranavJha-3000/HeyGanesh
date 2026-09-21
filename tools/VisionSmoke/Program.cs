using HeyGanesh.Tools.VisionSmoke;

// DPI awareness is a process-global property owned by the host; set it first so the physical-pixel
// coordinates Vision returns line up with capture frames on this 150% DPI display.
NativeFns.SetProcessDpiAwarenessContext(NativeFns.PerMonitorV2);

return await VisionSmokeHost.RunAsync(args);
