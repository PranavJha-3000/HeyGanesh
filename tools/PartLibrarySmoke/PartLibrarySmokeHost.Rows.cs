using HeyGanesh.Abstractions;
using HeyGanesh.PartLibrary;
using HeyGanesh.PartLibrary.Models;
using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.Tools.PartLibrarySmoke;

/// <summary>
/// PL-2..PL-5. The physics rows read the SHIPPED data (tracks and primitives), not the formulas that
/// produced them, so a wrong formula or a wrong wiring of it into a track both fail here.
/// </summary>
internal static partial class PartLibrarySmokeHost
{
    private const double Eps = 1e-9;

    // ---- PL-2: structural rules -------------------------------------------------------------

    private static int RunAssertLibrary()
    {
        var failures = 0;
        foreach (var model in ModelLibrary.V1)
        {
            var issues = LibraryCheck.Check(model);
            failures = Check(issues.Count == 0, $"{model.Id}: zero LibraryCheck issues{(issues.Count == 0 ? "" : $" - {string.Join("; ", issues.Select(i => $"{i.Code} {i.Where}: {i.Message}"))}")}", failures);
            var primitives = model.Parts.Sum(p => p.Primitives.Count);
            Console.WriteLine($"          {model.Parts.Count} parts, {primitives} primitives, {model.Animations.Count} animation(s)");
        }

        failures = Check(ModelLibrary.V1.Select(m => m.Id).Distinct().Count() == ModelLibrary.V1.Count, "model ids are unique across the library", failures);
        failures = Check(ModelLibrary.V1.Sum(m => m.Parts.Sum(p => p.Primitives.Count)) <= 200,
            "all three models together stay within spike 0b's measured 200-primitive scene", failures);

        // Every rule, proven to fire: a valid one-part model, broken one way at a time.
        var tiny = Tiny();
        failures = Check(LibraryCheck.Check(tiny).Count == 0, "the tiny reference model itself is clean", failures);
        var core = tiny.Parts[0];
        var move = tiny.Animations[0];
        var sphere = core.Primitives[0];
        LibraryModel WithPart(LibraryPart p) => tiny with { Parts = new[] { p } };
        LibraryModel WithAnim(LibraryAnimation a) => tiny with { Animations = new[] { a } };
        LibraryAnimation WithKeys(params Keyframe[] keys) => move with { Tracks = new[] { move.Tracks[0] with { Keys = keys } } };
        var v = new Vec3(0, 0.5, 0);
        var broken = new (string Code, LibraryModel Model)[]
        {
            (LibraryIssueCodes.IdInvalid, tiny with { Id = "Tiny Model" }),
            (LibraryIssueCodes.IdDuplicate, tiny with { Parts = new[] { core, core } }),
            (LibraryIssueCodes.TextLength, WithPart(core with { Description = new string('x', 141) })),
            (LibraryIssueCodes.PartEmpty, WithPart(core with { Primitives = Array.Empty<Primitive>() })),
            (LibraryIssueCodes.SizeInvalid, WithPart(core with { Primitives = new[] { sphere with { Size = Vec3.Zero } } })),
            (LibraryIssueCodes.NumberInvalid, WithPart(core with { ExplodeOffset = new Vec3(double.NaN, 0, 0) })),
            (LibraryIssueCodes.BudgetParts, tiny with { Parts = Enumerable.Range(1, 17).Select(i => core with { Id = $"p{i}" }).ToList(), Animations = Array.Empty<LibraryAnimation>() }),
            (LibraryIssueCodes.BudgetPrimitives, WithPart(core with { Primitives = Enumerable.Repeat(sphere with { Size = new Vec3(0.1, 0, 0) }, 65).ToList() })),
            (LibraryIssueCodes.Bounds, WithPart(core with { Primitives = new[] { sphere with { Position = new Vec3(1.95, 0, 0) } } })),
            (LibraryIssueCodes.TrackPart, WithAnim(move with { Tracks = new[] { move.Tracks[0] with { Part = "ghost" } } })),
            (LibraryIssueCodes.KeysOrder, WithAnim(WithKeys(new(0.2, Vec3.Zero), new(1, Vec3.Zero)))),
            (LibraryIssueCodes.Duration, WithAnim(move with { DurationSeconds = 30 })),
            (LibraryIssueCodes.LoopMismatch, WithAnim(WithKeys(new(0, Vec3.Zero), new(1, v)))),
            (LibraryIssueCodes.RestMismatch, WithAnim(WithKeys(new(0, v), new(0.5, Vec3.Zero), new(1, v)))),
        };
        foreach (var (code, model) in broken)
        {
            var codes = LibraryCheck.Check(model).Select(i => i.Code).Distinct().ToList();
            failures = Check(codes.Count == 1 && codes[0] == code, $"broken model -> exactly '{code}'{(codes.Count == 1 && codes[0] == code ? "" : $" (got {string.Join(", ", codes)})")}", failures);
        }

        return Summarize(failures, "LIBRARY OK");
    }

