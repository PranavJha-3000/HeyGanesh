using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using HeyGanesh.LessonSurface;
using HeyGanesh.PartLibrary;
using HeyGanesh.PartLibrary.Models;
using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.Tools.LessonSurfaceSmoke;

/// <summary>
/// LSU-7..LSU-14, the 3D scene rows (FEATURE-007 Phase 5 step 4, DEC-030). LSU-7/LSU-8 are offline
/// (CI G-17); the rest drive the real scene window on this desktop. Expectations come from the plan
/// and the Part Library, so the rows check the renderer, not a transcript.
/// </summary>
internal static partial class LessonSurfaceSmokeHost
{
    private const string EnginePlanFixture = "engine-3d.json";

    /// <summary>The LessonSmoke golden engine lesson, pointed at the real library model.</summary>
    private static LessonValidationResult EngineLesson() => Validated3d(Fixture(EnginePlanFixture).Replace("\"engine-test\"", $"\"{FourStrokeEngine.Id}\"", StringComparison.Ordinal));

    private static LessonValidationResult Validated3d(string json) =>
        new VisualPlanValidator().Validate(VisualPlanParser.Parse(json), new ValidationContext(VirtualDesktop()) { Scene3d = CardSurface.Scene3dCatalog });

    /// <summary>A one-scene lesson: place <paramref name="model"/>, then whatever extra ops the row needs.</summary>
    private static string SceneLesson(string model, params string[] extraOps)
    {
        var beats = new List<string>
        {
            $$"""{ "id": "b1", "say": "Here is the model.", "ops": [ { "op": "model", "surface": "scene", "id": "m", "model": "{{model}}" } ] }""",
        };
        beats.AddRange(extraOps.Select((op, i) => $$"""{ "id": "b{{i + 2}}", "say": "And now this step.", "ops": [ {{op}} ] }"""));
        return $$"""
            { "schema": "heyganesh.visualplan/1.1", "lessonId": "scene-row", "title": "scene row", "mode": "explain",
              "surfaces": [ { "id": "scene", "kind": "scene3d", "title": "{{model}}" } ],
              "beats": [ {{string.Join(", ", beats)}} ] }
            """;
    }

    // ---- LSU-7 additions to the source audit (called from RunAssertSource) ----------------------

    private static int CheckSceneSource(string root, string dir, int failures)
    {
        var html = File.ReadAllText(Path.Combine(dir, "Page", "scene.html"));
        var js = File.ReadAllText(Path.Combine(dir, "Page", "scene.js"));
        var svgJs = File.ReadAllText(Path.Combine(dir, "Page", "scene-svg.js"));
        failures = Check(
            html.Contains("Content-Security-Policy", StringComparison.Ordinal) && html.Contains("default-src 'self'", StringComparison.Ordinal)
                && html.Contains("connect-src 'none'", StringComparison.Ordinal) && !html.Contains("unsafe-eval", StringComparison.Ordinal) && !html.Contains("unsafe-inline", StringComparison.Ordinal),
            "scene.html carries the same CSP: default-src 'self', connect-src 'none', no unsafe-eval / unsafe-inline", failures);
        string[] sinks = { "innerHTML", "outerHTML", "insertAdjacentHTML", "document.write", "eval(", "new Function", "setTimeout(\"", "setInterval(\"" };
        var found = sinks.Where(s => js.Contains(s, StringComparison.Ordinal) || svgJs.Contains(s, StringComparison.Ordinal)).ToArray();
        failures = Check(found.Length == 0, $"scene.js and scene-svg.js have no HTML/code sinks ({(found.Length == 0 ? "none" : string.Join(", ", found))})", failures);
        failures = Check(svgJs.Contains("text.textContent = label.text;", StringComparison.Ordinal) && !Regex.IsMatch(svgJs, @"label\.text\s*[+`]|\$\{[^}]*label\.text"),
            "scene-svg.js puts label text in the drawing only through textContent, never into an SVG string (DEC-034)", failures);
        var clicks = Regex.Matches(js, @"post\(\{\s*kind:\s*""part-clicked""([^}]*)\}\)").Select(m => m.Groups[1].Value).ToList();
        failures = Check(clicks.Count == 1 && Regex.Matches(clicks[0], @"(\w+):").Select(m => m.Groups[1].Value).SequenceEqual(new[] { "instance", "part" }),
            "scene.js posts a part click as two ids only (instance, part) -- no text the host could mistake for a question (DEC-031)", failures);

        var vendor = Path.Combine(dir, "Page", "vendor");
        var table = File.ReadAllText(Path.Combine(vendor, "VENDOR.md"));
        var pins = Regex.Matches(table, @"\|\s*`((?:babylonjs|anatomy)-[^`]+)`\s*\|[^|]*\|[^|]*\|\s*`([0-9a-f]{64})`")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);
        failures = Check(pins.Count == 8, $"VENDOR.md pins 8 files ({string.Join(", ", pins.Keys)})", failures);
        foreach (var (file, pinned) in pins)
        {
            var path = Path.Combine(vendor, file.Replace('/', Path.DirectorySeparatorChar));
            var actual = File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() : "missing";
            failures = Check(actual == pinned, $"{file}: SHA-256 matches VENDOR.md ({actual[..Math.Min(12, actual.Length)]}…)", failures);
        }

