using HeyGanesh.LessonSurface;
using HeyGanesh.ScreenOverlay;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.Tools.LessonSurfaceSmoke;

/// <summary>
/// LSU-6, the demo (manual row): the golden lessons drawn on screen and spoken, for a human to
/// watch. "sapi" uses the voice built into Windows through COM late binding — host-only, no
/// package, no key; "timed" is silent and waits about as long as speaking would take.
/// </summary>
internal static partial class LessonSurfaceSmokeHost
{
    private static async Task<int> RunDemoAsync(string[] args)
    {
        var which = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "all";
        var voiceIndex = Array.IndexOf(args, "--voice");
        var voice = voiceIndex >= 0 && voiceIndex + 1 < args.Length ? args[voiceIndex + 1] : "sapi";
        var snapIndex = Array.IndexOf(args, "--snapshot");
        var snapshotDir = snapIndex >= 0 && snapIndex + 1 < args.Length ? args[snapIndex + 1] : null;
        var lessons = which == "all" ? GoldenFixtures : new[] { which.EndsWith(".json", StringComparison.Ordinal) ? which : which + ".json" };

        ILessonSpeaker speaker = voice == "sapi" && SapiSpeaker.TryCreate(out var sapi) ? sapi! : new TimedSpeaker();
        Console.WriteLine($"voice   : {(speaker is SapiSpeaker ? "Windows SAPI" : "timed (silent)")}");

        using var overlay = new ScreenOverlayService();
        await using var surface = await CardSurface.CreateAsync(marks: overlay);
        var player = new LessonPlayer(surface, speaker);
        foreach (var name in lessons)
        {
            var lesson = Validated(Plan(name));
            Console.WriteLine($"lesson  : {name} ({lesson.Outcome}, {lesson.Plan?.Beats.Count ?? 0} beats)");
            var report = await player.PlayAsync(lesson);
            Console.WriteLine($"          {report.Status}, {report.Beats.Count} beats played");
            await Task.Delay(1200);
            if (snapshotDir is not null && lesson.Plan is not null)
            {
                Directory.CreateDirectory(snapshotDir);
                foreach (var s in lesson.Plan.Surfaces)
                {
                    var path = Path.Combine(snapshotDir, $"{Path.GetFileNameWithoutExtension(name)}-{s.Id}.png");
                    await using (var file = File.Create(path))
                    {
                        await surface.CapturePageAsync(s.Id, file);
                    }

                    Console.WriteLine($"          snapshot {path}");
                }
            }

            await Task.Delay(1300);
        }

        await surface.HideAsync();
        Console.WriteLine("DEMO DONE (exit 0) — a human judges what was drawn and said");
        return 0;
    }

    /// <summary>Silent stand-in: waits roughly as long as the sentence would take to say.</summary>
    private sealed class TimedSpeaker : ILessonSpeaker
    {
        public Task SpeakAsync(string text, CancellationToken cancellationToken) =>
            Task.Delay(Math.Clamp(text.Length * 60, 600, 8000), cancellationToken);
    }

    /// <summary>
    /// The Windows SAPI voice (SAPI.SpVoice) via COM late binding. Speaks asynchronously and polls
    /// for the end so cancellation can purge the sentence. Demo-host only: product speech is Voice.
    /// </summary>
    private sealed class SapiSpeaker : ILessonSpeaker
    {
        private const int SvsfAsync = 1, SvsfPurgeBeforeSpeak = 2;
        private readonly dynamic _voice;

        private SapiSpeaker(object voice) => _voice = voice;

        public static bool TryCreate(out SapiSpeaker? speaker)
        {
            speaker = null;
            var type = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (type is null)
            {
                return false;
            }

            speaker = new SapiSpeaker(Activator.CreateInstance(type)!);
            return true;
        }

        public async Task SpeakAsync(string text, CancellationToken cancellationToken)
        {
            _voice.Speak(text, SvsfAsync);
            try
            {
                while (!(bool)_voice.WaitUntilDone(0))
                {
                    await Task.Delay(40, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                _voice.Speak(string.Empty, SvsfAsync | SvsfPurgeBeforeSpeak);
                throw;
            }
        }
    }
}
