namespace HeyGanesh.PartLibrary;

/// <summary>
/// The library's one rotation convention, used by the models to place things and by the checks to
/// verify them. Euler angles in degrees, applied Z first, then X, then Y, each about the fixed model
/// axes, right-hand rule: a Z rotation by a maps +Y to (−sin a, cos a, 0). The renderer must reproduce
/// exactly this; step 4 confirms it with a snapshot of each model.
/// </summary>
public static class Geometry
{
    public static Vec3 Rotate(Vec3 v, Vec3 rotationDeg)
    {
        var (sz, cz) = SinCos(rotationDeg.Z);
        var afterZ = new Vec3((v.X * cz) - (v.Y * sz), (v.X * sz) + (v.Y * cz), v.Z);
        var (sx, cx) = SinCos(rotationDeg.X);
        var afterX = new Vec3(afterZ.X, (afterZ.Y * cx) - (afterZ.Z * sx), (afterZ.Y * sx) + (afterZ.Z * cx));
        var (sy, cy) = SinCos(rotationDeg.Y);
        return new Vec3((afterX.X * cy) + (afterX.Z * sy), afterX.Y, (-afterX.X * sy) + (afterX.Z * cy));
    }

    /// <summary>A part-local point placed in model space by the part's pose.</summary>
    public static Vec3 ToModel(Vec3 local, Vec3 partPosition, Vec3 partRotationDeg) =>
        Rotate(local, partRotationDeg) + partPosition;

    /// <summary>Radius of a sphere that contains the primitive whatever its own rotation.</summary>
    public static double BoundingRadius(Primitive p) => p.Shape switch
    {
        PrimitiveShape.Box => new Vec3(p.Size.X, p.Size.Y, p.Size.Z).Length / 2,
        PrimitiveShape.Cylinder => new Vec3(Math.Max(p.Size.X, p.Size.Z), p.Size.Y, 0).Length / 2,
        PrimitiveShape.Sphere => p.Size.X / 2,
        PrimitiveShape.Torus => (p.Size.X + p.Size.Y) / 2,
        PrimitiveShape.Capsule => Math.Max(p.Size.X, p.Size.Y) / 2,
        _ => double.PositiveInfinity,
    };

    /// <summary>Exact half extents of an UNROTATED primitive along X, Y, Z.</summary>
    public static Vec3 HalfExtents(Primitive p) => p.Shape switch
    {
        PrimitiveShape.Box => new Vec3(p.Size.X / 2, p.Size.Y / 2, p.Size.Z / 2),
        PrimitiveShape.Cylinder => new Vec3(Math.Max(p.Size.X, p.Size.Z) / 2, p.Size.Y / 2, Math.Max(p.Size.X, p.Size.Z) / 2),
        PrimitiveShape.Sphere => new Vec3(p.Size.X / 2, p.Size.X / 2, p.Size.X / 2),
        PrimitiveShape.Torus => new Vec3((p.Size.X + p.Size.Y) / 2, p.Size.Y / 2, (p.Size.X + p.Size.Y) / 2),
        PrimitiveShape.Capsule => new Vec3(p.Size.X / 2, p.Size.Y / 2, p.Size.X / 2),
        _ => new Vec3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity),
    };

    public static double Deg(double radians) => radians * 180 / Math.PI;

    public static double Rad(double degrees) => degrees * Math.PI / 180;

    private static (double Sin, double Cos) SinCos(double degrees)
    {
        var r = Rad(degrees);
        return (Math.Sin(r), Math.Cos(r));
    }
}
