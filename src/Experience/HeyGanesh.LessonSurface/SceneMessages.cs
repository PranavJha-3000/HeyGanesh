using System.Text.Json.Serialization;
using HeyGanesh.PartLibrary;
using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.LessonSurface;

/// <summary>
/// Turns validated scene3d ops (DEC-028) into scene-page messages, taking geometry from the Part
/// Library (DEC-029). Every rotation leaves here as a quaternion computed with the library's own
/// convention, so the page never interprets Euler angles — the one place the frame could drift.
/// Pure data, public so LessonSurfaceSmoke (LSU-8) can check the maths without a window.
/// </summary>
public static class SceneMessages
{
    /// <summary>Largest Euler step between two rotation keys sent to the page; slerp is unambiguous well below 180°.</summary>
    public const double MaxRotationStepDeg = 45;

    /// <summary>
    /// The library's rotation (Z, then X, then Y about fixed axes — <see cref="Geometry.Rotate"/>) as a
    /// unit quaternion [x, y, z, w]: q = qY · qX · qZ, Hamilton product, rotating v as q v q*.
    /// </summary>
    public static double[] ToQuaternion(Vec3 rotationDeg)
    {
        var qz = Axis(0, 0, 1, rotationDeg.Z);
        var qx = Axis(1, 0, 0, rotationDeg.X);
        var qy = Axis(0, 1, 0, rotationDeg.Y);
        return Multiply(Multiply(qy, qx), qz);
    }

    /// <summary>Rotates <paramref name="v"/> by quaternion <paramref name="q"/> (for LSU-8's equivalence check).</summary>
    public static Vec3 Rotate(double[] q, Vec3 v)
    {
        var p = new[] { v.X, v.Y, v.Z, 0d };
        var r = Multiply(Multiply(q, p), new[] { -q[0], -q[1], -q[2], q[3] });
        return new Vec3(r[0], r[1], r[2]);
    }

    public static object Model(ModelOp op)
    {
        var model = ModelLibrary.Find(op.Model)
            ?? throw new InvalidOperationException($"model '{op.Model}' passed validation but is not in the part library");
        var parts = model.Parts.Select(p => new
        {
            id = p.Id,
            name = p.Name,
            position = Arr(p.Position),
            rotation = ToQuaternion(p.RotationDeg),
            explode = Arr(p.ExplodeOffset),
            anchor = Arr(Centre(p.Primitives.Select(x => x.Position))),
            primitives = p.Primitives.Select(x => new
            {
                shape = x.Shape.ToString(),
                size = Arr(x.Size),
                position = Arr(x.Position),
                rotation = ToQuaternion(x.RotationDeg),
                finish = x.Finish.ToString(),
            }).ToArray(),
        }).ToArray();

        // Where a label on the whole instance points: the centre of the parts' anchors at rest.
        var anchor = Centre(model.Parts.Select(p => Geometry.ToModel(Centre(p.Primitives.Select(x => x.Position)), p.Position, p.RotationDeg)));
        return new
        {
            op = "model",
            id = op.Id,
            model = model.Id,
            title = model.Title,
            schematic = model.Schematic,
            reviewed = model.Reviewed,
            anchor = Arr(anchor),
            parts,
        };
    }

    public static object Animation(AnimateOp op, string modelId)
    {
        var animation = ModelLibrary.Find(modelId)?.Animations.FirstOrDefault(a => a.Id == op.Animation)
            ?? throw new InvalidOperationException($"animation '{op.Animation}' of '{modelId}' passed validation but is not in the part library");
        return new
        {
            op = "animate",
            target = op.Target,
            animation = animation.Id,
            duration = animation.DurationSeconds,
            loop = animation.Loop,
            tracks = animation.Tracks.Select(t => new
            {
                part = t.Part,
                property = t.Property == TrackProperty.Position ? "position" : "rotation",
                keys = t.Property == TrackProperty.Position
                    ? t.Keys.Select(k => new SentKey(k.T, Arr(k.Value))).ToArray()
                    : RotationKeys(t.Keys),
            }).ToArray(),
        };
    }

