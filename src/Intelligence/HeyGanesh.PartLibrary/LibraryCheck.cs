using System.Text.RegularExpressions;

namespace HeyGanesh.PartLibrary;

/// <summary>One reason a model breaks the library rules. <paramref name="Where"/> names the part/animation, or the model.</summary>
public sealed record LibraryIssue(string Code, string Model, string Where, string Message);

/// <summary>Stable codes; rows match on these, never on messages.</summary>
public static class LibraryIssueCodes
{
    public const string IdInvalid = "id.invalid";
    public const string IdDuplicate = "id.duplicate";
    public const string TextLength = "text.length";
    public const string PartEmpty = "part.empty";
    public const string SizeInvalid = "size.invalid";
    public const string NumberInvalid = "number.invalid";
    public const string BudgetParts = "budget.parts";
    public const string BudgetPrimitives = "budget.primitives";
    public const string Bounds = "bounds";
    public const string TrackPart = "track.part";
    public const string KeysOrder = "keys.order";
    public const string Duration = "duration";
    public const string LoopMismatch = "loop.mismatch";
    public const string RestMismatch = "rest.mismatch";
}

/// <summary>
/// The structural rules every library model must meet (DEC-029). Deterministic, never throws on a
/// model: it reports. The budgets come from spike 0b — 200 primitives was the measured scene, and a
/// scene holds at most three models (LessonLimits.MaxModelsPerScene), so 64 per model.
/// </summary>
public static class LibraryCheck
{
    public const int MaxParts = 16;
    public const int MaxPrimitivesPerModel = 64;
    public const double HalfExtent = 2.0;
    public const int MaxNameChars = 40;
    public const int MaxDescriptionChars = 140;
    public const int MaxTitleChars = 80;

    private const double Tolerance = 1e-9;
    private static readonly Regex IdPattern = new(@"^[a-z][a-z0-9-]{0,31}\z", RegexOptions.CultureInvariant);

    public static IReadOnlyList<LibraryIssue> Check(LibraryModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var issues = new List<LibraryIssue>();
        void Add(string code, string where, string message) => issues.Add(new LibraryIssue(code, model.Id, where, message));

        CheckId(model.Id, model.Id, new HashSet<string>(), Add);
        CheckText(model.Title, MaxTitleChars, model.Id, "title", Add);
        CheckText(model.Basis, 400, model.Id, "basis", Add);
        if (model.Parts.Count == 0 || model.Parts.Count > MaxParts)
        {
            Add(LibraryIssueCodes.BudgetParts, model.Id, $"{model.Parts.Count} parts (1..{MaxParts})");
        }

        var primitives = model.Parts.Sum(p => p.Primitives.Count);
        if (primitives > MaxPrimitivesPerModel)
        {
            Add(LibraryIssueCodes.BudgetPrimitives, model.Id, $"{primitives} primitives (max {MaxPrimitivesPerModel})");
        }

        var partIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in model.Parts)
        {
            CheckId(part.Id, part.Id, partIds, Add);
            CheckText(part.Name, MaxNameChars, part.Id, "name", Add);
            CheckText(part.Description, MaxDescriptionChars, part.Id, "description", Add);
            if (part.Primitives.Count == 0)
            {
                Add(LibraryIssueCodes.PartEmpty, part.Id, "part has no primitives");
            }

            CheckFinite(part.Position, part.Id, "position", Add);
            CheckFinite(part.RotationDeg, part.Id, "rotation", Add);
            CheckFinite(part.ExplodeOffset, part.Id, "explode offset", Add);
            foreach (var p in part.Primitives)
            {
                CheckPrimitive(p, part, Add);
            }
        }

