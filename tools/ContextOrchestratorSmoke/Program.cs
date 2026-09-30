using HeyGanesh.Tools.ContextOrchestratorSmoke;

// DPI awareness is a process-global property owned by the host; set it first so every coordinate the
// orchestrator reports (UIA rectangles from Element Selection, back-mapped boxes from Vision) is in
// physical pixels and lines up with the capture module's MonitorInfo.Bounds.
NativeFns.SetProcessDpiAwarenessContext(NativeFns.PerMonitorV2);

return await ContextOrchestratorSmokeHost.RunAsync(args);