    private static LibraryModel Tiny()
    {
        var core = new LibraryPart("core", "Core", "A test part.", new[] { new Primitive(PrimitiveShape.Sphere, new Vec3(0.5, 0, 0), Vec3.Zero, Vec3.Zero, Finish.Metal) },
            Vec3.Zero, Vec3.Zero, Vec3.Zero);
        var move = new LibraryAnimation("move", "Move", 2, Loop: true, new[]
        {
            new Track("core", TrackProperty.Position, new Keyframe[] { new(0, Vec3.Zero), new(0.5, new Vec3(0, 0.5, 0)), new(1, Vec3.Zero) }),
        });
        return new LibraryModel("tiny", "Tiny", "test", Schematic: false, "test basis", new[] { core }, new[] { move });
    }

    // ---- PL-3: physics in the shipped data -----------------------------------------------------

    private static int RunAssertPhysics()
    {
        var failures = 0;
        failures = EnginePhysics(failures);
        failures = DnaPhysics(failures);
        failures = PumpPhysics(failures);
        return Summarize(failures, "PHYSICS OK");
    }

    private static Track TrackOf(LibraryModel m, string animation, string part, TrackProperty property) =>
        m.Animations.Single(a => a.Id == animation).Tracks.Single(t => t.Part == part && t.Property == property);

