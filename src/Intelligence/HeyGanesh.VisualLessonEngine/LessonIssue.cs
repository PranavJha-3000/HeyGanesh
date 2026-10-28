namespace HeyGanesh.VisualLessonEngine;

/// <summary>
/// One reason a plan, or one beat of it, is not drawn as authored. <paramref name="BeatId"/> is null
/// for plan-level issues (schema, limits, surfaces, title, mode) — those cost the whole drawing.
/// </summary>
public sealed record LessonIssue(string Code, string? BeatId, string Message);

/// <summary>Stable issue codes. Rows and later eval tooling match on these, never on messages.</summary>
public static class LessonIssueCodes
{
    public const string JsonMalformed = "json.malformed";
    public const string JsonTooLarge = "json.too-large";
    public const string SchemaUnsupported = "schema.unsupported";
    public const string FieldMissing = "field.missing";
    public const string FieldTooLong = "field.too-long";
    public const string LimitSurfaces = "limit.surfaces";
    public const string LimitBeats = "limit.beats";
    public const string LimitOps = "limit.ops";
    public const string IdInvalid = "id.invalid";
    public const string IdDuplicate = "id.duplicate";
    public const string RefSurface = "ref.surface";
    public const string RefTarget = "ref.target";
    public const string MarkOutOfBounds = "mark.out-of-bounds";
    public const string OpUnknown = "op.unknown";
    public const string GlyphUnknown = "glyph.unknown";
    public const string ValueInvalid = "value.invalid";
}
