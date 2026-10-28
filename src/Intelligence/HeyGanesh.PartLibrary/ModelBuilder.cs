namespace HeyGanesh.PartLibrary;

/// <summary>Short constructors so the model files read as geometry, not as record syntax.</summary>
internal static class ModelBuilder
{
    public static Vec3 V(double x, double y, double z) => new(x, y, z);

    public static Primitive Box(double w, double h, double d, Vec3 at, Finish finish, Vec3? rot = null) =>
        new(PrimitiveShape.Box, V(w, h, d), at, rot ?? Vec3.Zero, finish);

    public static Primitive Cylinder(double diameter, double height, Vec3 at, Finish finish, Vec3? rot = null, double? bottomDiameter = null) =>
        new(PrimitiveShape.Cylinder, V(diameter, height, bottomDiameter ?? diameter), at, rot ?? Vec3.Zero, finish);

    public static Primitive Sphere(double diameter, Vec3 at, Finish finish) =>
        new(PrimitiveShape.Sphere, V(diameter, 0, 0), at, Vec3.Zero, finish);

    public static Primitive Torus(double diameter, double thickness, Vec3 at, Finish finish, Vec3? rot = null) =>
        new(PrimitiveShape.Torus, V(diameter, thickness, 0), at, rot ?? Vec3.Zero, finish);

    public static Primitive Capsule(double diameter, double height, Vec3 at, Finish finish, Vec3? rot = null) =>
        new(PrimitiveShape.Capsule, V(diameter, height, 0), at, rot ?? Vec3.Zero, finish);

    public static LibraryPart Part(string id, string name, string description, Vec3 position, Vec3 explode, params Primitive[] primitives) =>
        new(id, name, description, primitives, position, Vec3.Zero, explode);

    /// <summary>Rotation about the model's X axis that lays a cylinder's local Y axis along Z (a shaft or pin).</summary>
    public static readonly Vec3 AlongZ = V(90, 0, 0);
}