        // Apache-2.0 §4: the licence and the project's NOTICE travel with the redistributed files.
        failures = Check(pins.Keys.Any(k => k.EndsWith("LICENSE.md", StringComparison.Ordinal)) && File.ReadAllText(Path.Combine(vendor, "babylonjs-9.30.0", "LICENSE.md")).Contains("Apache License", StringComparison.Ordinal)
                && pins.Keys.Any(k => k.EndsWith("NOTICE.md", StringComparison.Ordinal)) && File.ReadAllText(Path.Combine(vendor, "babylonjs-9.30.0", "NOTICE.md")).Contains("Babylon.js", StringComparison.Ordinal),
            "the Apache-2.0 licence and Babylon's NOTICE ship with the vendored files", failures);
        failures = Check(pins.Keys.Any(k => k == "anatomy-1482e846/LICENSE") && File.ReadAllText(Path.Combine(vendor, "anatomy-1482e846", "LICENSE")).Contains("MIT License", StringComparison.Ordinal),
            "Anatomy's MIT licence ships with its vendored kit (DEC-034)", failures);
        var attributes = File.ReadAllText(Path.Combine(root, ".gitattributes"));
        failures = Check(attributes.Contains("src/Experience/HeyGanesh.LessonSurface/Page/vendor/** -text", StringComparison.Ordinal),
            ".gitattributes keeps git from rewriting the vendored files' line endings", failures);
        return failures;
    }

    // ---- LSU-8: the maths, offline -----------------------------------------------------------

    private static int RunAssertSceneMath()
    {
        var failures = 0;
        var random = new Random(7);
        double R(double span) => (random.NextDouble() * 2 - 1) * span;
        double worst = 0, worstNorm = 0;
        for (var i = 0; i < 500; i++)
        {
            var rotation = new Vec3(R(360), R(360), R(360));
            var v = new Vec3(R(3), R(3), R(3));
            var q = SceneMessages.ToQuaternion(rotation);
            worst = Math.Max(worst, (SceneMessages.Rotate(q, v) - Geometry.Rotate(v, rotation)).Length);
            worstNorm = Math.Max(worstNorm, Math.Abs(Math.Sqrt(q.Sum(c => c * c)) - 1));
        }

        failures = Check(worst < 1e-12 && worstNorm < 1e-12, $"ToQuaternion rotates 500 random vectors exactly as Geometry.Rotate (worst {worst:E1}, unit-norm error {worstNorm:E1})", failures);

        var gapsOk = true;
        var hemisphereOk = true;
        foreach (var model in ModelLibrary.V1)
        {
            foreach (var track in model.Animations.SelectMany(a => a.Tracks).Where(t => t.Property == TrackProperty.RotationDeg))
            {
                var keys = SceneMessages.RotationKeys(track.Keys);
                for (var i = 1; i < keys.Length; i++)
                {
                    var dot = keys[i - 1].V.Zip(keys[i].V, (a, b) => a * b).Sum();
                    hemisphereOk &= dot >= 0;
                    gapsOk &= 2 * Math.Acos(Math.Min(1, Math.Abs(dot))) * 180 / Math.PI <= SceneMessages.MaxRotationStepDeg + 1e-9;
                }
            }
        }

        failures = Check(gapsOk && hemisphereOk, "every rotation key sent to the page is <= 45 deg from the last and in the same hemisphere", failures);
        var spin = SceneMessages.RotationKeys(ModelLibrary.Find(DnaHelix.Id)!.Animations[0].Tracks[0].Keys);
        failures = Check(spin.Length == 9 && spin[0].T == 0 && spin[^1].T == 1, $"DNA spin 0/180/360 deg is densified to 9 keys (got {spin.Length})", failures);

        foreach (var model in ModelLibrary.V1)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(SceneMessages.Model(new ModelOp("scene", "m", model.Id))));
            var parts = doc.RootElement.GetProperty("parts");
            var primitives = parts.EnumerateArray().Sum(p => p.GetProperty("primitives").GetArrayLength());
            failures = Check(parts.GetArrayLength() == model.Parts.Count && primitives == model.Parts.Sum(p => p.Primitives.Count)
                && doc.RootElement.GetProperty("reviewed").GetBoolean() == model.Reviewed && doc.RootElement.GetProperty("schematic").GetBoolean() == model.Schematic,
                $"{model.Id}: page message carries exactly the library's {model.Parts.Count} parts / {primitives} primitives and its schematic/reviewed flags", failures);
        }

        var catalog = CardSurface.Scene3dCatalog;
        failures = Check(catalog.Models.Count == ModelLibrary.V1.Count, "CardSurface.Scene3dCatalog is the Part Library's catalogue", failures);
        return Summarize(failures, "SCENE MATH OK");
    }

    // ---- LSU-9: the engine lesson on the real scene window ---------------------------------

    private sealed class SnapshotSpeaker : ILessonSpeaker
    {
        private readonly CardSurface _surface;

        public SnapshotSpeaker(CardSurface surface) => _surface = surface;

        public List<(string Say, SceneSnapshot Scene)> Seen { get; } = new();

        public async Task SpeakAsync(string text, CancellationToken cancellationToken)
        {
            Seen.Add((text, await _surface.DescribeSceneAsync()));
            await Task.Delay(150, cancellationToken);
        }
    }

    private static async Task<int> RunAssertSceneAsync()
    {
        var failures = 0;
        var lesson = EngineLesson();
        failures = Check(lesson.Outcome == LessonOutcome.Valid, $"engine-3d.json on '{FourStrokeEngine.Id}' validates against CardSurface.Scene3dCatalog ({lesson.Outcome})", failures);
        await using var surface = await CardSurface.CreateAsync();
        var speaker = new SnapshotSpeaker(surface);
        var report = await new LessonPlayer(surface, speaker).PlayAsync(lesson);
        failures = Check(report.Status == PlaybackStatus.Completed && speaker.Seen.Count == 6, $"lesson completed, all 6 sentences spoken ({report.Status}, {speaker.Seen.Count})", failures);
        if (speaker.Seen.Count < 6)
        {
            return Summarize(failures + 1, "SCENE OK");
        }

        var s = speaker.Seen.Select(x => x.Scene).ToList();
        var engine = ModelLibrary.Find(FourStrokeEngine.Id)!;
        var instance = s[0].Instances.FirstOrDefault();
        failures = Check(s[0].WebGl == 2, $"page runs WebGL 2 (got {s[0].WebGl})", failures);
        failures = Check(instance is { Id: "engine" } && instance.Model == engine.Id && instance.Parts == engine.Parts.Count && instance.Meshes == engine.Parts.Sum(p => p.Primitives.Count),
            $"b1: instance 'engine' built with {engine.Parts.Count} parts / {engine.Parts.Sum(p => p.Primitives.Count)} meshes ({instance})", failures);
        failures = Check(s[0].CameraView == "iso" && s[0].CameraFocus == "engine", $"b1: camera iso on engine ({s[0].CameraView} on {s[0].CameraFocus})", failures);
        failures = Check(s[1].Labels.Any(l => l.Id == "l-piston" && l.Target == "engine.piston" && l.Text == "piston") && s[1].Highlights.SequenceEqual(new[] { "engine.piston" }),
            "b2: label 'piston' on engine.piston, highlight engine.piston", failures);
        failures = Check(s[2].Labels.Any(l => l.Id == "l-rod" && l.Text == "connecting rod") && s[2].Highlights.LastOrDefault() == "engine.connecting-rod",
            "b3: label 'connecting rod', highlight engine.connecting-rod", failures);
        failures = Check(s[3].Playing.Contains("engine:stroke-cycle"), $"b4: stroke-cycle playing ({string.Join(", ", s[3].Playing)})", failures);
        var rod = engine.Part("connecting-rod").ExplodeOffset;
        failures = Check(s[4].Explode.TryGetValue("engine", out var amount) && Math.Abs(amount - 0.6) < 1e-9
            && s[4].PartOffsets.TryGetValue("engine.connecting-rod", out var off) && Math.Abs(off[0] - (0.6 * rod.X)) < 1e-6 && Math.Abs(off[1] - (0.6 * rod.Y)) < 1e-6 && Math.Abs(off[2] - (0.6 * rod.Z)) < 1e-6,
            "b5: explode 0.6, connecting-rod offset = 0.6 x its library explode offset", failures);
        failures = Check(s[4].CameraView == "side" && s[4].CameraFocus == "engine.crankshaft", $"b5: camera side on engine.crankshaft ({s[4].CameraView} on {s[4].CameraFocus})", failures);
        failures = Check(s[5].Explode["engine"] == 0 && s[5].PartOffsets.Values.All(v => v.All(c => Math.Abs(c) < 1e-9)) && s[5].Highlights.LastOrDefault() == "l-rod",
            "b6: explode back to 0 (every part offset 0), highlight on label l-rod", failures);

        var end = await surface.DescribeSceneAsync();
        failures = Check(end.CspViolations == 0, $"0 CSP violations on the scene page (got {end.CspViolations})", failures);
        failures = Check(end.Tags.Contains("not yet reviewed") && !end.Tags.Contains("schematic"), $"header tags: [{string.Join(", ", end.Tags)}]", failures);
        failures = Check(end.ScriptElements == 4, $"page has exactly its 4 scripts: Babylon, GUI, scene.js, scene-svg.js (got {end.ScriptElements})", failures);
        failures = Check(end.Visible && (end.ExStyle & WsExNoActivate) != 0 && (end.ExStyle & WsExToolWindow) != 0 && (end.ExStyle & WsExTopmost) != 0,
            $"scene window visible, NOACTIVATE | TOOLWINDOW | TOPMOST (0x{end.ExStyle:X})", failures);
        failures = Check(end.DisplayAffinity == WdaExcludeFromCapture, $"scene window display affinity is WDA_EXCLUDEFROMCAPTURE (0x{end.DisplayAffinity:X})", failures);
        failures = Check(end.CornerPreference == DwmwcpRound, $"scene window corners are rounded by DWM: DWMWCP_ROUND (read back {end.CornerPreference}) (DEC-064)", failures);
        failures = Check(end.BackdropType == DwmsbtTransientWindow, $"scene window frame is glass: DWMSBT_TRANSIENTWINDOW Acrylic (read back {end.BackdropType}) (DEC-064)", failures);
        failures = Check(!surface.EverTookFocus, "no card or scene window ever became the foreground window", failures);
        var cardShown = await surface.DescribeCardAsync("card-strokes");
        failures = Check(cardShown.ElementIds.Contains("s-exhaust") && end.Bounds.X + end.Bounds.Width <= cardShown.Bounds.X,
            "the card beside the scene drew its steps, and the scene sits left of the card column", failures);

        Console.WriteLine($"          scene window cold start: {surface.LastSceneStartup.TotalMilliseconds:F0} ms (environment page + Babylon parse + shader warm-up)");
        Console.WriteLine($"          warm model beat (b1: meshes + shaders ready + first frame): {end.LastModelBuildMs:F0} ms (FEATURE-007 budget 300 ms)");
        // Read after the lesson: LessonPlayer speaks while a beat draws, so b1's own snapshot can predate its ack.
        failures = Check(end.LastModelBuildMs > 0 && end.LastModelBuildMs < 300, "warm model beat within the 300 ms budget", failures);
        return Summarize(failures, "SCENE OK");
    }

    // ---- LSU-10: render on demand ------------------------------------------------------------

    private static async Task<int> RunAssertSceneIdleAsync()
    {
        var failures = 0;
        await using var surface = await CardSurface.CreateAsync();
        var player = new LessonPlayer(surface, new PauseSpeaker(150));

        var engine = Validated3d(SceneLesson(FourStrokeEngine.Id, """{ "op": "animate", "target": "m", "animation": "stroke-cycle" }"""));
        await player.PlayAsync(engine);
        var a = await surface.DescribeSceneAsync();
        await Task.Delay(1000);
        var b = await surface.DescribeSceneAsync();
        failures = Check(b.Frames - a.Frames > 20, $"looping stroke-cycle keeps drawing ({b.Frames - a.Frames} frames in 1 s)", failures);

        var pump = Validated3d(SceneLesson(SodiumPotassiumPump.Id, """{ "op": "animate", "target": "m", "animation": "pump-cycle" }"""));
        await player.PlayAsync(pump);
        await Task.Delay(6800);
        var c = await surface.DescribeSceneAsync();
        await Task.Delay(2000);
        var d = await surface.DescribeSceneAsync();
        failures = Check(c.Playing.Count == 0, $"the non-looping pump-cycle has ended ({string.Join(", ", c.Playing)})", failures);
        failures = Check(d.Frames == c.Frames, $"idle scene draws nothing: {d.Frames - c.Frames} frames in 2 s", failures);
        failures = Check(c.Tags.Contains("schematic"), "the pump's header carries the 'schematic' tag", failures);
        return Summarize(failures, "SCENE IDLE OK");
    }

    // ---- LSU-11: orientation from real pixels -------------------------------------------------

    private static async Task<int> RunAssertOrientationAsync()
    {
        var failures = 0;
        await using var surface = await CardSurface.CreateAsync();
        var probe = await surface.CheckOrientationAsync();
        Console.WriteLine($"          {probe.Detail}");
        failures = Check(probe.XRight, "+X renders to the right of the centre", failures);
        failures = Check(probe.YUp, "+Y renders above the centre", failures);
        failures = Check(probe.ZTurnMapsYToMinusX, "a 90 deg Z turn (C# quaternion) maps +Y to -X, as Geometry.Rotate says", failures);
        return Summarize(failures, "ORIENTATION OK");
    }

    // ---- LSU-12: no WebGL, the lesson continues in speech -------------------------------------

    private static Task<int> RunAssertSceneNoWebGlAsync() => WithoutWebGlAsync("lsu12", async surface =>
    {
        var failures = 0;
        var spoken = new List<string>();
        var report = await new LessonPlayer(surface, new PauseSpeaker(60, spoken.Add)).PlayAsync(EngineLesson());
        var scene = await surface.DescribeSceneAsync();
        failures = Check(scene.WebGl == 0, $"with --disable-webgl the page reports no WebGL (got {scene.WebGl})", failures);
        failures = Check(scene.Notice.Contains("showing a drawing instead", StringComparison.Ordinal), $"scene says it is showing a drawing instead ('{scene.Notice}'), DEC-034", failures);
        failures = Check(report.Status == PlaybackStatus.Completed && spoken.Count == 6, $"every beat acknowledged and all 6 sentences spoken ({report.Status}, {spoken.Count})", failures);
        var engine = ModelLibrary.Find(FourStrokeEngine.Id)!;
        var instance = scene.Instances.FirstOrDefault();
        failures = Check(instance is { Id: "engine" } && instance.Model == engine.Id && instance.Meshes == engine.Parts.Sum(p => p.Primitives.Count) && scene.DrawingSolids > instance.Meshes,
            $"the engine is drawn: {instance?.Meshes} primitives as {scene.DrawingSolids} solids (long parts and the ring are drawn in pieces)", failures);
        failures = Check(scene.Labels.Any(l => l.Id == "l-rod") && scene.Highlights.LastOrDefault() == "l-rod" && scene.Tags.Contains("animation needs 3D"),
            $"labels and highlights are kept, and the animation beat is tagged 'animation needs 3D' ([{string.Join(", ", scene.Tags)}])", failures);
        failures = Check(scene.DrawingBuildMs is > 0 and < 100 && scene.CspViolations == 0, $"last beat drawn in {scene.DrawingBuildMs:F1} ms (< 100), 0 CSP violations", failures);
        var card = await surface.DescribeCardAsync("card-strokes");
        failures = Check(card.ElementIds.Contains("s-exhaust"), "the 2D card beside it still drew its steps", failures);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var pick = await surface.PickAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        failures = Check(pick is null && clock.ElapsedMilliseconds < 2000, $"no click-to-ask on the drawing yet, so a pick returns null at once ({clock.ElapsedMilliseconds} ms), DEC-031/034", failures);
        return Summarize(failures, "SCENE NO-WEBGL OK");
    });

    // ---- LSU-13 / LSU-14: manual rows ---------------------------------------------------------

    private static async Task<int> RunSnapshot3dAsync(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("usage: --snapshot-3d <dir> (exit 2)");
            return 2;
        }

        var dir = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(dir);
        if (args.Contains("--no-webgl"))
        {
            return await WithoutWebGlAsync("snap", surface => SnapAllAsync(surface, dir, "-drawing"));
        }

        await using var webgl = await CardSurface.CreateAsync();
        return await SnapAllAsync(webgl, dir, string.Empty);
    }

    private static async Task<int> SnapAllAsync(CardSurface surface, string dir, string suffix)
    {
        var player = new LessonPlayer(surface, new PauseSpeaker(700));
        foreach (var model in ModelLibrary.V1)
        {
            await player.PlayAsync(Validated3d(SceneLesson(model.Id)));
            await SnapAsync(surface, Path.Combine(dir, $"{model.Id}-rest{suffix}.png"));
            await player.PlayAsync(Validated3d(SceneLesson(model.Id, """{ "op": "explode", "target": "m", "amount": 1 }""")));
            await Task.Delay(500);
            await SnapAsync(surface, Path.Combine(dir, $"{model.Id}-exploded{suffix}.png"));
        }

        // The golden schematic (DEC-050), at the end of its lesson, for a person to look at.
        await player.PlayAsync(SchematicLesson());
        await Task.Delay(500);
        await using (var file = File.Create(Path.Combine(dir, $"f35b-schematic{suffix}.png")))
        {
            await surface.CapturePageAsync("scene-jet", file);
        }

        return 0;
    }

    private static async Task SnapAsync(CardSurface surface, string path)
    {
        await using (var file = File.Create(path))
        {
            await surface.CapturePageAsync("scene", file);
        }

        Console.WriteLine($"  snapshot {path}");
    }

    private static async Task<int> RunDemo3dAsync(string[] args)
    {
        var voiceIndex = Array.IndexOf(args, "--voice");
        var voice = voiceIndex >= 0 && voiceIndex + 1 < args.Length ? args[voiceIndex + 1] : "sapi";
        ILessonSpeaker speaker = voice == "sapi" && SapiSpeaker.TryCreate(out var sapi) ? sapi! : new TimedSpeaker();
        Console.WriteLine($"voice   : {(speaker is SapiSpeaker ? "Windows SAPI" : "timed (silent)")}");
        await using var surface = await CardSurface.CreateAsync();
        var report = await new LessonPlayer(surface, speaker).PlayAsync(EngineLesson());
        Console.WriteLine($"lesson  : {report.Status}, {report.Beats.Count} beats");
        await Task.Delay(4000);
        return report.Status == PlaybackStatus.Completed ? 0 : 1;
    }
}
