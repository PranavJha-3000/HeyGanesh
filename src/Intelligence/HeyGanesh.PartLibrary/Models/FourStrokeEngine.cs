using static HeyGanesh.PartLibrary.ModelBuilder;

namespace HeyGanesh.PartLibrary.Models;

/// <summary>
/// A single-cylinder four-stroke engine. The moving parts follow slider-crank kinematics computed
/// here — piston height y(θ) = r·cos θ + √(L² − r²·sin² θ) above the crank axis, connecting-rod angle
/// from the crank pin to the piston pin — so the rod's length is exactly L at every key
/// (PartLibrarySmoke --assert-physics checks it). Crank axis along Z, piston travel along Y.
/// </summary>
public static class FourStrokeEngine
{
    public const string Id = "four-stroke-engine";

    /// <summary>Crank radius: half the stroke.</summary>
    public const double CrankRadius = 0.4;

    /// <summary>Connecting-rod length, centre to centre. L/r = 3.5, a typical ratio.</summary>
    public const double RodLength = 1.4;

    public const double CrankAxisY = -1.0;

    /// <summary>Piston crown above the piston-pin centre.</summary>
    public const double CrownAbovePin = 0.4;

    /// <summary>Valve-head centre when closed (flush with the head), and the peak lift.</summary>
    public const double ValveClosedY = 1.28;
    public const double ValveLift = 0.12;
    public const double ValveHeadThickness = 0.04;

    /// <summary>Crank degrees between keys; 720° (two turns, four strokes) gives 49 keys.</summary>
    public const int StepDeg = 15;

    public static LibraryModel Build()
    {
        var thetas = Enumerable.Range(0, (720 / StepDeg) + 1).Select(i => (double)(i * StepDeg)).ToArray();
        Keyframe Key(double theta, Vec3 value) => new(theta / 720, value);

        var crank = new Track("crankshaft", TrackProperty.RotationDeg, thetas.Select(t => Key(t, V(0, 0, -t))).ToList());
        var counter = crank with { Part = "counterweight" };
        var piston = new Track("piston", TrackProperty.Position, thetas.Select(t => Key(t, PistonPin(t))).ToList());
        var pin = piston with { Part = "piston-pin" };
        var rodPos = new Track("connecting-rod", TrackProperty.Position, thetas.Select(t => Key(t, (CrankPin(t) + PistonPin(t)) * 0.5)).ToList());
        var rodRot = new Track("connecting-rod", TrackProperty.RotationDeg, thetas.Select(t => Key(t, V(0, 0, RodAngleDeg(t)))).ToList());
        var intake = new Track("valve-intake", TrackProperty.Position, thetas.Select(t => Key(t, V(-0.22, ValveClosedY - IntakeLift(t), 0))).ToList());
        var exhaust = new Track("valve-exhaust", TrackProperty.Position, thetas.Select(t => Key(t, V(0.22, ValveClosedY - ExhaustLift(t), 0))).ToList());

        var parts = new List<LibraryPart>
        {
            Part("cylinder", "Cylinder", "The bore the piston slides in; drawn as clear glass so the moving parts stay visible.",
                Vec3.Zero, V(0, 0, -1.2), Cylinder(0.86, 1.5, V(0, 0.55, 0), Finish.Glass)),
            Part("cylinder-head", "Cylinder head", "Closes the top of the cylinder and holds the valves and the spark plug.",
                Vec3.Zero, V(0, 0.6, 0), Box(1.0, 0.25, 1.0, V(0, 1.425, 0), Finish.DarkMetal)),
            Part("spark-plug", "Spark plug", "Makes the spark that ignites the compressed fuel-air mixture at the start of the power stroke.",
                Vec3.Zero, V(0, 0.9, 0), Cylinder(0.12, 0.3, V(0, 1.65, 0), Finish.Brass)),
            Part("valve-intake", "Intake valve", "Opens during the intake stroke to let the fuel-air mixture in; closed for the other three strokes.",
                V(-0.22, ValveClosedY, 0), V(-0.3, 0.6, 0),
                Cylinder(0.22, ValveHeadThickness, Vec3.Zero, Finish.Metal), Cylinder(0.05, 0.5, V(0, 0.27, 0), Finish.Metal)),
            Part("valve-exhaust", "Exhaust valve", "Opens during the exhaust stroke to let the burnt gas out; closed for the other three strokes.",
                V(0.22, ValveClosedY, 0), V(0.3, 0.6, 0),
                Cylinder(0.22, ValveHeadThickness, Vec3.Zero, Finish.Metal), Cylinder(0.05, 0.5, V(0, 0.27, 0), Finish.Metal)),
            Part("piston", "Piston", "Slides up and down in the cylinder; the burning mixture pushes it down in the power stroke.",
                PistonPin(0), V(0, 0.3, 0),
                Cylinder(0.78, 0.6, V(0, 0.1, 0), Finish.Metal), Torus(0.78, 0.03, V(0, 0.3, 0), Finish.DarkMetal)),
            Part("piston-pin", "Piston pin", "Joins the piston to the small end of the connecting rod, letting the rod swing as the piston moves.",
                PistonPin(0), V(0, 0.3, 0.4), Cylinder(0.12, 0.5, Vec3.Zero, Finish.DarkMetal, AlongZ)),
            new("connecting-rod", "Connecting rod", "Links the piston to the crankshaft, turning the piston's up-and-down motion into rotation.",
                new[]
                {
                    Box(0.12, RodLength, 0.08, Vec3.Zero, Finish.Metal),
                    Cylinder(0.2, 0.1, V(0, RodLength / 2, 0), Finish.Metal, AlongZ),
                    Cylinder(0.24, 0.1, V(0, -RodLength / 2, 0), Finish.Metal, AlongZ),
                },
                (CrankPin(0) + PistonPin(0)) * 0.5, V(0, 0, RodAngleDeg(0)), V(0.6, 0, 0)),
            Part("crankshaft", "Crankshaft", "Turns once for every two strokes; the offset crank pin is what the connecting rod pushes on.",
                V(0, CrankAxisY, 0), V(0, -0.4, 0),
                Cylinder(0.18, 1.0, Vec3.Zero, Finish.DarkMetal, AlongZ),
                Box(0.25, 0.55, 0.08, V(0, 0.2, 0.12), Finish.DarkMetal),
                Cylinder(0.14, 0.2, V(0, CrankRadius, 0), Finish.Metal, AlongZ)),
            Part("counterweight", "Counterweight", "Mass opposite the crank pin that balances the crankshaft so it spins smoothly.",
                V(0, CrankAxisY, 0), V(0, -0.6, 0.3), Box(0.6, 0.3, 0.08, V(0, -0.3, 0.12), Finish.DarkMetal)),
        };

        var cycle = new LibraryAnimation("stroke-cycle", "Four-stroke cycle", 4.0, Loop: true,
            new[] { crank, counter, piston, pin, rodPos, rodRot, intake, exhaust });

        return new LibraryModel(
            Id,
            "Single-cylinder four-stroke engine",
            "mechanical engineering",
            Schematic: false,
            "Slider-crank kinematics computed in C#: crank radius 0.4 (stroke 0.8), rod 1.4, crank axis at y = -1. "
                + "Valve lift is a simplified sine profile with no valve overlap and no timing advance.",
            parts,
            new[] { cycle });
    }