    private static int EnginePhysics(int failures)
    {
        var engine = ModelLibrary.Find(FourStrokeEngine.Id)!;
        var piston = TrackOf(engine, "stroke-cycle", "piston", TrackProperty.Position).Keys;
        var rodPos = TrackOf(engine, "stroke-cycle", "connecting-rod", TrackProperty.Position).Keys;
        var rodRot = TrackOf(engine, "stroke-cycle", "connecting-rod", TrackProperty.RotationDeg).Keys;
        var crank = TrackOf(engine, "stroke-cycle", "crankshaft", TrackProperty.RotationDeg).Keys;
        var intake = TrackOf(engine, "stroke-cycle", "valve-intake", TrackProperty.Position).Keys;
        var exhaust = TrackOf(engine, "stroke-cycle", "valve-exhaust", TrackProperty.Position).Keys;
        var crankRest = engine.Part("crankshaft").Position;
        const double r = FourStrokeEngine.CrankRadius, l = FourStrokeEngine.RodLength;

        failures = Check(piston.Count == 49 && new[] { rodPos, rodRot, crank, intake, exhaust }.All(k => k.Count == 49), "engine: 49 keys per track (every 15 deg over 720 deg)", failures);
        var tdc = piston[0].Value.Y;
        var bdc = piston[12].Value.Y;
        failures = Check(Math.Abs(tdc - (FourStrokeEngine.CrankAxisY + r + l)) < Eps && Math.Abs(bdc - (FourStrokeEngine.CrankAxisY - r + l)) < Eps,
            $"engine: piston at top dead centre at 0 deg (y={tdc:F6}) and bottom dead centre at 180 deg (y={bdc:F6})", failures);
        failures = Check(Math.Abs((tdc - bdc) - (2 * r)) < Eps, $"engine: TDC - BDC = stroke {2 * r} (got {tdc - bdc:F12})", failures);
        failures = Check(piston.All(k => k.Value.Y <= tdc + Eps && k.Value.Y >= bdc - Eps), "engine: the piston never leaves the TDC..BDC range", failures);

        double worstBottom = 0, worstTop = 0;
        for (var i = 0; i < crank.Count; i++)
        {
            var crankPin = crankRest + Geometry.Rotate(new Vec3(0, r, 0), crank[i].Value);
            var bottom = rodPos[i].Value + Geometry.Rotate(new Vec3(0, -l / 2, 0), rodRot[i].Value);
            var top = rodPos[i].Value + Geometry.Rotate(new Vec3(0, l / 2, 0), rodRot[i].Value);
            worstBottom = Math.Max(worstBottom, (bottom - crankPin).Length);
            worstTop = Math.Max(worstTop, (top - piston[i].Value).Length);
        }

        failures = Check(worstBottom < 1e-9 && worstTop < 1e-9,
            $"engine: at all 49 keys the rod's big end sits on the crank pin and its small end on the piston pin (worst {Math.Max(worstBottom, worstTop):E1})", failures);
        failures = Check(Math.Abs(crank[^1].Value.Z + 720) < Eps, "engine: the crankshaft turns two full revolutions per four-stroke cycle", failures);

        static double Theta(Keyframe k) => k.T * 720;
        var intakeOk = intake.All(k => (FourStrokeEngine.ValveClosedY - k.Value.Y > Eps) == (Theta(k) is > 0 and < 180));
        var exhaustOk = exhaust.All(k => (FourStrokeEngine.ValveClosedY - k.Value.Y > Eps) == (Theta(k) is > 540 and < 720));
        failures = Check(intakeOk && exhaustOk, "engine: intake valve open only in 0-180 deg (intake stroke), exhaust only in 540-720 deg (exhaust stroke)", failures);
        var clearance = Enumerable.Range(0, piston.Count).Min(i =>
            Math.Min(intake[i].Value.Y, exhaust[i].Value.Y) - (FourStrokeEngine.ValveHeadThickness / 2) - (piston[i].Value.Y + FourStrokeEngine.CrownAbovePin));
        failures = Check(clearance > 0, $"engine: valve heads never reach the piston crown (minimum clearance {clearance:F3})", failures);
        return failures;
    }

