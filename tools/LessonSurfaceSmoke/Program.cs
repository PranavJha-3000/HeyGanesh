using HeyGanesh.Tools.LessonSurfaceSmoke;

// PerMonitorV2 before any window exists: card and overlay coordinates are physical pixels.
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
return await LessonSurfaceSmokeHost.RunAsync(args);
