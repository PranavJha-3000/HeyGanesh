using HeyGanesh.GlobalHotkeyMonitor;

// Manual verification harness for the Global Hotkey Monitor module.
//
// Usage:  dotnet run --project tools/HotkeySmoke -c Debug [log-path]
//
// It prints (and appends to a log file) one line per Pressed/Released event, so the operator
// can focus Notepad, hold the chord, and read the log from a second terminal. The harness never
// takes focus, never suppresses keys, and reports hook-callback health on exit.

const string HookTimeoutNote = "~300 ms (LowLevelHooksTimeout)";

// Arguments: [--seconds=N] [logPath]
// --seconds makes the run self-terminating so the harness can also be run unattended;
// without it the harness runs until Ctrl+C.
var seconds = 0;
string? logPathArgument = null;

foreach (var argument in args)
{
    if (argument.StartsWith("--seconds=", StringComparison.Ordinal)
        && int.TryParse(argument["--seconds=".Length..], out var parsedSeconds))
    {
        seconds = parsedSeconds;
    }
    else
    {
        logPathArgument = argument;
    }
}

var logPath = logPathArgument is null
    ? Path.Combine(AppContext.BaseDirectory, "smoke.log")
    : Path.GetFullPath(logPathArgument);

using var log = new StreamWriter(logPath, append: true) { AutoFlush = true };
var logLock = new object();

void Write(string message)
{
    var line = $"{DateTime.Now:HH:mm:ss.fff}  {message}";
    Console.WriteLine(line);
    lock (logLock)
    {
        log.WriteLine(line);
    }
}

Write("=== HeyGanesh Global Hotkey Monitor smoke test ===");
Write($"log file: {logPath}");
Write("Chord: hold LEFT Ctrl + LEFT Alt. Nothing is swallowed: type freely, keys still land.");

using var monitor = new GlobalHotkeyMonitor();

monitor.Pressed += (_, _) => Write($"Pressed    state={monitor.State}");
monitor.Released += (_, _) => Write($"Released   state={monitor.State}");

try
{
    monitor.Start();
}
catch (Exception ex)
{
    Write($"START FAILED: {ex.GetType().Name}: {ex.Message}");
    return 1;
}

Write($"hook live. state={monitor.State}. Focus another app and hold the chord. (Ctrl+C here to stop)");

var stop = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // stay alive long enough to report
    stop.Set();
};

if (seconds > 0)
{
    Write($"auto-stop after {seconds}s");
    stop.Wait(TimeSpan.FromSeconds(seconds));
}
else
{
    stop.Wait();
}

Write("stopping...");
monitor.Stop();

Write("--- summary ---");
Write($"hook callbacks seen : {monitor.HookCallbackCount}");
Write($"events not queued   : {monitor.DroppedEventCount}");
Write($"max callback latency: {monitor.MaxCallbackLatency.TotalMilliseconds:F4} ms  (OS removes hooks above {HookTimeoutNote})");
Write($"last error          : {monitor.LastError?.Message ?? "none"}");
Write($"final state         : {monitor.State}");

return 0;