    private static int DnaPhysics(int failures)
    {
        var dna = ModelLibrary.Find(DnaHelix.Id)!;
        var left = dna.Part("backbone-left").Primitives.Select(p => p.Position).ToList();
        var right = dna.Part("backbone-right").Primitives.Select(p => p.Position).ToList();
        static double Radial(Vec3 p) => Math.Sqrt((p.X * p.X) + (p.Z * p.Z));

        failures = Check(left.Count == 10 && right.Count == 10, "dna: 10 base pairs, one backbone sphere per strand per pair", failures);
        failures = Check(left.Concat(right).All(p => Math.Abs(Radial(p) - DnaHelix.Radius) < Eps), "dna: every backbone sphere sits on the 2.0 nm diameter (radius 1.0 from the axis)", failures);
        var rises = left.Zip(left.Skip(1), (a, b) => b.Y - a.Y).ToList();
        failures = Check(rises.All(d => Math.Abs(d - 0.34) < Eps), "dna: rise of 0.34 nm between consecutive pairs", failures);

        var angles = left.Select(p => Geometry.Deg(Math.Atan2(p.Z, p.X))).ToList();
        var twists = angles.Zip(angles.Skip(1), (a, b) => ((b - a) % 360 + 360) % 360).ToList();
        failures = Check(twists.All(t => Math.Abs(t - (360 / 10.5)) < 1e-6), $"dna: twist of {360 / 10.5:F4} deg per pair (10.5 pairs per turn)", failures);
        failures = Check(Math.Abs(twists.Sum() - (9 * 360 / 10.5)) < 1e-6, $"dna: total twist first-to-last pair = 9 x 34.29 deg = {twists.Sum():F2} deg", failures);

        // Pairing, read from the primitives: at each pair's height, one half-rung on each side of the axis.
        var halves = new[] { "adenine", "thymine", "guanine", "cytosine" }
            .SelectMany(part => dna.Part(part).Primitives.Select(p => (Part: part, Prim: p))).ToList();
        var pairingOk = true;
        var axisOk = true;
        for (var i = 0; i < DnaHelix.Sequence.Length; i++)
        {
            var u = DnaHelix.Toward(i);
            var here = halves.Where(h => Math.Abs(h.Prim.Position.Y - DnaHelix.PairY(i)) < Eps).ToList();
            var leftHalf = here.Where(h => (h.Prim.Position.X * u.X) + (h.Prim.Position.Z * u.Z) > 0).ToList();
            var rightHalf = here.Where(h => (h.Prim.Position.X * u.X) + (h.Prim.Position.Z * u.Z) < 0).ToList();
            pairingOk &= here.Count == 2 && leftHalf.Count == 1 && rightHalf.Count == 1
                && leftHalf[0].Part == DnaHelix.PartFor(DnaHelix.Sequence[i])
                && rightHalf[0].Part == DnaHelix.PartFor(DnaHelix.Complement(DnaHelix.Sequence[i]));
            foreach (var h in here)
            {
                var axis = Geometry.Rotate(new Vec3(0, 1, 0), h.Prim.RotationDeg);
                var cross = new Vec3((axis.Y * u.Z) - (axis.Z * u.Y), (axis.Z * u.X) - (axis.X * u.Z), (axis.X * u.Y) - (axis.Y * u.X));
                axisOk &= cross.Length < 1e-9;
            }
        }

        failures = Check(pairingOk, $"dna: strand 1 reads {DnaHelix.Sequence} and every pair is A-T or G-C", failures);
        failures = Check(axisOk, "dna: every half-rung lies along the line through both backbones (rotation convention checked)", failures);
        return failures;
    }

    private static int PumpPhysics(int failures)
    {
        var pump = ModelLibrary.Find(SodiumPotassiumPump.Id)!;
        var na = pump.Part("sodium-ions").Primitives;
        var k = pump.Part("potassium-ions").Primitives;
        var naEnd = TrackOf(pump, "pump-cycle", "sodium-ions", TrackProperty.Position).Keys[^1].Value;
        var kEnd = TrackOf(pump, "pump-cycle", "potassium-ions", TrackProperty.Position).Keys[^1].Value;
        var face = SodiumPotassiumPump.MembraneHalfThickness + (SodiumPotassiumPump.IonDiameter / 2);

        failures = Check(na.Count == 3 && na.All(p => p.Shape == PrimitiveShape.Sphere) && k.Count == 2 && k.All(p => p.Shape == PrimitiveShape.Sphere),
            "pump: 3 Na+ and 2 K+ per cycle", failures);
        failures = Check(pump.Schematic, "pump: marked schematic", failures);
        failures = Check(na.All(p => p.Position.Y < -face) && k.All(p => p.Position.Y > face), "pump: at rest Na+ is inside the cell and K+ outside", failures);
        failures = Check(na.All(p => p.Position.Y + naEnd.Y > face) && k.All(p => p.Position.Y + kEnd.Y < -face),
            "pump: after pump-cycle every Na+ is outside the membrane and every K+ inside", failures);
        return failures;
    }

    // ---- PL-4: catalogue round trip ----------------------------------------------------------

