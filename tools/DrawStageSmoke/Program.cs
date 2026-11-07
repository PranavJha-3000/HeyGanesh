using HeyGanesh.Tools.DrawStageSmoke;

// PerMonitorV2 before any window exists: the stage's frame and the cursor are physical pixels.
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
return DrawStageSmokeHost.Run(args);
