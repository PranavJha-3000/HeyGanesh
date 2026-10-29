using HeyGanesh.Abstractions;
using HeyGanesh.LessonSurface;
using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.Tools.LessonSurfaceSmoke;

/// <summary>
/// Drives the real card surface on the real desktop. Exit codes: 0 pass, 1 failed check or fault,
/// 2 usage. Rows need an interactive session (windows are created and shown); none needs a key.
/// </summary>
internal static partial class LessonSurfaceSmokeHost
{
    private static readonly string[] GoldenFixtures = { "chain-rule.json", "tensor-2d.json", "photosynthesis.json" };

    public static async Task<int> RunAsync(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "--help";
        Console.WriteLine("Lesson Surface verification host (real WebView2 cards, golden fixtures, no key)");
        Console.WriteLine();
        try
        {
            return mode switch
            {
                "--assert-source" => RunAssertSource(),
                "--assert-window" => await RunAssertWindowAsync(),
                "--assert-render" => await RunAssertRenderAsync(),
                "--assert-security" => await RunAssertSecurityAsync(),
                "--assert-marks" => await RunAssertMarksAsync(),
                "--assert-scene-math" => RunAssertSceneMath(),
                "--assert-scene" => await RunAssertSceneAsync(),
                "--assert-scene-idle" => await RunAssertSceneIdleAsync(),
                "--assert-orientation" => await RunAssertOrientationAsync(),
                "--assert-scene-nowebgl" => await RunAssertSceneNoWebGlAsync(),
                "--assert-pick" => await RunAssertPickAsync(),
                "--assert-scene-svg" => await RunAssertSceneSvgAsync(),
                "--snapshot-3d" => await RunSnapshot3dAsync(args.Skip(1).ToArray()),
                "--demo-3d" => await RunDemo3dAsync(args.Skip(1).ToArray()),
                "--demo" => await RunDemoAsync(args.Skip(1).ToArray()),
                "--help" or "-h" or "/?" => PrintUsage(),
                _ => Usage(mode),
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"UNEXPECTED: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static int Usage(string bad)
    {
        Console.WriteLine($"unknown argument '{bad}' (exit 2)");
        return 2;
    }

    private static int PrintUsage()
    {
        Console.WriteLine("Usage: LessonSurfaceSmoke <mode>");
        Console.WriteLine("  --assert-source     one PackageReference (WebView2), page has a CSP and no HTML sinks, exit 0");
        Console.WriteLine("  --assert-window     no-activate, top-most, capture-excluded cards; timeout; dispose mid-lesson, exit 0");
        Console.WriteLine("  --assert-render     golden fixtures drawn: ids, exact texts, arrows, highlights, plot points, exit 0");
        Console.WriteLine("  --assert-security   model HTML rendered literally; navigation and network blocked, exit 0");
        Console.WriteLine("  --assert-marks      screen marks reach the native overlay; back-to-back marks, exit 0");
        Console.WriteLine("  --demo [chain-rule|tensor-2d|photosynthesis|all] [--voice sapi|timed]");
        Console.WriteLine("                      plays lessons on screen for a human to watch (manual row)");
        Console.WriteLine("3D scene rows (DEC-030):");
        Console.WriteLine("  --assert-scene-math     quaternions = Geometry.Rotate, key densification, model messages (offline), exit 0");
        Console.WriteLine("  --assert-scene          the engine lesson on the real scene window: every op reflected, window hygiene, exit 0");
        Console.WriteLine("  --assert-scene-idle     frames only while something moves (render on demand), exit 0");
        Console.WriteLine("  --assert-orientation    +X right, +Y up, Z turn from real pixels, exit 0");
        Console.WriteLine("  --assert-scene-nowebgl  without WebGL the lesson is drawn as an SVG and spoken, exit 0");
        Console.WriteLine("  --assert-scene-svg      the no-WebGL drawing: pixel orientation, depth order, literal labels, highlight, explode, speed, exit 0");
        Console.WriteLine("  --assert-pick           real mouse clicks on engine parts -> exactly that part; guards; close (moves the cursor), exit 0");
        Console.WriteLine("  --snapshot-3d <dir> [--no-webgl]  PNG of every library model, rest and exploded; --no-webgl: the drawing (manual)");
        Console.WriteLine("  --demo-3d [--voice sapi|timed]  the engine lesson drawn and spoken (manual row)");
        return 0;
    }

    private static int Check(bool ok, string label, int failures)
    {
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {label}");
        return ok ? failures : failures + 1;
    }

    private static int Summarize(int failures, string okLabel)
    {
        Console.WriteLine(failures == 0 ? $"{okLabel} (exit 0)" : $"FAILED: {failures} check(s) (exit 1)");
        return failures == 0 ? 0 : 1;
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    private static PixelRect VirtualDesktop()
    {
        var v = SystemInformation.VirtualScreen;
        return new PixelRect(v.X, v.Y, v.Width, v.Height);
    }

    private static LessonValidationResult Validated(VisualPlan plan) =>
        new VisualPlanValidator().Validate(new ParseResult(plan, Array.Empty<LessonIssue>()), VirtualDesktop());

    private static VisualPlan Plan(string fixture) => VisualPlanParser.Parse(Fixture(fixture)).Plan!;

    private static VisualPlan AppendOp(VisualPlan plan, int beat, LessonOp op) =>
        plan with { Beats = plan.Beats.Select((b, i) => i == beat ? b with { Ops = b.Ops.Append(op).ToList() } : b).ToList() };

    /// <summary>Speech stand-in: a short pause per sentence so cards settle between beats.</summary>
    private sealed class PauseSpeaker : ILessonSpeaker
    {
        private readonly int _ms;
        private readonly Action<string>? _onSpeak;

        public PauseSpeaker(int ms, Action<string>? onSpeak = null)
        {
            _ms = ms;
            _onSpeak = onSpeak;
        }

        public async Task SpeakAsync(string text, CancellationToken cancellationToken)
        {
            _onSpeak?.Invoke(text);
            await Task.Delay(_ms, cancellationToken);
        }
    }
}