    private static int RunAssertCatalog()
    {
        var failures = 0;
        var catalog = ModelLibrary.ToCatalog();
        failures = Check(catalog.Models.Select(m => m.Id).OrderBy(x => x).SequenceEqual(ModelLibrary.V1.Select(m => m.Id).OrderBy(x => x)), "catalogue holds exactly the V1 model ids", failures);
        var namesMatch = ModelLibrary.V1.All(m => catalog.TryGet(m.Id, out var c)
            && c.Parts.SetEquals(m.Parts.Select(p => p.Id)) && c.Animations.SetEquals(m.Animations.Select(a => a.Id)));
        failures = Check(namesMatch, "each catalogue entry lists exactly its model's part and animation ids", failures);

        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "engine-3d.json"))
            .Replace("\"engine-test\"", $"\"{FourStrokeEngine.Id}\"", StringComparison.Ordinal);
        var context = new ValidationContext(new PixelRect(0, 0, 2560, 1440)) { Scene3d = catalog };
        var real = new VisualPlanValidator().Validate(VisualPlanParser.Parse(json), context);
        failures = Check(real.Outcome == LessonOutcome.Valid && real.Issues.Count == 0,
            $"engine-3d.json on '{FourStrokeEngine.Id}' -> Valid against the real catalogue{(real.Issues.Count == 0 ? "" : $" ({string.Join(", ", real.Issues.Select(i => i.Code))})")}", failures);
        var flux = new VisualPlanValidator().Validate(VisualPlanParser.Parse(json.Replace("engine.piston\", \"text\"", "engine.flux-capacitor\", \"text\"", StringComparison.Ordinal)), context);
        failures = Check(flux.Issues.Any(i => i.Code == LessonIssueCodes.PartUnknown && i.BeatId == "b2"), "a lesson naming engine.flux-capacitor -> part.unknown", failures);
        var stale = new VisualPlanValidator().Validate(VisualPlanParser.Parse(json.Replace($"\"{FourStrokeEngine.Id}\"", "\"engine-test\"", StringComparison.Ordinal)), context);
        failures = Check(stale.Issues.Any(i => i.Code == LessonIssueCodes.ModelUnknown), "the LessonSmoke test model 'engine-test' is not in the real catalogue -> model.unknown", failures);
        failures = Check(ModelLibrary.V1.All(m => !m.Reviewed), "no V1 model is marked Reviewed (owner review pending)", failures);
        return Summarize(failures, "CATALOG OK");
    }

    // ---- PL-5: review dump --------------------------------------------------------------------

    private static int RunDump(string which)
    {
        var models = which == "all" ? ModelLibrary.V1 : ModelLibrary.V1.Where(m => m.Id == which).ToList();
        if (models.Count == 0)
        {
            return Usage($"no model '{which}' (have: {string.Join(", ", ModelLibrary.V1.Select(m => m.Id))}, all)");
        }

        foreach (var m in models)
        {
            Console.WriteLine($"== {m.Id} - {m.Title}");
            Console.WriteLine($"   subject: {m.Subject}   schematic: {m.Schematic}   reviewed: {m.Reviewed}");
            Console.WriteLine($"   basis:   {m.Basis}");
            Console.WriteLine($"   parts ({m.Parts.Count}, {m.Parts.Sum(p => p.Primitives.Count)} primitives):");
            foreach (var p in m.Parts)
            {
                var shapes = string.Join(", ", p.Primitives.GroupBy(x => x.Shape).Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}"));
                Console.WriteLine($"     {p.Id,-16} {p.Name,-22} [{shapes}]");
                Console.WriteLine($"     {"",-16} {p.Description}");
            }

            foreach (var a in m.Animations)
            {
                Console.WriteLine($"   animation {a.Id}: \"{a.Name}\", {a.DurationSeconds} s, {(a.Loop ? "loops" : "plays once")}, tracks: {string.Join(", ", a.Tracks.Select(t => $"{t.Part}.{t.Property}"))}");
            }

            Console.WriteLine();
        }

        return 0;
    }
}