        var animationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var animation in model.Animations)
        {
            CheckAnimation(model, animation, animationIds, Add);
        }

        return issues;
    }

    private static void CheckPrimitive(Primitive p, LibraryPart part, Action<string, string, string> add)
    {
        CheckFinite(p.Position, part.Id, "primitive position", add);
        CheckFinite(p.RotationDeg, part.Id, "primitive rotation", add);
        var used = p.Shape switch
        {
            PrimitiveShape.Box => new[] { p.Size.X, p.Size.Y, p.Size.Z },
            PrimitiveShape.Cylinder => new[] { p.Size.X, p.Size.Y, p.Size.Z },
            PrimitiveShape.Sphere => new[] { p.Size.X },
            PrimitiveShape.Torus => new[] { p.Size.X, p.Size.Y },
            PrimitiveShape.Capsule => new[] { p.Size.X, p.Size.Y },
            _ => new[] { double.NaN },
        };
        if (used.Any(s => !double.IsFinite(s) || s <= 0))
        {
            add(LibraryIssueCodes.SizeInvalid, part.Id, $"{p.Shape} size {p.Size}");
            return;
        }

        // Rest pose only. Unrotated (primitive and part): exact per-axis extents. Rotated: a bounding
        // sphere, which is conservative — it can only over-report, never miss a shape outside the box.
        var center = Geometry.ToModel(p.Position, part.Position, part.RotationDeg);
        var unrotated = p.RotationDeg == Vec3.Zero && part.RotationDeg == Vec3.Zero;
        var r = Geometry.BoundingRadius(p);
        var half = unrotated ? Geometry.HalfExtents(p) : new Vec3(r, r, r);
        var outside = Math.Abs(center.X) + half.X > HalfExtent + Tolerance
            || Math.Abs(center.Y) + half.Y > HalfExtent + Tolerance
            || Math.Abs(center.Z) + half.Z > HalfExtent + Tolerance;
        if (outside)
        {
            add(LibraryIssueCodes.Bounds, part.Id, $"{p.Shape} at {center} (half extents {half}) leaves the ±{HalfExtent} box");
        }
    }

    private static void CheckAnimation(LibraryModel model, LibraryAnimation animation, HashSet<string> seen, Action<string, string, string> add)
    {
        CheckId(animation.Id, animation.Id, seen, add);
        CheckText(animation.Name, MaxNameChars, animation.Id, "name", add);
        if (!(double.IsFinite(animation.DurationSeconds) && animation.DurationSeconds >= 0.5 && animation.DurationSeconds <= 20))
        {
            add(LibraryIssueCodes.Duration, animation.Id, $"duration {animation.DurationSeconds} s (0.5..20)");
        }

        foreach (var track in animation.Tracks)
        {
            var part = model.Parts.FirstOrDefault(p => p.Id == track.Part);
            if (part is null)
            {
                add(LibraryIssueCodes.TrackPart, animation.Id, $"track names part '{track.Part}', which the model does not have");
                continue;
            }

            var keys = track.Keys;
            var ordered = keys.Count >= 2 && keys[0].T == 0 && keys[^1].T == 1
                && keys.Zip(keys.Skip(1)).All(k => k.Second.T > k.First.T);
            if (!ordered)
            {
                add(LibraryIssueCodes.KeysOrder, animation.Id, $"'{track.Part}' {track.Property} keys must run 0 -> 1, strictly ascending");
                continue;
            }

            foreach (var key in keys)
            {
                CheckFinite(key.Value, animation.Id, $"'{track.Part}' key", add);
            }

            var rest = track.Property == TrackProperty.Position ? part.Position : part.RotationDeg;
            if (!Same(keys[0].Value, rest, track.Property))
            {
                add(LibraryIssueCodes.RestMismatch, animation.Id, $"'{track.Part}' {track.Property} starts at {keys[0].Value}, rest pose is {rest}");
            }

            if (animation.Loop && !Same(keys[0].Value, keys[^1].Value, track.Property))
            {
                add(LibraryIssueCodes.LoopMismatch, animation.Id, $"'{track.Part}' {track.Property} ends at {keys[^1].Value}, starts at {keys[0].Value}");
            }
        }
    }

    /// <summary>Positions compare exactly (within tolerance); rotations compare modulo 360°.</summary>
    private static bool Same(Vec3 a, Vec3 b, TrackProperty property)
    {
        if (property == TrackProperty.Position)
        {
            return (a - b).Length <= Tolerance;
        }

        static bool Angle(double x, double y)
        {
            var d = Math.Abs((x - y) % 360);
            return d <= 1e-6 || Math.Abs(d - 360) <= 1e-6;
        }

        return Angle(a.X, b.X) && Angle(a.Y, b.Y) && Angle(a.Z, b.Z);
    }

    private static void CheckId(string id, string where, HashSet<string> seen, Action<string, string, string> add)
    {
        if (id is null || !IdPattern.IsMatch(id))
        {
            add(LibraryIssueCodes.IdInvalid, where, $"id '{id}'");
        }
        else if (!seen.Add(id))
        {
            add(LibraryIssueCodes.IdDuplicate, where, $"id '{id}' used twice");
        }
    }

    private static void CheckText(string value, int max, string where, string what, Action<string, string, string> add)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max)
        {
            add(LibraryIssueCodes.TextLength, where, $"{what} is {value?.Length ?? 0} chars (1..{max})");
        }
    }

    private static void CheckFinite(Vec3 v, string where, string what, Action<string, string, string> add)
    {
        if (!(double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z)))
        {
            add(LibraryIssueCodes.NumberInvalid, where, $"{what} {v}");
        }
    }
}
