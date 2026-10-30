using HeyGanesh.App;
using HeyGanesh.Tools.TraySmoke;

// TY-3 runs the real shell in this process, and the shell's overlay refuses to draw without PerMonitorV2, so the
// host sets it first, as both real entry points do.
NativeFns.SetProcessDpiAwarenessContext(NativeFns.PerMonitorV2);
return await TraySmokeHost.RunAsync(args);
