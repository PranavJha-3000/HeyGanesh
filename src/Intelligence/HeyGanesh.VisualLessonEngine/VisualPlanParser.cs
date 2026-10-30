using System.Text;
using System.Text.Json;
using HeyGanesh.Abstractions;

namespace HeyGanesh.VisualLessonEngine;

/// <summary>A parse attempt: the plan when the text was a JSON object, plus everything noticed on the way.</summary>
public sealed record ParseResult(VisualPlan? Plan, IReadOnlyList<LessonIssue> Issues);

/// <summary>
/// Turns model text into a <see cref="VisualPlan"/> without ever throwing on it. Shape problems that
/// still leave a usable object (a missing string, an unknown enum value, an invented op) are kept
/// in the plan and reported, so the validator can degrade one beat rather than lose the lesson;
/// only text that is not a JSON object at all yields no plan.
/// </summary>
public static class VisualPlanParser
{
    public static ParseResult Parse(string json, LessonLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        limits ??= LessonLimits.Default;

        if (Encoding.UTF8.GetByteCount(json) > limits.MaxJsonBytes)
        {
            return Fail(LessonIssueCodes.JsonTooLarge, $"payload exceeds {limits.MaxJsonBytes} bytes");
        }

        // Models often wrap the object in prose or a ```json fence. Taking the outermost braces once
        // recovers that common case; anything stranger stays malformed rather than being "repaired".
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            return Fail(LessonIssueCodes.JsonMalformed, "no JSON object found");
        }

        try
        {
            using var doc = JsonDocument.Parse(json[start..(end + 1)], new JsonDocumentOptions { MaxDepth = limits.MaxDepth });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Fail(LessonIssueCodes.JsonMalformed, "root is not an object");
            }