    /// <summary>Crank-pin centre at crank angle θ (0° = top dead centre).</summary>
    public static Vec3 CrankPin(double thetaDeg) =>
        V(0, CrankAxisY, 0) + Geometry.Rotate(V(0, CrankRadius, 0), V(0, 0, -thetaDeg));

    /// <summary>Piston-pin centre: on the cylinder axis, exactly one rod length from the crank pin.</summary>
    public static Vec3 PistonPin(double thetaDeg)
    {
        var s = Math.Sin(Geometry.Rad(thetaDeg));
        var c = Math.Cos(Geometry.Rad(thetaDeg));
        return V(0, CrankAxisY + (CrankRadius * c) + Math.Sqrt((RodLength * RodLength) - (CrankRadius * CrankRadius * s * s)), 0);
    }

    /// <summary>Z rotation that lays the rod's local +Y along crank pin → piston pin.</summary>
    public static double RodAngleDeg(double thetaDeg)
    {
        var d = PistonPin(thetaDeg) - CrankPin(thetaDeg);
        return Geometry.Deg(Math.Atan2(-d.X, d.Y));
    }

    /// <summary>Intake opens through the intake stroke (0–180° of crank), sine-shaped.</summary>
    public static double IntakeLift(double thetaDeg) =>
        thetaDeg is > 0 and < 180 ? ValveLift * Math.Sin(Geometry.Rad(thetaDeg)) : 0;

    /// <summary>Exhaust opens through the exhaust stroke (540–720°).</summary>
    public static double ExhaustLift(double thetaDeg) =>
        thetaDeg is > 540 and < 720 ? ValveLift * Math.Sin(Geometry.Rad(thetaDeg - 540)) : 0;
}
