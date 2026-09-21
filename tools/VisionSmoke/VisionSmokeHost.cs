using System.Runtime.InteropServices;
using HeyGanesh.ScreenCapture;
using HeyGanesh.Vision;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace HeyGanesh.Tools.VisionSmoke;

/// <summary>Per-monitor-v2 DPI awareness must be enabled by the host before any capture (physical px).</summary>
internal static class NativeFns
{
    internal static readonly IntPtr PerMonitorV2 = new(-4); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

    [DllImport("user32.dll")]
    internal static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
}

/// <summary>Verification host for the Vision / Coordinate Detection module.</summary>
internal static partial class VisionSmokeHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = ParseArgs(args);
        if (options.Help || string.IsNullOrWhiteSpace(options.Question))
        {
            PrintUsage();
            return 0;
        }

        Directory.CreateDirectory(options.OutDir);
        Console.WriteLine("Vision verification host (.NET 8 + Windows.Graphics.Imaging)");
        Console.WriteLine($"  question : {options.Question}");
        Console.WriteLine($"  samples  : {options.Samples}");
        Console.WriteLine($"  detail   : {options.Detail}");
        var keyPresent = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY"));
        Console.WriteLine($"  api key  : {(keyPresent ? "present (value suppressed)" : "MISSING -- set DEEPSEEK_API_KEY")}");
        Console.WriteLine($"  out dir  : {options.OutDir}");
        Console.WriteLine();

        using VisionService vision = new(client: null, options: new VisionOptions
        {
            MaxSamples = options.Samples,
            ImageDetail = options.Detail,
        });

        // --image: offline mode (no capture, no DPI dependence).
        if (!string.IsNullOrWhiteSpace(options.ImagePath))
            return await RunOfflineAsync(vision, options);

        if (!ScreenCaptureService.IsCaptureSupported())
        {
            Console.WriteLine("RESULT: GraphicsCaptureSession.IsSupported() == false on this device; cannot capture.");
            return 0;
        }

        using var captureSvc = new ScreenCaptureService(new ScreenCaptureOptions { MaxDimension = options.MaxDim });
        var monitors = captureSvc.EnumerateMonitors();
        var target = captureSvc.GetCursorMonitor() ?? monitors.FirstOrDefault()
            ?? throw new InvalidOperationException("No monitors were enumerated.");
        Console.WriteLine($"capturing {target.DeviceName} ({target.Bounds}) at max {options.MaxDim}px...");
        var capture = await captureSvc.CaptureMonitorAsync(target);
        var capturePath = Path.Combine(options.OutDir, "capture.jpg");
        await File.WriteAllBytesAsync(capturePath, capture.Jpeg);
        Console.WriteLine($"wrote {capturePath} ({capture.Jpeg.Length / 1024} KB, nonBlack={capture.NonBlackPixelRatio:P1})");
        Console.WriteLine();
        return await RunVisionAsync(vision, capture, options.Question, options.OutDir);
    }

    private static HostOptions ParseArgs(string[] args)
    {
        var question = ""; var image = ""; int samples = 2, maxDim = 1280; var detail = "high";
        var outDir = Path.Combine(AppContext.BaseDirectory, "out"); var help = false; var fake = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--question": question = args[++i]; break;
                case "--image": image = args[++i]; break;
                case "--samples": samples = int.Parse(args[++i]); break;
                case "--max-dim": maxDim = int.Parse(args[++i]); break;
                case "--detail": detail = args[++i]; break;
                case "--out": outDir = args[++i]; break;
                case "--fake": fake = true; break;
                case "--help": case "-h": case "/?": help = true; break;
                default: Console.WriteLine($"ignoring unknown argument '{args[i]}'"); break;
            }
        }
        return new HostOptions(question, image.Length > 0 ? image : null, samples, maxDim, detail, outDir, help, fake);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Vision smoke host");
        Console.WriteLine("Usage: VisionSmoke --question \"<text>\" [--image <path.jpg>] [--samples <n>]");
        Console.WriteLine("                   [--max-dim <px>] [--detail high|low|auto] [--out <dir>] [--help]");
        Console.WriteLine();
        Console.WriteLine("--image   : offline mode -- locate in a JPEG on disk instead of capturing.");
        Console.WriteLine("--samples : per-question samples for the consistency signal (default 2).");
        Console.WriteLine("--max-dim : capture downscale cap, px (default 1280).");
        Console.WriteLine("--detail  : provider image detail flag (default high).");
        Console.WriteLine("--out     : output directory (default ./out).");
        Console.WriteLine("--fake    : force the offline fake client (no network, no key, fixed box).");
        Console.WriteLine("Set DEEPSEEK_API_KEY in the environment. The host never echoes it.");
    }

    private sealed record HostOptions(string Question, string? ImagePath, int Samples, int MaxDim, string Detail, string OutDir, bool Help, bool Fake);
}