            var issues = new List<LessonIssue>();
            var plan = ReadPlan(doc.RootElement, issues);
            return new ParseResult(plan, issues);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            // JsonException: bad syntax or depth. ArgumentException: the text itself is invalid UTF-16
            // (a lone surrogate echoed from screen text). InvalidOperationException: a JSON-escaped lone
            // surrogate, which only fails when the string is read. All three are malformed model output.
            return Fail(LessonIssueCodes.JsonMalformed, ex.Message);
        }
    }

    private static ParseResult Fail(string code, string message) =>
        new(null, new[] { new LessonIssue(code, null, message) });

    private static VisualPlan ReadPlan(JsonElement root, List<LessonIssue> issues)
    {
        var mode = Str(root, "mode") switch
        {
            "explain" => LessonMode.Explain,
            "socratic" => LessonMode.Socratic,
            "worked-example" => LessonMode.WorkedExample,
            var other => Invalid(LessonMode.Explain, issues, null, $"mode '{other}'"),
        };

        var surfaces = Items(root, "surfaces")
            .Select(s => new LessonSurface(
                Str(s, "id"),
                Str(s, "kind") switch
                {
                    "card" => SurfaceKind.Card,
                    "scene3d" => SurfaceKind.Scene3d,
                    var other => Invalid(SurfaceKind.Card, issues, null, $"surface kind '{other}'"),
                },
                Str(s, "title")))
            .ToList();

        var beats = Items(root, "beats")
            .Select(b =>
            {
                var beatId = Str(b, "id");
                var ops = Items(b, "ops").Select(o => ReadOp(o, beatId, issues)).ToList();
                return new LessonBeat(beatId, Str(b, "say"), ops);
            })
            .ToList();

        var schematics = Items(root, "schematics")
            .Select(s => new SchematicModel(
                Str(s, "id"),
                Str(s, "title"),
                Items(s, "parts").Select(p => new SchematicPart(
                    Str(p, "id"),
                    Str(p, "name"),
                    Items(p, "shapes").Select(x => new SchematicShape(Str(x, "shape"), Numbers(x, "size"), Numbers(x, "at"), Numbers(x, "turn"), Str(x, "finish"))).ToList())).ToList()))
            .ToList();

        return new VisualPlan(Str(root, "schema"), Str(root, "lessonId"), Str(root, "title"), mode, surfaces, beats) { Schematics = schematics };
    }

    /// <summary>
    /// A schematic's number triple, read as given: a non-number becomes NaN and a missing field an empty
    /// list, so the validator can refuse it with a reason (schematic numbers are never defaulted to 0).
    /// </summary>
    private static IReadOnlyList<double> Numbers(JsonElement e, string name) =>
        Items(e, name).Select(v => v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : double.NaN).ToList();

    private static LessonOp ReadOp(JsonElement o, string beatId, List<LessonIssue> issues)
    {
        var name = Str(o, "op");
        return name switch
        {
            "write" => new WriteOp(Str(o, "surface"), Str(o, "id"), Str(o, "text"), Style(o, beatId, issues), OptStr(o, "math")),
            "step" => new StepOp(Str(o, "surface"), Str(o, "id"), ReqInt(o, "index", beatId, issues), Str(o, "text")),
            "arrow" => new ArrowOp(Str(o, "surface"), Str(o, "from"), Str(o, "to"), OptStr(o, "label")),
            "icon" => new IconOp(Str(o, "surface"), Str(o, "id"), Str(o, "glyph")),
            "plot" => new PlotOp(Str(o, "surface"), Str(o, "id"), Str(o, "expr"), ReqDbl(o, "xMin", beatId, issues), ReqDbl(o, "xMax", beatId, issues)),
            "highlight" => new HighlightOp(Str(o, "target")),
            "mark" => new ScreenMarkOp(
                Shape(o, beatId, issues),
                new PixelRect(ReqInt(o, "x", beatId, issues), ReqInt(o, "y", beatId, issues), ReqInt(o, "width", beatId, issues), ReqInt(o, "height", beatId, issues))),
            "clear" => new ClearOp(Str(o, "surface")),
            "model" => new ModelOp(Str(o, "surface"), Str(o, "id"), Str(o, "model")),
            "label" => new LabelOp(Str(o, "surface"), Str(o, "id"), Str(o, "target"), Str(o, "text")),
            "explode" => new ExplodeOp(Str(o, "target"), ReqDbl(o, "amount", beatId, issues)),
            "animate" => new AnimateOp(Str(o, "target"), Str(o, "animation")),
            "camera" => new CameraOp(Str(o, "surface"), Str(o, "focus"), View(o, beatId, issues)),
            _ => new UnknownOp(name),
        };
    }

    private static CameraView View(JsonElement o, string beatId, List<LessonIssue> issues) => Str(o, "view") switch
    {
        "front" => CameraView.Front,
        "side" => CameraView.Side,
        "top" => CameraView.Top,
        "iso" => CameraView.Iso,
        var other => Invalid(CameraView.Iso, issues, beatId, $"camera view '{other}'"),
    };

    private static TextStyle Style(JsonElement o, string beatId, List<LessonIssue> issues) => OptStr(o, "style") switch
    {
        null or "body" => TextStyle.Body,
        "heading" => TextStyle.Heading,
        "note" => TextStyle.Note,
        var other => Invalid(TextStyle.Body, issues, beatId, $"text style '{other}'"),
    };

    private static MarkShape Shape(JsonElement o, string beatId, List<LessonIssue> issues) => Str(o, "shape") switch
    {
        "circle" => MarkShape.Circle,
        "underline" => MarkShape.Underline,
        "arrow" => MarkShape.Arrow,
        var other => Invalid(MarkShape.Circle, issues, beatId, $"mark shape '{other}'"),
    };

    private static T Invalid<T>(T fallback, List<LessonIssue> issues, string? beatId, string what)
    {
        issues.Add(new LessonIssue(LessonIssueCodes.ValueInvalid, beatId, $"unknown {what}"));
        return fallback;
    }

    // Missing or wrongly-typed fields read as empty/zero; the validator reports them with context.
    private static string Str(JsonElement e, string name) => OptStr(e, name) ?? string.Empty;

    private static string? OptStr(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    // Numbers are where a silent default does harm: a mark coerced to 0 is a circle drawn at the
    // primary monitor's corner, pointing at the wrong thing with no issue raised. So a number that is
    // missing, not a number, fractional or out of int range is reported on its beat. Integral
    // decimals (512.0) are accepted — models emit them for whole numbers.
    private static int ReqInt(JsonElement e, string name, string beatId, List<LessonIssue> issues)
    {
        if (e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number)
        {
            if (v.TryGetInt32(out var i))
            {
                return i;
            }

            if (v.TryGetDouble(out var d) && d == Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue)
            {
                return (int)d;
            }
        }

        return Invalid(0, issues, beatId, $"or missing integer '{name}'");
    }

    private static double ReqDbl(JsonElement e, string name, string beatId, List<LessonIssue> issues) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)
            ? d
            : Invalid(0d, issues, beatId, $"or missing number '{name}'");

    private static IEnumerable<JsonElement> Items(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().ToList()
            : Enumerable.Empty<JsonElement>();
}