    /// <summary>
    /// Rotation keys as quaternions, densified so consecutive Euler values differ by at most
    /// <see cref="MaxRotationStepDeg"/> and kept in one hemisphere so slerp turns the authored way.
    /// The library interpolates Euler angles linearly; at ≤45° steps slerp follows that closely.
    /// </summary>
    public static SentKey[] RotationKeys(IReadOnlyList<Keyframe> keys)
    {
        var dense = new List<Keyframe> { keys[0] };
        for (var i = 1; i < keys.Count; i++)
        {
            var a = keys[i - 1];
            var b = keys[i];
            var step = Math.Max(Math.Abs(b.Value.X - a.Value.X), Math.Max(Math.Abs(b.Value.Y - a.Value.Y), Math.Abs(b.Value.Z - a.Value.Z)));
            var pieces = Math.Max(1, (int)Math.Ceiling(step / MaxRotationStepDeg));
            for (var s = 1; s <= pieces; s++)
            {
                var f = (double)s / pieces;
                dense.Add(new Keyframe(a.T + ((b.T - a.T) * f), a.Value + ((b.Value - a.Value) * f)));
            }
        }

        var result = new SentKey[dense.Count];
        double[]? previous = null;
        for (var i = 0; i < dense.Count; i++)
        {
            var q = ToQuaternion(dense[i].Value);
            if (previous is not null && Dot(previous, q) < 0)
            {
                q = q.Select(c => -c).ToArray();
            }

            result[i] = new SentKey(dense[i].T, q);
            previous = q;
        }

        return result;
    }

    public static object Label(LabelOp op) => new { op = "label", id = op.Id, target = op.Target, text = op.Text };

    public static object Highlight(HighlightOp op) => new { op = "highlight", target = op.Target };

    public static object Explode(ExplodeOp op) => new { op = "explode", target = op.Target, amount = op.Amount };

    public static object Camera(CameraOp op) => new { op = "camera", focus = op.Focus, view = op.View.ToString().ToLowerInvariant() };

    public static object Clear() => new { op = "clear" };

    /// <summary>The orientation self-test's one test pose: a 90° turn about Z, in the library's convention.</summary>
    public static double[] OrientationTurn => ToQuaternion(new Vec3(0, 0, 90));

    private static Vec3 Centre(IEnumerable<Vec3> points)
    {
        var list = points.ToList();
        return list.Count == 0 ? Vec3.Zero : new Vec3(list.Average(p => p.X), list.Average(p => p.Y), list.Average(p => p.Z));
    }

    private static double[] Arr(Vec3 v) => new[] { v.X, v.Y, v.Z };

    private static double[] Axis(double x, double y, double z, double degrees)
    {
        var half = Geometry.Rad(degrees) / 2;
        var s = Math.Sin(half);
        return new[] { x * s, y * s, z * s, Math.Cos(half) };
    }

    // Hamilton product of [x, y, z, w] quaternions.
    private static double[] Multiply(double[] a, double[] b) => new[]
    {
        (a[3] * b[0]) + (a[0] * b[3]) + (a[1] * b[2]) - (a[2] * b[1]),
        (a[3] * b[1]) - (a[0] * b[2]) + (a[1] * b[3]) + (a[2] * b[0]),
        (a[3] * b[2]) + (a[0] * b[1]) - (a[1] * b[0]) + (a[2] * b[3]),
        (a[3] * b[3]) - (a[0] * b[0]) - (a[1] * b[1]) - (a[2] * b[2]),
    };

    private static double Dot(double[] a, double[] b) => (a[0] * b[0]) + (a[1] * b[1]) + (a[2] * b[2]) + (a[3] * b[3]);
}

/// <summary>One animation key as the page receives it: time 0..1 and a position [x,y,z] or quaternion [x,y,z,w].</summary>
public sealed record SentKey([property: JsonPropertyName("t")] double T, [property: JsonPropertyName("v")] double[] V);
